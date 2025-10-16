using LabBridge.Core.Models;

namespace LabBridge.Core.Interfaces;

/// <summary>
/// Interface for transforming HL7v2 messages to FHIR R4 resources
/// </summary>
public interface IHL7ToFhirTransformer
{
    /// <summary>
    /// Transform an HL7v2 ORU^R01 message to FHIR resources
    /// (Patient, Observation[], DiagnosticReport)
    /// </summary>
    /// <param name="hl7Message">Parsed HL7v2 message object (NHapi IMessage)</param>
    /// <returns>Transformation result containing FHIR resources</returns>
    TransformationResult Transform(object hl7Message);

    /// <summary>
    /// Transform PID segment to FHIR Patient resource
    /// </summary>
    /// <param name="pidSegment">PID segment from HL7v2 message</param>
    /// <returns>FHIR Patient resource</returns>
    object TransformPatient(object pidSegment);

    /// <summary>
    /// Transform OBX segment to FHIR Observation resource
    /// </summary>
    /// <param name="obxSegment">OBX segment from HL7v2 message</param>
    /// <param name="patientId">Patient identifier for subject reference</param>
    /// <returns>FHIR Observation resource</returns>
    object TransformObservation(object obxSegment, string patientId);

    /// <summary>
    /// Transform OBR segment to FHIR DiagnosticReport resource
    /// </summary>
    /// <param name="obrSegment">OBR segment from HL7v2 message</param>
    /// <param name="patientId">Patient identifier for subject reference</param>
    /// <param name="observationIds">List of Observation IDs to include in result</param>
    /// <returns>FHIR DiagnosticReport resource</returns>
    object TransformDiagnosticReport(object obrSegment, string patientId, List<string> observationIds);
}
