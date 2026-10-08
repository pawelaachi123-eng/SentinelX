; Per-user installer for the WinUI 3 shell. User data lives outside {app}; never delete it on uninstall.
; During the migration period this installs next to the classic WPF build (separate folder, separate AppId).
#ifndef AppVersion
  #define AppVersion "0.96.0"
#endif
[Setup]
AppId={{57C43B63-7177-4F9B-93D4-7CD8C0ACA298}
AppName=SentinelX
AppVersion={#AppVersion}
AppPublisher=SentinelX
AppPublisherURL=https://github.com/pawelaachi123-eng/SentinelX
DefaultDirName={localappdata}\Programs\SentinelX.WinUI
DefaultGroupName=SentinelX WinUI
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=yes
OutputDir=..\bin\installer-winui
OutputBaseFilename=SentinelX-WinUI-Setup-{#AppVersion}-win-x64
SetupIconFile=..\SentinelX.WinUI\Assets\sentinel.ico
UninstallDisplayIcon={app}\SentinelX.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\portable-winui\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SentinelX"; Filename: "{app}\SentinelX.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\SentinelX"; Filename: "{app}\SentinelX.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\SentinelX.exe"; Description: "{cm:LaunchProgram,SentinelX}"; Flags: nowait postinstall skipifsilent
