using System.Text.RegularExpressions;
using System.Xml.Linq;
using CarScanner.Api.Services;

namespace CarScanner.Tests;

/// <summary>
/// Technical SEO of the public website: metadata in the first HTML response, canonical and hreflang links, sitemap.xml and
/// robots.txt. These checks run the real page generators; they say nothing about whether a search engine indexes the site.
/// </summary>
public sealed class SeoTests
{
    private static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "CarScanner.csproj")))
                    return dir;
                dir = Path.GetDirectoryName(dir) ?? dir;
            }

            throw new DirectoryNotFoundException("repository root not found");
        }
    }

    private static string Home(bool english) => SitePages.HomePage(6.19m, true, english);

    private static int Count(string html, string pattern) => Regex.Matches(html, pattern).Count;

    [Theory]
    [InlineData(false, "OBD360 — ავტომობილის დიაგნოსტიკა ქართულად", "ka", "https://obd360.ge/")]
    [InlineData(true, "OBD360 — OBD2 Car Diagnostic App", "en", "https://obd360.ge/en")]
    public void EachHomepage_HasItsTitle_Language_AndSelfReferencingCanonical_InTheFirstResponse(bool english, string title, string lang, string canonical)
    {
        var html = Home(english);
        Assert.Contains("<html lang=\"" + lang + "\"", html);
        Assert.Contains("<title>" + title + "</title>", html);
        Assert.Equal(1, Count(html, "<title>"));
        Assert.Contains("<link rel=\"canonical\" href=\"" + canonical + "\">", html);
        Assert.Equal(1, Count(html, "rel=\"canonical\""));
        Assert.Contains("name=\"viewport\"", html);
        Assert.Contains("rel=\"icon\"", html);
        Assert.Matches("<h1[ >]", html);                                          // the page has a main heading
    }

    [Fact]
    public void TheDescriptions_AreInTheRightLanguage_AndNotEmpty()
    {
        var ka = Home(false);
        var en = Home(true);
        var kaDescription = Regex.Match(ka, "<meta name=\"description\" content=\"([^\"]+)\"").Groups[1].Value;
        var enDescription = Regex.Match(en, "<meta name=\"description\" content=\"([^\"]+)\"").Groups[1].Value;
        Assert.Matches("[ა-ჿ]", kaDescription);                         // Georgian letters
        Assert.DoesNotMatch("[ა-ჿ]", enDescription);
        Assert.InRange(kaDescription.Length, 50, 200);
        Assert.InRange(enDescription.Length, 50, 200);
        Assert.NotEqual(kaDescription, enDescription);
    }

    [Fact]
    public void Hreflang_IsReciprocal_AbsoluteHttps_AndHasADefault()
    {
        foreach (var english in new[] { false, true })
        {
            var html = Home(english);
            Assert.Contains("<link rel=\"alternate\" hreflang=\"ka\" href=\"https://obd360.ge/\">", html);
            Assert.Contains("<link rel=\"alternate\" hreflang=\"en\" href=\"https://obd360.ge/en\">", html);
            Assert.Contains("<link rel=\"alternate\" hreflang=\"x-default\" href=\"https://obd360.ge/\">", html);   // "/" is the default-language page
            Assert.DoesNotContain("hreflang=\"ka\" href=\"/\"", html);                                          // no relative leftovers
        }
    }

    [Fact]
    public void OpenGraphAndTwitterTags_UseThePageUrl_AndAnImageThatExists()
    {
        foreach (var english in new[] { false, true })
        {
            var html = Home(english);
            var url = english ? "https://obd360.ge/en" : "https://obd360.ge/";
            Assert.Contains("<meta property=\"og:url\" content=\"" + url + "\">", html);
            Assert.Contains("property=\"og:title\"", html);
            Assert.Contains("property=\"og:description\"", html);
            Assert.Contains("<meta property=\"og:locale\" content=\"" + (english ? "en_US" : "ka_GE") + "\">", html);
            Assert.Contains("name=\"twitter:card\" content=\"summary\"", html);
            Assert.Contains("name=\"twitter:title\"", html);

            var image = Regex.Match(html, "property=\"og:image\" content=\"([^\"]+)\"").Groups[1].Value;
            Assert.StartsWith("https://obd360.ge/assets/", image);
            var file = Path.Combine(Root, "Backend", "CarScanner.Api", "wwwroot", "assets", Path.GetFileName(image));
            Assert.True(File.Exists(file), "social image must exist: " + file);
        }
    }

    [Fact]
    public void NoPublicPage_IsAccidentallyNoindex()
    {
        foreach (var english in new[] { false, true })
        {
            var html = Home(english);
            Assert.DoesNotContain("noindex", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("nofollow", html, StringComparison.OrdinalIgnoreCase);
        }

        var program = File.ReadAllText(Path.Combine(Root, "Backend", "CarScanner.Api", "Program.cs"));
        foreach (var route in new[] { "/privacy-policy", "/terms-of-use", "/support" })
        {
            var start = program.IndexOf("app.MapGet(\"" + route + "\"", StringComparison.Ordinal);
            Assert.True(start >= 0, route);
            var end = program.IndexOf("app.MapGet(", start + 10, StringComparison.Ordinal);
            Assert.DoesNotContain("noindex", program.Substring(start, end - start), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheSitemap_IsWellFormedXml_WithOnlyAbsoluteCanonicalHttpsUrls()
    {
        var xml = SeoPages.SitemapXml();
        var document = XDocument.Parse(xml);                                      // throws when not well-formed
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = document.Descendants(ns + "loc").Select(item => item.Value).ToList();

        Assert.Equal(locs.Distinct().Count(), locs.Count);                         // no duplicates
        Assert.Contains("https://obd360.ge/", locs);
        Assert.Contains("https://obd360.ge/en", locs);
        Assert.All(locs, loc =>
        {
            Assert.StartsWith("https://obd360.ge", loc);
            Assert.DoesNotContain("?", loc);
            Assert.DoesNotContain("#", loc);
            Assert.DoesNotMatch("/(api|admin|health|account|web|r)(/|$)", loc);    // no API, admin, tools, tokens or forms
        });
        Assert.DoesNotContain("<lastmod>", xml);                                   // no invented dates
        Assert.Equal(SeoPages.SitemapPaths.Count, locs.Count);
    }

    [Fact]
    public void EverySitemapUrl_IsARealRoute_OfThePublicSite()
    {
        var program = File.ReadAllText(Path.Combine(Root, "Backend", "CarScanner.Api", "Program.cs"));
        foreach (var path in SeoPages.SitemapPaths)
        {
            var route = "\"" + path + "\"";
            Assert.True(program.Contains("app.MapGet(" + route, StringComparison.Ordinal) || Regex.IsMatch(program, @"app\.MapGet\(\s*" + Regex.Escape(route)), "no route for " + path);
        }

        Assert.Contains("app.MapGet(\"/sitemap.xml\"", program);
        Assert.Contains("application/xml", program);
        Assert.Contains("app.MapGet(\"/robots.txt\"", program);
        Assert.Contains("text/plain", program);
    }

    [Fact]
    public void RobotsTxt_KeepsThePublicSiteOpen_AndPointsToTheSitemap()
    {
        var robots = SeoPages.RobotsTxt();
        Assert.Contains("User-agent: *", robots);
        Assert.Contains("Allow: /", robots);
        Assert.Contains("Sitemap: https://obd360.ge/sitemap.xml", robots);
        Assert.DoesNotMatch(@"(?im)^\s*Disallow:\s*/\s*$", robots);                // never "Disallow: /"
        Assert.DoesNotContain("Disallow", robots);                                 // no invented restrictions (assets, CSS, JS stay crawlable)
    }

    [Fact]
    public void WithMeta_AddsADescriptionAndCanonicalOnce_ToTheHandWrittenPages()
    {
        const string page = "<html><head><title>T</title></head><body></body></html>";
        var once = SeoPages.WithMeta(page, "/support");
        Assert.Contains("<link rel=\"canonical\" href=\"https://obd360.ge/support\">", once);
        Assert.Contains("<meta name=\"description\" content=\"", once);
        Assert.Equal(once, SeoPages.WithMeta(once, "/support"));                    // idempotent
        Assert.Equal(page, SeoPages.WithMeta(page, "/not-a-public-page"));          // unknown paths are left alone

        var program = File.ReadAllText(Path.Combine(Root, "Backend", "CarScanner.Api", "Program.cs"));
        foreach (var route in new[] { "/privacy-policy", "/support", "/terms-of-use" })
            Assert.Contains("SeoPages.WithMeta(html, \"" + route + "\")", program);
    }

    [Theory]
    [InlineData(false, "ka", "https://obd360.ge/", "0 ₾")]
    [InlineData(true, "en", "https://obd360.ge/en", "0 ₾")]
    public void TheStructuredData_IsValidJson_Descriptive_AndMatchesTheVisiblePage(bool english, string lang, string url, string visibleFree)
    {
        var html = Home(english);
        var match = Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(match.Success);
        Assert.Equal(1, Count(html, "application/ld\\+json"));

        using var json = System.Text.Json.JsonDocument.Parse(match.Groups[1].Value);   // throws when not valid JSON
        var root = json.RootElement;
        Assert.Equal("https://schema.org", root.GetProperty("@context").GetString());
        Assert.Equal("MobileApplication", root.GetProperty("@type").GetString());
        Assert.Equal("OBD360", root.GetProperty("name").GetString());
        Assert.Equal(url, root.GetProperty("url").GetString());
        Assert.Equal(lang, root.GetProperty("inLanguage").GetString());
        Assert.Equal("Android", root.GetProperty("operatingSystem").GetString());     // the page offers Google Play only
        Assert.Equal("0", root.GetProperty("offers").GetProperty("price").GetString());
        Assert.Contains(visibleFree, html);                                          // the free price is visible on the page
        Assert.Contains("OBD360", html);

        // nothing is invented: no ratings, reviews, counts, store identifiers or store links
        foreach (var forbidden in new[] { "aggregateRating", "review", "ratingValue", "ratingCount", "downloadUrl", "installUrl", "softwareVersion", "play.google.com", "apple.com" })
            Assert.DoesNotContain(forbidden, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheHomepage_LinksToGooglePlay_WithACrawlableAnchor_AndInventsNoAppStoreLink()
    {
        foreach (var english in new[] { false, true })
        {
            var html = Home(english);
            Assert.Matches("<a [^>]*href=\"https://play\\.google\\.com/store/apps/details\\?id=com\\.gcar360\\.app\"", html);
            Assert.DoesNotContain("apps.apple.com", html);                            // no App Store listing exists in the repository
        }
    }

    [Fact]
    public void ThePublicOriginIsFixed_NotTakenFromTheRequestHost()
    {
        Assert.Equal("https://obd360.ge", SeoPages.CanonicalOrigin);
        Assert.Equal("https://obd360.ge/", SeoPages.Absolute("/"));
        Assert.Equal("https://obd360.ge/en", SeoPages.Absolute("/en"));
    }
}
