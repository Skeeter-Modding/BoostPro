; BoostPro Installer Script for Inno Setup
; Created by Skeeter - Triple Threat Tactical Gaming Community
; Download Inno Setup from: https://jrsoftware.org/isdl.php

#define MyAppName "BoostPro"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Skeeter - Triple Threat Tactical Gaming Community"
#define MyAppURL "https://discord.gg/triplethreat"
#define MyAppExeName "BoostProUI.exe"

[Setup]
; Unique app ID - generate a new GUID for your app
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Request admin rights (needed for system optimizations)
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; Output installer settings
OutputDir=installer_output
OutputBaseFilename=BoostPro_Setup
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
; Modern Windows look
WizardStyle=modern
; Icon
SetupIconFile=icon.ico
; Uninstall icon
UninstallDisplayIcon={app}\{#MyAppExeName}
; Version info shown in Windows
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=BoostPro - Windows Gaming Optimizer
VersionInfoProductName={#MyAppName}
; Allow user to choose install location
DisableProgramGroupPage=yes
; License file (optional)
; LicenseFile=LICENSE.txt
; Minimum Windows version (Windows 10)
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "Create a &Quick Launch icon"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; Main application files - self-contained publish output
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Option to launch after install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
// Custom code to check for .NET runtime (optional, not needed for self-contained)
function InitializeSetup(): Boolean;
begin
  Result := True;
  // Add any pre-install checks here
end;
