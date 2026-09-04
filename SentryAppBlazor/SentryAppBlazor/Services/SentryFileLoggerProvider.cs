using System.Text;

namespace SentryAppBlazor.Services;

/// <summary>
/// Writes error-level application logs to a text file without requiring an
/// external logging package.
/// </summary>
public sealed class SentryFileLoggerProvider(string filePath) : ILoggerProvider
{
    private readonly object writeLock = new();
    private readonly string filePath = Path.GetFullPath(filePath);

    public ILogger CreateLogger(string categoryName) => new SentryFileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(
        LogLevel logLevel,
        EventId eventId,
        string categoryName,
        string message,
        Exception? exception)
    {
        var entry = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("O"))
            .Append(" [").Append(logLevel).Append("] ")
            .Append(categoryName);

        if (eventId.Id != 0)
        {
            entry.Append(" (EventId: ").Append(eventId.Id).Append(')');
        }

        entry.AppendLine().AppendLine(message);
        if (exception is not null)
        {
            entry.AppendLine(exception.ToString());
        }

        entry.AppendLine();

        try
        {
            lock (writeLock)
            {
                File.AppendAllText(filePath, entry.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
        {
            // A logging failure must not terminate the application or hide the
            // original exception being reported by another logging provider.
        }
    }

    private sealed class SentryFileLogger(SentryFileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(logLevel, eventId, categoryName, formatter(state, exception), exception);
        }
    }
}
