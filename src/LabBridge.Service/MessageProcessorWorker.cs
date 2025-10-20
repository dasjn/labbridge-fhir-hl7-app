using LabBridge.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabBridge.Service;

public class MessageProcessorWorker : BackgroundService
{
    private readonly IMessageQueue _messageQueue;
    private readonly IHL7ToFhirTransformer _transformer;
    private readonly IFhirClient _fhirClient;
    private readonly ILogger<MessageProcessorWorker> _logger;

    public MessageProcessorWorker(
        IMessageQueue messageQueue,
        IHL7ToFhirTransformer transformer,
        IFhirClient fhirClient,
        ILogger<MessageProcessorWorker> logger)
    {
        _messageQueue = messageQueue;
        _transformer = transformer;
        _fhirClient = fhirClient;
        _logger = logger;
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
        _logger.LogInformation("Processing HL7 message from queue ({Size} bytes)", hl7Message.Length);

        try
        {
            // Transform HL7 to FHIR
            var result = _transformer.Transform(hl7Message);

            _logger.LogInformation("Transformed HL7 to FHIR: Patient exists={HasPatient}, Observations={ObsCount}, Report exists={HasReport}",
                result.Patient != null,
                result.Observations?.Count ?? 0,
                result.DiagnosticReport != null);

            // Send to FHIR API (LabFlow)
            if (result.Patient != null)
            {
                var patient = await _fhirClient.CreateOrUpdatePatientAsync(result.Patient);
                _logger.LogInformation("Patient created/updated in FHIR API: FhirId={FhirId}", patient.Id);

                // Create observations
                if (result.Observations != null && result.Observations.Count > 0)
                {
                    foreach (var observation in result.Observations)
                    {
                        // Update subject reference to use FHIR patient ID
                        observation.Subject = new Hl7.Fhir.Model.ResourceReference($"Patient/{patient.Id}");

                        var createdObs = await _fhirClient.CreateObservationAsync(observation);
                        _logger.LogInformation("Observation created in FHIR API: FhirId={FhirId}", createdObs.Id);
                    }
                }

                // Create diagnostic report
                if (result.DiagnosticReport != null)
                {
                    // Update subject reference to use FHIR patient ID
                    result.DiagnosticReport.Subject = new Hl7.Fhir.Model.ResourceReference($"Patient/{patient.Id}");

                    var report = await _fhirClient.CreateDiagnosticReportAsync(result.DiagnosticReport);
                    _logger.LogInformation("DiagnosticReport created in FHIR API: FhirId={FhirId}", report.Id);
                }

                _logger.LogInformation("Successfully processed and sent HL7 message to FHIR API");
            }
            else
            {
                _logger.LogWarning("No patient data in transformation result, skipping FHIR API submission");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing HL7 message and sending to FHIR API");
            throw; // Re-throw to trigger RabbitMQ NACK and send to DLQ
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MessageProcessorWorker stopping...");
        await _messageQueue.StopConsumingAsync();
        await base.StopAsync(cancellationToken);
    }
}
