using Hl7.Fhir.Model;

namespace LabBridge.Core.Models;

/// <summary>
/// Result of transforming an HL7v2 message to FHIR resources
/// </summary>
public class TransformationResult
{
    /// <summary>
    /// Patient resource extracted from PID segment
    /// </summary>
    public Patient? Patient { get; set; }

    /// <summary>
    /// Observation resources extracted from OBX segments
    /// </summary>
    public List<Observation> Observations { get; set; } = new();

    /// <summary>
    /// DiagnosticReport resource extracted from OBR segment
    /// </summary>
    public DiagnosticReport? DiagnosticReport { get; set; }

    /// <summary>
    /// Original HL7v2 Message Control ID (MSH-10) for correlation
    /// </summary>
    public string MessageControlId { get; set; } = string.Empty;

    /// <summary>
    /// Original HL7v2 message type (e.g., "ORU^R01")
    /// </summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when transformation was performed
    /// </summary>
    public DateTimeOffset TransformedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Indicates if transformation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if transformation failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}
