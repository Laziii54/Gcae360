using System.Text;
using CarScanner.Services.DataCollection;
using CarScanner.Services.OBD;

namespace CarScanner.Tests;

public sealed class CollectionSanitizerTests
{
    private static ObdRawExchange Ex(string request, string response, int tx = 0x7E0, int rx = 0x7E8, bool ext29 = false) =>
        new(tx, rx, ext29, request, response);

    private static string Hex(string ascii) => Convert.ToHexString(Encoding.ASCII.GetBytes(ascii));

    [Fact]
    public void ReadOnlyAnswer_IsKept()
    {
        var clean = CollectionSanitizer.Sanitize(Ex("22F40C", "7E8046 2F40C1AF8".Replace(" ", "")));
        Assert.NotNull(clean);
        Assert.Equal("22F40C", clean!.Request);
        Assert.Equal(0x7E0, clean.TxCanId);
    }

    [Fact]
    public void Mode09Pid02_Vin_IsDropped() =>
        Assert.Null(CollectionSanitizer.Sanitize(Ex("0902", "7E8101449020157564E")));

    [Theory]
    [InlineData("22F190")] // VIN DID
    [InlineData("22F18C")] // ECU serial number
    [InlineData("22F19D")] // not on the allow-list
    public void IdentifyingDids_AreDropped(string request) =>
        Assert.Null(CollectionSanitizer.Sanitize(Ex(request, "7E80562" + request[2..] + "AABB")));

    [Fact]
    public void SoftwareVersionDid_IsKept() =>
        Assert.NotNull(CollectionSanitizer.Sanitize(Ex("22F189", "7E80562F189AABB")));

    [Theory]
    [InlineData("1003")] // session control
    [InlineData("2701")] // security access
    [InlineData("2E1234AA")] // write data
    [InlineData("31010203")] // routine
    [InlineData("14FFFFFF")] // clear DTC
    [InlineData("04")] // OBD clear DTC
    [InlineData("3101FF00")] // routine control
    public void NonReadRequests_AreNeverCollected(string request) =>
        Assert.Null(CollectionSanitizer.Sanitize(Ex(request, "7E8025000")));

    [Fact]
    public void VinInsideAnAnswer_DropsTheWholeRecord()
    {
        // Single frame carrying 17 ASCII VIN characters.
        var vin = "WVWZZZ1JZ3W386752";
        Assert.Null(CollectionSanitizer.Sanitize(Ex("22F1A0", "7E8" + "10" + "14" + "62F1A0" + Hex(vin)[..8])));
        Assert.Null(CollectionSanitizer.Sanitize(Ex("22F1A0", "7E80062" + Hex(vin))));
    }

    [Fact]
    public void VinSplitOverSeveralFrames_IsDetected()
    {
        // ISO-TP: first frame 10 14 + 3 header bytes + 3 data bytes, then consecutive frames 21..., 22...
        var vin = Hex("WVWZZZ1JZ3W386752");
        var lines = string.Join("\r",
            "7E8" + "1014" + "62F1A0" + vin[..6],
            "7E8" + "21" + vin[6..20],
            "7E8" + "22" + vin[20..]);
        Assert.Null(CollectionSanitizer.Sanitize(Ex("22F1A0", lines)));
    }

    [Fact]
    public void ContainsVin_IgnoresOrdinaryNumbers() =>
        Assert.False(CollectionSanitizer.ContainsVin("41 0C 1A F8 41 0D 3C 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00".Replace(" ", "")));

    [Fact]
    public void CorruptLines_AreDiscarded_ValidOnesKept()
    {
        var clean = CollectionSanitizer.Sanitize(Ex("010C", "\u0001@@##\r7E804410C1AF8\r?!?\r7E8ZZ"));
        Assert.NotNull(clean);
        Assert.Equal("7E804410C1AF8", clean!.Response);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\u0001\u0002\u0003")]
    [InlineData("zzzz")]
    public void OnlyGarbage_YieldsNothing(string response) =>
        Assert.Null(CollectionSanitizer.Sanitize(Ex("010C", response)));

    [Fact]
    public void KnownStatusWord_IsKept_ForUnsupportedRequests()
    {
        var clean = CollectionSanitizer.Sanitize(Ex("22F40C", "NO DATA"));
        Assert.NotNull(clean);
        Assert.Equal("NO DATA", clean!.Response);
    }

    [Fact]
    public void OddLengthHexLine_IsTreatedAsCorrupt() =>
        Assert.Null(CollectionSanitizer.Sanitize(Ex("010C", "7E804410C1AF")));

    [Fact]
    public void TooManyLines_IsRejected()
    {
        var response = string.Join("\r", Enumerable.Repeat("7E8044100BE3F", CollectionSanitizer.MaxLines + 1));
        Assert.Null(CollectionSanitizer.Sanitize(Ex("0100", response)));
    }

    [Fact]
    public void NullExchange_IsRejected() => Assert.Null(CollectionSanitizer.Sanitize(null!));
}

public sealed class CollectionQueueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gcar-queue-" + Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static QueuedExchange Item(string response, string model = "GOLF") =>
        new("VOLKSWAGEN", model, "2015", "CAN", new CollectedExchange("22F40C", response, 0x7E0, 0x7E8, false));

    [Fact]
    public void SameExchange_IsStoredOnce()
    {
        var queue = new CollectionQueue(PathFor("q.json"));
        Assert.True(queue.Add(Item("7E80562F40C1AF8")));
        Assert.False(queue.Add(Item("7E80562F40C1AF8")));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Capacity_DropsOldest()
    {
        var queue = new CollectionQueue(PathFor("q.json"));
        for (var i = 0; i < CollectionQueue.Capacity + 25; i++)
            queue.Add(Item($"7E8056 2F40C{i:X4}".Replace(" ", "")));

        Assert.Equal(CollectionQueue.Capacity, queue.Count);
        Assert.DoesNotContain(queue.Peek(5), q => q.Exchange.Response.EndsWith("0000"));
    }

    [Fact]
    public void Queue_SurvivesRestart_AndRemoveWorks()
    {
        var path = PathFor("q.json");
        var first = new CollectionQueue(path);
        first.Add(Item("7E80562F40C1111"));
        first.Add(Item("7E80562F40C2222"));

        var second = new CollectionQueue(path);
        Assert.Equal(2, second.Count);
        second.Remove(second.Peek(1));
        Assert.Equal(1, new CollectionQueue(path).Count);
    }

    [Fact]
    public void CorruptFile_IsIgnored_NotCrashing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor("q.json"), "{ this is not json \u0000\u0001");
        var queue = new CollectionQueue(PathFor("q.json"));
        Assert.Equal(0, queue.Count);
        Assert.True(queue.Add(Item("7E80562F40C1111")));
    }

    [Fact]
    public void Clear_RemovesEverythingIncludingTheFile()
    {
        var path = PathFor("q.json");
        var queue = new CollectionQueue(path);
        queue.Add(Item("7E80562F40C1111"));
        Assert.True(File.Exists(path));

        queue.Clear();
        Assert.Equal(0, queue.Count);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Intake_RequiresVehicle_AndSanitises()
    {
        var queue = new CollectionQueue(PathFor("q.json"));
        var good = new ObdRawExchange(0x7E0, 0x7E8, false, "22F40C", "7E80562F40C1AF8");
        var vin = new ObdRawExchange(0x7E0, 0x7E8, false, "0902", "7E8101449020157564E");

        Assert.False(CollectionIntake.TryAdd(queue, good, null, "GOLF", "2015", "CAN"));
        Assert.False(CollectionIntake.TryAdd(queue, good, "VOLKSWAGEN", "  ", "2015", "CAN"));
        Assert.False(CollectionIntake.TryAdd(queue, vin, "VOLKSWAGEN", "GOLF", "2015", "CAN"));
        Assert.True(CollectionIntake.TryAdd(queue, good, "VOLKSWAGEN", "GOLF", "2015", "CAN"));
        Assert.Equal(1, queue.Count);
    }
}
