# ============================================================================
# SCDMS — Smoke test: build, start, verify HTTPS response on localhost:5443.
# ============================================================================
$ErrorActionPreference = 'Stop'

$Project = Join-Path $PSScriptRoot '..\src\SCDMS\SCDMS.csproj'
$Url = 'https://localhost:5443/'

Write-Host 'Building SCDMS...'
dotnet build $Project -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Write-Host 'Starting SCDMS for smoke test...'
$p = Start-Process dotnet -ArgumentList "run --project `"$Project`" --no-build -c Release" -PassThru -WindowStyle Hidden

try {
    $deadline = (Get-Date).AddSeconds(40)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -SkipCertificateCheck -TimeoutSec 2 -UseBasicParsing
            if ($resp.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }

    if (-not $ready) {
        Write-Error 'Port 5443 did not open - SCDMS failed to start (smoke test FAIL)'
        exit 1
    }

    if ($resp.Content -match 'SCDMS') {
        Write-Host 'HTTPS 200 + SCDMS layout found - smoke test PASS' -ForegroundColor Green
        exit 0
    }

    Write-Error 'Page responded but SCDMS layout marker missing (smoke test FAIL)'
    exit 1
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
