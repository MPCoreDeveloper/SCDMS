# ============================================================================
# SCDMS — Cross-platform dev launcher (Windows PowerShell)
# Builds (if needed) and starts SCDMS, then opens the browser.
# Usage:
#   .\launch.ps1                    # build + run + open browser
#   .\launch.ps1 -NoBuild           # run existing build
#   .\launch.ps1 -Port 5443         # custom port
# ============================================================================
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [int]$Port = 5443,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$Project = Join-Path $PSScriptRoot '..\src\SCDMS\SCDMS.csproj'

Write-Host '=== SCDMS — Sharp Core Database Management System ===' -ForegroundColor Cyan

if (-not $NoBuild) {
    Write-Host "[1/3] Building SCDMS ($Configuration)..."
    dotnet build $Project -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

Write-Host "[2/3] Starting SCDMS on https://localhost:$Port ..."
$env:SCDMS__HttpsPort = "$Port"

$job = Start-Job -ScriptBlock {
    param($proj, $config)
    dotnet run --project $proj --no-build -c $config
} -ArgumentList $Project, $Configuration

Write-Host '[3/3] Opening browser...'
$url = "https://localhost:$Port"
$deadline = (Get-Date).AddSeconds(30)
$ready = $false
while ((Get-Date) -lt $deadline) {
    try {
        $resp = Invoke-WebRequest -Uri $url -SkipCertificateCheck -TimeoutSec 2 -UseBasicParsing
        if ($resp.StatusCode -eq 200) { $ready = $true; break }
    } catch { Start-Sleep -Milliseconds 500 }
}

if ($ready) {
    Start-Process $url
    Write-Host "SCDMS running at $url" -ForegroundColor Green
} else {
    Write-Warning 'SCDMS did not respond in time. Check console output above.'
}

Receive-Job $job -Wait
