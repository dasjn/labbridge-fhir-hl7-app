using Hl7.Fhir.Model;
using LabBridge.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LabBridge.Infrastructure.FHIR;

/// <summary>
/// Implementation of IFhirClient that wraps the Refit ILabFlowApi
/// Provides additional logging and error handling
/// Serialization/deserialization is handled automatically by Refit + FhirHttpContentSerializer
/// </summary>
public class LabFlowClient : IFhirClient
{
    private readonly ILabFlowApi _api;
    private readonly ILogger<LabFlowClient> _logger;

    public LabFlowClient(ILabFlowApi api, ILogger<LabFlowClient> logger)
    {
        _api = api;
        _logger = logger;
    }

    public async Task<Patient> CreateOrUpdatePatientAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        var mrn = patient.Identifier.FirstOrDefault()?.Value;
        _logger.LogInformation("Creating/updating Patient in FHIR API: MRN={Mrn}", mrn);

        try
        {
            // Refit + FhirHttpContentSerializer handle serialization/deserialization automatically
            var result = await _api.CreatePatientAsync(patient, cancellationToken);

            _logger.LogInformation("Patient created/updated successfully: MRN={Mrn}, FhirId={FhirId}",
                mrn, result.Id);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create/update Patient: MRN={Mrn}", mrn);
            throw;
        }
    }

    public async Task<Observation> CreateObservationAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        var code = observation.Code?.Coding?.FirstOrDefault()?.Code;
        _logger.LogInformation("Creating Observation in FHIR API: Code={Code}", code);

        try
        {
            // Refit + FhirHttpContentSerializer handle serialization/deserialization automatically
            var result = await _api.CreateObservationAsync(observation, cancellationToken);

            _logger.LogInformation("Observation created successfully: Code={Code}, FhirId={FhirId}",
                code, result.Id);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Observation: Code={Code}", code);
            throw;
        }
    }

    public async Task<DiagnosticReport> CreateDiagnosticReportAsync(DiagnosticReport report, CancellationToken cancellationToken = default)
    {
        var code = report.Code?.Coding?.FirstOrDefault()?.Code;
        _logger.LogInformation("Creating DiagnosticReport in FHIR API: Code={Code}", code);

        try
        {
            // Refit + FhirHttpContentSerializer handle serialization/deserialization automatically
            var result = await _api.CreateDiagnosticReportAsync(report, cancellationToken);

            _logger.LogInformation("DiagnosticReport created successfully: Code={Code}, FhirId={FhirId}",
                code, result.Id);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create DiagnosticReport: Code={Code}", code);
            throw;
        }
    }
}
