#ifndef AppVersion
  #define AppVersion "1.4.1"
#endif
#ifndef PackageDir
  #define PackageDir "..\artifacts\publish-win-x64"
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\artifacts\release"
#endif
#ifndef PackageSuffix
  #define PackageSuffix "win-x64"
#endif

[Setup]
AppId={{F57BA096-F7CF-4595-9E62-C16926254F84}
AppName=AKASS Optimizer
AppVersion={#AppVersion}
AppPublisher=Ahmed Kassab
AppPublisherURL=https://akass.dev
AppSupportURL=https://akass.dev
AppUpdatesURL=https://github.com/AhmedKassab287/AKASS-OPTIMIZER/releases/latest
DefaultDirName={localappdata}\Programs\AKASS Optimizer
UsePreviousAppDir=yes
UsePreviousTasks=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
SetupIconFile=..\assets\akass.ico
UninstallDisplayIcon={app}\AKASS.Optimizer.exe
AppMutex=Local\AKASS.Optimizer.InUse
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir={#ReleaseDir}
OutputBaseFilename=AKASS-Optimizer-{#AppVersion}-{#PackageSuffix}-setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Icons]
Name: "{userprograms}\AKASS Optimizer"; Filename: "{app}\AKASS.Optimizer.exe"; WorkingDir: "{app}"; IconFilename: "{app}\AKASS.Optimizer.exe"
Name: "{userdesktop}\AKASS Optimizer"; Filename: "{app}\AKASS.Optimizer.exe"; WorkingDir: "{app}"; IconFilename: "{app}\AKASS.Optimizer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AKASS.Optimizer.exe"; Description: "{cm:LaunchProgram,AKASS Optimizer}"; Flags: nowait postinstall skipifsilent unchecked
Filename: "{app}\AKASS.Optimizer.exe"; WorkingDir: "{app}"; Flags: nowait skipifnotsilent; Check: ReopenAfterUpdate

[UninstallRun]
Filename: "{app}\AKASS.Optimizer.exe"; Parameters: "--uninstall"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterAkassMaintenance"

[Code]
function ReopenAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:AKASSUPDATE|0}') = '1';
end;

#ifdef FrameworkFallback
function InitializeSetup: Boolean;
var
  ReleaseValue: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', ReleaseValue) and
    (ReleaseValue >= 528040);
  if not Result then
    SuppressibleMsgBox('Install Microsoft .NET Framework 4.8 or later, or use the self-contained AKASS Optimizer build.', mbError, MB_OK, IDOK);
end;
#endif
