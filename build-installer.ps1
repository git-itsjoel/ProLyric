<#
.SYNOPSIS
    Builds and packages ProLyric into a Windows Installer (.exe) using Inno Setup.
#>
param(
    [switch]$SkipPublish = $false
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "         Building ProLyric Windows Installer        " -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

# 1. Publish WPF App
$publishDir = Join-Path $scriptDir "dist\publish"
if (-not $SkipPublish) {
    Write-Host "`n[1/3] Publishing ProLyric (Release, win-x64, Self-Contained)..." -ForegroundColor Yellow
    
    # Ensure dist folder is clean
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    $publishArgs = @(
        "publish", "ProLyric\ProLyric.csproj",
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=none",
        "-o", $publishDir
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
    Write-Host "Publish complete: $publishDir" -ForegroundColor Green
} else {
    Write-Host "`n[1/3] Skipping publish step as requested." -ForegroundColor Gray
}

# 2. Locate Inno Setup Compiler (ISCC.exe)
Write-Host "`n[2/3] Locating Inno Setup Compiler (ISCC.exe)..." -ForegroundColor Yellow

$isccPath = $null
$possiblePaths = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
)

foreach ($path in $possiblePaths) {
    if ($path -and (Test-Path $path)) {
        $isccPath = $path
        break
    }
}

if (-not $isccPath) {
    Write-Warning "Inno Setup (ISCC.exe) was not found on this machine."
    Write-Host "Published binaries are available in: $publishDir" -ForegroundColor Yellow
    Write-Host "To build the installer, install Inno Setup 6 from https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
    Write-Host "Or via winget: winget install --id JRSoftware.InnoSetup" -ForegroundColor Cyan
    exit 1
}

Write-Host "Found Inno Setup at: $isccPath" -ForegroundColor Green

# 3. Compile Installer Script
Write-Host "`n[3/3] Compiling installer script (installer.iss)..." -ForegroundColor Yellow
$issFile = Join-Path $scriptDir "installer.iss"
if (-not (Test-Path $issFile)) {
    Write-Error "Installer script '$issFile' not found!"
    exit 1
}

& $isccPath $issFile
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

$setupExe = Join-Path $scriptDir "dist\ProLyric-Setup.exe"
if (Test-Path $setupExe) {
    $sizeMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 2)
    Write-Host "`n====================================================" -ForegroundColor Green
    Write-Host " Installer created successfully!" -ForegroundColor Green
    Write-Host " File: $setupExe ($sizeMb MB)" -ForegroundColor White
    Write-Host "====================================================" -ForegroundColor Green
} else {
    Write-Host "`nCompilation completed." -ForegroundColor Green
}
