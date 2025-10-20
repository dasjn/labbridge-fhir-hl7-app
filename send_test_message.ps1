# PowerShell script to send HL7v2 message via MLLP to LabBridge

param(
    [string]$Server = "localhost",
    [int]$Port = 2575,
    [string]$MessageFile = "test_oru_r01.hl7"
)

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  HL7v2 MLLP Test Client" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# MLLP framing bytes
$StartByte = [byte]0x0B  # Vertical Tab
$EndByte = [byte]0x1C    # File Separator
$CR = [byte]0x0D         # Carriage Return

try {
    # Read HL7 message from file
    if (-not (Test-Path $MessageFile)) {
        Write-Host "Error: File '$MessageFile' not found" -ForegroundColor Red
        exit 1
    }

    $hl7Content = Get-Content $MessageFile -Raw
    $hl7Content = $hl7Content -replace "`r`n", "`r"  # Ensure only \r line endings

    Write-Host "Message file: $MessageFile" -ForegroundColor Green
    Write-Host "HL7 Content ($($hl7Content.Length) chars):" -ForegroundColor Yellow
    Write-Host $hl7Content -ForegroundColor Gray
    Write-Host ""

    # Connect to MLLP server
    Write-Host "Connecting to $Server`:$Port..." -ForegroundColor Yellow
    $client = New-Object System.Net.Sockets.TcpClient
    $client.Connect($Server, $Port)
    $stream = $client.GetStream()

    Write-Host "Connected successfully!" -ForegroundColor Green
    Write-Host ""

    # Prepare MLLP-framed message
    $messageBytes = [System.Text.Encoding]::UTF8.GetBytes($hl7Content)
    $framedMessage = New-Object byte[] ($messageBytes.Length + 3)
    $framedMessage[0] = $StartByte
    [Array]::Copy($messageBytes, 0, $framedMessage, 1, $messageBytes.Length)
    $framedMessage[$framedMessage.Length - 2] = $EndByte
    $framedMessage[$framedMessage.Length - 1] = $CR

    # Send message
    Write-Host "Sending MLLP-framed message ($($framedMessage.Length) bytes)..." -ForegroundColor Yellow
    $stream.Write($framedMessage, 0, $framedMessage.Length)
    $stream.Flush()
    Write-Host "Message sent!" -ForegroundColor Green
    Write-Host ""

    # Wait for ACK response
    Write-Host "Waiting for ACK response..." -ForegroundColor Yellow
    $responseBuffer = New-Object byte[] 8192
    $bytesRead = $stream.Read($responseBuffer, 0, $responseBuffer.Length)

    if ($bytesRead -gt 0) {
        # Extract ACK content (remove MLLP framing)
        $ackBytes = $responseBuffer[1..($bytesRead - 3)]
        $ackMessage = [System.Text.Encoding]::UTF8.GetString($ackBytes)

        Write-Host "ACK received ($bytesRead bytes):" -ForegroundColor Green
        Write-Host $ackMessage -ForegroundColor Cyan
        Write-Host ""

        # Check ACK status
        if ($ackMessage -match "MSA\|AA") {
            Write-Host "SUCCESS: Message accepted (AA)" -ForegroundColor Green
        } elseif ($ackMessage -match "MSA\|AE") {
            Write-Host "ERROR: Message error (AE)" -ForegroundColor Red
        } elseif ($ackMessage -match "MSA\|AR") {
            Write-Host "REJECTED: Message rejected (AR)" -ForegroundColor Red
        }
    } else {
        Write-Host "Warning: No ACK received" -ForegroundColor Yellow
    }

} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    if ($stream) { $stream.Close() }
    if ($client) { $client.Close() }
    Write-Host ""
    Write-Host "Connection closed" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Press any key to exit..." -ForegroundColor Yellow
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
