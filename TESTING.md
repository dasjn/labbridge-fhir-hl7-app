# Manual Testing Guide - LabBridge

Esta guía te muestra cómo testear manualmente el flujo completo: **HL7v2 MLLP → RabbitMQ → FHIR Transformation**.

---

## Requisitos Previos

- ✅ Docker Desktop instalado y corriendo
- ✅ .NET 8 SDK instalado
- ✅ PowerShell (viene con Windows)

---

## Paso 1: Levantar RabbitMQ

Desde la raíz del proyecto:

```bash
docker-compose up -d
```

**¿Qué hace esto?**
- Descarga la imagen de RabbitMQ (primera vez, ~200MB)
- Levanta RabbitMQ en background (`-d` = detached)
- Expone puertos 5672 (AMQP) y 15672 (Management UI)

**Verificar que está corriendo**:
```bash
docker ps
```

Deberías ver:
```
CONTAINER ID   IMAGE                      STATUS         PORTS
abc123...      rabbitmq:3.13-management   Up 10 seconds  0.0.0.0:5672->5672/tcp, 0.0.0.0:15672->15672/tcp
```

**Acceder al Management UI**:
- URL: http://localhost:15672
- Usuario: `guest`
- Password: `guest`

---

## Paso 2: Ejecutar LabBridge Service

Abre una **nueva terminal** y ejecuta:

```bash
cd src/LabBridge.Service
dotnet run
```

**Logs esperados**:
```
info: LabBridge.Infrastructure.HL7.MllpServer[0]
      MLLP Server started on port 2575

info: LabBridge.Infrastructure.Messaging.RabbitMqQueue[0]
      RabbitMQ connection established: localhost:5672

info: LabBridge.Service.MessageProcessorWorker[0]
      MessageProcessorWorker starting...

info: LabBridge.Service.MessageProcessorWorker[0]
      Started consuming messages from RabbitMQ queue: labbridge.hl7.queue
```

Si ves estos logs, ¡todo está funcionando! 🎉

**Dejar esta terminal abierta** (está escuchando mensajes).

---

## Paso 3: Enviar Mensaje HL7v2

Abre **otra terminal** y ejecuta el script PowerShell:

```powershell
.\send_test_message.ps1
```

**¿Qué hace el script?**
1. Lee el mensaje HL7v2 de `test_oru_r01.hl7`
2. Conecta al puerto 2575 (MLLP server)
3. Envía el mensaje con MLLP framing (0x0B + mensaje + 0x1C + 0x0D)
4. Espera el ACK del servidor
5. Muestra el resultado

**Output esperado**:
```
==========================================
  HL7v2 MLLP Test Client
==========================================

Message file: test_oru_r01.hl7
HL7 Content (305 chars):
MSH|^~\&|PANTHER|LAB|LABFLOW|HOSPITAL|20251020120000||ORU^R01|MSG12345|P|2.5
PID|1||12345678^^^MRN||García^Juan^Carlos||19850315|M
OBR|1|ORD123|LAB456|58410-2^CBC panel^LN|||20251020115500||||||||||||||||F
OBX|1|NM|718-7^Hemoglobin^LN||14.5|g/dL|13.5-17.5|N|||F|||20251020120000
OBX|2|NM|6690-2^WBC^LN||7500|cells/uL|4500-11000|N|||F|||20251020120000
OBX|3|NM|777-3^Platelets^LN||250000|cells/uL|150000-400000|N|||F|||20251020120000

Connecting to localhost:2575...
Connected successfully!

Sending MLLP-framed message (308 bytes)...
Message sent!

Waiting for ACK response...
ACK received (123 bytes):
MSH|^~\&|LABBRIDGE|HOSPITAL|PANTHER|LAB|20251020120000||ACK|MSG12345|P|2.5
MSA|AA|MSG12345

SUCCESS: Message accepted (AA)

Connection closed
```

---

## Paso 4: Verificar el Flujo Completo

### A. En la terminal de LabBridge Service

Deberías ver logs como estos (en orden):

```
[10:30:00 INF] Client connected: 127.0.0.1:xxxxx
[10:30:00 INF] Received HL7 message from 127.0.0.1:xxxxx (305 bytes)
[10:30:00 INF] Parsed HL7 message: Type=ORU^R01, ControlId=MSG12345
[10:30:00 INF] Published HL7 message to RabbitMQ: MessageControlId=MSG12345, Size=305 bytes
[10:30:00 INF] Sent ACK to 127.0.0.1:xxxxx
[10:30:00 INF] Client disconnected: 127.0.0.1:xxxxx
[10:30:01 INF] Processing message from queue: MessageId=MSG12345
[10:30:01 INF] Processing HL7 message from queue (305 bytes)
[10:30:01 INF] Transformed HL7 to FHIR: Patient exists=True, Observations=3, Report exists=True
[10:30:01 INF] Message processed successfully: MessageId=MSG12345
```

**Esto confirma**:
1. ✅ MLLP server recibió el mensaje
2. ✅ Mensaje validado y parseado
3. ✅ Mensaje publicado a RabbitMQ
4. ✅ ACK enviado al cliente (< 1 segundo)
5. ✅ Mensaje consumido de la cola
6. ✅ Transformado a FHIR correctamente

---

### B. En RabbitMQ Management UI

1. Abre http://localhost:15672
2. Login: `guest` / `guest`
3. Click en **"Queues"** en el menú superior
4. Deberías ver:
   - `labbridge.hl7.queue` (main queue)
   - `labbridge.hl7.dlq` (dead letter queue)

**Observa**:
- **Total messages**: Contador sube y baja (mensaje entra y sale rápido)
- **Message rate**: Velocidad de procesamiento
- Click en la cola → "Get messages" para ver mensajes en la cola

---

## Paso 5: Load Testing - Enviar Múltiples Mensajes

### Opción A: Mensajes secuenciales (uno tras otro)

```powershell
.\send_multiple_messages.ps1 -Count 10
```

**Parámetros**:
- `-Count <número>`: Cuántos mensajes enviar (default: 5)
- `-Server <host>`: Servidor MLLP (default: localhost)
- `-Port <puerto>`: Puerto MLLP (default: 2575)
- `-Concurrent`: Enviar todos los mensajes en paralelo

**Output ejemplo**:
```
===========================================
  HL7v2 MLLP Load Test Client
===========================================

Configuration:
  Server: localhost:2575
  Messages: 10
  Mode: Sequential

Generating 10 random HL7v2 messages...
  [1] García, Juan Carlos - CBC panel (3 tests)
  [2] Martínez, Ana Lucía - Lipid panel (3 tests)
  [3] López, Pedro Miguel - Metabolic panel (3 tests)
  ...

Sending messages to localhost:2575...

[1/10] ✓ ACCEPTED - MessageId=MSG456789
[2/10] ✓ ACCEPTED - MessageId=MSG123456
[3/10] ✓ ACCEPTED - MessageId=MSG789123
...

===========================================
  Summary
===========================================
Total messages:   10
Successful:       10
Failed:           0
Duration:         2.34 seconds
Throughput:       4.27 msg/sec
```

### Opción B: Mensajes concurrentes (todos a la vez)

```powershell
.\send_multiple_messages.ps1 -Count 50 -Concurrent
```

**Esto envía 50 mensajes simultáneamente** para testear cómo maneja el servidor alta carga.

**¿Qué hace diferente el script?**
- ✅ Genera **datos aleatorios** (nombres, fechas de nacimiento, MRN, resultados)
- ✅ Alterna entre **3 tipos de paneles**: CBC, Lipid, Metabolic
- ✅ Valores de tests **dentro de rangos realistas**
- ✅ Message Control IDs **únicos** (no duplicados)
- ✅ Muestra **throughput** (mensajes/segundo)

**En RabbitMQ UI** verás:
- El gráfico de "Message rates" mostrando actividad intensa
- Mensajes procesándose en tiempo real
- Picos de throughput en modo concurrent

---

## Troubleshooting

### ❌ Error: "Connection refused" al enviar mensaje

**Problema**: LabBridge Service no está corriendo
**Solución**: Ejecuta `dotnet run` en `src/LabBridge.Service`

---

### ❌ Error: "RabbitMQ connection failed"

**Problema**: RabbitMQ no está corriendo
**Solución**:
```bash
docker-compose up -d
docker ps  # verificar que está corriendo
```

---

### ❌ Error: "Port 2575 already in use"

**Problema**: Ya hay un proceso usando el puerto
**Solución**:
```bash
# Windows
netstat -ano | findstr :2575
taskkill /PID <PID> /F

# Luego reinicia LabBridge Service
```

---

## Limpiar Todo

Cuando termines de testear:

### Detener LabBridge Service
- `Ctrl + C` en la terminal donde corre `dotnet run`

### Detener RabbitMQ
```bash
docker-compose down
```

### Eliminar datos de RabbitMQ (opcional)
```bash
docker-compose down -v  # -v elimina los volumes
```

---

## Siguiente Paso

Una vez que veas que todo funciona, el siguiente paso es implementar el **FHIR API Client (Refit)** para enviar los recursos FHIR transformados a LabFlow API.

Por ahora, el MessageProcessor solo transforma a FHIR y loguea, pero NO envía a ninguna API (eso es Phase 1B.3).

---

## Diagrama del Flujo

```
┌─────────────┐  HL7v2 MLLP   ┌──────────────┐  Publish   ┌──────────────┐
│  Analyzer   │ ────────────> │ MllpServer   │ ─────────> │  RabbitMQ    │
│ (PowerShell)│               │ (port 2575)  │            │   Queue      │
└─────────────┘               └──────────────┘            └──────────────┘
                                     │                            │
                                     │ ACK (< 1 sec)              │
                                     ↓                            │ Consume
                              ┌──────────────┐                    │
                              │   Client     │                    ↓
                              │ (PowerShell) │          ┌──────────────────┐
                              └──────────────┘          │ MessageProcessor │
                                                        │ Worker           │
                                                        └──────────────────┘
                                                               │
                                                               ↓
                                                        ┌──────────────────┐
                                                        │ FHIR Transform   │
                                                        │ (Patient, Obs,   │
                                                        │  DiagnosticRpt)  │
                                                        └──────────────────┘
```
