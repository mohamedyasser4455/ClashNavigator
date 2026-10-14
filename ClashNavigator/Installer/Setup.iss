; Clash Navigator - one installer for Revit 2022 to 2026.
;
; There is no {app} folder: a Revit add-in belongs next to Revit's own add-in manifests.
; For every Revit version found on the machine we drop
;     ...\Addins\<version>\ClashNavigator.addin          (the manifest)
;     ...\Addins\<version>\ClashNavigator\ClashNavigator.dll   (the build for that version)
; The 2022-2024 DLLs are .NET Framework 4.8 builds, the 2025-2026 DLLs are .NET 8 builds.

#define AppName        "Clash Navigator"
#define AppVersion     "1.0.0"
#define Publisher      "Mohamed Yasser"
#define PublisherUrl   "https://www.linkedin.com/in/mohamed-y-khalil"
#define AddinsRoot     "{commonappdata}\Autodesk\Revit\Addins"

[Setup]
AppId={{C1CDF251-EECD-45B1-815E-88349A48DA47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL={#PublisherUrl}
VersionInfoVersion={#AppVersion}
CreateAppDir=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
; Revit is 64-bit only. "x64compatible" is what current Inno Setup versions expect -
; the older "x64" spelling still works but is deprecated and prints a warning.
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=KT-BD-R11-GP-Mohamed Yasser-ClashNavigator-Setup
SetupIconFile=..\Resources\AppIcon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; ---------- Revit 2022 (.NET Framework 4.8) ----------
Source: "..\bin\R22\ClashNavigator.dll"; DestDir: "{#AddinsRoot}\2022\ClashNavigator"; Flags: ignoreversion skipifsourcedoesntexist; Check: ShouldInstall('2022')
Source: "ClashNavigator.addin";          DestDir: "{#AddinsRoot}\2022";                Flags: ignoreversion; Check: ShouldInstall('2022')

; ---------- Revit 2023 (.NET Framework 4.8) ----------
Source: "..\bin\R23\ClashNavigator.dll"; DestDir: "{#AddinsRoot}\2023\ClashNavigator"; Flags: ignoreversion skipifsourcedoesntexist; Check: ShouldInstall('2023')
Source: "ClashNavigator.addin";          DestDir: "{#AddinsRoot}\2023";                Flags: ignoreversion; Check: ShouldInstall('2023')

; ---------- Revit 2024 (.NET Framework 4.8) ----------
Source: "..\bin\R24\ClashNavigator.dll"; DestDir: "{#AddinsRoot}\2024\ClashNavigator"; Flags: ignoreversion skipifsourcedoesntexist; Check: ShouldInstall('2024')
Source: "ClashNavigator.addin";          DestDir: "{#AddinsRoot}\2024";                Flags: ignoreversion; Check: ShouldInstall('2024')

; ---------- Revit 2025 (.NET 8) ----------
Source: "..\bin\R25\ClashNavigator.dll"; DestDir: "{#AddinsRoot}\2025\ClashNavigator"; Flags: ignoreversion skipifsourcedoesntexist; Check: ShouldInstall('2025')
Source: "ClashNavigator.addin";          DestDir: "{#AddinsRoot}\2025";                Flags: ignoreversion; Check: ShouldInstall('2025')

; ---------- Revit 2026 (.NET 8) ----------
Source: "..\bin\R26\ClashNavigator.dll"; DestDir: "{#AddinsRoot}\2026\ClashNavigator"; Flags: ignoreversion skipifsourcedoesntexist; Check: ShouldInstall('2026')
Source: "ClashNavigator.addin";          DestDir: "{#AddinsRoot}\2026";                Flags: ignoreversion; Check: ShouldInstall('2026')

[UninstallDelete]
Type: dirifempty; Name: "{#AddinsRoot}\2022\ClashNavigator"
Type: dirifempty; Name: "{#AddinsRoot}\2023\ClashNavigator"
Type: dirifempty; Name: "{#AddinsRoot}\2024\ClashNavigator"
Type: dirifempty; Name: "{#AddinsRoot}\2025\ClashNavigator"
Type: dirifempty; Name: "{#AddinsRoot}\2026\ClashNavigator"

[Code]
var
  InstallForAllVersions: Boolean;

// A version counts as present if Revit.exe is there, or if Revit has already created its
// add-ins folder for that year (true for network / custom installs too).
function RevitDetected(Version: String): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf}\Autodesk\Revit ' + Version + '\Revit.exe'))
         or DirExists(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version));
end;

function ShouldInstall(Version: String): Boolean;
begin
  Result := InstallForAllVersions or RevitDetected(Version);
end;

function InitializeSetup(): Boolean;
var
  AnyFound: Boolean;
begin
  AnyFound := RevitDetected('2022') or RevitDetected('2023') or RevitDetected('2024')
           or RevitDetected('2025') or RevitDetected('2026');

  // Nothing detected is not a reason to fail - register everywhere and let Revit ignore
  // the years it does not have.
  InstallForAllVersions := not AnyFound;

  if InstallForAllVersions then
    MsgBox('No Revit installation was detected on this machine.' + #13#10 + #13#10 +
           'Clash Navigator will be registered for Revit 2022 to 2026 anyway. Revit simply ' +
           'ignores the versions that are not installed.', mbInformation, MB_OK);

  Result := True;
end;
