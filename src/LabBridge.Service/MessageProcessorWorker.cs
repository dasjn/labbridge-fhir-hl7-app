using System.Diagnostics;
using Hl7.Fhir.Serialization;
using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabBridge.Service;

public class MessageProcessorWorker : BackgroundService
{
    private readonly IMessageQueue _messageQueue;
    private readonly IHL7Parser _parser;
    private readonly IHL7ToFhirTransformer _transformer;
    private readonly IFhirClient _fhirClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageProcessorWorker> _logger;
    private readonly FhirJsonSerializer _fhirSerializer;

    public MessageProcessorWorker(
        IMessageQueue messageQueue,
        IHL7Parser parser,
        IHL7ToFhirTransformer transformer,
        IFhirClient fhirClient,
        IServiceScopeFactory scopeFactory,
        ILogger<MessageProcessorWorker> logger)
    {
        _messageQueue = messageQueue;
        _parser = parser;
        _transformer = transformer;
        _fhirClient = fhirClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _fhirSerializer = new FhirJsonSerializer();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MessageProcessorWorker starting...");

        try
        {
            await _messageQueue.StartConsumingAsync(ProcessMessageAsync, stoppingToken);

            // Keep the worker running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MessageProcessorWorker stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in MessageProcessorWorker");
            throw;
        }
    }

    private async Task ProcessMessageAsync(string hl7Message)
    {
        var stopwatch = Stopwatch.StartNew();
        var receivedAt = DateTimeOffset.UtcNow;

        string? messageControlId = null;
        string? messageType = null;
        string? patientId = null;
        string? sourceSystem = null;

        _logger.LogInformation("Processing HL7 message from queue ({Size} bytes)", hl7Message.Length);

        try
        {
            // Parse HL7 message first
            var parsedMessage = _parser.Parse(hl7Message);

            // Extract metadata for audit logging
            messageType = _parser.GetMessageType(hl7Message);
            messageControlId = _parser.GetMessageControlId(hl7Message);

            _logger.LogInformation("HL7 message parsed successfully: Type={MessageType}, ControlId={MessageControlId}",
                messageType, messageControlId);

            // Transform HL7 to FHIR
            var result = _transformer.Transform(parsedMessage);

            // Extract patient ID for audit logging
            patientId = result.Patient?.Identifier?.FirstOrDefault()?.Value;

            _logger.LogInformation("Transformed HL7 to FHIR: Patient exists={HasPatient}, Observations={ObsCount}, Report exists={HasReport}",
                result.Patient != null,
                result.Observations?.Count ?? 0,
                result.DiagnosticReport != null);

            // Send to FHIR API (LabFlow)
            if (result.Patient != null)
            {
                var patient = await _fhirClient.CreateOrUpdatePatientAsync(result.Patient);
                _logger.LogInformation("Patient created/updated in FHIR API: FhirId={FhirId}", patient.Id);

                // Create observations and track their FHIR IDs
                var createdObservationIds = new List<string>();
                if (result.Observations != null && result.Observations.Count > 0)
                {
                    foreach (var observation in result.Observations)
                    {
                        // Update subject reference to use FHIR patient ID
                        observation.Subject = new Hl7.Fhir.Model.ResourceReference($"Patient/{patient.Id}");

                        var createdObs = await _fhirClient.CreateObservationAsync(observation);
                        createdObservationIds.Add(createdObs.Id);
                        _logger.LogInformation("Observation created in FHIR API: FhirId={FhirId}", createdObs.Id);
                    }
                }

                // Create diagnostic report with updated observation references
                if (result.DiagnosticReport != null)
                {
                    // Update subject reference to use FHIR patient ID
                    result.DiagnosticReport.Subject = new Hl7.Fhir.Model.ResourceReference($"Patient/{patient.Id}");

                    // CRITICAL: Update Result references to use actual FHIR observation IDs
                    if (createdObservationIds.Count > 0)
                    {
                        result.DiagnosticReport.Result = createdObservationIds
                            .Select(id => new Hl7.Fhir.Model.ResourceReference($"Observation/{id}"))
                            .ToList();
                    }

                    var report = await _fhirClient.CreateDiagnosticReportAsync(result.DiagnosticReport);
                    _logger.LogInformation("DiagnosticReport created in FHIR API: FhirId={FhirId}, Results={ResultCount}",
                        report.Id, report.Result?.Count ?? 0);
                }

                stopwatch.Stop();
                _logger.LogInformation("Successfully processed and sent HL7 message to FHIR API (Duration={DurationMs}ms)", stopwatch.ElapsedMilliseconds);

                // Track Prometheus metrics
                LabBridgeMetrics.MessagesProcessedSuccess.WithLabels(messageType ?? "UNKNOWN").Inc();
                LabBridgeMetrics.MessageProcessingDuration.WithLabels(messageType ?? "UNKNOWN").Observe(stopwatch.Elapsed.TotalSeconds);
                LabBridgeMetrics.E2EMessageLatency.WithLabels(messageType ?? "UNKNOWN").Observe(stopwatch.Elapsed.TotalSeconds);

                // Log success to audit database
                await LogAuditSuccessAsync(
                    messageControlId ?? "UNKNOWN",
                    messageType ?? "UNKNOWN",
                    hl7Message,
                    result,
                    patientId,
                    sourceSystem,
                    receivedAt,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning("No patient data in transformation result, skipping FHIR API submission");

                stopwatch.Stop();

                // Log as failure - no patient data
                await LogAuditFailureAsync(
                    messageControlId ?? "UNKNOWN",
                    messageType ?? "UNKNOWN",
                    hl7Message,
                    "No patient data in transformation result",
                    null,
                    patientId,
                    sourceSystem,
                    receivedAt);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Error processing HL7 message and sending to FHIR API (Duration={DurationMs}ms)", stopwatch.ElapsedMilliseconds);

            // Track Prometheus metrics
            var errorType = ex.GetType().Name; // e.g., "HL7Exception", "HttpRequestException", etc.
            LabBridgeMetrics.MessagesProcessedFailure.WithLabels(messageType ?? "UNKNOWN", errorType).Inc();

            // Log failure to audit database
            await LogAuditFailureAsync(
                messageControlId ?? "UNKNOWN",
                messageType ?? "UNKNOWN",
                hl7Message,
                ex.Message,
                ex.StackTrace,
                patientId,
                sourceSystem,
                receivedAt);

            throw; // Re-throw to trigger RabbitMQ NACK and send to DLQ
        }
    }

    private async Task LogAuditSuccessAsync(
        string messageControlId,
        string messageType,
        string rawHl7Message,
        Core.Models.TransformationResult transformationResult,
        string? patientId,
        string? sourceSystem,
        DateTimeOffset receivedAt,
        long processingDurationMs)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

            // Serialize FHIR resources to JSON
            var patientJson = transformationResult.Patient != null
                ? _fhirSerializer.SerializeToString(transformationResult.Patient)
                : null;

            var observationsJson = transformationResult.Observations != null && transformationResult.Observations.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(
                    transformationResult.Observations.Select(o => _fhirSerializer.SerializeToString(o)))
                : null;

            var reportJson = transformationResult.DiagnosticReport != null
                ? _fhirSerializer.SerializeToString(transformationResult.DiagnosticReport)
                : null;

            await auditLogger.LogSuccessAsync(
                messageControlId,
                messageType,
                rawHl7Message,
                patientJson,
                observationsJson,
                reportJson,
                patientId,
                sourceSystem,
                "http://localhost:5000", // TODO: Get from configuration
                receivedAt,
                processingDurationMs);
        }
        catch (Exception ex)
        {
            // Audit logging failure should not break message processing
            _logger.LogError(ex, "Failed to log audit success for MessageControlId={MessageControlId}", messageControlId);
        }
    }

    private async Task LogAuditFailureAsync(
        string messageControlId,
        string messageType,
        string rawHl7Message,
        string errorMessage,
        string? errorStackTrace,
        string? patientId,
        string? sourceSystem,
        DateTimeOffset receivedAt)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

            await auditLogger.LogFailureAsync(
                messageControlId,
                messageType,
                rawHl7Message,
                errorMessage,
                errorStackTrace,
                patientId,
                sourceSystem,
                receivedAt);
        }
        catch (Exception ex)
        {
            // Audit logging failure should not break message processing
            _logger.LogError(ex, "Failed to log audit failure for MessageControlId={MessageControlId}", messageControlId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MessageProcessorWorker stopping...");
        await _messageQueue.StopConsumingAsync();
        await base.StopAsync(cancellationToken);
    }
}
