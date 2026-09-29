using System.Collections;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace CarScanner.Tests;

public sealed class GeorgianDtcCatalogTests
{
    private static readonly Regex DtcCode = new(
        "^[PBCU][0-9A-F]{4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ForeignWritingSystem = new(
        "[\\u0400-\\u052F\\u0590-\\u08FF]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KnownBadMixedText = new(
        @"A general ხარვეზი|სარკინიგზო|kick[ -]?down|pull[ -]?down|limp mode|wiggle test|check engine|glow plug|misfire|wastegate|common-rail|checksum|gateway",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void BundledGeorgianCatalog_IsCompleteParseableAndFreeOfKnownMixedText()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "DtcCatalog", "ka", "generic");
        var files = Directory.GetFiles(root, "*.yaml").OrderBy(path => path).ToArray();
        Assert.Equal(8, files.Length);

        var deserializer = new DeserializerBuilder().Build();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueEnglish = new HashSet<string>(StringComparer.Ordinal);
        var translatedFieldCount = 0;

        foreach (var file in files)
        {
            using var reader = File.OpenText(file);
            var document = deserializer.Deserialize<object>(reader);
            Walk(document, codes, uniqueEnglish, ref translatedFieldCount);
        }

        Assert.Equal(13_626, codes.Count);
        Assert.Equal(67_074, translatedFieldCount);
        Assert.Equal(23_852, uniqueEnglish.Count);
    }

    private static void Walk(
        object? node,
        HashSet<string> codes,
        HashSet<string> uniqueEnglish,
        ref int translatedFieldCount)
    {
        switch (node)
        {
            case IDictionary<object, object> map:
                if (map.TryGetValue("code", out var rawCode))
                {
                    var code = Convert.ToString(rawCode) ?? string.Empty;
                    if (DtcCode.IsMatch(code))
                        Assert.True(codes.Add(code), $"Duplicate DTC code: {code}");
                }

                if (map.TryGetValue("en", out var rawEnglish) &&
                    map.TryGetValue("ka", out var rawGeorgian))
                {
                    var english = Convert.ToString(rawEnglish) ?? string.Empty;
                    var georgian = Convert.ToString(rawGeorgian) ?? string.Empty;

                    Assert.False(string.IsNullOrWhiteSpace(english));
                    Assert.False(string.IsNullOrWhiteSpace(georgian), $"Missing Georgian for: {english}");
                    Assert.DoesNotMatch(ForeignWritingSystem, georgian);
                    Assert.DoesNotMatch(KnownBadMixedText, georgian);

                    uniqueEnglish.Add(english.Trim());
                    translatedFieldCount++;
                }

                foreach (var value in map.Values)
                    Walk(value, codes, uniqueEnglish, ref translatedFieldCount);
                break;

            case IEnumerable sequence when node is not string:
                foreach (var value in sequence)
                    Walk(value, codes, uniqueEnglish, ref translatedFieldCount);
                break;
        }
    }
}
