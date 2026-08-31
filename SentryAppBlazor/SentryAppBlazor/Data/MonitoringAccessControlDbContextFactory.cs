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

        var options = ConfigureProvider(new DbContextOptionsBuilder<AccessControlDbContext>(), connectionString).Options;

        return new AccessControlDbContext(options);
    }

    internal static DbContextOptionsBuilder<TContext> ConfigureProvider<TContext>(
        DbContextOptionsBuilder<TContext> builder, string connectionString) where TContext : DbContext
    {
        if (connectionString.Contains("Filename=", StringComparison.OrdinalIgnoreCase) ||
            (connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) &&
             (connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase) ||
              connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))))
            return builder.UseSqlite(connectionString);

        return builder.UseSqlServer(connectionString);
    }
}

public sealed class MonitoringPersonnelsDbContextFactory(MonitoringSettingsStore settings)
    : IDbContextFactory<PersonnelsDbContext>
{
    public PersonnelsDbContext CreateDbContext() => new(
        BuildOptions<PersonnelsDbContext>(settings.CurrentConnectionStrings.PersonnelsDb, "Personnels"));

    internal static DbContextOptions<TContext> BuildOptions<TContext>(string connectionString, string databaseName)
        where TContext : DbContext
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Set the {databaseName} connection string in Monitoring Settings before starting monitoring or generating demo data.");
        return MonitoringAccessControlDbContextFactory.ConfigureProvider(new DbContextOptionsBuilder<TContext>(), connectionString).Options;
    }
}

public sealed class MonitoringStaffDbContextFactory(MonitoringSettingsStore settings)
    : IDbContextFactory<StaffDbContext>
{
    public StaffDbContext CreateDbContext() => new(
        MonitoringPersonnelsDbContextFactory.BuildOptions<StaffDbContext>(settings.CurrentConnectionStrings.StaffDb, "STAFF"));
}

public sealed class MonitoringStudentDbContextFactory(MonitoringSettingsStore settings)
    : IDbContextFactory<StudentDbContext>
{
    public StudentDbContext CreateDbContext() => new(
        MonitoringPersonnelsDbContextFactory.BuildOptions<StudentDbContext>(settings.CurrentConnectionStrings.StudentDb, "STUDENT"));
}
