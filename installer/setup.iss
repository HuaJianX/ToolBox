; ============================================================================
;  电脑工具百宝箱 —— Inno Setup 打包脚本
;
;  这个脚本装出来的东西：
;    * 一个正常的 Windows 安装包，没有广告、没有捆绑、没有浏览器插件
;    * 不改首页、不改文件关联、不安装任何计划任务
;    * 装到当前用户目录，不需要管理员权限
;
;  怎么用（两种都行）：
;    1) 开 Inno Setup Compiler，打开本文件，按 F9
;    2) 命令行：pwsh -File installer\build-installer.ps1
;
;  重要：本文件必须存成「带 BOM 的 UTF-8」，否则 Inno Setup 会按 ANSI 读，
;        中文会变乱码。build-installer.ps1 会自动帮你处理。
; ============================================================================

#define AppName        "电脑工具百宝箱"
#define AppVersion     "1.0.0"
#define AppPublisher   "本地工具"
#define AppExeName     "电脑工具百宝箱.exe"
#define AppId          "{8F3A2B14-5C7D-4E9A-B6F1-2D4E6A8C0B93}"

[Setup]
; 固定 AppId：升级时会自动覆盖旧版本，而不是并排装两份
AppId={{#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

; 装到用户目录，全程不需要管理员权限（老人机器上经常没有管理员密码）
DefaultDirName={autopf}\{#AppName}
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

; lzma2/max：压得最狠。程序里 90% 是 ffmpeg 和各种 dll，压完小很多。
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
AllowNoIcons=yes

[Languages]
; 想让它显示中文安装界面：去 Inno Setup 官网下载 ChineseSimplified.isl，
; 放到 Inno Setup 安装目录的 Languages\ 下，然后把下面这行前面的分号去掉。
; (Inno Setup 6 自带不带中文，所以默认用英文界面 —— 安装包本身就是中文名，
;  按钮就几个，影响很小。)
;Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; dist\publish 是 build-installer.ps1 用 dotnet publish 生成的完整目录，
; 里面已经包含了程序本体、Magick.NET 以及 tools\ 下的 ffmpeg / poppler / pandoc。
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; 不加 [UninstallDelete]：安装进去的文件由 Inno 自己负责删干净。
; 程序的临时文件都在 %TEMP%\电脑工具百宝箱\ 下，跟安装目录无关。
; 用户的「转换结果」文件夹和日志放在用户目录，卸载时一律不碰。

[Code]
var
  LibreOfficeMissing: Boolean;

function InitializeSetup(): Boolean;
begin
  { LibreOffice 是 350 MB 的独立办公软件，不塞进安装包。
    装了就能用「文档转换」和「PDF 转 Word」，没装也不影响其它功能。 }
  LibreOfficeMissing := not (
    FileExists(ExpandConstant('{pf}\LibreOffice\program\soffice.exe')) or
    FileExists(ExpandConstant('{pf32}\LibreOffice\program\soffice.exe'))
  );

  Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Note: String;
begin
  if (CurPageID = wpFinished) and LibreOfficeMissing then
  begin
    Note := #13#10#13#10 +
      '提示：没有检测到 LibreOffice，所以「文档转换」和「PDF 转 Word」暂时用不了。' + #13#10 +
      '想用的话，自己装一个 LibreOffice 就行，程序会自动找到它。' + #13#10 +
      '其它功能（图片、音频、视频、PDF 转图片/文字）不受影响，现在就能用。';

    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + Note;
  end;
end;
