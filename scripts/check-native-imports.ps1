# Fails (exit 1) where the shipped code can reach a Windows library by a search no resolver
# of ours answers, read from the four single-file bundles build-and-test publishes.
#
# Core and the command line declare their P/Invokes in InstallerClean.Interop.Native, where
# the resolver loads each library from System32. CoreLib's own ole32.dll and oleaut32.dll
# imports are bound by the runtime's search instead, which looks beside the exe first. So:
#
#  - A call from Core or the command line into a framework method that reaches one of those
#    CoreLib imports fails, unless an entry below names that caller and that callee and says
#    why the import is not reached at run time. Each hit prints its whole path to the import.
#    The window's InstallerClean.dll is held to CoreLib and the packages alone: WPF itself
#    calls CoreLib's ole32 and oleaut32 imports at start-up.
#  - A P/Invoke outside InstallerClean.Interop.Native fails, and so does any NativeLibrary
#    call other than SetDllImportResolver unless it is listed below.
#  - The package assemblies each framework-dependent bundle carries are pinned by name with
#    the libraries they import, and the self-contained bundle beside it must carry the same
#    bytes.
#  - A reference to System.Net.Quic from our code or a package fails.
#
# The framework is read out of the bundles: a self-contained bundle carries its whole
# framework, and a framework-dependent one is judged against its self-contained sibling's,
# the framework the release is built with. A virtual or interface call is not followed to the
# overrides it can reach (NativeImports.cs says why), so a route through one is for the
# native-libraries job, which runs the builds and sees the look.
#
# Every run also checks itself: known members in and out of each framework set, and a planted
# assembly built here holding one of each fault, every one of which must be reported. An entry
# or a listed call that matches nothing fails, as stale.
#
# Run from the repo root after the four publishes: pwsh scripts/check-native-imports.ps1
param([string]$PublishRoot = 'publish')

$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'NativeImports.cs') -ReferencedAssemblies System.Reflection.Metadata, System.Collections,
  System.Linq, System.Runtime, System.Collections.Immutable, System.IO.Compression, System.Security.Cryptography, System.Memory

$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) { $failures.Add($message); Write-Host "::error::$message" }

# The .NET version the entries' source lines were read at.
$citedVersion = '10.0.13'

$bundles = @(
  [pscustomobject]@{ Label = 'command line'; Path = 'cli/installerclean-cli.exe'; Ours = @('InstallerClean.Core', 'installerclean-cli'); Sibling = $null }
  [pscustomobject]@{ Label = 'command line, framework-dependent'; Path = 'cli-framework-dependent/installerclean-cli.exe'; Ours = @('InstallerClean.Core', 'installerclean-cli'); Sibling = 'command line' }
  [pscustomobject]@{ Label = 'portable'; Path = 'self-contained/InstallerClean.exe'; Ours = @('InstallerClean.Core', 'InstallerClean'); Sibling = $null }
  [pscustomobject]@{ Label = 'slim'; Path = 'framework-dependent/InstallerClean.exe'; Ours = @('InstallerClean.Core', 'InstallerClean'); Sibling = 'portable' }
)
$ours = 'InstallerClean.Core', 'installerclean-cli', 'InstallerClean'

# Each entry names the calls it covers, by caller and callee as this script keys them, and the
# reason the CoreLib import at the end of the path is not reached at run time.
$entries = @(
  [pscustomobject]@{
    Calls = @(, @('[InstallerClean.Core]InstallerClean.Helpers.UnelevatedLauncher::Wrap(IntPtr,System.Collections.Generic.List`1<Object>)',
      'System.Runtime.InteropServices.ComWrappers::GetOrCreateObjectForComInstance(IntPtr,System.Runtime.InteropServices.CreateObjectFlags)'))
    Reason = 'CoGetContextToken is reached only through ReferenceTrackerNativeObjectWrapper, which NativeObjectWrapper.Create makes only ' +
      'under CreateObjectFlags.TrackerObject (ComWrappers.cs:567-580), and Wrap passes UniqueInstance (UnelevatedLauncher.cs). ' +
      'The weak-reference callback the static constructor registers (:551-557, ComWeakRefToObject :1576-1593) creates through ' +
      'GetOrCreateObjectFromWrapper (:751-768), which uses TrackerObject only for an instance registered with RegisterForTrackerSupport ' +
      'or RegisterForMarshalling, and the app registers neither. Read at .NET v10.0.13.'
  }
  [pscustomobject]@{
    Calls = @(
      @('InstallerClean.Services.SelfReplacement::HandOverAndEnd(String,String,System.Nullable`1<System.TimeSpan>)', 'System.Diagnostics.Process::Start(System.Diagnostics.ProcessStartInfo)'),
      @('[installerclean-cli]InstallerClean.Cli.ConsoleUpdateHost::Start(String,System.Collections.Generic.IReadOnlyList`1<String>,String,System.Threading.CancellationToken)', 'System.Diagnostics.Process::Start(System.Diagnostics.ProcessStartInfo)'))
    Reason = 'StartWithCreateProcess reaches CoTaskMemAlloc only through SecureStringToGlobalAllocUnicode, called where ' +
      'ProcessStartInfo.UserName is not empty and Password is set (Process.Windows.cs:527, :552-553). UserName defaults to empty ' +
      '(ProcessStartInfo.cs:157-160) and neither caller sets UserName or Password. Read at .NET v10.0.13.'
  }
  [pscustomobject]@{
    Calls = @(
      @('InstallerClean.Services.UpdateCheckService::CreateShippingHandler()', 'System.Net.Http.HttpClientHandler::.ctor()'),
      @('InstallerClean.Services.UpdateDownloadService::.ctor()', 'System.Net.Http.HttpClientHandler::.ctor()'),
      @('InstallerClean.Services.ResultLogService::.cctor()', 'System.Net.Http.HttpClient::.ctor()'))
    Reason = 'The constructor sets ClientCertificateOption.Manual (HttpClientHandler.cs:77), whose setter installs a certificate ' +
      'selection callback calling CertificateHelper.GetEligibleClientCertificate with SslOptions.ClientCertificates (:275). That ' +
      'returns null at once for a null or empty collection (CertificateHelper.cs:18-20), before the certificate reads that reach ' +
      'FindOidInfo. The callback that reads the certificate store is installed only for ClientCertificateOption.Automatic (:282). ' +
      'The app sets no client certificate and no ClientCertificateOptions. Read at .NET v10.0.13.'
  }
  [pscustomobject]@{
    Calls = @(, @('InstallerClean.Services.MsiFileInfoService::GetDigitalSignature(String)', 'System.Security.Cryptography.X509Certificates.X509Certificate::CreateFromSignedFile(String)'))
    Reason = 'CreateFromSignedFile (X509Certificate.cs:240-243) takes a signed MSI or MSP through GetSignerInPKCS7Store ' +
      '(CertificatePal.Windows.Import.cs:70-73). Only a file whose content is a PFX takes the PKCS#12 loader (:75-90), the one route ' +
      'to Guid.NewGuid, and a PFX is not what Windows Installer keeps in its cache. Read at .NET v10.0.13.'
  }
)

# The NativeLibrary calls Core may make, by caller and callee. SetDllImportResolver is printed
# and not listed.
$nativeLibraryCalls = @(
  [pscustomobject]@{
    Caller = '[InstallerClean.Core]InstallerClean.Interop.Native.SystemLibraries::LoadLibraryFromSystem32(String)'
    Callees = @('System.Runtime.InteropServices.NativeLibrary::TryLoad(String,IntPtr&)', 'System.Runtime.InteropServices.NativeLibrary::TryGetExport(IntPtr,String,IntPtr&)')
    Reason = 'loads kernel32.dll by its full path in Environment.SystemDirectory and takes LoadLibraryExW from it'
  }
)

# The packages each framework-dependent bundle carries, with the libraries each imports.
$packages = @{
  'command line, framework-dependent' = [ordered]@{
    'Microsoft.Extensions.DependencyInjection' = ''
    'Microsoft.Extensions.DependencyInjection.Abstractions' = ''
    'System.Diagnostics.EventLog' = 'advapi32.dll,kernel32.dll,wevtapi.dll'
    'System.Diagnostics.EventLog.Messages' = ''
    'System.IO.Abstractions' = ''
    'TestableIO.System.IO.Abstractions' = ''
    'TestableIO.System.IO.Abstractions.Wrappers' = ''
    'Testably.Abstractions.FileSystem.Interface' = ''
  }
  'slim' = [ordered]@{
    'CommunityToolkit.Mvvm' = ''
    'Microsoft.Extensions.DependencyInjection' = ''
    'Microsoft.Extensions.DependencyInjection.Abstractions' = ''
    'System.Diagnostics.EventLog.Messages' = ''
    'System.IO.Abstractions' = ''
    'TestableIO.System.IO.Abstractions' = ''
    'TestableIO.System.IO.Abstractions.Wrappers' = ''
    'Testably.Abstractions.FileSystem.Interface' = ''
  }
}
# The planted assembly: one of each fault, built here and judged as if it shipped.
$fixtureSource = @'
using System;
using System.Net.Quic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Planted;

public static partial class Faults
{
    public static Guid NewGuid() => Guid.NewGuid();
    public static IntPtr Bstr(string value) => Marshal.StringToBSTR(value);
    public static IntPtr Load() => NativeLibrary.Load("planted");
    public static bool Quic() => QuicConnection.IsSupported;

    [LibraryImport("planted", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ReadName(out string name);
}
'@
$fixtureFindings = [ordered]@{
  'call: System.Guid::NewGuid()' = $false
  'call: System.Runtime.InteropServices.Marshal::StringToBSTR(String)' = $false
  'call: System.Runtime.InteropServices.Marshalling.Utf16StringMarshaller::Free(UInt16*)' = $false
  'P/Invoke: Planted.Faults planted!ReadName' = $false
  'NativeLibrary: System.Runtime.InteropServices.NativeLibrary::Load(String)' = $false
  'reference: System.Net.Quic' = $false
}

$fixtureDir = Join-Path ([IO.Path]::GetTempPath()) ('planted-native-imports-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureDir | Out-Null
Set-Content -Path (Join-Path $fixtureDir 'Planted.cs') -Value $fixtureSource
Set-Content -Path (Join-Path $fixtureDir 'Planted.csproj') -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NoWarn>CA1416</NoWarn>
  </PropertyGroup>
</Project>
'@
$build = dotnet build (Join-Path $fixtureDir 'Planted.csproj') -c Release --nologo -o (Join-Path $fixtureDir 'out') 2>&1
if ($LASTEXITCODE -ne 0) { $build | Write-Host; throw 'The planted assembly did not build.' }
$fixturePath = Join-Path $fixtureDir 'out/Planted.dll'
if (-not (Test-Path -LiteralPath $fixturePath)) { throw "The planted assembly's build wrote no $fixturePath." }
$fixture = [NativeImports.Assembly]::Open([IO.File]::ReadAllBytes($fixturePath), 'Planted.dll')
Remove-Item -LiteralPath $fixtureDir -Recurse -Force

# Read every bundle. One Assembly object per distinct set of bytes, shared by every bundle
# that carries them.
$bySha = @{}
foreach ($bundle in $bundles) {
  $path = Join-Path $PublishRoot $bundle.Path
  if (-not (Test-Path -LiteralPath $path)) { throw "$path is not there; the four publishes come first." }
  $bundle | Add-Member Assemblies @()
  $bundle | Add-Member Others ([ordered]@{})
  foreach ($entry in [NativeImports.Bundle]::Read((Resolve-Path -LiteralPath $path).Path)) {
    if (-not [NativeImports.Assembly]::HasMetadata($entry.Bytes)) {
      if (-not $bundle.Others.Contains("kind $($entry.Kind)")) { $bundle.Others["kind $($entry.Kind)"] = [System.Collections.Generic.List[string]]::new() }
      $bundle.Others["kind $($entry.Kind)"].Add($entry.Path)
      continue
    }
    $sha = [NativeImports.Bundle]::Sha256($entry.Bytes)
    if (-not $bySha.ContainsKey($sha)) { $bySha[$sha] = [NativeImports.Assembly]::Open($entry.Bytes, $entry.Path) }
    $bundle.Assemblies += [pscustomobject]@{ Path = $entry.Path; Compressed = $entry.Compressed; Assembly = $bySha[$sha] }
  }
}

# The non-public type names of every assembly, by name, so a reference is keyed the way the
# assembly it names keys its own definition.
$nonPublic = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.HashSet[string]]]::new()
foreach ($a in @($bySha.Values) + $fixture) {
  if (-not $nonPublic.ContainsKey($a.Name)) { $nonPublic[$a.Name] = [System.Collections.Generic.HashSet[string]]::new() }
  $nonPublic[$a.Name].UnionWith($a.NonPublic)
}
foreach ($a in @($bySha.Values) + $fixture) { $a.ReadMethods($nonPublic) }

function Get-Bundle([string]$label) { $bundles | Where-Object Label -eq $label }
function Get-CoreLib($bundle) { @($bundle.Assemblies | Where-Object { $_.Assembly.Name -eq 'System.Private.CoreLib' }) }

# The first line: the .NET each bundle carries, and where it differs from the entries' sources.
$versions = foreach ($bundle in $bundles) {
  $corelib = Get-CoreLib $bundle
  if ($bundle.Sibling) {
    if ($corelib.Count) { throw "The $($bundle.Label) bundle carries a CoreLib, which a framework-dependent build does not." }
    "$($bundle.Label): none carried, judged against the $($bundle.Sibling)'s"
  }
  else {
    if ($corelib.Count -ne 1) { throw "The $($bundle.Label) bundle carries $($corelib.Count) copies of CoreLib where it should carry one." }
    $bundle | Add-Member Version (($corelib[0].Assembly.InformationalVersion -split '[-+]')[0])
    "$($bundle.Label): .NET $($bundle.Version) ($($corelib[0].Assembly.InformationalVersion))"
  }
}
Write-Host ('.NET read from each bundle: ' + ($versions -join '; '))
$carried = @($bundles | Where-Object Version | ForEach-Object Version | Sort-Object -Unique)
if ($carried | Where-Object { $_ -ne $citedVersion }) {
  Write-Host "The entries' source lines were read at .NET v$citedVersion; these bundles carry v$($carried -join ', v')."
}

foreach ($bundle in $bundles) {
  Write-Host ''
  Write-Host "== $($bundle.Label): $(Join-Path $PublishRoot $bundle.Path)"
  foreach ($a in $bundle.Assemblies | Sort-Object Path) {
    Write-Host "  $($a.Path)  $($a.Assembly.Sha256)$(if ($a.Compressed) { '  (compressed)' })"
  }
  foreach ($kind in $bundle.Others.Keys) { Write-Host "  without metadata, $kind`: $($bundle.Others[$kind] -join ', ')" }
  foreach ($name in $bundle.Ours) {
    $found = @($bundle.Assemblies | Where-Object { $_.Path -eq "$name.dll" })
    if ($found.Count -ne 1) { throw "The $($bundle.Label) bundle carries $($found.Count) copies of $name.dll where it should carry one." }
  }
}

# The assemblies a bundle is judged against: its own other than ours, and for a
# framework-dependent bundle its sibling's too, where it carries none at that path.
function Get-Framework($bundle) {
  $own = @($bundle.Assemblies | Where-Object { $_.Assembly.Name -notin $ours })
  if (-not $bundle.Sibling) { return $own }
  $paths = [System.Collections.Generic.HashSet[string]]::new([string[]]@($own | ForEach-Object Path))
  $own + @((Get-Bundle $bundle.Sibling).Assemblies | Where-Object { $_.Assembly.Name -notin $ours -and -not $paths.Contains($_.Path) })
}

# The window's set: CoreLib and the packages, which are what the slim carries other than ours.
$packagePaths = @((Get-Bundle 'slim').Assemblies | Where-Object { $_.Path -notmatch '/' -and $_.Assembly.Name -notin $ours } | ForEach-Object Path)
function Get-WindowSet($bundle) {
  @(Get-Framework $bundle) | Where-Object { $_.Assembly.Name -eq 'System.Private.CoreLib' -or $_.Path -in $packagePaths }
}

$inClosure = 'System.Guid::NewGuid()', 'System.Runtime.InteropServices.Marshal::StringToBSTR(String)',
  'System.Runtime.InteropServices.Marshal::FreeCoTaskMem(IntPtr)', 'System.Environment::GetFolderPath(System.Environment+SpecialFolder)',
  'System.Runtime.InteropServices.Marshalling.Utf16StringMarshaller::Free(UInt16*)'
$outOfClosure = 'System.Runtime.InteropServices.Marshal::ReadInt32(IntPtr)',
  'System.Runtime.InteropServices.Marshalling.ArrayMarshaller`2+ManagedToUnmanagedIn::Free()'

function New-Closure([string]$name, $set) {
  $closure = [NativeImports.Closure]::new([NativeImports.Assembly[]]@($set | ForEach-Object Assembly))
  $corelib = @($set | Where-Object { $_.Assembly.Name -eq 'System.Private.CoreLib' })
  if ($corelib.Count -ne 1) { throw "$name holds $($corelib.Count) copies of CoreLib where it should hold one." }
  Write-Host "  $name`: $(@($set).Count) assemblies, $($closure.Sinks.Count) CoreLib imports of ole32 or oleaut32, $($closure.Members.Count) members reaching them"
  if ($closure.Sinks.Count -eq 0) { Fail "$name finds no CoreLib import of ole32 or oleaut32." }
  foreach ($key in $inClosure) {
    if (-not $corelib[0].Assembly.Methods.Contains($key)) { Fail "$name`: CoreLib defines no $key, so the control cannot be read." }
    elseif ($closure.Members.Contains($key)) { Write-Host "    control, in the set as it should be: $key" }
    else { Fail "$name leaves out $key, which reaches an import." }
  }
  foreach ($key in $outOfClosure) {
    if (-not $corelib[0].Assembly.Methods.Contains($key)) { Fail "$name`: CoreLib defines no $key, so the control cannot be read." }
    elseif (-not $closure.Members.Contains($key)) { Write-Host "    control, out of the set as it should be: $key" }
    else { Fail "$name holds $key, which reaches no import." }
  }
  $closure
}

$matched = @{}
$reported = @{}
function Test-Calls([string]$where, $assembly, $closure) {
  foreach ($call in $assembly.Calls) {
    if (-not $closure.Members.Contains($call.Callee)) { continue }
    $id = "$($call.Caller) -> $($call.Callee)"
    $entry = $entries | Where-Object { $_.Calls | Where-Object { $_[0] -eq $call.Caller -and $_[1] -eq $call.Callee } } | Select-Object -First 1
    if ($entry) { $matched[$id] = $true }
    $seen = "$where|$id"
    if ($reported.ContainsKey($seen)) { continue }
    $reported[$seen] = $true
    Write-Host "  $($assembly.Name): $($call.Caller)"
    Write-Host ('    -> ' + $closure.Path($call.Callee).Replace("`n", "`n    "))
    if ($entry) { Write-Host "    listed: $($entry.Reason)" }
    else { Fail "$where`: $($assembly.Name)'s $($call.Caller) calls $($call.Callee), which reaches a CoreLib import of ole32 or oleaut32." }
  }
}

Write-Host ''
Write-Host '== Calls into framework code that reaches one of CoreLib''s ole32 or oleaut32 imports'
foreach ($bundle in $bundles) {
  $note = if ($bundle.Sibling) { ", judged against the $($bundle.Sibling)'s framework" } else { '' }
  Write-Host "$($bundle.Label)$note"
  $framework = New-Closure "$($bundle.Label), framework" @(Get-Framework $bundle)
  foreach ($name in $bundle.Ours | Where-Object { $_ -ne 'InstallerClean' }) {
    Test-Calls $bundle.Label ($bundle.Assemblies | Where-Object Path -eq "$name.dll").Assembly $framework
  }
  if ($bundle.Ours -contains 'InstallerClean') {
    $window = New-Closure "$($bundle.Label), CoreLib and the packages" @(Get-WindowSet $bundle)
    Test-Calls $bundle.Label ($bundle.Assemblies | Where-Object Path -eq 'InstallerClean.dll').Assembly $window
  }
}
foreach ($entry in $entries) {
  foreach ($pair in $entry.Calls) {
    if (-not $matched.ContainsKey("$($pair[0]) -> $($pair[1])")) { Fail "The entry for $($pair[0]) -> $($pair[1]) matches no call in any bundle." }
  }
}

# P/Invokes, NativeLibrary calls, calli and references, once for each distinct assembly of ours.
function Test-Assembly($assembly, [string]$where, $findings) {
  Write-Host "$($assembly.Name).dll $($assembly.Sha256), in $where"
  $imports = @($assembly.Imports | Sort-Object Type, EntryPoint)
  Write-Host "  P/Invokes: $($imports.Count)"
  foreach ($import in $imports) {
    $namespace = ($import.Type -split '\+')[0] -replace '\.[^.]+$', ''
    Write-Host "    $($import.Type)  $($import.Library)!$($import.EntryPoint)"
    if ($namespace -ne 'InstallerClean.Interop.Native') {
      if ($findings) { $findings["P/Invoke: $($import.Type) $($import.Library)!$($import.EntryPoint)"] = $true }
      else { Fail "$($assembly.Name) declares a P/Invoke outside InstallerClean.Interop.Native: $($import.Type) $($import.Library)!$($import.EntryPoint)." }
    }
  }
  foreach ($call in $assembly.Calls | Where-Object { $_.Callee.StartsWith('System.Runtime.InteropServices.NativeLibrary::') } | Sort-Object Caller, Callee -Unique) {
    Write-Host "  NativeLibrary: $($call.Caller) -> $($call.Callee)"
    if ($call.Callee.StartsWith('System.Runtime.InteropServices.NativeLibrary::SetDllImportResolver(')) { continue }
    $listed = $nativeLibraryCalls | Where-Object { $_.Caller -eq $call.Caller -and $_.Callees -contains $call.Callee }
    if ($listed) { $script:nativeMatched["$($call.Caller) -> $($call.Callee)"] = $true; Write-Host "    listed: $($listed.Reason)" }
    elseif ($findings) { $findings["NativeLibrary: $($call.Callee)"] = $true }
    else { Fail "$($assembly.Name)'s $($call.Caller) calls $($call.Callee), which loads a library no resolver is asked about." }
  }
  $generated = @($assembly.Callis.Keys | Where-Object { $_ -match '\]<[^>]+>F[0-9A-F]+__InterfaceImplementation::' })
  Write-Host "  calli: $($generated.Count) methods of generated COM interface implementations"
  foreach ($key in $assembly.Callis.Keys | Where-Object { $_ -notin $generated } | Sort-Object) { Write-Host "  calli: $key" }
  if ($assembly.AssemblyReferences -contains 'System.Net.Quic') {
    if ($findings) { $findings['reference: System.Net.Quic'] = $true }
    else { Fail "$($assembly.Name) references System.Net.Quic." }
  }
}

Write-Host ''
Write-Host '== P/Invokes, NativeLibrary calls and calli in our assemblies'
$nativeMatched = @{}
foreach ($group in $bundles | ForEach-Object { $b = $_; $b.Assemblies | Where-Object { $_.Assembly.Name -in $ours -and $_.Path -notmatch '/' } | ForEach-Object { [pscustomobject]@{ Bundle = $b.Label; Assembly = $_.Assembly } } } | Group-Object { $_.Assembly.Sha256 }) {
  Test-Assembly $group.Group[0].Assembly (($group.Group | ForEach-Object Bundle) -join ', ') $null
}
foreach ($listed in $nativeLibraryCalls) {
  foreach ($callee in $listed.Callees) {
    if (-not $nativeMatched.ContainsKey("$($listed.Caller) -> $callee")) { Fail "The listed NativeLibrary call $($listed.Caller) -> $callee matches no call." }
  }
}

Write-Host ''
Write-Host '== Packages'
foreach ($fd in $bundles | Where-Object Sibling) {
  $pinned = $packages[$fd.Label]
  $sibling = Get-Bundle $fd.Sibling
  $carried = @($fd.Assemblies | Where-Object { $_.Path -notmatch '/' -and $_.Assembly.Name -notin $ours })
  Write-Host "$($fd.Label)"
  foreach ($p in $carried | Sort-Object Path) {
    $name = $p.Assembly.Name
    $libraries = (@($p.Assembly.Imports | ForEach-Object { [NativeImports.Closure]::Library($_.Library) }) | Sort-Object -Unique) -join ','
    Write-Host "  $name  imports: $(if ($libraries) { $libraries } else { 'none' })"
    if (-not $pinned.Contains($name)) { Fail "The $($fd.Label) bundle carries $name, which is not on its package list." }
    elseif ($pinned[$name] -ne $libraries) { Fail "$name in the $($fd.Label) bundle imports '$libraries' where its package list gives '$($pinned[$name])'." }
    if ($p.Assembly.AssemblyReferences -contains 'System.Net.Quic') { Fail "$name references System.Net.Quic." }
    $copy = @($sibling.Assemblies | Where-Object Path -eq $p.Path)
    if ($copy.Count -ne 1) { Fail "The $($sibling.Label) bundle carries $($copy.Count) copies of $($p.Path) where it should carry one." }
    elseif ($copy[0].Assembly.Sha256 -eq $p.Assembly.Sha256) { Write-Host "    the $($sibling.Label) carries the same bytes" }
    else { Fail "The $($sibling.Label) bundle's $name differs from the $($fd.Label) bundle's." }
  }
  foreach ($name in $pinned.Keys) {
    if (-not ($carried | Where-Object { $_.Assembly.Name -eq $name })) { Fail "The $($fd.Label) bundle does not carry $name, which its package list names." }
  }
}
Write-Host ''
Write-Host '== The planted assembly, judged against the command line''s framework'
$cliFramework = New-Closure 'command line, framework' @(Get-Framework (Get-Bundle 'command line'))
foreach ($call in $fixture.Calls | Where-Object { $cliFramework.Members.Contains($_.Callee) } | Sort-Object Callee -Unique) {
  Write-Host "  $($call.Caller) -> $($call.Callee)"
  $fixtureFindings["call: $($call.Callee)"] = $true
}
Test-Assembly $fixture 'the planted assembly' $fixtureFindings
foreach ($finding in $fixtureFindings.Keys) {
  if ($fixtureFindings[$finding]) { Write-Host "  reported, as it should be: $finding" }
  else { Fail "The planted assembly's $finding was not reported, so the check cannot be shown to find it." }
}

Write-Host ''
if ($failures.Count) {
  Write-Host "$($failures.Count) checks failed:"
  $failures | ForEach-Object { Write-Host "  $_" }
  exit 1
}
Write-Host 'Every check passed.'
