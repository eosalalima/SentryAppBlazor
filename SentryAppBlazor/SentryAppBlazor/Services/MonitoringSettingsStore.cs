using System.Text.Json;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace SentryAppBlazor.Services;

public sealed class MonitoringSettingsStore(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IOptionsMonitor<MonitoringOptions> options,
    ILogger<MonitoringSettingsStore> logger)
{
    public const string ConfigFileName = "sentryconfig.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string configPath = Path.Combine(environment.ContentRootPath, ConfigFileName);
    private readonly SemaphoreSlim gate = new(1, 1);

    public MonitoringOptions Current => LoadCurrent();
    public ConnectionStringSettings CurrentConnectionStrings
    {
        get
        {
            var persisted = LoadConfig()?.ConnectionStrings;
            return new ConnectionStringSettings
            {
                AccessControlDb = persisted?.AccessControlDb ?? configuration.GetConnectionString("AccessControlDb") ?? string.Empty,
                StaffDb = persisted?.StaffDb ?? configuration.GetConnectionString("StaffDb") ?? string.Empty,
                StudentDb = persisted?.StudentDb ?? configuration.GetConnectionString("StudentDb") ?? string.Empty,
                PersonnelsDb = persisted?.PersonnelsDb ?? configuration.GetConnectionString("PersonnelsDb") ?? string.Empty
            };
        }
    }
    public string CurrentDatabaseConnectionString => CurrentConnectionStrings.AccessControlDb;
    public async Task SaveAsync(
        MonitoringOptions monitoring,
        ConnectionStringSettings connectionStrings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(connectionStrings);
        Validator.ValidateObject(monitoring, new ValidationContext(monitoring), validateAllProperties: true);
        await gate.WaitAsync(cancellationToken);
        var temporaryPath = $"{configPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var config = new MonitoringConfigFile
            {
                AllowedHosts = "*",
                ConnectionStrings = connectionStrings.Clone(),
                Monitoring = monitoring.Clone()
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
        public string AllowedHosts { get; set; } = "*";
        public ConnectionStringSettings? ConnectionStrings { get; set; }
        public MonitoringOptions Monitoring { get; set; } = new();
    }

}

public sealed class ConnectionStringSettings
{
    public string AccessControlDb { get; set; } = string.Empty;
    public string StaffDb { get; set; } = string.Empty;
    public string StudentDb { get; set; } = string.Empty;
    public string PersonnelsDb { get; set; } = string.Empty;

    public ConnectionStringSettings Clone() => new()
    {
        AccessControlDb = AccessControlDb,
        StaffDb = StaffDb,
        StudentDb = StudentDb,
        PersonnelsDb = PersonnelsDb
    };
}
