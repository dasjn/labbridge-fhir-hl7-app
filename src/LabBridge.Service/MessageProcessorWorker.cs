using LabBridge.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabBridge.Service;

public class MessageProcessorWorker : BackgroundService
{
    private readonly IMessageQueue _messageQueue;
    private readonly IHL7ToFhirTransformer _transformer;
    private readonly ILogger<MessageProcessorWorker> _logger;

    public MessageProcessorWorker(
        IMessageQueue messageQueue,
        IHL7ToFhirTransformer transformer,
        ILogger<MessageProcessorWorker> logger)
    {
        _messageQueue = messageQueue;
        _transformer = transformer;
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

        // Transform HL7 to FHIR
        var result = _transformer.Transform(hl7Message);

        _logger.LogInformation("Transformed HL7 to FHIR: Patient exists={HasPatient}, Observations={ObsCount}, Report exists={HasReport}",
            result.Patient != null,
            result.Observations?.Count ?? 0,
            result.DiagnosticReport != null);

        // TODO: In next phase, send to FHIR API (LabFlow) using Refit client
        // For now, just log the transformation

        await Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MessageProcessorWorker stopping...");
        await _messageQueue.StopConsumingAsync();
        await base.StopAsync(cancellationToken);
    }
}
