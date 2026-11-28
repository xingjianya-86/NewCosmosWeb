; NewCosmos Shell 按用户安装脚本（Inno Setup 6/7）
;
; 由 deploy/scripts/build-installers.ps1 调用；也可手工用 ISCC 编译。
; 构建脚本通过环境变量覆盖：
;   NC_APP_VERSION          显示版本
;   NC_APP_NUMERIC_VERSION  文件版本
;   NC_SHELL_PUBLISH_DIR    Shell publish 输出目录
;   NC_AGENT_PUBLISH_DIR    Agent publish 输出目录（默认捆绑）
;   NC_OUTPUT_DIR           安装包输出目录
;
; 安装位置：%LOCALAPPDATA%\Programs\NewCosmos Shell
; 捆绑 Agent：{app}\Agent（任务“安装打印代理”默认勾选）
; WebView2：优先使用 deploy\installer\cache\MicrosoftEdgeWebview2Setup.exe 静默安装；
;           缓存不存在时在安装阶段提示下载地址。

#define AppVersion GetEnv("NC_APP_VERSION")
#if AppVersion == ""
  #define AppVersion "2.0.20261129"
#endif

#define NumericVersion GetEnv("NC_APP_NUMERIC_VERSION")
#if NumericVersion == ""
  #define NumericVersion "2.0.0.0"
#endif

#define ShellPublishDir GetEnv("NC_SHELL_PUBLISH_DIR")
#if ShellPublishDir == ""
  #define ShellPublishDir "..\..\publish\shell"
#endif

#define AgentPublishDir GetEnv("NC_AGENT_PUBLISH_DIR")
#if AgentPublishDir == ""
  #define AgentPublishDir "..\..\publish\agent"
#endif

#define OutputDir GetEnv("NC_OUTPUT_DIR")
#if OutputDir == ""
  #define OutputDir "..\..\publish\installers"
#endif

; 离线 WebView2 安装包是否随安装包一起编译
#define WebView2SetupRel "cache\MicrosoftEdgeWebview2Setup.exe"
#if FileExists(AddBackslash(SourcePath) + WebView2SetupRel)
  #define HasWebView2Setup
#endif

[Setup]
AppId={{2E4B9A17-6D3C-4E8A-9F21-7C5D0A8B2C02}
AppName=NewCosmos Shell
AppVersion={#AppVersion}
AppVerName=NewCosmos Shell {#AppVersion}
AppPublisher=NewCosmos
AppPublisherURL=https://cosmos.sua17.cn/
AppSupportURL=https://cosmos.sua17.cn/
AppUpdatesURL=https://cosmos.sua17.cn/
DefaultDirName={localappdata}\Programs\NewCosmos Shell
DefaultGroupName=NewCosmos
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=NewCosmosShellSetup_{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#NumericVersion}
VersionInfoCompany=NewCosmos
VersionInfoDescription=NewCosmos Shell Setup
VersionInfoProductName=NewCosmos Shell
VersionInfoProductVersion={#NumericVersion}
UninstallDisplayIcon={app}\NewCosmos.Shell.exe
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

#define LangDir AddBackslash(CompilerPath) + "Languages\"
#if FileExists(LangDir + "ChineseSimplified.isl")
[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#endif

[Tasks]
Name: "installagent"; Description: "安装打印代理（NewCosmos Agent）"; GroupDescription: "组件："; Flags: checkedonce
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#ShellPublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#AgentPublishDir}\*"; DestDir: "{app}\Agent"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: installagent

#ifdef HasWebView2Setup
; 只在运行时按需解压，不占安装目录
Source: "{#WebView2SetupRel}"; DestDir: "{tmp}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy
#endif

[Icons]
Name: "{group}\NewCosmos Shell"; Filename: "{app}\NewCosmos.Shell.exe"; WorkingDir: "{app}"; Comment: "NewCosmos 桌面壳（WebView2）"
Name: "{group}\NewCosmos Agent"; Filename: "{app}\Agent\NewCosmos.Agent.exe"; WorkingDir: "{app}\Agent"; Tasks: installagent
Name: "{group}\卸载 NewCosmos Shell"; Filename: "{uninstallexe}"
Name: "{autodesktop}\NewCosmos Shell"; Filename: "{app}\NewCosmos.Shell.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; 只在没有独立 Agent 自启项时写入，避免覆盖已单独安装的 Agent
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NewCosmos.Agent"; ValueData: """{app}\Agent\NewCosmos.Agent.exe"""; Tasks: installagent; Check: AgentAutostartNeedsWrite; Flags: uninsdeletevalue

[Run]
Filename: "{app}\NewCosmos.Shell.exe"; Description: "启动 NewCosmos Shell"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Agent\NewCosmos.Agent.exe"; Description: "启动打印代理"; Flags: nowait postinstall skipifsilent; Tasks: installagent; Check: ShouldLaunchBundledAgent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Agent"

[Code]
const
  WebView2RegKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2DownloadUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  AgentRunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  AgentRunValue = 'NewCosmos.Agent';

function GetWebView2Version: String;
var
  Version: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM32, WebView2RegKey, 'pv', Version) and (Version <> '') then
    Result := Version
  else if IsWin64 and RegQueryStringValue(HKLM64, WebView2RegKey, 'pv', Version) and (Version <> '') then
    Result := Version
  else if RegQueryStringValue(HKCU, WebView2RegKey, 'pv', Version) and (Version <> '') then
    Result := Version;
end;

function AgentAutostartNeedsWrite: Boolean;
var
  Existing: String;
begin
  Result := True;
  if RegQueryStringValue(HKCU, AgentRunKey, AgentRunValue, Existing) then
    Result := Pos(LowerCase(ExpandConstant('{app}\Agent\NewCosmos.Agent.exe')), LowerCase(Existing)) > 0;
end;

function ShouldLaunchBundledAgent: Boolean;
begin
  Result := WizardIsTaskSelected('installagent') and AgentAutostartNeedsWrite();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Version: String;
#ifdef HasWebView2Setup
  ResultCode: Integer;
  SetupPath: String;
#endif
begin
  Result := '';
  Version := GetWebView2Version();
  if Version <> '' then begin
    Log('WebView2 Runtime 已安装：' + Version);
    Exit;
  end;

#ifdef HasWebView2Setup
  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  SetupPath := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
  Log('尝试静默安装 WebView2：' + SetupPath);
  if Exec(SetupPath, '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    Log('WebView2 安装程序退出码：' + IntToStr(ResultCode))
  else
    Log('WebView2 安装程序启动失败：' + IntToStr(ResultCode));
  Version := GetWebView2Version();
#endif

  if Version = '' then begin
    if MsgBox('未检测到 Microsoft Edge WebView2 Runtime。' + #13#10 + #13#10 +
      'NewCosmos Shell 依赖 WebView2 显示页面。请下载并安装：' + #13#10 +
      WebView2DownloadUrl + #13#10 + #13#10 +
      '运维提示：把 MicrosoftEdgeWebview2Setup.exe 放到 ' +
      'deploy\installer\cache\ 后重新编译，安装程序会自动静默安装。' + #13#10 + #13#10 +
      '是否仍要继续安装？', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
      Result := '安装已取消：缺少 Microsoft Edge WebView2 Runtime。';
  end;
end;
