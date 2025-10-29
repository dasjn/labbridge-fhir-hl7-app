# PowerShell script to send continuous random HL7v2 messages via MLLP to LabBridge
# Simulates realistic laboratory traffic for dashboard monitoring

param(
    [string]$Server = "localhost",
    [int]$Port = 2575,
    [int]$MinMessages = 1,
    [int]$MaxMessages = 5,
    [int]$IntervalSeconds = 10
)

Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "  HL7v2 Continuous Traffic Generator" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Server: $Server`:$Port" -ForegroundColor Gray
Write-Host "  Messages per batch: $MinMessages-$MaxMessages (random)" -ForegroundColor Gray
Write-Host "  Interval: $IntervalSeconds seconds" -ForegroundColor Gray
Write-Host "  Press Ctrl+C to stop" -ForegroundColor Gray
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
        PatientName = "$lastName, $firstName"
        PanelName = $panel.Name
    }
}

function Send-HL7Message {
    param(
        [string]$HL7Message,
        [string]$MessageId
    )

    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $client.SendTimeout = 5000
        $client.ReceiveTimeout = 5000
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

        $stream.Close()
        $client.Close()

        if ($bytesRead -gt 0) {
            $ackBytes = $responseBuffer[1..($bytesRead - 3)]
            $ackMessage = [System.Text.Encoding]::UTF8.GetString($ackBytes)

            $status = if ($ackMessage -match "MSA\|AA") { "OK" }
                     elseif ($ackMessage -match "MSA\|AE") { "ERROR" }
                     elseif ($ackMessage -match "MSA\|AR") { "REJECT" }
                     else { "UNKNOWN" }

            return @{
                Success = $status -eq "OK"
                Status = $status
            }
        }

        return @{ Success = $false; Status = "NO_ACK" }

    } catch {
        return @{
            Success = $false
            Status = "FAILED"
            Error = $_.Exception.Message
        }
    }
}

# Statistics
$totalSent = 0
$totalSuccess = 0
$totalFailed = 0
$batchNumber = 0
$startTime = Get-Date

Write-Host "Starting continuous traffic..." -ForegroundColor Green
Write-Host "Watch the dashboard at: http://localhost:3000/d/labbridge-main" -ForegroundColor Yellow
Write-Host ""

try {
    while ($true) {
        $batchNumber++

        # Random number of messages in this batch
        $batchSize = Get-Random -Minimum $MinMessages -Maximum ($MaxMessages + 1)

        $batchStart = Get-Date
        $batchSuccess = 0
        $batchFailed = 0

        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Batch #$batchNumber - Sending $batchSize message(s)..." -ForegroundColor Cyan

        for ($i = 1; $i -le $batchSize; $i++) {
            $msg = Generate-RandomHL7Message
            $result = Send-HL7Message -HL7Message $msg.Message -MessageId $msg.MessageId

            $totalSent++

            if ($result.Success) {
                $batchSuccess++
                $totalSuccess++
                $color = "Green"
                $symbol = "✓"
            } else {
                $batchFailed++
                $totalFailed++
                $color = "Red"
                $symbol = "✗"
            }

            Write-Host "  $symbol [$i/$batchSize] $($msg.PatientName) - $($msg.PanelName) - $($result.Status)" -ForegroundColor $color
        }

        $batchDuration = ((Get-Date) - $batchStart).TotalSeconds

        Write-Host "  Batch completed: $batchSuccess OK, $batchFailed failed ($([math]::Round($batchDuration, 2))s)" -ForegroundColor Gray
        Write-Host ""

        # Show cumulative stats every 5 batches
        if ($batchNumber % 5 -eq 0) {
            $elapsed = ((Get-Date) - $startTime).TotalSeconds
            $rate = if ($elapsed -gt 0) { [math]::Round($totalSent / $elapsed, 2) } else { 0 }
            $successRate = if ($totalSent -gt 0) { [math]::Round(($totalSuccess / $totalSent) * 100, 1) } else { 0 }

            Write-Host "=========================================" -ForegroundColor DarkCyan
            Write-Host "  Statistics (after $batchNumber batches)" -ForegroundColor DarkCyan
            Write-Host "=========================================" -ForegroundColor DarkCyan
            Write-Host "  Total sent:     $totalSent messages" -ForegroundColor Gray
            Write-Host "  Success:        $totalSuccess ($successRate%)" -ForegroundColor Green
            Write-Host "  Failed:         $totalFailed" -ForegroundColor $(if ($totalFailed -gt 0) { "Red" } else { "Gray" })
            Write-Host "  Avg rate:       $rate msg/sec" -ForegroundColor Gray
            Write-Host "  Elapsed:        $([math]::Round($elapsed / 60, 1)) minutes" -ForegroundColor Gray
            Write-Host ""
        }

        # Wait for next batch
        Write-Host "  Waiting $IntervalSeconds seconds until next batch..." -ForegroundColor DarkGray
        Start-Sleep -Seconds $IntervalSeconds
    }
}
catch {
    Write-Host ""
    Write-Host "Script interrupted by user" -ForegroundColor Yellow
}
finally {
    $elapsed = ((Get-Date) - $startTime).TotalSeconds

    Write-Host ""
    Write-Host "=========================================" -ForegroundColor Cyan
    Write-Host "  Final Statistics" -ForegroundColor Cyan
    Write-Host "=========================================" -ForegroundColor Cyan
    Write-Host "  Total batches:  $batchNumber" -ForegroundColor Gray
    Write-Host "  Total sent:     $totalSent messages" -ForegroundColor Gray
    Write-Host "  Success:        $totalSuccess" -ForegroundColor Green
    Write-Host "  Failed:         $totalFailed" -ForegroundColor $(if ($totalFailed -gt 0) { "Red" } else { "Gray" })
    Write-Host "  Duration:       $([math]::Round($elapsed / 60, 1)) minutes" -ForegroundColor Gray
    Write-Host ""
}
