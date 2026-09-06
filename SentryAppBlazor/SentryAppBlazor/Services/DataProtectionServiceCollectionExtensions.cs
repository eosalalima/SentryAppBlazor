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
        var applicationName = configuration["DataProtection:ApplicationName"];
        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName(string.IsNullOrWhiteSpace(applicationName)
                ? DefaultApplicationName
                : applicationName);

        // An IIS application pool commonly has read-only access to the deployed
        // application directory. Do not prevent the entire web application from
        // starting when the configured key ring cannot be created there. In that
        // case ASP.NET Core can still use its platform default (or an ephemeral key
        // ring) until the administrator configures a writable shared location.
        if (TryPrepareKeysDirectory(keysPath, contentRootPath, out var keysDirectory))
        {
            dataProtection.PersistKeysToFileSystem(keysDirectory);
        }

        return services;
    }

    private static bool TryPrepareKeysDirectory(
        string keysPath,
        string contentRootPath,
        out DirectoryInfo keysDirectory)
    {
        keysDirectory = null!;

        try
        {
            var absoluteKeysPath = Path.GetFullPath(keysPath, contentRootPath);
            Directory.CreateDirectory(absoluteKeysPath);
            var probePath = Path.Combine(absoluteKeysPath, $".write-test-{Guid.NewGuid():N}");
            using (File.Create(probePath, bufferSize: 1, FileOptions.DeleteOnClose))
            {
            }

            keysDirectory = new DirectoryInfo(absoluteKeysPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
    }
}
