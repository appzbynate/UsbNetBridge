[Setup]
AppName=UsbNetBridge
AppVersion=2.1.0
AppPublisher=appzbynate
DefaultDirName={autopf}\UsbNetBridge
DefaultGroupName=UsbNetBridge
OutputDir=publish
OutputBaseFilename=UsbNetBridge_Setup
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
; SetupIconFile=UsbNetBridge.Client\icon.ico
UninstallDisplayIcon={app}\UsbNetBridge.Client.exe
PrivilegesRequired=lowest

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Automatically start UsbNetBridge when Windows starts"; GroupDescription: "Startup Options"

[Files]
Source: "UsbNetBridge.Client\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\UsbNetBridge"; Filename: "{app}\UsbNetBridge.Client.exe"
Name: "{group}\{cm:UninstallProgram,UsbNetBridge}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\UsbNetBridge"; Filename: "{app}\UsbNetBridge.Client.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "UsbNetBridge"; ValueData: """{app}\UsbNetBridge.Client.exe"" --tray"; Tasks: startup

[Run]
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""UsbNetBridge"" dir=in action=allow program=""{app}\UsbNetBridge.Client.exe"" enable=yes profile=any"; Flags: runhidden
Filename: "{app}\UsbNetBridge.Client.exe"; Description: "{cm:LaunchProgram,UsbNetBridge}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""UsbNetBridge"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"
