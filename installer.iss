#define MyAppName "My Clinic"
#define MyAppVersion "1.3"
#define MyAppPublisher "Dr. Ahmed Khalif"
#define MyAppExeName "MyClinic.exe"
; Notice the path is updated to match your .NET 9 Windows target framework
#define MyPublishFolder "bin\Release\net9.0-windows\win-x64\publish"

[Setup]
; AppId uniquely identifies this application. Do not use this AppId for other apps.
AppId={{1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputBaseFilename=Install_MyClinic
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin

; This will use your app's logo for the setup wizard icon itself!
SetupIconFile=Assets\logo.ico
UninstallDisplayIcon={app}\Assets\logo.ico

[Languages]
; Adds Arabic and English to the installer wizard
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; This grabs the MyClinic.exe, all the DLLs, the Google API credentials.json, and the Assets folder
Source: "{#MyPublishFolder}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Assets\logo.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; The application database is stored outside the install directory at:
; %LOCALAPPDATA%\MyClinicApp\ClinicData.db
; This installer does not include, copy, overwrite, or delete that database.
