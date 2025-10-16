# LabBridge - Technical Context & Specifications

**IMPORTANTE: Este archivo contiene el contexto técnico completo para iniciar el proyecto LabBridge en un nuevo repositorio.**

---

## 📋 Overview

**LabBridge** es un servicio de integración que actúa como **traductor bidireccional** entre sistemas legacy de laboratorio (HL7v2) y sistemas modernos basados en FHIR R4.

### Relación con LabFlow

**LabFlow FHIR API** (este repositorio):
- FHIR R4 REST API server
- Gestiona Patient, Observation, DiagnosticReport, ServiceRequest
- JWT authentication + role-based authorization
- SQLite (dev) / PostgreSQL (prod)

**LabBridge** (nuevo repositorio):
- HL7v2 MLLP TCP listener
- HL7v2 ↔ FHIR transformation engine
- RabbitMQ message queue
- Cliente de LabFlow FHIR API

**Ecosistema completo**:
```
┌──────────────┐    HL7v2    ┌──────────┐    FHIR R4    ┌──────────┐
│  Analyzer    │ ──────────> │ LabBridge│ ───────────> │ LabFlow  │
│ (Panther,    │ MLLP/TCP    │          │ REST API     │ FHIR API │
│  Abbott)     │ <───────────│          │ <────────────│          │
└──────────────┘   ACK       └──────────┘   Resources  └──────────┘
```

---

## 🏗️ Arquitectura Técnica

### Technology Stack

**Core**:
- **.NET 8** - C# 12, async/await, minimal APIs
- **NHapi v3.2.0** - HL7v2 parsing (supports v2.3, v2.4, v2.5, v2.6)
- **Hl7.Fhir.R4 (Firely SDK) v5.12.2** - FHIR resource generation/validation
- **Refit v7.2.22** - Type-safe HTTP client for FHIR API calls
- **Entity Framework Core 9** - Message audit logging

**Messaging**:
- **RabbitMQ.Client v6.8.1** - Message queue for reliability
- **MassTransit v8.3.5** (optional) - Higher-level messaging abstraction

**Supporting**:
- **Serilog v9.0.0** - Structured logging
- **Polly v8.5.0** - Retry policies and circuit breakers
- **Quartz.NET v3.14.0** - Background jobs (retry, cleanup)
- **xUnit + FluentAssertions** - Testing

### Project Structure

```
LabBridge/
├── LabBridge.sln
├── src/
│   ├── LabBridge.Api/                    # ASP.NET Core host (if REST monitoring needed)
│   ├── LabBridge.Core/                   # Domain models, interfaces
│   │   ├── Models/
│   │   │   ├── HL7v2/                    # HL7v2 message models
│   │   │   ├── FHIR/                     # FHIR resource wrappers
│   │   │   └── Audit/                    # Audit log models
│   │   ├── Interfaces/
│   │   │   ├── IHL7Parser.cs
│   │   │   ├── IHL7ToFhirTransformer.cs
│   │   │   ├── IFhirClient.cs
│   │   │   ├── IMessageQueue.cs
│   │   │   └── IAuditLogger.cs
│   │   └── Exceptions/
│   ├── LabBridge.Infrastructure/         # External dependencies
│   │   ├── HL7/
│   │   │   ├── NHapiParser.cs           # NHapi implementation
│   │   │   ├── MllpServer.cs            # TCP listener
│   │   │   └── AckGenerator.cs          # HL7v2 ACK messages
│   │   ├── FHIR/
│   │   │   ├── FhirTransformer.cs       # HL7 → FHIR logic
│   │   │   └── LabFlowClient.cs         # Refit HTTP client
│   │   ├── Messaging/
│   │   │   ├── RabbitMqQueue.cs         # RabbitMQ implementation
│   │   │   └── MessageProcessor.cs      # Queue consumer
│   │   └── Data/
│   │       ├── AuditDbContext.cs        # EF Core context
│   │       └── Entities/                # Audit entities
│   ├── LabBridge.Service/               # Background service (Worker)
│   │   ├── Program.cs
│   │   ├── MllpListenerWorker.cs       # TCP listener worker
│   │   ├── MessageProcessorWorker.cs   # Queue consumer worker
│   │   └── appsettings.json
│   └── LabBridge.Jobs/                  # Quartz.NET scheduled jobs
│       ├── RetryFailedMessagesJob.cs
│       └── CleanupOldAuditLogsJob.cs
├── tests/
│   ├── LabBridge.UnitTests/
│   │   ├── HL7ParsingTests.cs
│   │   ├── TransformationTests.cs
│   │   └── AckGenerationTests.cs
│   └── LabBridge.IntegrationTests/
│       ├── EndToEndTests.cs            # TestContainers
│       └── MllpServerTests.cs
├── docs/
│   ├── HL7_MESSAGE_SAMPLES/            # Example messages
│   │   ├── ORU_R01_CBC.hl7
│   │   ├── ORM_O01_Order.hl7
│   │   └── ADT_A01_Admit.hl7
│   └── MAPPING_SPECS.md               # HL7 → FHIR field mapping
├── docker/
│   ├── Dockerfile
│   ├── docker-compose.yml             # LabBridge + RabbitMQ + PostgreSQL
│   └── .env.example
└── README.md
```

---

## 🔄 Message Flow Architecture

### 1. Inbound: HL7v2 → FHIR (ORU^R01)

```
┌─────────────────────────────────────────────────────────────────────┐
│ LabBridge Service                                                   │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────────────┐                                              │
│  │ MllpListenerWorker│  (Async TCP server on port 2575)            │
│  │                  │                                              │
│  │ Receives HL7v2   │───┬───> NHapiParser.Parse()                 │
│  │ message over TCP │   │                                          │
│  └──────────────────┘   │                                          │
│           │              │                                          │
│           │              ▼                                          │
│           │     ┌────────────────────┐                             │
│           │     │ Validate message   │                             │
│           │     │ - MSH segment      │                             │
│           │     │ - Required fields  │                             │
│           │     └────────────────────┘                             │
│           │              │                                          │
│           │              ▼                                          │
│           │     ┌────────────────────┐                             │
│           │     │ Generate ACK       │                             │
│           │     │ (AA = success)     │                             │
│           │     └────────────────────┘                             │
│           │              │                                          │
│           ▼◄─────────────┘                                          │
│  ┌──────────────────┐                                              │
│  │ Send ACK to      │ (Immediate response < 1 sec)                 │
│  │ analyzer         │                                              │
│  └──────────────────┘                                              │
│                                                                     │
│  ┌──────────────────┐                                              │
│  │ RabbitMQ Publish │                                              │
│  │ (persistence ON) │───> Queue: hl7-to-fhir                       │
│  └──────────────────┘                                              │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────────┐ │
│  │ MessageProcessorWorker (Queue Consumer)                      │ │
│  │                                                              │ │
│  │  1. Dequeue message                                          │ │
│  │  2. FhirTransformer.Transform(hl7Message)                    │ │
│  │     - Extract PID → Patient resource                         │ │
│  │     - Extract OBX → Observation resources                    │ │
│  │     - Extract OBR → DiagnosticReport resource                │ │
│  │  3. LabFlowClient.PostPatient() (with retry via Polly)       │ │
│  │  4. LabFlowClient.PostObservations()                         │ │
│  │  5. LabFlowClient.PostDiagnosticReport()                     │ │
│  │  6. AuditLogger.LogSuccess()                                 │ │
│  │  7. Acknowledge message (remove from queue)                  │ │
│  │                                                              │ │
│  │  Error handling:                                             │ │
│  │  - Transient error (network, 5xx) → Retry 3x with backoff   │ │
│  │  - Permanent error (400, validation) → Dead letter queue    │ │
│  │  - Max retries exceeded → Dead letter queue                 │ │
│  └──────────────────────────────────────────────────────────────┘ │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 2. Outbound: FHIR → HL7v2 (ORM^O01) [Phase 3]

```
┌─────────────────┐      FHIR ServiceRequest      ┌──────────────┐
│ LabFlow API     │──────────────────────────────>│ LabBridge    │
│ (EHR creates    │   POST /ServiceRequest        │ API endpoint │
│  lab order)     │                               │              │
└─────────────────┘                               └──────────────┘
                                                         │
                                                         ▼
                                               ┌──────────────────┐
                                               │ Transform FHIR   │
                                               │ → HL7v2 ORM^O01  │
                                               └──────────────────┘
                                                         │
                                                         ▼
                                               ┌──────────────────┐
                                               │ Route to analyzer│
                                               │ by test code     │
                                               │ (LOINC → IP:PORT)│
                                               └──────────────────┘
                                                         │
                                                         ▼
                                               ┌──────────────────┐
                                               │ MLLP Client      │
                                               │ Send ORM^O01     │
                                               │ Wait for ACK     │
                                               └──────────────────┘
```

---

## 🧩 HL7v2 → FHIR Mapping Specifications

### ORU^R01 (Observation Result) → FHIR Resources

**HL7v2 Message Structure**:
```
MSH  - Message Header
PID  - Patient Identification
[PV1] - Patient Visit (optional)
OBR  - Observation Request (panel/test)
OBX  - Observation Result (individual value)
OBX  - Observation Result
OBX  - ...
[NTE] - Notes (optional)
```

**Mapping Table**:

| HL7v2 Segment/Field | FHIR Resource | FHIR Field | Notes |
|---------------------|---------------|------------|-------|
| **MSH-10** (Message Control ID) | - | Audit log correlation ID | Unique message identifier |
| **PID-3** (Patient ID) | Patient | identifier[0].value | MRN (Medical Record Number) |
| **PID-5** (Patient Name) | Patient | name[0].family, name[0].given | Format: LastName^FirstName^MiddleName |
| **PID-7** (Date of Birth) | Patient | birthDate | Format: YYYYMMDD |
| **PID-8** (Gender) | Patient | gender | M→male, F→female, O→other, U→unknown |
| **OBR-2** (Placer Order Number) | DiagnosticReport | identifier[0].value | Order ID from EHR |
| **OBR-3** (Filler Order Number) | DiagnosticReport | identifier[1].value | Order ID from analyzer |
| **OBR-4** (Universal Service ID) | DiagnosticReport | code.coding[0].code | LOINC panel code (e.g., 58410-2) |
| **OBR-7** (Observation Date/Time) | DiagnosticReport | effectiveDateTime | When test was performed |
| **OBR-22** (Results Rpt/Status) | DiagnosticReport | status | F→final, P→preliminary, C→corrected |
| **OBX-2** (Value Type) | Observation | value[x] type | NM→valueQuantity, CE→valueCodeableConcept |
| **OBX-3** (Observation Identifier) | Observation | code.coding[0].code | LOINC test code (e.g., 718-7 = Hemoglobin) |
| **OBX-5** (Observation Value) | Observation | valueQuantity.value OR valueCodeableConcept | Numeric or coded value |
| **OBX-6** (Units) | Observation | valueQuantity.unit | UCUM units (e.g., g/dL, mg/dL) |
| **OBX-7** (Reference Range) | Observation | referenceRange[0].text | Normal range (e.g., "13.5-17.5") |
| **OBX-8** (Abnormal Flags) | Observation | interpretation[0].coding[0].code | N→normal, H→high, L→low |
| **OBX-11** (Observation Result Status) | Observation | status | F→final, P→preliminary |
| **OBX-14** (Date/Time of Observation) | Observation | effectiveDateTime | When observation was made |

**Example: CBC Panel (ORU^R01) → FHIR**

HL7v2 Input:
```
MSH|^~\&|PANTHER|LAB|LABFLOW|HOSPITAL|20251016120000||ORU^R01|MSG123|P|2.5
PID|1||12345678^^^MRN||García^Juan^Carlos||19850315|M
OBR|1|ORD123|LAB456|58410-2^CBC panel^LN|||20251016115500||||||||||||||||F
OBX|1|NM|718-7^Hemoglobin^LN||14.5|g/dL|13.5-17.5|N|||F|||20251016120000
OBX|2|NM|6690-2^WBC^LN||7500|cells/uL|4500-11000|N|||F|||20251016120000
OBX|3|NM|777-3^Platelets^LN||250000|cells/uL|150000-400000|N|||F|||20251016120000
```

FHIR Output (simplified):
```json
// 1. Patient resource
{
  "resourceType": "Patient",
  "identifier": [{ "system": "urn:oid:MRN", "value": "12345678" }],
  "name": [{ "family": "García", "given": ["Juan", "Carlos"] }],
  "gender": "male",
  "birthDate": "1985-03-15"
}

// 2. Observation resources (3x, one per OBX)
{
  "resourceType": "Observation",
  "status": "final",
  "code": { "coding": [{ "system": "http://loinc.org", "code": "718-7", "display": "Hemoglobin" }] },
  "subject": { "reference": "Patient/12345678" },
  "effectiveDateTime": "2025-10-16T12:00:00Z",
  "valueQuantity": { "value": 14.5, "unit": "g/dL", "system": "http://unitsofmeasure.org", "code": "g/dL" },
  "referenceRange": [{ "text": "13.5-17.5" }],
  "interpretation": [{ "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationInterpretation", "code": "N" }] }]
}
// ... (WBC, Platelets similar)

// 3. DiagnosticReport resource (groups all 3 observations)
{
  "resourceType": "DiagnosticReport",
  "identifier": [
    { "system": "urn:oid:PlacerOrderNumber", "value": "ORD123" },
    { "system": "urn:oid:FillerOrderNumber", "value": "LAB456" }
  ],
  "status": "final",
  "code": { "coding": [{ "system": "http://loinc.org", "code": "58410-2", "display": "CBC panel" }] },
  "subject": { "reference": "Patient/12345678" },
  "effectiveDateTime": "2025-10-16T11:55:00Z",
  "issued": "2025-10-16T12:00:00Z",
  "result": [
    { "reference": "Observation/obs1" },
    { "reference": "Observation/obs2" },
    { "reference": "Observation/obs3" }
  ]
}
```

---

## 🔐 Security & Configuration

### appsettings.json (Development)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/labbridge-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  },

  "MllpListener": {
    "Port": 2575,
    "MaxConcurrentConnections": 50,
    "ReceiveTimeoutSeconds": 300,
    "EnableTls": false,
    "TlsCertificatePath": ""
  },

  "FhirApiSettings": {
    "BaseUrl": "https://localhost:7000",
    "AuthenticationEndpoint": "/Auth/login",
    "Username": "labbridge@system.com",
    "Password": "SecurePassword123!",
    "TimeoutSeconds": 30,
    "RetryCount": 3,
    "RetryDelaySeconds": 2
  },

  "RabbitMqSettings": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/",
    "QueueName": "hl7-to-fhir",
    "DeadLetterQueueName": "hl7-to-fhir-dlq",
    "PrefetchCount": 10,
    "DurableQueues": true
  },

  "DatabaseSettings": {
    "Provider": "PostgreSQL",
    "ConnectionString": "Host=localhost;Database=labbridge_audit;Username=postgres;Password=dev_password"
  },

  "MessageProcessing": {
    "MaxRetryAttempts": 3,
    "RetryDelaySeconds": 5,
    "EnableDeadLetterQueue": true,
    "MessageRetentionDays": 90
  }
}
```

### Production Configuration (Environment Variables)

```bash
# FHIR API
export FhirApiSettings__BaseUrl="https://labflow-api.azurewebsites.net"
export FhirApiSettings__Username="labbridge@system.com"
export FhirApiSettings__Password="[SecurePasswordFromKeyVault]"

# RabbitMQ (or Azure Service Bus)
export RabbitMqSettings__HostName="rabbitmq.production.local"
export RabbitMqSettings__UserName="labbridge"
export RabbitMqSettings__Password="[SecurePasswordFromKeyVault]"

# Database
export DatabaseSettings__ConnectionString="Host=postgres.production.local;Database=labbridge_audit;Username=labbridge;Password=[SecurePasswordFromKeyVault]"
```

---

## 🧪 Testing Strategy

### Unit Tests (LabBridge.UnitTests)

**HL7 Parsing** (~15 tests):
- Parse ORU^R01 message successfully
- Extract MSH segment fields (Message Control ID, datetime)
- Extract PID segment fields (patient demographics)
- Extract OBR segment fields (panel code, datetime)
- Extract OBX segment fields (test code, value, units)
- Handle missing optional segments (PV1, NTE)
- Reject malformed messages (missing MSH, invalid structure)

**HL7 → FHIR Transformation** (~25 tests):
- Transform PID → Patient resource (valid demographics)
- Transform OBX (NM) → Observation with valueQuantity
- Transform OBX (CE) → Observation with valueCodeableConcept
- Transform OBR → DiagnosticReport with result references
- Map HL7 status codes → FHIR status (F→final, P→preliminary)
- Map HL7 gender codes → FHIR gender (M→male, F→female)
- Map HL7 abnormal flags → FHIR interpretation (H→high, L→low, N→normal)
- Handle missing optional fields gracefully
- Generate valid FHIR resource IDs

**ACK Generation** (~8 tests):
- Generate AA (Application Accept) for valid message
- Generate AE (Application Error) for validation failure
- Preserve MSH-10 (Message Control ID) in ACK
- Format ACK message correctly (MLLP envelope)

**Patient Matching** (~10 tests):
- Match patient by MRN (exact match)
- Create new patient if MRN not found
- Update patient demographics if changed (configurable)
- Handle multiple identifier systems

### Integration Tests (LabBridge.IntegrationTests)

**End-to-End** (~15 tests, using TestContainers):
- Send HL7v2 ORU^R01 → Verify FHIR resources created in LabFlow API
- Send multiple OBX segments → Verify multiple Observations created
- Send malformed message → Verify error logged, ACK AE returned
- FHIR API unavailable → Verify message queued for retry
- Retry 3x → Verify message moved to dead letter queue

**Performance** (~5 tests):
- Process 100 messages/minute sustained
- Handle 50 concurrent MLLP connections
- End-to-end latency < 2 seconds (p95)

---

## 📊 Monitoring & Alerts

### Metrics (Prometheus/Azure Monitor)

```
# Messages received
labbridge_messages_received_total{message_type="ORU^R01"}

# Messages processed successfully
labbridge_messages_processed_success_total{message_type="ORU^R01"}

# Messages failed
labbridge_messages_processed_failed_total{message_type="ORU^R01", error_type="ValidationError"}

# FHIR API call duration
labbridge_fhir_api_call_duration_seconds{operation="PostObservation", status="200"}

# Queue depth
labbridge_queue_depth{queue_name="hl7-to-fhir"}

# Processing latency
labbridge_message_processing_duration_seconds{p50, p95, p99}
```

### Alerts

**Critical**:
- RabbitMQ queue depth > 1000 (backlog building)
- FHIR API unavailable > 5 minutes
- Dead letter queue depth > 100

**Warning**:
- Message processing latency > 5 seconds (p95)
- FHIR API error rate > 5%
- Retry rate > 10%

---

## 🚀 Deployment

### Docker Compose (Development)

```yaml
version: '3.8'

services:
  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest

  postgres:
    image: postgres:16
    ports:
      - "5432:5432"
    environment:
      POSTGRES_DB: labbridge_audit
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: dev_password
    volumes:
      - postgres_data:/var/lib/postgresql/data

  labbridge:
    build: .
    ports:
      - "2575:2575"  # MLLP listener
    depends_on:
      - rabbitmq
      - postgres
    environment:
      FhirApiSettings__BaseUrl: "https://host.docker.internal:7000"
      RabbitMqSettings__HostName: "rabbitmq"
      DatabaseSettings__ConnectionString: "Host=postgres;Database=labbridge_audit;Username=postgres;Password=dev_password"

volumes:
  postgres_data:
```

### Azure Deployment (Production)

**Resources**:
- **Azure Container Apps** - LabBridge service (auto-scaling)
- **Azure Service Bus** - Replace RabbitMQ (managed service)
- **Azure Database for PostgreSQL** - Audit logs
- **Azure Monitor** - Logging and metrics
- **Azure Key Vault** - Secrets management

---

## 📝 Phase 1 Checklist (Week 1-2)

**Core Features**:
- [ ] Solution structure created
- [ ] NuGet packages installed (NHapi, Firely SDK, Refit, RabbitMQ.Client, EF Core)
- [ ] MLLP TCP listener (async server on port 2575)
- [ ] HL7v2 parser (NHapi integration, ORU^R01 support)
- [ ] ACK generator (AA, AE responses)
- [ ] HL7v2 → FHIR transformer (PID→Patient, OBX→Observation, OBR→DiagnosticReport)
- [ ] FHIR API client (Refit, authentication, retry with Polly)
- [ ] RabbitMQ integration (publish, consume, dead letter queue)
- [ ] Audit logger (EF Core, PostgreSQL)
- [ ] Configuration (appsettings.json, environment variables)
- [ ] Logging (Serilog, structured logs)

**Testing**:
- [ ] Unit tests: HL7 parsing (15 tests)
- [ ] Unit tests: HL7 → FHIR transformation (25 tests)
- [ ] Unit tests: ACK generation (8 tests)
- [ ] Integration test: End-to-end ORU^R01 → FHIR (1 test)

**Documentation**:
- [ ] README.md (project overview, getting started)
- [ ] MAPPING_SPECS.md (HL7 → FHIR field mapping)
- [ ] Sample HL7v2 messages (ORU_R01_CBC.hl7, etc.)
- [ ] Docker Compose (RabbitMQ + PostgreSQL + LabBridge)

**Total estimated time**: 40-60 hours (1-2 weeks full-time)

---

## 🔗 Referencias

### HL7v2
- [HL7 v2.5.1 Specification](http://www.hl7.eu/refactored/index.html)
- [NHapi GitHub](https://github.com/nHapiNET/nHapi)
- [HL7 Message Examples](https://hl7-definition.caristix.com/v2/HL7v2.5.1/TriggerEvents)

### FHIR
- [FHIR R4 Specification](http://hl7.org/fhir/R4/)
- [Firely SDK Docs](https://docs.fire.ly/projects/Firely-NET-SDK/)

### Messaging
- [RabbitMQ .NET Client](https://www.rabbitmq.com/tutorials/tutorial-one-dotnet.html)
- [MassTransit Documentation](https://masstransit-project.com/)

### Libraries
- [Refit](https://github.com/reactiveui/refit) - HTTP client
- [Polly](https://github.com/App-vNext/Polly) - Retry policies

---

**Última actualización**: 2025-10-16
**Versión**: 1.0 (Initial specification)
**Proyecto relacionado**: LabFlow FHIR API (este repositorio)
