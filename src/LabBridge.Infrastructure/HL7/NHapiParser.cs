using LabBridge.Core.Interfaces;
using NHapi.Base.Model;
using NHapi.Base.Parser;
using NHapi.Model.V25.Message;

namespace LabBridge.Infrastructure.HL7;

/// <summary>
/// HL7v2 parser implementation using NHapi library
/// </summary>
public class NHapiParser : IHL7Parser
{
    private readonly PipeParser _parser;

    public NHapiParser()
    {
        _parser = new PipeParser();
    }

    public object Parse(string hl7Message)
    {
        if (string.IsNullOrWhiteSpace(hl7Message))
        {
            throw new ArgumentNullException(nameof(hl7Message), "HL7 message cannot be null or empty");
        }

        try
        {
            IMessage parsedMessage = _parser.Parse(hl7Message);
            return parsedMessage;
        }
        catch (NHapi.Base.HL7Exception ex)
        {
            throw new NHapi.Base.HL7Exception($"Failed to parse HL7 message: {ex.Message}", ex);
        }
    }

    public bool IsValid(string hl7Message)
    {
        if (string.IsNullOrWhiteSpace(hl7Message))
        {
            return false;
        }

        try
        {
            _parser.Parse(hl7Message);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GetMessageType(string hl7Message)
    {
        if (string.IsNullOrWhiteSpace(hl7Message))
        {
            throw new ArgumentNullException(nameof(hl7Message), "HL7 message cannot be null or empty");
        }

        try
        {
            var message = _parser.Parse(hl7Message);
            var messageType = message.GetStructureName(); // e.g., "ORU_R01"
            return messageType.Replace("_", "^"); // Convert to HL7 format: "ORU^R01"
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract message type: {ex.Message}", ex);
        }
    }

    public string GetMessageControlId(string hl7Message)
    {
        if (string.IsNullOrWhiteSpace(hl7Message))
        {
            throw new ArgumentNullException(nameof(hl7Message), "HL7 message cannot be null or empty");
        }

        try
        {
            var message = _parser.Parse(hl7Message);

            // Handle ORU^R01 (most common lab message type)
            if (message is ORU_R01 oruMessage)
            {
                return oruMessage.MSH.MessageControlID.Value;
            }

            // Handle other message types via reflection
            var mshProperty = message.GetType().GetProperty("MSH");
            if (mshProperty != null)
            {
                dynamic msh = mshProperty.GetValue(message);
                return msh?.MessageControlID?.Value ?? string.Empty;
            }

            throw new InvalidOperationException("Could not access MSH segment to extract Message Control ID");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract Message Control ID: {ex.Message}", ex);
        }
    }
}
