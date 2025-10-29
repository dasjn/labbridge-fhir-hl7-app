namespace LabBridge.Core.Interfaces;

/// <summary>
/// Interface for MLLP (Minimum Lower Layer Protocol) TCP server
/// Receives HL7v2 messages from laboratory analyzers via MLLP protocol
/// </summary>
public interface IMllpServer
{
    /// <summary>
    /// Start the MLLP server on the specified TCP port
    /// Listens for incoming connections from laboratory analyzers
    /// </summary>
    /// <param name="port">TCP port to listen on (typically 2575 for MLLP)</param>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown</param>
    /// <returns>Task that completes when server stops</returns>
    Task StartAsync(int port, CancellationToken cancellationToken);

    /// <summary>
    /// Stop the MLLP server and close all active connections
    /// Waits for in-flight messages to complete processing (up to 10 seconds)
    /// </summary>
    /// <returns>Task that completes when server is fully stopped</returns>
    Task StopAsync();
}
