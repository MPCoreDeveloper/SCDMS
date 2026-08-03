# ============================================================================
# SCDMS installer for Windows (per-user, no admin required)
#
# One-liner:
#   irm https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.ps1 | iex
#
# Parameters:
#   -Version v1.0.0   install a specific version (default: latest release)
#   -Uninstall        remove SCDMS from this machine
#
# The installer is idempotent: re-running it performs an in-place update.
# Downloads are verified against the SHA256SUMS.txt published with each release.
# ============================================================================
[CmdletBinding()]
param(
    [string]$Version = '',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$Repo = 'MPCoreDeveloper/SCDMS'
$AppName = 'SCDMS'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\SCDMS'
$StartMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'

function Get-LatestVersion {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" `
        -Headers @{ 'User-Agent' = 'SCDMS-Installer' }
    return $release.tag_name.TrimStart('v')
}

if ($Uninstall) {
    Write-Host 'Uninstalling SCDMS...'
    Get-Process -Name 'scdms' -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $StartMenuDir 'SCDMS.lnk') -Force -ErrorAction SilentlyContinue

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($userPath -and $userPath.Contains($InstallDir)) {
        $newPath = ($userPath.Split(';') | Where-Object { $_ -ne $InstallDir }) -join ';'
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    }

    Write-Host "SCDMS removed. Your databases and settings remain in $env:LOCALAPPDATA\SCDMS."
    exit 0
}

if (-not $Version) {
    Write-Host 'Resolving latest SCDMS release...'
    $Version = Get-LatestVersion
}
$Version = $Version.TrimStart('v')
Write-Host "Installing SCDMS v$Version ..."

$assetName = "scdms_${Version}_win-x64.zip"
$baseUrl = "https://github.com/$Repo/releases/download/v$Version"
$tempDir = Join-Path $env:TEMP ("scdms-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tempDir | Out-Null

try {
    $zipPath = Join-Path $tempDir $assetName
    $sumsPath = Join-Path $tempDir 'SHA256SUMS.txt'

    Write-Host "[1/5] Downloading $assetName ..."
    Invoke-WebRequest -Uri "$baseUrl/$assetName" -OutFile $zipPath -UseBasicParsing
    Invoke-WebRequest -Uri "$baseUrl/SHA256SUMS.txt" -OutFile $sumsPath -UseBasicParsing

    Write-Host '[2/5] Verifying SHA256 checksum ...'
    $expected = ($null, (Get-Content $sumsPath | Where-Object { $_ -match [regex]::Escape($assetName) }) -split '\s+')[-2]
    if ([string]::IsNullOrWhiteSpace($expected)) { throw "No checksum found for $assetName in SHA256SUMS.txt" }
    $actual = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected.ToLowerInvariant()) { throw "Checksum mismatch! Expected $expected, got $actual" }
    Write-Host '      Checksum OK.'

    Write-Host '[3/5] Stopping running SCDMS instances ...'
    Get-Process -Name 'scdms' -ErrorAction SilentlyContinue | Stop-Process -Force

    Write-Host "[4/5] Installing to $InstallDir ..."
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force

    Write-Host '[5/5] Creating shortcuts and PATH entry ...'
    # Launcher: starts the server and opens the browser.
    $launcherPath = Join-Path $InstallDir 'SCDMS.cmd'
    @"
@echo off
start "" "%~dp0scdms.exe"
timeout /t 3 /nobreak >nul
start https://localhost:5443
"@ | Set-Content -Path $launcherPath -Encoding ASCII

    $wsh = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut((Join-Path $StartMenuDir 'SCDMS.lnk'))
    $shortcut.TargetPath = $launcherPath
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = 'SCDMS - Sharp Core Database Management System'
    $shortcut.Save()

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not $userPath) { $userPath = '' }
    if (-not ($userPath.Split(';') | Where-Object { $_ -eq $InstallDir })) {
        [Environment]::SetEnvironmentVariable('Path', "$userPath;$InstallDir".Trim(';'), 'User')
    }
}
finally {
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '===================================================='
Write-Host "  SCDMS v$Version installed successfully!"
Write-Host ''
Write-Host '  Start:  use the Start Menu shortcut "SCDMS"'
Write-Host '  CLI:    open a NEW terminal, then:'
Write-Host '            scdms              (start the server)'
Write-Host '            scdms --update     (check for updates)'
Write-Host ''
Write-Host '  Open:   https://localhost:5443'
Write-Host '  Note:   first launch uses a self-signed localhost'
Write-Host '          certificate; accept the browser warning once.'
Write-Host '  Data:   %LOCALAPPDATA%\SCDMS'
Write-Host '===================================================='
