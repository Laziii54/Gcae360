namespace CarScanner.Tests;

/// <summary>App side of the profile picture: the Account page picks and uploads it, the ranking shows it or a default
/// car icon. Source-reading checks only; picking from the gallery and the look on a phone were not tried here.</summary>
public sealed class AvatarAppWiringTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    [Fact]
    public void TheRanking_ShowsThePhotoOrTheDefaultIcon_NotLetters()
    {
        var xaml = Read("Views", "AccelerationLeaderboardPage.xaml");
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(xaml, "<controls:AvatarView ").Count);   // podium 1-3 and the list rows
        Assert.DoesNotContain(".Initial}", xaml);
        Assert.Contains("CarScanner.Services.AvatarService.Absolute(entry.AvatarUrl)", Read("ViewModels", "AccelerationLeaderboardViewModel.cs"));
    }

    [Fact]
    public void TheAccountPage_LetsThePersonPickFromTheGallery_OrRemoveThePhoto()
    {
        Assert.Contains("TapGestureRecognizer Tapped=\"OnAvatarTapped\"", Read("Views", "AccountPage.xaml"));
        var code = Read("Views", "AccountPage.xaml.cs");
        Assert.Contains("_avatars.PickAndShrinkAsync()", code);
        Assert.Contains("_avatars.UploadAsync(jpeg)", code);
        Assert.Contains("_avatars.DeleteAsync()", code);
        Assert.Contains("AvatarOutcome.Rejected", code);   // an offensive photo is refused with a clear message
    }

    [Fact]
    public void ThePhoto_IsShrunkOnThePhone_AndSentOnlyToTheOwnAccountEndpoint()
    {
        var service = Read("Services", "AvatarService.cs");
        Assert.Contains("MaxSidePixels = 256", service);
        Assert.Contains("ImageFormat.Jpeg", service);
        Assert.Contains("\"/api/accounts/avatar\"", service);
        Assert.Contains("RotateImage = true", service);
        // .NET 10: the single-photo method is obsolete; the system pickers need no permission on Android 13+ / iOS 14+.
        Assert.Contains("MediaPicker.Default.PickPhotosAsync(", service);
        Assert.Contains("SelectionLimit = 1", service);
        Assert.DoesNotContain("PickPhotoAsync(", service);
    }

    [Fact]
    public void ThePhotoIsShrunkOffTheScreenThread_ShowsAtOnce_AndAStoredCopyIsKeptOnThePhone()
    {
        var service = Read("Services", "AvatarService.cs");
        Assert.Contains("await Task.Run(() => Shrink(stream))", service);              // the page does not freeze while a big photo is decoded
        Assert.Contains("TimeSpan.FromSeconds(30)", service);                           // no minute-long wait before an error
        Assert.Contains("SaveLocalCandidate(", service);

        var page = Read("Views", "AccountPage.xaml.cs");
        Assert.Contains("AvatarService.SaveLocalCandidate(email, jpeg)", page);          // the chosen photo appears before the upload ends
        Assert.Contains("AvatarService.LocalFileFor(_account.Email)", page);             // and again every time the page opens, also offline
        Assert.Contains("AvatarService.PruneLocal(_account.Email)", page);               // removed together with the photo on the server
        Assert.Contains("WaitForApprovalAsync", page);                                   // the server finishes its check in the background
        Assert.Contains("LocalFilePath", Read("Controls", "AvatarView.cs"));
    }
}