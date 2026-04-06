#ifndef AppVersion
#define AppVersion "0.0.0"
#endif

#ifndef SourceDir
#define SourceDir "."
#endif

#ifndef OutputDir
#define OutputDir "."
#endif

#ifndef OutputBaseFilename
#define OutputBaseFilename "novalist-windows-x64-setup"
#endif

#ifndef InstallerIconFile
#define InstallerIconFile "..\..\..\Novalist.Desktop\novalist.ico"
#endif

[Setup]
AppId={{4F96BF95-1D05-4F60-93DD-F3C3F564A845}
AppName=Novalist
AppVersion={#AppVersion}
AppVerName=Novalist {#AppVersion}
DefaultDirName={autopf64}\Novalist
DefaultGroupName=Novalist
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#InstallerIconFile}
UninstallDisplayIcon={app}\Novalist.Desktop.exe

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Novalist"; Filename: "{app}\Novalist.Desktop.exe"
Name: "{autodesktop}\Novalist"; Filename: "{app}\Novalist.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Novalist.Desktop.exe"; Description: "Launch Novalist"; Flags: nowait postinstall skipifsilent