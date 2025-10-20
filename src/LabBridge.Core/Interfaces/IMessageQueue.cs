namespace LabBridge.Core.Interfaces;

public interface IMessageQueue
{
    /// <summary>
    /// Publishes an HL7v2 message to the queue for processing
    /// </summary>
    Task PublishAsync(string hl7Message, string messageControlId);

    /// <summary>
    /// Starts consuming messages from the queue
    /// </summary>
    Task StartConsumingAsync(Func<string, Task> messageHandler, CancellationToken cancellationToken);

    /// <summary>
    /// Stops consuming messages
    /// </summary>
    Task StopConsumingAsync();
}
