# FaraBotModerator Build & Package Script

Write-Host "Starting Build Process..." -ForegroundColor Cyan

$projectName = "FaraBotModerator"
$publishDir = "bin\Release\net8.0-windows10.0.22621.0\publish\win-x64"

# 1. Clean and Publish
Write-Host "Step 1: Publishing as a single-file executable..." -ForegroundColor Yellow
dotnet publish $projectName.csproj -c Release /p:PublishProfile=FolderProfile

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Publish completed. Files are in: $publishDir" -ForegroundColor Green

# 2. Package (Simple ZIP for now, can be extended for Inno Setup)
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

$version = (Get-Date -Format "yyyyMMdd_HHmm")
$zipName = "$releaseDir\$projectName`_$version.zip"

Write-Host "Step 2: Creating a ZIP package..." -ForegroundColor Yellow
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipName -Force

# 3. Compile Inno Setup (if ISCC is available)
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) {
    Write-Host "Step 3: Compiling Inno Setup Installer..." -ForegroundColor Yellow
    & $iscc "Installer\installer.iss"
    Write-Host "Installer created in $releaseDir" -ForegroundColor Green
} else {
    Write-Host "Inno Setup (ISCC.exe) not found. Please compile Installer\installer.iss manually." -ForegroundColor Gray
}

Write-Host "Done!" -ForegroundColor Cyan