#ifndef AppVersion
  #define AppVersion "0.5.2"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\MedNote-Reader-Windows"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{B78AA6F8-89EE-41D8-A697-D90741A9A050}
AppName=MedNote Reader
AppVersion={#AppVersion}
AppVerName=MedNote Reader {#AppVersion}
AppPublisher=MedNote
AppPublisherURL=https://github.com/madness1997-gif/mednote_windows
AppSupportURL=https://github.com/madness1997-gif/mednote_windows/issues
AppUpdatesURL=https://github.com/madness1997-gif/mednote_windows/releases/tag/windows-preview
DefaultDirName={localappdata}\Programs\MedNote Reader
DefaultGroupName=MedNote Reader
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=MedNote-Reader-Setup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
Uninstallable=yes
UninstallDisplayIcon={app}\MedNote.Reader.exe
UninstallDisplayName=MedNote Reader
VersionInfoCompany=MedNote
VersionInfoDescription=MedNote Reader Windows Installer
VersionInfoProductName=MedNote Reader
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\MedNote Reader"; Filename: "{app}\MedNote.Reader.exe"; WorkingDir: "{app}"; Comment: "Open MedNote Reader"
Name: "{userdesktop}\MedNote Reader"; Filename: "{app}\MedNote.Reader.exe"; WorkingDir: "{app}"; Comment: "Open MedNote Reader"; Tasks: desktopicon

[Run]
Filename: "{app}\MedNote.Reader.exe"; Description: "Launch MedNote Reader"; Flags: nowait postinstall skipifsilent
