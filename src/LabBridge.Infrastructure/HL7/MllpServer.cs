using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace LabBridge.Infrastructure.HL7;

public class MllpServer : IMllpServer
{
    private readonly IHL7Parser _parser;
    private readonly IAckGenerator _ackGenerator;
    private readonly IMessageQueue _messageQueue;
    private readonly ILogger<MllpServer> _logger;
    private TcpListener? _listener;
    private readonly List<Task> _clientTasks = new();

    // MLLP protocol bytes
    private const byte StartByte = 0x0B;  // Vertical Tab
    private const byte EndByte = 0x1C;    // File Separator
    private const byte CarriageReturn = 0x0D;

    public MllpServer(
        IHL7Parser parser,
        IAckGenerator ackGenerator,
        IMessageQueue messageQueue,
        ILogger<MllpServer> logger)
    {
        _parser = parser;
        _ackGenerator = ackGenerator;
        _messageQueue = messageQueue;
        _logger = logger;
    }

    public async Task StartAsync(int port, CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        _logger.LogInformation("MLLP Server started on port {Port}", port);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);

                // Handle client connection asynchronously (fire and forget)
                var clientTask = Task.Run(async () => await HandleClientAsync(client, cancellationToken), cancellationToken);
                _clientTasks.Add(clientTask);

                // Clean up completed tasks
                _clientTasks.RemoveAll(t => t.IsCompleted);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client connection");
            }
        }
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping MLLP Server...");

        _listener?.Stop();

        // Wait for all client tasks to complete (with timeout)
        await Task.WhenAll(_clientTasks).WaitAsync(TimeSpan.FromSeconds(10));

        _logger.LogInformation("MLLP Server stopped");
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var clientEndpoint = client.Client.RemoteEndPoint?.ToString();
        _logger.LogInformation("Client connected: {ClientEndpoint}", clientEndpoint);

        // Track active connections
        LabBridgeMetrics.ActiveMllpConnections.Inc();

        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                // Set timeouts to prevent hanging connections
                stream.ReadTimeout = 30000;  // 30 seconds
                stream.WriteTimeout = 10000; // 10 seconds

                var buffer = new byte[8192];
                var messageBuffer = new List<byte>();
                var bytesRead = 0;

                while (!cancellationToken.IsCancellationRequested &&
                       (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    messageBuffer.AddRange(buffer.Take(bytesRead));

                    // Check if we have a complete MLLP message
                    if (HasCompleteMessage(messageBuffer))
                    {
                        var hl7Message = ExtractMessage(messageBuffer);

                        _logger.LogInformation("Received HL7 message from {ClientEndpoint} ({Size} bytes)",
                            clientEndpoint, hl7Message.Length);

                        // Process message and generate ACK
                        var ackMessage = await ProcessMessageAsync(hl7Message);

                        // Send ACK response with MLLP framing
                        var ackBytes = WrapWithMllpFraming(ackMessage);

                        try
                        {
                            await stream.WriteAsync(ackBytes, 0, ackBytes.Length, cancellationToken);
                            await stream.FlushAsync(cancellationToken);

                            _logger.LogInformation("Sent ACK to {ClientEndpoint}", clientEndpoint);
                        }
                        catch (IOException ioEx) when (ioEx.InnerException is SocketException)
                        {
                            // Client closed connection before ACK could be sent - this is expected under high load
                            _logger.LogWarning("Client {ClientEndpoint} closed connection before ACK could be sent", clientEndpoint);
                            break;
                        }

                        // Clear buffer for next message (if any)
                        messageBuffer.Clear();
                    }
                }
            }
        }
        catch (IOException ioEx) when (ioEx.InnerException is SocketException socketEx)
        {
            // Network errors (connection reset, forcibly closed, etc.) are expected under high load
            _logger.LogWarning("Network error with client {ClientEndpoint}: {ErrorCode}",
                clientEndpoint, socketEx.ErrorCode);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected during shutdown
            _logger.LogDebug("Client handler cancelled for {ClientEndpoint}", clientEndpoint);
        }
        catch (Exception ex)
        {
            // Unexpected errors should still be logged as errors
            _logger.LogError(ex, "Unexpected error handling client {ClientEndpoint}", clientEndpoint);
        }
        finally
        {
            // Track connection closed
            LabBridgeMetrics.ActiveMllpConnections.Dec();
            _logger.LogInformation("Client disconnected: {ClientEndpoint}", clientEndpoint);
        }
    }

    private bool HasCompleteMessage(List<byte> buffer)
    {
        if (buffer.Count < 3) return false;

        // Look for StartByte at beginning, EndByte + CarriageReturn at end
        return buffer[0] == StartByte &&
               buffer.Contains(EndByte) &&
               buffer.LastIndexOf(EndByte) < buffer.Count - 1 &&
               buffer[buffer.LastIndexOf(EndByte) + 1] == CarriageReturn;
    }

    private string ExtractMessage(List<byte> buffer)
    {
        var startIndex = buffer.IndexOf(StartByte);
        var endIndex = buffer.LastIndexOf(EndByte);

        if (startIndex == -1 || endIndex == -1 || startIndex >= endIndex)
        {
            throw new InvalidOperationException("Invalid MLLP message framing");
        }

        // Extract message content (between StartByte and EndByte)
        var messageBytes = buffer.GetRange(startIndex + 1, endIndex - startIndex - 1);
        return Encoding.UTF8.GetString(messageBytes.ToArray());
    }

    private async Task<string> ProcessMessageAsync(string hl7Message)
    {
        var stopwatch = Stopwatch.StartNew();
        string? messageType = null;
        string ackCode = "AE"; // Default to error

        try
        {
            // Validate message
            if (!_parser.IsValid(hl7Message))
            {
                _logger.LogWarning("Received invalid HL7 message");
                LabBridgeMetrics.AcksSent.WithLabels("AR").Inc(); // AR = Reject
                return _ackGenerator.GenerateErrorAck(hl7Message, "Invalid HL7 message structure");
            }

            // Parse message to validate it
            var parsedMessage = _parser.Parse(hl7Message);
            messageType = _parser.GetMessageType(hl7Message);
            var messageControlId = _parser.GetMessageControlId(hl7Message);

            stopwatch.Stop();

            // Track metrics
            LabBridgeMetrics.MessagesReceived.WithLabels(messageType ?? "UNKNOWN").Inc();
            LabBridgeMetrics.Hl7ParsingDuration.WithLabels(messageType ?? "UNKNOWN").Observe(stopwatch.Elapsed.TotalSeconds);

            _logger.LogInformation("Parsed HL7 message: Type={MessageType}, ControlId={ControlId}",
                messageType, messageControlId);

            // Publish to RabbitMQ for async processing
            await _messageQueue.PublishAsync(hl7Message, messageControlId);

            // Generate ACK (success) - send immediately to analyzer
            var ackMessage = _ackGenerator.GenerateAcceptAck(hl7Message);
            ackCode = "AA"; // AA = Application Accept
            LabBridgeMetrics.AcksSent.WithLabels(ackCode).Inc();

            return ackMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing HL7 message");
            ackCode = "AE"; // AE = Application Error
            LabBridgeMetrics.AcksSent.WithLabels(ackCode).Inc();
            return _ackGenerator.GenerateErrorAck(hl7Message, $"Processing error: {ex.Message}");
        }
    }

    private byte[] WrapWithMllpFraming(string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var framedMessage = new byte[messageBytes.Length + 3];

        framedMessage[0] = StartByte;
        Array.Copy(messageBytes, 0, framedMessage, 1, messageBytes.Length);
        framedMessage[framedMessage.Length - 2] = EndByte;
        framedMessage[framedMessage.Length - 1] = CarriageReturn;

        return framedMessage;
    }
}
