; ============================================================================
;  电脑工具百宝箱 —— Inno Setup 打包脚本
;
;  装出来的东西：
;    * 一个正常的 Windows 安装包，没有广告、没有捆绑、没有浏览器插件
;    * 不改首页、不改文件关联、不安装任何计划任务
;    * 装到当前用户目录，不需要管理员权限
;
;  ⚠️ 安装目录刻意用纯英文名 ToolBox，这不是随便起的：
;     poppler 组件在带中文的路径下找不到自己的字体映射表，
;     抽中文 PDF 的文字会得到空文件（详见 README「硬知识 5」）。
;     所以安装路径必须英文；开始菜单和桌面快捷方式的显示名仍然是中文。
;
;  怎么用：
;    powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
;
;  本文件必须存成「带 BOM 的 UTF-8」，否则 Inno Setup 会按 ANSI 读，中文变乱码。
;  build-installer.ps1 会自动补 BOM。
; ============================================================================

#define AppName        "电脑工具百宝箱"
#define AppVersion     "1.0.0"
#define AppPublisher   "本地工具"
#define AppExeName     "电脑工具百宝箱.exe"
#define AppId          "{8F3A2B14-5C7D-4E9A-B6F1-2D4E6A8C0B93}"
#define InstallFolder  "ToolBox"

[Setup]
; 固定 AppId：升级时会自动覆盖旧版本，而不是并排装两份
AppId={{#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

; 装到用户目录，全程不需要管理员权限
DefaultDirName={autopf}\{#InstallFolder}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=..\dist
OutputBaseFilename={#AppName}-{#AppVersion}-安装包
SetupIconFile=..\src\ToolBox.App\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

; 内容有 1.4 GB（大头是 LibreOffice），用 lzma2 最狠压 + 多线程，否则要压很久
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=8
LZMAUseSeparateProcess=yes

WizardStyle=modern
DisableWelcomePage=no
AllowNoIcons=yes

[Languages]
; 想要中文安装向导：去 Inno Setup 官网下载 ChineseSimplified.isl，
; 放到 Inno Setup 安装目录的 Languages\ 下，然后把下面那行前面的分号去掉。
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; dist\publish 是 build-installer.ps1 用 dotnet publish 生成的完整目录，
; 里面包含程序本体、ImageMagick，以及 tools\ 下的 ffmpeg / LibreOffice / poppler / pandoc。
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; 不加 [UninstallDelete]：安装进去的文件由 Inno 自己负责删干净。
; 程序的临时文件在 %TEMP%\电脑工具百宝箱\ 下，跟安装目录无关。
; 用户的「转换结果」文件夹和日志放在用户目录，卸载时一律不碰。

[Code]
procedure CurPageChanged(CurPageID: Integer);
var
  Note: String;
begin
  if CurPageID = wpFinished then
  begin
    Note := #13#10#13#10 +
      '程序装在：' + ExpandConstant('{app}') + #13#10 +
      '这个文件夹名是纯英文的，请保持英文，不要改成中文 —— ' + #13#10 +
      'PDF 转纯文本用的组件在中文路径下会失效（中文 PDF 会抽出空文件）。' + #13#10 +
      '桌面和开始菜单上的快捷方式显示名是中文，那个随便改，不影响。';

    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + Note;
  end;
end;
