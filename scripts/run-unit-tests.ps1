# ====================
# Script para ejecutar SOLO unit tests (sin Docker)
# Mucho más rápido para desarrollo
# ====================

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "LabBridge Unit Test Runner" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

Write-Host ""
Write-Host "Ejecutando 64 unit tests..." -ForegroundColor Yellow
Push-Location $ProjectRoot
dotnet test tests\LabBridge.UnitTests\LabBridge.UnitTests.csproj --verbosity normal
$TestExitCode = $LASTEXITCODE
Pop-Location

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
if ($TestExitCode -eq 0) {
    Write-Host "✓ Unit tests pasaron exitosamente" -ForegroundColor Green
    exit 0
} else {
    Write-Host "✗ Unit tests fallaron" -ForegroundColor Red
    exit $TestExitCode
}
