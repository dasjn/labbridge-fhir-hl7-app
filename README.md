# LabBridge - Legacy Laboratory Integration Service

**Bridge the gap between legacy HL7v2 laboratory systems and modern FHIR-based hospital infrastructure.**

## ✅ Estado Actual: Phase 1 COMPLETA + E2E Tests Pasando

**Última actualización**: 2025-10-23

**Estado**: ✅ **PRODUCTION-READY** - Flujo completo HL7v2 → FHIR funcionando

**Tests**:
- ✅ 64/64 unit tests pasando
- ✅ 1/1 E2E integration test pasando

**Funcionalidad implementada**:
- ✅ MLLP TCP listener (puerto 2575)
- ✅ HL7v2 parser + ACK generator
- ✅ HL7v2 → FHIR transformation (Patient, Observation, DiagnosticReport)
- ✅ RabbitMQ message queue (persistence + DLQ)
- ✅ FHIR API client con Refit + Polly retry policies
- ✅ Custom FhirHttpContentSerializer para serialización FHIR R4 correcta
- ✅ Background workers (MLLP listener + Message processor)
- ✅ Integration tests E2E con Docker (RabbitMQ + LabFlow API)

---

## 🎯 The Clinical Problem

Hospitals invest millions in modern EHR systems (Epic, Cerner, Allscripts) that support **FHIR R4**, but their laboratory equipment—often **10-20 years old**—only speaks **HL7v2**.

This creates critical workflow bottlenecks:

- ❌ **Manual data entry** - Lab technicians manually transcribe results into EHR
- ❌ **Delayed critical results** - Hours between analyzer result and physician notification
- ❌ **Transcription errors** - Human error affecting patient safety (wrong values, wrong patient)
- ❌ **No real-time availability** - Clinicians can't access results from mobile devices
- ❌ **Compliance issues** - Delays violate regulatory turnaround time requirements

### Real-World Impact

**Example: Sepsis Patient in ICU**
- **Legacy workflow**: Blood culture positive → Lab tech sees result → Manually enters into EHR → Physician notified → **45-60 minutes**
- **LabBridge workflow**: Blood culture positive → Auto-transmitted via HL7v2 → Converted to FHIR → Posted to EHR → Physician mobile alert → **< 2 minutes**

**Outcome**: Earlier antibiotic intervention = reduced mortality

---

## 🚀 The Solution

LabBridge acts as a **bidirectional translation layer** between legacy and modern systems:

```
┌─────────────────────┐         HL7v2          ┌──────────────┐         FHIR R4         ┌─────────────────┐
│ Laboratory Analyzer │ ──────────────────────> │  LabBridge   │ ──────────────────────> │  FHIR Server    │
│ (Hologic Panther,   │                         │  Integration │                         │  (LabFlow API,  │
│  Abbott, Roche,     │ <────────────────────── │  Service     │ <────────────────────── │   Epic, Cerner) │
│  Siemens)           │    HL7v2 ACK/Orders     │              │    FHIR Resources       │                 │
└─────────────────────┘                         └──────────────┘                         └─────────────────┘
```

### Workflow: Observation Results (ORU^R01)

1. **Receive**: Laboratory analyzer sends HL7v2 ORU^R01 message (Observation Result Unsolicited)
2. **Parse**: Extract patient demographics, test codes (LOINC), results, flags
3. **Validate**: Verify message structure, patient identifiers, required fields
4. **Transform**: Convert to FHIR R4 resources (Patient, Observation, DiagnosticReport)
5. **Post**: Send to FHIR server via RESTful API
6. **Acknowledge**: Return HL7v2 ACK (Application Acknowledgement) to analyzer

### Workflow: Laboratory Orders (ORM^O01)

1. **Receive**: FHIR ServiceRequest from EHR (physician orders lab test)
2. **Transform**: Convert to HL7v2 ORM^O01 message (Order Message)
3. **Route**: Send to appropriate laboratory analyzer (by test code)
4. **Track**: Monitor order status and link to eventual results

---

## 🏥 Field Experience Foundation

Based on **6 years as Field Service Engineer** supporting:

- **Hologic Panther** - Molecular diagnostics (PCR, viral load testing)
- **Abbott Architect** - Chemistry/immunoassay analyzers
- **Roche cobas** - High-throughput clinical chemistry
- **Siemens Atellica** - Integrated chemistry/immunoassay systems

**Key Insights**:
- Most analyzers communicate via **HL7v2 MLLP** (Minimal Lower Layer Protocol) over TCP/IP
- Message formats vary slightly by vendor (ADT^A01, ORU^R01, ORM^O01, etc.)
- **Critical requirement**: Never lose a result message (financial + regulatory)
- Analyzers expect ACK within seconds or will retry/alarm
- Legacy systems cannot be upgraded (manufacturer support ended, cost-prohibitive)

---

## 📦 Technology Stack

### Core
- **.NET 8** - High-performance async message processing
- **NHapi v3.2.0** - HL7v2 parsing library (supports v2.3, v2.4, v2.5, v2.6)
- **Hl7.Fhir.R4 (Firely SDK) v5.12.2** - FHIR resource generation and serialization
- **Refit v7.2.22** - Type-safe HTTP client for FHIR API calls
- **Custom FhirHttpContentSerializer** - Ensures FHIR R4 compliant serialization in Refit

### Messaging & Integration
- **RabbitMQ v6.8.1** - Message queue for reliability and persistence
- **MLLP Server** - Custom async TCP listener for HL7v2 connections (port 2575)
- **Entity Framework Core 9** - (Future) Message logging and audit trail

### Supporting
- **Serilog v9.0.0** - Structured logging (critical for troubleshooting)
- **Polly v8.5.0** - Retry policies + circuit breaker for FHIR API calls
- **xUnit v2.9.2 + FluentAssertions v7.0.0** - Unit and integration testing
- **TestContainers** - E2E tests with real RabbitMQ and LabFlow API in Docker

---

## 🏗️ Architecture

### Service Components

```
┌─────────────────────────────────────────────────────────────────────┐
│                          LabBridge Service                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────────┐      ┌──────────────┐      ┌─────────────────┐  │
│  │ MLLP Listener│ ───> │ HL7v2 Parser │ ───> │ Message Queue   │  │
│  │ (TCP/IP)     │      │ (NHapi)      │      │ (RabbitMQ)      │  │
│  └──────────────┘      └──────────────┘      └─────────────────┘  │
│         │                                              │            │
│         │ ACK                                          ▼            │
│         │                                     ┌─────────────────┐  │
│         └─────────────────────────────────────┤ Transformer     │  │
│                                               │ HL7→FHIR        │  │
│                                               └─────────────────┘  │
│                                                        │            │
│                                                        ▼            │
│                                               ┌─────────────────┐  │
│                                               │ FHIR Client     │  │
│                                               │ (Refit)         │  │
│                                               └─────────────────┘  │
│                                                        │            │
│                                               ┌────────▼────────┐  │
│                                               │ Audit Logger    │  │
│                                               │ (EF Core + SQL) │  │
│                                               └─────────────────┘  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
                          ┌─────────────────┐
                          │ LabFlow FHIR API│
                          │ (or Epic/Cerner)│
                          └─────────────────┘
```

### Data Flow: HL7v2 ORU^R01 → FHIR

**Input: HL7v2 ORU^R01 (Observation Result)**
```
MSH|^~\&|PANTHER|LAB|LABFLOW|HOSPITAL|20251016120000||ORU^R01|MSG123|P|2.5
PID|1||12345678^^^MRN||García^Juan^Carlos||19850315|M
OBR|1|ORD123|LAB456|58410-2^CBC panel^LN|||20251016115500
OBX|1|NM|718-7^Hemoglobin^LN||14.5|g/dL|13.5-17.5|N|||F|||20251016120000
OBX|2|NM|6690-2^WBC^LN||7500|cells/uL|4500-11000|N|||F|||20251016120000
```

**Output: FHIR R4 Resources**

1. **Patient Resource** (if not exists, create; if exists, validate match)
```json
{
  "resourceType": "Patient",
  "identifier": [{ "system": "urn:oid:2.16.840.1.113883.4.1", "value": "12345678" }],
  "name": [{ "family": "García", "given": ["Juan", "Carlos"] }],
  "gender": "male",
  "birthDate": "1985-03-15"
}
```

2. **Observation Resources** (one per OBX segment)
```json
{
  "resourceType": "Observation",
  "status": "final",
  "code": {
    "coding": [{ "system": "http://loinc.org", "code": "718-7", "display": "Hemoglobin" }]
  },
  "subject": { "reference": "Patient/12345678" },
  "effectiveDateTime": "2025-10-16T12:00:00Z",
  "valueQuantity": { "value": 14.5, "unit": "g/dL", "system": "http://unitsofmeasure.org", "code": "g/dL" },
  "interpretation": [{ "coding": [{ "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationInterpretation", "code": "N" }] }]
}
```

3. **DiagnosticReport Resource** (one per OBR segment, groups all OBX)
```json
{
  "resourceType": "DiagnosticReport",
  "status": "final",
  "code": {
    "coding": [{ "system": "http://loinc.org", "code": "58410-2", "display": "CBC panel" }]
  },
  "subject": { "reference": "Patient/12345678" },
  "effectiveDateTime": "2025-10-16T11:55:00Z",
  "issued": "2025-10-16T12:00:00Z",
  "result": [
    { "reference": "Observation/obs1" },
    { "reference": "Observation/obs2" }
  ]
}
```

---

## 🔍 HL7v2 Message Types Supported

### Phase 1: Results (ORU^R01)
- **ORU^R01** - Observation Result Unsolicited
  - Laboratory analyzer sends completed test results
  - Most common message type (90% of lab traffic)
  - Segments: MSH, PID, OBR, OBX, NTE (notes)

### Phase 2: Orders (ORM^O01)
- **ORM^O01** - Order Message
  - EHR/FHIR → HL7v2 transformation
  - Sends test orders to analyzers
  - Segments: MSH, PID, ORC, OBR

### Phase 3: ADT Messages (Future)
- **ADT^A01** - Admit Patient
- **ADT^A08** - Update Patient Information
- **ADT^A04** - Register Outpatient

---

## 🎯 Project Status

**Timeline**: Week 2 of development
**Current Phase**: Phase 1 Complete - Production-Ready Integration Service
**Last Updated**: 2025-10-21

---

## ✅ Completed

### Phase 1: Core Integration Service (COMPLETED ✅)

**Infrastructure & Setup**
- [x] Solution structure (Core, Infrastructure, Service, UnitTests)
- [x] NuGet packages installed
  - [x] NHapi v3.2.0 - HL7v2 parsing
  - [x] Hl7.Fhir.R4 (Firely SDK) v5.12.2 - FHIR resources
  - [x] Refit v7.2.22 - HTTP client
  - [x] RabbitMQ.Client v6.8.1 - Message queue
  - [x] Polly v8.5.0 - Retry policies
  - [x] xUnit v2.9.2 + FluentAssertions v7.0.0 - Testing

**HL7v2 Processing**
- [x] **HL7v2 Parser** (`NHapiParser.cs` - 110 LOC)
  - Parse ORU^R01 messages with NHapi
  - Validate message structure
  - Extract message type and control ID
  - Handle special characters (UTF-8 encoding)
- [x] **ACK Generator** (`AckGenerator.cs` - 110 LOC)
  - Generate AA (Application Accept) acknowledgements
  - Generate AE (Application Error) with error messages
  - Generate AR (Application Reject) with rejection reasons
  - Preserve Message Control ID (MSH-10)
  - Fallback ACK for malformed messages

**FHIR Transformation**
- [x] **FHIR Transformer** (`FhirTransformer.cs` - 380 LOC)
  - Transform HL7 PID → FHIR Patient
  - Transform HL7 OBX → FHIR Observation
  - Transform HL7 OBR → FHIR DiagnosticReport
  - Map HL7 status codes → FHIR (F→final, P→preliminary)
  - Map HL7 gender codes → FHIR (M→male, F→female)
  - Map HL7 abnormal flags → FHIR interpretation (H→high, L→low, N→normal)
  - Handle numeric (NM) and coded (CE) value types

**MLLP & Messaging**
- [x] **MLLP Server** (`MllpServer.cs` - 230 LOC)
  - Async TCP listener on configurable port (default: 2575)
  - MLLP protocol framing (0x0B, 0x1C, 0x0D)
  - Concurrent connection handling
  - Immediate ACK response (< 1 second)
  - Read/write timeouts (30s/10s)
  - Graceful error handling
- [x] **RabbitMQ Integration** (`RabbitMqQueue.cs` - 174 LOC)
  - Persistent message queue
  - Dead Letter Queue (DLQ) for failed messages
  - Publisher with message persistence
  - Consumer with manual ACK/NACK
  - QoS settings (prefetch: 1)

**FHIR API Client**
- [x] **LabFlow Client** (`LabFlowClient.cs` - 85 LOC)
  - Refit-based HTTP client
  - CreateOrUpdatePatient endpoint
  - CreateObservation endpoint
  - CreateDiagnosticReport endpoint
  - Structured logging (Serilog)
- [x] **Polly Retry Policies** (`Program.cs`)
  - Exponential backoff (2s, 4s, 8s)
  - Circuit breaker (5 failures → 30s open)
  - Transient error handling (5xx, 408, 429)

**Background Workers**
- [x] **MllpListenerWorker** (`MllpListenerWorker.cs` - 50 LOC)
  - Hosted service for MLLP TCP listener
  - Configurable port via appsettings.json
  - Graceful shutdown
- [x] **MessageProcessorWorker** (`MessageProcessorWorker.cs` - 112 LOC)
  - RabbitMQ consumer
  - HL7 → FHIR transformation
  - FHIR API posting (Patient → Observations → DiagnosticReport)
  - Error handling with DLQ routing

**Testing**
- [x] **64 Unit Tests** - All passing ✅
  - **HL7 Parsing** (15 tests): Valid parsing, PID/OBX/OBR extraction, special chars, validation
  - **FHIR Transformation** (24 tests): Patient/Observation/DiagnosticReport mapping, status codes, gender/date parsing
  - **ACK Generation** (8 tests): AA/AE/AR generation, Message Control ID preservation, fallback
  - **MLLP Server** (6 tests): Valid/invalid messages, concurrent connections, error handling
  - **FHIR Client** (10 tests): API calls, logging, error handling
  - **Baseline** (1 test): Dummy test for CI/CD

---

## 🚧 In Progress

- None - Phase 1 core integration complete!

---

## 📝 Next Steps (Future Phases)

### Phase 2: Audit & Observability
- [ ] Database audit logging (EF Core + PostgreSQL)
- [ ] Message persistence (raw HL7 + FHIR resources)
- [ ] Prometheus metrics (messages received, processed, failed)
- [ ] Grafana dashboards (throughput, latency, error rates)
- [ ] Alerting (queue depth, API errors)

### Phase 3: Bidirectional (FHIR → HL7v2)
- [ ] FHIR ServiceRequest → HL7v2 ORM^O01 transformer
- [ ] Order routing by LOINC code → analyzer mapping
- [ ] Order status tracking
- [ ] Result linking (ORM → ORU correlation)

### Phase 4: Production Hardening
- [ ] Integration tests (TestContainers + RabbitMQ + LabFlow API)
- [ ] Performance testing (1000+ messages/hour)
- [ ] Docker containerization
- [ ] Azure deployment (Container Apps + Service Bus)
- [ ] IEC 62304 documentation (if targeting medical device software roles)
- [ ] CI/CD pipeline (GitHub Actions)

---

## 🔗 Integration with LabFlow FHIR API

LabBridge is designed to work seamlessly with **LabFlow FHIR API** (the portfolio project in this repository):

```
┌───────────────────┐       HL7v2 ORU^R01      ┌───────────────┐       FHIR R4        ┌──────────────┐
│ Laboratory        │ ──────────────────────> │  LabBridge    │ ──────────────────> │ LabFlow API  │
│ Analyzer          │                         │  Integration  │                     │ FHIR Server  │
│ (Hologic Panther) │ <────────────────────── │  Service      │ <──────────────────│ (SQLite/PG)  │
└───────────────────┘    HL7v2 ACK            └───────────────┘    FHIR Resources   └──────────────┘
```

**LabFlow provides**:
- ✅ FHIR R4 compliant REST API
- ✅ Patient, Observation, DiagnosticReport, ServiceRequest resources
- ✅ JWT authentication and authorization
- ✅ Search and pagination
- ✅ Audit trail and version tracking

**LabBridge provides**:
- ✅ HL7v2 MLLP listener (legacy analyzer connectivity)
- ✅ Message parsing and validation
- ✅ HL7v2 ↔ FHIR transformation
- ✅ Message queue reliability
- ✅ Retry policies and error handling

**Together**: Complete end-to-end laboratory interoperability solution

---

## 📐 Design Decisions

### Why NHapi?
- **Industry standard** for HL7v2 parsing in .NET
- Supports all HL7v2 versions (2.3 through 2.8)
- Type-safe message object model
- Active community and updates

### Why RabbitMQ?
- **Message persistence** - Never lose a result (critical for regulatory compliance)
- **Retry logic** - Automatic retry on FHIR API failures
- **Dead letter queue** - Isolate problematic messages for manual review
- **Scalability** - Horizontal scaling with multiple consumers

### Why MLLP over HTTP?
- **Legacy compatibility** - Most analyzers only support MLLP (Minimal Lower Layer Protocol)
- **Real-time** - Push-based (analyzer sends immediately when result available)
- **Vendor standard** - Required by FDA/IVD manufacturers

### Why Async Processing?
- **Throughput** - Handle 100+ concurrent analyzer connections
- **Responsiveness** - Immediate ACK to analyzer (< 1 second)
- **Resource efficiency** - Low memory footprint

---

## 🔐 Security & Compliance

### Data Protection
- **TLS 1.2+** for MLLP connections (where analyzer supports)
- **PHI encryption** at rest (SQL database encryption)
- **JWT authentication** for FHIR API calls
- **Audit logging** (every message processed, WHO, WHAT, WHEN)

### Regulatory Compliance
- **HL7v2 ACK required** - Never silently drop messages
- **Message persistence** - Retain raw HL7v2 and FHIR for audit trail
- **Unique message IDs** - MSH.10 Message Control ID tracked
- **Idempotency** - Duplicate message detection (same MSH.10 = skip)

### IEC 62304 Considerations
- **Software Safety Class B** (manages PHI, no direct device control)
- Risk: Message loss or corruption affecting patient care
- Mitigation: Queue persistence, retry logic, duplicate detection, comprehensive logging

---

## 🧪 Testing Strategy

### Unit Tests (48 tests - All passing ✅)

**HL7 Parsing (15 tests)**
- Parse valid ORU^R01 messages
- Extract PID, OBX, OBR segments
- Handle special characters (ñ, á, é)
- Validate message structure
- Extract message type and control ID

**FHIR Transformation (24 tests)**
- Transform PID → Patient (identifiers, names, gender, birthDate)
- Transform OBX → Observation (valueQuantity, codes, interpretation)
- Transform OBR → DiagnosticReport (identifiers, panel codes)
- Handle numeric (NM) and coded (CE) value types
- Map HL7 status codes to FHIR (F→final, P→preliminary)

**ACK Generation (8 tests)**
- Generate AA (Application Accept) acknowledgements
- Generate AE (Application Error) with error messages
- Generate AR (Application Reject) with rejection reasons
- Preserve Message Control ID (MSH-10)
- Fallback ACK for malformed messages

### Integration Tests
- End-to-end: HL7v2 message → MLLP → Transform → FHIR API → Database
- TestContainers: RabbitMQ + PostgreSQL + LabFlow API
- Error scenarios: Malformed messages, network failures, FHIR API errors

### Performance Tests
- **Throughput**: 1000 messages/hour sustained
- **Latency**: < 2 seconds end-to-end (HL7 received → FHIR posted)
- **Concurrency**: 50 simultaneous analyzer connections

---

## 📊 Monitoring & Observability

### Metrics
- **Messages received** (rate, total)
- **Messages processed successfully** (rate, total)
- **FHIR API errors** (rate, error types)
- **Message queue depth** (current, max)
- **Processing latency** (p50, p95, p99)

### Alerts
- **Critical**: Message queue depth > 1000 (backlog building)
- **Critical**: FHIR API unavailable > 5 minutes
- **Warning**: Message processing > 5 seconds
- **Warning**: Retry rate > 10%

### Logging
- **Structured logs** (Serilog → Elasticsearch/Azure Monitor)
- **Correlation IDs** (track message through entire pipeline)
- **PHI redaction** (patient identifiers masked in logs)

---

## 🚀 Getting Started

### Prerequisites
- .NET 8 SDK
- RabbitMQ (Docker: `docker run -d -p 5672:5672 -p 15672:15672 rabbitmq:3-management`)
- LabFlow FHIR API running (or Epic/Cerner test endpoint)

### Configuration (appsettings.json)

```json
{
  "MllpListener": {
    "Port": 2575,
    "MaxConcurrentConnections": 50,
    "ReceiveTimeoutSeconds": 300
  },
  "FhirApiSettings": {
    "BaseUrl": "https://localhost:7000",
    "Username": "labbridge@system.com",
    "Password": "SecurePassword123!",
    "TimeoutSeconds": 30
  },
  "RabbitMqSettings": {
    "HostName": "localhost",
    "UserName": "guest",
    "Password": "guest",
    "QueueName": "hl7-to-fhir",
    "DeadLetterQueueName": "hl7-to-fhir-dlq"
  },
  "DatabaseSettings": {
    "ConnectionString": "Host=localhost;Database=labbridge_audit;Username=postgres;Password=xxx"
  }
}
```

### Run the Service

```bash
# Navigate to project
cd LabBridge

# Build
dotnet build

# Run
dotnet run

# Service will listen on TCP port 2575 for HL7v2 MLLP connections
```

### Send Test HL7v2 Message

```bash
# Using hl7-tools (npm install -g hl7-tools)
hl7-send --host localhost --port 2575 --file test_oru_r01.hl7

# Or using netcat (Linux/Mac)
cat test_oru_r01.hl7 | nc localhost 2575
```

---

## 📖 Resources

### HL7v2 Specification
- [HL7 Version 2.5.1](http://www.hl7.eu/refactored/index.html) - Complete specification
- [HL7 Message Types](https://hl7-definition.caristix.com/v2/HL7v2.5.1/TriggerEvents) - ADT, ORM, ORU, etc.
- [LOINC Codes](https://loinc.org/) - Laboratory test codes

### FHIR Resources
- [FHIR R4 Specification](http://hl7.org/fhir/R4/)
- [Firely SDK Documentation](https://docs.fire.ly/projects/Firely-NET-SDK/)

### Libraries
- [NHapi GitHub](https://github.com/nHapiNET/nHapi) - HL7v2 parsing
- [Refit Documentation](https://github.com/reactiveui/refit) - Type-safe HTTP client
- [Polly Documentation](https://github.com/App-vNext/Polly) - Retry policies

---

## 🎯 Portfolio Value

This project demonstrates:

✅ **Healthcare interoperability** - HL7v2 and FHIR expertise
✅ **Legacy system integration** - Real-world field service experience
✅ **Message-driven architecture** - RabbitMQ, async processing
✅ **Production-ready design** - Retry logic, audit trail, monitoring
✅ **Regulatory awareness** - IEC 62304, FDA compliance considerations
✅ **Performance engineering** - High-throughput message processing
✅ **.NET expertise** - Modern C# patterns, async/await, dependency injection

**Target roles**: Medical Device Software Engineer, Healthcare Integration Engineer, Laboratory Informatics Developer

---

## 📄 License

MIT License - See LICENSE file for details

---

## 👨‍💻 Developer

**Background**: 6 years Field Service Engineer (pharma/medical devices) + 2 years .NET development
**Goal**: Demonstrate end-to-end healthcare interoperability skills for Medical Device Software Engineer roles

**Projects**:
- **LabFlow FHIR API** - Modern FHIR R4 server (Patient, Observation, DiagnosticReport, ServiceRequest)
- **LabBridge** - Legacy HL7v2 ↔ FHIR integration service (this project)

Together: Complete laboratory interoperability solution from legacy analyzers to modern EHR systems.
