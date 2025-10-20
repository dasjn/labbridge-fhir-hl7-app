using System.Net.Sockets;
using System.Text;
using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.HL7;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace LabBridge.UnitTests.HL7;

public class MllpServerTests : IAsyncLifetime
{
    private readonly Mock<IHL7Parser> _mockParser;
    private readonly Mock<IAckGenerator> _mockAckGenerator;
    private readonly Mock<ILogger<MllpServer>> _mockLogger;
    private MllpServer _mllpServer;
    private CancellationTokenSource _cancellationTokenSource;
    private Task _serverTask = Task.CompletedTask;

    private const int TestPort = 2576; // Different port to avoid conflicts
    private const byte StartByte = 0x0B;
    private const byte EndByte = 0x1C;
    private const byte CR = 0x0D;

    public MllpServerTests()
    {
        _mockParser = new Mock<IHL7Parser>();
        _mockAckGenerator = new Mock<IAckGenerator>();
        _mockLogger = new Mock<ILogger<MllpServer>>();

        _mllpServer = new MllpServer(_mockParser.Object, _mockAckGenerator.Object, _mockLogger.Object);
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public async Task InitializeAsync()
    {
        // Start server in background
        _serverTask = Task.Run(async () =>
        {
            try
            {
                await _mllpServer.StartAsync(TestPort, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when tests complete
            }
        });

        // Wait a bit for server to start
        await Task.Delay(500);
    }

    public async Task DisposeAsync()
    {
        _cancellationTokenSource.Cancel();
        await _mllpServer.StopAsync();

        try
        {
            await _serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore timeout
        }

        _cancellationTokenSource.Dispose();
    }

    [Fact]
    public async Task SendValidHl7Message_ReceivesAckResponse()
    {
        // Arrange
        var hl7Message = CreateValidOruMessage();
        var expectedAck = "MSH|^~\\&|LABBRIDGE|HOSPITAL|PANTHER|LAB|20251020120000||ACK|MSG123|P|2.5\rMSA|AA|MSG123";

        _mockParser.Setup(p => p.IsValid(It.IsAny<string>())).Returns(true);
        _mockParser.Setup(p => p.Parse(It.IsAny<string>())).Returns(new object());
        _mockParser.Setup(p => p.GetMessageType(It.IsAny<string>())).Returns("ORU^R01");
        _mockParser.Setup(p => p.GetMessageControlId(It.IsAny<string>())).Returns("MSG123");
        _mockAckGenerator.Setup(a => a.GenerateAcceptAck(It.IsAny<string>())).Returns(expectedAck);

        // Act
        var response = await SendMllpMessageAsync(hl7Message);

        // Assert
        response.Should().NotBeNull();
        response.Should().Contain("MSA|AA|MSG123");
        _mockParser.Verify(p => p.IsValid(It.IsAny<string>()), Times.Once);
        _mockParser.Verify(p => p.Parse(It.IsAny<string>()), Times.Once);
        _mockAckGenerator.Verify(a => a.GenerateAcceptAck(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SendMultipleConcurrentMessages_AllReceiveAck()
    {
        // Arrange
        var messageCount = 10;
        var hl7Message = CreateValidOruMessage();
        var expectedAck = "MSH|^~\\&|LABBRIDGE|HOSPITAL|PANTHER|LAB|20251020120000||ACK|MSG123|P|2.5\rMSA|AA|MSG123";

        _mockParser.Setup(p => p.IsValid(It.IsAny<string>())).Returns(true);
        _mockParser.Setup(p => p.Parse(It.IsAny<string>())).Returns(new object());
        _mockParser.Setup(p => p.GetMessageType(It.IsAny<string>())).Returns("ORU^R01");
        _mockParser.Setup(p => p.GetMessageControlId(It.IsAny<string>())).Returns("MSG123");
        _mockAckGenerator.Setup(a => a.GenerateAcceptAck(It.IsAny<string>())).Returns(expectedAck);

        // Act
        var tasks = Enumerable.Range(0, messageCount)
            .Select(_ => SendMllpMessageAsync(hl7Message))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().HaveCount(messageCount);
        responses.Should().AllSatisfy(r => r.Should().Contain("MSA|AA|MSG123"));
        _mockParser.Verify(p => p.Parse(It.IsAny<string>()), Times.Exactly(messageCount));
    }

    [Fact]
    public async Task SendInvalidHl7Message_ReceivesErrorAck()
    {
        // Arrange
        var invalidMessage = "INVALID_MESSAGE_NO_MSH";
        var expectedErrorAck = "MSH|^~\\&|LABBRIDGE|HOSPITAL|SENDER|FACILITY|20251020120000||ACK|UNKNOWN|P|2.5\rMSA|AE|UNKNOWN|Invalid HL7 message structure";

        _mockParser.Setup(p => p.IsValid(It.IsAny<string>())).Returns(false);
        _mockAckGenerator.Setup(a => a.GenerateErrorAck(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(expectedErrorAck);

        // Act
        var response = await SendMllpMessageAsync(invalidMessage);

        // Assert
        response.Should().NotBeNull();
        response.Should().Contain("MSA|AE");
        _mockParser.Verify(p => p.IsValid(It.IsAny<string>()), Times.Once);
        _mockParser.Verify(p => p.Parse(It.IsAny<string>()), Times.Never);
        _mockAckGenerator.Verify(a => a.GenerateErrorAck(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageWithoutMllpFraming_ServerHandlesGracefully()
    {
        // Arrange
        var hl7MessageWithoutFraming = CreateValidOruMessage();
        var client = new TcpClient();

        try
        {
            // Act
            await client.ConnectAsync("localhost", TestPort);
            var stream = client.GetStream();

            // Send without MLLP framing (no Start/End bytes)
            var messageBytes = Encoding.UTF8.GetBytes(hl7MessageWithoutFraming);
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            await stream.FlushAsync();

            // Wait a bit
            await Task.Delay(1000);

            // Try to read response (should timeout because server won't respond to incomplete message)
            var buffer = new byte[1024];
            var bytesRead = 0;

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));
            try
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout is expected - server correctly ignores malformed message
                bytesRead = 0;
            }

            // Assert - Server should not respond (and not crash)
            bytesRead.Should().Be(0);
        }
        finally
        {
            client.Close();
        }
    }

    [Fact]
    public async Task SendMalformedMllpMessage_MissingEndByte_NoResponse()
    {
        // Arrange
        var hl7Message = CreateValidOruMessage();
        var client = new TcpClient();

        try
        {
            // Act
            await client.ConnectAsync("localhost", TestPort);
            var stream = client.GetStream();

            // Send with Start Byte but missing End Byte
            var messageBytes = Encoding.UTF8.GetBytes(hl7Message);
            var malformedMessage = new byte[messageBytes.Length + 1];
            malformedMessage[0] = StartByte;
            Array.Copy(messageBytes, 0, malformedMessage, 1, messageBytes.Length);
            // Missing EndByte and CR

            await stream.WriteAsync(malformedMessage, 0, malformedMessage.Length);
            await stream.FlushAsync();

            // Wait a bit
            await Task.Delay(1000);

            // Try to read response (should timeout because server won't respond to incomplete message)
            var buffer = new byte[1024];
            var bytesRead = 0;

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));
            try
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout is expected - server correctly waits for complete MLLP frame
                bytesRead = 0;
            }

            // Assert - No response because message is incomplete
            bytesRead.Should().Be(0);
        }
        finally
        {
            client.Close();
        }
    }

    [Fact]
    public async Task ParserThrowsException_ReceivesErrorAck()
    {
        // Arrange
        var hl7Message = CreateValidOruMessage();
        var expectedErrorAck = "MSH|^~\\&|LABBRIDGE|HOSPITAL|SENDER|FACILITY|20251020120000||ACK|UNKNOWN|P|2.5\rMSA|AE|UNKNOWN|Processing error: Parse failed";

        _mockParser.Setup(p => p.IsValid(It.IsAny<string>())).Returns(true);
        _mockParser.Setup(p => p.Parse(It.IsAny<string>())).Throws(new Exception("Parse failed"));
        _mockAckGenerator.Setup(a => a.GenerateErrorAck(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(expectedErrorAck);

        // Act
        var response = await SendMllpMessageAsync(hl7Message);

        // Assert
        response.Should().NotBeNull();
        response.Should().Contain("MSA|AE");
        response.Should().Contain("Parse failed");
        _mockAckGenerator.Verify(a => a.GenerateErrorAck(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    private async Task<string> SendMllpMessageAsync(string hl7Message)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("localhost", TestPort);

        using var stream = client.GetStream();

        // Send MLLP-framed message
        var framedMessage = WrapWithMllpFraming(hl7Message);
        await stream.WriteAsync(framedMessage, 0, framedMessage.Length);
        await stream.FlushAsync();

        // Read response
        var buffer = new byte[8192];
        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

        // Extract response (remove MLLP framing)
        if (bytesRead > 3)
        {
            var responseBytes = buffer[1..(bytesRead - 2)];
            return Encoding.UTF8.GetString(responseBytes);
        }

        return string.Empty;
    }

    private byte[] WrapWithMllpFraming(string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var framedMessage = new byte[messageBytes.Length + 3];

        framedMessage[0] = StartByte;
        Array.Copy(messageBytes, 0, framedMessage, 1, messageBytes.Length);
        framedMessage[framedMessage.Length - 2] = EndByte;
        framedMessage[framedMessage.Length - 1] = CR;

        return framedMessage;
    }

    private string CreateValidOruMessage()
    {
        return "MSH|^~\\&|PANTHER|LAB|LABFLOW|HOSPITAL|20251020120000||ORU^R01|MSG123|P|2.5\r" +
               "PID|1||12345678^^^MRN||García^Juan^Carlos||19850315|M\r" +
               "OBR|1|ORD123|LAB456|58410-2^CBC panel^LN|||20251020115500||||||||||||||||F\r" +
               "OBX|1|NM|718-7^Hemoglobin^LN||14.5|g/dL|13.5-17.5|N|||F|||20251020120000";
    }
}
