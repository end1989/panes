#!/usr/bin/env pwsh
$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot
try {
    # Native commands don't throw on failure, so every step checks its exit code;
    # otherwise a failed step still ends in "Built" with a stale installer in dist.
    Write-Host "==> dotnet test" -ForegroundColor Cyan
    dotnet test --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed (exit code $LASTEXITCODE)" }

    # Short output folder on purpose: Inno Setup can't read paths over 260
    # characters, and libVLC's deepest files sit ~90 characters below it.
    # Emptied first: publish doesn't delete files the app no longer ships.
    Write-Host "==> dotnet publish" -ForegroundColor Cyan
    if (Test-Path publish) { Remove-Item publish -Recurse -Force }
    dotnet publish src/PanesLocal/PanesLocal.csproj `
        -c Release -r win-x64 --self-contained -o publish `
        --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)" }

    Write-Host "==> Inno Setup" -ForegroundColor Cyan
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if (-not $iscc) {
        throw "iscc.exe not found in PATH. Install Inno Setup 6 from https://jrsoftware.org/isdl.php and add 'C:\Program Files (x86)\Inno Setup 6' to PATH."
    }
    & iscc installer\PanesLocal.iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit code $LASTEXITCODE)" }

    $installer = Join-Path (Resolve-Path "dist") "PanesLocal-Setup.exe"
    Write-Host "==> Built $installer" -ForegroundColor Green
}
finally {
    Pop-Location
}
