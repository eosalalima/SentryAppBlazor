using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;

/// <summary>Creates and seeds the persistent SQLite database shipped with demo mode.</summary>
public sealed class DemoDatabaseInitializer(
    MonitoringSettingsStore settings,
    ILogger<DemoDatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var monitoring = settings.Current;
        var connectionString = settings.CurrentDatabaseConnectionString;
        if (!DemoDeviceLogGenerator.IsDemoMode(monitoring) ||
            (!connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase) &&
             !connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))) return;

        var builder = new DbContextOptionsBuilder<DemoDatabaseContext>();
        MonitoringAccessControlDbContextFactory.ConfigureProvider(builder, connectionString);
        await using var db = new DemoDatabaseContext(builder.Options);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (!await db.ZkDevices.AnyAsync(cancellationToken))
            db.ZkDevices.Add(new ZkDevice { SerialNumber = "DEMO-GATE-01", Name = "Main Campus Gate" });
        if (!await db.Personnels.AnyAsync(cancellationToken))
        {
            db.Personnels.AddRange(
                new Personnel { AccessNumber = "DGL-1001", FirstName = "Ada", LastName = "Lovelace" },
                new Personnel { AccessNumber = "DGL-1002", FirstName = "Alan", LastName = "Turing" },
                new Personnel { AccessNumber = "DGL-1003", FirstName = "Grace", LastName = "Hopper" });
        }
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Local demo database is ready at {ConnectionString}", connectionString);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

}
