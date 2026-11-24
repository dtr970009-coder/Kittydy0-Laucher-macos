; Script Inno Setup cho KittyDy0 Launcher
[Setup]
AppName=KittyDy0 Launcher
AppVersion=1.0.0
AppPublisher=KittyDy0 Network
DefaultDirName={autopf}\KittyDy0 Launcher
DefaultGroupName=KittyDy0 Launcher
OutputDir=Output
OutputBaseFilename=KittyDy0Launcher-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
SetupIconFile=Assets\icon.ico
UninstallDisplayIcon={app}\icon.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "publish\KittyDy0Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Assets\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\KittyDy0 Launcher"; Filename: "{app}\KittyDy0Launcher.exe"; IconFilename: "{app}\icon.ico"
Name: "{autodesktop}\KittyDy0 Launcher"; Filename: "{app}\KittyDy0Launcher.exe"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\KittyDy0Launcher.exe"; Description: "Open KittyDy0 Launcher"; Flags: nowait postinstall skipifsilent
