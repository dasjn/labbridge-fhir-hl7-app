using LabBridge.Core.Interfaces;
using NHapi.Base.Parser;
using NHapi.Model.V25.Message;
using NHapi.Model.V25.Segment;

namespace LabBridge.Infrastructure.HL7;

/// <summary>
/// Generates HL7v2 ACK (Acknowledgement) messages in response to received messages
/// </summary>
public class AckGenerator : IAckGenerator
{
    private readonly PipeParser _parser;

    public AckGenerator()
    {
        _parser = new PipeParser();
    }

    public string GenerateAcceptAck(string originalMessage)
    {
        return GenerateAck(originalMessage, "AA", null);
    }

    public string GenerateErrorAck(string originalMessage, string errorMessage)
    {
        return GenerateAck(originalMessage, "AE", errorMessage);
    }

    public string GenerateRejectAck(string originalMessage, string rejectReason)
    {
        return GenerateAck(originalMessage, "AR", rejectReason);
    }

    private string GenerateAck(string originalMessage, string acknowledgmentCode, string? errorText)
    {
        try
        {
            // Parse original message to extract MSH fields
            var originalMsg = _parser.Parse(originalMessage);
            var originalMsh = ((ACK)_parser.Parse(originalMessage)).MSH;

            // Create ACK message
            var ack = new ACK();

            // Populate MSH segment (swap sender/receiver)
            var msh = ack.MSH;
            msh.FieldSeparator.Value = "|";
            msh.EncodingCharacters.Value = "^~\\&";
            msh.SendingApplication.NamespaceID.Value = originalMsh.ReceivingApplication.NamespaceID.Value;
            msh.SendingFacility.NamespaceID.Value = originalMsh.ReceivingFacility.NamespaceID.Value;
            msh.ReceivingApplication.NamespaceID.Value = originalMsh.SendingApplication.NamespaceID.Value;
            msh.ReceivingFacility.NamespaceID.Value = originalMsh.SendingFacility.NamespaceID.Value;
            msh.DateTimeOfMessage.Time.Value = DateTime.Now.ToString("yyyyMMddHHmmss");
            msh.MessageType.MessageCode.Value = "ACK";
            msh.MessageType.TriggerEvent.Value = originalMsh.MessageType.TriggerEvent.Value;
            msh.MessageControlID.Value = originalMsh.MessageControlID.Value;
            msh.ProcessingID.ProcessingID.Value = originalMsh.ProcessingID.ProcessingID.Value;
            msh.VersionID.VersionID.Value = "2.5";

            // Populate MSA segment (acknowledgment)
            var msa = ack.MSA;
            msa.AcknowledgmentCode.Value = acknowledgmentCode;
            msa.MessageControlID.Value = originalMsh.MessageControlID.Value;

            if (!string.IsNullOrWhiteSpace(errorText))
            {
                msa.TextMessage.Value = errorText;
            }

            // Convert ACK to string
            return _parser.Encode(ack);
        }
        catch (Exception ex)
        {
            // Fallback: Generate minimal ACK manually if parsing fails
            var messageControlId = ExtractMessageControlId(originalMessage);
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");

            return $"MSH|^~\\&|LABBRIDGE|HOSPITAL|SENDER|FACILITY|{timestamp}||ACK|{messageControlId}|P|2.5\r" +
                   $"MSA|{acknowledgmentCode}|{messageControlId}|{errorText ?? string.Empty}";
        }
    }

    private string ExtractMessageControlId(string hl7Message)
    {
        if (string.IsNullOrWhiteSpace(hl7Message))
        {
            return Guid.NewGuid().ToString().Substring(0, 20);
        }

        try
        {
            // MSH-10 is the 10th field in MSH segment
            var mshLine = hl7Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            var fields = mshLine.Split('|');

            // Field separator counts as field 0, so MSH-10 is at index 9
            if (fields.Length > 9)
            {
                return fields[9];
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return Guid.NewGuid().ToString().Substring(0, 20);
    }
}
