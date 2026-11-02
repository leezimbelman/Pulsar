; Inno Setup script for Pulsar. Built by .github/workflows/release.yml:
;   iscc /DAppVersion=1.2.3 /DSourceDir=..\publish installer\Pulsar.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{6C1F3E2A-7B4D-4F59-9E61-1D2C3B4A5F70}
AppName=Pulsar
AppVersion={#AppVersion}
AppVerName=Pulsar {#AppVersion}
AppPublisher=Pulsar contributors
AppPublisherURL=https://github.com/leezimbelman/Pulsar
DefaultDirName={autopf}\Pulsar
DefaultGroupName=Pulsar
DisableProgramGroupPage=yes
; Per-user install by default (no admin prompt); users can choose all-users in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=Pulsar-Setup-{#AppVersion}-x64
SetupIconFile=..\Pulsar\Assets\Pulsar.ico
UninstallDisplayIcon={app}\Pulsar.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "sendto"; Description: "Add Pulsar to Explorer's ""Send to"" menu"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Pulsar"; Filename: "{app}\Pulsar.exe"
Name: "{autodesktop}\Pulsar"; Filename: "{app}\Pulsar.exe"; Tasks: desktopicon
Name: "{usersendto}\Pulsar Player"; Filename: "{app}\Pulsar.exe"; Parameters: "--play"; Tasks: sendto
Name: "{usersendto}\Pulsar Tools"; Filename: "{app}\Pulsar.exe"; Parameters: "--tools"; Tasks: sendto

[Run]
Filename: "{app}\Pulsar.exe"; Description: "Launch Pulsar"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{usersendto}\Pulsar Player.lnk"
Type: files; Name: "{usersendto}\Pulsar Tools.lnk"

[Registry]
; Remove the "start with Windows" entry on uninstall if the user turned it on in Settings.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Pulsar"; Flags: uninsdeletevalue dontcreatekey
