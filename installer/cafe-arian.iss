#ifndef AppVersion
  #error AppVersion must be passed by build.ps1.
#endif

[Setup]
AppId=CafeArian.0e45295c-0ffc-4dd1-ba16-047ae5f32d8a
AppName=Cafe Arian
AppVersion={#AppVersion}
AppPublisher=Cafe Arian
AppPublisherURL=https://github.com/Mohammadreza-72/Cafe-Accounting
DefaultDirName={localappdata}\Programs\CafeArian
DefaultGroupName=Cafe Arian
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=cafe-arian-setup-{#AppVersion}
SetupIconFile=..\Assets\arian.ico
UninstallDisplayIcon={app}\CafeArian.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
InfoAfterFile=after-install.txt
#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
#endif

[Files]
#ifdef SignToolName
Source: "..\artifacts\publish-installer\CafeArian.exe"; DestDir: "{app}"; Flags: ignoreversion signonce
#else
Source: "..\artifacts\publish-installer\CafeArian.exe"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce

[Icons]
Name: "{group}\Cafe Arian"; Filename: "{app}\CafeArian.exe"
Name: "{autodesktop}\Cafe Arian"; Filename: "{app}\CafeArian.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CafeArian.exe"; Description: "Launch Cafe Arian"; Flags: nowait postinstall skipifsilent
