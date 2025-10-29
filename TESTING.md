# Testing Guide - LabBridge

Esta guía muestra cómo ejecutar los tests de LabBridge usando los scripts automatizados.

---

## 📋 Tipos de Tests

### 1. Unit Tests (Rápido - ~10 segundos)
- **64 unit tests** sin dependencias externas
- No requiere Docker, RabbitMQ ni servicios externos
- Ideal para desarrollo rápido (TDD)

### 2. E2E Integration Tests (Completo - ~2 minutos)
- **1 test E2E** que valida el flujo completo
- Requiere Docker Desktop corriendo
- Levanta: RabbitMQ, PostgreSQL, LabFlow API
- Valida: HL7v2 → FHIR transformation + audit logging

---

## 🚀 Quick Start

### Opción 1: Solo Unit Tests (Desarrollo Rápido)

```powershell
.\scripts\run-unit-tests.ps1
```

**Output esperado:**
```
==========================================
LabBridge Unit Test Runner
==========================================

Ejecutando 64 unit tests...
[Test execution...]
Correctas! - Con error: 0, Superado: 64, Total: 64

==========================================
SUCCESS: Unit tests passed
```

**Tiempo**: ~10 segundos

---

### Opción 2: E2E Tests (Validación Completa)

**Prerequisito**: Docker Desktop instalado y corriendo

```powershell
.\scripts\run-e2e-tests.ps1
```

**Output esperado:**
```
==========================================
LabBridge E2E Test Runner
==========================================

[1/5] Cleaning previous containers...
Done: Containers cleaned

[2/5] Starting services (RabbitMQ, PostgreSQL, LabFlow API)...
[Services starting...]

[3/5] Waiting for services to be ready (20 seconds)...
  Checking RabbitMQ... RabbitMQ is ready
  Checking LabFlow API... LabFlow API is ready
  Checking PostgreSQL... PostgreSQL is ready

[4/5] Running E2E tests...
==========================================
[Test output...]
Correctas! - Con error: 0, Superado: 1, Total: 1

[5/5] Cleaning up containers...

==========================================
SUCCESS: E2E tests passed
```

**Tiempo**: ~2 minutos (incluye startup de Docker)

---

## 🧪 Manual Testing - Generar Tráfico Realista

Para monitorear el sistema en tiempo real con Grafana dashboards:

### Paso 1: Levantar Todo el Stack

```bash
cd docker
docker-compose up -d
```

Esto levanta:
- LabBridge service (puerto 2575 MLLP)
- RabbitMQ (puertos 5672, 15672)
- PostgreSQL (puerto 5432)
- Prometheus (puerto 9090)
- Grafana (puerto 3000)

### Paso 2: Generar Tráfico Continuo

```powershell
.\send_continuous_traffic.ps1
```

**Parámetros opcionales:**
```powershell
.\send_continuous_traffic.ps1 `
    -Server "localhost" `
    -Port 2575 `
    -MinMessages 1 `
    -MaxMessages 5 `
    -IntervalSeconds 10
```

**¿Qué hace?**
- Genera mensajes HL7v2 ORU^R01 realistas
- Datos aleatorios (nombres, fechas de nacimiento, MRN, resultados)
- Alterna entre 3 paneles de laboratorio: CBC, Lipid, Metabolic
- LOINC codes correctos
- Valores dentro de rangos normales
- Statistics tracking

**Output ejemplo:**
```
==========================================
  HL7v2 Continuous Traffic Generator
==========================================

Configuration:
  Server: localhost:2575
  Messages per batch: 1-5 (random)
  Interval: 10 seconds
  Press Ctrl+C to stop

Starting continuous traffic...
Watch the dashboard at: http://localhost:3000/d/labbridge-main

[14:30:45] Batch #1 - Sending 3 message(s)...
  ✓ [1/3] García, Juan - CBC panel - OK
  ✓ [2/3] Martínez, Ana - Lipid panel - OK
  ✓ [3/3] López, Pedro - Metabolic panel - OK
  Batch completed: 3 OK, 0 failed (1.23s)

  Waiting 10 seconds until next batch...
```

### Paso 3: Monitorear en Grafana

1. Abrir: http://localhost:3000
2. Usuario: `admin` / Password: `admin`
3. Dashboard: "LabBridge - HL7 to FHIR Integration"

**Métricas en tiempo real:**
- Messages received rate (mensajes/segundo)
- Success vs Failure rate
- Processing latency (p50, p90, p99)
- Active MLLP connections
- RabbitMQ queue depth
- FHIR API call duration

### Paso 4: Verificar Audit Logs en PostgreSQL

```bash
docker exec -it labbridge-postgres psql -U labbridge -d labbridge_audit
```

```sql
-- Ver últimos 10 mensajes procesados
SELECT
    message_control_id,
    patient_id,
    message_type,
    status,
    processing_duration_ms,
    received_at
FROM "AuditLogs"
ORDER BY received_at DESC
LIMIT 10;

-- Contar mensajes por status
SELECT status, COUNT(*)
FROM "AuditLogs"
GROUP BY status;

-- Ver mensaje completo
SELECT
    raw_hl7_message,
    fhir_patient_json,
    fhir_observations_json
FROM "AuditLogs"
WHERE message_control_id = 'MSG123456';
```

### Paso 5: Detener Todo

```bash
# Detener generador de tráfico
Ctrl + C

# Detener stack completo
cd docker
docker-compose down

# Eliminar datos persistentes (opcional)
docker-compose down -v
```

---

## 🐛 Troubleshooting

### ❌ Error: "Docker daemon not running"

**Solución:**
1. Iniciar Docker Desktop
2. Esperar a que muestre "Docker Desktop is running"
3. Reintentar el script

---

### ❌ Error: "labflow-api:latest not found"

**Problema**: Falta construir la imagen de LabFlow API

**Solución:**
```bash
cd ../LabFlow
docker build -t labflow-api:latest .
```

---

### ❌ Error: "Port 5672/8080 already in use"

**Problema**: Servicios de tests anteriores no se limpiaron

**Solución:**
```bash
cd tests/LabBridge.IntegrationTests
docker-compose -f docker-compose.test.yml down
```

---

### ❌ Unit tests fallan con "Missing file"

**Problema**: Ejecutando desde directorio incorrecto

**Solución:**
```powershell
# Siempre ejecutar desde la raíz del proyecto
cd D:\Personal\FHIR\LabBridge
.\scripts\run-unit-tests.ps1
```

---

## 📊 Estructura de Tests

```
tests/
├── LabBridge.UnitTests/              # 64 tests (sin Docker)
│   ├── HL7/
│   │   ├── HL7ParsingTests.cs        # 15 tests
│   │   ├── AckGenerationTests.cs     # 8 tests
│   │   └── MllpServerTests.cs        # 6 tests
│   └── FHIR/
│       ├── FhirTransformationTests.cs # 24 tests
│       └── LabFlowClientTests.cs     # 10 tests
│
└── LabBridge.IntegrationTests/       # 1 E2E test (con Docker)
    ├── EndToEndTests.cs              # Flujo completo
    └── docker-compose.test.yml       # RabbitMQ, PostgreSQL, LabFlow
```

---

## 🔄 Workflow Recomendado

### Durante Desarrollo (TDD)
```powershell
# Loop rápido: solo unit tests
.\scripts\run-unit-tests.ps1
```

### Antes de Commit
```powershell
# Validación completa E2E
.\scripts\run-e2e-tests.ps1
```

### Para Demos / Testing Manual
```powershell
# Levantar stack + generar tráfico + ver Grafana
cd docker && docker-compose up -d
cd ..
.\send_continuous_traffic.ps1
# Abrir http://localhost:3000 en browser
```

---

## 📚 Más Información

- **scripts/README.md** - Documentación detallada de scripts
- **INFRASTRUCTURE_GUIDE.md** - Arquitectura completa del sistema
- **README.md** - Overview general del proyecto
