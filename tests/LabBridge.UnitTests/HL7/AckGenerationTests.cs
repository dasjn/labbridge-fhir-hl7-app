using FluentAssertions;
using LabBridge.Infrastructure.HL7;
using Xunit;

namespace LabBridge.UnitTests.HL7;

public class AckGenerationTests
{
    private readonly AckGenerator _ackGenerator;
    private readonly NHapiParser _parser;

    public AckGenerationTests()
    {
        _ackGenerator = new AckGenerator();
        _parser = new NHapiParser();
    }

    #region GenerateAcceptAck Tests

    [Fact]
    public void GenerateAcceptAck_ValidMessage_ReturnsAckWithAA()
    {
        // Arrange
        var originalMessage = CreateValidOruR01Message();

        // Act
        var ack = _ackGenerator.GenerateAcceptAck(originalMessage);

        // Assert
        ack.Should().NotBeNullOrEmpty();
        ack.Should().Contain("MSA|AA|");
    }

    [Fact]
    public void GenerateAcceptAck_ValidMessage_PreservesMessageControlId()
    {
        // Arrange
        var originalMessage = CreateValidOruR01Message();
        var originalControlId = _parser.GetMessageControlId(originalMessage);

        // Act
        var ack = _ackGenerator.GenerateAcceptAck(originalMessage);

        // Assert
        ack.Should().Contain($"MSA|AA|{originalControlId}");
    }

    [Fact]
    public void GenerateAcceptAck_ValidMessage_CreatesValidHL7Message()
    {
        // Arrange
        var originalMessage = CreateValidOruR01Message();

        // Act
        var ack = _ackGenerator.GenerateAcceptAck(originalMessage);

        // Assert
        ack.Should().StartWith("MSH|");
        _parser.IsValid(ack).Should().BeTrue();
    }

    #endregion

    #region GenerateErrorAck Tests

    [Fact]
    public void GenerateErrorAck_WithErrorMessage_ReturnsAckWithAE()
    {
        // Arrange
        var originalMessage = CreateValidOruR01Message();
        var errorMessage = "Validation failed: Missing required field";

        // Act
        var ack = _ackGenerator.GenerateErrorAck(originalMessage, errorMessage);

        // Assert
        ack.Should().Contain("MSA|AE|");
        ack.Should().Contain(errorMessage);
    }

    [Fact]
    public void GenerateErrorAck_WithErrorMessage_PreservesMessageControlId()
    {
        // Arrange
        var originalMessage = CreateValidOruR01Message();
        var originalControlId = _parser.GetMessageControlId(originalMessage);
        var errorMessage = "Error occurred";

        // Act
        var ack = _ackGenerator.GenerateErrorAck(originalMessage, errorMessage);

        // Assert
        ack.Should().Contain($"MSA|AE|{originalControlId}");
    }

    #endregion

    #region GenerateRejectAck Tests

    [Fact]
    public void GenerateRejectAck_WithRejectReason_ReturnsAckWithAR()
    {
        // Arrange
        var originalMessage = CreateValidOruR01Message();
        var rejectReason = "Message type not supported";

        // Act
        var ack = _ackGenerator.GenerateRejectAck(originalMessage, rejectReason);

        // Assert
        ack.Should().Contain("MSA|AR|");
        ack.Should().Contain(rejectReason);
    }

    [Fact]
    public void GenerateRejectAck_WithRejectReason_PreservesMessageControlId()
    {
        // Arrange
        var originalMessage = CreateValidOruR01Message();
        var originalControlId = _parser.GetMessageControlId(originalMessage);
        var rejectReason = "Rejected";

        // Act
        var ack = _ackGenerator.GenerateRejectAck(originalMessage, rejectReason);

        // Assert
        ack.Should().Contain($"MSA|AR|{originalControlId}");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GenerateAcceptAck_MalformedMessage_GeneratesFallbackAck()
    {
        // Arrange
        var malformedMessage = "INVALID|HL7|MESSAGE";

        // Act
        var ack = _ackGenerator.GenerateAcceptAck(malformedMessage);

        // Assert
        ack.Should().NotBeNullOrEmpty();
        ack.Should().Contain("MSH|");
        ack.Should().Contain("MSA|AA|");
    }

    #endregion

    #region Helper Methods

    private static string CreateValidOruR01Message()
    {
        return "MSH|^~\\&|PANTHER|LAB|LABFLOW|HOSPITAL|20251016120000||ORU^R01|MSG123456|P|2.5\r" +
               "PID|1||12345678^^^MRN||García^Juan^Carlos||19850315|M\r" +
               "OBR|1|ORD123|LAB456|58410-2^CBC panel^LN|||20251016115500||||||||||||||||F\r" +
               "OBX|1|NM|718-7^Hemoglobin^LN||14.5|g/dL|13.5-17.5|N|||F|||20251016120000";
    }

    #endregion
}
