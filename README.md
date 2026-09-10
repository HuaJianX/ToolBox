# 电脑工具百宝箱

[![build-and-test](https://github.com/HuaJianX/ToolBox/actions/workflows/build.yml/badge.svg)](https://github.com/HuaJianX/ToolBox/actions/workflows/build.yml)
[![license: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![platform: Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4.svg)

Windows 桌面上的文件格式转换工具。**完全离线、不依赖任何外部软件、不要钱、无广告、不登录、不上传文件。**

首页就五个大按钮，每次转换三步：**选文件 → 选目标格式 → 开始转换**。
目标格式按源文件自动推荐（HEIC 照片进来就自动选好 JPG），高级参数一律不暴露，
错误提示全是大白话（「这个文件打不开，换一个试试」「磁盘空间不够了」）。
转好的文件统一放到用户目录下的 **`转换结果`** 文件夹，**绝不覆盖原文件**。

## 设计原则：一切自带，一个外部软件都不依赖

五个大按钮背后的引擎**全部打包在程序目录的 `tools\` 里**，
不蹭你机器上装没装 Office、Git、B 站客户端：

| 功能 | 引擎 | 在哪 |
|---|---|---|
| 图片 JPG/PNG/WEBP/BMP/GIF/TIFF 互转 | **Magick.NET**（ImageMagick） | 编译进 exe，原生库随 exe 走 |
| 图片 **HEIC 读取**（→ JPG/PNG/WEBP） | 同上（libheif 解码器） | 同上 |
| 音频 MP3/WAV/FLAC/M4A 互转 | **FFmpeg** + ffprobe | `tools\ffmpeg\bin\`（181 MB） |
| 视频 MP4/MKV/AVI/MOV 互转、提取声音 | 同上 | 同上 |
| Word/Excel/PPT/TXT → PDF | **LibreOffice**（headless） | `tools\LibreOffice\`（797 MB） |
| **PDF → Word** | 同上（`writer_pdf_import`） | 同上 |
| PDF → 图片 | **Poppler**（`pdftoppm`） | `tools\poppler\Library\bin\`（99 MB） |
| PDF → 纯文本 | **Poppler**（`pdftotext`） | 同上 |
| Markdown → PDF | **Pandoc** 排版 → LibreOffice 转 PDF | `tools\pandoc\`（223 MB） |

所以：**换一台干净的 Windows 10/11，把整个文件夹拷过去双击就能用**，
不需要先装任何东西。整个文件夹也能拷到 U 盘，走到哪用到哪。
唯一的环境要求是 Windows 10 2004（19041）以上。

> ⚠️ **请把程序放在纯英文路径下**（默认装在 `%LOCALAPPDATA%\Programs\ToolBox\`）。
> poppler 在带中文的路径下抽不出中文 PDF 的文字，原因见下面「硬知识 5」。
> 桌面快捷方式的显示名是中文，那个随便改，不影响。

`ToolLocator.cs` 的搜索顺序是
**程序目录 `tools\` → 从程序目录往上找 `tools\` → 组件常见安装位置 → 系统 PATH**，
所以开发和跑测试时不用复制组件，发布时自动跟着走。

---

## 现在就能跑

需要 **.NET 10 SDK**（本机已验证 10.0.303）才能**编译**；
编译出来的程序运行时不需要任何东西。

```powershell
dotnet build ToolBox.slnx
dotnet run --project src\ToolBox.App
```

第一次要先把组件下到 `tools\` 里（联网一次，之后永久离线）：

```powershell
powershell -ExecutionPolicy Bypass -File tools\get-tools.ps1
```

脚本会下载 FFmpeg、Poppler、Pandoc、LibreOffice 并自动解包、自动瘦身。
**用 curl 分段并行下载**：很多网络对单连接限速（实测单连接 54 KB/s，
8–12 连接能到 600 KB/s 以上，快 10 倍以上），服务器不支持分段会自动退回单连接。
GitHub 资源会直连和反代各试一遍；LibreOffice 优先走国内镜像。

## 自检

```powershell
dotnet run --project tests\ToolBox.SmokeTest -c Release
```

设计要点：**装了组件就做真实转换，没装就退回去验证「缺组件时的中文提示」**，
所以在任何机器上跑都不会「假绿」。它还会直接驱动 `MainViewModel`，
把界面上那套「点按钮 → 选文件 → 开始 → 转换好了」的流程也跑一遍。本机实测：

```
结果：通过 46 项，失败 0 项，跳过 0 项（总共 33 秒）
```

| 验证内容 | 实测结果 |
|---|---|
| PNG → JPG / PNG / WEBP / BMP / TIFF | ✅ 5 种全部产出 |
| **HEIC → JPG / PNG / WEBP**（真实 1440×960 样张） | ✅ 全部产出 |
| WAV → MP3 / FLAC / M4A | ✅ 全部产出 |
| **MP3 → WAV / FLAC**、**FLAC → MP3**、**M4A → WAV** | ✅ 反向也验了（之前只从 WAV 往外转） |
| MP4 → MKV / AVI / MOV | ✅ 全部产出 |
| **MKV → MP4**、**AVI → MKV**、**MOV → AVI**、**MKV → MOV** | ✅ 非 MP4 源也验了 |
| MP4 → 只要声音（MP3） | ✅ 产出 |
| FFmpeg 进度解析 | ✅ 走自带的 ffprobe，百分比准确 |
| PDF → PNG / TXT / Word | ✅ 走自带的 Poppler 和 LibreOffice |
| TXT → PDF | ✅ |
| **Word(docx) → PDF** | ✅ 27,154 字节 |
| **Excel(xlsx) → PDF** | ✅ 42,808 字节 |
| **PPT(pptx) → PDF** | ✅ 27,130 字节 |
| **Markdown → PDF**（Pandoc 排版 + LibreOffice 出稿） | ✅ 24,651 字节 |
| **批量**（驱动 MainViewModel 一次转 3 个） | ✅ 列表 3 个、默认格式自动推荐、提示「全部 3 个都转换好了」 |
| **取消**（1080p 素材转码途中按取消） | ✅ 0.5 秒响应，提示「已经取消了」，**不留半个坏文件** |
| 断网可用 | ✅ 源码里没有任何联网代码，程序集也没引用 `System.Net.*` |
| HEIC 拒绝提示、坏文件提示、缺组件提示 | ✅ 全是人话 |

`tests/assets/` 里带了三个自己造的 Office 测试素材（docx / xlsx / pptx），
Markdown 素材是运行时现生成的，所以这几条验证换了机器也能复现。

> HEIC 样张有第三方版权、**没有放进仓库**，所以全新克隆下来跑会是
> **25 项通过 + 1 项跳过**（跳过时会打印原因，不会假装通过）。
> 想跑那一项，自己放一张 iPhone 照片到 `tests\assets\sample.heic` 即可。

## 体积（本机实测）

| 组成 | 大小 | 说明 |
|---|---|---|
| `电脑工具百宝箱.exe` | **76.2 MB** | 单文件、自包含，含 .NET 运行时和 ImageMagick |
| `tools\LibreOffice\` | **797 MB** | 文档转 PDF / PDF 转 Word |
| `tools\pandoc\` | **223 MB** | Markdown 排版（可选，删了 Markdown 就不排版，内容不丢） |
| `tools\ffmpeg\bin\` | **181 MB** | 音视频 |
| `tools\poppler\` | **99 MB** | PDF 转图片 / 文字 |
| **合计** | **1,376 MB** | 解压即用，无需安装 |

已经做过瘦身，省下约 740 MB：
- LibreOffice 删掉 **119 种语言的界面资源**（保留 en_GB / en_ZA / zh_CN / zh_TW）
- 删掉 **57 个拼写词典**（保留 dict-en）和剪贴画库、nlpsolver、wiki-publisher
- 删掉 **118 个界面语言包** `.xcd`
- poppler 删掉 `Library\lib`（导入库）和 `Library\include`（头文件）—— 编译才用
- ffmpeg 删掉 `ffplay.exe`（播放器，转码用不上）

想再压：删掉 `tools\pandoc\` 省 223 MB（Markdown 转 PDF 会自动降级成纯文本，内容不丢）。

---

## 实测出来的六个硬知识

### 1. HEIC 只能读、不能写

```
Heic : read=True   write=False     ← 有解码器，没有编码器
Jpg / Png / WebP / Bmp / Tiff : read=True write=True
```

HEIC 编码依赖 x265 的 HEVC 专利，没有任何免费方案可以合法分发。
ffmpeg 也一样：有 `libx265` 但**没有 HEIF/HEIC 封装器**（实测
`Unable to find a suitable output format for '.heic'`）。
所以**「转成 HEIC」做不到**，「HEIC → JPG/PNG/WEBP」完全没问题。
程序里干脆没放这个按钮，而不是放一个点了就报错的按钮。

### 2. LibreOffice 的用户配置必须复用，否则每次白等 8 秒

实测同一个 profile 连续转换：

```
第 1 次（建配置）    15.3 秒
第 2 次及以后        6.1 秒     ← 配置建好之后
每次都用全新 profile  14.2 秒     ← 等于每次都从头建
```

所以 `LibreOfficeConverter` 把配置放在
`%LOCALAPPDATA%\电脑工具百宝箱\office-profile` **复用**，
并用信号量排队（同一个配置目录不能被两个 soffice 同时用）。
另外程序启动时会在后台 `--terminate_after_init` **预热**一次，
这样用户第一次点「开始转换」就是 6 秒，而不是干等 15 秒。

### 3. LibreOffice 转换失败也返回退出码 0

`soffice --convert-to` 出错了照样 `exit 0`，光看退出码会误判成功。
唯一可信的判断是**产物文件到底有没有生成**，所以代码里查 `File.Exists`。

### 4. Office COM 有坑（作为可选备用路径保留着）

程序优先用 LibreOffice；如果用户机器上正好有 Office 且 LibreOffice 缺失，
`OfficeComConverter` 会顶上。它踩过的坑：
- Office 自动化要求 **STA 线程**（转换器默认在线程池的 MTA 上）
- 打开 `.txt` 会弹「文件转换」编码框把程序卡死 → 靠 `Documents.Open` 第 15 个参数
  `NoEncodingDialog=true` + `Encoding=UTF-8` 解决
- 用户本来就开着 Word 时 COM 会附着到他的实例上，这时**绝不能改 `Visible`、
  绝不能 `Quit`**，否则会弄丢人家没保存的东西

### 5. poppler 处理不了非 ASCII 路径（所以程序必须装在纯英文目录）

这个是补测 `docx/xlsx/pptx → PDF` 时顺带撞出来的，也是最隐蔽的一个：

```
程序装在  ...\Programs\电脑工具百宝箱\      ← 中文路径
pdftotext 报 I/O Error: Couldn't open 'nameToUnicode' file
           '...\Programs\<b5><e7><c4><d4><b9><a4><be><df><b0><d9><b1><a6><cf><e4>\...'
抽中文 PDF 的文字 → 空文件
```

路径里的 `电脑工具百宝箱` 被当成 GBK 字节打成 `<b5><e7>...` ——
poppler 是**按 exe 自己的路径去推数据目录**（`share/poppler/nameToUnicode/`）的，
路径里有中文就找不到那些字体映射表。而中文 PDF 基本都用 CID 字体，**必须要这张表**。
所以症状是：**只有中文 PDF 会失败，而且失败得很安静（抽出空文件）**。
英文 PDF（比如 `dummy.pdf`）照样能转，所以自检一开始没发现。

试过但没用的办法：
- `POPPLER_DATADIR` 环境变量 —— poppler 根本不认
- 8.3 短路径 —— `电脑工~1` 还是中文
- 把输入输出文件换成纯 ASCII 名再暂存转回 —— 没用，坏的是安装路径本身
- 让 LibreOffice 导纯文本 —— 实测 `txt:Text` / `txt` / `Text` 三种过滤器都不出文件

**最终解法：程序装在纯英文路径下**（`%LOCALAPPDATA%\Programs\ToolBox\`，
桌面快捷方式的显示名仍然是中文）。实测中文路径下抽出来是空文件、
英文路径下抽出来是 `测试标题 / 这是正文内容。 / 项目一 / 项目二`，完全正确。

代码里也加了兜底：`ToolLocator.InstalledInAsciiPath` 会检测安装路径，
真是被放到中文目录里时，PDF 转文字失败会给一句准确的话
（「程序现在装在带中文的文件夹里，poppler 组件在中文路径下会失效，
挪到纯英文路径就好了」），而不是误导用户说「你这是个扫描件」。

### 6. 取消要顺手清掉半成品

用户点「取消」时，ffmpeg 往往已经写出半个文件了。不清理的话，
「转换结果」里会躺着一个**看着像成功、其实打不开**的文件 —— 这比报错更坑人。
所以 `FfmpegConverter` / `ImageConverter` / `PdfConverter` 都在取消时删掉自己的半成品，
自检里也专门验了这一条（取消后输出目录必须没有残留）。

---

## 打安装包
```powershell
winget install --id JRSoftware.InnoSetup      # 只需装一次
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

`setup.iss` 已经把 `dist\publish\*` 整个收进去，所以出来的安装包是**完整离线包**。
注意体积：1.38 GB 的目录用 lzma2 压完大约 400–600 MB。

> 单文件发布有个坑：`tools\` 里是要被当**子进程执行**的 exe，必须实实在在躺在磁盘上。
> 不排除的话单文件发布会把它们塞进 exe 内部，运行时 `AppContext.BaseDirectory\tools`
> 就是空的、功能全废。csproj 里靠 `ExcludeFromSingleFile="true"` 解决。

---

## 东西都放在哪

| 什么 | 在哪 |
|---|---|
| 转好的文件 | `C:\Users\<你>\转换结果\` |
| 运行日志（含组件自检） | `%LOCALAPPDATA%\电脑工具百宝箱\运行日志.txt` |
| LibreOffice 配置 | `%LOCALAPPDATA%\电脑工具百宝箱\office-profile\` |
| 自带的转换组件 | 程序目录下的 `tools\` |
| 临时文件 | `%TEMP%\电脑工具百宝箱\`，转换完就删 |

程序**只写本地文件，一个字节都不上传**，断网状态下所有功能照常。

## 文档

| 文件 | 内容 |
|---|---|
| `docs\01-界面草图.md` | 四屏 ASCII 草图、字号颜色表、需求对应表 |
| `docs\02-技术选型与项目结构.md` | 为什么选 WPF、引擎分工表、目录结构、踩坑记录 |
| `docs\03-打包成安装包.md` | 发布参数、无广告自证、体积、签名、编码坑 |
| `THIRD-PARTY-NOTICES.md` | 第三方组件的许可说明 |

## 源码地图

```
src\ToolBox.App\
├── MainWindow.xaml              全部界面（首页 + 转换页）
├── ViewModels\MainViewModel.cs  三步流程、批量、进度、取消
└── Services\
    ├── FormatCatalog.cs         ★ 所有格式定义；加格式只改这一个文件
    ├── ConversionService.cs     ★ 调度 + 磁盘预检 + 异常翻译
    ├── ToolLocator.cs           找自带的 ffmpeg / soffice / poppler / pandoc
    ├── ProcessRunner.cs         跑外部程序（不弹黑窗、参数不乱码、取消杀整棵树）
    ├── FriendlyError.cs         技术异常 → 大白话
    └── Converters\
        ├── ImageConverter.cs          Magick.NET
        ├── FfmpegConverter.cs         FFmpeg（两级时长探测）
        ├── LibreOfficeConverter.cs    LibreOffice（配置复用 + 预热 + 排队）
        ├── OfficeComConverter.cs      Microsoft Office COM（可选备用）
        ├── PdfConverter.cs            PDF → 图片 / 文字 / Word 的分发
        ├── PdfWindowsRenderer.cs      Windows.Data.Pdf 兜底渲染
        └── PandocConverter.cs         Markdown 排版（带降级）
```

## 加新格式只要改两处

1. `Services\FormatCatalog.cs` —— 在对应类别的 `Targets` 里加一行，
   界面上自动多出一个按钮，**XAML 一个字都不用改**。
2. `Services\ConversionService.cs` —— 在分派处加一个分支，指向已有转换器（或新写一个）。

## 许可

本仓库代码用 **MIT 许可**，见 [LICENSE](LICENSE)。
第三方组件（FFmpeg / Poppler / Pandoc / LibreOffice / Magick.NET）**不在本仓库里**
（由 `tools\get-tools.ps1` 下载），各自许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
