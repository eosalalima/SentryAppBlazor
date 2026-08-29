using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Data;

/// <summary>
/// Creates access-control contexts from the currently persisted monitoring setting.
/// A normal AddDbContextFactory registration captures its options at startup, which
/// would leave demo generation connected to the old database after Apply is clicked.
/// </summary>
public sealed class MonitoringAccessControlDbContextFactory(MonitoringSettingsStore settings)
    : IDbContextFactory<AccessControlDbContext>
{
    public AccessControlDbContext CreateDbContext()
    {
        var connectionString = settings.CurrentDatabaseConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Set the Database connection string in Monitoring Settings before starting monitoring or generating demo data.");

        var options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AccessControlDbContext(options);
    }
}
