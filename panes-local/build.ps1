#!/usr/bin/env pwsh
$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot

Write-Host "==> dotnet test" -ForegroundColor Cyan
dotnet test --nologo --verbosity minimal

Write-Host "==> dotnet publish" -ForegroundColor Cyan
dotnet publish src/PanesLocal/PanesLocal.csproj `
    -c Release -r win-x64 --self-contained `
    --nologo --verbosity minimal

Write-Host "==> Inno Setup" -ForegroundColor Cyan
$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    throw "iscc.exe not found in PATH. Install Inno Setup 6 from https://jrsoftware.org/isdl.php and add 'C:\Program Files (x86)\Inno Setup 6' to PATH."
}
& iscc installer\PanesLocal.iss

$installer = Join-Path (Resolve-Path "dist") "PanesLocal-Setup.exe"
Write-Host "==> Built $installer" -ForegroundColor Green

Pop-Location
