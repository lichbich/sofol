#define MyAppName "Sofol"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Sofol"
#define MyAppURL "https://github.com/trong/Sofol"
#define MyAppExeName "Sofol.App.exe"

[Setup]
AppId={{8B41FE19-D985-4E7B-9E3E-74B01E2E9152}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=SofolSetup
SetupIconFile=Sofol.App\Assets\AppLogo.ico
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenu"; Description: "Tạo biểu tượng trong Menu Start"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Tự động khởi động cùng Windows (chạy ngầm dưới khay)"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "dist\Sofol-v{#MyAppVersion}-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Sofol"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu; IconFilename: "{app}\Assets\AppLogo.ico"
Name: "{autodesktop}\Sofol"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\Assets\AppLogo.ico"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Sofol"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
