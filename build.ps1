# FLAVIRUS - Script de build
$ErrorActionPreference = "Stop"
Write-Host "=== FLAVIRUS BUILD ===" -ForegroundColor Cyan

$localDotnetRoot = Join-Path $env:LOCALAPPDATA "dotnet"
if (Test-Path (Join-Path $localDotnetRoot "sdk\11.0.100")) {
    $env:PATH = "$localDotnetRoot;$env:PATH"
}

Write-Host "`n[1/3] Restore..." -ForegroundColor Yellow
dotnet restore Flavirus.slnx

Write-Host "`n[2/3] Build Release..." -ForegroundColor Yellow
dotnet build Flavirus.slnx -c Release --no-restore

Write-Host "`n[3/3] Testes..." -ForegroundColor Yellow
dotnet test Flavirus.Tests/Flavirus.Tests.csproj -c Release --no-build

Write-Host "`nBuild concluido com sucesso!"
