using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LabBridge.Infrastructure.Data;

/// <summary>
/// Audit logger implementation using Entity Framework Core.
/// Stores all HL7v2 → FHIR transformations for regulatory compliance.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly AuditDbContext _context;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(AuditDbContext context, ILogger<AuditLogger> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogSuccessAsync(
        string messageControlId,
        string messageType,
        string rawHl7Message,
        string? fhirPatientJson,
        string? fhirObservationsJson,
        string? fhirDiagnosticReportJson,
        string? patientId,
        string? sourceSystem,
        string? fhirServerUrl,
        DateTimeOffset receivedAt,
        long processingDurationMs,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var auditLog = new AuditLogEntity
            {
                MessageControlId = messageControlId,
                MessageType = messageType,
                RawHl7Message = rawHl7Message,
                FhirPatientJson = fhirPatientJson,
                FhirObservationsJson = fhirObservationsJson,
                FhirDiagnosticReportJson = fhirDiagnosticReportJson,
                PatientId = patientId,
                SourceSystem = sourceSystem,
                FhirServerUrl = fhirServerUrl,
                Status = "Success",
                ReceivedAt = receivedAt,
                ProcessedAt = DateTimeOffset.UtcNow,
                ProcessingDurationMs = processingDurationMs,
                RetryCount = 0
            };

            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Audit log created: MessageControlId={MessageControlId}, Status=Success, Duration={DurationMs}ms",
                messageControlId, processingDurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save audit log for MessageControlId={MessageControlId}", messageControlId);
            // Don't throw - audit logging failure should not break message processing
        }
    }

    public async Task LogFailureAsync(
        string messageControlId,
        string messageType,
        string rawHl7Message,
        string errorMessage,
        string? errorStackTrace,
        string? patientId,
        string? sourceSystem,
        DateTimeOffset receivedAt,
        int retryCount = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var auditLog = new AuditLogEntity
            {
                MessageControlId = messageControlId,
                MessageType = messageType,
                RawHl7Message = rawHl7Message,
                ErrorMessage = errorMessage,
                ErrorStackTrace = errorStackTrace,
                PatientId = patientId,
                SourceSystem = sourceSystem,
                Status = "Failed",
                ReceivedAt = receivedAt,
                ProcessedAt = DateTimeOffset.UtcNow,
                RetryCount = retryCount
            };

            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Audit log created: MessageControlId={MessageControlId}, Status=Failed, Error={ErrorMessage}, RetryCount={RetryCount}",
                messageControlId, errorMessage, retryCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save audit log for MessageControlId={MessageControlId}", messageControlId);
            // Don't throw - audit logging failure should not break message processing
        }
    }

    public async Task<List<object>> SearchByPatientAsync(
        string patientId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var logs = await _context.AuditLogs
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.ReceivedAt)
            .Take(limit)
            .Select(a => new
            {
                a.Id,
                a.MessageControlId,
                a.MessageType,
                a.Status,
                a.ReceivedAt,
                a.ProcessedAt,
                a.ProcessingDurationMs,
                a.SourceSystem,
                a.PatientId,
                a.ErrorMessage
            })
            .ToListAsync(cancellationToken);

        return logs.Cast<object>().ToList();
    }

    public async Task<object?> GetByMessageControlIdAsync(
        string messageControlId,
        CancellationToken cancellationToken = default)
    {
        var log = await _context.AuditLogs
            .Where(a => a.MessageControlId == messageControlId)
            .Select(a => new
            {
                a.Id,
                a.MessageControlId,
                a.MessageType,
                a.Status,
                a.ReceivedAt,
                a.ProcessedAt,
                a.ProcessingDurationMs,
                a.SourceSystem,
                a.PatientId,
                a.FhirServerUrl,
                a.ErrorMessage,
                a.RetryCount,
                a.RawHl7Message,
                a.FhirPatientJson,
                a.FhirObservationsJson,
                a.FhirDiagnosticReportJson
            })
            .FirstOrDefaultAsync(cancellationToken);

        return log;
    }

    public async Task<List<object>> GetRecentFailuresAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var logs = await _context.AuditLogs
            .Where(a => a.Status == "Failed")
            .OrderByDescending(a => a.ReceivedAt)
            .Take(limit)
            .Select(a => new
            {
                a.Id,
                a.MessageControlId,
                a.MessageType,
                a.ReceivedAt,
                a.ProcessedAt,
                a.SourceSystem,
                a.PatientId,
                a.ErrorMessage,
                a.RetryCount
            })
            .ToListAsync(cancellationToken);

        return logs.Cast<object>().ToList();
    }

    public async Task<object> GetStatisticsAsync(
        DateTimeOffset? fromDate = null,
        CancellationToken cancellationToken = default)
    {
        var startDate = fromDate ?? DateTimeOffset.UtcNow.AddHours(-24);

        var logs = await _context.AuditLogs
            .Where(a => a.ReceivedAt >= startDate)
            .ToListAsync(cancellationToken);

        var totalCount = logs.Count;
        var successCount = logs.Count(l => l.Status == "Success");
        var failureCount = logs.Count(l => l.Status == "Failed");
        var successRate = totalCount > 0 ? (double)successCount / totalCount * 100 : 0;
        var avgProcessingTimeMs = logs
            .Where(l => l.ProcessingDurationMs.HasValue)
            .Select(l => l.ProcessingDurationMs!.Value)
            .DefaultIfEmpty(0)
            .Average();

        var messageTypeStats = logs
            .GroupBy(l => l.MessageType)
            .Select(g => new
            {
                MessageType = g.Key,
                Count = g.Count(),
                SuccessCount = g.Count(l => l.Status == "Success"),
                FailureCount = g.Count(l => l.Status == "Failed")
            })
            .ToList();

        return new
        {
            FromDate = startDate,
            ToDate = DateTimeOffset.UtcNow,
            TotalMessages = totalCount,
            SuccessCount = successCount,
            FailureCount = failureCount,
            SuccessRate = Math.Round(successRate, 2),
            AvgProcessingTimeMs = Math.Round(avgProcessingTimeMs, 2),
            MessageTypeBreakdown = messageTypeStats
        };
    }
}
