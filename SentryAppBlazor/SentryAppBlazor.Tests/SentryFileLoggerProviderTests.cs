using Microsoft.Extensions.Logging;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Tests;

public sealed class SentryFileLoggerProviderTests
{
    [Fact]
    public void Error_logs_include_the_exception_in_sentry_log()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sentry-log-{Guid.NewGuid():N}");
        var logPath = Path.Combine(directory, "sentry.log");
        Directory.CreateDirectory(directory);

        try
        {
            using var provider = new SentryFileLoggerProvider(logPath);
            var logger = provider.CreateLogger("Tests.ExceptionSource");
            var exception = new InvalidOperationException("database unavailable");

            logger.LogError(exception, "Polling failed for {Device}", "North Gate");

            var log = File.ReadAllText(logPath);
            Assert.Contains("[Error] Tests.ExceptionSource", log);
            Assert.Contains("Polling failed for North Gate", log);
            Assert.Contains("System.InvalidOperationException: database unavailable", log);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Logs_below_error_level_are_not_written()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sentry-log-{Guid.NewGuid():N}");
        var logPath = Path.Combine(directory, "sentry.log");
        Directory.CreateDirectory(directory);

        try
        {
            using var provider = new SentryFileLoggerProvider(logPath);
            var logger = provider.CreateLogger("Tests.InformationalSource");

            logger.LogWarning("Temporary connection delay");

            Assert.False(File.Exists(logPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
