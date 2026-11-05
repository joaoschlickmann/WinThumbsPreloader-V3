#define MyAppName "WinThumbsPreloader"
#define MyAppReleaseDirectory "..\WinThumbsPreloader\WinThumbsPreloader\bin\Release\net10.0-windows7.0\win-x64"
#define MyAppFilename MyAppName + ".exe"
#define MyAppFilepath MyAppReleaseDirectory + "\" + MyAppFilename
#define MyAppConfig  MyAppReleaseDirectory + "\" + MyAppName
#dim Version[4]
#expr GetVersionComponents(MyAppFilepath, Version[0], Version[1], Version[2], Version[3])
#define MyAppVersion Str(Version[0]) + "." + Str(Version[1]) + "." + Str(Version[2])
#define MyAppPublisher1 "Dmitry Bruhov"
#define MyAppPublisher2 "Mutahar Farooq"
#define MyAppPublisher3 "João Schlickmann"
#define MyAppId "CF49DD18-AA76-4E79-97C2-4FEAED1AED5F"

[Setup]
AppCopyright=Copyright (c) 2018 {#MyAppPublisher1}, Copyright (c) 2026 {#MyAppPublisher2}, {#MyAppPublisher3}
AppId={#MyAppId}
AppMutex={#MyAppId}
AppName={#MyAppName}
AppPublisher={#MyAppPublisher1}, {#MyAppPublisher2}, {#MyAppPublisher3}
AppPublisherURL=https://github.com/joaoschlickmann/WinThumbsPreloader-V3
AppSupportURL=https://github.com/joaoschlickmann/WinThumbsPreloader-V3/issues
AppUpdatesURL=https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases
AppVerName={#MyAppName} {#MyAppVersion}
AppVersion={#MyAppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
DefaultDirName={commonpf}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=admin
UsedUserAreasWarning=no
DisableDirPage=yes
UsePreviousAppDir=no
DirExistsWarning=no
DisableReadyPage=yes
DisableProgramGroupPage=yes
LicenseFile=license.txt
MinVersion=10.0
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-setup
OutputDir=Output
ShowLanguageDialog=no
UninstallDisplayIcon={app}\{#MyAppFilename}
UninstallDisplayName={#MyAppName}
VersionInfoTextVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
WizardImageFile=WizardImageFile.bmp
WizardImageStretch=no
WizardSmallImageFile=WizardSmallImageFile.bmp
SolidCompression=yes
Compression=lzma2/max
CloseApplications=force
CloseApplicationsFilter=*.exe,*.dll

[Languages]
Name: en; MessagesFile: "compiler:Default.isl"
Name: ru; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
; Context menu items
en.PreloadThumbnails=Preload thumbnails
ru.PreloadThumbnails=Загрузить эскизы

en.PreloadThumbnailsRecursively=Preload thumbnails recursively
ru.PreloadThumbnailsRecursively=Загрузить эскизы включая подпапки

[Files]
; Core app files
Source: "{#MyAppFilepath}";               DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppConfig}.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppConfig}.deps.json";       DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppConfig}.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppConfig}.dll.config";    DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppReleaseDirectory}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppReleaseDirectory}\ru\*.resources.dll"; DestDir: "{app}\Languages\ru"; Flags: ignoreversion
Source: "{#MyAppReleaseDirectory}\Microsoft.Win32.TaskScheduler.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{commonprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppFilename}"

[Run]
Filename: "{app}\{#MyAppFilename}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall shellexec; Check: not CmdLineParamExists('/AUTOLAUNCHMINIMIZED')
Filename: "{app}\{#MyAppFilename}"; Parameters: "-startminimized"; Flags: nowait postinstall shellexec skipifnotsilent; Check: CmdLineParamExists('/AUTOLAUNCHMINIMIZED')

[Registry]
; ---- Remove any stale per-user HKCU context menu overrides so canonical HKCR/HKLM entries take precedence ----
Root: "HKCU"; Subkey: "Software\Classes\Directory\shell\{#MyAppName}"; Flags: dontcreatekey deletekey
Root: "HKCU"; Subkey: "Software\Classes\Directory\Background\shell\{#MyAppName}"; Flags: dontcreatekey deletekey
Root: "HKCU"; Subkey: "Software\Classes\Directory\Background\ContextMenus\Menu{#MyAppName}"; Flags: dontcreatekey deletekey
Root: "HKCU"; Subkey: "Software\Classes\Drive\shell\{#MyAppName}"; Flags: dontcreatekey deletekey

; ---- Directory context menu ----
Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}"; Flags: uninsdeletekey
Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{#MyAppName}"
Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppFilename}"",0"
Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}"; ValueType: string; ValueName: "ExtendedSubCommandsKey"; ValueData: "Directory\shell\{#MyAppName}"
Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}\Shell"

Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}\Shell\Preload"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:PreloadThumbnails}"
Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}\Shell\Preload"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppFilename}"",0"
Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}\Shell\Preload\command"; ValueType: string; ValueData: """{app}\{#MyAppFilename}"" -m ""%V"""

Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}\Shell\PreloadRecursively"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:PreloadThumbnailsRecursively}"
Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}\Shell\PreloadRecursively"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppFilename}"",0"
Root: "HKCR"; Subkey: "Directory\shell\{#MyAppName}\Shell\PreloadRecursively\command"; ValueType: string; ValueData: """{app}\{#MyAppFilename}"" -m -r ""%V"""

; ---- Directory background context menu ----
Root: "HKCR"; Subkey: "Directory\Background\shell\{#MyAppName}"; Flags: uninsdeletekey
Root: "HKCR"; Subkey: "Directory\Background\shell\{#MyAppName}"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{#MyAppName}"
Root: "HKCR"; Subkey: "Directory\Background\shell\{#MyAppName}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppFilename}"",0"
Root: "HKCR"; Subkey: "Directory\Background\shell\{#MyAppName}"; ValueType: string; ValueName: "ExtendedSubCommandsKey"; ValueData: "Directory\Background\ContextMenus\Menu{#MyAppName}"

Root: "HKCR"; Subkey: "Directory\Background\ContextMenus\Menu{#MyAppName}"; Flags: uninsdeletekey
Root: "HKCR"; Subkey: "Directory\Background\ContextMenus\Menu{#MyAppName}\shell\Preload"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:PreloadThumbnails}"
Root: "HKCR"; Subkey: "Directory\Background\ContextMenus\Menu{#MyAppName}\shell\Preload"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppFilename}"",0"
Root: "HKCR"; Subkey: "Directory\Background\ContextMenus\Menu{#MyAppName}\shell\Preload\command"; ValueType: string; ValueData: """{app}\{#MyAppFilename}"" -m ""%V"""

Root: "HKCR"; Subkey: "Directory\Background\ContextMenus\Menu{#MyAppName}\shell\PreloadRecursively"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:PreloadThumbnailsRecursively}"
Root: "HKCR"; Subkey: "Directory\Background\ContextMenus\Menu{#MyAppName}\shell\PreloadRecursively"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppFilename}"",0"
Root: "HKCR"; Subkey: "Directory\Background\ContextMenus\Menu{#MyAppName}\shell\PreloadRecursively"; ValueType: string; ValueName: "HasLUAShield"; ValueData: ""
Root: "HKCR"; Subkey: "Directory\Background\ContextMenus\Menu{#MyAppName}\shell\PreloadRecursively\command"; ValueType: string; ValueData: """{app}\{#MyAppFilename}"" -m -r ""%V"""

; ---- Drive context menu ----
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}"; Flags: uninsdeletekey
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{#MyAppName}"
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppFilename}"",0"
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}"; ValueType: string; ValueName: "ExtendedSubCommandsKey"; ValueData: "Drive\shell\{#MyAppName}"
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}\Shell"

Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}\Shell\Preload"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:PreloadThumbnails}"
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}\Shell\Preload"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppFilename}"",0"
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}\Shell\Preload\command"; ValueType: string; ValueData: """{app}\{#MyAppFilename}"" -m ""%V"""
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}\Shell\PreloadRecursively"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:PreloadThumbnailsRecursively}"
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}\Shell\PreloadRecursively"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppFilename}"",0"
Root: "HKCR"; Subkey: "Drive\shell\{#MyAppName}\Shell\PreloadRecursively\command"; ValueType: string; ValueData: """{app}\{#MyAppFilename}"" -m -r ""%V"""

Root: "HKLM"; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Check: not IsInPath(ExpandConstant('{app}'))

; ---- Prevent Windows Automatic Maintenance (SilentCleanup) from wiping the thumbnail cache ----
Root: "HKLM"; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache"; ValueType: dword; ValueName: "Autorun"; ValueData: "0"
Root: "HKLM"; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache"; ValueType: dword; ValueName: "Autorun"; ValueData: "0"
Root: "HKCU"; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache"; ValueType: dword; ValueName: "Autorun"; ValueData: "0"

; ---- Start with Windows by default (minimized to system tray) ----
Root: "HKLM"; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#MyAppName}"; Flags: dontcreatekey deletevalue
Root: "HKCU"; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppFilename}"" -startminimized"; Flags: uninsdeletevalue

[Code]
const
  EVENT_MODIFY_STATE = $0002;

function OpenEventW(dwDesiredAccess: Cardinal; bInheritHandle: BOOL; lpName: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

procedure SignalRunningInstanceShutdown();
var
  EventHandle: THandle;
begin
  EventHandle := OpenEventW(EVENT_MODIFY_STATE, False, 'Local\{#MyAppName}_{#MyAppId}_Shutdown');
  if EventHandle <> 0 then
  begin
    SetEvent(EventHandle);
    CloseHandle(EventHandle);
    Sleep(250);
  end;
end;

function CmdLineParamExists(const Value: string): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function NormalizeDirPath(const DirPath: string): string;
begin
  Result := RemoveBackslashUnlessRoot(Trim(RemoveQuotes(DirPath)));
end;

procedure CleanupAlternateDirectory(const CandidateDir, CanonicalDir: string);
var
  NormCandidate, NormCanonical, UninsExe: string;
  ResultCode: Integer;
begin
  NormCandidate := NormalizeDirPath(CandidateDir);
  NormCanonical := NormalizeDirPath(CanonicalDir);

  if (NormCandidate = '') or (Length(NormCandidate) <= 3) or (CompareText(NormCandidate, NormCanonical) = 0) then
    Exit;

  if DirExists(NormCandidate) then
  begin
    UninsExe := AddBackslash(NormCandidate) + 'unins000.exe';
    if FileExists(UninsExe) then
      Exec(UninsExe, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    DelTree(NormCandidate, True, True, True);
  end;
end;

procedure CheckAndRemoveUninstallLocation(const RootKey: Integer; const SubKey, CanonicalDir: string);
var
  OldDir: string;
begin
  if RegQueryStringValue(RootKey, SubKey, 'InstallLocation', OldDir) then
    CleanupAlternateDirectory(OldDir, CanonicalDir);

  if RegQueryStringValue(RootKey, SubKey, 'Inno Setup: App Path', OldDir) then
    CleanupAlternateDirectory(OldDir, CanonicalDir);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  CanonicalDir: string;
begin
  CanonicalDir := ExpandConstant('{app}');

  { 1. Signal running v3.2.0+ GUI instance via named IPC event so tray icon is removed without spawning old binaries }
  SignalRunningInstanceShutdown();

  { 2. Force-terminate any remaining preloader/GUI processes (including legacy v3.1.x instances) }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM "{#MyAppFilename}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { 3. Remove any previous installation located in a different directory }
  CheckAndRemoveUninstallLocation(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1', CanonicalDir);
  CheckAndRemoveUninstallLocation(HKLM32, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1', CanonicalDir);
  CheckAndRemoveUninstallLocation(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1', CanonicalDir);
  CheckAndRemoveUninstallLocation(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppName}_is1', CanonicalDir);

  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppName}_is1');

  { 4. Remove non-canonical per-user or 32-bit installations }
  CleanupAlternateDirectory(ExpandConstant('{localappdata}\Programs\{#MyAppName}'), CanonicalDir);
  CleanupAlternateDirectory(ExpandConstant('{commonpf32}\{#MyAppName}'), CanonicalDir);

  { 5. Remove duplicate per-user Start Menu or Desktop shortcuts }
  DeleteFile(ExpandConstant('{userprograms}\{#MyAppName}.lnk'));
  DelTree(ExpandConstant('{userprograms}\{#MyAppName}'), True, True, True);
  DeleteFile(ExpandConstant('{userdesktop}\{#MyAppName}.lnk'));

  Result := '';
end;

function IsInPath(Dir: string): Boolean;
var
  S: string;
begin
  if RegQueryStringValue(HKLM,
     'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
     'Path', S) then
  begin
    S := ';' + Uppercase(S) + ';';
    Result := Pos(';' + Uppercase(Dir) + ';', S) > 0;
  end
  else
    Result := False;
end;
