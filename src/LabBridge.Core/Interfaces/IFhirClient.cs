using Hl7.Fhir.Model;

namespace LabBridge.Core.Interfaces;

/// <summary>
/// Interface for FHIR API client (LabFlow API)
/// Uses Refit for type-safe HTTP calls
/// </summary>
public interface IFhirClient
{
    /// <summary>
    /// Creates or updates a Patient resource in the FHIR server
    /// </summary>
    /// <param name="patient">Patient resource to create/update</param>
    /// <returns>Created/updated Patient resource with server-assigned ID</returns>
    Task<Patient> CreateOrUpdatePatientAsync(Patient patient, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an Observation resource in the FHIR server
    /// </summary>
    /// <param name="observation">Observation resource to create</param>
    /// <returns>Created Observation resource with server-assigned ID</returns>
    Task<Observation> CreateObservationAsync(Observation observation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a DiagnosticReport resource in the FHIR server
    /// </summary>
    /// <param name="report">DiagnosticReport resource to create</param>
    /// <returns>Created DiagnosticReport resource with server-assigned ID</returns>
    Task<DiagnosticReport> CreateDiagnosticReportAsync(DiagnosticReport report, CancellationToken cancellationToken = default);
}
