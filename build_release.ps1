# FaraBotModerator Build & Package Script
# -PublishOnly: dotnet publish のみ実行 / -PackageOnly: インストーラー作成のみ実行
# (CI で publish とパッケージングの間に exe へ署名するために分割できるようにしている)
# -Version: アプリに埋め込むバージョン（例: v1.2.0 または 1.2.0）。省略すると、既定（0.0.0-dev。画面には「開発版」と表示）になる
param(
    [switch]$PublishOnly,
    [switch]$PackageOnly,
    [string]$Version = ""
)

Write-Host "Starting Build Process..." -ForegroundColor Cyan

$projectName = "FaraBotModerator"
$publishDir = "bin\Release\net10.0-windows10.0.22621.0\publish\win-x64"

if (-not $PackageOnly) {
    # 1. Clean and Publish
    Write-Host "Step 1: Publishing as a single-file executable..." -ForegroundColor Yellow
    $publishArgs = @("-c", "Release", "/p:PublishProfile=FolderProfile")
    if ($Version.Trim() -ne "") {
        # v1.2.0 の先頭の v を除いて、アプリのバージョン（1.2.0）として埋め込む。形式は X.Y.Z（任意で -prerelease）
        $appVersion = $Version.Trim().TrimStart("v")
        if ($appVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
            Write-Host "Invalid version: $Version (example: v1.2.0)" -ForegroundColor Red
            exit 1
        }
        $publishArgs += "/p:Version=$appVersion"
    }
    dotnet publish "$projectName.csproj" @publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed!" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Host "Publish completed. Files are in: $publishDir" -ForegroundColor Green
}

if ($PublishOnly) {
    Write-Host "Done!" -ForegroundColor Cyan
    exit 0
}

# 2. Compile Inno Setup (if ISCC is available)
$releaseDir = "Releases"
if (!(Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir
}

# Download WebView2 Installer if not exists
$webv2Installer = "Installer\WebView2RuntimeInstaller.exe"
if (!(Test-Path $webv2Installer)) {
    Write-Host "Downloading WebView2 Runtime Installer..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://go.microsoft.com/fwlink/p/?LinkId=2124703" -OutFile $webv2Installer
}

$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) {
    Write-Host "Step 2: Compiling Inno Setup Installer..." -ForegroundColor Yellow
    & $iscc "Installer\installer.iss"
    Write-Host "Installer created in $releaseDir" -ForegroundColor Green
} else {
    Write-Host "Inno Setup (ISCC.exe) not found. Please compile Installer\installer.iss manually." -ForegroundColor Gray
}

Write-Host "Done!" -ForegroundColor Cyan