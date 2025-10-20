# PowerShell script to send multiple random HL7v2 messages via MLLP to LabBridge

param(
    [string]$Server = "localhost",
    [int]$Port = 2575,
    [int]$Count = 5,
    [switch]$Concurrent
)

Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "  HL7v2 MLLP Load Test Client" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Server: $Server`:$Port" -ForegroundColor Gray
Write-Host "  Messages: $Count" -ForegroundColor Gray
Write-Host "  Mode: $(if ($Concurrent) { 'Concurrent' } else { 'Sequential' })" -ForegroundColor Gray
Write-Host ""

# MLLP framing bytes
$StartByte = [byte]0x0B  # Vertical Tab
$EndByte = [byte]0x1C    # File Separator
$CR = [byte]0x0D         # Carriage Return

# Random data generators
$firstNames = @("Juan", "María", "Carlos", "Ana", "Pedro", "Lucía", "Miguel", "Isabel", "Francisco", "Carmen")
$lastNames = @("García", "Rodríguez", "Martínez", "López", "González", "Hernández", "Pérez", "Sánchez", "Ramírez", "Torres")
$genders = @("M", "F")

# LOINC codes for common lab tests
$testPanels = @(
    @{Code="58410-2"; Name="CBC panel"; Tests=@(
        @{Code="718-7"; Name="Hemoglobin"; Unit="g/dL"; Min=12.0; Max=17.0},
        @{Code="6690-2"; Name="WBC"; Unit="cells/uL"; Min=4000; Max=11000},
        @{Code="777-3"; Name="Platelets"; Unit="cells/uL"; Min=150000; Max=400000}
    )},
    @{Code="24331-1"; Name="Lipid panel"; Tests=@(
        @{Code="2093-3"; Name="Cholesterol"; Unit="mg/dL"; Min=150; Max=250},
        @{Code="2571-8"; Name="Triglycerides"; Unit="mg/dL"; Min=50; Max=200},
        @{Code="2085-9"; Name="HDL"; Unit="mg/dL"; Min=40; Max=80}
    )},
    @{Code="24323-8"; Name="Metabolic panel"; Tests=@(
        @{Code="2345-7"; Name="Glucose"; Unit="mg/dL"; Min=70; Max=140},
        @{Code="2951-2"; Name="Sodium"; Unit="mmol/L"; Min=135; Max=145},
        @{Code="2823-3"; Name="Potassium"; Unit="mmol/L"; Min=3.5; Max=5.0}
    )}
)

function Generate-RandomHL7Message {
    param([int]$Index)

    # Random patient data
    $firstName = $firstNames | Get-Random
    $lastName = $lastNames | Get-Random
    $middleName = $firstNames | Get-Random
    $gender = $genders | Get-Random
    $mrn = "MRN" + (Get-Random -Minimum 100000 -Maximum 999999)
    $birthYear = Get-Random -Minimum 1950 -Maximum 2005
    $birthMonth = "{0:D2}" -f (Get-Random -Minimum 1 -Maximum 12)
    $birthDay = "{0:D2}" -f (Get-Random -Minimum 1 -Maximum 28)
    $birthDate = "$birthYear$birthMonth$birthDay"

    # Random test panel
    $panel = $testPanels | Get-Random

    # Message control ID (unique)
    $messageId = "MSG" + (Get-Random -Minimum 100000 -Maximum 999999)
    $orderNumber = "ORD" + (Get-Random -Minimum 1000 -Maximum 9999)
    $labNumber = "LAB" + (Get-Random -Minimum 10000 -Maximum 99999)

    # Current timestamp
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"

    # Build HL7 message
    $msh = "MSH|^~\&|PANTHER|LAB|LABFLOW|HOSPITAL|$timestamp||ORU^R01|$messageId|P|2.5"
    $pidSegment = "PID|1||$mrn^^^MRN||$lastName^$firstName^$middleName||$birthDate|$gender"
    $obr = "OBR|1|$orderNumber|$labNumber|$($panel.Code)^$($panel.Name)^LN|||$timestamp||||||||||||||||F"

    # Generate OBX segments with random values
    $obxSegments = @()
    $obxIndex = 1
    foreach ($test in $panel.Tests) {
        $value = [math]::Round((Get-Random -Minimum ([double]$test.Min) -Maximum ([double]$test.Max)), 1)
        $refRange = "$($test.Min)-$($test.Max)"
        $abnormalFlag = if ($value -lt $test.Min) { "L" } elseif ($value -gt $test.Max) { "H" } else { "N" }

        $obx = "OBX|$obxIndex|NM|$($test.Code)^$($test.Name)^LN||$value|$($test.Unit)|$refRange|$abnormalFlag|||F|||$timestamp"
        $obxSegments += $obx
        $obxIndex++
    }

    # Combine all segments
    $hl7Message = $msh + "`r" + $pidSegment + "`r" + $obr + "`r" + ($obxSegments -join "`r")

    return @{
        Message = $hl7Message
        MessageId = $messageId
        PatientName = "$lastName, $firstName $middleName"
        PanelName = $panel.Name
        TestCount = $panel.Tests.Count
    }
}

function Send-HL7Message {
    param(
        [string]$HL7Message,
        [string]$MessageId,
        [int]$MessageNumber
    )

    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $client.Connect($Server, $Port)
        $stream = $client.GetStream()

        # Prepare MLLP-framed message
        $messageBytes = [System.Text.Encoding]::UTF8.GetBytes($HL7Message)
        $framedMessage = New-Object byte[] ($messageBytes.Length + 3)
        $framedMessage[0] = $StartByte
        [Array]::Copy($messageBytes, 0, $framedMessage, 1, $messageBytes.Length)
        $framedMessage[$framedMessage.Length - 2] = $EndByte
        $framedMessage[$framedMessage.Length - 1] = $CR

        # Send message
        $stream.Write($framedMessage, 0, $framedMessage.Length)
        $stream.Flush()

        # Wait for ACK
        $responseBuffer = New-Object byte[] 8192
        $bytesRead = $stream.Read($responseBuffer, 0, $responseBuffer.Length)

        if ($bytesRead -gt 0) {
            $ackBytes = $responseBuffer[1..($bytesRead - 3)]
            $ackMessage = [System.Text.Encoding]::UTF8.GetString($ackBytes)

            $status = if ($ackMessage -match "MSA\|AA") { "[OK] ACCEPTED" }
                     elseif ($ackMessage -match "MSA\|AE") { "[ERR] ERROR" }
                     elseif ($ackMessage -match "MSA\|AR") { "[REJ] REJECTED" }
                     else { "[?] UNKNOWN" }

            $color = if ($status -like "*ACCEPTED*") { "Green" } else { "Red" }

            Write-Host "[$MessageNumber/$Count] $status - MessageId=$MessageId" -ForegroundColor $color

            return @{
                Success = $status -like "*ACCEPTED*"
                MessageId = $MessageId
                Status = $status
            }
        }

        $stream.Close()
        $client.Close()

    } catch {
        Write-Host "[$MessageNumber/$Count] [FAIL] FAILED - MessageId=$MessageId - Error: $($_.Exception.Message)" -ForegroundColor Red
        return @{
            Success = $false
            MessageId = $MessageId
            Status = "FAILED"
            Error = $_.Exception.Message
        }
    }
}

# Generate all messages
Write-Host "Generating $Count random HL7v2 messages..." -ForegroundColor Yellow
$messages = @()
for ($i = 1; $i -le $Count; $i++) {
    $msg = Generate-RandomHL7Message -Index $i
    $messages += $msg
    Write-Host "  [$i] $($msg.PatientName) - $($msg.PanelName) ($($msg.TestCount) tests)" -ForegroundColor Gray
}
Write-Host ""

# Send messages
Write-Host "Sending messages to $Server`:$Port..." -ForegroundColor Yellow
Write-Host ""

$startTime = Get-Date

if ($Concurrent) {
    # Send all messages concurrently using runspaces
    $jobs = @()
    for ($i = 0; $i -lt $messages.Count; $i++) {
        $msg = $messages[$i]
        $scriptBlock = {
            param($Server, $Port, $HL7Message, $MessageId, $MessageNumber, $StartByte, $EndByte, $CR)

            try {
                $client = New-Object System.Net.Sockets.TcpClient
                $client.SendTimeout = 10000  # 10 seconds
                $client.ReceiveTimeout = 10000  # 10 seconds
                $client.Connect($Server, $Port)
                $stream = $client.GetStream()

                $messageBytes = [System.Text.Encoding]::UTF8.GetBytes($HL7Message)
                $framedMessage = New-Object byte[] ($messageBytes.Length + 3)
                $framedMessage[0] = $StartByte
                [Array]::Copy($messageBytes, 0, $framedMessage, 1, $messageBytes.Length)
                $framedMessage[$framedMessage.Length - 2] = $EndByte
                $framedMessage[$framedMessage.Length - 1] = $CR

                $stream.Write($framedMessage, 0, $framedMessage.Length)
                $stream.Flush()

                # Give server time to process and respond
                Start-Sleep -Milliseconds 50

                $responseBuffer = New-Object byte[] 8192
                $bytesRead = $stream.Read($responseBuffer, 0, $responseBuffer.Length)

                $ackBytes = $responseBuffer[1..($bytesRead - 3)]
                $ackMessage = [System.Text.Encoding]::UTF8.GetString($ackBytes)

                $status = if ($ackMessage -match "MSA\|AA") { "ACCEPTED" } else { "ERROR" }

                # Small delay before closing to ensure ACK is fully received
                Start-Sleep -Milliseconds 10

                $stream.Close()
                $client.Close()

                return @{
                    Success = $status -eq "ACCEPTED"
                    MessageId = $MessageId
                    MessageNumber = $MessageNumber
                    Status = $status
                }
            } catch {
                return @{
                    Success = $false
                    MessageId = $MessageId
                    MessageNumber = $MessageNumber
                    Status = "FAILED"
                    Error = $_.Exception.Message
                }
            }
        }

        $job = Start-Job -ScriptBlock $scriptBlock -ArgumentList $Server, $Port, $msg.Message, $msg.MessageId, ($i + 1), $StartByte, $EndByte, $CR
        $jobs += $job
    }

    # Wait for all jobs to complete
    $results = $jobs | Wait-Job | Receive-Job

    # Display results
    foreach ($result in $results | Sort-Object MessageNumber) {
        $color = if ($result.Success) { "Green" } else { "Red" }
        $symbol = if ($result.Success) { "[OK]" } else { "[ERR]" }
        Write-Host "[$($result.MessageNumber)/$Count] $symbol $($result.Status) - MessageId=$($result.MessageId)" -ForegroundColor $color
    }

    # Cleanup jobs
    $jobs | Remove-Job

} else {
    # Send messages sequentially
    $results = @()
    for ($i = 0; $i -lt $messages.Count; $i++) {
        $msg = $messages[$i]
        $result = Send-HL7Message -HL7Message $msg.Message -MessageId $msg.MessageId -MessageNumber ($i + 1)
        $results += $result
    }
}

$endTime = Get-Date
$duration = ($endTime - $startTime).TotalSeconds

# Summary
Write-Host ""
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
$successCount = ($results | Where-Object { $_.Success }).Count
$failedCount = $Count - $successCount
Write-Host "Total messages:   $Count" -ForegroundColor Gray
Write-Host "Successful:       $successCount" -ForegroundColor Green
Write-Host "Failed:           $failedCount" -ForegroundColor $(if ($failedCount -gt 0) { "Red" } else { "Gray" })
Write-Host "Duration:         $([math]::Round($duration, 2)) seconds" -ForegroundColor Gray
Write-Host "Throughput:       $([math]::Round($Count / $duration, 2)) msg/sec" -ForegroundColor Gray
Write-Host ""
Write-Host "Check RabbitMQ UI: http://localhost:15672" -ForegroundColor Yellow
Write-Host ""
