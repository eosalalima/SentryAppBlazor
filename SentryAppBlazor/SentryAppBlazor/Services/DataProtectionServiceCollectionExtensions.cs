using Microsoft.AspNetCore.DataProtection;

namespace SentryAppBlazor.Services;

public static class DataProtectionServiceCollectionExtensions
{
    private const string DefaultApplicationName = "SentryAppBlazor";
    private const string DefaultKeysPath = "data-protection-keys";

    public static IServiceCollection AddPersistentDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        var configuredPath = configuration["DataProtection:KeysPath"];
        var keysPath = string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultKeysPath
            : configuredPath;
        var absoluteKeysPath = Path.GetFullPath(keysPath, contentRootPath);

        Directory.CreateDirectory(absoluteKeysPath);

        var applicationName = configuration["DataProtection:ApplicationName"];
        services
            .AddDataProtection()
            .SetApplicationName(string.IsNullOrWhiteSpace(applicationName)
                ? DefaultApplicationName
                : applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(absoluteKeysPath));

        return services;
    }
}
