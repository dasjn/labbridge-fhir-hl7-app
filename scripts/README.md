# LabBridge Test Scripts

Scripts automatizados para ejecutar tests de LabBridge con gestión automática de contenedores Docker.

## 📋 Scripts Disponibles

### 1. `run-e2e-tests.ps1` (PowerShell)
Ejecuta **tests End-to-End completos** con limpieza automática de contenedores.

**Qué hace:**
1. ✓ Limpia contenedores anteriores
2. ✓ Levanta servicios frescos (RabbitMQ, PostgreSQL, LabFlow API)
3. ✓ Verifica que los servicios estén listos
4. ✓ Ejecuta los tests de integración
5. ✓ Limpia contenedores al finalizar

**Uso:**
```powershell
# Desde la raíz del proyecto
.\scripts\run-e2e-tests.ps1

# O desde cualquier ubicación
cd D:\Personal\FHIR\LabBridge
powershell -ExecutionPolicy Bypass -File scripts\run-e2e-tests.ps1
```

**Tiempo aproximado:** ~2 minutos

---

### 2. `run-e2e-tests.sh` (Bash)
Versión bash del script E2E (para Linux/Mac/WSL/Git Bash).

**Uso:**
```bash
# Dar permisos de ejecución (primera vez)
chmod +x scripts/run-e2e-tests.sh

# Ejecutar
./scripts/run-e2e-tests.sh
```

---

### 3. `run-unit-tests.ps1` (PowerShell)
Ejecuta **solo tests unitarios** (sin Docker) - Mucho más rápido para desarrollo.

**Qué hace:**
- Ejecuta los 64 unit tests
- No requiere Docker ni servicios externos
- Perfecto para TDD y desarrollo rápido

**Uso:**
```powershell
.\scripts\run-unit-tests.ps1
```

**Tiempo aproximado:** ~10 segundos

---

## 🚀 Workflow Recomendado

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

### En CI/CD Pipeline
```yaml
# GitHub Actions / Azure Pipelines
- name: Run E2E Tests
  run: |
    pwsh scripts/run-e2e-tests.ps1
```

---

## 🔧 Prerequisitos

### Para E2E Tests:
- ✅ Docker Desktop instalado y corriendo
- ✅ Imagen `labflow-api:latest` disponible localmente
- ✅ PowerShell (Windows) o Bash (Linux/Mac)

### Para Unit Tests:
- ✅ .NET 8 SDK instalado
- ❌ NO requiere Docker

---

## 🐛 Troubleshooting

### Error: "Docker daemon not running"
```powershell
# Iniciar Docker Desktop y esperar a que esté listo
```

### Error: "labflow-api:latest not found"
```bash
# Construir la imagen de LabFlow
cd ../LabFlow
docker build -t labflow-api:latest .
```

### Error: "Port 5672/8080 already in use"
```powershell
# Detener contenedores existentes
cd tests/LabBridge.IntegrationTests
docker-compose -f docker-compose.test.yml down
```

### Tests fallando con datos duplicados
```powershell
# Los scripts ya limpian automáticamente, pero si persiste:
docker-compose -f tests/LabBridge.IntegrationTests/docker-compose.test.yml down -v
# El flag -v elimina volúmenes
```

---

## 📊 Salida Esperada

### E2E Tests (Exitoso)
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

### Unit Tests (Exitoso)
```
==========================================
LabBridge Unit Test Runner
==========================================

Executing 64 unit tests...
[Test output...]
Correctas! - Con error: 0, Superado: 64, Total: 64

==========================================
SUCCESS: Unit tests passed
```

---

## 💡 Tips

1. **Desarrollo rápido**: Usa `run-unit-tests.ps1` en loop durante desarrollo
2. **Validación pre-commit**: Ejecuta `run-e2e-tests.ps1` antes de hacer commit
3. **CI/CD**: Los scripts retornan exit code 0 (éxito) o 1 (fallo) para integración fácil
4. **Datos limpios**: Con `tmpfs` en docker-compose, los datos se limpian automáticamente al hacer `down`

---

## 🔄 Alternativa Manual

Si prefieres control manual:

```powershell
# Levantar servicios
cd tests\LabBridge.IntegrationTests
docker-compose -f docker-compose.test.yml up -d

# Ejecutar tests
cd ..\..
dotnet test tests\LabBridge.IntegrationTests

# Limpiar
cd tests\LabBridge.IntegrationTests
docker-compose -f docker-compose.test.yml down
```

---

**Creado:** 2025-10-28
**Última actualización:** 2025-10-28
