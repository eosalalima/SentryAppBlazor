using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SentryAppBlazor.Services;

public sealed class MonitoringSettingsStore(
    IWebHostEnvironment environment,
    IOptionsMonitor<MonitoringOptions> options,
    ILogger<MonitoringSettingsStore> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string configPath = Path.Combine(environment.ContentRootPath, "config.json");
    private readonly SemaphoreSlim gate = new(1, 1);

    public MonitoringOptions Current => options.CurrentValue.Clone();

    public async Task SaveAsync(MonitoringOptions monitoring, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitoring);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var config = new MonitoringConfigFile { Monitoring = monitoring.Clone() };
            await using var stream = File.Create(configPath);
            await JsonSerializer.SerializeAsync(stream, config, SerializerOptions, cancellationToken);
            logger.LogInformation("Monitoring settings saved to {ConfigPath}", configPath);
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed class MonitoringConfigFile
    {
        public MonitoringOptions Monitoring { get; set; } = new();
    }
}
