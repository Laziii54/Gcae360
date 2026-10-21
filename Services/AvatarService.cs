using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace CarScanner.Services;

public enum AvatarOutcome
{
    /// <summary>The picture was checked and is shown on the ranking.</summary>
    Added,
    /// <summary>Stored, but it is shown only after an administrator approves it (the automatic check was not available).</summary>
    WaitingForApproval,
    /// <summary>The check found the picture offensive; nothing was stored.</summary>
    Rejected,
    NotSignedIn,
    TooManyAttempts,
    Failed
}

public sealed record AvatarUploadResult(AvatarOutcome Outcome, string? AvatarUrl);

public sealed record AvatarInfo(bool HasAvatar, string? Status, string? AvatarUrl);

/// <summary>
/// The person's own profile picture: pick, shrink (256 px JPEG), upload, remove. The server checks every picture before it
/// is shown to others. Nothing is sent unless the person chooses a picture.
/// </summary>
public sealed class AvatarService
{
    /// <summary>Longest side sent to the server.</summary>
    public const int MaxSidePixels = 256;

    private readonly AccountService _account;
    // The server answers within about ten seconds (a slow check no longer holds the upload back), so a long wait means a real problem.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public AvatarService(AccountService account) => _account = account;

    /// <summary>Makes an absolute address from the relative one the server returns (empty when there is none).</summary>
    public static string Absolute(string? relativeOrAbsolute)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
            return string.Empty;
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute))
            return absolute.ToString();
        return Uri.TryCreate(AIConfig.BackendBaseUrl, UriKind.Absolute, out var baseUri)
            ? new Uri(baseUri, relativeOrAbsolute).ToString()
            : string.Empty;
    }

    /// <summary>Lets the person pick a photo from the gallery (or any photo source the system offers) and returns it as a small JPEG; null when cancelled.</summary>
    public async Task<byte[]?> PickAndShrinkAsync()
    {
        // Android: the system photo picker (no permission needed; older systems use the ordinary "choose a picture" screen).
        // iOS 14+: the system photo picker (no permission needed). RotateImage fixes the orientation of phone photos before
        // they are shrunk. The single-photo method is obsolete in .NET 10: one photo = SelectionLimit 1.
        var files = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
        {
            Title = "OBD360",
            SelectionLimit = 1,
            MaximumWidth = 512,
            MaximumHeight = 512,
            RotateImage = true
        });
        var file = files?.FirstOrDefault();
        if (file is null)
            return null;

        await using var stream = await file.OpenReadAsync();
        // Decoding and shrinking a phone photo takes a moment: not on the screen thread, or the page freezes while it works.
        return await Task.Run(() => Shrink(stream));
    }

    /// <summary>Re-encodes any picture as a JPEG whose longest side is at most 256 px (about 20-60 KB).</summary>
    public static byte[] Shrink(Stream source)
    {
        var image = PlatformImage.FromStream(source);
        var small = image.Width > MaxSidePixels || image.Height > MaxSidePixels
            ? image.Downsize(MaxSidePixels, disposeOriginal: true)
            : image;
        try { return small.AsBytes(ImageFormat.Jpeg, 0.85f); }
        finally { small.Dispose(); }
    }

    // ---- the own picture kept on the phone ----------------------------------------------------------------------------
    // The picture belongs to the account on the server. A copy of what the person chose is also kept here, so it shows at
    // once (also while the server still checks it, and without a network) every time the account page opens. The file name
    // changes with every new picture, because the image control may cache a picture by its file name.

    private static string LocalPrefix(string? email)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((email ?? string.Empty).Trim().ToLowerInvariant()));
        return "profile-photo-" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant() + "-";
    }

    /// <summary>The newest kept copy of the picture of this account; empty when there is none.</summary>
    public static string LocalFileFor(string? email)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
                return string.Empty;
            var prefix = LocalPrefix(email);
            return Directory.EnumerateFiles(FileSystem.AppDataDirectory, prefix + "*.jpg")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Keeps a new copy next to the older ones (they are removed by <see cref="PruneLocal"/> once the upload succeeded). Empty when it could not be written.</summary>
    public static string SaveLocalCandidate(string? email, byte[] jpeg)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
                return string.Empty;
            var path = Path.Combine(FileSystem.AppDataDirectory, LocalPrefix(email) + DateTime.UtcNow.Ticks.ToString("D19") + ".jpg");
            File.WriteAllBytes(path, jpeg);
            return path;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Removes every kept copy of the picture of this account except <paramref name="keep"/> (empty keep = remove all).</summary>
    public static void PruneLocal(string? email, string keep = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
                return;
            foreach (var file in Directory.EnumerateFiles(FileSystem.AppDataDirectory, LocalPrefix(email) + "*.jpg"))
                if (!string.Equals(file, keep, StringComparison.Ordinal))
                    TryDelete(file);
        }
        catch
        {
        }
    }

    public static void DeleteLocalFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            TryDelete(path);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    public async Task<AvatarUploadResult> UploadAsync(byte[] jpeg, CancellationToken token = default)
    {
        if (!_account.IsSignedIn)
            return new(AvatarOutcome.NotSignedIn, null);

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/accounts/avatar");
            request.Content = new ByteArrayContent(jpeg);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    return new(AvatarOutcome.NotSignedIn, null);
                case HttpStatusCode.TooManyRequests:
                    return new(AvatarOutcome.TooManyAttempts, null);
                case HttpStatusCode.UnprocessableEntity:
                    return new(AvatarOutcome.Rejected, null);
            }

            if (!response.IsSuccessStatusCode)
                return new(AvatarOutcome.Failed, null);

            var body = await response.Content.ReadFromJsonAsync<UploadBody>(cancellationToken: token).ConfigureAwait(false);
            return string.Equals(body?.Status, "Approved", StringComparison.OrdinalIgnoreCase)
                ? new(AvatarOutcome.Added, Absolute(body?.AvatarUrl))
                : new(AvatarOutcome.WaitingForApproval, null);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return new(AvatarOutcome.Failed, null);
        }
    }

    public async Task<bool> DeleteAsync(CancellationToken token = default)
    {
        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Delete, "/api/accounts/avatar");
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    /// <summary>The state of the own picture on the server; null when it could not be read.</summary>
    public async Task<AvatarInfo?> GetMineAsync(CancellationToken token = default)
    {
        if (!_account.IsSignedIn)
            return null;

        try
        {
            using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Get, "/api/accounts/avatar");
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadFromJsonAsync<MineBody>(cancellationToken: token).ConfigureAwait(false);
            return body is null ? null : new AvatarInfo(body.HasAvatar, body.Status, Absolute(body.AvatarUrl));
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private sealed record UploadBody(string? Status, string? AvatarUrl);
    private sealed record MineBody(bool HasAvatar, string? Status, string? AvatarUrl);
}
