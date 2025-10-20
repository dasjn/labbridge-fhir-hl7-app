using FluentAssertions;
using Hl7.Fhir.Model;
using LabBridge.Infrastructure.FHIR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LabBridge.UnitTests.FHIR;

public class LabFlowClientTests
{
    private readonly Mock<ILabFlowApi> _mockApi;
    private readonly Mock<ILogger<LabFlowClient>> _mockLogger;
    private readonly LabFlowClient _client;

    public LabFlowClientTests()
    {
        _mockApi = new Mock<ILabFlowApi>();
        _mockLogger = new Mock<ILogger<LabFlowClient>>();
        _client = new LabFlowClient(_mockApi.Object, _mockLogger.Object);
    }

    #region CreateOrUpdatePatientAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task CreateOrUpdatePatientAsync_ValidPatient_CallsApiAndReturnsResult()
    {
        // Arrange
        var patient = CreateTestPatient();
        var expectedPatient = CreateTestPatient();
        expectedPatient.Id = "patient-123";

        _mockApi.Setup(x => x.CreatePatientAsync(patient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPatient);

        // Act
        var result = await _client.CreateOrUpdatePatientAsync(patient);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("patient-123");
        _mockApi.Verify(x => x.CreatePatientAsync(patient, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateOrUpdatePatientAsync_ValidPatient_LogsInformationMessages()
    {
        // Arrange
        var patient = CreateTestPatient();
        var expectedPatient = CreateTestPatient();
        expectedPatient.Id = "patient-123";

        _mockApi.Setup(x => x.CreatePatientAsync(patient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPatient);

        // Act
        await _client.CreateOrUpdatePatientAsync(patient);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Creating/updating Patient")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("created/updated successfully")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateOrUpdatePatientAsync_ApiThrowsException_LogsErrorAndRethrows()
    {
        // Arrange
        var patient = CreateTestPatient();
        var exception = new Exception("API error");

        _mockApi.Setup(x => x.CreatePatientAsync(patient, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        Func<System.Threading.Tasks.Task> act = async () => await _client.CreateOrUpdatePatientAsync(patient);

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("API error");

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed to create/update Patient")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateOrUpdatePatientAsync_PatientWithoutIdentifier_CallsApi()
    {
        // Arrange
        var patient = new Patient
        {
            Name = new List<HumanName> { new HumanName { Family = "Doe", Given = new[] { "John" } } }
        };
        var expectedPatient = new Patient { Id = "patient-456" };

        _mockApi.Setup(x => x.CreatePatientAsync(patient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPatient);

        // Act
        var result = await _client.CreateOrUpdatePatientAsync(patient);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("patient-456");
    }

    #endregion

    #region CreateObservationAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task CreateObservationAsync_ValidObservation_CallsApiAndReturnsResult()
    {
        // Arrange
        var observation = CreateTestObservation();
        var expectedObservation = CreateTestObservation();
        expectedObservation.Id = "observation-123";

        _mockApi.Setup(x => x.CreateObservationAsync(observation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedObservation);

        // Act
        var result = await _client.CreateObservationAsync(observation);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("observation-123");
        _mockApi.Verify(x => x.CreateObservationAsync(observation, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateObservationAsync_ValidObservation_LogsInformationMessages()
    {
        // Arrange
        var observation = CreateTestObservation();
        var expectedObservation = CreateTestObservation();
        expectedObservation.Id = "observation-123";

        _mockApi.Setup(x => x.CreateObservationAsync(observation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedObservation);

        // Act
        await _client.CreateObservationAsync(observation);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Creating Observation")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Observation created successfully")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateObservationAsync_ApiThrowsException_LogsErrorAndRethrows()
    {
        // Arrange
        var observation = CreateTestObservation();
        var exception = new Exception("API error");

        _mockApi.Setup(x => x.CreateObservationAsync(observation, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        Func<System.Threading.Tasks.Task> act = async () => await _client.CreateObservationAsync(observation);

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("API error");

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed to create Observation")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region CreateDiagnosticReportAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task CreateDiagnosticReportAsync_ValidReport_CallsApiAndReturnsResult()
    {
        // Arrange
        var report = CreateTestDiagnosticReport();
        var expectedReport = CreateTestDiagnosticReport();
        expectedReport.Id = "report-123";

        _mockApi.Setup(x => x.CreateDiagnosticReportAsync(report, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedReport);

        // Act
        var result = await _client.CreateDiagnosticReportAsync(report);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("report-123");
        _mockApi.Verify(x => x.CreateDiagnosticReportAsync(report, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateDiagnosticReportAsync_ValidReport_LogsInformationMessages()
    {
        // Arrange
        var report = CreateTestDiagnosticReport();
        var expectedReport = CreateTestDiagnosticReport();
        expectedReport.Id = "report-123";

        _mockApi.Setup(x => x.CreateDiagnosticReportAsync(report, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedReport);

        // Act
        await _client.CreateDiagnosticReportAsync(report);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Creating DiagnosticReport")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("DiagnosticReport created successfully")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateDiagnosticReportAsync_ApiThrowsException_LogsErrorAndRethrows()
    {
        // Arrange
        var report = CreateTestDiagnosticReport();
        var exception = new Exception("API error");

        _mockApi.Setup(x => x.CreateDiagnosticReportAsync(report, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        Func<System.Threading.Tasks.Task> act = async () => await _client.CreateDiagnosticReportAsync(report);

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("API error");

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed to create DiagnosticReport")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private static Patient CreateTestPatient()
    {
        return new Patient
        {
            Identifier = new List<Identifier>
            {
                new Identifier
                {
                    System = "urn:oid:2.16.840.1.113883.4.1",
                    Value = "12345678"
                }
            },
            Name = new List<HumanName>
            {
                new HumanName
                {
                    Family = "García",
                    Given = new[] { "Juan", "Carlos" }
                }
            },
            Gender = AdministrativeGender.Male,
            BirthDate = "1985-03-15"
        };
    }

    private static Observation CreateTestObservation()
    {
        return new Observation
        {
            Status = ObservationStatus.Final,
            Code = new CodeableConcept
            {
                Coding = new List<Coding>
                {
                    new Coding
                    {
                        System = "http://loinc.org",
                        Code = "718-7",
                        Display = "Hemoglobin"
                    }
                }
            },
            Subject = new ResourceReference("Patient/12345678"),
            Value = new Quantity
            {
                Value = 14.5m,
                Unit = "g/dL",
                System = "http://unitsofmeasure.org",
                Code = "g/dL"
            }
        };
    }

    private static DiagnosticReport CreateTestDiagnosticReport()
    {
        return new DiagnosticReport
        {
            Status = DiagnosticReport.DiagnosticReportStatus.Final,
            Code = new CodeableConcept
            {
                Coding = new List<Coding>
                {
                    new Coding
                    {
                        System = "http://loinc.org",
                        Code = "58410-2",
                        Display = "CBC panel"
                    }
                }
            },
            Subject = new ResourceReference("Patient/12345678"),
            Result = new List<ResourceReference>
            {
                new ResourceReference("Observation/1"),
                new ResourceReference("Observation/2")
            }
        };
    }

    #endregion
}
