<#
.SYNOPSIS
    把所有离线转换组件下载到 tools\ 下，让项目成为一个不依赖外部的完整包。

.DESCRIPTION
    联网跑一次，之后程序完全自给自足：不再依赖机器上碰巧装了 B 站客户端、Git 或 Office。
    产物全部在 tools\ 下，跟着 exe 走，拷到任何一台 Windows 10/11 上都能用。

    下载内容：
      FFmpeg        音视频转换（约 82 MB，带 libx264 / libmp3lame / ffprobe）
      Poppler       PDF 转图片 / 转文字（约 40 MB）
      Pandoc        Markdown 排版（约 40 MB）
      LibreOffice   文档转 PDF、PDF 转 Word（约 349 MB，解包成绿色版放 tools\LibreOffice）

    下载方式：用系统自带的 curl.exe **分段并行**下载。
    很多网络对单连接限速（实测单连接 54 KB/s，8 连接 387 KB/s，快约 7 倍），
    脚本会把文件切成若干段同时下、再拼起来；服务器不支持分段会自动退回单连接。

    每个组件都按「主源 → 备用源」的顺序试，一个不通换下一个。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\get-tools.ps1
    powershell -ExecutionPolicy Bypass -File tools\get-tools.ps1 -SkipLibreOffice -Connections 16
#>
[CmdletBinding()]
param(
    [switch]$SkipFfmpeg,
    [switch]$SkipPoppler,
    [switch]$SkipPandoc,
    [switch]$SkipLibreOffice,

    # 分段并行下载用几个连接。网络对单连接限速时，调大能明显变快。
    [ValidateRange(1, 32)]
    [int]$Connections = 8
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$toolsRoot = $PSScriptRoot
$downloadRoot = Join-Path $toolsRoot '.downloads'
$partRoot = Join-Path $downloadRoot 'parts'
$oneMb = 1MB

# GitHub 有时候连不上，这两个反代实测能用（2026-09 测：446 / 464 KB/s）
$gitHubMirrors = @('', 'https://ghfast.top/', 'https://gh-proxy.com/')

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

function Get-RemoteSize([string]$Curl, [string]$Url) {
    $headers = & $Curl -sL --max-time 40 -r 0-0 -D - -o NUL $Url 2>&1
    foreach ($line in $headers) {
        if ($line -match '^content-range:\s*bytes\s+\d+-\d+/(\d+)') { return [int64]$Matches[1] }
    }
    return 0
}

function Remove-Parts {
    if (Test-Path $partRoot) { Remove-Item $partRoot -Recurse -Force -ErrorAction SilentlyContinue }
}

function Invoke-SegmentedDownload {
    param(
        [Parameter(Mandatory)][string]$Curl,
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][int64]$Total,
        [Parameter(Mandatory)][int]$Segments
    )

    Remove-Parts
    New-Item -ItemType Directory -Path $partRoot -Force | Out-Null

    $chunk = [int64][math]::Ceiling($Total / $Segments)
    $jobs = @()

    for ($i = 0; $i -lt $Segments; $i++) {
        $from = $i * $chunk
        if ($from -ge $Total) { break }
        $to = [math]::Min($from + $chunk - 1, $Total - 1)
        $part = Join-Path $partRoot ("part{0:D3}.bin" -f $i)

        $jobs += [pscustomobject]@{
            Index    = $i
            Path     = $part
            Expected = $to - $from + 1
            Process  = Start-Process -FilePath $Curl -PassThru -WindowStyle Hidden -ArgumentList @(
                '-sL', '--fail', '--retry', '3', '--retry-delay', '2', '--max-time', '7200',
                '-r', "$from-$to", '-o', $part, $Url
            )
        }
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    foreach ($job in $jobs) { $job.Process | Wait-Process -ErrorAction SilentlyContinue }
    $sw.Stop()

    foreach ($job in $jobs) {
        if (-not (Test-Path $job.Path)) { return $false }
        if ((Get-Item $job.Path).Length -ne $job.Expected) { return $false }
    }

    $outStream = [System.IO.File]::Create($Target)
    try {
        foreach ($job in ($jobs | Sort-Object Index)) {
            $inStream = [System.IO.File]::OpenRead($job.Path)
            try { $inStream.CopyTo($outStream, $oneMb) } finally { $inStream.Dispose() }
        }
    }
    finally { $outStream.Dispose() }

    if ((Get-Item $Target).Length -ne $Total) { return $false }

    $seconds = [math]::Max($sw.Elapsed.TotalSeconds, 0.001)
    Write-Host ("    {0} 个连接并行，{1:N1} 秒，平均 {2:N0} KB/s" -f `
        $Segments, $seconds, (($Total / 1KB) / $seconds))
    return $true
}

function Save-FromUrl {
    param([Parameter(Mandatory)][string]$Url, [Parameter(Mandatory)][string]$Target)

    $curl = Get-CurlPath
    $total = 0
    if ($curl) { $total = Get-RemoteSize $curl $Url }

    if ($curl -and $total -gt (8 * $oneMb) -and $Connections -gt 1) {
        $segments = [Math]::Min($Connections, [Math]::Max(2, [int][math]::Floor($total / $oneMb)))
        if (Invoke-SegmentedDownload -Curl $curl -Url $Url -Target $Target -Total $total -Segments $segments) {
            Remove-Parts
            return $true
        }
        Remove-Item $Target -Force -ErrorAction SilentlyContinue
        Remove-Parts
    }

    if ($curl) {
        & $curl -L --fail --retry 3 --retry-delay 2 -o $Target $Url
        return ($LASTEXITCODE -eq 0)
    }

    try {
        Invoke-WebRequest -Uri $Url -OutFile $Target -MaximumRedirection 10
        return $true
    }
    catch { return $false }
}

# 依次试主源和备用源，返回下载好的本地路径
function Save-Download {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Urls
    )

    $target = Join-Path $downloadRoot $Name
    if (Test-Path $target) {
        Write-Host ("    已经下载过了：{0}（{1:N1} MB）" -f $Name, ((Get-Item $target).Length / $oneMb))
        return $target
    }

    foreach ($url in $Urls) {
        $host_ = ([uri]$url).Host
        Write-Host ("    试 {0} …" -f $host_)
        if (Save-FromUrl -Url $url -Target $target) {
            Write-Host ("    下载完成：{0:N1} MB" -f ((Get-Item $target).Length / $oneMb))
            return $target
        }
        Write-Warning ("    {0} 不通，换下一个源" -f $host_)
        Remove-Item $target -Force -ErrorAction SilentlyContinue
    }

    throw "所有源都下载不了 $Name"
}

# GitHub 资源：直连 + 反代都试一遍
function Save-GitHubAsset {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Pattern
    )

    $release = Get-Json "https://api.github.com/repos/$Repository/releases/latest"
    $asset = $release.assets | Where-Object { $_.name -like $Pattern } | Select-Object -First 1
    if (-not $asset) { throw "在 $Repository 的最新发布里找不到匹配 '$Pattern' 的文件。" }

    Write-Host ("    版本 {0} / 文件 {1}（{2:N1} MB）" -f $release.tag_name, $asset.name, ($asset.size / $oneMb))

    $urls = @()
    foreach ($mirror in $gitHubMirrors) { $urls += ($mirror + $asset.browser_download_url) }
    return Save-Download -Name $asset.name -Urls $urls
}

function Get-Json([string]$Url) {
    $curl = Get-CurlPath
    if ($curl) {
        $raw = & $curl -sL --fail --max-time 60 -H 'User-Agent: toolbox-get-tools' $Url
        if ($LASTEXITCODE -eq 0 -and $raw) { return (($raw -join "`n") | ConvertFrom-Json) }
    }
    return Invoke-RestMethod -Uri $Url -Headers @{ 'User-Agent' = 'toolbox-get-tools' }
}

function Expand-Components {
    param([Parameter(Mandatory)][string]$Archive)

    $staging = Join-Path $downloadRoot ("unpack-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    try {
        Expand-Archive -Path $Archive -DestinationPath $staging -Force
    }
    catch {
        Remove-Item $Archive -Force -ErrorAction SilentlyContinue
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
        throw ("压缩包不完整（大概是上次下载中途断了）：{0} 已经删掉，重跑一次会重新下载。" -f (Split-Path $Archive -Leaf))
    }
    return $staging
}

if (-not (Test-Path $downloadRoot)) { New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null }

# ==========================================================================
if (-not $SkipFfmpeg) {
    Write-Step 'FFmpeg（音视频转换）'

    # 用 BtbN 的 gpl-shared：带 ffprobe、含 libx264，而且是 LGPL/GPL 正规发布
    $zip = Save-GitHubAsset -Repository 'BtbN/FFmpeg-Builds' -Pattern '*win64-gpl-shared.zip'
    $staging = Expand-Components -Archive $zip

    $binSource = Get-ChildItem -Path $staging -Recurse -Directory -Filter 'bin' |
        Where-Object { Test-Path (Join-Path $_.FullName 'ffmpeg.exe') } | Select-Object -First 1
    if (-not $binSource) { throw '压缩包结构跟预期不一样，找不到含 ffmpeg.exe 的 bin 目录。' }

    $targetDirectory = Join-Path $toolsRoot 'ffmpeg\bin'
    if (Test-Path $targetDirectory) { Remove-Item $targetDirectory -Recurse -Force }
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

    # shared 构建要连 DLL 一起拷，否则 exe 起不来
    Copy-Item (Join-Path $binSource.FullName '*') $targetDirectory -Recurse -Force

    # ffplay 是个命令行播放器，我们只转码，用不上，删掉省约 18 MB
    Remove-Item (Join-Path $targetDirectory 'ffplay.exe') -Force -ErrorAction SilentlyContinue
    Remove-Item $staging -Recurse -Force

    $ffmpegSize = [math]::Round((Get-ChildItem $targetDirectory -Recurse -File |
        Measure-Object -Property Length -Sum).Sum / $oneMb, 1)
    Write-Host ("    已放置 tools\ffmpeg\bin\（ffmpeg.exe + ffprobe.exe + 依赖 DLL，共 {0} MB）" -f $ffmpegSize)
}

# ==========================================================================
if (-not $SkipPoppler) {
    Write-Step 'Poppler（PDF 转图片 / 转文字）'
    $zip = Save-GitHubAsset -Repository 'oschwartz10612/poppler-windows' -Pattern '*.zip'
    $staging = Expand-Components -Archive $zip

    $targetDirectory = Join-Path $toolsRoot 'poppler'
    if (Test-Path $targetDirectory) { Remove-Item $targetDirectory -Recurse -Force }

    $libraryBin = Get-ChildItem -Path $staging -Recurse -Directory -Filter 'bin' |
        Where-Object { $_.FullName -like '*Library*' } | Select-Object -First 1
    if (-not $libraryBin) { throw '压缩包结构跟预期不一样，找不到 Library\bin 目录。' }

    $popplerRoot = Split-Path (Split-Path $libraryBin.FullName -Parent) -Parent
    Move-Item $popplerRoot $targetDirectory

    # 只留运行时需要的：lib 是导入库、include 是头文件，编译才用，运行不需要（省约 18 MB）
    Remove-Item (Join-Path $targetDirectory 'Library\lib') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $targetDirectory 'Library\include') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $staging -Recurse -Force

    foreach ($exe in 'pdftoppm.exe', 'pdftotext.exe') {
        if (-not (Test-Path (Join-Path $targetDirectory "Library\bin\$exe"))) { throw "搬运之后找不到 $exe" }
    }
    Write-Host '    已放置 tools\poppler\Library\bin\（pdftoppm / pdftotext）'
}

# ==========================================================================
if (-not $SkipPandoc) {
    Write-Step 'Pandoc（Markdown 排版）'
    try {
        $zip = Save-GitHubAsset -Repository 'jgm/pandoc' -Pattern '*windows-x86_64.zip'
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

# ==========================================================================
if (-not $SkipLibreOffice) {
    Write-Step 'LibreOffice（文档转 PDF、PDF 转 Word）—— 约 349 MB，最慢的一步'

    # 绿色版：下 MSI，然后用 msiexec /a 做"管理安装"把它解包成文件夹，
    # 不写注册表、不需要管理员权限，整个 tools\LibreOffice 跟着程序走。
    $version = '25.8.7'
    $file = "LibreOffice_${version}_Win_x86-64.msi"
    $urls = @(
        "https://mirrors.aliyun.com/libreoffice/stable/$version/win/x86_64/$file",
        "https://download.documentfoundation.org/libreoffice/stable/$version/win/x86_64/$file"
    )

    $msi = Save-Download -Name $file -Urls $urls

    $targetDirectory = Join-Path $toolsRoot 'LibreOffice'
    if (Test-Path $targetDirectory) { Remove-Item $targetDirectory -Recurse -Force }

    $extract = Join-Path $downloadRoot 'lo-extract'
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    New-Item -ItemType Directory -Path $extract -Force | Out-Null

    Write-Host '    正在解包成绿色版（msiexec /a，不安装、不写注册表）…'
    $p = Start-Process msiexec.exe -PassThru -Wait -WindowStyle Hidden -ArgumentList @(
        '/a', "`"$msi`"", '/qn', "TARGETDIR=`"$extract`""
    )
    if ($p.ExitCode -ne 0) { throw "解包失败，msiexec 退出码 $($p.ExitCode)" }

    $soffice = Get-ChildItem -Path $extract -Recurse -Filter 'soffice.exe' | Select-Object -First 1
    if (-not $soffice) { throw '解包完了但找不到 soffice.exe' }

    # soffice.exe 在 ...\program\ 下，它的上一级就是 LibreOffice 根目录
    $programDirectory = Split-Path $soffice.FullName -Parent
    $libreRoot = Split-Path $programDirectory -Parent
    Move-Item $libreRoot $targetDirectory
    Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue

    # 瘦身：LibreOffice 默认带 120 多种语言的界面资源和 57 个拼写词典，
    # 文档转换一个都用不上。实测 1,502 MB -> 797 MB，省下 700 多 MB。
    # 删掉的语言会自动回落到英文，不影响转换。
    $keepLanguages = 'en_GB', 'en_ZA', 'zh_CN', 'zh_TW'
    Get-ChildItem (Join-Path $targetDirectory 'program\resource') -Directory -ErrorAction SilentlyContinue |
        Where-Object { $keepLanguages -notcontains $_.Name } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Get-ChildItem (Join-Path $targetDirectory 'share\extensions') -Directory -Filter 'dict-*' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'dict-en' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    foreach ($unused in 'share\extensions\nlpsolver', 'share\extensions\wiki-publisher', 'share\gallery') {
        Remove-Item (Join-Path $targetDirectory $unused) -Recurse -Force -ErrorAction SilentlyContinue
    }

    Get-ChildItem (Join-Path $targetDirectory 'share\registry\res') -File -Filter 'fcfg_langpack_*.xcd' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch '_(en|zh)[-_]' } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $size = [math]::Round((Get-ChildItem $targetDirectory -Recurse -File |
        Measure-Object -Property Length -Sum).Sum / $oneMb, 1)
    Write-Host ("    已放置 tools\LibreOffice\program\soffice.exe（共 {0:N0} MB）" -f $size)
}

# ==========================================================================
Write-Step '完成，确认一下都齐了'

$checks = @(
    @{ Label = 'FFmpeg';      Path = 'ffmpeg\bin\ffmpeg.exe' },
    @{ Label = 'FFprobe';     Path = 'ffmpeg\bin\ffprobe.exe' },
    @{ Label = 'Poppler';     Path = 'poppler\Library\bin\pdftoppm.exe' },
    @{ Label = 'Pandoc';      Path = 'pandoc\pandoc.exe' },
    @{ Label = 'LibreOffice'; Path = 'LibreOffice\program\soffice.exe' }
)
foreach ($c in $checks) {
    $full = Join-Path $toolsRoot $c.Path
    if (Test-Path $full) {
        Write-Host ("    [有] {0,-12} {1}" -f $c.Label, $c.Path) -ForegroundColor Green
    }
    else {
        Write-Host ("    [缺] {0,-12} {1}" -f $c.Label, $c.Path) -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '跑一次自检，确认转换引擎都通了：'
Write-Host '    dotnet run --project tests\ToolBox.SmokeTest -c Release' -ForegroundColor Green
Write-Host ''
Write-Host '下载缓存放在 tools\.downloads（可以随时删；删了下次要重下）。'
Write-Host 'tools\ 里的东西是不进版本库的，发布时会自动跟着复制进安装包。'
