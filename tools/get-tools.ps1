<#
.SYNOPSIS
    一次性下载「电脑工具百宝箱」需要的离线转换组件。

.DESCRIPTION
    联网跑一次，之后程序就能完全离线使用。
    组件都放进仓库根目录的 tools\ 下，发布时会自动复制到程序目录的 tools\ 子目录。

    下载内容：
      FFmpeg     音视频转换（约 106 MB，gyan.dev 的 essentials 构建，
                 含 libmp3lame / libx264 / libvorbis / aac）
      Poppler    PDF 转图片 / 转文字（约 20 MB）
      Pandoc     Markdown 排版（约 30 MB，可选：没有它也能转，只是不排版）

    LibreOffice（文档转 PDF 用，约 350 MB）默认不下载，因为它本来就是独立安装的软件，
    程序会自动去 C:\Program Files\LibreOffice 找。想一起装可以加 -IncludeLibreOffice。

    优先用系统自带的 curl.exe 下载：比 Invoke-WebRequest 快很多，也不吃内存。
    Windows PowerShell 5.1 和 PowerShell 7 都能跑。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\get-tools.ps1
    powershell -ExecutionPolicy Bypass -File tools\get-tools.ps1 -SkipFfmpeg
#>
[CmdletBinding()]
param(
    [switch]$SkipFfmpeg,
    [switch]$SkipPoppler,
    [switch]$SkipPandoc,
    [switch]$IncludeLibreOffice
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # 关掉进度条，大文件快好几倍

# Windows PowerShell 5.1 默认可能只用 TLS 1.0，而 GitHub 只收 TLS 1.2。
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$toolsRoot = $PSScriptRoot
$downloadRoot = Join-Path $toolsRoot '.downloads'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-CurlPath {
    $candidate = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (Test-Path $candidate) { return $candidate }
    $onPath = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

function Get-Json([string]$Url) {
    $curl = Get-CurlPath
    if ($curl) {
        $raw = & $curl -sL --fail --max-time 60 -H 'User-Agent: toolbox-get-tools' $Url
        if ($LASTEXITCODE -eq 0 -and $raw) { return (($raw -join "`n") | ConvertFrom-Json) }
    }

    return Invoke-RestMethod -Uri $Url -Headers @{ 'User-Agent' = 'toolbox-get-tools' }
}

function Get-GitHubAssetUrl {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Pattern
    )

    $release = Get-Json "https://api.github.com/repos/$Repository/releases/latest"
    $asset = $release.assets | Where-Object { $_.name -like $Pattern } | Select-Object -First 1
    if (-not $asset) { throw "在 $Repository 的最新发布里找不到匹配 '$Pattern' 的文件。" }

    Write-Host "    版本 $($release.tag_name) / 文件 $($asset.name)"
    return $asset.browser_download_url
}

function Save-Download {
    param([Parameter(Mandatory)][string]$Url)

    $name = Split-Path $Url -Leaf
    $target = Join-Path $downloadRoot $name
    if (Test-Path $target) {
        $size = [math]::Round((Get-Item $target).Length / 1MB, 1)
        Write-Host "    已经下载过了：$name（$size MB）"
        return $target
    }

    Write-Host "    正在下载 $name ..."
    $curl = Get-CurlPath

    if ($curl) {
        # curl 在 Win10 1803+ 是系统自带的，边下边写盘，快而且内存占用低
        & $curl -L --fail --retry 3 --retry-delay 2 -o $target $Url
        if ($LASTEXITCODE -ne 0) { throw "下载失败（curl 退出码 $LASTEXITCODE）：$Url" }
    }
    else {
        Invoke-WebRequest -Uri $Url -OutFile $target -MaximumRedirection 10
    }

    $size = [math]::Round((Get-Item $target).Length / 1MB, 1)
    Write-Host "    下载完成：$size MB"
    return $target
}

function Expand-Components {
    param([Parameter(Mandatory)][string]$Archive)

    $staging = Join-Path $downloadRoot ("unpack-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))

    try {
        Expand-Archive -Path $Archive -DestinationPath $staging -Force
    }
    catch {
        # 上次下载被中途掐断，留下了半个压缩包 —— 截断的 zip 到这一步才会暴露出来。
        # 直接删掉它，用户重跑一次脚本就会重新下载，不用自己去文件夹里找。
        Remove-Item $Archive -Force -ErrorAction SilentlyContinue
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
        throw ("压缩包不完整（大概是上次下载中途断了）：{0} 已经删掉，" +
               "重新跑一次本脚本就会重新下载。") -f (Split-Path $Archive -Leaf)
    }

    return $staging
}

if (-not (Test-Path $downloadRoot)) { New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null }

# --------------------------------------------------------------------------
if (-not $SkipFfmpeg) {
    Write-Step 'FFmpeg（音视频转换）'
    $url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
    $zip = Save-Download -Url $url
    $staging = Expand-Components -Archive $zip

    $targetDirectory = Join-Path $toolsRoot 'ffmpeg\bin'
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

    foreach ($executable in 'ffmpeg.exe', 'ffprobe.exe') {
        $found = Get-ChildItem -Path $staging -Recurse -Filter $executable | Select-Object -First 1
        if (-not $found) { throw "压缩包里没有找到 $executable" }
        Copy-Item $found.FullName -Destination (Join-Path $targetDirectory $executable) -Force
        Write-Host ("    已放置 tools\ffmpeg\bin\{0}（{1} MB）" -f $executable,
            [math]::Round((Get-Item $found.FullName).Length / 1MB, 1))
    }

    Remove-Item $staging -Recurse -Force
}

# --------------------------------------------------------------------------
if (-not $SkipPoppler) {
    Write-Step 'Poppler（PDF 转图片 / 转文字）'
    $url = Get-GitHubAssetUrl -Repository 'oschwartz10612/poppler-windows' -Pattern '*.zip'
    $zip = Save-Download -Url $url
    $staging = Expand-Components -Archive $zip

    # 压缩包里是 poppler-<版本>\Library\bin\... 这种结构，整体搬到 tools\poppler
    $targetDirectory = Join-Path $toolsRoot 'poppler'
    if (Test-Path $targetDirectory) { Remove-Item $targetDirectory -Recurse -Force }

    $libraryBin = Get-ChildItem -Path $staging -Recurse -Directory -Filter 'bin' |
        Where-Object { $_.FullName -like '*Library*' } | Select-Object -First 1
    if (-not $libraryBin) { throw '压缩包结构跟预期不一样，找不到 Library\bin 目录。' }

    $popplerRoot = Split-Path (Split-Path $libraryBin.FullName -Parent) -Parent
    Move-Item $popplerRoot $targetDirectory
    Remove-Item $staging -Recurse -Force

    foreach ($executable in 'pdftoppm.exe', 'pdftotext.exe') {
        if (-not (Test-Path (Join-Path $targetDirectory "Library\bin\$executable"))) {
            throw "搬运之后找不到 $executable"
        }
    }
    Write-Host '    已放置 tools\poppler\Library\bin\pdftoppm.exe 等'
}

# --------------------------------------------------------------------------
if (-not $SkipPandoc) {
    Write-Step 'Pandoc（Markdown 排版，可选）'
    try {
        $url = Get-GitHubAssetUrl -Repository 'jgm/pandoc' -Pattern '*windows-x86_64.zip'
        $zip = Save-Download -Url $url
        $staging = Expand-Components -Archive $zip

        $targetDirectory = Join-Path $toolsRoot 'pandoc'
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

        $found = Get-ChildItem -Path $staging -Recurse -Filter 'pandoc.exe' | Select-Object -First 1
        if (-not $found) { throw '压缩包里没有找到 pandoc.exe' }

        Copy-Item $found.FullName -Destination (Join-Path $targetDirectory 'pandoc.exe') -Force
        Remove-Item $staging -Recurse -Force
        Write-Host '    已放置 tools\pandoc\pandoc.exe'
    }
    catch {
        Write-Warning "Pandoc 没装上：$($_.Exception.Message)"
        Write-Warning '没装也能用，只是 Markdown 转 PDF 不会排版标题。'
    }
}

# --------------------------------------------------------------------------
if ($IncludeLibreOffice) {
    Write-Step 'LibreOffice（文档转 PDF，约 350 MB）'
    Write-Host '    建议直接去官网装正式版，程序会自动找到它：' -ForegroundColor Yellow
    Write-Host '    https://zh-cn.libreoffice.org/download/libreoffice/' -ForegroundColor Yellow
    Write-Host '    装完之后不需要做任何配置。' -ForegroundColor Yellow
}

# --------------------------------------------------------------------------
Write-Step '完成'
Write-Host '现在运行一次自检，确认组件都找到了：'
Write-Host '    dotnet run --project tests\ToolBox.SmokeTest -c Release' -ForegroundColor Green
Write-Host ''
Write-Host '发布时组件会自动跟着走（见 src\ToolBox.App\ToolBox.App.csproj 里的 tools 复制规则）。'
Write-Host '下载缓存放在 tools\.downloads，可以随时删掉。'
