using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LabBridge.Infrastructure.Data;

/// <summary>
/// Design-time factory for creating AuditDbContext instances.
/// Required for EF Core migrations to work.
/// </summary>
public class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuditDbContext>();

        // Use PostgreSQL for migrations (default connection string)
        // This can be overridden in production via appsettings.json
        optionsBuilder.UseNpgsql("Host=localhost;Database=labbridge_audit;Username=postgres;Password=dev_password");

        return new AuditDbContext(optionsBuilder.Options);
    }
}
