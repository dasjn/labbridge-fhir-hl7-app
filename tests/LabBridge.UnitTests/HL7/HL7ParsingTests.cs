using FluentAssertions;
using LabBridge.Infrastructure.HL7;
using NHapi.Model.V25.Message;
using Xunit;

namespace LabBridge.UnitTests.HL7;

public class HL7ParsingTests
{
    private readonly NHapiParser _parser;

    public HL7ParsingTests()
    {
        _parser = new NHapiParser();
    }

    #region Parse() - Success Cases

    [Fact]
    public void Parse_ValidOruR01Message_ParsesSuccessfully()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();

        // Act
        var result = _parser.Parse(hl7Message);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<ORU_R01>();
    }

    [Fact]
    public void Parse_ValidOruR01Message_ExtractsPidFields()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();

        // Act
        var message = (ORU_R01)_parser.Parse(hl7Message);
        var pid = message.GetPATIENT_RESULT(0).PATIENT.PID;

        // Assert
        pid.GetPatientIdentifierList(0).IDNumber.Value.Should().Be("12345678");
        pid.GetPatientName(0).FamilyName.Surname.Value.Should().Be("García");
        pid.GetPatientName(0).GivenName.Value.Should().Be("Juan");
        pid.DateTimeOfBirth.Time.Value.Should().Be("19850315");
        pid.AdministrativeSex.Value.Should().Be("M");
    }

    [Fact]
    public void Parse_MessageWithMultipleObxSegments_ParsesAllSegments()
    {
        // Arrange
        var hl7Message = CreateOruR01WithMultipleObx();

        // Act
        var message = (ORU_R01)_parser.Parse(hl7Message);
        var obxCount = message.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).OBSERVATIONRepetitionsUsed;

        // Assert
        obxCount.Should().Be(3); // Hemoglobin, WBC, Platelets
    }

    [Fact]
    public void Parse_MessageWithSpecialCharacters_HandlesEncoding()
    {
        // Arrange
        var hl7Message = CreateMessageWithSpecialCharacters();

        // Act
        var message = (ORU_R01)_parser.Parse(hl7Message);
        var patientName = message.GetPATIENT_RESULT(0).PATIENT.PID.GetPatientName(0).FamilyName.Surname.Value;

        // Assert
        patientName.Should().Be("Muñoz"); // Contains ñ
    }

    [Fact]
    public void Parse_ValidMessage_ReturnsORUR01Type()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();

        // Act
        var result = _parser.Parse(hl7Message);

        // Assert
        result.Should().BeAssignableTo<ORU_R01>();
    }

    #endregion

    #region Parse() - Error Cases

    [Fact]
    public void Parse_NullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        string nullMessage = null!;

        // Act
        Action act = () => _parser.Parse(nullMessage);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("hl7Message");
    }

    [Fact]
    public void Parse_EmptyMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var emptyMessage = string.Empty;

        // Act
        Action act = () => _parser.Parse(emptyMessage);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_MalformedMessage_ThrowsHL7Exception()
    {
        // Arrange
        var malformedMessage = "INVALID|HL7|MESSAGE";

        // Act
        Action act = () => _parser.Parse(malformedMessage);

        // Assert
        act.Should().Throw<NHapi.Base.HL7Exception>();
    }

    #endregion

    #region IsValid() Tests

    [Fact]
    public void IsValid_ValidMessage_ReturnsTrue()
    {
        // Arrange
        var validMessage = CreateValidOruR01Message();

        // Act
        var result = _parser.IsValid(validMessage);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValid_NullOrEmptyMessage_ReturnsFalse()
    {
        // Act & Assert
        _parser.IsValid(null!).Should().BeFalse();
        _parser.IsValid(string.Empty).Should().BeFalse();
        _parser.IsValid("   ").Should().BeFalse();
    }

    [Fact]
    public void IsValid_MalformedMessage_ReturnsFalse()
    {
        // Arrange
        var malformedMessage = "INVALID|HL7|MESSAGE";

        // Act
        var result = _parser.IsValid(malformedMessage);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetMessageType() Tests

    [Fact]
    public void GetMessageType_OruR01Message_ReturnsCorrectType()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();

        // Act
        var messageType = _parser.GetMessageType(hl7Message);

        // Assert
        messageType.Should().Be("ORU^R01");
    }

    [Fact]
    public void GetMessageType_InvalidMessage_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidMessage = "INVALID|MESSAGE";

        // Act
        Action act = () => _parser.GetMessageType(invalidMessage);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region GetMessageControlId() Tests

    [Fact]
    public void GetMessageControlId_ValidMessage_ExtractsControlId()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();

        // Act
        var controlId = _parser.GetMessageControlId(hl7Message);

        // Assert
        controlId.Should().Be("MSG123456");
    }

    [Fact]
    public void GetMessageControlId_InvalidMessage_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidMessage = "INVALID|MESSAGE";

        // Act
        Action act = () => _parser.GetMessageControlId(invalidMessage);

        // Assert
        act.Should().Throw<InvalidOperationException>();
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

    private static string CreateOruR01WithMultipleObx()
    {
        return "MSH|^~\\&|PANTHER|LAB|LABFLOW|HOSPITAL|20251016120000||ORU^R01|MSG123456|P|2.5\r" +
               "PID|1||12345678^^^MRN||García^Juan^Carlos||19850315|M\r" +
               "OBR|1|ORD123|LAB456|58410-2^CBC panel^LN|||20251016115500||||||||||||||||F\r" +
               "OBX|1|NM|718-7^Hemoglobin^LN||14.5|g/dL|13.5-17.5|N|||F|||20251016120000\r" +
               "OBX|2|NM|6690-2^WBC^LN||7500|cells/uL|4500-11000|N|||F|||20251016120000\r" +
               "OBX|3|NM|777-3^Platelets^LN||250000|cells/uL|150000-400000|N|||F|||20251016120000";
    }

    private static string CreateMessageWithSpecialCharacters()
    {
        return "MSH|^~\\&|PANTHER|LAB|LABFLOW|HOSPITAL|20251016120000||ORU^R01|MSG123456|P|2.5\r" +
               "PID|1||12345678^^^MRN||Muñoz^María^José||19900520|F\r" +
               "OBR|1|ORD123|LAB456|58410-2^CBC panel^LN|||20251016115500||||||||||||||||F\r" +
               "OBX|1|NM|718-7^Hemoglobin^LN||12.5|g/dL|12.0-16.0|N|||F|||20251016120000";
    }

    #endregion
}
