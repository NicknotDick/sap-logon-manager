# SAP Logon Manager - publish script (ASCII only: Windows PowerShell 5.1 reads .ps1 as ANSI)
# Usage: powershell -ExecutionPolicy Bypass -File build\publish.ps1
$ErrorActionPreference = 'Stop'

# 复制重试：避免 exe 被安全软件/占用短暂锁定导致复制半途失败
function Copy-Robust([string]$src, [string]$dst) {
    for ($i = 0; $i -lt 6; $i++) {
        try {
            Copy-Item $src $dst -Force -ErrorAction Stop
            return
        }
        catch {
            if ($i -eq 5) { throw ("copy failed: " + $dst + " -> " + $_.Exception.Message) }
            Start-Sleep -Milliseconds 600
        }
    }
}

$root = Split-Path -Parent $PSScriptRoot
$sdkRoot = 'C:\Users\camyo\.workbuddy\binaries\dotnet-sdk-8'
$dotnet = Join-Path $sdkRoot 'dotnet.exe'

if (-not (Test-Path $dotnet)) {
    throw "dotnet SDK not found: $dotnet"
}

$targetName = if ($args.Count -ge 1) { [string]$args[0] } else { 'SapLauncher.exe' }

$env:DOTNET_ROOT = $sdkRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$project = Join-Path $root 'src\SapLauncher\SapLauncher.csproj'
# 带时间戳的子目录：避免残留产物占用导致写入失败
$publishDir = Join-Path $root ('publish/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$distDir = Join-Path $root 'dist'

Write-Host "[1/4] prepare output dirs" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host "[2/4] dotnet publish (win-x64, self-contained, single file, NO self-extract)" -ForegroundColor Cyan
# 注意：本机安全策略禁止从 %TEMP%\.net 加载原生 DLL，故关闭原生库自解压，
# 让 WPF 原生库(D3DCompiler/PresentationNative/wpfgfx 等)留在 exe 同目录直接加载。
& $dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:SatelliteResourceLanguages=zh-Hans `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

Write-Host "[3/4] copy to dist (exe + native dlls)" -ForegroundColor Cyan
$exe = Join-Path $publishDir 'SapLauncher.exe'
if (-not (Test-Path $exe)) { throw "publish output not found: $exe" }
$target = Join-Path $distDir $targetName
# 拷贝 exe（改名为目标名）以及所有必需的旁置原生 DLL
Get-ChildItem $publishDir -File | ForEach-Object {
    if ($_.Name -eq 'SapLauncher.exe') {
        Copy-Robust $_.FullName $target
    }
    else {
        Copy-Robust $_.FullName (Join-Path $distDir $_.Name)
    }
}

Write-Host "[4/4] verify" -ForegroundColor Cyan
$info = Get-Item $target
$hash = (Get-FileHash $target -Algorithm SHA256).Hash
Write-Host ("file  : " + $info.FullName)
Write-Host ("size  : " + [math]::Round($info.Length / 1MB, 1) + " MB")
Write-Host ("sha256: " + $hash)
# 清理 7 天前的旧发布目录
Get-ChildItem (Join-Path $root 'publish') -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-7) } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "done." -ForegroundColor Green
