; InstallerClean Inno Setup script.
;
; AppId stays constant across versions ("InstallerClean") so Windows
; treats every shipped version as the same product; the uninstall
; entry lives at HKLM\Software\Microsoft\Windows\CurrentVersion\
; Uninstall\InstallerClean_is1. Changing AppId breaks Add/Remove
; Programs continuity across versions.
;
; AppVersion is passed in by CI and by the release script, both reading it
; from Directory.Build.props: "ISCC.exe /DAppVersion=X.Y.Z ...". A compile
; without it fails here rather than falling back, and there is deliberately no
; fallback to add: any hand-maintained default tracks the shipping version only
; until the version bump that forgets it, and a stale one produces a setup whose
; Add/Remove Programs entry states a version the binaries inside it are not,
; silently, on the one build nobody checks. There is no correct value to fall
; back TO.
[Setup]
#ifndef AppVersion
  #error AppVersion is not defined. Pass it on the command line, reading the value from Directory.Build.props: ISCC.exe /DAppVersion=X.Y.Z installer\InstallerClean.iss
#endif
; Copyright year comes from the build clock (compile-time) so the
; notice never goes stale.
#define CurrentYear GetDateTimeString('yyyy','','')
; The publisher name and repo URL each appear in several directives below;
; defined once here so they cannot drift apart.
#define MyCompany "No Faff"
#define MyRepoUrl "https://github.com/no-faff/InstallerClean"
; The published binaries and the setup output all sit under ..\publish;
; defined once here so the [Files] sources and OutputDir cannot drift apart.
#define PublishDir "..\publish"
; The single-instance mutex, named by AppMutex and by the wait in [Code]. It matches
; InstanceLock.Name in src/InstallerClean.Core/Services/InstanceLock.cs, and
; scripts/check-instance-lock-name.mjs holds the two together.
#define AppMutexName "Global\InstallerClean_SingleInstance"
; The mutex Setup holds while it runs, named by SetupMutex. It matches
; ExeReplacement.SetupMutexName in src/InstallerClean.Core/Services/ExeReplacement.cs,
; which leaves the files [Code] holds aside alone while Setup runs, and
; scripts/check-setup-held-copies.mjs holds the two together.
#define SetupMutexName "Global\InstallerClean_Setup"
; What [Code] adds to the name of each file it holds aside while Setup writes.
; It matches ExeReplacement.SetupHeldSuffix, and the same check holds them together.
#define HeldSuffix ".setup-old"
AppId=InstallerClean
AppName=InstallerClean
AppVersion={#AppVersion}
; AppVerName sets the name+version string Inno shows in the wizard title bar; set
; explicitly so it reads "InstallerClean <version>" with no localised
; "version"/"versione" word. Inno Setup 6's default includes that word in every
; language and offers no [Messages] override for it (an "<lang>.NameAndVersion"
; entry is unrecognised and silently ignored). Inno Setup 7 drops the word from
; the default, so on 7.x this directive is redundant but harmless; keeping it
; means the title stays correct on either compiler. The Add/Remove Programs entry
; is named by UninstallDisplayName further down and does not follow this
; directive.
AppVerName=InstallerClean {#AppVersion}
; Setup pauses with a "close the running app" prompt while the mutex's name can
; be opened, which it can while the window or a command-line Delete or Move has it
; open, held or not. A command-line scan never opens it, so Setup can meet one
; running from {app}; [Code] holds each file aside before it is written for that
; reason (CurStepChanged below). Started by an update, from the window or the
; command line, Setup first waits for the app to close (InitializeSetup below).
AppMutex={#AppMutexName}
SetupMutex={#SetupMutexName}
; %LOCALAPPDATA%\InstallerClean\ user data (settings.json,
; last-run.json, settings.json.bad on a corrupt-and-recovered run,
; crash.log, crash.log.old once the log has rotated) survives
; uninstall by design: the saved move destination, and settings.json's
; record of whether this account's report has gone or is still waiting
; to go, carry across upgrades.
; The CLI's Application event-log source, registered on its first run at
; HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Application\InstallerClean,
; survives too, and no [Registry] or [UninstallDelete] entry should be added
; to remove it: Event Viewer resolves an entry's description through its
; source, so deleting the source turns every audit entry the CLI has already
; written into "the description for Event ID ... cannot be found".
; The first-run mark, the value FirstRunRecorded under
; HKLM\SOFTWARE\NoFaff\InstallerClean, survives too, and no [Registry] or
; [UninstallDelete] entry should be added for it: removed, it would make a
; reinstall read as the PC's first run.
AppPublisher={#MyCompany}
AppPublisherURL={#MyRepoUrl}
AppSupportURL={#MyRepoUrl}/discussions
AppCopyright=(c) {#CurrentYear} {#MyCompany}
; Win32 VS_FIXEDFILEINFO is a four-part version; AppVersion is three,
; so VersionInfoVersion / VersionInfoProductVersion pad with .0.
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}.0
VersionInfoProductName=InstallerClean
VersionInfoCompany={#MyCompany}
VersionInfoCopyright=(c) {#CurrentYear} {#MyCompany}
VersionInfoDescription=InstallerClean Setup
DefaultDirName={autopf}\InstallerClean
DefaultGroupName=InstallerClean
; The name Windows shows in Apps and Features. Inno's default here is AppVerName,
; which would carry the version into a list that already prints it in a column of
; its own, so the entry would read it twice. Setting the name explicitly leaves
; the wizard title bar alone. It does not change what winget matches on either:
; the published manifest correlates on the product code and carries no display
; name at all.
UninstallDisplayName=InstallerClean
UninstallDisplayIcon={app}\InstallerClean.exe
OutputDir={#PublishDir}
; The version is part of the download's name, so a setup exe sitting in a
; Downloads folder months later says which release it is before anybody opens
; its properties. The release pipeline builds the same name from the same
; version to find this file afterwards, so the two have to agree.
OutputBaseFilename=InstallerClean-{#AppVersion}-setup
; Compression=bzip; SolidCompression=no. Static antivirus models score a setup's
; hash differently under each compression setting, and lzma2 and zip draw false
; positives on this one more often than bzip does. Changing either line means
; scanning the setup again.
Compression=bzip
SolidCompression=no
; The .NET 10 Desktop Runtime's oldest supported Windows release is
; Windows 10 version 1607 (build 14393), so an older build is blocked here
; with a clear message instead of failing cryptically at first launch. Inno
; Setup reads the true build via RtlGetVersion, which is not subject to the
; GetVersionEx compatibility cap, so the build-level floor is enforced on
; Windows 10 and 11.
MinVersion=10.0.14393
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupIconFile=..\src\InstallerClean\Assets\app.ico
WizardStyle=modern dynamic
DisableProgramGroupPage=yes
WizardImageFile=wizard-image.bmp
WizardImageFileDynamicDark=wizard-image-dark.bmp
WizardSmallImageFile=wizard-small.png
WizardSmallImageFileDynamicDark=wizard-small.png
WizardImageAlphaFormat=defined
ShowLanguageDialog=yes
; Re-detect the wizard language each run rather than reusing the previous
; install's pick, so a language added in a later version becomes the default
; for an upgrading user whose OS matches it; the dialog still lists them all.
UsePreviousLanguage=no
; The uninstaller is signed only by the release workflow, which defines
; UninstallerSignatureDir. With SignedUninstaller=yes and no SignTool, the
; compiler writes the uninstaller it is about to embed into that folder as
; uninst-<Inno Setup version>-<hash>.e32 and stops, unless a signed copy of that
; exact file is already there. The workflow compiles once to get the file, has it
; signed with the executables, puts the signed copy back and compiles again. The
; hash is of the file's contents, so the second compile stops the same way if
; anything that goes into the uninstaller changed in between. An installed
; unins000.exe then carries the setup's publisher, and that is the name Windows
; shows when uninstalling asks for administrator rights. Setup also writes the
; uninstaller's messages to unins000.msg beside it, because they cannot be added
; to a signed file. Every other build leaves the name undefined and embeds an
; unsigned uninstaller.
#ifdef UninstallerSignatureDir
SignedUninstaller=yes
SignedUninstallerDir={#UninstallerSignatureDir}
#endif

; Language, message and custom-message definitions live in their own file so the
; main script stays on install logic; each added language touches only that file.
; It is UTF-8 (it carries text in many languages); see its header.
#include "InstallerClean_Languages.iss"

[Files]
Source: "{#PublishDir}\self-contained\InstallerClean.exe"; DestDir: "{app}"; Flags: ignoreversion
; CLI is a .NET console exe published from src/InstallerClean.Cli;
; ships alongside the GUI so PowerShell and cmd block on the process
; subsystem naturally.
Source: "{#PublishDir}\cli\installerclean-cli.exe"; DestDir: "{app}"; Flags: ignoreversion
; pad.xml's Distribution_Permissions requires the Apache 2.0 licence text to
; travel alongside any redistributed binary, so Setup installs it too.
; DestName gives the installed copy a .txt extension so a double-click opens it
; in Notepad; a bare "LICENSE" with no extension makes Windows show the "how do
; you want to open this file?" picker instead.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
; Same argument, for what is redistributed rather than what is licensed: the GUI
; embeds the fonts and icons THIRD-PARTY-NOTICES names, each under its own
; licence, and this setup compiles in the wizard translations carried under
; installer\Languages. The DestName trick is the LICENSE one and is needed for
; the same reason.
Source: "..\THIRD-PARTY-NOTICES"; DestDir: "{app}"; DestName: "THIRD-PARTY-NOTICES.txt"; Flags: ignoreversion

[UninstallDelete]
; A file [Code] held aside and could not delete afterwards, its program still
; running then.
Type: files; Name: "{app}\*{#HeldSuffix}"
Type: files; Name: "{app}\*{#HeldSuffix}-*"

[Icons]
Name: "{group}\InstallerClean"; Filename: "{app}\InstallerClean.exe"; IconFilename: "{app}\InstallerClean.exe"
Name: "{group}\{cm:UninstallApp}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\InstallerClean.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent shellexec
; The app's Update now starts this setup with /SILENT and /UPDATEFROMAPP and then
; closes, and the app opens again once the files are in place. skipifsilent
; skips the entry above on every silent install, winget's and Chocolatey's
; included, so this one does the opening, and only where UpdatingFromApp finds the
; app's own switch on the command line.
Filename: "{app}\InstallerClean.exe"; Flags: nowait shellexec; Check: UpdatingFromApp

[Code]
// Whether Setup's command line carries Switch. Setup takes no meaning from a switch
// it does not know and hands it to this code through ParamStr.
function HasSwitch(const Switch: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Switch) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

// Whether the setup was started by the app's Update now, which passes
// /UPDATEFROMAPP. Every version of the app passes it to every later
// setup, so it keeps this meaning.
function UpdatingFromApp: Boolean;
begin
  Result := HasSwitch('/UPDATEFROMAPP');
end;

// Whether the setup was started by an update of the command line, which passes
// /UPDATEFROMCLI. The command line passes it to the setup of the version it updates
// to, so it keeps this meaning. It opens nothing: nobody may be watching, and the
// [Run] entry that opens the app checks UpdatingFromApp alone.
function UpdatingFromCommandLine: Boolean;
begin
  Result := HasSwitch('/UPDATEFROMCLI');
end;

// Started by an update, Setup waits for every copy of the app to close before it
// checks AppMutex, for up to 30 seconds: the copy that starts Setup, or the one it
// stands for, may still be closing, and its handle to the mutex stays open until it
// closes it as it exits. Setup calls InitializeSetup before its own AppMutex check,
// which then asks for InstallerClean to be closed where the mutex is still open, and
// fails the setup with nothing written where /SUPPRESSMSGBOXES answers that box.
function InitializeSetup: Boolean;
var
  Waited: Integer;
begin
  Result := True;
  if not (UpdatingFromApp or UpdatingFromCommandLine) then Exit;
  Waited := 0;
  while CheckForMutexes('{#AppMutexName}') and (Waited < 30000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  if Waited > 0 then
    Log(Format('Waited %d ms for InstallerClean to close.', [Waited]));
end;

// Before any file is written, each file [Files] writes to {app} is renamed aside, and
// Setup writes every one as new. Inno Setup replaces a file in place by deleting it and
// moving its own copy in, and Windows refuses to delete a running program, such as a
// command-line scan, which holds no mutex; it allows renaming one. A failed setup
// undoes only what it added, so a file it had replaced in place would stay the newer
// version beside the older ones; a file renamed aside is moved back instead. Where the
// install then fails, Setup deletes what it wrote and DeinitializeSetup moves each file
// back; where it completes, the files held aside are deleted, and one still running is
// deleted at the next start of the copy at that path, or by the uninstaller.
// HoldAside is called once for each [Files] destination in {app}, and
// scripts/check-setup-held-copies.mjs fails where one is missing.
var
  HeldFrom: array of String;
  HeldAt: array of String;
  Installed: Boolean;

// The Nth name a file at Path can be held aside under.
function HeldName(const Path: String; N: Integer): String;
begin
  if N = 0 then
    Result := Path + '{#HeldSuffix}'
  else
    Result := Path + '{#HeldSuffix}-' + IntToStr(N);
end;

// The first held name for Path with nothing at it, or '' where all are taken. A name
// already in use, left by an earlier setup, is never reused: the copy at the path may
// be deleting it as it starts, and a file held aside under it would go with it.
function FreeHeldName(const Path: String): String;
var
  N: Integer;
begin
  Result := '';
  for N := 0 to 99 do
    if not FileOrDirExists(HeldName(Path, N)) then
    begin
      Result := HeldName(Path, N);
      Exit;
    end;
end;

// Renames the file Name in {app} aside, where there is one. Answers False where it is
// there and could not be renamed.
function HoldAside(const Name: String): Boolean;
var
  Path, Held: String;
  I: Integer;
begin
  Result := True;
  Path := ExpandConstant('{app}\') + Name;
  if not FileExists(Path) then Exit;
  Held := FreeHeldName(Path);
  if Held = '' then
  begin
    Log(Format('No free name to hold %s aside under.', [Path]));
    Result := False;
    Exit;
  end;
  if not RenameFile(Path, Held) then
  begin
    Log(Format('Could not rename %s to %s: %s', [Path, Held, SysErrorMessage(DLLGetLastError)]));
    Result := False;
    Exit;
  end;
  Log(Format('Held %s aside as %s.', [Path, Held]));
  I := GetArrayLength(HeldFrom);
  SetArrayLength(HeldFrom, I + 1);
  SetArrayLength(HeldAt, I + 1);
  HeldFrom[I] := Path;
  HeldAt[I] := Held;
end;

// Moves each file held aside back to its name, deleting whatever Setup left there.
// Every failure is written to the setup log.
procedure PutHeldBack;
var
  I: Integer;
begin
  for I := GetArrayLength(HeldAt) - 1 downto 0 do
  begin
    if FileExists(HeldFrom[I]) and not DeleteFile(HeldFrom[I]) then
      Log(Format('Could not delete %s to put the earlier file back: %s. The earlier file is left as %s.',
        [HeldFrom[I], SysErrorMessage(DLLGetLastError), HeldAt[I]]))
    else if RenameFile(HeldAt[I], HeldFrom[I]) then
      Log(Format('Put %s back as %s.', [HeldAt[I], HeldFrom[I]]))
    else
      Log(Format('Could not rename %s back to %s: %s. The earlier file is left as %s.',
        [HeldAt[I], HeldFrom[I], SysErrorMessage(DLLGetLastError), HeldAt[I]]));
  end;
  SetArrayLength(HeldFrom, 0);
  SetArrayLength(HeldAt, 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  I: Integer;
  Held: Boolean;
begin
  if CurStep = ssInstall then
  begin
    Held := HoldAside('InstallerClean.exe');
    if Held then Held := HoldAside('installerclean-cli.exe');
    if Held then Held := HoldAside('LICENSE.txt');
    if Held then Held := HoldAside('THIRD-PARTY-NOTICES.txt');
    if not Held then
    begin
      // Nothing has been written yet, so putting back what moved leaves {app} as it was.
      PutHeldBack;
      Abort;
    end;
  end
  else if CurStep = ssPostInstall then
  begin
    Installed := True;
    for I := 0 to GetArrayLength(HeldAt) - 1 do
      if not DeleteFile(HeldAt[I]) then
        Log(Format('Could not delete %s: %s. It is left for the next start.', [HeldAt[I], SysErrorMessage(DLLGetLastError)]));
  end;
end;

// Called however Setup ends, after a failed install has been rolled back.
procedure DeinitializeSetup;
begin
  if not Installed then PutHeldBack;
end;
