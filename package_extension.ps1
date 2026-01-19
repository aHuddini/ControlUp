# ControlUp Extension Packaging Script
# Creates a .pext package for Playnite installation
#
# Usage: .\package_extension.ps1 [-Configuration Release|Debug]
#
# Note: This script packages an already-built project. Build first with:
#   dotnet build -c Release

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  ControlUp Extension Packaging" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

# Read version from version.txt (single source of truth)
$versionFile = Join-Path $scriptDir "version.txt"
if (-not (Test-Path $versionFile)) {
    Write-Host "ERROR: version.txt not found. Please create it with the version number (e.g., 1.0.0)" -ForegroundColor Red
    exit 1
}
$versionFull = (Get-Content $versionFile -Raw).Trim()
# Convert version format: 1.0.0 -> 1_0_0 for filename
$version = $versionFull -replace '\.', '_'

# Build paths
$outputDir = "bin\$Configuration\net4.6.2"
$packageDir = "package"
$extensionName = "ControlUp"
$extensionId = "8d646e1b-c919-49d7-be40-5ef9960064bc"

# Verify DLL exists and show details
$dllPath = Join-Path $outputDir "ControlUp.dll"
if (-not (Test-Path $dllPath)) {
    Write-Host "ERROR: ControlUp.dll not found in $outputDir" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please build the project first:" -ForegroundColor Yellow
    Write-Host "  dotnet build -c $Configuration" -ForegroundColor White
    Write-Host ""
    exit 1
}

# Show DLL info to verify it's fresh
$dllInfo = Get-Item $dllPath
Write-Host "Found DLL: $($dllInfo.Name)" -ForegroundColor Green
Write-Host "  Size: $([math]::Round($dllInfo.Length/1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Modified: $($dllInfo.LastWriteTime)" -ForegroundColor Gray
Write-Host ""

# Clean previous package
Write-Host "Preparing package directory..." -ForegroundColor Yellow
if (Test-Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  Cleaned existing package directory" -ForegroundColor Gray
}
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

Write-Host "Copying extension files..." -ForegroundColor Yellow

# Update extension.yaml with current version before copying
$extensionYamlPath = Join-Path $scriptDir "extension.yaml"
if (Test-Path $extensionYamlPath) {
    $yamlContent = Get-Content $extensionYamlPath -Raw
    # Update version line if it exists
    if ($yamlContent -match "Version:\s*[\d\.]+") {
        $yamlContent = $yamlContent -replace "Version:\s*[\d\.]+", "Version: $versionFull"
        Set-Content -Path $extensionYamlPath -Value $yamlContent -NoNewline
        Write-Host "  Updated extension.yaml with version $versionFull" -ForegroundColor Gray
    }
}

# Copy core files
$coreFiles = @(
    "extension.yaml",
    "icon.png",
    "LICENSE"
)

foreach ($file in $coreFiles) {
    if (Test-Path $file) {
        Copy-Item $file -Destination $packageDir -Force
        Write-Host "  Copied file: $file" -ForegroundColor Gray
    } else {
        Write-Host "  WARNING: $file not found (optional)" -ForegroundColor Yellow
    }
}

# Copy main DLL
Copy-Item $dllPath -Destination $packageDir -Force
Write-Host "  Copied: ControlUp.dll" -ForegroundColor Gray

# Note: Playnite.SDK.dll is provided by Playnite at runtime, don't include it in the package

# Copy dependencies - Use lib\dll as primary source, fallback to build output
Write-Host "Copying dependencies..." -ForegroundColor Yellow
$dllLibDir = Join-Path $scriptDir "lib\dll"

# Required DLLs for the extension (explicit list to ensure nothing is missed)
$requiredDlls = @(
    "MaterialDesignColors.dll",
    "MaterialDesignThemes.Wpf.dll"
)

# System DLLs that Playnite provides (don't include)
$excludedDlls = @(
    "ControlUp.dll",
    "Playnite.SDK.dll",
    "System.Net.Http.dll",  # Playnite may provide this
    "System.Security.Cryptography.*"  # System DLLs
)

foreach ($dllName in $requiredDlls) {
    $copied = $false

    # Try lib\dll first (one-stop-shop for all DLLs)
    $libDllPath = Join-Path $dllLibDir $dllName
    if (Test-Path $libDllPath) {
        Copy-Item $libDllPath -Destination $packageDir -Force
        Write-Host "  Copied: $dllName from lib\dll" -ForegroundColor Gray
        $copied = $true
    } else {
        # Fallback to build output
        $outputDllPath = Join-Path $outputDir $dllName
        if (Test-Path $outputDllPath) {
            Copy-Item $outputDllPath -Destination $packageDir -Force
            Write-Host "  Copied: $dllName from build output" -ForegroundColor Gray
            $copied = $true
        }
    }

    if (-not $copied) {
        Write-Host "  WARNING: $dllName not found in lib\dll or build output" -ForegroundColor Yellow
    }
}

# Copy any other DLLs from build output that aren't in our required list or excluded
Write-Host "Copying additional dependencies from build output..." -ForegroundColor Yellow
$additionalDlls = Get-ChildItem -Path $outputDir -Filter "*.dll" | Where-Object {
    $_.Name -ne "ControlUp.dll" -and
    $_.Name -ne "Playnite.SDK.dll" -and
    $_.Name -notin $requiredDlls -and
    $_.Name -notlike "System.*" -and
    $_.Name -notlike "WindowsBase.dll" -and
    $_.Name -notlike "PresentationCore.dll" -and
    $_.Name -notlike "PresentationFramework.dll"
}

if ($additionalDlls) {
    foreach ($dll in $additionalDlls) {
        $destPath = Join-Path $packageDir $dll.Name
        if (-not (Test-Path $destPath)) {
            Copy-Item $dll.FullName -Destination $destPath -Force
            Write-Host "  Copied: $($dll.Name)" -ForegroundColor Gray
        }
    }
}

# Create .pext file (ZIP with different extension)
Write-Host "Creating .pext package..." -ForegroundColor Yellow

# Create pext output folder if it doesn't exist
$pextOutputDir = Join-Path $scriptDir "pext"
if (-not (Test-Path $pextOutputDir)) {
    New-Item -ItemType Directory -Path $pextOutputDir -Force | Out-Null
    Write-Host "  Created pext output folder" -ForegroundColor Gray
}

$pextFileName = "$extensionName.$extensionId`_$version.pext"
$pextFilePath = Join-Path $pextOutputDir $pextFileName
$zipFilePath = Join-Path $pextOutputDir "$extensionName.$extensionId`_$version.zip"

# Remove old package if exists
if (Test-Path $pextFilePath) {
    Remove-Item $pextFilePath -Force -ErrorAction SilentlyContinue
}
if (Test-Path $zipFilePath) {
    Remove-Item $zipFilePath -Force -ErrorAction SilentlyContinue
}

# Verify package contents before creating archive
Write-Host "Verifying package contents..." -ForegroundColor Yellow
$packageFiles = Get-ChildItem -Path $packageDir -File
$requiredFiles = @("ControlUp.dll", "extension.yaml", "icon.png")
$missingFiles = @()

foreach ($required in $requiredFiles) {
    if (-not ($packageFiles | Where-Object { $_.Name -eq $required })) {
        $missingFiles += $required
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "ERROR: Missing required files in package:" -ForegroundColor Red
    foreach ($file in $missingFiles) {
        Write-Host "  - $file" -ForegroundColor Red
    }
    exit 1
}

Write-Host "  Package contains $($packageFiles.Count) files" -ForegroundColor Gray
Write-Host ""

# Create ZIP first (Compress-Archive limitation)
Write-Host "Creating .pext archive..." -ForegroundColor Yellow
try {
    Compress-Archive -Path "$packageDir\*" -DestinationPath $zipFilePath -Force

    # Rename to .pext
    Rename-Item -Path $zipFilePath -NewName $pextFileName -Force

    $packageInfo = Get-Item $pextFilePath

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  PACKAGE CREATED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Package Details:" -ForegroundColor Cyan
    Write-Host "  File: $($packageInfo.Name)" -ForegroundColor White
    Write-Host "  Size: $([math]::Round($packageInfo.Length/1KB, 2)) KB" -ForegroundColor White
    Write-Host "  Location: $($packageInfo.FullName)" -ForegroundColor White
    Write-Host "  Version: $versionFull" -ForegroundColor White
    Write-Host ""
    Write-Host "Package Contents:" -ForegroundColor Cyan
    foreach ($file in $packageFiles | Sort-Object Name) {
        Write-Host "  - $($file.Name) ($([math]::Round($file.Length/1KB, 2)) KB)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "To install in Playnite:" -ForegroundColor Cyan
    Write-Host "  1. Open Playnite" -ForegroundColor White
    Write-Host "  2. Go to Add-ons -> Extensions" -ForegroundColor White
    Write-Host "  3. Click 'Add extension' and select the .pext file" -ForegroundColor White
    Write-Host ""
} catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  ERROR: Failed to create package" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Yellow
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host $_.Exception.InnerException.Message -ForegroundColor Red
    }
    Write-Host ""
    exit 1
}