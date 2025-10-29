# Changelog

All notable changes to LabBridge will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-10-24

### ✅ PRODUCTION READY - Phase 1 Complete

**65/65 tests passing** (64 unit + 1 E2E integration)

### Added

#### Core Features
- **MLLP TCP Listener** (`MllpServer.cs`) - Async server on port 2575
  - Concurrent connection handling
  - MLLP protocol framing (0x0B start, 0x1C end, 0x0D terminator)
  - Read/write timeouts
  - Graceful shutdown

- **HL7v2 Parser** (`NHapiParser.cs`) - NHapi v3.2.0 integration
  - Support for HL7v2 versions 2.3 to 2.6
  - ORU^R01 message parsing
  - Segment extraction (MSH, PID, OBR, OBX)

- **ACK Generator** (`AckGenerator.cs`)
  - AA (Application Accept) for valid messages
  - AE (Application Error) for validation failures
  - Preserves MSH-10 (Message Control ID)

- **HL7v2 → FHIR Transformer** (`FhirTransformer.cs`)
  - PID segment → Patient resource
  - OBX segments → Observation resources (multiple)
  - OBR segment → DiagnosticReport resource
  - LOINC code mapping
  - Unit conversion and normalization

- **FHIR API Client** (`LabFlowClient.cs`)
  - Refit v7.2.22 HTTP client
  - Polly v8.5.0 retry policies (exponential backoff)
  - Circuit breaker pattern
  - Structured logging

- **FhirHttpContentSerializer** (`FhirHttpContentSerializer.cs`)
  - Custom Refit content serializer for FHIR R4
  - Uses FhirJsonSerializer for serialization
  - Reflection-based deserialization (fixes Parse<Base> abstract type error)
  - Validates types inherit from Hl7.Fhir.Model.Base

- **RabbitMQ Integration** (`RabbitMqQueue.cs`)
  - Persistent message queue
  - Dead Letter Queue (DLQ) for failed messages
  - Manual ACK/NACK
  - Publisher confirms
  - QoS settings (prefetch: 1)

- **Background Workers**
  - `MllpListenerWorker.cs` - TCP listener service
  - `MessageProcessorWorker.cs` - RabbitMQ consumer + FHIR posting

#### Testing
- **64 Unit Tests** (xUnit + FluentAssertions)
  - 15 tests: HL7 Parsing
  - 24 tests: FHIR Transformation
  - 8 tests: ACK Generation
  - 6 tests: MLLP Server
  - 10 tests: FHIR Client
  - 1 test: Baseline

- **1 E2E Integration Test** (`EndToEndTests.cs`)
  - Full flow: MLLP → Parser → RabbitMQ → Transformer → FHIR API
  - Docker Compose setup (RabbitMQ + LabFlow API)
  - Validates Patient, Observation, DiagnosticReport creation
  - Verifies ACK response

#### Infrastructure
- **Docker Compose** (`docker-compose.test.yml`)
  - RabbitMQ 3.13 with management UI
  - LabFlow FHIR API (separate project)
  - Automatic health checks
  - Volume persistence for testing

### Fixed

- **Bug #1** (2025-10-21): HL7v2 messages used `\n` instead of `\r` as segment separator
  - **Impact**: Parser failures
  - **Fix**: Updated all test messages to use `\r` (carriage return)

- **Bug #2** (2025-10-21): MessageProcessor didn't parse before transforming
  - **Impact**: Transformation received raw string instead of parsed message
  - **Fix**: Added IHL7Parser dependency to MessageProcessorWorker

- **Bug #3** (2025-10-21): HTTP 500 authentication errors in LabFlow API
  - **Impact**: E2E test failures
  - **Fix**: Implemented TestAuthHandler to bypass authentication in testing mode

- **Bug #4** (2025-10-21): HTTP 500 "no such table" errors
  - **Impact**: Database not initialized
  - **Fix**: EF Core migrations applied automatically on LabFlow API startup

- **Bug #5** (2025-10-21): HTTP 400 serialization errors
  - **Impact**: LabFlow API couldn't parse FHIR resources
  - **Fix**: Changed serialization from object to StringContent + FhirJsonSerializer

- **Bug #6** (2025-10-24): `Parse<Base>` abstract type error in FhirHttpContentSerializer
  - **Impact**: HTTP 201 responses couldn't be deserialized
  - **Error**: `System.ArgumentException: The type of a node must be a concrete type, 'Base' is abstract.`
  - **Root Cause**: `_parser.Parse<Base>(json)` attempted to parse as abstract base class
  - **Fix**: Implemented reflection-based deserialization
    ```csharp
    var resourceType = typeof(T);
    var parseMethod = _parser.GetType()
        .GetMethod(nameof(FhirJsonParser.Parse), new[] { typeof(string) })!
        .MakeGenericMethod(resourceType);
    var resource = parseMethod.Invoke(_parser, new object[] { json });
    return (T?)resource;
    ```
  - **Result**: All FHIR resources deserialize correctly, zero errors in logs

### Technical Details

**LOC (Lines of Code)**:
- NHapiParser: 110
- AckGenerator: 110
- FhirTransformer: 380
- MllpServer: 230
- RabbitMqQueue: 174
- LabFlowClient: 85
- FhirHttpContentSerializer: 82
- MllpListenerWorker: 50
- MessageProcessorWorker: 112
- **Total Core Logic**: ~1,333 LOC

**Dependencies**:
- .NET 8 (C# 12)
- NHapi v3.2.0
- Hl7.Fhir.R4 v5.12.2 (Firely SDK)
- Refit v7.2.22
- Polly v8.5.0
- RabbitMQ.Client v6.8.1
- Serilog v9.0.0
- xUnit v2.9.2
- FluentAssertions v7.0.0

### Known Limitations

- Only supports ORU^R01 (Observation Results) - Inbound direction
- ORM^O01 (Laboratory Orders) - Not yet implemented (planned for Phase 3)
- No database audit logging (planned for Phase 2)
- No Prometheus metrics (planned for Phase 2)
- No performance testing (planned for Phase 4)

### Migration Notes

- No database migrations required (Phase 1 doesn't use database)
- Docker Compose required for E2E testing
- RabbitMQ required for message queue
- LabFlow FHIR API required for E2E testing (separate project)

---

## [1.1.0] - 2025-10-28

### ✅ PRODUCTION READY - Phase 2 Complete (Audit & Observability)

**65/65 tests passing** (64 unit + 1 E2E integration with audit validation)

### Added

#### Audit Logging
- **PostgreSQL Audit Logging** (`AuditLogger.cs`, `AuditDbContext.cs`)
  - 18-field audit log schema (raw HL7, FHIR JSON, status, timing, errors)
  - 6 optimized indexes for fast queries
  - EF Core 9.0.4 with Npgsql
  - FDA 21 CFR Part 11 compliance
  - Try-catch wrapper (audit failures don't block message processing)
  - Auto-migration on startup

#### Observability
- **Prometheus Metrics** (`LabBridgeMetrics.cs`)
  - 12 metrics: 5 counters, 3 histograms, 3 gauges, 1 summary
  - MLLP metrics (connections, parsing duration, ACKs)
  - Processing metrics (success/failure, duration, E2E latency)
  - FHIR API metrics (calls by resource type, status code, duration)
  - HTTP `/metrics` endpoint (port 5000)
  - HTTP `/health` endpoint for status checks

- **Grafana Dashboard** (pre-configured JSON)
  - 10 panels visualizing all metrics
  - Real-time monitoring with 10s auto-refresh
  - Success rate gauge with thresholds
  - Latency percentiles (p50, p90, p99)
  - Queue depth tracking

#### Testing & Automation
- **Automated Test Scripts**
  - `run-e2e-tests.ps1` - Full E2E workflow with Docker lifecycle
  - `run-e2e-tests.sh` - Bash equivalent for Linux/Mac/WSL
  - `run-unit-tests.ps1` - Fast unit test runner (no Docker)
  - `scripts/README.md` - Comprehensive documentation

- **Testing Utilities**
  - `send_continuous_traffic.ps1` - Realistic lab traffic generator
  - `test_message.hl7` - Sample HL7v2 ORU^R01 for manual testing

#### Documentation
- **INFRASTRUCTURE_GUIDE.md** (1900+ lines)
  - Deep-dive into every component (MLLP, NHapi, RabbitMQ, Polly, etc.)
  - Design decisions and trade-offs
  - Observability stack explanation
  - Failure modes and resilience patterns
  - Real-world field service insights

### Changed

- **Program.cs** - Switched from Host to WebApplication for HTTP endpoints
- **docker-compose.test.yml** - Added PostgreSQL, changed to tmpfs for volatile storage
- **EndToEndTests.cs** - Added migration application, fixed PostgreSQL credentials
- **ILabFlowApi.cs** - Changed return types to IApiResponse<T> for status code access
- **LabFlowClient.cs** - Extract real HTTP status codes instead of hardcoded values
- **LabFlowClientTests.cs** - Updated all 10 tests to mock IApiResponse<T>

### Fixed

- **Bug #7** (2025-10-28): Hardcoded HTTP status codes in metrics
  - **Impact**: Prometheus always showed "201" or "500", not real status codes
  - **Fix**: Use IApiResponse<T> to extract actual StatusCode

- **Bug #8** (2025-10-28): E2E test audit log failures
  - **Impact**: "relation AuditLogs does not exist" error
  - **Fix**: Apply EF Core migrations in InitializeAsync() before starting service

- **Bug #9** (2025-10-28): PostgreSQL authentication failures in E2E tests
  - **Impact**: Connection refused errors
  - **Fix**: Align connection string credentials with docker-compose (labbridge/labbridge123)

### Technical Details

**New Dependencies**:
- Microsoft.EntityFrameworkCore 9.0.4
- Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4
- prometheus-net 8.2.1
- prometheus-net.AspNetCore 8.2.1
- System.Text.Json 9.0.4

**Additional LOC**:
- AuditLogger: 145
- AuditDbContext: 85
- LabBridgeMetrics: 160
- Scripts: 400+
- **Total Phase 2**: ~790 LOC

## [Unreleased]

### Planned for Phase 3
- [ ] Bidirectional flow: FHIR → HL7v2
- [ ] ORM^O01 transformer (Laboratory Orders)
- [ ] Order routing by test code
- [ ] Order status tracking

### Planned for Phase 4
- [ ] Performance testing (1000+ messages/hour)
- [ ] Azure deployment (Container Apps)
- [ ] IEC 62304 documentation
- [ ] CI/CD pipeline (GitHub Actions)

---

**Project maintained by**: David (Medical Device Software Engineer)
**Related projects**: LabFlow FHIR API (separate repository)
