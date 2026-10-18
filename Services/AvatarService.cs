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
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

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
            MaximumWidth = 1024,
            MaximumHeight = 1024,
            RotateImage = true
        });
        var file = files?.FirstOrDefault();
        if (file is null)
            return null;

        await using var stream = await file.OpenReadAsync();
        return Shrink(stream);
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
