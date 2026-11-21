#ifndef MyAppVersion
  #define MyAppVersion "1.1.1"
#endif

#define MyAppName "LivGuard"
#define MyAppPublisher "Flaviofdsx"

[Setup]
AppId={{A3D18C4E-7811-4B4A-A9E8-E7E85E44A16C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\LivGuard
DefaultGroupName=LivGuard
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Flavirus.UI\app.ico
InfoBeforeFile=install-info.txt
UninstallDisplayIcon={app}\Flavirus.UI.exe
UninstallDisplayName=LivGuard
OutputDir=..\artifacts\installer
OutputBaseFilename=LivGuard-Setup-{#MyAppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
Uninstallable=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\Flavirus.UI\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\LivGuard"; Filename: "{app}\Flavirus.UI.exe"; WorkingDir: "{app}"; Comment: "Abrir o LivGuard"
Name: "{userdesktop}\LivGuard"; Filename: "{app}\Flavirus.UI.exe"; WorkingDir: "{app}"; Tasks: desktopicon
