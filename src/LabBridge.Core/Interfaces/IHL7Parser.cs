namespace LabBridge.Core.Interfaces;

/// <summary>
/// Interface for parsing HL7v2 messages using NHapi
/// </summary>
public interface IHL7Parser
{
    /// <summary>
    /// Parse an HL7v2 message string into a structured message object
    /// </summary>
    /// <param name="hl7Message">Raw HL7v2 message string</param>
    /// <returns>Parsed HL7 message object (NHapi IMessage)</returns>
    /// <exception cref="ArgumentNullException">When hl7Message is null or empty</exception>
    /// <exception cref="HL7Exception">When message parsing fails</exception>
    object Parse(string hl7Message);

    /// <summary>
    /// Validate if an HL7v2 message is well-formed
    /// </summary>
    /// <param name="hl7Message">Raw HL7v2 message string</param>
    /// <returns>True if message is valid, false otherwise</returns>
    bool IsValid(string hl7Message);

    /// <summary>
    /// Get the message type from an HL7v2 message (e.g., "ORU^R01", "ORM^O01")
    /// </summary>
    /// <param name="hl7Message">Raw HL7v2 message string</param>
    /// <returns>Message type string</returns>
    string GetMessageType(string hl7Message);

    /// <summary>
    /// Get the Message Control ID (MSH-10) from an HL7v2 message
    /// </summary>
    /// <param name="hl7Message">Raw HL7v2 message string</param>
    /// <returns>Message Control ID for correlation and deduplication</returns>
    string GetMessageControlId(string hl7Message);
}
