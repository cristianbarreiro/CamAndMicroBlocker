# =====================================================================
# PrivLock — Automated Build & Packaging Pipeline
# =====================================================================

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Building & Packaging PrivLock Installer" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Run Unit Tests
Write-Host "`n[1/4] Running unit test suite..." -ForegroundColor Yellow
dotnet test "$ProjectRoot\CamMicBlocker.sln" --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Unit tests failed! Aborting installer build."
    exit 1
}

# 2. Clean previous build outputs
Write-Host "`n[2/4] Cleaning previous output directories..." -ForegroundColor Yellow
$PublishDir = "$ProjectRoot\publish_out"
$InstallerOutDir = "$ProjectRoot\installer_out"

# Terminate running process instances to release file locks before publishing
Get-Process -Name "PrivLock", "CamMicBlocker" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $InstallerOutDir) { Remove-Item $InstallerOutDir -Recurse -Force -ErrorAction SilentlyContinue }
if (-not (Test-Path $InstallerOutDir)) { New-Item -ItemType Directory -Path $InstallerOutDir | Out-Null }

# 3. Publish Single-File Self-Contained Binary
Write-Host "`n[3/4] Publishing single-file self-contained win-x64 release..." -ForegroundColor Yellow
dotnet publish "$ProjectRoot\src\CamMicBlocker\CamMicBlocker.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publishing failed! Aborting installer build."
    exit 1
}

# 4. Locate Inno Setup Compiler (ISCC.exe) and compile setup executable
Write-Host "`n[4/4] Compiling Windows Setup Installer with Inno Setup..." -ForegroundColor Yellow

$IsccCandidatePaths = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$IsccPath = $null
foreach ($path in $IsccCandidatePaths) {
    if (Test-Path $path) {
        $IsccPath = $path
        break
    }
}

if (-not $IsccPath) {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        $IsccPath = $cmd.Source
    }
}

if (-not $IsccPath) {
    Write-Error "ISCC.exe (Inno Setup Compiler) was not found! Please install Inno Setup 6."
    exit 1
}

Write-Host "Using ISCC compiler: $IsccPath" -ForegroundColor Gray
& $IsccPath "/O$InstallerOutDir" "/FPrivLock-Setup-1.0.0" "$ProjectRoot\installer\setup.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Installer compilation failed!"
    exit 1
}

# 5. Create Portable ZIP Package
Write-Host "`n[5/5] Creating Portable ZIP package..." -ForegroundColor Yellow
$ZipPath = "$InstallerOutDir\PrivLock-Portable-1.0.0.zip"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " SUCCESS! Release assets generated successfully at:" -ForegroundColor Green
Write-Host " Setup:    $InstallerOutDir\PrivLock-Setup-1.0.0.exe" -ForegroundColor White
Write-Host " Portable: $InstallerOutDir\PrivLock-Portable-1.0.0.zip" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
