#define MyAppName "OixNodeHelper"
#define MyAppVersion "0.3.0"
#define MyAppPublisher "OixNodeHelper"
#define MyAppExeName "oix_node_helper.exe"

[Setup]
AppId={{9A391182-FAB3-492C-8331-3949C558472D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\OixNodeHelper
DefaultGroupName=OixNodeHelper
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\build
OutputBaseFilename=OixNodeHelper-Flutter-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimp"; MessagesFile: ".\ChineseSimplified.isl"

[Files]
Source: "..\app\build\windows\x64\runner\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\config\flclash-provider.yaml.example"; DestDir: "{app}\config"; Flags: ignoreversion
Source: ".\ChineseSimplified.LICENSE"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\OixNodeHelper"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\OixNodeHelper"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 OixNodeHelper"; Flags: nowait postinstall skipifsilent
