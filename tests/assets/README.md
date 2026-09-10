# 测试素材

## dummy.pdf ✅ 已包含

- **来源**：<https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf>
- **内容**：只有一句 "Dummy PDF file"，13 KB，W3C 的通用测试文件
- **用途**：`ToolBox.SmokeTest` 用它验证「PDF 转图片」和「PDF 转纯文本」

## sample.heic ⛔ 没有包含在本仓库里

自检程序本来靠它验证「iPhone 照片（HEIC）转 JPG」这条最关键的路径，但它被拿掉了：

- 它来自 Nokia 的 HEIF 示例仓库 `nokiatech/heif`
  （文件路径 `gh-pages/content/images/autumn_1440x960.heic`）
- 那个仓库的 `LICENSE.TXT` 是 Nokia 自己定的 **HEIF License v2.1**，
  只授权在 "Licensed Field" 内使用、运行、修改、复制**软件**，
  **没有明确授权再分发仓库里的示例图片**（GitHub 把它标记为 SPDX `NOASSERTION`）

那是一张真实拍摄的照片，属于有版权的创作内容。把它放进公开仓库法律上是含糊的，
所以本仓库不带它 —— 代码可以随便看，第三方素材不要随便传。

### 想验证 HEIC 这条路，自己放一张进来就行

文件名必须是 `sample.heic`，放在这个目录下：

```powershell
# 最快的办法：iPhone 传一张照片过来，改个名就行
Copy-Item D:\照片\IMG_1234.HEIC .\sample.heic

# 然后跑自检，它会自动发现这张图，并验证 HEIC -> JPG / PNG / WEBP
dotnet run --project tests\ToolBox.SmokeTest -c Release
```

文件不在的时候，自检程序会**明确打印「跳过」并说明原因**，
既不会失败，也不会假装通过。
