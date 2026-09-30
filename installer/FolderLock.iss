# FolderLock installer script (Inno Setup 6)
# First run: dotnet publish src\FolderLock.App\FolderLock.App.csproj -c Release -p:PublishProfile=win-x64
# Then open this file in the Inno Setup Compiler, or from the command line: ISCC.exe installer\FolderLock.iss

#define AppName "FolderLock"
#define AppVersion "1.0.0"
#define AppPublisher "FolderLock"
#define AppExeName "FolderLock.App.exe"
#define PublishDir "..\src\FolderLock.App\bin\Release\net8.0-windows\publish\win-x64"

[Setup]
AppId={{9B2E4C1F-4B7A-4C3E-9E5D-6A1C2F8B0D31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=FolderLock-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional tasks:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--uninstall-shell"; Flags: runhidden; RunOnceId: "CleanupShellMenu"
