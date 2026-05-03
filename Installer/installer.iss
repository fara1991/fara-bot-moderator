; Inno Setup Script for FaraBotModerator
#define MyAppName "FaraBotModerator"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "Fara"
#define MyAppExeName "FaraBotModerator.exe"

[Setup]
AppId={{D3F9B6E1-4C5A-4B9D-8D3A-2A7C6E5B4A3D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\Releases
OutputBaseFilename=FaraBotModerator_Setup
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallFilesDir={app}
SetupIconFile=..\Resources\app.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
WizardImageFile=..\Resources\wizard_large.png
FlatComponentsList=yes
[Dirs]
Name: "{app}"; Permissions: users-modify

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Messages]
japanese.StatusExtractFiles=ファイルを展開しています...

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\Release\net8.0-windows10.0.22621.0\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Resources\secrets.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Manual.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "WebView2RuntimeInstaller.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[InstallDelete]
Type: filesandordirs; Name: "{app}\uninstall.exe"

[Code]
// アンインストール時にuninstall.exeを使用するようにレジストリを書き換える
procedure CurStepChanged(CurStep: TSetupStep);
var
  UninstallExe: String;
  AppId: String;
  RegKey: String;
begin
  if CurStep = ssPostInstall then
  begin
    UninstallExe := ExpandConstant('{app}\uninst000.exe');
    if FileExists(UninstallExe) then
    begin
      RenameFile(UninstallExe, ExpandConstant('{app}\uninstall.exe'));
      
      // レジストリのUninstallStringを書き換える
      AppId := '{D3F9B6E1-4C5A-4B9D-8D3A-2A7C6E5B4A3D}';
      RegKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + AppId + '_is1';
      
      // 64bit OSの場合はWow6432Nodeも考慮する必要があるかもしれないが、Inno Setupの[Setup]セクションのArchitecturesInstallIn64BitMode設定に依存する。
      // デフォルトでは32bitレジストリに書き込まれることが多い。
      if Is64BitInstallMode then
      begin
         RegWriteStringValue(HKEY_LOCAL_MACHINE, RegKey, 'UninstallString', '"' + ExpandConstant('{app}\uninstall.exe') + '"');
         RegWriteStringValue(HKEY_LOCAL_MACHINE, RegKey, 'QuietUninstallString', '"' + ExpandConstant('{app}\uninstall.exe') + '" /SILENT');
      end
      else
      begin
         RegWriteStringValue(HKEY_LOCAL_MACHINE, RegKey, 'UninstallString', '"' + ExpandConstant('{app}\uninstall.exe') + '"');
         RegWriteStringValue(HKEY_LOCAL_MACHINE, RegKey, 'QuietUninstallString', '"' + ExpandConstant('{app}\uninstall.exe') + '" /SILENT');
      end;
    end;
  end;
end;

// WebView2ランタイムのインストール確認
function NeedInstallWebView2(): Boolean;
var
  Version: String;
begin
  Result := not RegQueryStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A714C5}', 'pv', Version);
  if Result then
    Result := not RegQueryStringValue(HKEY_CURRENT_USER, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A714C5}', 'pv', Version);
end;

[Run]
Filename: "{tmp}\WebView2RuntimeInstaller.exe"; Parameters: "/silent /install"; Check: NeedInstallWebView2; StatusMsg: "Microsoft Edge WebView2 ランタイムをインストールしています..."
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon