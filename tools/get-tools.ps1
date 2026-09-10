<#
.SYNOPSIS
    一次性下载「电脑工具百宝箱」需要的离线转换组件。

.DESCRIPTION
    联网跑一次，之后程序就能完全离线使用。
    组件都放进仓库根目录的 tools\ 下，发布时会自动复制到程序目录的 tools\ 子目录。

    下载内容：
      FFmpeg     音视频转换（约 106 MB，gyan.dev 的 essentials 构建，
                 含 libmp3lame / libx264 / libvorbis / aac）
      Poppler    PDF 转图片 / 转文字（约 40 MB）
      Pandoc     Markdown 排版（约 40 MB，可选：没有它也能转，只是不排版）

    LibreOffice（文档转 PDF 用，约 350 MB）默认不下载，因为它本来就是独立安装的软件，
    程序会自动去 C:\Program Files\LibreOffice 找。想一起装可以加 -IncludeLibreOffice。

    下载方式：用系统自带的 curl.exe 分段并行下载。
    很多网络对单个连接限速（实测单连接 54 KB/s，8 个连接能到 266 KB/s，快约 5 倍），
    所以默认把文件切成若干段同时下、再拼起来；服务器不支持分段会自动退回单连接。
    Windows PowerShell 5.1 和 PowerShell 7 都能跑。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\get-tools.ps1
    powershell -ExecutionPolicy Bypass -File tools\get-tools.ps1 -SkipFfmpeg -Connections 16
#>
[CmdletBinding()]
param(
    [switch]$SkipFfmpeg,
    [switch]$SkipPoppler,
    [switch]$SkipPandoc,
    [switch]$IncludeLibreOffice,

    # 分段并行下载用几个连接。网络对单连接限速时，调大能明显变快。
    [ValidateRange(1, 32)]
    [int]$Connections = 8
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # 关掉进度条，大文件快好几倍

# Windows PowerShell 5.1 默认可能只用 TLS 1.0，而 GitHub 只收 TLS 1.2。
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$toolsRoot = $PSScriptRoot
$downloadRoot = Join-Path $toolsRoot '.downloads'
$partRoot = Join-Path $downloadRoot 'parts'
$oneMb = 1MB

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

# 用一次「只取第 0 个字节」的请求问出文件总大小，顺便验证服务器支不支持分段。
function Get-RemoteSize([string]$Curl, [string]$Url) {
    $headers = & $Curl -sL --max-time 40 -r 0-0 -D - -o NUL $Url 2>&1
    foreach ($line in $headers) {
        if ($line -match '^content-range:\s*bytes\s+\d+-\d+/(\d+)') { return [int64]$Matches[1] }
    }
    return 0   # 取不到（或服务器不支持 Range）就返回 0，调用方会退回单连接
}

function Remove-Parts {
    if (Test-Path $partRoot) { Remove-Item $partRoot -Recurse -Force -ErrorAction SilentlyContinue }
}

# 分段并行下载。成功返回 $true；任何一步不对劲都返回 $false，由调用方单连接重来。
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
                '-sL', '--fail', '--retry', '3', '--retry-delay', '2',
                '--max-time', '3600',
                '-r', "$from-$to",
                '-o', $part,
                $Url
            )
        }
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    foreach ($job in $jobs) { $job.Process | Wait-Process -ErrorAction SilentlyContinue }
    $sw.Stop()

    # 每一段都必须拿到预期字节数。少一个字节就说明服务器没按 Range 返回，整批作废。
    foreach ($job in $jobs) {
        if (-not (Test-Path $job.Path)) { Write-Warning "    第 $($job.Index) 段没落盘"; return $false }
        $actual = (Get-Item $job.Path).Length
        if ($actual -ne $job.Expected) {
            Write-Warning "    第 $($job.Index) 段大小不对（要 $($job.Expected)，实际 $actual）"
            return $false
        }
    }

    # 按顺序拼起来
    $outStream = [System.IO.File]::Create($Target)
    try {
        foreach ($job in ($jobs | Sort-Object Index)) {
            $inStream = [System.IO.File]::OpenRead($job.Path)
            try { $inStream.CopyTo($outStream, $oneMb) } finally { $inStream.Dispose() }
        }
    }
    finally { $outStream.Dispose() }

    $finalSize = (Get-Item $Target).Length
    if ($finalSize -ne $Total) {
        Write-Warning "    拼起来之后大小不对（要 $Total，实际 $finalSize）"
        return $false
    }

    $seconds = [math]::Max($sw.Elapsed.TotalSeconds, 0.001)
    Write-Host ("    {0} 个连接并行，{1:N1} 秒，平均 {2:N2} MB/s" -f `
        $Segments, $seconds, (($finalSize / $oneMb) / $seconds))
    return $true
}

function Save-Download {
    param([Parameter(Mandatory)][string]$Url)

    $name = Split-Path $Url -Leaf
    $target = Join-Path $downloadRoot $name
    if (Test-Path $target) {
        $size = [math]::Round((Get-Item $target).Length / $oneMb, 1)
        Write-Host "    已经下载过了：$name（$size MB）"
        return $target
    }

    $curl = Get-CurlPath
    $total = 0
    if ($curl) { $total = Get-RemoteSize $curl $Url }

    if ($curl -and $total -gt (8 * $oneMb) -and $Connections -gt 1) {
        $segments = [Math]::Min($Connections, [Math]::Max(2, [int][math]::Floor($total / $oneMb)))
        Write-Host ("    大小 {0:N1} MB，切成 {1} 段并行下载…" -f ($total / $oneMb), $segments)

        if (Invoke-SegmentedDownload -Curl $curl -Url $Url -Target $target -Total $total -Segments $segments) {
            Remove-Parts
            Write-Host ("    下载完成：{0} MB" -f [math]::Round((Get-Item $target).Length / $oneMb, 1))
            return $target
        }

        Write-Warning '    分段下载失败，改用单连接重来'
        Remove-Item $target -Force -ErrorAction SilentlyContinue
        Remove-Parts
    }

    Write-Host "    正在下载 $name ..."
    if ($curl) {
        & $curl -L --fail --retry 3 --retry-delay 2 -o $target $Url
        if ($LASTEXITCODE -ne 0) { throw "下载失败（curl 退出码 $LASTEXITCODE）：$Url" }
    }
    else {
        Invoke-WebRequest -Uri $Url -OutFile $target -MaximumRedirection 10
    }

    Write-Host ("    下载完成：{0} MB" -f [math]::Round((Get-Item $target).Length / $oneMb, 1))
    return $target
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
            [math]::Round((Get-Item $found.FullName).Length / $oneMb, 1))
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
