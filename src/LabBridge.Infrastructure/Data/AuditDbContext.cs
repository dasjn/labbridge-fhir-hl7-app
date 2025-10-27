using LabBridge.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LabBridge.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context for audit logging.
/// Stores all HL7v2 → FHIR transformations for regulatory compliance.
/// Uses PostgreSQL in production, SQLite for development.
/// </summary>
public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Audit logs table.
    /// </summary>
    public DbSet<AuditLogEntity> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            // Table name
            entity.ToTable("AuditLogs");

            // Primary key
            entity.HasKey(e => e.Id);

            // Indexes for common queries
            // 1. Search by Message Control ID (unique identifier from HL7)
            entity.HasIndex(e => e.MessageControlId)
                .HasDatabaseName("IX_AuditLogs_MessageControlId");

            // 2. Search by Patient ID
            entity.HasIndex(e => e.PatientId)
                .HasDatabaseName("IX_AuditLogs_PatientId");

            // 3. Search by Status (for finding failures)
            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_AuditLogs_Status");

            // 4. Compound index: PatientId + ReceivedAt (for patient timeline queries)
            entity.HasIndex(e => new { e.PatientId, e.ReceivedAt })
                .HasDatabaseName("IX_AuditLogs_PatientId_ReceivedAt");

            // 5. Search by ReceivedAt (for time-based queries)
            entity.HasIndex(e => e.ReceivedAt)
                .HasDatabaseName("IX_AuditLogs_ReceivedAt");

            // 6. Search by MessageType (for analytics)
            entity.HasIndex(e => e.MessageType)
                .HasDatabaseName("IX_AuditLogs_MessageType");

            // Required fields
            entity.Property(e => e.MessageControlId)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.MessageType)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.RawHl7Message)
                .IsRequired();

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.ReceivedAt)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("NOW()"); // PostgreSQL: NOW(), SQLite: datetime('now')

            // Optional fields
            entity.Property(e => e.PatientId)
                .HasMaxLength(50);

            entity.Property(e => e.SourceSystem)
                .HasMaxLength(100);

            entity.Property(e => e.FhirServerUrl)
                .HasMaxLength(200);

            // Large text fields (no length limit)
            entity.Property(e => e.FhirPatientJson);
            entity.Property(e => e.FhirObservationsJson);
            entity.Property(e => e.FhirDiagnosticReportJson);
            entity.Property(e => e.ErrorMessage);
            entity.Property(e => e.ErrorStackTrace);
        });
    }
}
