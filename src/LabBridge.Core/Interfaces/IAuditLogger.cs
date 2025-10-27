namespace LabBridge.Core.Interfaces;

/// <summary>
/// Audit logger interface for tracking all HL7v2 → FHIR message processing.
/// Critical for regulatory compliance (IEC 62304, FDA 21 CFR Part 11).
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs a successfully processed HL7v2 message with FHIR resources.
    /// </summary>
    /// <param name="messageControlId">HL7 Message Control ID (MSH-10)</param>
    /// <param name="messageType">HL7 Message Type (e.g., "ORU^R01")</param>
    /// <param name="rawHl7Message">Full raw HL7v2 message</param>
    /// <param name="fhirPatientJson">Serialized FHIR Patient resource (JSON)</param>
    /// <param name="fhirObservationsJson">Serialized FHIR Observation resources (JSON array)</param>
    /// <param name="fhirDiagnosticReportJson">Serialized FHIR DiagnosticReport resource (JSON)</param>
    /// <param name="patientId">Patient identifier from PID segment</param>
    /// <param name="sourceSystem">Source system (MSH-3 Sending Application)</param>
    /// <param name="fhirServerUrl">FHIR server URL where resources were posted</param>
    /// <param name="receivedAt">When message was received</param>
    /// <param name="processingDurationMs">Processing duration in milliseconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogSuccessAsync(
        string messageControlId,
        string messageType,
        string rawHl7Message,
        string? fhirPatientJson,
        string? fhirObservationsJson,
        string? fhirDiagnosticReportJson,
        string? patientId,
        string? sourceSystem,
        string? fhirServerUrl,
        DateTimeOffset receivedAt,
        long processingDurationMs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a failed HL7v2 message processing attempt.
    /// </summary>
    /// <param name="messageControlId">HL7 Message Control ID (MSH-10)</param>
    /// <param name="messageType">HL7 Message Type (e.g., "ORU^R01")</param>
    /// <param name="rawHl7Message">Full raw HL7v2 message</param>
    /// <param name="errorMessage">Error message</param>
    /// <param name="errorStackTrace">Exception stack trace (for debugging)</param>
    /// <param name="patientId">Patient identifier (if extracted before failure)</param>
    /// <param name="sourceSystem">Source system (MSH-3 Sending Application)</param>
    /// <param name="receivedAt">When message was received</param>
    /// <param name="retryCount">Number of retry attempts</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogFailureAsync(
        string messageControlId,
        string messageType,
        string rawHl7Message,
        string errorMessage,
        string? errorStackTrace,
        string? patientId,
        string? sourceSystem,
        DateTimeOffset receivedAt,
        int retryCount = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches audit logs by patient ID.
    /// Returns messages in descending order by ReceivedAt.
    /// </summary>
    /// <param name="patientId">Patient identifier</param>
    /// <param name="limit">Maximum number of results (default: 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of audit log records</returns>
    Task<List<object>> SearchByPatientAsync(
        string patientId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches audit logs by Message Control ID.
    /// </summary>
    /// <param name="messageControlId">HL7 Message Control ID (MSH-10)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Audit log record (null if not found)</returns>
    Task<object?> GetByMessageControlIdAsync(
        string messageControlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent failed messages for troubleshooting.
    /// </summary>
    /// <param name="limit">Maximum number of results (default: 50)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of failed audit log records</returns>
    Task<List<object>> GetRecentFailuresAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics summary (total processed, success rate, avg processing time).
    /// </summary>
    /// <param name="fromDate">Start date (default: 24 hours ago)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Statistics object</returns>
    Task<object> GetStatisticsAsync(
        DateTimeOffset? fromDate = null,
        CancellationToken cancellationToken = default);
}
