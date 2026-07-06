#define AppName "WSLC Desktop"
#define AppPublisher "yuWorm"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\dist\wslc-desktop"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts\release"
#endif
#ifndef OutputBaseFilename
#define OutputBaseFilename "wslc-desktop-setup"
#endif
#ifndef InstallerArchitecturesAllowed
#define InstallerArchitecturesAllowed "x64compatible"
#endif
#ifndef InstallerArchitecturesInstallIn64BitMode
#define InstallerArchitecturesInstallIn64BitMode "x64compatible"
#endif

[Setup]
AppId={{90C679B9-C199-4538-B2D2-871801D15B4B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\WSLC Desktop
DefaultGroupName=WSLC Desktop
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\Assets\AppIcon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed={#InstallerArchitecturesAllowed}
ArchitecturesInstallIn64BitMode={#InstallerArchitecturesInstallIn64BitMode}
ChangesEnvironment=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "Add WSLC Desktop bin directory to the current user's PATH"; GroupDescription: "Command-line integration:"; Flags: unchecked

[Dirs]
Name: "{app}\bin"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WSLC Desktop"; Filename: "{app}\wslc-desktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\WSLC Desktop"; Filename: "{app}\wslc-desktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\bin"; Check: NeedsAddPath(ExpandConstant('{app}\bin')); Tasks: addtopath

[Run]
Filename: "{app}\wslc-desktop.exe"; Description: "{cm:LaunchProgram,WSLC Desktop}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsAddPath(PathEntry: string): Boolean;
var
  ExistingPath: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', ExistingPath) then
  begin
    Result := True;
    exit;
  end;

  Result := Pos(';' + Uppercase(PathEntry) + ';', ';' + Uppercase(ExistingPath) + ';') = 0;
end;
