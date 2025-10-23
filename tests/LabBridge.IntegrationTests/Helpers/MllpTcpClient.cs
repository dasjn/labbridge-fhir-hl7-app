using System.Net.Sockets;
using System.Text;

namespace LabBridge.IntegrationTests.Helpers;

/// <summary>
/// Cliente TCP MLLP simple que simula un analizador de laboratorio
/// enviando mensajes HL7v2 a LabBridge
/// </summary>
/// <remarks>
/// MLLP (Minimal Lower Layer Protocol) usa framing especial:
/// - Start Byte: 0x0B (Vertical Tab)
/// - Message content (HL7v2)
/// - End Byte: 0x1C (File Separator)
/// - Carriage Return: 0x0D
/// </remarks>
public class MllpTcpClient : IDisposable
{
    // MLLP protocol bytes
    private const byte StartByte = 0x0B;  // Vertical Tab
    private const byte EndByte = 0x1C;    // File Separator
    private const byte CarriageReturn = 0x0D;

    private readonly string _hostname;
    private readonly int _port;

    /// <summary>
    /// Crea un nuevo cliente MLLP TCP
    /// </summary>
    /// <param name="hostname">Hostname del servidor MLLP (ej: "localhost")</param>
    /// <param name="port">Puerto del servidor MLLP (ej: 2575)</param>
    public MllpTcpClient(string hostname, int port)
    {
        _hostname = hostname;
        _port = port;
    }

    /// <summary>
    /// Envía un mensaje HL7v2 y espera la respuesta ACK
    /// </summary>
    /// <param name="hl7Message">Mensaje HL7v2 sin framing MLLP (plain text)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Respuesta ACK del servidor (sin framing MLLP)</returns>
    /// <exception cref="SocketException">Si hay errores de red</exception>
    /// <exception cref="IOException">Si hay errores de I/O</exception>
    public async Task<string> SendMessageAsync(string hl7Message, CancellationToken cancellationToken = default)
    {
        // Paso 1: Conectar al servidor MLLP
        using var client = new TcpClient();
        await client.ConnectAsync(_hostname, _port, cancellationToken);

        using var stream = client.GetStream();

        // Configurar timeouts
        stream.ReadTimeout = 5000;  // 5 segundos para leer ACK
        stream.WriteTimeout = 5000; // 5 segundos para enviar mensaje

        // Paso 2: Envolver mensaje con framing MLLP
        // Formato: <0x0B> + HL7 message + <0x1C> + <0x0D>
        var messageBytes = WrapWithMllpFraming(hl7Message);

        // Paso 3: Enviar mensaje
        await stream.WriteAsync(messageBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // Paso 4: Leer respuesta ACK
        var buffer = new byte[4096]; // Buffer de 4KB (suficiente para ACK típico)
        var bytesRead = await stream.ReadAsync(buffer, cancellationToken);

        // Paso 5: Extraer ACK del framing MLLP
        var ackMessage = ExtractMessage(buffer, bytesRead);

        return ackMessage;
    }

    /// <summary>
    /// Envuelve un mensaje HL7v2 con framing MLLP
    /// </summary>
    /// <param name="message">Mensaje HL7v2 plain text</param>
    /// <returns>Mensaje con framing MLLP: [0x0B][message][0x1C][0x0D]</returns>
    private byte[] WrapWithMllpFraming(string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var framedMessage = new byte[messageBytes.Length + 3]; // +3 para start, end, CR

        // [0x0B] + message + [0x1C] + [0x0D]
        framedMessage[0] = StartByte;
        Array.Copy(messageBytes, 0, framedMessage, 1, messageBytes.Length);
        framedMessage[framedMessage.Length - 2] = EndByte;
        framedMessage[framedMessage.Length - 1] = CarriageReturn;

        return framedMessage;
    }

    /// <summary>
    /// Extrae el contenido del mensaje desde el framing MLLP
    /// </summary>
    /// <param name="buffer">Buffer con mensaje MLLP completo</param>
    /// <param name="bytesRead">Cantidad de bytes leídos</param>
    /// <returns>Mensaje sin framing MLLP</returns>
    private string ExtractMessage(byte[] buffer, int bytesRead)
    {
        if (bytesRead < 3)
        {
            // Mensaje demasiado corto para tener framing válido
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }

        // Buscar start byte (0x0B) y end byte (0x1C)
        var startIndex = Array.IndexOf(buffer, StartByte);
        var endIndex = Array.IndexOf(buffer, EndByte);

        if (startIndex == -1 || endIndex == -1 || startIndex >= endIndex)
        {
            // Framing inválido, devolver todo el buffer
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }

        // Extraer contenido entre start byte y end byte
        var messageLength = endIndex - startIndex - 1;
        return Encoding.UTF8.GetString(buffer, startIndex + 1, messageLength);
    }

    public void Dispose()
    {
        // No hay recursos no administrados que liberar en esta implementación simple
    }
}
