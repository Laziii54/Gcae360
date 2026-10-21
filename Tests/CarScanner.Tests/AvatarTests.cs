using System.Net;
using System.Text;
using CarScanner.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// Profile pictures on the ranking: what the server accepts, that only an approved picture is ever served or joined into
/// the ranking, that replacing / deleting really removes the old file, and how the automatic check's reply is read.
/// Unit tests with a stubbed provider; the real Gemini check and the phone side have not been tried here.
/// </summary>
public sealed class AvatarTests
{
    /// <summary>A JPEG-shaped byte array: SOI + JFIF + a baseline start-of-frame with the given size + padding.</summary>
    private static byte[] Jpeg(int width = 256, int height = 256, int length = 400)
    {
        var data = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 };
        data.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01 });
        while (data.Count < length - 2)
            data.Add(0x55);
        data.AddRange(new byte[] { 0xFF, 0xD9 });
        return data.ToArray();
    }

    [Fact]
    public void AnOrdinarySmallJpeg_IsAccepted()
    {
        Assert.True(AvatarStore.IsAcceptableJpeg(Jpeg(), out var reason), reason);
        Assert.True(AvatarStore.TryReadJpegSize(Jpeg(300, 200), out var width, out var height));
        Assert.Equal((300, 200), (width, height));
    }

    [Fact]
    public void ThingsThatAreNotASmallJpeg_AreRefused()
    {
        Assert.False(AvatarStore.IsAcceptableJpeg(new byte[10], out var tiny));
        Assert.Equal("too small", tiny);

        var png = new byte[400];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        Assert.False(AvatarStore.IsAcceptableJpeg(png, out var notJpeg));
        Assert.Equal("not a JPEG", notJpeg);

        Assert.False(AvatarStore.IsAcceptableJpeg(Jpeg(length: AvatarStore.MaxBytes + 10), out var large));
        Assert.Equal("too large", large);

        Assert.False(AvatarStore.IsAcceptableJpeg(Jpeg(width: 4000, height: 3000), out var big));
        Assert.Equal("dimensions too large", big);
    }

    [Fact]
    public void OnlyThirtyTwoLowercaseHexCharacters_AreAPublicId()
    {
        Assert.True(AvatarStore.IsPublicId(new string('a', 32)));
        Assert.True(AvatarStore.IsPublicId("0123456789abcdef0123456789abcdef"));
        Assert.False(AvatarStore.IsPublicId(new string('A', 32)));
        Assert.False(AvatarStore.IsPublicId(new string('a', 31)));
        Assert.False(AvatarStore.IsPublicId("../../etc/passwd" + new string('a', 16)));
        Assert.False(AvatarStore.IsPublicId(null));
    }

    [Fact]
    public async Task OnlyAnApprovedPicture_IsServed_ReplacingAndDeletingRemoveTheOldFile()
    {
        using var sandbox = new Sandbox();
        var avatars = new AvatarStore(sandbox.Environment, sandbox.Configuration);

        var first = await avatars.SaveAsync("account-1", Jpeg(), AvatarStatus.Approved, CancellationToken.None);
        Assert.NotNull(await avatars.ReadApprovedAsync(first.PublicId, CancellationToken.None));
        Assert.Equal("/api/avatars/" + first.PublicId, first.Url);

        // A new upload gets a new id; the old address stops working and its file is gone.
        var second = await avatars.SaveAsync("account-1", Jpeg(300, 300), AvatarStatus.Approved, CancellationToken.None);
        Assert.NotEqual(first.PublicId, second.PublicId);
        Assert.Null(await avatars.ReadApprovedAsync(first.PublicId, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(sandbox.Directory, "avatars", first.PublicId + ".jpg")));

        // A picture waiting for approval is stored but never served.
        var pending = await avatars.SaveAsync("account-2", Jpeg(), AvatarStatus.Pending, CancellationToken.None);
        Assert.Null(pending.Url);
        Assert.Null(await avatars.ReadApprovedAsync(pending.PublicId, CancellationToken.None));
        Assert.Equal(AvatarStatus.Pending, (await avatars.GetForAccountAsync("account-2", CancellationToken.None))!.Status);

        await avatars.DeleteAsync("account-1", CancellationToken.None);
        Assert.Null(await avatars.GetForAccountAsync("account-1", CancellationToken.None));
        Assert.Null(await avatars.ReadApprovedAsync(second.PublicId, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(sandbox.Directory, "avatars", second.PublicId + ".jpg")));
    }

    [Fact]
    public async Task TheRanking_ShowsOnlyAnApprovedPicture_AndNeverTheAccountId()
    {
        using var sandbox = new Sandbox();
        var ranking = new AccelerationLeaderboardStore(sandbox.Environment, sandbox.Configuration);
        var avatars = new AvatarStore(sandbox.Environment, sandbox.Configuration);

        var gps = new RunGpsCheckResult(RunGpsCheck.Passed, 0.5, 0, 100, 20);
        var withPicture = Request("Nika");
        var withoutPicture = Request("Luka");
        await ranking.SaveAsync("account-with", withPicture, gps, CancellationToken.None);
        await ranking.SaveAsync("account-without", withoutPicture, gps, CancellationToken.None);

        var approved = await avatars.SaveAsync("account-with", Jpeg(), AvatarStatus.Approved, CancellationToken.None);
        await avatars.SaveAsync("account-without", Jpeg(), AvatarStatus.Pending, CancellationToken.None);

        var items = await ranking.GetAsync(CancellationToken.None);
        Assert.Equal("/api/avatars/" + approved.PublicId, items.Single(item => item.UserName == "Nika").AvatarUrl);
        Assert.Null(items.Single(item => item.UserName == "Luka").AvatarUrl);   // pending: not shown
        Assert.DoesNotContain(items, item => item.AvatarUrl is { } url && url.Contains("account-"));

        // Removing the picture removes it from the ranking at once.
        await avatars.DeleteAsync("account-with", CancellationToken.None);
        Assert.All(await ranking.GetAsync(CancellationToken.None), item => Assert.Null(item.AvatarUrl));
    }

    // ---- the administrator side -----------------------------------------------------------------------------------

    [Fact]
    public async Task TheAdministrator_SeesEveryPicture_PendingFirst_CanApproveOne_AndReadsAnyStatus()
    {
        using var sandbox = new Sandbox();
        var avatars = new AvatarStore(sandbox.Environment, sandbox.Configuration);

        var approved = await avatars.SaveAsync("account-a", Jpeg(), AvatarStatus.Approved, CancellationToken.None);
        var pending = await avatars.SaveAsync("account-b", Jpeg(300, 300), AvatarStatus.Pending, CancellationToken.None);

        var list = await avatars.ListAsync(50, CancellationToken.None);
        Assert.Equal(new[] { "account-b", "account-a" }, list.Select(x => x.AccountId));   // waiting for approval first

        // The public address never serves it, the administrator's reader does.
        Assert.Null(await avatars.ReadApprovedAsync(pending.PublicId, CancellationToken.None));
        Assert.NotNull(await avatars.ReadAnyAsync(pending.PublicId, CancellationToken.None));
        Assert.Null(await avatars.ReadAnyAsync("../../secret" + new string('a', 20), CancellationToken.None));
        Assert.Equal("account-b", (await avatars.GetByPublicIdAsync(pending.PublicId, CancellationToken.None))!.AccountId);

        Assert.True(await avatars.SetStatusAsync("account-b", AvatarStatus.Approved, CancellationToken.None));
        Assert.NotNull(await avatars.ReadApprovedAsync(pending.PublicId, CancellationToken.None));
        Assert.False(await avatars.SetStatusAsync("nobody", AvatarStatus.Approved, CancellationToken.None));
        Assert.NotNull(await avatars.ReadApprovedAsync(approved.PublicId, CancellationToken.None));
    }

    private static readonly AdminService.Session AdminSession = new("owner@example.test", "csrf-token");

    [Fact]
    public void TheAdminPhotosPage_ShowsEachPicture_WithApproveOnlyWhenWaiting_DeleteAlways_AndEncodesTheEmail()
    {
        var waiting = new AvatarRecord("acc-1", new string('a', 32), AvatarStatus.Pending, DateTime.UtcNow, 40_000);
        var shown = new AvatarRecord("acc-2", new string('b', 32), AvatarStatus.Approved, DateTime.UtcNow, 30_000);

        var html = AdminPages.Avatars(AdminSession, new[] { (waiting, (string?)"x<script>alert(1)</script>@example.test"), (shown, "ok@example.test") }, null, true);

        Assert.Contains("src=\"/admin/avatars/" + waiting.PublicId + "/image\"", html);
        Assert.Contains("action=\"/admin/avatars/" + waiting.PublicId + "/approve\"", html);
        Assert.DoesNotContain("/admin/avatars/" + shown.PublicId + "/approve", html);               // already shown: nothing to approve
        Assert.Contains("action=\"/admin/avatars/" + shown.PublicId + "/delete\"", html);
        Assert.Contains("action=\"/admin/avatars/" + waiting.PublicId + "/delete\"", html);
        Assert.Contains("name=\"csrf\" value=\"csrf-token\"", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);                                  // the e-mail is HTML-encoded
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("href=\"/admin?tab=avatars\"", html);                                       // it is a tab of the admin menu
    }

    [Fact]
    public void TheAdminPhotosPage_WithNoPictures_SaysSo()
    {
        var html = AdminPages.Avatars(AdminSession, Array.Empty<(AvatarRecord, string?)>(), null, true);
        Assert.Contains("პროფილის ფოტო ჯერ არავის დაუმატებია", html);
    }

    [Fact]
    public void TheAdminPages_MayShowImagesFromTheirOwnOrigin_ButNothingElse()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        AdminService.SecureHeaders(context.Response);
        var policy = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("img-src 'self' data:", policy);
        Assert.Contains("default-src 'none'", policy);
        Assert.DoesNotContain("http:", policy);
        Assert.DoesNotContain("*", policy);
    }

    [Fact]
    public void TheAdminRoutes_AreGuardedByTheAdminSession_AndEveryActionIsAudited()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "Backend", "CarScanner.Api", "Program.cs")).Replace("\r\n", "\n");
        var image = program.IndexOf("\"/admin/avatars/{publicId}/image\"", StringComparison.Ordinal);
        Assert.True(image > 0);
        var body = program.Substring(image, 700);
        Assert.Contains("admin.ReadSession(http.Request, AdminAccess.IsAdmin) is null", body);
        Assert.Contains("private, no-store", body);

        var action = program.IndexOf("if (kind == \"avatars\")", StringComparison.Ordinal);
        Assert.True(action > 0);
        var branch = program.Substring(action, 1400);
        Assert.Contains("\"avatar_approve\"", branch);
        Assert.Contains("\"avatar_delete\"", branch);
    }

    [Fact]
    public void TheIosApp_ExplainsWhyItReadsThePhotoLibrary_ForTheProfilePicturePicker()
    {
        var plist = File.ReadAllText(Path.Combine(Root(), "Platforms", "iOS", "Info.plist"));
        Assert.Contains("<key>NSPhotoLibraryUsageDescription</key>", plist);
    }
    // ---- reading the automatic check's reply ---------------------------------------------------------------------

    private static string Reply(string text, string finish = "STOP") =>
        "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"" + text + "\"}]},\"finishReason\":\"" + finish + "\"}]}";

    [Theory]
    [InlineData("SAFE", AvatarVerdict.Safe)]
    [InlineData("Safe.", AvatarVerdict.Safe)]
    [InlineData("UNSAFE", AvatarVerdict.Unsafe)]
    [InlineData("unsafe - nudity", AvatarVerdict.Unsafe)]
    public void TheAnswerWord_BecomesTheVerdict(string answer, AvatarVerdict expected) =>
        Assert.Equal(expected, AvatarModeration.ReadVerdict(Reply(answer)));

    [Fact]
    public void ARefusalByTheProvider_CountsAsUnsafe()
    {
        Assert.Equal(AvatarVerdict.Unsafe, AvatarModeration.ReadVerdict("{\"promptFeedback\":{\"blockReason\":\"OTHER\"}}"));
        Assert.Equal(AvatarVerdict.Unsafe, AvatarModeration.ReadVerdict(Reply("", "SAFETY")));
        Assert.Equal(AvatarVerdict.Unsafe, AvatarModeration.ReadVerdict(Reply("", "PROHIBITED_CONTENT")));
    }

    [Theory]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"hmm\"}]}}]}")]
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"SAFE\",\"thought\":true}]}}]}")]
    public void AnUnclearReply_IsNoVerdict_SoTheNextModelIsAsked(string body) =>
        Assert.Null(AvatarModeration.ReadVerdict(body));

    private sealed class Scripted(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _index;
        public int Calls => _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses[_index++]());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ATransientFailure_MovesToTheNextModel_AndTheVerdictComesFromTheFirstClearReply()
    {
        var handler = new Scripted(() => Json(HttpStatusCode.ServiceUnavailable, "{}"), () => Json(HttpStatusCode.OK, Reply("UNSAFE")));
        using var client = new HttpClient(handler);

        var verdict = await AvatarModeration.CheckAsync(client, "key", Jpeg(), NullLogger.Instance, CancellationToken.None);

        Assert.Equal(AvatarVerdict.Unsafe, verdict);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ABadKey_StopsAtOnce_AndTheCheckIsUnavailable()
    {
        var handler = new Scripted(() => Json(HttpStatusCode.Forbidden, "{}"));
        using var client = new HttpClient(handler);

        Assert.Equal(AvatarVerdict.Unavailable, await AvatarModeration.CheckAsync(client, "key", Jpeg(), NullLogger.Instance, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task EveryModelFailing_IsUnavailable_NeverSafe()
    {
        var handler = new Scripted(
            () => Json(HttpStatusCode.ServiceUnavailable, "{}"),
            () => Json(HttpStatusCode.TooManyRequests, "{}"),
            () => Json(HttpStatusCode.OK, "{\"candidates\":[]}"));
        using var client = new HttpClient(handler);

        Assert.Equal(AvatarVerdict.Unavailable, await AvatarModeration.CheckAsync(client, "key", Jpeg(), NullLogger.Instance, CancellationToken.None));
        Assert.Equal(AppConfig.AllowedModels.Length, handler.Calls);
    }

    /// <summary>A provider that never answers within the test's budget.</summary>
    private sealed class Hanging : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Json(HttpStatusCode.OK, Reply("SAFE"));
        }
    }

    [Fact]
    public async Task ASlowProvider_RunsOutOfTime_AndIsUnavailable_NeverSafe()
    {
        using var client = new HttpClient(new Hanging());

        var started = DateTime.UtcNow;
        var verdict = await AvatarModeration.CheckWithBudgetAsync(client, "key", Jpeg(), NullLogger.Instance, TimeSpan.FromMilliseconds(300));

        Assert.Equal(AvatarVerdict.Unavailable, verdict);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "the budget must end the wait");
    }

    [Fact]
    public async Task TheCheckWithABudget_StillReturnsTheVerdictOfAFastProvider()
    {
        var handler = new Scripted(() => Json(HttpStatusCode.OK, Reply("UNSAFE")));
        using var client = new HttpClient(handler);

        Assert.Equal(AvatarVerdict.Unsafe, await AvatarModeration.CheckWithBudgetAsync(client, "key", Jpeg(), NullLogger.Instance, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ALateVerdict_ChangesOnlyThePictureItWasAskedAbout()
    {
        using var sandbox = new Sandbox();
        var avatars = new AvatarStore(sandbox.Environment, sandbox.Configuration);

        var first = await avatars.SaveAsync("account-1", Jpeg(), AvatarStatus.Pending, CancellationToken.None);
        // The person uploads a newer picture before the check of the first one finishes.
        var second = await avatars.SaveAsync("account-1", Jpeg(300, 300), AvatarStatus.Pending, CancellationToken.None);

        // The late "safe" for the old picture changes nothing; the same for a late "unsafe".
        Assert.False(await avatars.SetStatusAsync("account-1", AvatarStatus.Approved, CancellationToken.None, onlyIfPublicId: first.PublicId));
        await avatars.DeleteIfCurrentAsync("account-1", first.PublicId, CancellationToken.None);
        Assert.Equal(AvatarStatus.Pending, (await avatars.GetForAccountAsync("account-1", CancellationToken.None))!.Status);

        // For the current picture they do apply.
        Assert.True(await avatars.SetStatusAsync("account-1", AvatarStatus.Approved, CancellationToken.None, onlyIfPublicId: second.PublicId));
        Assert.NotNull(await avatars.ReadApprovedAsync(second.PublicId, CancellationToken.None));
        await avatars.DeleteIfCurrentAsync("account-1", second.PublicId, CancellationToken.None);
        Assert.Null(await avatars.GetForAccountAsync("account-1", CancellationToken.None));
    }

    [Fact]
    public void TheUploadStoresThePicture_WithoutWaitingForTheWholeCheck_AndNotDependingOnThePhoneStayingConnected()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "Backend", "CarScanner.Api", "Program.cs")).Replace("\r\n", "\n");
        Assert.Contains("AvatarModeration.CheckWithBudgetAsync(", program);                    // the check has its own time limit
        Assert.Contains("Task.WhenAny(checkTask, Task.Delay(AvatarModeration.FastWait))", program);   // a slow check does not hold the answer back
        Assert.Contains("avatars.SaveAsync(identity.AccountId, jpeg, status, CancellationToken.None)", program);   // saved even if the phone leaves
        Assert.DoesNotContain("AvatarModeration.CheckAsync(clients.CreateClient(), apiKey, jpeg, logger, ct)", program);
        Assert.Contains("SetStatusAsync(accountId, AvatarStatus.Approved, CancellationToken.None, onlyIfPublicId: storedId)", program);   // a late "safe" publishes it
    }

    [Fact]
    public void TheServer_PublishesAPictureOnlyAfterASafeVerdict_UnlessTheOwnerSwitchedThatOn()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "Backend", "CarScanner.Api", "Program.cs")).Replace("\r\n", "\n");
        Assert.Contains("verdict == AvatarVerdict.Safe || publishUnchecked ? AvatarStatus.Approved : AvatarStatus.Pending", program);
        Assert.Contains("if (verdict == AvatarVerdict.Unsafe)", program);               // rejected: nothing is stored
        Assert.Contains("GetRequiredService<AvatarStore>().DeleteAsync(accountId, ct)", program);   // deleting the account removes the picture
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static AccelerationLeaderboardRequest Request(string name) => new(
        Guid.NewGuid().ToString("N"), name, "Toyota Prius 2014",
        "JTDBR32E720123456", true, "OBD_PID_010D", "0-100", 0, 100,
        9.84, 30, 101, 140, 4.2, DateTime.UtcNow, false);

    private sealed class Sandbox : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "gcar360-avatar-tests", Guid.NewGuid().ToString("N"));
        public IConfiguration Configuration { get; }
        public IHostEnvironment Environment { get; }

        public Sandbox()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDirectory"] = Directory })
                .Build();
            Environment = new TestEnvironment(Directory);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CarScanner.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
