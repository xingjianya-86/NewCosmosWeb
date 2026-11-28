; NewCosmos Agent 按用户安装脚本（Inno Setup 6/7）
;
; 由 deploy/scripts/build-installers.ps1 调用；也可手工用 ISCC 编译。
; 构建脚本通过环境变量覆盖：
;   NC_APP_VERSION          显示版本，例如 2.0.20261124
;   NC_APP_NUMERIC_VERSION  文件版本，例如 2.0.0.0
;   NC_AGENT_PUBLISH_DIR    dotnet publish 输出目录
;   NC_OUTPUT_DIR           安装包输出目录
;
; 安装位置：%LOCALAPPDATA%\Programs\NewCosmos Agent（无需管理员）
; 配置数据：%APPDATA%\NewCosmos\agent.json（卸载不删除）

#define AppVersion GetEnv("NC_APP_VERSION")
#if AppVersion == ""
  #define AppVersion "2.0.20261129"
#endif

#define NumericVersion GetEnv("NC_APP_NUMERIC_VERSION")
#if NumericVersion == ""
  #define NumericVersion "2.0.0.0"
#endif

#define AgentPublishDir GetEnv("NC_AGENT_PUBLISH_DIR")
#if AgentPublishDir == ""
  #define AgentPublishDir "..\..\publish\agent"
#endif

#define OutputDir GetEnv("NC_OUTPUT_DIR")
#if OutputDir == ""
  #define OutputDir "..\..\publish\installers"
#endif

[Setup]
AppId={{7E1E7A27-9C0D-4D25-9A77-4F9B5C0B1B01}
AppName=NewCosmos Agent
AppVersion={#AppVersion}
AppVerName=NewCosmos Agent {#AppVersion}
AppPublisher=NewCosmos
AppPublisherURL=https://cosmos.sua17.cn/
AppSupportURL=https://cosmos.sua17.cn/
AppUpdatesURL=https://cosmos.sua17.cn/
DefaultDirName={localappdata}\Programs\NewCosmos Agent
DefaultGroupName=NewCosmos
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=NewCosmosAgentSetup_{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#NumericVersion}
VersionInfoCompany=NewCosmos
VersionInfoDescription=NewCosmos Agent Setup
VersionInfoProductName=NewCosmos Agent
VersionInfoProductVersion={#NumericVersion}
UninstallDisplayIcon={app}\NewCosmos.Agent.exe
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

#define LangDir AddBackslash(CompilerPath) + "Languages\"
#if FileExists(LangDir + "ChineseSimplified.isl")
[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#endif

[Tasks]
Name: "autostart"; Description: "开机自动启动 NewCosmos Agent 托盘"; GroupDescription: "附加任务："; Flags: checkedonce
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#AgentPublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NewCosmos Agent"; Filename: "{app}\NewCosmos.Agent.exe"; WorkingDir: "{app}"; Comment: "NewCosmos 打印/文书代理"
Name: "{group}\卸载 NewCosmos Agent"; Filename: "{uninstallexe}"
Name: "{autodesktop}\NewCosmos Agent"; Filename: "{app}\NewCosmos.Agent.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; 自启命令与 Agent 自身 --install-autostart 写入的值保持一致
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NewCosmos.Agent"; ValueData: """{app}\NewCosmos.Agent.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; 自动升级所需：去掉 skipifsilent，让 /SILENT 静默安装完成后也自动启动新版 Agent
; （Agent 的自动更新以 /SILENT 调用本安装包；交互安装行为不变，完成页仍显示"运行"复选框）。
Filename: "{app}\NewCosmos.Agent.exe"; Description: "启动 NewCosmos Agent 托盘"; Flags: nowait postinstall

[UninstallDelete]
; 清理运行日志等安装目录内的残留；%APPDATA%\NewCosmos 下的用户配置不删
Type: files; Name: "{app}\*.log"
