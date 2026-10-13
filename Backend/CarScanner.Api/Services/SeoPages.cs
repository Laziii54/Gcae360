using System.Net;
using System.Text;
using System.Text.Json;

namespace CarScanner.Api.Services;

/// <summary>
/// Technical SEO for the public website: one list of the public, indexable pages and everything derived from it
/// (canonical / hreflang / Open Graph / Twitter tags, sitemap.xml, robots.txt). The public hostname is fixed on
/// purpose: a canonical URL must not follow whatever host header a request arrives with (the Azure default host name
/// and obd360.ge both reach this app).
/// </summary>
public static class SeoPages
{
    /// <summary>The one preferred public origin (https, no trailing slash).</summary>
    public const string CanonicalOrigin = "https://obd360.ge";

    /// <summary>Square brand image that exists in wwwroot/assets (512 x 512).</summary>
    public const string SocialImagePath = "/assets/icon-512.png";

    public sealed record Page(string Path, string Language, string Title, string Description);

    public static readonly Page Georgian = new("/", "ka",
        "OBD360 — ავტომობილის დიაგნოსტიკა ქართულად",
        "OBD2 დიაგნოსტიკა ტელეფონით, შეცდომის კოდები ქართულად, ყიდვისწინა შემოწმება და VIN ისტორია აუქციონის ფოტოებით.");

    public static readonly Page English = new("/en", "en",
        "OBD360 — OBD2 Car Diagnostic App",
        "OBD2 diagnostics on your phone, fault codes explained, pre-purchase checks and VIN history with auction photos.");

    /// <summary>Public pages written by hand elsewhere in the app; they get a description and a canonical link.</summary>
    public static readonly IReadOnlyList<Page> LegalPages = new[]
    {
        new Page("/privacy-policy", "en", "OBD360 — Privacy Policy / კონფიდენციალურობის პოლიტიკა",
            "Privacy policy of the OBD360 app and website: what data is processed, why, and how to ask for deletion. Also in Georgian."),
        new Page("/terms-of-use", "en", "OBD360 — Terms of Use",
            "Terms of use of the OBD360 diagnostic app: what the diagnostic information is and is not, and the rules for using the app."),
        new Page("/support", "en", "OBD360 — Support",
            "OBD360 support: how to get help, what to prepare before writing, and how to send purchase or data-deletion requests.")
    };

    /// <summary>Every URL that is listed in the sitemap: the two language homepages and the public legal / support pages.</summary>
    public static IReadOnlyList<string> SitemapPaths { get; } =
        new[] { Georgian.Path, English.Path }.Concat(LegalPages.Select(page => page.Path)).ToArray();

    public static string Absolute(string path) => path == "/" ? CanonicalOrigin + "/" : CanonicalOrigin + path;

    private static string E(string value) => WebUtility.HtmlEncode(value);

    /// <summary>Title, description, canonical, hreflang (ka / en / x-default), Open Graph and Twitter tags of a homepage.</summary>
    public static string HomeHead(bool english)
    {
        var page = english ? English : Georgian;
        var other = english ? Georgian : English;
        var sb = new StringBuilder();
        sb.Append("<title>").Append(E(page.Title)).Append("</title>\n");
        sb.Append("<meta name=\"description\" content=\"").Append(E(page.Description)).Append("\">\n");
        sb.Append("<link rel=\"canonical\" href=\"").Append(Absolute(page.Path)).Append("\">\n");
        // The two homepages are translations of each other, so the pair is reciprocal; "/" is the default-language page.
        sb.Append("<link rel=\"alternate\" hreflang=\"ka\" href=\"").Append(Absolute(Georgian.Path)).Append("\">");
        sb.Append("<link rel=\"alternate\" hreflang=\"en\" href=\"").Append(Absolute(English.Path)).Append("\">");
        sb.Append("<link rel=\"alternate\" hreflang=\"x-default\" href=\"").Append(Absolute(Georgian.Path)).Append("\">\n");
        sb.Append("<meta property=\"og:type\" content=\"website\"><meta property=\"og:site_name\" content=\"OBD360\">");
        sb.Append("<meta property=\"og:title\" content=\"").Append(E(page.Title)).Append("\">");
        sb.Append("<meta property=\"og:description\" content=\"").Append(E(page.Description)).Append("\">");
        sb.Append("<meta property=\"og:url\" content=\"").Append(Absolute(page.Path)).Append("\">");
        sb.Append("<meta property=\"og:locale\" content=\"").Append(english ? "en_US" : "ka_GE").Append("\">");
        sb.Append("<meta property=\"og:locale:alternate\" content=\"").Append(english ? "ka_GE" : "en_US").Append("\">");
        sb.Append("<meta property=\"og:image\" content=\"").Append(CanonicalOrigin + SocialImagePath).Append("\">");
        sb.Append("<meta property=\"og:image:width\" content=\"512\"><meta property=\"og:image:height\" content=\"512\">\n");
        sb.Append("<meta name=\"twitter:card\" content=\"summary\">");
        sb.Append("<meta name=\"twitter:title\" content=\"").Append(E(page.Title)).Append("\">");
        sb.Append("<meta name=\"twitter:description\" content=\"").Append(E(page.Description)).Append("\">");
        sb.Append("<meta name=\"twitter:image\" content=\"").Append(CanonicalOrigin + SocialImagePath).Append("\">");
        sb.Append("\n<script type=\"application/ld+json\">").Append(ApplicationJsonLd(english)).Append("</script>");
        _ = other;
        return sb.ToString();
    }

    /// <summary>
    /// schema.org MobileApplication data for the homepage, using only what the page itself says: the name, what the app does, that
    /// the basic diagnostics are free (the visible "0 ₾"), the page language and the brand image. Deliberately absent: ratings,
    /// reviews, download counts, store identifiers and store links (the app listing is not a verified public URL yet) and the
    /// iOS platform (the page offers Google Play only). Without ratings or reviews this is descriptive data, not a rich-result claim.
    /// </summary>
    public static string ApplicationJsonLd(bool english)
    {
        var page = english ? English : Georgian;
        var data = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "MobileApplication",
            ["name"] = "OBD360",
            ["description"] = page.Description,
            ["url"] = Absolute(page.Path),
            ["inLanguage"] = page.Language,
            ["applicationCategory"] = "UtilitiesApplication",
            ["operatingSystem"] = "Android",
            ["image"] = CanonicalOrigin + SocialImagePath,
            ["offers"] = new Dictionary<string, object> { ["@type"] = "Offer", ["price"] = "0", ["priceCurrency"] = "GEL" }
        };
        // The default encoder escapes < > & so the JSON can never close the script element.
        return JsonSerializer.Serialize(data);
    }

    /// <summary>
    /// Adds a description and a self-referencing canonical link to a hand-written page, right after its title.
    /// A page that already declares a canonical link or a description is left as it is.
    /// </summary>
    public static string WithMeta(string html, string path)
    {
        var page = LegalPages.FirstOrDefault(item => item.Path == path);
        if (page is null)
            return html;

        var close = html.IndexOf("</title>", StringComparison.OrdinalIgnoreCase);
        if (close < 0)
            return html;

        var add = new StringBuilder();
        if (!html.Contains("rel=\"canonical\"", StringComparison.OrdinalIgnoreCase))
            add.Append("\n    <link rel=\"canonical\" href=\"").Append(Absolute(path)).Append("\">");
        if (!html.Contains("name=\"description\"", StringComparison.OrdinalIgnoreCase))
            add.Append("\n    <meta name=\"description\" content=\"").Append(E(page.Description)).Append("\">");

        return add.Length == 0 ? html : html.Insert(close + "</title>".Length, add.ToString());
    }

    /// <summary>sitemap.xml: absolute canonical URLs only, with the language alternates of the two homepages. No lastmod values are invented.</summary>
    public static string SitemapXml()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">\n");
        foreach (var path in SitemapPaths)
        {
            sb.Append("  <url>\n    <loc>").Append(E(Absolute(path))).Append("</loc>\n");
            if (path == Georgian.Path || path == English.Path)
            {
                sb.Append("    <xhtml:link rel=\"alternate\" hreflang=\"ka\" href=\"").Append(Absolute(Georgian.Path)).Append("\"/>\n");
                sb.Append("    <xhtml:link rel=\"alternate\" hreflang=\"en\" href=\"").Append(Absolute(English.Path)).Append("\"/>\n");
                sb.Append("    <xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"").Append(Absolute(Georgian.Path)).Append("\"/>\n");
            }

            sb.Append("  </url>\n");
        }

        sb.Append("</urlset>\n");
        return sb.ToString();
    }

    /// <summary>robots.txt: the public site is open to crawlers; no private path is listed and no rule is invented.</summary>
    public static string RobotsTxt() =>
        "User-agent: *\nAllow: /\n\nSitemap: " + CanonicalOrigin + "/sitemap.xml\n";
}
