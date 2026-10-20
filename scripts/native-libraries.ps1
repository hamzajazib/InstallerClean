# native-libraries.ps1: Process Monitor, and the functions that record a build with it
# and judge which DLLs the build looked for and loaded: the native-libraries job in
# ci.yml, and the update self-update.yml performs.
#
# A workflow writes native.ps1, which defines $builds and then dot-sources this file, and
# each step that records or judges a build dot-sources native.ps1. $builds holds each
# build's label, its exe, the source file holding its lists, its arguments, how long it
# is given, what it must load and the kind of its first act seen. The functions are
# given the builds and runs they judge, and a run carries its build. $procmon is read
# from $env:PROCMON, which the step that runs Get-Procmon sets for the steps after it.
# Source files are read from the repository this file is in, whatever the working
# directory.

$repository = Split-Path $PSScriptRoot

$procmon = $env:PROCMON
$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) { $failures.Add($message); Write-Host "::error::$message" }

# The apphost reads these before the runtime starts. A planted one it takes can
# stop the build starting at all, so they are planted in a run of their own.
$plantedAlone = 'hostfxr.dll', 'hostpolicy.dll'

# A name looked for that no file on the runner carries is planted as this
# library under that name, so the look is judged like any other.
$standIn = Join-Path $env:SystemRoot 'System32\msimg32.dll'

# Each Process Monitor call that is waited for is bounded, so one that hangs fails the
# step with a message. A record starts only once no earlier Process Monitor is left,
# since one started while another is ending can hand its arguments over to it instead
# of recording.
function Invoke-Procmon([string[]]$arguments, [int]$seconds, [string]$what) {
  $p = Start-Process $procmon -ArgumentList $arguments -PassThru
  if (-not $p.WaitForExit($seconds * 1000)) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    throw "Process Monitor did not finish $what within $seconds s."
  }
}

function Wait-ProcmonGone([string]$what) {
  $deadline = [DateTime]::UtcNow.AddSeconds(60)
  while (Get-Process -Name Procmon64 -ErrorAction SilentlyContinue) {
    if ([DateTime]::UtcNow -gt $deadline) { throw "Process Monitor was still running 60 s after $what." }
    Start-Sleep -Milliseconds 500
  }
}

function Start-Record([string]$name) {
  if (Get-Process -Name Procmon64 -ErrorAction SilentlyContinue) { Invoke-Procmon @('/Terminate') 60 'ending a record left running' }
  Wait-ProcmonGone 'the earlier record ended'
  $pml = Join-Path (Split-Path $procmon) "$name.pml"
  Start-Process $procmon -ArgumentList '/AcceptEula', '/Quiet', '/Minimized', '/BackingFile', "`"$pml`""
  Invoke-Procmon @('/AcceptEula', '/WaitForIdle') 120 "starting the $name record"
}

function Stop-Record([string]$name) {
  $dir = Split-Path $procmon
  Invoke-Procmon @('/Terminate') 60 "ending the $name record"
  Wait-ProcmonGone "the $name record was ended"
  $csv = Join-Path $dir "$name.csv"
  Invoke-Procmon @('/AcceptEula', '/Quiet', '/OpenLog', "`"$dir\$name.pml`"", '/SaveAs', "`"$csv`"") 300 "saving the $name record"
  Wait-ProcmonGone "the $name record was saved"
  if (-not (Test-Path $csv)) { throw "Process Monitor saved no $name record." }
  $csv
}

# One build at a time, each ended and waited for before the next starts, since
# the windows share the single-instance mutex. The command line scans and ends;
# a window is given twenty seconds and then ended.
function Invoke-Build($build, [string]$label, [string]$folder) {
  $start = @{ FilePath = (Join-Path $folder (Split-Path $build.Exe -Leaf)); WorkingDirectory = $folder; PassThru = $true }
  if ($build.Arguments.Count) { $start.ArgumentList = $build.Arguments }
  $p = Start-Process @start
  # Read now, so the exit code is kept once the process ends.
  $null = $p.Handle
  if (-not $p.WaitForExit($build.Wait * 1000)) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; $p.WaitForExit() }
  Write-Host "  ${label}: process $($p.Id), ended with $($p.ExitCode)"
  $p.Id
}

# Each run's rows, by process. Process Monitor writes the record in event order,
# so a row's place among its process's rows is its place in that sequence. Rows
# are taken by process ID as well as name, since other processes on the runner
# can carry the same name.
function Read-Record([string]$csv, [object[]]$runs) {
  $exes = @{}
  foreach ($run in $runs) {
    if ($exes.ContainsKey([string]$run.Id)) { Fail "Two runs had process $($run.Id) in one record, so neither is judged." }
    $exes[[string]$run.Id] = $run.Exe
  }
  $shared = @($runs | Group-Object { [string]$_.Id } | Where-Object Count -gt 1 | ForEach-Object Name)
  $byProcess = @{}
  Import-Csv $csv | ForEach-Object {
    $exe = $exes[$_.PID]
    if ($exe -and $_.'Process Name' -eq $exe -and $_.PID -notin $shared) {
      if (-not $byProcess.ContainsKey($_.PID)) { $byProcess[$_.PID] = [System.Collections.Generic.List[object]]::new() }
      $byProcess[$_.PID].Add($_)
    }
  }
  Write-Host "Process Monitor recorded $([int]($byProcess.Values | Measure-Object -Property Count -Sum).Sum) operations by the builds."
  $byProcess
}

# Every .dll row in a run's own folder or under %TEMP%\.net, where a single-file
# build extracts its own libraries, and every library it loaded from anywhere.
function Get-Libraries($rows, [string]$folder) {
  $own = $folder.TrimEnd('\') + '\'
  for ($i = 0; $i -lt $rows.Count; $i++) {
    $row = $rows[$i]
    if (-not $row.Path.EndsWith('.dll', 'OrdinalIgnoreCase')) { continue }
    $place = if ($row.Path.StartsWith($own, 'OrdinalIgnoreCase')) { 'own' } elseif ($row.Path -match '\\\.net\\') { 'extraction' } else { 'elsewhere' }
    if ($place -eq 'elsewhere' -and $row.Operation -ne 'Load Image') { continue }
    [pscustomobject]@{
      Index = $i + 1; Place = $place; Operation = $row.Operation; Result = $row.Result
      Relative = $(if ($place -eq 'own') { $row.Path.Substring($own.Length) })
      Path = $row.Path; Name = (Split-Path $row.Path -Leaf).ToLowerInvariant(); Time = $row.'Time of Day'
    }
  }
}

# The first act of the build's own code that Process Monitor can see, and the first it
# can see of the code that runs in Main or later, each with its kind. A row after the
# first came after the build's own code started. In the slim, the first is the module
# initializer's load of the first assembly on the exe's list from the installed
# runtime's folder, the row on which Process Monitor shows the image loaded. A
# self-contained single-file build reads every assembly from its own bundle, so nothing
# names its module initializer's loads, and its first act seen is in Main. Main and
# later make the others: the look for the exe's old or new copy beside it, the app's
# folder under %LOCALAPPDATA% or the NoFaff one earlier versions use, a file under
# C:\Windows\Installer, and the load of msi.dll. Each build names the kind it shows
# first, and the first pass fails where it shows another.
function Get-Marker($rows, $run) {
  $first = @(Read-SourceList $run.Build.Source 'ImportingAssemblies')[0]
  $exe = $run.Folder.TrimEnd('\') + '\' + $run.Exe
  $initializer = $null
  for ($i = 0; $i -lt $rows.Count; $i++) {
    $row = $rows[$i]
    $kind = if ($row.Path -eq "$exe.old" -or $row.Path -eq "$exe.new") { 'old copy' }
      elseif ($row.Path -match '\\AppData\\Local\\((NoFaff\\)?InstallerClean(\\|$)|NoFaff$)') { 'app folder' }
      elseif ($row.Path -match '^C:\\Windows\\Installer(\\|$)') { 'Installer folder' }
      elseif ($row.Operation -eq 'Load Image' -and (Split-Path $row.Path -Leaf) -eq 'msi.dll') { 'msi.dll' }
      elseif (-not $initializer -and $row.Operation -eq 'Load Image' -and $row.Path -match '\\dotnet\\shared\\' -and (Split-Path $row.Path -Leaf) -eq "$first.dll") { 'module initializer' }
    if (-not $kind) { continue }
    $act = [pscustomobject]@{ Index = $i + 1; Kind = $kind; What = "$($row.Operation) $($row.Path)" }
    if ($kind -eq 'module initializer') { $initializer = $act; continue }
    return [pscustomobject]@{ First = $(if ($initializer) { $initializer } else { $act }); Main = $act }
  }
  if ($initializer) { [pscustomobject]@{ First = $initializer; Main = $null } }
}

function Get-When($library, $marker) {
  if ($marker -and $library.Index -gt $marker.Index) { 'after its own code began' } else { 'before any act of its own code seen' }
}

function Show-Libraries($libraries, $marker, $mainMarker, [string]$firstTime) {
  Write-Host "  first row at $firstTime"
  if ($marker) { Write-Host "  first act of its own code seen: row $($marker.Index), $($marker.Kind), $($marker.What)" }
  else { Write-Host "  no act of its own code seen" }
  if ($mainMarker -and $mainMarker.Index -ne $marker.Index) { Write-Host "  first act seen of its code in Main or later: row $($mainMarker.Index), $($mainMarker.Kind), $($mainMarker.What)" }
  Write-Host "  every look in its own folder and under %TEMP%\.net (row, time, operation, result, times seen, path):"
  $libraries | Where-Object { $_.Place -ne 'elsewhere' -and $_.Operation -ne 'Load Image' } |
    Group-Object Path, Operation, Result | Sort-Object { $_.Group[0].Index } | ForEach-Object {
      $first = $_.Group[0]
      Write-Host "    row $($first.Index)  $($first.Time)  $($first.Operation)  $($first.Result)  x$($_.Count)  $($first.Path)  ($(Get-When $first $marker))"
    }
  Write-Host "  libraries loaded:"
  $libraries | Where-Object Operation -eq 'Load Image' | ForEach-Object Path | Sort-Object -Unique | ForEach-Object { Write-Host "    $_" }
}

# The same as Show-Libraries for a program that is not one of the builds: its build
# names no source file, so nothing places a look against the start of its own code.
function Show-Looks($libraries, [string]$firstTime) {
  Write-Host "  first row at $firstTime"
  Write-Host "  every look in its own folder and under %TEMP%\.net (row, time, operation, result, times seen, path):"
  $libraries | Where-Object { $_.Place -ne 'elsewhere' -and $_.Operation -ne 'Load Image' } |
    Group-Object Path, Operation, Result | Sort-Object { $_.Group[0].Index } | ForEach-Object {
      $first = $_.Group[0]
      Write-Host "    row $($first.Index)  $($first.Time)  $($first.Operation)  $($first.Result)  x$($_.Count)  $($first.Path)"
    }
  Write-Host "  libraries loaded:"
  $libraries | Where-Object Operation -eq 'Load Image' | ForEach-Object Path | Sort-Object -Unique | ForEach-Object { Write-Host "    $_" }
}

# The genuine file for a name: one the build loaded under that name from
# anywhere but its own folder, else System32's, else the stand-in.
function Get-Plant([string]$name, $loads) {
  $loaded = @($loads | Where-Object { $_.Name -eq $name -and $_.Place -ne 'own' })
  $system = Join-Path $env:SystemRoot "System32\$name"
  if ($loaded) { [pscustomobject]@{ Source = $loaded[0].Path; Kind = 'the copy it loaded' } }
  elseif (Test-Path -LiteralPath $system) { [pscustomobject]@{ Source = $system; Kind = "System32's copy" } }
  else { [pscustomobject]@{ Source = $standIn; Kind = 'a stand-in, since no file on the runner carries this name' } }
}

# Prints a run's rows and returns its libraries, its loads and its marker, or
# nothing where Process Monitor recorded nothing for it.
function Read-Run($run, $byProcess) {
  Write-Host "== $($run.Label), $($run.Exe), process $($run.Id)"
  $rows = $byProcess[[string]$run.Id]
  if (-not $rows) { Fail "$($run.Label): Process Monitor recorded nothing for process $($run.Id)."; return }
  Write-Host "  $($rows.Count) operations"
  $libraries = @(Get-Libraries $rows $run.Folder)
  if ($run.Build.Source) {
    $markers = Get-Marker $rows $run
    Show-Libraries $libraries $markers.First $markers.Main $rows[0].'Time of Day'
  }
  else {
    $markers = $null
    Show-Looks $libraries $rows[0].'Time of Day'
  }
  [pscustomobject]@{ Libraries = $libraries; Loads = @($libraries | Where-Object Operation -eq 'Load Image'); Marker = $markers.First; MainMarker = $markers.Main }
}

function Test-MustLoad($run, $loads) {
  if (-not $run.Build.MustLoad) { Fail "$($run.Label) names nothing it must load."; return }
  foreach ($name in $run.Build.MustLoad) {
    if (-not ($loads | Where-Object Name -eq $name)) {
      Fail "$($run.Label) never loaded $name, so it did not get far enough for this check to judge it."
    }
  }
}

# A run that checks for updates reaches github.com, which the update check alone
# contacts, and finds no newer release while the build's version is at or above the
# latest. Process Monitor names the far end of a connection by its host name where
# it can, and by its address otherwise. Every /s run writes one summary entry to
# the Application log, or a 3005 or 3006 where an update ended the run, so a run
# with no entry there, or a log that cannot be read, fails rather than reading as
# one that found nothing newer. Only the entries written while the run was going are
# judged, so a run of the same exe before or after it cannot answer for it.
# Get-WinEvent takes its StartTime and EndTime to the whole second, so it is asked
# from the run's start, which only widens the read, and the entries are then kept by
# the time each was written.
function Test-UpdateCheck($run, $byProcess) {
  $github = @([System.Net.Dns]::GetHostAddresses('github.com') | ForEach-Object IPAddressToString)
  $answers = @($byProcess[[string]$run.Id] | Where-Object {
    $_.Operation -eq 'TCP Receive' -and $_.Path -match '-> (.+):(https|443)$' -and
    ($Matches[1] -match '(^|\.)github\.com$' -or $Matches[1] -in $github)
  })
  if ($answers) { "  $($run.Label): the update check had an answer from $(($answers[0].Path -split ' -> ')[-1]) at $($answers[0].'Time of Day')" }
  else { Fail "$($run.Label): the update check got no answer from GitHub, so the run's network libraries were not judged." }
  try {
    $logged = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'InstallerClean'; StartTime = $run.Started } -ErrorAction SilentlyContinue -ErrorVariable readErrors)
  }
  catch {
    Fail "$($run.Label): the Application log could not be read, so whether it updated itself was not judged: $($_.Exception.Message)"
    return
  }
  $entries = @($logged | Where-Object { $_.TimeCreated -ge $run.Started -and $_.TimeCreated -le $run.Ended })
  # No entry matching, and a source that was never registered or writes to another
  # log, each come back as an error that means the run wrote nothing here.
  $noEntry = 'NoMatchingEventsFound', 'NoMatchingProvidersFound', 'LogsAndProvidersDontOverlap'
  $unread = @($readErrors | Where-Object { ($_.FullyQualifiedErrorId -split ',')[0] -notin $noEntry })
  foreach ($readError in $unread) { Fail "$($run.Label): the Application log could not be read in full, so whether it updated itself may not have been judged: $($readError.Exception.Message)" }
  if (-not $entries -and -not $unread) { Fail "$($run.Label) wrote no entry to the Application log, so whether it updated itself was not judged." }
  foreach ($entry in $entries) { "  $($run.Label): Event ID $($entry.Id) at $($entry.TimeCreated)" }
  foreach ($entry in @($entries | Where-Object Id -in 3005, 3006)) {
    if ($entry.Id -eq 3005) { Fail "$($run.Label) updated itself to a newer release, Event ID 3005 at $($entry.TimeCreated), so what ran was not this build." }
    else { Fail "$($run.Label) found a newer release and the update failed or was not tried, Event ID 3006 at $($entry.TimeCreated)." }
  }
}

# The libraries FrameworkLibraries hands back to the runtime, read from its source,
# each as the resolver compares it: without ".dll", in lower case.
function Get-OwnLibraries {
  @(Read-SourceList 'src/InstallerClean.Core/Interop/Native/FrameworkLibraries.cs' 'OwnLibraries' | ForEach-Object { Get-BaseName $_ })
}

# The strings in the array a source file initialises under $name. The step stops
# where the file has no such array, unless $optional, or where it reads as empty.
function Read-SourceList([string]$path, [string]$name, [switch]$optional) {
  $source = Get-Content -Raw (Join-Path $repository $path)
  if ($source -notmatch "(?s)\b$name\s*=\s*\[(.*?)\];") {
    if ($optional) { return @() }
    throw "$path has no $name list to read."
  }
  $names = @([regex]::Matches($Matches[1], '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
  if (-not $names) { throw "$path's $name list read as empty." }
  $names
}

function Get-BaseName([string]$name) { ($name -replace '\.dll$', '').ToLowerInvariant() }

# The folders under %LOCALAPPDATA% the app keeps its crash log in: its own, and the
# NoFaff one earlier versions use, which it keeps using where it cannot move it.
$crashLogFolders = 'InstallerClean', 'NoFaff\InstallerClean'

# The assemblies the window loads before its Main only where the runtime lists them
# as trusted platform assemblies, read from its LibrarySearch.ImportingAssembliesWhereInstalled.
function Get-WhereInstalled { Read-SourceList 'src/InstallerClean/LibrarySearch.cs' 'ImportingAssembliesWhereInstalled' }

# Whether a file carries .NET metadata, and every module its methods import from,
# by the name the import spells.
function Read-Imports([string]$path) {
  $stream = [IO.File]::OpenRead($path)
  try {
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
      if (-not $pe.HasMetadata) { return [pscustomobject]@{ Managed = $false; Imports = @() } }
      $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
      $imports = @($md.MethodDefinitions | ForEach-Object {
        $import = $md.GetMethodDefinition($_).GetImport()
        if (-not $import.Module.IsNil) { $md.GetString($md.GetModuleReference($import.Module).Name) }
      } | Sort-Object -Unique -CaseSensitive)
      [pscustomobject]@{ Managed = $true; Imports = $imports }
    }
    finally { $pe.Dispose() }
  }
  finally { $stream.Dispose() }
}

# NuGet's global-packages folder, where the runtime packs the builds were made from are.
function Get-NuGetCache {
  $cache = @(dotnet nuget locals global-packages --list | Where-Object { $_ -match '^global-packages:' } |
    ForEach-Object { ($_ -replace '^global-packages:\s*', '').Trim() })
  if ($cache.Count -ne 1) { throw "dotnet nuget locals named $($cache.Count) global-packages folders." }
  $cache[0]
}

# A single-file bundle's bytes and its manifest's header: the entry count and where
# the deps.json and the runtimeconfig.json are, with a reader at the first entry. The
# host finds the manifest by the 32-byte bundle signature, which the SDK writes after
# the manifest's offset.
function Read-BundleHeader([string]$path) {
  $data = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path).Path)
  $signature = [byte[]](0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
    0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae)
  $latin1 = [Text.Encoding]::Latin1
  $at = $latin1.GetString($data).IndexOf($latin1.GetString($signature), [StringComparison]::Ordinal)
  if ($at -lt 8) { throw "$path carries no bundle signature." }
  $reader = [IO.BinaryReader]::new([IO.MemoryStream]::new($data))
  $reader.BaseStream.Position = [BitConverter]::ToInt64($data, $at - 8)
  $major = $reader.ReadUInt32()
  $null = $reader.ReadUInt32()
  $count = $reader.ReadInt32()
  if ($major -ne 6) { throw "$path has a version $major bundle manifest, which this step does not read." }
  # An entry is at least three 8-byte fields, a kind byte and a one-byte path length.
  if ($count -le 0 -or $count -gt ($data.Length - $reader.BaseStream.Position) / 26) { throw "$path's bundle manifest counts $count entries, which its bytes cannot hold." }
  $null = $reader.ReadString()
  # The deps.json's offset and size, the runtimeconfig.json's, then the flags.
  $depsOffset = $reader.ReadInt64()
  $depsSize = $reader.ReadInt64()
  $configOffset = $reader.ReadInt64()
  $configSize = $reader.ReadInt64()
  $null = $reader.ReadInt64()
  [pscustomobject]@{ Data = $data; Reader = $reader; Count = $count; DepsOffset = $depsOffset; DepsSize = $depsSize
    ConfigOffset = $configOffset; ConfigSize = $configSize }
}

# The .NET runtime packs a single-file build was built from, each by name and
# version, read from the libraries its bundled deps.json names. The bundler stores the
# deps.json uncompressed in every bundle. The deps.json also names each pack as a
# dependency, with its version beside the name; only the libraries key is read. The
# step stops where the bundle carries no deps.json, where it names no pack, and where
# it names two versions of one.
function Read-RuntimePacks([string]$path) {
  $bundle = Read-BundleHeader $path
  if ($bundle.DepsSize -le 0 -or $bundle.DepsOffset + $bundle.DepsSize -gt $bundle.Data.Length) { throw "$path's bundle carries no deps.json." }
  $deps = [Text.Encoding]::UTF8.GetString($bundle.Data, $bundle.DepsOffset, $bundle.DepsSize) | ConvertFrom-Json -AsHashtable
  if (-not $deps.libraries) { throw "$path's deps.json has no libraries." }
  $packs = @(foreach ($key in $deps.libraries.Keys) {
    if ($key -cmatch '^runtimepack\.([A-Za-z.]+)\.Runtime\.win-x64/([0-9][^/]*)$') { [pscustomobject]@{ Name = $Matches[1]; Version = $Matches[2] } }
  })
  if (-not $packs) { throw "$path's deps.json names no .NET runtime pack." }
  foreach ($group in ($packs | Group-Object Name)) {
    if ($group.Count -ne 1) { throw "$path's deps.json names $(($group.Group | ForEach-Object { "$($_.Name) $($_.Version)" }) -join ', '), where it should name one version of that runtime pack." }
  }
  $packs
}

# The assemblies a single-file bundle carries in its own folder, by name, read from
# the bundle's manifest. Each entry of a version 6 manifest is its offset, size,
# compressed size, kind (1 for an assembly) and path.
function Read-BundleAssemblies([string]$path) {
  $bundle = Read-BundleHeader $path
  $reader = $bundle.Reader
  $assemblies = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in 1..$bundle.Count) {
    foreach ($field in 1..3) { $null = $reader.ReadInt64() }
    $kind = $reader.ReadByte()
    $file = $reader.ReadString()
    if ($kind -eq 1 -and $file -notmatch '[\\/]' -and $file.EndsWith('.dll', 'OrdinalIgnoreCase')) { $null = $assemblies.Add($file.Substring(0, $file.Length - 4)) }
  }
  if ($assemblies.Count -eq 0) { throw "$path's bundle manifest names no assembly." }
  # The comma keeps the set whole rather than letting PowerShell unroll it.
  , $assemblies
}

# The folder a run's copy of its build is started from, named for the pass and the run.
function Get-RunFolder([string]$pass, [string]$label) {
  Join-Path $env:RUNNER_TEMP ("$pass-" + ($label -replace '[^A-Za-z0-9]+', '-'))
}

# Ends the step with every failure listed, where there was one.
function Exit-IfFailed {
  if (-not $failures.Count) { return }
  Write-Host "$($failures.Count) checks failed:"
  $failures | ForEach-Object { Write-Host "  $_" }
  exit 1
}

# The string literals in one assembly's code, each spelled as the assembly spells it.
function Read-UserStrings([string]$path) {
  $stream = [System.IO.File]::OpenRead($path)
  try {
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
      $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
      $handle = [System.Reflection.Metadata.Ecma335.MetadataTokens]::UserStringHandle(1)
      $strings = @(while (-not $handle.IsNil) {
        $metadata.GetUserString($handle)
        $handle = [System.Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetNextHandle($metadata, $handle)
      })
      @($strings | Sort-Object -Unique -CaseSensitive)
    }
    finally { $pe.Dispose() }
  }
  finally { $stream.Dispose() }
}

# Downloads Process Monitor into $folder and answers its path. It stops where the file
# is not validly signed by Microsoft.
function Get-Procmon([string]$folder) {
  New-Item -ItemType Directory -Force -Path $folder | Out-Null
  $path = Join-Path $folder 'Procmon64.exe'
  Invoke-WebRequest https://live.sysinternals.com/Procmon64.exe -OutFile $path -ConnectionTimeoutSeconds 60 -OperationTimeoutSeconds 60
  $sig = Get-AuthenticodeSignature $path
  if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw "Procmon64.exe is not validly signed by Microsoft: $($sig.Status), $($sig.SignerCertificate.Subject)"
  }
  $path
}

# The Windows libraries the .NET runtime inside the command line and the portable
# looks for in the build's own folders, on behalf of an assembly whose call no
# resolver, module initializer or library search the app sets can answer first. Each
# entry names the exe, the runs of it that make the look, the folders ('beside' the
# exe, and 'extraction' for the folder under %TEMP%\.net its bundle extracts to), the
# assembly whose call makes the look, the kind of call ('import', one of the
# assembly's own imports, or 'NativeLibrary.Load'), when the first look comes
# ('before' any code of the build's own runs, or 'after'), and the names, each
# spelled as the assembly spells it. A look one of these covers is not planted in
# those runs where it passes the first pass's check.
function Get-NotPlanted {
  $cli = 'publish/cli/installerclean-cli.exe'
  $portable = 'publish/self-contained/InstallerClean.exe'
  $bothCli = @('command line', 'command line, checking for updates')
  @(
    # CoreLib imports called during the runtime's start-up in coreclr_initialize:
    # advapi32's EventRegister for EventSource's provider, and a kernel32 import.
    [pscustomobject]@{ Exe = $cli; Runs = $bothCli; Places = @('beside'); Assembly = 'System.Private.CoreLib'; Call = 'import'; When = 'before'; Names = @('advapi32.dll', 'kernel32.dll') }
    # CoreLib's ntdll and bcrypt imports: NtQuerySystemInformation for the leap-second
    # setting on the first DateTime read, and BCryptGenRandom, which seeds the random
    # numbers the thread pool uses.
    [pscustomobject]@{ Exe = $cli; Runs = $bothCli; Places = @('beside'); Assembly = 'System.Private.CoreLib'; Call = 'import'; When = 'after'; Names = @('ntdll.dll', 'BCrypt.dll') }
    # CoreLib's GetProcessWindowStation for Environment.UserInteractive, and its calloc
    # under NativeMemory, which System.Net's DNS query allocates with.
    [pscustomobject]@{ Exe = $cli; Runs = @('command line, checking for updates'); Places = @('beside'); Assembly = 'System.Private.CoreLib'; Call = 'import'; When = 'after'; Names = @('user32.dll', 'ucrtbase.dll') }
    # The runtime's start-up, as the command line's.
    [pscustomobject]@{ Exe = $portable; Runs = @('portable'); Places = @('beside', 'extraction'); Assembly = 'System.Private.CoreLib'; Call = 'import'; When = 'before'; Names = @('advapi32.dll', 'kernel32.dll') }
    # CoreLib imports called during WPF's start-up: a bcrypt, an ole32 and a user32
    # import, and RtlGetVersion for Environment.OSVersion from WPF's event tracing;
    # calloc for System.Net's DNS query in the window's update check; and an oleaut32
    # import once the window's text services have started.
    [pscustomobject]@{ Exe = $portable; Runs = @('portable'); Places = @('beside', 'extraction'); Assembly = 'System.Private.CoreLib'; Call = 'import'; When = 'after'; Names = @('BCrypt.dll', 'ole32.dll', 'ntdll.dll', 'user32.dll', 'ucrtbase.dll', 'oleaut32.dll') }
    # PresentationCore's module initializer, which loads DirectWrite with
    # NativeLibrary.Load and DllImportSearchPath.System32. That call asks no resolver
    # and searches the folder the bundle extracts to before System32.
    [pscustomobject]@{ Exe = $portable; Runs = @('portable'); Places = @('extraction'); Assembly = 'PresentationCore'; Call = 'NativeLibrary.Load'; When = 'before'; Names = @('dwrite.dll') }
  )
}

# Each entry's assembly is read from the runtime packs its exe's bundle was built
# from, in NuGet's cache: an 'import' entry's names have to be among the assembly's
# imports, and a 'NativeLibrary.Load' entry's among the string literals in its code,
# spelled the same. An entry that fails these checks fails the step and is dropped
# from the list, so the looks it would cover are planted in the second pass. The step
# stops where NuGet's cache, a runtime pack or an assembly's metadata cannot be read.
#
# An entry has to name an exe and runs among $builds. With -OnlyWhatThisRuns, an entry
# naming no run in $builds is left out and printed, and one naming some runs not there
# keeps only those that are, for a workflow that runs some of the builds the list is
# kept for.
function Select-NotPlanted($notPlanted, $builds, [switch]$OnlyWhatThisRuns) {
  $cache = Get-NuGetCache
  $packFolders = @{}
  $assemblyPaths = @{}
  $assemblyNames = @{}
  @(foreach ($entry in $notPlanted) {
    $runsOfExe = @($builds | Where-Object Exe -eq $entry.Exe | ForEach-Object Label)
    if ($OnlyWhatThisRuns) {
      $here = @($entry.Runs | Where-Object { $_ -in $runsOfExe })
      if (-not $here) { Write-Host "  not judged here: $($entry.Names -join ', ') listed for $($entry.Exe) in the runs '$($entry.Runs -join "', '")'"; continue }
      $entry = $entry.PSObject.Copy()
      $entry.Runs = $here
    }
    if (-not $runsOfExe) { Fail "The libraries not planted name the exe $($entry.Exe), which no build this job runs is."; continue }
    $stray = @($entry.Runs | Where-Object { $_ -notin $runsOfExe })
    if ($stray -or -not $entry.Runs) { Fail "The libraries not planted for $($entry.Exe) name the runs '$($entry.Runs -join "', '")', where its runs are '$($runsOfExe -join "', '")'."; continue }
    $strayPlace = @($entry.Places | Where-Object { $_ -cnotin 'beside', 'extraction' })
    if ($strayPlace -or -not $entry.Places) { Fail "The libraries not planted for $($entry.Exe) name the folders '$($entry.Places -join "', '")', where each should be 'beside' or 'extraction'."; continue }
    if ($entry.Call -cnotin 'import', 'NativeLibrary.Load') { Fail "The libraries not planted for $($entry.Exe) name the call '$($entry.Call)', which is neither 'import' nor 'NativeLibrary.Load'."; continue }
    if ($entry.When -cnotin 'before', 'after') { Fail "The libraries not planted for $($entry.Exe) say they are looked for '$($entry.When)', which is neither 'before' nor 'after'."; continue }
    if (-not $packFolders.ContainsKey($entry.Exe)) {
      $packFolders[$entry.Exe] = @(foreach ($pack in @(Read-RuntimePacks $entry.Exe)) {
        $folder = Join-Path $cache "$($pack.Name.ToLowerInvariant()).runtime.win-x64/$($pack.Version)"
        if (-not (Test-Path -LiteralPath $folder)) { throw "NuGet's cache does not hold $folder, the $($pack.Name) runtime pack $($entry.Exe) was built from." }
        Write-Host "  $($entry.Exe) was built from the $($pack.Name) runtime pack $($pack.Version)"
        $folder
      })
    }
    $assemblyKey = "$($entry.Exe)|$($entry.Assembly)"
    if (-not $assemblyPaths.ContainsKey($assemblyKey)) {
      $assemblyPaths[$assemblyKey] = @($packFolders[$entry.Exe] | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -File -Filter "$($entry.Assembly).dll" } | ForEach-Object FullName)
    }
    $found = $assemblyPaths[$assemblyKey]
    if ($found.Count -ne 1) { Fail "The runtime packs $($entry.Exe) was built from hold $($found.Count) copies of $($entry.Assembly).dll, where they should hold one."; continue }
    $key = "$($found[0])|$($entry.Call)"
    if (-not $assemblyNames.ContainsKey($key)) {
      $assemblyNames[$key] = if ($entry.Call -eq 'import') { @((Read-Imports $found[0]).Imports) } else { @(Read-UserStrings $found[0]) }
      if (-not $assemblyNames[$key]) { throw "Nothing was read from $($found[0]) for the call $($entry.Call)." }
      if ($entry.Call -eq 'import') { Write-Host "  $($found[0]) imports: $($assemblyNames[$key] -join ', ')" }
    }
    $what = if ($entry.Call -eq 'import') { 'among its imports' } else { 'among the string literals in its code' }
    $missing = @($entry.Names | Where-Object { $assemblyNames[$key] -cnotcontains $_ })
    foreach ($name in $missing) { Fail "$name is listed as not planted for $($entry.Exe), and $($entry.Assembly) does not have it $what, spelled so." }
    if ($missing) { continue }
    $entry
  })
}

# The genuine files a first-pass run's looks call for in its second-pass run, less
# the looks the not-planted list covers. Each listed look the run made is added to
# $listedSeen under its exe, folder and name, with the run's label.
function Get-RunPlants($run, $read, $notPlanted, $listedSeen, $extractionFolders) {
  # A plant goes at the path that was looked at. A look under %TEMP%\.net in a
  # folder that no longer exists once the run ends, the one the bundle writes
  # into before moving its files into place, is printed with its path.
  $plants = [System.Collections.Generic.List[object]]::new()
  $looks = @($read.Libraries | Where-Object { $_.Operation -ne 'Load Image' -and $_.Path -notmatch '[*?]' })
  foreach ($relative in ($looks | Where-Object Place -eq 'own' | ForEach-Object { $_.Relative.ToLowerInvariant() } | Sort-Object -Unique)) {
    $plant = Get-Plant (Split-Path $relative -Leaf) $read.Loads
    $plants.Add([pscustomobject]@{ Place = 'beside'; Path = $relative; Source = $plant.Source; Kind = $plant.Kind })
  }
  foreach ($path in ($looks | Where-Object Place -eq 'extraction' | ForEach-Object { $_.Path.ToLowerInvariant() } | Sort-Object -Unique)) {
    if (Test-Path -LiteralPath $path) { continue }
    if (-not (Test-Path -LiteralPath (Split-Path $path))) { Write-Host "  looked under %TEMP%\.net in a folder gone after the run: $path"; continue }
    $plant = Get-Plant (Split-Path $path -Leaf) $read.Loads
    $plants.Add([pscustomobject]@{ Place = 'extraction'; Path = $path; Source = $plant.Source; Kind = $plant.Kind })
  }

  # A listed library is matched to every look this run made at that name beside the
  # exe, or in a folder it loaded its bundle's files from. It is judged by its first
  # look: a file put at that path would be taken there, so a later look at it adds
  # nothing. The extractor's working folder, which is gone by the end of the run, is
  # never matched. Only the paths of the looks that pass are left unplanted, so a
  # listed look this pass fails is planted in the second as any other.
  $covered = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $unplaced = [System.Collections.Generic.List[string]]::new()
  foreach ($entry in @($notPlanted | Where-Object { $_.Exe -eq $run.Build.Exe -and $run.Label -in $_.Runs })) {
    foreach ($place in $entry.Places) {
      foreach ($name in $entry.Names) {
        $where = if ($place -eq 'beside') { 'beside itself' } else { 'in its extraction folder' }
        $at = @($looks | Where-Object {
          if ($place -eq 'beside') { $_.Place -eq 'own' -and $_.Relative -eq $name }
          else { $_.Place -eq 'extraction' -and (Split-Path $_.Path -Leaf) -eq $name -and (Split-Path $_.Path) -in $extractionFolders }
        } | Sort-Object Index)
        if (-not $at) { Write-Host "  not planted: $name $where, not looked for in this run"; continue }
        $key = "$($entry.Exe)|$place|$name"
        if (-not $listedSeen.ContainsKey($key)) { $listedSeen[$key] = [System.Collections.Generic.List[string]]::new() }
        $listedSeen[$key].Add($run.Label)
        $first = $at[0]
        $spelled = Split-Path $first.Path -Leaf
        $line = "$name $where, first looked for at row $($first.Index), $(Get-When $first $read.Marker), with $($at.Count) rows at that name in all: $($first.Operation) $($first.Result) $($first.Path)"
        if ($spelled -cne $name) { Fail "$($run.Label) looked for $spelled $where at row $($first.Index), which $($entry.Assembly) spells $name."; continue }
        if ($entry.When -eq 'before') {
          if (-not $read.Marker) { $unplaced.Add("$name $where"); continue }
          if ($first.Index -gt $read.Marker.Index) { Fail "$($run.Label) is listed as looking for $name $where before its own code runs, and this pass saw it first at row $($first.Index), after row $($read.Marker.Index), the first act of its own code."; continue }
        }
        Write-Host "  not planted: $line"
        foreach ($look in $at) { $null = $covered.Add($(if ($place -eq 'beside') { "beside|$($look.Relative)" } else { "extraction|$($look.Path)" })) }
      }
    }
  }
  if ($unplaced.Count) { Fail "$($run.Label) showed no act of its own code, so these, listed as looked for before it runs, were not placed and are planted: $($unplaced -join '; ')." }
  @($plants | Where-Object { -not $covered.Contains("$($_.Place)|$($_.Path)") })
}

# A listed library no run of its exe looked for is no longer one it needs listing.
function Test-ListedSeen($notPlanted, $listedSeen) {
  foreach ($entry in $notPlanted) {
    foreach ($place in $entry.Places) {
      foreach ($name in $entry.Names) {
        $seen = $listedSeen["$($entry.Exe)|$place|$name"]
        if ($seen) { Write-Host "  $name, listed as not planted $place $($entry.Exe), was looked for in: $($seen -join '; ')" }
        else { Fail "$name is listed as not planted $place $($entry.Exe), and no run of it this pass looked for it there." }
      }
    }
  }
}

# Puts each of $plants in place for a run started from $folder, and answers each path
# with its hash.
function Add-Plants([string]$folder, $plants) {
  $placed = [System.Collections.Generic.List[object]]::new()
  foreach ($plant in @($plants | Where-Object { $_ })) {
    $path = if ($plant.Place -eq 'beside') { Join-Path $folder $plant.Path } else { $plant.Path }
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    Copy-Item -LiteralPath $plant.Source -Destination $path
    $placed.Add([pscustomobject]@{ Path = $path; Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash })
  }
  $placed
}

# When a row came, against the start of the build's own code, for a message about a
# build; nothing for a program whose build names no source file.
function Get-WhenSuffix($run, $row, $marker) {
  if ($run.Build.Source) { ", $(Get-When $row $marker)" } else { '' }
}

# Judges a second-pass run against the files planted for it. For a program whose build
# names no source file, a load from its own folder is left to Test-OwnLoads.
function Test-PlantedRun($run, $read) {
  # A native library is mapped and loaded; a managed one can be read instead, so
  # any of the three by the build counts as the planted file being taken.
  $placedPaths = @($run.Placed | ForEach-Object Path)
  $loadedPaths = @($read.Loads | ForEach-Object Path)
  foreach ($taken in ($read.Libraries | Where-Object { $_.Operation -in 'CreateFileMapping', 'ReadFile' -and $placedPaths -contains $_.Path -and $loadedPaths -notcontains $_.Path } | Group-Object Path)) {
    $first = $taken.Group[0]
    Fail "$($run.Label) read the planted $($first.Path), $($first.Operation) at row $($first.Index)$(Get-WhenSuffix $run $first $read.Marker)."
  }
  foreach ($load in $read.Loads) {
    if ($placedPaths -contains $load.Path) {
      Fail "$($run.Label) loaded the planted $($load.Path), row $($load.Index)$(Get-WhenSuffix $run $load $read.Marker)."
    }
    elseif ($run.Build.Source -and ($load.Place -eq 'own' -or ($load.Place -eq 'extraction' -and $run.BundleFiles -notcontains $load.Path))) {
      Fail "$($run.Label) loaded $($load.Path), which neither its bundle nor the first pass put there, row $($load.Index)$(Get-WhenSuffix $run $load $read.Marker)."
    }
  }
  foreach ($plant in $run.Placed) {
    $at = @($read.Libraries | Where-Object { $_.Path -eq $plant.Path -and $_.Operation -ne 'Load Image' })
    $found = @($at | Where-Object { $_.Result -eq 'SUCCESS' -and $_.Operation -in 'QueryOpen', 'CreateFile' })
    if (-not $at) { Fail "$($run.Label) did not look for the planted $($plant.Path)." }
    elseif (-not $found) {
      Fail "The planted $($plant.Path) was not there when $($run.Label) looked: $(($at | ForEach-Object { "$($_.Operation) $($_.Result)" } | Sort-Object -Unique) -join ', ')."
    }
    if (-not (Test-Path -LiteralPath $plant.Path)) { Fail "The planted $($plant.Path) was gone once $($run.Label) ended." }
    elseif ((Get-FileHash -Algorithm SHA256 -LiteralPath $plant.Path).Hash -ne $plant.Hash) { Fail "The planted $($plant.Path) was changed while $($run.Label) ran." }
  }
}

# The is-*.tmp folder a path lies under, the first along it, or nothing. Inno Setup
# makes these in %TEMP%, new and newly named for every run.
function Get-IsTmpFolder([string]$path) {
  if ($path -match '^(.*?\\is-[^\\]+\.tmp)\\') { $Matches[1] }
}

# Fails where the run's process maps an image from its own folder, of any name but its
# own exe's, or from an is-*.tmp folder. An image in an is-*.tmp folder that this same
# process created at that path earlier in the record, a CreateFile whose result is
# Created, is printed and passes: a file a process has just written into a folder made
# new for the run is not one left there for it. Answers each is-*.tmp folder an image
# was mapped from.
function Test-OwnLoads($run, $rows) {
  $own = $run.Folder.TrimEnd('\') + '\'
  $self = $own + $run.Exe
  $tempFolders = [System.Collections.Generic.List[string]]::new()
  for ($i = 0; $i -lt $rows.Count; $i++) {
    $row = $rows[$i]
    if ($row.Operation -ne 'Load Image' -or $row.Path -eq $self) { continue }
    $inOwn = $row.Path.StartsWith($own, 'OrdinalIgnoreCase')
    $isTmp = Get-IsTmpFolder $row.Path
    if (-not $inOwn -and -not $isTmp) { continue }
    if ($isTmp) {
      if ($tempFolders -notcontains $isTmp) { $tempFolders.Add($isTmp) }
      $made = @(for ($j = 0; $j -lt $i; $j++) {
        $r = $rows[$j]
        if ($r.Operation -eq 'CreateFile' -and $r.Path -eq $row.Path -and $r.Detail -match 'OpenResult: Created') { $j + 1 }
      })
      if ($made) { Write-Host "  loaded $($row.Path) at row $($i + 1), which it created at row $($made[0])"; continue }
    }
    if ($inOwn) { Fail "$($run.Exe), process $($run.Id), loaded $($row.Path) from its own folder, row $($i + 1)." }
    else { Fail "$($run.Exe), process $($run.Id), loaded $($row.Path) from the is-*.tmp folder $isTmp, which it did not create there, row $($i + 1)." }
  }
  $tempFolders
}

# Each process $parent started, other than one whose image is at $skip, and each
# process those started, read from the record $csv by the process ID in each Process
# Create row. Each is printed with its looks and judged by Test-OwnLoads. Command lines
# are not printed. Answers each process as a run, with the is-*.tmp folders it loaded
# images from.
#
# A Process Create row spells the image's path as the parent passed it, which can be
# in short names, as in C:\Users\RUNNER~1. So a process's folder is taken from its own
# load of its image, spelled as its other rows are, and a process with no such load
# fails and is not judged. A process whose image is at $skip is left out by either
# spelling. Its exe's name is the Create row's, which Read-Record matches to the
# record's process name, so a short name there leaves the process with no rows and
# fails.
function Test-StartedProcesses([string]$csv, $parent, $parentRows, [string]$skip) {
  $all = [System.Collections.Generic.List[object]]::new()
  $parents = @([pscustomobject]@{ Run = $parent; Rows = $parentRows })
  foreach ($depth in 1, 2) {
    $started = @(foreach ($p in $parents) {
      foreach ($row in @($p.Rows | Where-Object Operation -eq 'Process Create')) {
        if ($skip -and $row.Path -eq $skip) { continue }
        if ($row.Detail -notmatch '^PID: (\d+)') { Fail "A Process Create row of process $($p.Run.Id) names no process ID."; continue }
        [pscustomobject]@{ Label = Split-Path $row.Path -Leaf; Build = [pscustomobject]@{ Exe = $row.Path }; Folder = Split-Path $row.Path
          Exe = Split-Path $row.Path -Leaf; Id = [int]$Matches[1]; Parent = "$($p.Run.Exe), process $($p.Run.Id)"; StartedAs = $row.Path; TempFolders = @() }
      }
    })
    if (-not $started) { break }
    $byChild = Read-Record $csv $started
    $next = foreach ($child in $started) {
      $rows = $byChild[[string]$child.Id]
      if (-not $rows) { $all.Add($child); Fail "Process Monitor recorded nothing for $($child.Exe), process $($child.Id)."; continue }
      $image = @($rows | Where-Object { $_.Operation -eq 'Load Image' -and (Split-Path $_.Path -Leaf) -eq $child.Exe })
      if (-not $image) { $all.Add($child); Fail "$($child.Exe), process $($child.Id), started as $($child.StartedAs): Process Monitor recorded no load of its image, so its folder is not known and its looks were not judged."; continue }
      if ($skip -and $image[0].Path -eq $skip) { continue }
      $child.Build.Exe = $image[0].Path
      $child.Folder = Split-Path $image[0].Path
      $all.Add($child)
      Write-Host "== $($child.Exe), process $($child.Id), started by $($child.Parent), from $($child.Folder)"
      if ($child.StartedAs -ne $image[0].Path) { Write-Host "  started as $($child.StartedAs)" }
      Show-Looks @(Get-Libraries $rows $child.Folder) $rows[0].'Time of Day'
      $child.TempFolders = @(Test-OwnLoads $child $rows)
      [pscustomobject]@{ Run = $child; Rows = $rows }
    }
    $parents = @($next)
  }
  $all
}

# Starts a job writing to $out each is-*.tmp folder that appears in %TEMP%, with its
# owner and its permissions as read the moment it is seen, until $stop exists. A
# folder there before the job starts is left out. Answers the job once it has
# started.
function Start-TempWatch([string]$out, [string]$stop) {
  $job = Start-Job -ArgumentList $env:TEMP, $out, $stop -ScriptBlock {
    param($temp, $out, $stop)
    $seen = @{}
    foreach ($d in @(Get-ChildItem -LiteralPath $temp -Directory -Filter 'is-*.tmp' -ErrorAction SilentlyContinue)) { $seen[$d.FullName] = $true }
    Set-Content -Path $out -Value ''
    while (-not (Test-Path -LiteralPath $stop)) {
      foreach ($d in @(Get-ChildItem -LiteralPath $temp -Directory -Filter 'is-*.tmp' -ErrorAction SilentlyContinue)) {
        if ($seen.ContainsKey($d.FullName)) { continue }
        try {
          $acl = Get-Acl -LiteralPath $d.FullName
          "$($d.FullName)`towner $($acl.Owner)`t$($acl.GetSecurityDescriptorSddlForm('Access'))" | Add-Content -Path $out
          $seen[$d.FullName] = $true
        }
        catch { }
      }
      Start-Sleep -Milliseconds 50
    }
  }
  $deadline = [DateTime]::UtcNow.AddSeconds(60)
  while (-not (Test-Path -LiteralPath $out)) {
    if ([DateTime]::UtcNow -gt $deadline) { throw "The job watching %TEMP% did not start within 60 s." }
    Start-Sleep -Milliseconds 200
  }
  $job
}

# Ends the job Start-TempWatch started. An error in ending it is printed.
function Stop-TempWatch($job, [string]$stop) {
  try {
    New-Item -ItemType File -Path $stop -Force | Out-Null
    $null = Wait-Job $job -Timeout 60
    Receive-Job $job | Out-Host
  }
  catch { Write-Host "The job watching %TEMP% did not end cleanly: $($_.Exception.Message)" }
  finally { Remove-Job $job -Force -ErrorAction SilentlyContinue }
}

# Prints the folders the watch wrote to $out. Fails where none of $runs ran from an
# is-*.tmp folder, and where one ran from or loaded an image from such a folder that
# the watch did not see made, whose permissions are then not known.
function Show-TempFolders([string]$out, $runs) {
  Write-Host "== The is-*.tmp folders made in %TEMP% (path, owner, permissions):"
  $made = @(Get-Content $out -ErrorAction SilentlyContinue | Where-Object { $_ })
  foreach ($line in $made) { Write-Host "  $line" }
  $madePaths = @($made | ForEach-Object { ($_ -split "`t")[0] })
  $fromTemp = @($runs | Where-Object { (Split-Path $_.Folder -Leaf) -like 'is-*.tmp' })
  if (-not $fromTemp) { Fail "No process the setup started ran from an is-*.tmp folder." }
  foreach ($child in $fromTemp) {
    if ($madePaths -contains $child.Folder) { Write-Host "  $($child.Exe), process $($child.Id), ran from $($child.Folder), printed above" }
    else { Fail "$($child.Exe), process $($child.Id), ran from $($child.Folder), which the watch on %TEMP% did not see made, so its permissions are not known." }
  }
  foreach ($run in $runs) {
    foreach ($folder in @($run.TempFolders | Where-Object { $_ -and $_ -ne $run.Folder })) {
      if ($madePaths -contains $folder) { Write-Host "  $($run.Exe), process $($run.Id), loaded images from $folder, printed above" }
      else { Fail "$($run.Exe), process $($run.Id), loaded images from $folder, which the watch on %TEMP% did not see made, so its permissions are not known." }
    }
  }
}

# Runs Test-StartedProcesses and Show-TempFolders over a record made up for the
# purpose, and fails the step unless each fails exactly where it must. The setup's
# started process is created under a short-named path, as in C:\Users\RUNNER~1, with
# its own rows in full names. It loads a file nobody created from its own is-*.tmp
# folder, one it opened there without creating it, and one from an is-*.tmp folder
# the watch did not see made, each of which must fail; and one it created in its own
# folder and one it created in another is-*.tmp folder, each of which must pass. A
# second process, created under a short name for $skip's path, must be left out, and
# a third, with no load of its image, must fail.
function Test-SetupControls {
  Write-Host "== Control: the checks on the processes a setup starts"
  $temp = 'C:\Users\runneradmin\AppData\Local\Temp'
  $own = "$temp\is-AAAA.tmp"
  $other = "$temp\is-BBBB.tmp"
  $unseen = "$temp\is-CCCC.tmp"
  $skip = 'C:\Program Files\Control\skipped.exe'
  $created = 'Desired Access: Generic Read/Write, Disposition: OverwriteIf, OpenResult: Created'
  $opened = 'Desired Access: Generic Read, Disposition: Open, OpenResult: Opened'
  $rows = @(
    @(201, 'control.tmp', 'Load Image', "$own\control.tmp", ''),
    @(201, 'control.tmp', 'Load Image', "$own\planted.dll", ''),
    @(201, 'control.tmp', 'CreateFile', "$own\opened.dll", $opened),
    @(201, 'control.tmp', 'Load Image', "$own\opened.dll", ''),
    @(201, 'control.tmp', 'CreateFile', "$own\made.dll", $created),
    @(201, 'control.tmp', 'Load Image', "$own\made.dll", ''),
    @(201, 'control.tmp', 'CreateFile', "$other\_isetup\helper.dll", $created),
    @(201, 'control.tmp', 'Load Image', "$other\_isetup\helper.dll", ''),
    @(201, 'control.tmp', 'CreateFile', "$unseen\helper.dll", $created),
    @(201, 'control.tmp', 'Load Image', "$unseen\helper.dll", ''),
    @(202, 'skipped.exe', 'Load Image', $skip, ''),
    @(202, 'skipped.exe', 'Load Image', 'C:\Program Files\Control\planted.dll', ''),
    @(203, 'noimage.exe', 'CreateFile', 'C:\Windows\System32\kernel32.dll', $opened)
  ) | ForEach-Object {
    [pscustomobject]@{ 'Time of Day' = '12:00:00.0000000 PM'; 'Process Name' = $_[1]; PID = [string]$_[0]; Operation = $_[2]; Path = $_[3]; Result = 'SUCCESS'; Detail = $_[4] }
  }
  $folder = Join-Path ([IO.Path]::GetTempPath()) ('setup-controls-' + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force -Path $folder | Out-Null
  $csv = Join-Path $folder 'record.csv'
  $out = Join-Path $folder 'temp.txt'
  $rows | Export-Csv -Path $csv -NoTypeInformation
  Set-Content -Path $out -Value "$own`towner control`tD:P", "$other`towner control`tD:P"
  $parent = [pscustomobject]@{ Exe = 'control-setup.exe'; Id = 200; Folder = 'C:\Control' }
  $parentRows = @(
    [pscustomobject]@{ Operation = 'Process Create'; Path = 'C:\Users\RUNNER~1\AppData\Local\Temp\is-AAAA.tmp\control.tmp'; Detail = 'PID: 201' },
    [pscustomobject]@{ Operation = 'Process Create'; Path = 'C:\PROGRA~1\Control\skipped.exe'; Detail = 'PID: 202' },
    [pscustomobject]@{ Operation = 'Process Create'; Path = 'C:\Control\noimage.exe'; Detail = 'PID: 203' })
  $caught = [System.Collections.Generic.List[string]]::new()
  $started = & {
    function Fail([string]$message) { $caught.Add($message) }
    $runs = @(Test-StartedProcesses $csv $parent $parentRows $skip)
    Show-TempFolders $out $runs
    $runs
  }
  Remove-Item -LiteralPath $folder -Recurse -Force
  $must = @(
    "loaded $own\planted.dll from its own folder",
    "loaded $own\opened.dll from its own folder",
    "loaded images from $unseen, which the watch",
    'noimage.exe, process 203, started as C:\Control\noimage.exe: Process Monitor recorded no load of its image')
  foreach ($message in $must) {
    $hits = @($caught | Where-Object { $_.Contains($message) })
    if ($hits.Count -eq 1) { Write-Host "  failed as it must: $($hits[0])" }
    else { Fail "The control on a setup's processes failed $($hits.Count) times where one failure names: $message" }
  }
  foreach ($message in $caught) {
    if (-not @($must | Where-Object { $message.Contains($_) })) { Fail "The control on a setup's processes failed where it must pass: $message" }
  }
  $child = @($started | Where-Object Id -eq 201)
  if ($child.Count -ne 1 -or $child[0].Folder -ne $own) { Fail "The control's setup.tmp was not judged from its own folder $own." }
  if (@($started | Where-Object Id -eq 202)) { Fail "The control's process at $skip was judged, where it is to be left out." }
}
