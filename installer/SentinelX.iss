; Per-user installer. User data lives outside {app}; never delete it on uninstall.
#ifndef AppVersion
  #define AppVersion "0.85.0"
#endif
[Setup]
AppId={{8A74FE2D-DA3A-48B5-9A88-20C17E983E53}
AppName=SentinelX
AppVersion={#AppVersion}
AppPublisher=SentinelX
AppPublisherURL=https://github.com/pawelaachi123-eng/SentinelX
DefaultDirName={localappdata}\Programs\SentinelX
DefaultGroupName=SentinelX
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=yes
OutputDir=..\bin\installer
OutputBaseFilename=SentinelX-Setup-{#AppVersion}-win-x64
SetupIconFile=..\Assets\sentinel.ico
UninstallDisplayIcon={app}\SentinelX.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SentinelX"; Filename: "{app}\SentinelX.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\SentinelX"; Filename: "{app}\SentinelX.exe"; WorkingDir: "{app}"; Tasks: desktopicon
