#!/bin/bash
# ====================
# Script para ejecutar E2E tests de LabBridge
# Limpia automáticamente los contenedores entre ejecuciones
# ====================

set -e  # Exit on error

echo "=========================================="
echo "LabBridge E2E Test Runner"
echo "=========================================="

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Variables
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_DIR="$PROJECT_ROOT/tests/LabBridge.IntegrationTests"
COMPOSE_FILE="$COMPOSE_DIR/docker-compose.test.yml"

echo ""
echo "${YELLOW}[1/5]${NC} Limpiando contenedores anteriores..."
cd "$COMPOSE_DIR"
docker-compose -f docker-compose.test.yml down -v 2>/dev/null || true
echo "${GREEN}✓${NC} Contenedores limpiados"

echo ""
echo "${YELLOW}[2/5]${NC} Levantando servicios (RabbitMQ, PostgreSQL, LabFlow API)..."
docker-compose -f docker-compose.test.yml up -d

echo ""
echo "${YELLOW}[3/5]${NC} Esperando a que los servicios estén listos (20 segundos)..."
sleep 20

# Verificar que los servicios estén corriendo
echo "  Verificando RabbitMQ..."
if curl -s http://localhost:15672 > /dev/null; then
    echo "${GREEN}  ✓ RabbitMQ está listo${NC}"
else
    echo "${RED}  ✗ RabbitMQ no está disponible${NC}"
    exit 1
fi

echo "  Verificando LabFlow API..."
if curl -s http://localhost:8080/health > /dev/null; then
    echo "${GREEN}  ✓ LabFlow API está listo${NC}"
else
    echo "${RED}  ✗ LabFlow API no está disponible${NC}"
    exit 1
fi

echo "  Verificando PostgreSQL..."
if docker exec labbridge-test-postgres pg_isready -U labbridge > /dev/null 2>&1; then
    echo "${GREEN}  ✓ PostgreSQL está listo${NC}"
else
    echo "${RED}  ✗ PostgreSQL no está disponible${NC}"
    exit 1
fi

echo ""
echo "${YELLOW}[4/5]${NC} Ejecutando tests E2E..."
echo "=========================================="
cd "$PROJECT_ROOT"
dotnet test tests/LabBridge.IntegrationTests/LabBridge.IntegrationTests.csproj --verbosity normal
TEST_EXIT_CODE=$?

echo ""
echo "${YELLOW}[5/5]${NC} Limpiando contenedores..."
cd "$COMPOSE_DIR"
docker-compose -f docker-compose.test.yml down

echo ""
echo "=========================================="
if [ $TEST_EXIT_CODE -eq 0 ]; then
    echo "${GREEN}✓ Tests E2E pasaron exitosamente${NC}"
    exit 0
else
    echo "${RED}✗ Tests E2E fallaron${NC}"
    exit $TEST_EXIT_CODE
fi
