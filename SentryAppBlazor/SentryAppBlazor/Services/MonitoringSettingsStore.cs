using System.Text.Json;
using Microsoft.Extensions.Options;
using SentryAppBlazor.Turnstile;

namespace SentryAppBlazor.Services;

public sealed class MonitoringSettingsStore(
    IWebHostEnvironment environment,
    IOptionsMonitor<MonitoringOptions> options,
    IOptionsMonitor<SentryAppBlazor.Turnstile.SimulationOptions> simulationOptions,
    ILogger<MonitoringSettingsStore> logger)
{
    public const string ConfigFileName = "sentryconfig.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string configPath = Path.Combine(environment.ContentRootPath, ConfigFileName);
    private readonly SemaphoreSlim gate = new(1, 1);

    public MonitoringOptions Current => LoadCurrent();
    public SimulationOptions CurrentSimulation => LoadConfig()?.Simulation?.Clone() ?? simulationOptions.CurrentValue.Clone();

    public async Task SaveAsync(MonitoringOptions monitoring, SimulationOptions simulation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(simulation);
        await gate.WaitAsync(cancellationToken);
        var temporaryPath = $"{configPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var currentSimulation = simulationOptions.CurrentValue;
            var config = new MonitoringConfigFile
            {
                Monitoring = monitoring.Clone(),
                Simulation = new SentryAppBlazor.Turnstile.SimulationOptions
                {
                    IsLiveMode = !monitoring.OperatingMode.Equals("Demo", StringComparison.OrdinalIgnoreCase),
                    EnableSimulatedLogs = monitoring.EnableSimulatedLogs,
                    EnableManualTestLogs = currentSimulation.EnableManualTestLogs,
                    AdministrationKey = currentSimulation.AdministrationKey,
                    MinimumDelaySeconds = currentSimulation.MinimumDelaySeconds,
                    MaximumDelaySeconds = currentSimulation.MaximumDelaySeconds
                }
            };
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, config, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, configPath, overwrite: true);
            logger.LogInformation("Monitoring settings saved to {ConfigPath}", configPath);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private MonitoringOptions LoadCurrent()
    {
        return LoadConfig()?.Monitoring?.Clone() ?? options.CurrentValue.Clone();
    }

    private MonitoringConfigFile? LoadConfig()
    {
        if (!File.Exists(configPath)) return null;

        try
        {
            using var stream = File.OpenRead(configPath);
            return JsonSerializer.Deserialize<MonitoringConfigFile>(stream);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Unable to read monitoring settings from {ConfigPath}", configPath);
            return null;
        }
    }

    private sealed class MonitoringConfigFile
    {
        public MonitoringOptions Monitoring { get; set; } = new();
        public SentryAppBlazor.Turnstile.SimulationOptions? Simulation { get; set; }
    }
}
