namespace SentryAppBlazor.Services;

public sealed class PhotoService(
    MonitoringSettingsStore settings,
    IWebHostEnvironment environment,
    ILogger<PhotoService> logger)
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
            [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml"
        };

    public IResult Get(string photoId)
    {
        if (!TryResolve(photoId, out var path, out var contentType))
        {
            logger.LogWarning("Rejected or missing personnel photo identifier");
            return Placeholder();
        }

        return Results.File(path, contentType, enableRangeProcessing: true);
    }

    internal bool TryResolve(string? photoId, out string path, out string contentType)
    {
        path = string.Empty;
        contentType = string.Empty;
        if (string.IsNullOrWhiteSpace(photoId)) return false;
        var requested = photoId.Trim();
        var sanitized = Path.GetFileName(requested);
        if (sanitized != requested || Path.IsPathRooted(requested) || requested.Contains("..", StringComparison.Ordinal) ||
            requested.IndexOfAny(['/', '\\']) >= 0 ||
            !ContentTypes.TryGetValue(Path.GetExtension(sanitized), out contentType!)) return false;

        try
        {
            var configuredRoot = settings.Current.ExternalPhotoDirectory;
            if (string.IsNullOrWhiteSpace(configuredRoot)) return false;
            var root = Path.GetFullPath(configuredRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            path = Path.GetFullPath(Path.Combine(root, sanitized));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return path.StartsWith(root + Path.DirectorySeparatorChar, comparison) && File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning(exception, "Unable to resolve a personnel photo");
            return false;
        }
    }

    private IResult Placeholder() => Results.File(
        Path.Combine(environment.WebRootPath, "img", "avatar-placeholder.svg"), "image/svg+xml");
}
