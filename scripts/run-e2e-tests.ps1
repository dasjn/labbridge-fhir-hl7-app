# ====================
# Script to run LabBridge E2E tests
# Automatically cleans containers between runs
# ====================

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "LabBridge E2E Test Runner" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# Variables
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$ComposeDir = Join-Path $ProjectRoot "tests\LabBridge.IntegrationTests"
$ComposeFile = Join-Path $ComposeDir "docker-compose.test.yml"

Write-Host ""
Write-Host "[1/5] Cleaning previous containers..." -ForegroundColor Yellow
Push-Location $ComposeDir
try {
    docker-compose -f docker-compose.test.yml down -v 2>$null
} catch {
    # Ignore errors if no containers exist
}
Write-Host "Done: Containers cleaned" -ForegroundColor Green

Write-Host ""
Write-Host "[2/5] Starting services (RabbitMQ, PostgreSQL, LabFlow API)..." -ForegroundColor Yellow
docker-compose -f docker-compose.test.yml up -d

Write-Host ""
Write-Host "[3/5] Waiting for services to be ready (20 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 20

# Verify services are running
Write-Host "  Checking RabbitMQ..." -NoNewline
try {
    $response = Invoke-WebRequest -Uri "http://localhost:15672" -UseBasicParsing -TimeoutSec 5
    Write-Host " RabbitMQ is ready" -ForegroundColor Green
} catch {
    Write-Host " RabbitMQ is not available" -ForegroundColor Red
    Pop-Location
    exit 1
}

Write-Host "  Checking LabFlow API..." -NoNewline
try {
    $response = Invoke-WebRequest -Uri "http://localhost:8080/health" -UseBasicParsing -TimeoutSec 5
    Write-Host " LabFlow API is ready" -ForegroundColor Green
} catch {
    Write-Host " LabFlow API is not available" -ForegroundColor Red
    Pop-Location
    exit 1
}

Write-Host "  Checking PostgreSQL..." -NoNewline
$pgCheck = docker exec labbridge-test-postgres pg_isready -U labbridge 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host " PostgreSQL is ready" -ForegroundColor Green
} else {
    Write-Host " PostgreSQL is not available" -ForegroundColor Red
    Pop-Location
    exit 1
}

Write-Host ""
Write-Host "[4/5] Running E2E tests..." -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Cyan
Pop-Location
Push-Location $ProjectRoot
dotnet test tests\LabBridge.IntegrationTests\LabBridge.IntegrationTests.csproj --verbosity normal
$TestExitCode = $LASTEXITCODE

Write-Host ""
Write-Host "[5/5] Cleaning up containers..." -ForegroundColor Yellow
Pop-Location
Push-Location $ComposeDir
docker-compose -f docker-compose.test.yml down
Pop-Location

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
if ($TestExitCode -eq 0) {
    Write-Host "SUCCESS: E2E tests passed" -ForegroundColor Green
    exit 0
} else {
    Write-Host "FAILED: E2E tests failed" -ForegroundColor Red
    exit $TestExitCode
}
