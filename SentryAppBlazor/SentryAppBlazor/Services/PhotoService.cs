using Microsoft.AspNetCore.Http.HttpResults;
namespace SentryAppBlazor.Services;

public sealed class PhotoService(MonitoringSettingsStore settings, IWebHostEnvironment environment, ILogger<PhotoService> logger)
{
    public IResult Get(string photoId)
    {
        var safeId = Path.GetFileName(photoId);
        if (string.IsNullOrWhiteSpace(safeId) || safeId != photoId || Path.HasExtension(safeId))
            return Placeholder();

        try
        {
            // Read persisted settings for every request so changing the protected
            // photo directory through the Settings page takes effect immediately.
            var root = Path.GetFullPath(settings.Current.PhotosPath);
            var path = Path.GetFullPath(Path.Combine(root, safeId + ".jpg"));
            if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                return Results.File(path, "image/jpeg", enableRangeProcessing: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(ex, "Unable to read personnel photo {PhotoId}", safeId);
        }
        return Placeholder();
    }

    private IResult Placeholder() => Results.File(
        Path.Combine(environment.WebRootPath, "img", "avatar-placeholder.svg"), "image/svg+xml");
}
