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
    public SimulationOptions CurrentSimulation
    {
        get
        {
            var config = LoadConfig();
            return ResolveSimulation(
                config?.IsLiveMode,
                config?.Monitoring,
                config?.Simulation,
                simulationOptions.CurrentValue);
        }
    }

    // Older sentryconfig.json files stored IsLiveMode at the root and the demo
    // switch under Monitoring, without a Simulation section.  Falling all the
    // way back to appsettings.json in that case silently re-enabled live mode,
    // preventing the demo generator from ever writing a row.
    internal static SimulationOptions ResolveSimulation(
        bool? legacyIsLiveMode,
        MonitoringOptions? monitoring,
        SimulationOptions? persisted,
        SimulationOptions defaults)
    {
        if (persisted is not null)
            return persisted.Clone();

        var resolved = defaults.Clone();
        if (legacyIsLiveMode.HasValue)
            resolved.IsLiveMode = legacyIsLiveMode.Value;
        if (monitoring is not null)
            resolved.EnableSimulatedLogs = monitoring.EnableSimulatedLogs;
        return resolved;
    }

    public async Task SaveAsync(MonitoringOptions monitoring, SimulationOptions simulation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(simulation);
        await gate.WaitAsync(cancellationToken);
        var temporaryPath = $"{configPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var config = new MonitoringConfigFile
            {
                IsLiveMode = simulation.IsLiveMode,
                Monitoring = monitoring.Clone(),
                Simulation = new SentryAppBlazor.Turnstile.SimulationOptions
                {
                    IsLiveMode = simulation.IsLiveMode,
                    EnableSimulatedLogs = monitoring.EnableSimulatedLogs,
                    EnableManualTestLogs = simulation.EnableManualTestLogs,
                    AdministrationKey = simulation.AdministrationKey,
                    MinimumDelaySeconds = simulation.MinimumDelaySeconds,
                    MaximumDelaySeconds = simulation.MaximumDelaySeconds
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
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Unable to read monitoring settings from {ConfigPath}", configPath);
            return null;
        }
    }

    private sealed class MonitoringConfigFile
    {
        public bool IsLiveMode { get; set; } = true;
        public MonitoringOptions Monitoring { get; set; } = new();
        public SentryAppBlazor.Turnstile.SimulationOptions? Simulation { get; set; }
    }
}
