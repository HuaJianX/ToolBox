<#
.SYNOPSIS
    一条命令：编译程序 → 打成无广告安装包。

.DESCRIPTION
    做四件事：
      1. （可选）跑一遍自检，确认转换引擎是好的
      2. dotnet publish 发布到 dist\publish
      3. 确保 installer\setup.iss 是带 BOM 的 UTF-8（不然中文乱码）
      4. 调 Inno Setup 的 ISCC.exe 编译出安装包

.PARAMETER FrameworkDependent
    发布成「依赖 .NET 运行时」的版本。安装包从约 75 MB 掉到约 3 MB，
    但用户机器上得先装 .NET 10 桌面运行时（Win10/11 默认没有）。

.PARAMETER SkipTests
    跳过自检，直接打包。

.EXAMPLE
    pwsh -File installer\build-installer.ps1
    pwsh -File installer\build-installer.ps1 -FrameworkDependent -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$FrameworkDependent,
    [switch]$SkipTests,
    [string]$InnoSetupCompiler
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $root 'src\ToolBox.App\ToolBox.App.csproj'
$publishDirectory = Join-Path $root 'dist\publish'
$issPath = Join-Path $PSScriptRoot 'setup.iss'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

# Inno Setup 和 Windows 都要求带 BOM 的 UTF-8 才能正确读中文，这里兜一道。
function Ensure-Utf8Bom([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    if ($hasBom) { return }

    $text = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($Path, $text, [System.Text.UTF8Encoding]::new($true))
    Write-Host "    已为 $(Split-Path $Path -Leaf) 补上 UTF-8 BOM（否则中文会乱码）"
}

# --------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Step '第 1 步 / 4：跑自检'
    dotnet run --project (Join-Path $root 'tests\ToolBox.SmokeTest\ToolBox.SmokeTest.csproj') -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "自检没通过（退出码 $LASTEXITCODE），先修好再打包。想强制打包就加 -SkipTests。"
    }
}
else {
    Write-Host '（已跳过自检）' -ForegroundColor Yellow
}

# --------------------------------------------------------------------------
Write-Step '第 2 步 / 4：检查免安装组件'

$ffmpeg = Join-Path $root 'tools\ffmpeg\bin\ffmpeg.exe'
$poppler = Join-Path $root 'tools\poppler\Library\bin\pdftoppm.exe'
$pandoc = Join-Path $root 'tools\pandoc\pandoc.exe'

foreach ($item in @(
    @{ Path = $ffmpeg;  Name = 'FFmpeg（音视频）' },
    @{ Path = $poppler; Name = 'Poppler（PDF）' },
    @{ Path = $pandoc;  Name = 'Pandoc（Markdown 排版，可选）' }
)) {
    if (Test-Path $item.Path) {
        Write-Host "    [有] $($item.Name)"
    }
    else {
        Write-Warning "    [缺] $($item.Name) —— 先跑一次 pwsh -File tools\get-tools.ps1"
    }
}

# --------------------------------------------------------------------------
Write-Step '第 3 步 / 4：发布程序'

if (Test-Path $publishDirectory) { Remove-Item $publishDirectory -Recurse -Force }

$selfContained = (-not $FrameworkDependent).ToString().ToLowerInvariant()

dotnet publish $projectPath `
    -c $Configuration `
    -r $RuntimeIdentifier `
    -o $publishDirectory `
    -p:SelfContained=$selfContained `
    -p:DebugType=none `
    -p:GenerateDocumentationFile=false

if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }

$publishedSize = [Math]::Round(((Get-ChildItem $publishDirectory -Recurse -File |
    Measure-Object -Property Length -Sum).Sum / 1MB), 1)
Write-Host "    发布完成：$publishedSize MB（自包含=$selfContained）"
Write-Host "    目录：$publishDirectory"

# --------------------------------------------------------------------------
Write-Step '第 4 步 / 4：编译安装包'

Ensure-Utf8Bom $issPath

$compilerCandidates = @(
    $InnoSetupCompiler,
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) }

$compiler = $compilerCandidates | Select-Object -First 1

if (-not $compiler) {
    Write-Host ''
    Write-Warning '没找到 Inno Setup 的 ISCC.exe。'
    Write-Host '装一下就好（免费、开源、无广告）：' -ForegroundColor Yellow
    Write-Host '    https://jrsoftware.org/isdl.php' -ForegroundColor Yellow
    Write-Host '  或者用 winget：' -ForegroundColor Yellow
    Write-Host '    winget install --id JRSoftware.InnoSetup' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "程序本身已经发布好了，可以直接用：$publishDirectory" -ForegroundColor Green
    exit 1
}

Write-Host "    用这个编译器：$compiler"
& $compiler $issPath
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败（退出码 $LASTEXITCODE）" }

# --------------------------------------------------------------------------
Write-Step '完成'

$installer = Get-ChildItem (Join-Path $root 'dist') -Filter '*.exe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($installer) {
    Write-Host ("安装包：{0}" -f $installer.FullName) -ForegroundColor Green
    Write-Host ("大小  ：{0} MB" -f [Math]::Round($installer.Length / 1MB, 1)) -ForegroundColor Green
}
