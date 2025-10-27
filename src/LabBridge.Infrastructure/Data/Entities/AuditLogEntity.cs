using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LabBridge.Infrastructure.Data.Entities;

/// <summary>
/// Audit log entity for tracking all HL7v2 → FHIR transformations.
/// Stores raw HL7v2 messages, FHIR resources, processing results, and error details.
/// Critical for regulatory compliance (IEC 62304, FDA 21 CFR Part 11).
/// </summary>
[Table("AuditLogs")]
public class AuditLogEntity
{
    /// <summary>
    /// Unique identifier for this audit log entry.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    /// <summary>
    /// HL7 Message Control ID (MSH-10).
    /// Unique identifier from the HL7v2 message header.
    /// Used for correlation and idempotency checking.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string MessageControlId { get; set; } = string.Empty;

    /// <summary>
    /// HL7 Message Type (e.g., "ORU^R01", "ORM^O01").
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string MessageType { get; set; } = string.Empty;

    /// <summary>
    /// Raw HL7v2 message as received (full text).
    /// Stored for troubleshooting and regulatory compliance.
    /// </summary>
    [Required]
    public string RawHl7Message { get; set; } = string.Empty;

    /// <summary>
    /// Serialized FHIR Patient resource (JSON).
    /// Null if transformation failed before creating Patient.
    /// </summary>
    public string? FhirPatientJson { get; set; }

    /// <summary>
    /// Serialized FHIR Observation resources (JSON array).
    /// Null if transformation failed before creating Observations.
    /// </summary>
    public string? FhirObservationsJson { get; set; }

    /// <summary>
    /// Serialized FHIR DiagnosticReport resource (JSON).
    /// Null if transformation failed before creating DiagnosticReport.
    /// </summary>
    public string? FhirDiagnosticReportJson { get; set; }

    /// <summary>
    /// Processing status: Success, Failed, PartialSuccess.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Error message if processing failed.
    /// Null if Status = Success.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Detailed exception stack trace if processing failed.
    /// Used for debugging and troubleshooting.
    /// </summary>
    public string? ErrorStackTrace { get; set; }

    /// <summary>
    /// Patient ID extracted from PID segment (PID-3).
    /// Used for searching audit logs by patient.
    /// </summary>
    [MaxLength(50)]
    public string? PatientId { get; set; }

    /// <summary>
    /// Number of retry attempts made (if applicable).
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// When the message was received by the MLLP listener.
    /// </summary>
    [Required]
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// When the message processing completed (success or failure).
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// Processing duration in milliseconds.
    /// Useful for performance monitoring.
    /// </summary>
    public long? ProcessingDurationMs { get; set; }

    /// <summary>
    /// Source system sending the HL7v2 message (e.g., "Abbott Architect", "Hologic Panther").
    /// Extracted from MSH-3 (Sending Application).
    /// </summary>
    [MaxLength(100)]
    public string? SourceSystem { get; set; }

    /// <summary>
    /// FHIR server URL where resources were posted.
    /// </summary>
    [MaxLength(200)]
    public string? FhirServerUrl { get; set; }

    /// <summary>
    /// Record creation timestamp (immutable).
    /// </summary>
    [Required]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
