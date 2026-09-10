# 电脑工具百宝箱

[![build-and-test](https://github.com/HuaJianX/ToolBox/actions/workflows/build.yml/badge.svg)](https://github.com/HuaJianX/ToolBox/actions/workflows/build.yml)
[![license: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![platform: Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4.svg)

Windows 桌面上的文件格式转换工具。**本地离线、不要钱、无广告、不登录、不捆绑、不上传文件。**

首页就五个大按钮，每次转换三步：**选文件 → 选目标格式 → 开始转换**。
目标格式按源文件自动推荐（HEIC 照片进来就自动选好 JPG），高级参数一律不暴露，
错误提示全是大白话（「这个文件打不开，换一个试试」「磁盘空间不够了」）。
转好的文件统一放到用户目录下的 **`转换结果`** 文件夹，**绝不覆盖原文件**。

第一版只做格式转换，但底子是通用的 —— 以后要加压缩、改名之类的新工具，
只要在格式表里加一项、写一个转换器就行（见 `docs/02-技术选型与项目结构.md` 第 7 节）。

---

## 现在就能跑

需要 **.NET 10 SDK**（本机已验证 10.0.303）。

```powershell
# 编译
dotnet build ToolBox.slnx

# 跑起来（图片功能立刻可用，不依赖任何外部组件）
dotnet run --project src\ToolBox.App
```

**图片转换开箱即用**：ImageMagick 的原生库通过 NuGet 打包进程序，不需要另外装东西。
音视频 / 文档 / PDF 需要先下载几个免安装组件（联网跑一次，之后永久离线）：

```powershell
powershell -ExecutionPolicy Bypass -File tools\get-tools.ps1
```

它会下载 FFmpeg、Poppler、Pandoc 到 `tools\` 下。
注意 FFmpeg 的 Windows 构建是静态链接的，**下载包就有 106 MB**（脚本跑完会打印真实大小）。
**LibreOffice（文档转 PDF 用，约 350 MB）不下载** —— 它是独立的办公软件，
自己装一个就行，程序会自动去 `C:\Program Files\LibreOffice` 找。

---

## 自检

`tests\ToolBox.SmokeTest` 不开界面，直接调转换引擎，验证「能转的真的能转、不能转的提示说人话」：

```powershell
dotnet run --project tests\ToolBox.SmokeTest -c Release
```

当前结果（本机实测）：**通过 13 项，失败 0 项**，包含两个真实 HEIC 样张的转码。

> HEIC 样张有第三方版权，**没有放进仓库**，所以全新克隆下来跑会是
> **10 项通过 + 1 项跳过**（跳过时它会打印原因，不会假装通过）。
> 想把这一项也跑起来，自己放一张 iPhone 照片到 `tests\assets\sample.heic` 就行，
> 详见 `tests\assets\README.md`。

---

## 打安装包

```powershell
# 先装一次 Inno Setup（免费、开源、无广告）
winget install --id JRSoftware.InnoSetup

# 一条命令：自检 → 发布 → 出安装包
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

产物在 `dist\电脑工具百宝箱-1.0.0-安装包.exe`。细节见 `docs\03-打包成安装包.md`。

想要小一点的安装包（省掉 .NET 运行时：发布目录从 **158 MB** 降到 **26.5 MB**）：

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -FrameworkDependent
```

---

## 实测验证情况

| 功能 | 状态 |
|---|---|
| 图片 PNG ↔ JPG / WEBP / BMP / TIFF 互转 | ✅ 实测通过（5 种目标格式全过） |
| **HEIC（iPhone 照片）→ JPG / PNG / WEBP** | ✅ 实测通过，两个真实样张 |
| 转成 HEIC | ⚠️ **做不到**，见下文 |
| 界面（三步流程、进度、取消、批量、拖拽） | ✅ 实测能启动、能转、无异常（自包含发布版也实测能启动） |
| 缺组件 / 坏文件时的中文提示 | ✅ 实测通过（下面几条「没跑真实转换」的，验证的正是这些提示） |
| 音频 / 视频互相转、提取声音 | ⏳ 代码已就绪，**但本机没跑成真实转换**：ffmpeg 下载包有 106 MB，本机网络只有 20–50 KB/s，没能下完（GitHub 的 release 下载在本机还被挡了） |
| Word / Excel / PPT / TXT / MD → PDF | ⏳ 同上，需要机器上有 LibreOffice |
| PDF → 图片 / 纯文本 / Word | ⏳ 同上，需要 Poppler |

**这三项怎么自己验证**：`tools\get-tools.ps1` 下载好组件后，跑一次自检就知道 ——
自检会自动识别组件在不在，在就做真实转换，不在才退回去验证提示文案。

```powershell
powershell -File tools\get-tools.ps1
dotnet run --project tests\ToolBox.SmokeTest -c Release
```

### 一个必须说清楚的限制：HEIC 只能读、不能写

实测 `Magick.NET-Q16-x64 14.17.1` 的格式能力：

```
Heic : read=True   write=False     ← 有解码器，没有编码器
Jpg / Png / WebP / Bmp / Tiff : read=True write=True
```

原因：HEIC 编码依赖 x265，因专利授权问题不随包分发。
所以 **「JPG/PNG → HEIC」做不到**，「HEIC → JPG/PNG/WEBP」完全没问题
（这才是大家真正的需求：把 iPhone 照片变成到处都能看的格式）。

程序里干脆没放「转成 HEIC」这个按钮，而不是放一个点了就报错的按钮。
真要支持写 HEIC，得另外塞一个带 x265 的编码器，会引入专利授权问题，不建议第一版做。

---

## 东西都放在哪

| 什么 | 在哪 |
|---|---|
| 转好的文件 | `C:\Users\你的名字\转换结果\` |
| 出错的日志（排错用） | `%LOCALAPPDATA%\电脑工具百宝箱\运行日志.txt` |
| 免安装组件 | 程序目录下的 `tools\`（跟着 exe 走，整个文件夹拷走也能用） |
| 临时文件 | `%TEMP%\电脑工具百宝箱\`，转换完就删 |

程序**只写本地文件，一个字节都不上传**，断网状态下所有功能照常。

---

## 文档

| 文件 | 内容 |
|---|---|
| `docs\01-界面草图.md` | 四个界面的 ASCII 草图、字号颜色表、需求对应关系 |
| `docs\02-技术选型与项目结构.md` | 为什么选 WPF 不选 Tauri、引擎分工表、目录结构、8 个踩坑记录 |
| `docs\03-打包成安装包.md` | 发布参数、Inno Setup 说明、体积优化、签名、怎么自证无广告 |

## 源码地图

```
src\ToolBox.App\
├── MainWindow.xaml            全部界面（首页 + 转换页），代码里只接按钮点击
├── ViewModels\MainViewModel.cs 三步流程、批量、进度、取消
└── Services\
    ├── FormatCatalog.cs        ★ 所有格式定义；加格式只改这一个文件
    ├── ConversionService.cs    ★ 调度 + 磁盘空间预检 + 异常翻译
    ├── ToolLocator.cs          找 ffmpeg / soffice / poppler / pandoc
    ├── ProcessRunner.cs        跑外部程序（不弹黑窗、参数不乱码、取消杀整棵树）
    ├── FriendlyError.cs        技术异常 → 大白话
    └── Converters\             Image / Ffmpeg / LibreOffice / Pandoc / Pdf 五个转换器
```

一共 19 个源文件，没有第三方 MVVM 框架、没有依赖注入容器、没有日志库。

## 加新格式只要改两处

1. `Services\FormatCatalog.cs` —— 在对应类别的 `Targets` 里加一行，
   界面上自动多出一个按钮，**XAML 一个字都不用改**。
2. `Services\ConversionService.cs` —— 在分派处加一个分支，指向已有转换器（或新写一个）。

## 开源与许可

本仓库的代码用 **MIT 许可**，见 [LICENSE](LICENSE)。

运行时依赖的第三方组件（FFmpeg / Poppler / Pandoc / LibreOffice / Magick.NET）
**不在本仓库里**，各有各的许可，商用或再分发前请看
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
