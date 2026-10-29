; VPB 6 production installer
#define MyAppName "Virtual Piano Bot 6"
#define MyAppVersion "6.0.1"
#define MyAppPublisher "Virtual Piano Bot"
#define MyAppExeName "VPB6.exe"
#define StudioExeName "VPB6Studio.exe"

[Setup]
AppId={{9F1F1D55-2A5A-4E4D-9B9E-2A1B9A600006}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Virtual Piano Bot 6
DefaultGroupName={#MyAppName}
OutputDir=..\release
OutputBaseFilename=VirtualPianoBot-6.0.1-Setup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\release\app\VPB6.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\release\app\runtime\VPB6Studio.exe"; DestDir: "{app}\runtime"; Flags: ignoreversion; Attribs: hidden
Source: "..\release\app\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\release\app\updates.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Virtual Piano Bot 6"; Filename: "{app}\VPB6.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Virtual Piano Bot 6"; Filename: "{app}\VPB6.exe"; WorkingDir: "{app}"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
Filename: "{app}\VPB6.exe"; Description: "Launch Virtual Piano Bot 6"; Flags: nowait postinstall skipifsilent

