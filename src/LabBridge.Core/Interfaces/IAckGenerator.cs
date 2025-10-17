namespace LabBridge.Core.Interfaces;

/// <summary>
/// Interface for generating HL7v2 ACK (Acknowledgement) messages
/// </summary>
public interface IAckGenerator
{
    /// <summary>
    /// Generate ACK message for successful receipt (AA - Application Accept)
    /// </summary>
    /// <param name="originalMessage">Original HL7v2 message received</param>
    /// <returns>ACK message string with MSA|AA</returns>
    string GenerateAcceptAck(string originalMessage);

    /// <summary>
    /// Generate ACK message for error (AE - Application Error)
    /// </summary>
    /// <param name="originalMessage">Original HL7v2 message received</param>
    /// <param name="errorMessage">Error description</param>
    /// <returns>ACK message string with MSA|AE</returns>
    string GenerateErrorAck(string originalMessage, string errorMessage);

    /// <summary>
    /// Generate ACK message for rejection (AR - Application Reject)
    /// </summary>
    /// <param name="originalMessage">Original HL7v2 message received</param>
    /// <param name="rejectReason">Rejection reason</param>
    /// <returns>ACK message string with MSA|AR</returns>
    string GenerateRejectAck(string originalMessage, string rejectReason);
}
