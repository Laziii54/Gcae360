using Android.Content;
using Android.Media;
using Android.Provider;
using CarScanner.Services.PerformanceVideo;

namespace CarScanner.Droid.PerformanceVideo;

/// <summary>Android: burns the overlay into the video and saves files to Movies/GCar360.</summary>
public sealed class AndroidPerformanceVideoExporter : IPerformanceVideoExporter
{
    private const string AlbumFolder = "GCar360";

    public bool IsSupported => true;

    public Task ComposeWithOverlayAsync(
        string inputPath,
        string outputPath,
        PerformanceVideoTelemetry telemetry,
        PerformanceOverlayStyle style,
        IProgress<double>? progress,
        CancellationToken token) =>
        AndroidOverlayVideoComposer.ComposeAsync(inputPath, outputPath, telemetry, style, progress, token);

    public async Task<bool> EnsureSavePermissionAsync()
    {
        // Android 10+ saves through MediaStore without a permission.
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            return true;

        var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.StorageWrite>();
        return status == PermissionStatus.Granted;
    }

    public async Task SaveToGalleryAsync(string filePath, string displayName, CancellationToken token)
    {
        var context = Platform.AppContext;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var resolver = context.ContentResolver!;
            var values = new ContentValues();
            values.Put("_display_name", displayName);
            values.Put("mime_type", "video/mp4");
            values.Put("relative_path", $"{Android.OS.Environment.DirectoryMovies}/{AlbumFolder}");
            values.Put("is_pending", 1);

            var collection = MediaStore.Video.Media.GetContentUri("external_primary")!;
            var uri = resolver.Insert(collection, values) ?? throw new IOException("MediaStore insert failed");

            try
            {
                await using (var output = resolver.OpenOutputStream(uri) ?? throw new IOException("Cannot open gallery file"))
                await using (var input = File.OpenRead(filePath))
                {
                    await input.CopyToAsync(output, token);
                }

                var done = new ContentValues();
                done.Put("is_pending", 0);
                resolver.Update(uri, done, null, null);
            }
            catch
            {
                try { resolver.Delete(uri, null, null); } catch { }
                throw;
            }

            return;
        }

#pragma warning disable CS0618, CA1422 // Public Movies folder on Android 7–9.
        var movies = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryMovies)!.AbsolutePath;
#pragma warning restore CS0618, CA1422
        var folder = Path.Combine(movies, AlbumFolder);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, displayName);

        await using (var input = File.OpenRead(filePath))
        await using (var output = File.Create(target))
        {
            await input.CopyToAsync(output, token);
        }

        MediaScannerConnection.ScanFile(context, new[] { target }, new[] { "video/mp4" }, null);
    }
}
