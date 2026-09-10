# 电脑工具百宝箱

[![build-and-test](https://github.com/HuaJianX/ToolBox/actions/workflows/build.yml/badge.svg)](https://github.com/HuaJianX/ToolBox/actions/workflows/build.yml)
[![license: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![platform: Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4.svg)

Windows 桌面上的文件格式转换工具。**本地离线、不要钱、无广告、不登录、不捆绑、不上传文件。**

首页就五个大按钮，每次转换三步：**选文件 → 选目标格式 → 开始转换**。
目标格式按源文件自动推荐（HEIC 照片进来就自动选好 JPG），高级参数一律不暴露，
错误提示全是大白话（「这个文件打不开，换一个试试」「磁盘空间不够了」）。
转好的文件统一放到用户目录下的 **`转换结果`** 文件夹，**绝不覆盖原文件**。

## 一条设计原则：能用本机现成的，就绝不让你再下一个

这个项目**不往仓库里塞任何第三方二进制**，也**尽量不让你为了一个功能去下载上百 MB 的东西**。
每个功能都按「本机已有的 → 系统自带的 → 才考虑下载」的顺序找一个能用的：

| 功能 | 优先用 | 没有就退而用 | 再没有 |
|---|---|---|---|
| 图片转换 | **Magick.NET**（编译进程序，零外部依赖） | — | 永远可用 |
| 音频 / 视频转换 | 本机的 `ffmpeg.exe` | 系统 PATH 里的 ffmpeg | 提示你跑 `get-tools.ps1` 下载 |
| 文档转 PDF | 本机 **LibreOffice**（headless，最干净） | 本机 **Microsoft Office**（COM 自动化） | 提示装一个就行 |
| PDF 转图片 | 本机 **Poppler**（`pdftoppm`） | **Windows 自带的 PDF 渲染**（`Windows.Data.Pdf`） | 几乎永远可用 |
| PDF 转纯文本 | 本机 **Poppler**（`pdftotext`） | LibreOffice | 提示缺组件 |
| Markdown 转 PDF | 本机 **Pandoc** 排版 | 当纯文本交给 Word 排 | 内容不丢，只是不排版 |

本机测试机上，**五个大按钮全部可用，一个字节都没有下载** ——
ffmpeg 是从已装的 B 站客户端里拿的，pdftotext 是 Git for Windows 自带的，
文档转换用的是已装的 Microsoft Office，PDF 渲染用的是 Windows 自带 API。
详见下面的「组件是从哪来的」。

---

## 现在就能跑

需要 **.NET 10 SDK**（本机已验证 10.0.303）。

```powershell
# 编译
dotnet build ToolBox.slnx

# 跑起来（图片功能立刻可用，完全不依赖外部组件）
dotnet run --project src\ToolBox.App
```

音视频 / 文档 / PDF 需要几个外部组件。**先别急着下载** —— 程序会自己在
程序目录的 `tools\`、系统 PATH、常见安装位置里找。确实一个都没有，再跑：

```powershell
powershell -ExecutionPolicy Bypass -File tools\get-tools.ps1
```

它会下载 FFmpeg、Poppler、Pandoc 到 `tools\` 下（脚本用 curl 分段并行下载，
实测比单连接快约 5 倍）。

## 自检

`tests\ToolBox.SmokeTest` 不开界面，直接调转换引擎。它的设计是
**装了外部组件就做真实转换，没装就退回去验证「缺组件时的中文提示」**，
所以在任何机器上跑都不会出现「假绿」。

```powershell
dotnet run --project tests\ToolBox.SmokeTest -c Release
```

本机实测结果：**通过 26 项，失败 0 项**，五大类全部是真实转换：

| 验证内容 | 实测结果 |
|---|---|
| PNG → JPG / PNG / WEBP / BMP / TIFF | ✅ 5 种全部产出 |
| **HEIC → JPG / PNG / WEBP**（真实 1440×960 样张） | ✅ 全部产出 |
| WAV → MP3 / FLAC / M4A | ✅ 全部产出 |
| MP4 → MKV / AVI / MOV | ✅ 全部产出 |
| MP4 → 只要声音（MP3） | ✅ 产出 |
| **FFmpeg 进度解析** | ✅ 收得到百分比，且本机**没有 ffprobe**，走的是「用 ffmpeg 读时长」的兜底 |
| PDF → PNG（Windows 内置渲染） | ✅ 1 页 → 1735×2455 图片 |
| PDF → TXT | ✅ 抽出内容正确 |
| TXT → PDF（Microsoft Office） | ✅ 产出，回头用 pdftotext 验证过里面文字是对的 |
| HEIC 拒绝提示、坏文件提示、缺组件提示 | ✅ 全是人话 |

> HEIC 样张有第三方版权、**没有放进仓库**，所以全新克隆下来跑会是
> **25 项通过 + 1 项跳过**（跳过时它会打印原因，不会假装通过）。
> 想跑那一项，自己放一张 iPhone 照片到 `tests\assets\sample.heic` 即可，
> 详见 `tests\assets\README.md`。

## 组件是从哪来的

跑测试这台机器上，`tools\` 里的东西全是从**已装的软件**里拷出来的，没有下载：

| 组件 | 来源 | 用途 |
|---|---|---|
| `ffmpeg.exe`（41 MB，编码器齐全） | B 站客户端 `%APPDATA%\bilibili\ffmpeg\` | 音频 / 视频 / 提取声音 |
| `pdftotext.exe` + `zlib1.dll` | Git for Windows `C:\Program Files\Git\mingw64\bin\` | PDF 转纯文本 |

这两个**不进版本库**（`.gitignore` 已排除）：它们是第三方二进制，各有各的许可，
公开仓库不适合分发。你自己机器上想用，从本机拷一份到 `tools\` 就行。

`ToolLocator.cs` 的搜索顺序是：
**程序目录 `tools\` → 从程序目录往上找 `tools\` → 组件常见安装位置 → 系统 PATH**。
所以整个文件夹拷到 U 盘也能用。

---

## 实测出来的三个硬知识

### 1. HEIC 只能读、不能写

```
Heic : read=True   write=False     ← 有解码器，没有编码器
Jpg / Png / WebP / Bmp / Tiff : read=True write=True
```

HEIC 编码依赖 x265，因专利授权原因不随包分发，实测报
`no encode delegate for this image format 'HEIC'`。
所以**「转成 HEIC」做不到**，「HEIC → JPG/PNG/WEBP」完全没问题
（这才是真正的需求：把 iPhone 照片变成到处都能看的格式）。
程序里干脆没放「转成 HEIC」这个按钮，而不是放一个点了就报错的按钮。

### 2. 很多机器的 ffmpeg 是别的软件自带的，没有 ffprobe

ffprobe 只有一个用途：问出音视频总时长来算百分比进度。
B 站客户端自带的 ffmpeg 就没有 ffprobe。
所以 `FfmpegConverter` 做了两级探测：**有 ffprobe 用 ffprobe，
没有就用 `ffmpeg -i` 输出的 `Duration:` 行**（这条命令退出码是 1，
但元信息照样打在 stderr 里）。少一个组件，进度条照样准。

### 3. Office COM 有坑，但能驯服

- 必须跑在 **STA 线程**上（转换器默认在线程池的 MTA 上，所以自己开了个 STA 线程）
- **打开 .txt 会弹「文件转换」编码对话框**把程序卡死 —— 解法是给 `Documents.Open`
  传第 15 个参数 `NoEncodingDialog=true` 并指定 `Encoding=UTF-8`
- **如果用户本来就开着 Word**，COM 会附着到他那个实例上，这时候
  **绝不能改 `Visible`、绝不能 `Quit`**，否则会把人家没保存的东西弄没。
  代码里到处在判断 `ownedApp`（启动前有没有这个进程）来决定能不能动它
- **PDF 转 Word 不能用 Word COM**：Word 确实能打开 PDF，但会弹「要不要转换」的确认框，
  自动化时直接卡死（实测超时 120 秒），而且版式也乱。这一项老实要求 LibreOffice

---

## 打安装包

```powershell
# 先装一次 Inno Setup（免费、开源、无广告）
winget install --id JRSoftware.InnoSetup

# 一条命令：自检 → 发布 → 出安装包
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

体积（本机实测）：

| 组成 | 大小 |
|---|---|
| `电脑工具百宝箱.exe`（单文件、自包含） | **76.2 MB** |
| `tools\`（ffmpeg + pdftotext） | **42.7 MB** |
| **合计** | **118.9 MB** |

去掉 `-FrameworkDependent` 里省掉 .NET 运行时的话，exe 能从 76 MB 降到约 26 MB
（但用户机器上得先有 .NET 10 桌面运行时）。

> 单文件发布有个坑：`tools\` 里是要被当**子进程执行**的 exe，必须实实在在躺在磁盘上。
> 不排除的话单文件发布会把它们塞进 exe 内部，运行时 `AppContext.BaseDirectory\tools`
> 就是空的、功能全废。csproj 里靠 `ExcludeFromSingleFile="true"` 解决。

---

## 东西都放在哪

| 什么 | 在哪 |
|---|---|
| 转好的文件 | `C:\Users\<你>\转换结果\` |
| 运行日志（含组件自检，排错用） | `%LOCALAPPDATA%\电脑工具百宝箱\运行日志.txt` |
| 免安装组件 | 程序目录下的 `tools\`（跟着 exe 走） |
| 临时文件 | `%TEMP%\电脑工具百宝箱\`，转换完就删 |

程序**只写本地文件，一个字节都不上传**，断网状态下所有功能照常。

## 文档

| 文件 | 内容 |
|---|---|
| `docs\01-界面草图.md` | 四屏 ASCII 草图、字号颜色表、需求对应表 |
| `docs\02-技术选型与项目结构.md` | 为什么选 WPF、引擎分工表、目录结构、踩坑记录 |
| `docs\03-打包成安装包.md` | 发布参数、无广告自证、实测体积、签名、编码坑 |
| `THIRD-PARTY-NOTICES.md` | 第三方组件的许可说明 |

## 源码地图

```
src\ToolBox.App\
├── MainWindow.xaml              全部界面（首页 + 转换页）
├── ViewModels\MainViewModel.cs  三步流程、批量、进度、取消
└── Services\
    ├── FormatCatalog.cs         ★ 所有格式定义；加格式只改这一个文件
    ├── ConversionService.cs     ★ 调度 + 磁盘预检 + 异常翻译
    ├── ToolLocator.cs           到处找 ffmpeg / soffice / poppler / pandoc
    ├── ProcessRunner.cs         跑外部程序（不弹黑窗、参数不乱码、取消杀整棵树）
    ├── FriendlyError.cs         技术异常 → 大白话
    └── Converters\
        ├── ImageConverter.cs          Magick.NET
        ├── FfmpegConverter.cs         FFmpeg（含两级时长探测）
        ├── LibreOfficeConverter.cs    LibreOffice headless
        ├── OfficeComConverter.cs      Microsoft Office COM（STA + 防卡死）
        ├── PdfConverter.cs            PDF → 图片 / 文字 / Word 的分发
        ├── PdfWindowsRenderer.cs      Windows.Data.Pdf 内置渲染
        └── PandocConverter.cs         Markdown 排版（带降级）
```

## 加新格式只要改两处

1. `Services\FormatCatalog.cs` —— 在对应类别的 `Targets` 里加一行，
   界面上自动多出一个按钮，**XAML 一个字都不用改**。
2. `Services\ConversionService.cs` —— 在分派处加一个分支，指向已有转换器（或新写一个）。

## 许可

本仓库代码用 **MIT 许可**，见 [LICENSE](LICENSE)。
运行依赖的第三方组件（FFmpeg / Poppler / Pandoc / LibreOffice / Magick.NET）
不在本仓库里，各自许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
