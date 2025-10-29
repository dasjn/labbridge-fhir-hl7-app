using Hl7.Fhir.Model;
using Refit;

namespace LabBridge.Infrastructure.FHIR;

/// <summary>
/// Refit interface for LabFlow FHIR API
/// Defines HTTP endpoints for FHIR resource operations
/// Uses FhirHttpContentSerializer for proper FHIR serialization/deserialization
/// Returns IApiResponse to access HTTP status codes and headers
/// </summary>
public interface ILabFlowApi
{
    /// <summary>
    /// POST /Patient - Create or update a Patient resource
    /// Uses conditional create with identifier (MRN) for idempotency
    /// </summary>
    [Post("/Patient")]
    Task<IApiResponse<Patient>> CreatePatientAsync([Body] Patient patient, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /Observation - Create an Observation resource
    /// </summary>
    [Post("/Observation")]
    Task<IApiResponse<Observation>> CreateObservationAsync([Body] Observation observation, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /DiagnosticReport - Create a DiagnosticReport resource
    /// </summary>
    [Post("/DiagnosticReport")]
    Task<IApiResponse<DiagnosticReport>> CreateDiagnosticReportAsync([Body] DiagnosticReport report, CancellationToken cancellationToken = default);
}
