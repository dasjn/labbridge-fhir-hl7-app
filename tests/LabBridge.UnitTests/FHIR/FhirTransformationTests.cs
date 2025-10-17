using FluentAssertions;
using Hl7.Fhir.Model;
using LabBridge.Infrastructure.FHIR;
using LabBridge.Infrastructure.HL7;
using NHapi.Model.V25.Message;
using Xunit;

namespace LabBridge.UnitTests.FHIR;

public class FhirTransformationTests
{
    private readonly FhirTransformer _transformer;
    private readonly NHapiParser _parser;

    public FhirTransformationTests()
    {
        _transformer = new FhirTransformer();
        _parser = new NHapiParser();
    }

    #region Transform() - Main Method Tests

    [Fact]
    public void Transform_ValidOruR01Message_SuccessfullyTransforms()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = _parser.Parse(hl7Message);

        // Act
        var result = _transformer.Transform(parsedMessage);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.MessageControlId.Should().Be("MSG123456");
        result.MessageType.Should().Be("ORU^R01");
    }

    [Fact]
    public void Transform_ValidMessage_CreatesPatientAndObservationsAndReport()
    {
        // Arrange
        var hl7Message = CreateOruR01WithMultipleObx();
        var parsedMessage = _parser.Parse(hl7Message);

        // Act
        var result = _transformer.Transform(parsedMessage);

        // Assert
        result.Patient.Should().NotBeNull();
        result.Observations.Should().HaveCount(3); // Hemoglobin, WBC, Platelets
        result.DiagnosticReport.Should().NotBeNull();
    }

    [Fact]
    public void Transform_NullMessage_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => _transformer.Transform(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Transform_NonOruR01Message_ReturnsFailureResult()
    {
        // Arrange
        var invalidMessage = "not an ORU message";

        // Act
        var result = _transformer.Transform(invalidMessage);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Transform_MalformedMessage_HandlesErrorGracefully()
    {
        // Arrange
        var invalidObject = new object();

        // Act
        var result = _transformer.Transform(invalidObject);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported message type");
    }

    #endregion

    #region TransformPatient() Tests

    [Fact]
    public void TransformPatient_ValidPid_CreatesPatientWithAllFields()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var pid = parsedMessage.GetPATIENT_RESULT(0).PATIENT.PID;

        // Act
        var patient = (Patient)_transformer.TransformPatient(pid);

        // Assert
        patient.Should().NotBeNull();
        patient.Identifier.Should().HaveCount(1);
        patient.Name.Should().HaveCount(1);
        patient.Gender.Should().NotBeNull();
        patient.BirthDate.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TransformPatient_ValidPid_MapsPatientIdCorrectly()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var pid = parsedMessage.GetPATIENT_RESULT(0).PATIENT.PID;

        // Act
        var patient = (Patient)_transformer.TransformPatient(pid);

        // Assert
        patient.Identifier[0].Value.Should().Be("12345678");
        patient.Identifier[0].System.Should().Be("urn:oid:2.16.840.1.113883.4.1");
    }

    [Fact]
    public void TransformPatient_ValidPid_MapsNameCorrectly()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var pid = parsedMessage.GetPATIENT_RESULT(0).PATIENT.PID;

        // Act
        var patient = (Patient)_transformer.TransformPatient(pid);

        // Assert
        patient.Name[0].Family.Should().Be("García");
        patient.Name[0].Given.Should().Contain("Juan");
        patient.Name[0].Given.Should().Contain("Carlos");
    }

    [Fact]
    public void TransformPatient_MaleGender_MapsMToMale()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var pid = parsedMessage.GetPATIENT_RESULT(0).PATIENT.PID;

        // Act
        var patient = (Patient)_transformer.TransformPatient(pid);

        // Assert
        patient.Gender.Should().Be(AdministrativeGender.Male);
    }

    [Fact]
    public void TransformPatient_FemaleGender_MapsFToFemale()
    {
        // Arrange
        var hl7Message = CreateMessageWithFemalePatient();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var pid = parsedMessage.GetPATIENT_RESULT(0).PATIENT.PID;

        // Act
        var patient = (Patient)_transformer.TransformPatient(pid);

        // Assert
        patient.Gender.Should().Be(AdministrativeGender.Female);
    }

    [Fact]
    public void TransformPatient_ValidBirthDate_ParsesCorrectly()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var pid = parsedMessage.GetPATIENT_RESULT(0).PATIENT.PID;

        // Act
        var patient = (Patient)_transformer.TransformPatient(pid);

        // Assert
        patient.BirthDate.Should().Be("1985-03-15");
    }

    [Fact]
    public void TransformPatient_WithMiddleName_MapsAllGivenNames()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var pid = parsedMessage.GetPATIENT_RESULT(0).PATIENT.PID;

        // Act
        var patient = (Patient)_transformer.TransformPatient(pid);

        // Assert
        patient.Name[0].Given.Should().HaveCount(2); // Juan, Carlos
    }

    [Fact]
    public void TransformPatient_InvalidSegment_ThrowsArgumentException()
    {
        // Arrange
        var invalidSegment = "not a PID segment";

        // Act
        Action act = () => _transformer.TransformPatient(invalidSegment);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region TransformObservation() Tests

    [Fact]
    public void TransformObservation_NumericValue_CreatesObservationWithValueQuantity()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obx = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).GetOBSERVATION(0).OBX;

        // Act
        var observation = (Observation)_transformer.TransformObservation(obx, "12345678");

        // Assert
        observation.Should().NotBeNull();
        observation.Value.Should().BeOfType<Quantity>();
        var quantity = (Quantity)observation.Value;
        quantity.Value.Should().Be(14.5m);
        quantity.Unit.Should().Be("g/dL");
    }

    [Fact]
    public void TransformObservation_ValidObx_MapsLoincCodeCorrectly()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obx = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).GetOBSERVATION(0).OBX;

        // Act
        var observation = (Observation)_transformer.TransformObservation(obx, "12345678");

        // Assert
        observation.Code.Coding[0].System.Should().Be("http://loinc.org");
        observation.Code.Coding[0].Code.Should().Be("718-7");
        observation.Code.Coding[0].Display.Should().Be("Hemoglobin");
    }

    [Fact]
    public void TransformObservation_ValidObx_MapsReferenceRangeCorrectly()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obx = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).GetOBSERVATION(0).OBX;

        // Act
        var observation = (Observation)_transformer.TransformObservation(obx, "12345678");

        // Assert
        observation.ReferenceRange.Should().HaveCount(1);
        observation.ReferenceRange[0].Text.Should().Be("13.5-17.5");
    }

    [Fact]
    public void TransformObservation_NormalFlag_MapsInterpretationCorrectly()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obx = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).GetOBSERVATION(0).OBX;

        // Act
        var observation = (Observation)_transformer.TransformObservation(obx, "12345678");

        // Assert
        observation.Interpretation.Should().HaveCount(1);
        observation.Interpretation[0].Coding[0].Code.Should().Be("N");
    }

    [Fact]
    public void TransformObservation_FinalStatus_MapsFToFinal()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obx = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).GetOBSERVATION(0).OBX;

        // Act
        var observation = (Observation)_transformer.TransformObservation(obx, "12345678");

        // Assert
        observation.Status.Should().Be(ObservationStatus.Final);
    }

    [Fact]
    public void TransformObservation_ValidObx_MapsEffectiveDateTimeCorrectly()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obx = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).GetOBSERVATION(0).OBX;

        // Act
        var observation = (Observation)_transformer.TransformObservation(obx, "12345678");

        // Assert
        observation.Effective.Should().NotBeNull();
        observation.Effective.Should().BeOfType<FhirDateTime>();
    }

    [Fact]
    public void TransformObservation_ValidObx_SetsPatientReference()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obx = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).GetOBSERVATION(0).OBX;

        // Act
        var observation = (Observation)_transformer.TransformObservation(obx, "12345678");

        // Assert
        observation.Subject.Reference.Should().Be("Patient/12345678");
    }

    #endregion

    #region TransformDiagnosticReport() Tests

    [Fact]
    public void TransformDiagnosticReport_ValidObr_CreatesReportWithIdentifiers()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obr = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).OBR;
        var observationIds = new List<string> { "Observation/1" };

        // Act
        var report = (DiagnosticReport)_transformer.TransformDiagnosticReport(obr, "12345678", observationIds);

        // Assert
        report.Identifier.Should().HaveCount(2); // Placer + Filler
        report.Identifier[0].Value.Should().Be("ORD123");
        report.Identifier[1].Value.Should().Be("LAB456");
    }

    [Fact]
    public void TransformDiagnosticReport_ValidObr_MapsPanelCodeCorrectly()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obr = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).OBR;
        var observationIds = new List<string> { "Observation/1" };

        // Act
        var report = (DiagnosticReport)_transformer.TransformDiagnosticReport(obr, "12345678", observationIds);

        // Assert
        report.Code.Coding[0].System.Should().Be("http://loinc.org");
        report.Code.Coding[0].Code.Should().Be("58410-2");
        report.Code.Coding[0].Display.Should().Be("CBC panel");
    }

    [Fact]
    public void TransformDiagnosticReport_ValidObr_SetsPatientReference()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obr = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).OBR;
        var observationIds = new List<string> { "Observation/1" };

        // Act
        var report = (DiagnosticReport)_transformer.TransformDiagnosticReport(obr, "12345678", observationIds);

        // Assert
        report.Subject.Reference.Should().Be("Patient/12345678");
    }

    [Fact]
    public void TransformDiagnosticReport_ValidObr_IncludesObservationReferences()
    {
        // Arrange
        var hl7Message = CreateValidOruR01Message();
        var parsedMessage = (ORU_R01)_parser.Parse(hl7Message);
        var obr = parsedMessage.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0).OBR;
        var observationIds = new List<string> { "Observation/1", "Observation/2", "Observation/3" };

        // Act
        var report = (DiagnosticReport)_transformer.TransformDiagnosticReport(obr, "12345678", observationIds);

        // Assert
        report.Result.Should().HaveCount(3);
        report.Result[0].Reference.Should().Be("Observation/1");
        report.Result[1].Reference.Should().Be("Observation/2");
        report.Result[2].Reference.Should().Be("Observation/3");
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

    private static string CreateMessageWithFemalePatient()
    {
        return "MSH|^~\\&|PANTHER|LAB|LABFLOW|HOSPITAL|20251016120000||ORU^R01|MSG123456|P|2.5\r" +
               "PID|1||12345678^^^MRN||Muñoz^María^José||19900520|F\r" +
               "OBR|1|ORD123|LAB456|58410-2^CBC panel^LN|||20251016115500||||||||||||||||F\r" +
               "OBX|1|NM|718-7^Hemoglobin^LN||12.5|g/dL|12.0-16.0|N|||F|||20251016120000";
    }

    #endregion
}
