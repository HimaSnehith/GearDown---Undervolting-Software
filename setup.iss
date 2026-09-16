; Inno Setup Script for GearDown
; Configured for 100% Pure Cleanup on Uninstall (No leftover files, folders, or AppData traces)

[Setup]
AppId={D37E84B1-3E2A-4F90-85F0-A1E297394D23}
AppName=GearDown
AppVersion=3.0
AppPublisher=GearDown Software
DefaultDirName={autopf}\GearDown
DefaultGroupName=GearDown
UninstallDisplayIcon={app}\GearDown.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
OutputBaseFilename=GearDown_Setup_v2.0
OutputDir=InstallerOutput

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Copy all binaries, assemblies, and wwwroot assets from publish directory
Source: "bin\Release\net8.0-windows\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GearDown"; Filename: "{app}\GearDown.exe"
Name: "{autodesktop}\GearDown"; Filename: "{app}\GearDown.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GearDown.exe"; Description: "{cm:LaunchProgram,GearDown}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Delete all leftover app files, AppData settings, and LocalAppData WebView2 cache
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{userappdata}\GearDown"
Type: filesandordirs; Name: "{localappdata}\GearDown"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Pure cleanup: Remove installation folder and all appdata/localappdata user data
    DelTree(ExpandConstant('{app}'), True, True, True);
    DelTree(ExpandConstant('{userappdata}\GearDown'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\GearDown'), True, True, True);
  end;
end;
