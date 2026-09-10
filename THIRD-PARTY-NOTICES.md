# 第三方组件与素材说明

本仓库自己的代码用 MIT 许可（见 [LICENSE](LICENSE)）。
下面这些东西**不属于本仓库的代码**，各有各的许可，商用或再分发前请自行确认。

## 一、程序运行时依赖的组件

这些组件**不在本仓库里**，由 `tools/get-tools.ps1` 在用户机器上下载，
或由用户自行安装。它们的许可与 MIT 无关。

| 组件 | 用途 | 许可 |
|---|---|---|
| FFmpeg | 音视频转换 | LGPL / GPL（取决于具体构建；`get-tools.ps1` 下的是 gyan.dev 的 essentials 构建） |
| Poppler | PDF 转图片 / 纯文本 | GPL-2.0 / GPL-3.0 |
| Pandoc | Markdown 排版 | GPL-2.0-or-later |
| LibreOffice | 文档转 PDF（用户自行安装） | MPL-2.0 |
| Magick.NET / ImageMagick | 图片转换（通过 NuGet 引用） | Apache-2.0 / ImageMagick License |

> 注意：如果将来要把 FFmpeg 或 Poppler **打进安装包一起分发**，
> 那就变成了「再分发二进制」，需要遵守它们各自的许可（GPL 系还要提供相应源码）。
> 本仓库不包含这些二进制，所以不存在这个问题。

## 二、测试素材

| 文件 | 来源 | 说明 |
|---|---|---|
| `tests/assets/dummy.pdf` | [W3C 公开测试素材](https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf) | 内容只有一句 "Dummy PDF file"，13 KB，版权归 W3C |

`tests/assets/sample.heic` 曾经存在，但**已从本仓库移除**，原因见
[tests/assets/README.md](tests/assets/README.md) ——
它来自 Nokia 的 HEIF 示例仓库，那里的 LICENSE 没有明确授权再分发示例图片。
想跑 HEIC 那条测试，自己放一张 iPhone 照片进去即可。
