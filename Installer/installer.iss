; Inno Setup Script for FaraBotModerator
#define MyAppName "FaraBotModerator"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "Fara"
#define MyAppExeName "FaraBotModerator.exe"

[Setup]
AppId={{D3F9B6E1-4C5A-4B9D-8D3A-2A7C6E5B4A3D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; 「アプリ」の一覧に出る名前。指定しないと、「FaraBotModerator バージョン 1.1.0」のように、バージョンが付く（バージョンは、別の欄に出る）
AppVerName={#MyAppName}
; アプリの起動中は、インストール・アンインストールを止める（起動中に消すと、アプリの終了時にエラーが出る）。アプリが、同じ名前の Mutex を持っている（App.xaml.cs）
AppMutex=FaraBotModerator_Running
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
[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Messages]
japanese.StatusExtractFiles=ファイルを展開しています...

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 設定・ログは、インストール先ではなく、ユーザーごとの %LOCALAPPDATA%\FaraBotModerator に保存する（アプリが作る）。
; 開発中の設定ファイル（Resources\secrets.json）が、インストーラーに入らないよう、除く
Source: "..\bin\Release\net10.0-windows10.0.22621.0\publish\win-x64\*"; DestDir: "{app}"; Excludes: "Resources\secrets.json"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Manual.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "WebView2RuntimeInstaller.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[InstallDelete]
Type: filesandordirs; Name: "{app}\uninstall.exe"

[Code]
// 以前のバージョンの設定ファイル（インストール先の Resources\secrets.json。認証情報が入っている）を、新しい保存先へ移す。
// インストール先は、Users が読み取れる場所なので、コピーできてから、元を消す。保存先に同じファイルがあれば、何もしない（アプリも、起動時に同じ移行をする）
procedure MigrateLegacySecrets();
var
  Legacy: String;
  DataDir: String;
  Target: String;
begin
  Legacy := ExpandConstant('{app}\Resources\secrets.json');
  DataDir := ExpandConstant('{localappdata}\FaraBotModerator');
  Target := DataDir + '\secrets.json';
  if FileExists(Legacy) and (not FileExists(Target)) then
  begin
    if ForceDirectories(DataDir) and CopyFile(Legacy, Target, True) then
      DeleteFile(Legacy);
  end;
end;

// アンインストール時にuninstall.exeを使用するようにレジストリを書き換える
procedure CurStepChanged(CurStep: TSetupStep);
var
  UninstallExe: String;
  AppId: String;
  RegKey: String;
begin
  if CurStep = ssInstall then
    MigrateLegacySecrets();

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
; 以前のバージョンは、インストール先に全ユーザー（Users）の書き込み権限を付けていた。実行ファイルを差し替えられるので、外す（継承された読み取り権限は残る）
Filename: "{sys}\icacls.exe"; Parameters: """{app}"" /remove:g *S-1-5-32-545 /T /C"; Flags: runhidden; StatusMsg: "インストール先の権限を整えています..."
Filename: "{tmp}\WebView2RuntimeInstaller.exe"; Parameters: "/silent /install"; Check: NeedInstallWebView2; StatusMsg: "Microsoft Edge WebView2 ランタイムをインストールしています..."
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon