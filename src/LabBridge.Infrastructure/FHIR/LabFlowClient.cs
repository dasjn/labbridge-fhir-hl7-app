using System.Diagnostics;
using Hl7.Fhir.Model;
using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.Observability;
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

        var stopwatch = Stopwatch.StartNew();
        string statusCode;

        try
        {
            // Refit + FhirHttpContentSerializer handle serialization/deserialization automatically
            var response = await _api.CreatePatientAsync(patient, cancellationToken);

            stopwatch.Stop();
            statusCode = ((int)response.StatusCode).ToString(); // Get actual status code from API

            // Track metrics
            LabBridgeMetrics.FhirApiCalls.WithLabels("Patient", "POST", statusCode).Inc();
            LabBridgeMetrics.FhirApiCallDuration.WithLabels("Patient", "POST").Observe(stopwatch.Elapsed.TotalSeconds);

            if (response.IsSuccessStatusCode && response.Content != null)
            {
                _logger.LogInformation("Patient created/updated successfully: MRN={Mrn}, FhirId={FhirId}, StatusCode={StatusCode}",
                    mrn, response.Content.Id, statusCode);

                return response.Content;
            }
            else
            {
                throw new HttpRequestException($"FHIR API returned unsuccessful status code: {statusCode}");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Try to extract status code from Refit's ApiException
            statusCode = ex is Refit.ApiException apiEx
                ? ((int)apiEx.StatusCode).ToString()
                : "500"; // Unknown error

            // Track failure metrics
            LabBridgeMetrics.FhirApiCalls.WithLabels("Patient", "POST", statusCode).Inc();

            _logger.LogError(ex, "Failed to create/update Patient: MRN={Mrn}, StatusCode={StatusCode}", mrn, statusCode);
            throw;
        }
    }

    public async Task<Observation> CreateObservationAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        var code = observation.Code?.Coding?.FirstOrDefault()?.Code;
        _logger.LogInformation("Creating Observation in FHIR API: Code={Code}", code);

        var stopwatch = Stopwatch.StartNew();
        string statusCode;

        try
        {
            // Refit + FhirHttpContentSerializer handle serialization/deserialization automatically
            var response = await _api.CreateObservationAsync(observation, cancellationToken);

            stopwatch.Stop();
            statusCode = ((int)response.StatusCode).ToString(); // Get actual status code from API

            // Track metrics
            LabBridgeMetrics.FhirApiCalls.WithLabels("Observation", "POST", statusCode).Inc();
            LabBridgeMetrics.FhirApiCallDuration.WithLabels("Observation", "POST").Observe(stopwatch.Elapsed.TotalSeconds);

            if (response.IsSuccessStatusCode && response.Content != null)
            {
                _logger.LogInformation("Observation created successfully: Code={Code}, FhirId={FhirId}, StatusCode={StatusCode}",
                    code, response.Content.Id, statusCode);

                return response.Content;
            }
            else
            {
                throw new HttpRequestException($"FHIR API returned unsuccessful status code: {statusCode}");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Try to extract status code from Refit's ApiException
            statusCode = ex is Refit.ApiException apiEx
                ? ((int)apiEx.StatusCode).ToString()
                : "500"; // Unknown error

            // Track failure metrics
            LabBridgeMetrics.FhirApiCalls.WithLabels("Observation", "POST", statusCode).Inc();

            _logger.LogError(ex, "Failed to create Observation: Code={Code}, StatusCode={StatusCode}", code, statusCode);
            throw;
        }
    }

    public async Task<DiagnosticReport> CreateDiagnosticReportAsync(DiagnosticReport report, CancellationToken cancellationToken = default)
    {
        var code = report.Code?.Coding?.FirstOrDefault()?.Code;
        _logger.LogInformation("Creating DiagnosticReport in FHIR API: Code={Code}", code);

        var stopwatch = Stopwatch.StartNew();
        string statusCode;

        try
        {
            // Refit + FhirHttpContentSerializer handle serialization/deserialization automatically
            var response = await _api.CreateDiagnosticReportAsync(report, cancellationToken);

            stopwatch.Stop();
            statusCode = ((int)response.StatusCode).ToString(); // Get actual status code from API

            // Track metrics
            LabBridgeMetrics.FhirApiCalls.WithLabels("DiagnosticReport", "POST", statusCode).Inc();
            LabBridgeMetrics.FhirApiCallDuration.WithLabels("DiagnosticReport", "POST").Observe(stopwatch.Elapsed.TotalSeconds);

            if (response.IsSuccessStatusCode && response.Content != null)
            {
                _logger.LogInformation("DiagnosticReport created successfully: Code={Code}, FhirId={FhirId}, StatusCode={StatusCode}",
                    code, response.Content.Id, statusCode);

                return response.Content;
            }
            else
            {
                throw new HttpRequestException($"FHIR API returned unsuccessful status code: {statusCode}");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Try to extract status code from Refit's ApiException
            statusCode = ex is Refit.ApiException apiEx
                ? ((int)apiEx.StatusCode).ToString()
                : "500"; // Unknown error

            // Track failure metrics
            LabBridgeMetrics.FhirApiCalls.WithLabels("DiagnosticReport", "POST", statusCode).Inc();

            _logger.LogError(ex, "Failed to create DiagnosticReport: Code={Code}, StatusCode={StatusCode}", code, statusCode);
            throw;
        }
    }
}
