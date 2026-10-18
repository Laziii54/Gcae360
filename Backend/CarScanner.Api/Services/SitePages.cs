using System.Globalization;
using System.Net;

namespace CarScanner.Api.Services;

/// <summary>
/// The public OBD360 website: "/" (Georgian) and "/en" (English).
/// Flat, light design: neutral paper background, graphite text, the logo's
/// blue for actions and amber for PRO. No gradients or glow.
/// </summary>
public static class SitePages
{
    public const string PlayStoreUrl = "https://play.google.com/store/apps/details?id=com.gcar360.app";

    private static string Gel(decimal? value) =>
        (value ?? 0).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Google Search Console ownership tag (GCS_GOOGLE_SITE_VERIFICATION = the content value).</summary>
    private static string VerificationMeta()
    {
        var code = Environment.GetEnvironmentVariable("GCS_GOOGLE_SITE_VERIFICATION");
        if (string.IsNullOrWhiteSpace(code)) return "";
        code = code.Trim();
        var i = code.IndexOf("content=\"", StringComparison.OrdinalIgnoreCase);
        if (i >= 0) { code = code[(i + 9)..]; var j = code.IndexOf('"'); if (j > 0) code = code[..j]; }
        return "<meta name=\"google-site-verification\" content=\"" + WebUtility.HtmlEncode(code) + "\">";
    }

    // Line icons (24×24, stroke).
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["alert"] = """<path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4M12 17h.01"/>""",
        ["cpu"] = """<rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><path d="M9 1v3M15 1v3M9 20v3M15 20v3M20 9h3M20 14h3M1 9h3M1 14h3"/>""",
        ["activity"] = """<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>""",
        ["heart"] = """<path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 0 0-7.8 7.8l1 1.1L12 21l7.8-7.5 1-1.1a5.5 5.5 0 0 0 0-7.8z"/>""",
        ["leaf"] = """<path d="M11 20A7 7 0 0 1 9.8 6.1C15.5 5 17 4.5 19 2c1 2 2 4.2 2 8 0 5.5-4.8 10-10 10z"/><path d="M2 21c0-3 1.9-5.4 5.2-6"/>""",
        ["battery"] = """<rect x="2" y="7" width="16" height="10" rx="2"/><path d="M22 11v2M6 10v4M10 10v4"/>""",
        ["wrench"] = """<path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.8-3.8a6 6 0 0 1-7.9 7.9l-6.9 6.9a2.1 2.1 0 0 1-3-3l6.9-6.9a6 6 0 0 1 7.9-7.9z"/>""",
        ["flag"] = """<path d="M4 22V4a1 1 0 0 1 1-1h13l-3 5 3 5H5"/>""",
        ["plug"] = """<path d="m7 7 10 10-5 5V2l5 5L7 17"/>""",
        ["cart"] = """<circle cx="9" cy="21" r="1"/><circle cx="20" cy="21" r="1"/><path d="M1 1h4l2.7 13.4a2 2 0 0 0 2 1.6h9.7a2 2 0 0 0 2-1.6L23 6H6"/>""",
        ["spark"] = """<path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"/>""",
        ["chat"] = """<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>""",
        ["search"] = """<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>""",
        ["clock"] = """<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>""",
        ["bolt"] = """<path d="M13 2L4 14h7l-1 8 9-12h-7z"/>""",
        ["fuel"] = """<path d="M3 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18"/><path d="M2 22h14M6 7h6"/><path d="M15 9h2a2 2 0 0 1 2 2v6a1.5 1.5 0 0 0 3 0V8l-3-3"/>""",
        ["layers"] = """<path d="m12 2 10 5-10 5L2 7z"/><path d="m2 17 10 5 10-5M2 12l10 5 10-5"/>""",
        ["file"] = """<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M8 13h8M8 17h6"/>""",
        ["car"] = """<path d="M5 17h14M3 17v-4l2-5a2 2 0 0 1 1.9-1.4h10.2A2 2 0 0 1 19 8l2 5v4"/><circle cx="7" cy="17" r="2"/><circle cx="17" cy="17" r="2"/><path d="M3 13h18"/>""",
        ["play"] = """<path d="M5 3l14 9-14 9z"/>""",
    };

    private static string I(string name, int size = 22) =>
        $"<svg class=\"i\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{Icons[name]}</svg>";

    private const string Style = """
<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Noto+Sans+Georgian:wght@400;500;600;700;800&display=swap" rel="stylesheet">
<style>
:root{--bg:#f4f5f3;--surface:#ffffff;--panel:#e9ecea;--ink:#17191c;--ink2:#3f454b;--muted:#6b7177;--line:#dfe2df;
--blue:#1d5bd6;--blue-ink:#ffffff;--amber:#c97f00;--amber-soft:#f8ecd4;--dark:#1d2024;--ok:#2f7d4f;--r:10px}
@media (prefers-color-scheme: dark){:root:not([data-theme="light"]){color-scheme:dark;--bg:#131517;--surface:#1b1e21;--panel:#202428;--ink:#eceeef;--ink2:#c3c8cd;--muted:#8d949a;--line:#2c3136;--blue:#4c84ec;--amber:#e6a02a;--amber-soft:#2e2412;--dark:#0d0f11}}
:root[data-theme="dark"]{color-scheme:dark;--bg:#131517;--surface:#1b1e21;--panel:#202428;--ink:#eceeef;--ink2:#c3c8cd;--muted:#8d949a;--line:#2c3136;--blue:#4c84ec;--amber:#e6a02a;--amber-soft:#2e2412;--dark:#0d0f11}
*{box-sizing:border-box}html{scroll-behavior:smooth}
body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.65 "Noto Sans Georgian",system-ui,-apple-system,"Segoe UI",sans-serif;-webkit-font-smoothing:antialiased}
a{color:var(--blue)}
.c{max-width:1120px;margin:0 auto;padding-inline:20px}
h1,h2,h3{text-wrap:balance}
.i{flex:none}
/* nav */
.nav{position:sticky;top:0;z-index:20;background:var(--bg);border-bottom:1px solid var(--line)}
.nav .c{display:flex;align-items:center;gap:22px;height:62px}
.logo{display:flex;align-items:center;gap:10px;font-weight:800;font-size:18px;color:var(--ink);text-decoration:none}
.logo img{height:34px;width:auto;display:block}
.nav nav{display:flex;gap:22px;margin-left:auto}.nav nav a{color:var(--ink2);font-size:14.5px;text-decoration:none}.nav nav a:hover{color:var(--ink)}
.lang{display:flex;border:1px solid var(--line);border-radius:7px;overflow:hidden;font-size:13px;font-weight:600}
.lang a{padding:4px 9px;color:var(--muted);text-decoration:none}.lang a.on{background:var(--ink);color:var(--bg)}
.theme{display:grid;place-items:center;width:34px;height:34px;border-radius:8px;border:1px solid var(--line);background:var(--surface);color:var(--ink);cursor:pointer;padding:0}
.theme:hover{border-color:var(--ink2)}.theme .moon{display:none}
:root[data-theme="dark"] .theme .sun{display:none}:root[data-theme="dark"] .theme .moon{display:block}
@media (prefers-color-scheme: dark){:root:not([data-theme="light"]) .theme .sun{display:none}:root:not([data-theme="light"]) .theme .moon{display:block}}
.btn{display:inline-flex;align-items:center;gap:8px;padding:11px 18px;border-radius:8px;font-weight:700;font-size:15px;text-decoration:none;border:1px solid transparent}
.btn-p{background:var(--blue);color:var(--blue-ink)}.btn-p:hover{filter:brightness(.94)}
.btn-o{border-color:var(--line);color:var(--ink);background:var(--surface)}.btn-o:hover{border-color:var(--ink2)}
.nav .btn{padding:8px 14px;font-size:14px}
@media (max-width:860px){.nav nav{display:none}.nav .lang{margin-left:auto}}
@media (max-width:420px){.nav .btn{display:none}}
/* hero */
.hero .c{display:grid;grid-template-columns:1.05fr .95fr;gap:40px;align-items:center;padding-block:56px 64px}
.kicker{font-size:13.5px;font-weight:600;color:var(--muted);margin:0}
.hero h1{font-size:clamp(32px,4.4vw,50px);line-height:1.14;margin:12px 0 16px;font-weight:800;letter-spacing:-.01em}
.hero h1 span{color:var(--blue)}
.hero p.lead{font-size:18px;color:var(--ink2);max-width:32em;margin:0 0 26px}
.cta{display:flex;flex-wrap:wrap;gap:10px}
.small-facts{display:flex;flex-wrap:wrap;gap:6px 22px;margin-top:26px;color:var(--muted);font-size:14px}
.small-facts b{color:var(--ink)}
.stage{border-radius:18px;height:520px;position:relative;overflow:hidden}
.phone{position:absolute;width:236px;height:480px;border-radius:34px;background:#15171a;padding:9px;box-shadow:0 14px 30px -18px rgba(0,0,0,.45)}
.phone .scr{height:100%;border-radius:26px;overflow:hidden;background:#f4f6f8;color:#15171a;font-size:11.5px;position:relative}
.phone .notch{position:absolute;top:9px;left:50%;transform:translateX(-50%);width:78px;height:20px;background:#15171a;border-radius:0 0 12px 12px;z-index:2}
.p1{left:8%;top:22px}.p2{right:7%;top:24px}
.sbar{height:32px}
.vhero{margin:0 9px;border-radius:14px;padding:12px;background:#1d5bd6;color:#fff}
.vhero .t{font-weight:800;font-size:15px}.vhero .s{opacity:.85;font-size:10.5px}
.chips{display:flex;gap:5px;flex-wrap:wrap;margin:7px 0}.chips span{background:rgba(255,255,255,.18);border-radius:6px;padding:1px 6px;font-size:9.5px}
.vinbox{background:rgba(0,0,0,.2);border-radius:8px;padding:6px 8px;font-family:ui-monospace,Consolas,monospace;font-size:11px;letter-spacing:.05em}
.tiles{display:grid;grid-template-columns:1fr 1fr;gap:6px;margin:9px}
.tile{background:#fff;border-radius:11px;padding:8px}.tile b{display:block;font-size:15px}.tile small{color:#6b7177;font-size:9.5px}
.photo{margin:0 9px;height:130px;border-radius:12px;background:#c9ced3;display:grid;place-items:center;color:#fff;position:relative}
.photo:after{content:"1 / 23";position:absolute;right:7px;bottom:7px;background:rgba(0,0,0,.55);font-size:9.5px;padding:1px 6px;border-radius:6px}
.facts{margin:9px;background:#fff;border-radius:11px;padding:6px 9px}.facts div{display:flex;justify-content:space-between;padding:3px 0;border-bottom:1px solid #eef0f2}.facts div:last-child{border:0}.facts span{color:#6b7177}
.gwrap{display:grid;place-items:center;margin:2px 0 4px}
.dtc{margin:0 9px 6px;background:#fff;border-radius:10px;padding:7px 9px;display:flex;gap:8px;align-items:center}
.dtc .code{font-weight:800;font-size:11px;border-radius:6px;padding:1px 5px}.dtc .y{background:#fdf0cf;color:#9a6200}.dtc .r{background:#fbe1dc;color:#a8321e}
.live{display:grid;grid-template-columns:1fr 1fr 1fr;gap:5px;margin:7px 9px}.live div{background:#fff;border-radius:9px;padding:6px;text-align:center}.live b{display:block;font-size:13px}.live small{color:#6b7177;font-size:9px}
@media (max-width:960px){.hero .c{grid-template-columns:1fr;padding-top:36px}.stage{height:500px}}
@media (max-width:520px){.stage{height:450px}.phone{width:196px;height:410px}.p1{left:3%}.p2{right:3%}}
/* sections */
section{padding-block:72px;border-top:1px solid var(--line)}
.head{max-width:640px;margin:0 0 32px}
h2{font-size:clamp(26px,3vw,34px);line-height:1.2;margin:0 0 10px;letter-spacing:-.01em}
.head p{color:var(--muted);font-size:17px;margin:0}
.row{display:grid;grid-template-columns:repeat(4,1fr);gap:0;border:1px solid var(--line);border-radius:var(--r);background:var(--surface)}
.row div{padding:18px 20px;border-right:1px solid var(--line)}.row div:last-child{border:0}.row b{display:block;font-size:22px}.row span{color:var(--muted);font-size:14px}
@media (max-width:760px){.row{grid-template-columns:1fr 1fr}.row div:nth-child(2){border-right:0}.row div:nth-child(-n+2){border-bottom:1px solid var(--line)}}
.duo{display:grid;grid-template-columns:1fr 1fr;gap:16px}
.prod{border-radius:var(--r);padding:26px;border:1px solid var(--line);background:var(--surface)}
.prod .top{display:flex;justify-content:space-between;align-items:center;gap:10px;margin-bottom:10px;color:var(--blue)}
.prod .top small{color:var(--muted);font-size:13px;font-weight:600}
.prod h3{font-size:21px;margin:0 0 6px}.prod p{color:var(--muted);margin:0 0 14px}
ul.ck{list-style:none;padding:0;margin:0;display:grid;gap:7px}ul.ck li{padding-left:22px;position:relative;font-size:15px;color:var(--ink2)}
ul.ck li:before{content:"";position:absolute;left:2px;top:.55em;width:9px;height:5px;border-left:2px solid var(--ok);border-bottom:2px solid var(--ok);transform:rotate(-45deg)}
@media (max-width:820px){.duo{grid-template-columns:1fr}}
.steps{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}
.step{padding:22px;border-radius:var(--r);background:var(--surface);border:1px solid var(--line)}
.step b.n{display:inline-block;font-size:13px;color:var(--muted);font-weight:600;margin-bottom:6px}
.step h3{margin:0 0 6px;font-size:17px}.step p{margin:0;color:var(--muted);font-size:15px}
@media (max-width:820px){.steps{grid-template-columns:1fr}}
.feats{display:grid;grid-template-columns:repeat(3,1fr);gap:0;border-top:1px solid var(--line);border-left:1px solid var(--line)}
.feat{padding:22px;border-right:1px solid var(--line);border-bottom:1px solid var(--line);background:var(--surface)}
.feat .ic{color:var(--blue);margin-bottom:8px}.feat.pro .ic{color:var(--amber)}
.feat h3{font-size:16.5px;margin:0 0 5px;display:flex;align-items:center;gap:8px;flex-wrap:wrap}.feat p{margin:0;color:var(--muted);font-size:14.5px}
.tag{font-size:11px;font-weight:700;border-radius:5px;padding:1px 6px;background:var(--amber-soft);color:var(--amber)}
@media (max-width:980px){.feats{grid-template-columns:1fr 1fr}}@media (max-width:620px){.feats{grid-template-columns:1fr}}
.sub{display:flex;align-items:center;gap:10px;margin:40px 0 16px}.sub h3{margin:0;font-size:19px}
.tiers{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}
.tier{padding:24px;border-radius:var(--r);border:1px solid var(--line);background:var(--surface);display:flex;flex-direction:column}
.tier.hi{border:2px solid var(--ink)}
.tier .t{display:flex;justify-content:space-between;align-items:center;gap:8px;margin-bottom:6px}.tier h3{margin:0;font-size:18px}
.tier .pr{font-size:30px;font-weight:800;margin:4px 0 2px}.tier .pr small{font-size:14px;color:var(--muted);font-weight:500}
.tier p{color:var(--muted);margin:0 0 14px;font-size:14.5px}
.tier ul.ck{margin-bottom:18px;flex:1}
.tier .btn{justify-content:center}
.lbl{font-size:12px;font-weight:700;border-radius:5px;padding:2px 8px;background:var(--panel);color:var(--ink2)}
@media (max-width:900px){.tiers{grid-template-columns:1fr}}
.extra{display:flex;flex-wrap:wrap;gap:8px 28px;margin-top:18px;color:var(--ink2);font-size:15px}.extra b{color:var(--ink)}
.faq{max-width:800px}
details{border-bottom:1px solid var(--line);padding:16px 2px}
summary{cursor:pointer;font-weight:700;font-size:16.5px;list-style:none;display:flex;justify-content:space-between;gap:16px}
summary::-webkit-details-marker{display:none}summary:after{content:"+";color:var(--muted);font-size:20px;line-height:1}details[open] summary:after{content:"−"}
details p{color:var(--ink2);margin:8px 0 0}
.final{border-radius:14px;padding:36px;background:var(--dark);color:#f1f2f3;display:flex;justify-content:space-between;align-items:center;gap:24px;flex-wrap:wrap}
.final .fl{display:flex;align-items:center;gap:20px}.final .fl img{width:104px;height:auto;flex:none}
.final h2{color:#fff;margin:0 0 4px}.final p{color:#b9bec3;margin:0}
.final .btn-p{background:#fff;color:#17191c}
@media (max-width:620px){.final .fl img{display:none}}
footer{border-top:1px solid var(--line);padding-block:30px 44px;color:var(--muted);font-size:14px}
footer .c{display:flex;justify-content:space-between;gap:18px;flex-wrap:wrap;align-items:center}
footer nav{
    display:flex;
    flex-wrap:wrap;
    gap:10px 18px;
}

footer nav a{
    color:var(--muted);
    margin:0;
    text-decoration:none;
}

footer nav a:hover{
    color:var(--ink)
}

@media (max-width:520px){
    footer .c{
        flex-direction:column;
        align-items:flex-start;
        gap:14px;
    }

    footer nav{
        width:100%;
        gap:8px 14px;
    }
}footer nav a:hover{color:var(--ink)}
#boot{position:fixed;inset:0;z-index:100;background:var(--bg);display:grid;place-items:center;transition:opacity .4s ease,visibility .4s ease}
#boot img{width:min(180px,44vw);height:auto;display:block;animation:bootpulse 1.6s ease-in-out infinite}
@keyframes bootpulse{0%,100%{opacity:.75;transform:scale(.97)}50%{opacity:1;transform:scale(1)}}
#boot.done{opacity:0;visibility:hidden}
html.booting,html.booting body{overflow:hidden}
a:focus-visible,summary:focus-visible,.btn:focus-visible{outline:2px solid var(--blue);outline-offset:3px}
@media (prefers-reduced-motion:reduce){html{scroll-behavior:auto}#boot{transition:none}#boot img{animation:none}}
</style>
""";

    private const string BootHead = """
<script>try{var t=localStorage.getItem("gcs_theme");if(t==="dark"||t==="light")document.documentElement.setAttribute("data-theme",t);}catch(e){}</script>
<script>try{if(!sessionStorage.getItem("gcs_boot"))document.documentElement.classList.add("booting");}catch(e){document.documentElement.classList.add("booting");}</script>
""";

    private const string BootScript = """
<script>(function(){var b=document.getElementById("boot"),r=document.documentElement;if(!r.classList.contains("booting")){b.classList.add("done");return;}
function hide(){var wait=Math.max(0,2300-performance.now());setTimeout(function(){b.classList.add("done");r.classList.remove("booting");try{sessionStorage.setItem("gcs_boot","1")}catch(e){}},wait);}
if(document.readyState==="complete")hide();else window.addEventListener("load",hide);setTimeout(function(){b.classList.add("done");r.classList.remove("booting")},6000);})();
document.addEventListener("DOMContentLoaded",function(){var btn=document.getElementById("theme");if(!btn)return;btn.addEventListener("click",function(){var r=document.documentElement,cur=r.getAttribute("data-theme");if(!cur)cur=window.matchMedia("(prefers-color-scheme: dark)").matches?"dark":"light";var next=cur==="dark"?"light":"dark";r.setAttribute("data-theme",next);try{localStorage.setItem("gcs_theme",next)}catch(e){}});});</script>
""";

    public static string HomePage(decimal fullReportPrice, bool fullReportLive, bool english = false)
    {
        string T(string ka, string en) => english ? en : ka;
        string Li(string ka, string en) => $"<li>{T(ka, en)}</li>";
        string Feat(string icon, string ka, string en, string kaText, string enText, bool pro = false) =>
            $"<article class=\"feat{(pro ? " pro" : "")}\"><div class=\"ic\">{I(icon)}</div><h3>{T(ka, en)}{(pro ? " <span class=\"tag\">PRO</span>" : "")}</h3><p>{T(kaText, enText)}</p></article>";

        var obd = WebPaymentService.PriceFor(ProductCatalog.ObdMonthly);
        var vinPrice = WebPaymentService.PriceFor(ProductCatalog.VinMonthly);
        var fullPrice = WebPaymentService.PriceFor(ProductCatalog.FullMonthly);
        var single = WebPaymentService.PriceFor(ProductCatalog.VinSingle);
        var q = ProductCatalog.VinMonthlyQuota;
        var month = T("თვე", "month");

        var free = string.Concat(
            Feat("alert", "შეცდომის კოდები", "Fault codes", "წაიკითხე და წაშალე შეცდომები. თითოეულზე აღწერა, მიზეზები, სიმპტომები, სიმძიმე და რა შეამოწმო.", "Read and clear faults. Each code comes with a description, causes, symptoms, severity and what to check."),
            Feat("cpu", "ყველა მოდულის სკანირება", "All-module scan", "მანქანის ყველა კომპიუტერის (ECU) პოვნა და მათი შეცდომები ერთ სიაში.", "Finds every control unit (ECU) in the car and lists their faults in one place."),
            Feat("activity", "ცოცხალი მონაცემები", "Live data", "ბრუნები, ტემპერატურა, სიჩქარე და სენსორები რეალურ დროში, გრაფიკით და ჩაწერით.", "RPM, temperatures, speed and sensors in real time, with graphs and recording."),
            Feat("heart", "ჯანმრთელობის სკანი", "Health scan", "ერთი ღილაკით სრული შემოწმება, ქულა და ნაპოვნი პრობლემების სია.", "A full check with one tap, a score and a list of the problems found."),
            Feat("leaf", "ინსპექციის მზადყოფნა", "Inspection readiness", "Readiness მონიტორები, Mode 05/06 ტესტები და Freeze Frame.", "Readiness monitors, Mode 05/06 tests and freeze frame data."),
            Feat("clock", "გარბენის გადამოწმება", "Mileage check", "ადარებს მოდულებში შენახულ გარბენს. ეხმარება დაგრეხილი სპიდომეტრის პოვნაში.", "Compares mileage stored in different modules to help spot a rolled-back odometer."),
            Feat("battery", "აკუმულატორი", "Battery", "12V ძაბვა, მინიმუმი და მაქსიმუმი, ისტორია. ჰიბრიდებზე მაღალი ძაბვის ბატარეაც.", "12V voltage with min, max and history. High-voltage battery on hybrids where available."),
            Feat("wrench", "სერვისის განულება", "Service reset", "ზეთის და ტექმომსახურების ინდიკატორის განულება 150+ მოდელზე, ნაბიჯ-ნაბიჯ.", "Step-by-step oil and service light reset for 150+ models."),
            Feat("flag", "აჩქარების გაზომვა", "Performance", "0–100 და სხვა მონაკვეთები, ისტორია და ვიდეო მონაცემებით.", "0–100 and other runs, with history and video overlay."),
            Feat("plug", "ადაპტერის შემოწმება", "Adapter check", "ამოწმებს ადაპტერს, კავშირს და რას უჭერს მხარს შენი მანქანა.", "Tests the adapter, the connection and what your car supports."));

        var pro = string.Concat(
            Feat("cart", "ყიდვისწინა შემოწმება", "Pre-purchase check", "შეცდომები ყველა მოდულში, გარბენის შედარება, ბატარეა და მზადყოფნა. ბოლოს შეფასება.", "Faults in every module, mileage comparison, battery and readiness, with a final verdict.", true),
            Feat("spark", "AI დიაგნოსტიკა", "AI diagnostics", "აანალიზებს კოდებს და სენსორებს და მარტივად გიხსნის, რა ხდება.", "Looks at codes and sensor data and explains what is going on in plain words.", true),
            Feat("chat", "AI კონსულტაცია", "AI consultation", "ყიდვისწინა შემოწმების შედეგზე გასაგები შეფასება და რჩევები მყიდველისთვის.", "A clear assessment of the pre-purchase result, with advice for the buyer.", true),
            Feat("bolt", "ჰიბრიდის ბატარეის შემოწმება", "Hybrid battery check", "მაღალი ძაბვის ბატარეის მდგომარეობა: უჯრედების სხვაობა, ტემპერატურა, SOH და დატვირთვის ტესტი.", "High-voltage battery condition: cell spread, temperature, SOH and a load test.", true),
            Feat("fuel", "საწვავის ხარჯი", "Fuel economy", "მგზავრობის ანალიტიკა, ჩასხმები ლარებში და რეალური ხარჯი.", "Trip analytics, refuel log and real consumption tank to tank.", true),
            Feat("layers", "მოდულების მონაცემები", "Module data", "მწარმოებლის მოდულების მონაცემები, ნაწილის ნომრები და ვერსიები.", "Manufacturer module data, part numbers and software versions.", true));

        var fullTier = $"""
<div class="tier"><div class="t"><h3>{T("სრული ისტორია", "Full history")}</h3><span class="lbl">{(fullReportLive ? T("ერთჯერადი", "one-time") : T("მალე", "soon"))}</span></div>
<div class="pr">{Gel(fullReportPrice)} ₾<small> / VIN</small></div><p>{T("აშშ-ის ოფიციალური ტიტულის ბაზა (NMVTIS).", "Official US title database (NMVTIS).")}</p>
<ul class="ck">{Li("ტიტულის ჩანაწერები და გარბენი", "Title records and odometer readings")}{Li("Salvage, Flood, Rebuilt, Junk", "Salvage, Flood, Rebuilt, Junk brands")}{Li("სადაზღვევო ზარალის ჩანაწერები", "Insurance total-loss records")}{Li("ჩანაწერი თუ არ არის, გადახდას არ შემოგთავაზებთ", "No records, no payment offered")}</ul></div>
""";

        var home = english ? "/en" : "/";
        return $$"""
<!doctype html><html lang="{{(english ? "en" : "ka")}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
{{SeoPages.HomeHead(english)}}
<meta name="theme-color" content="#f4f5f3">{{VerificationMeta()}}
<link rel="icon" type="image/png" href="/assets/favicon.png"><link rel="icon" type="image/png" sizes="512x512" href="/assets/icon-512.png"><link rel="apple-touch-icon" href="/assets/apple-touch-icon.png">
{{BootHead}}<noscript><style>#boot{display:none}</style></noscript>
{{Style}}</head><body>
<div id="boot" aria-hidden="true"><img src="/assets/logo.png" alt="" width="180" height="132"></div>
{{BootScript}}

<header class="nav"><div class="c">
<a class="logo" href="{{home}}"><img src="/assets/logo.png" alt="" width="46" height="34"><span>OBD360</span></a>
<nav><a href="#obd">{{T("დიაგნოსტიკა", "Diagnostics")}}</a><a href="#vin">{{T("VIN შემოწმება", "VIN check")}}</a><a href="#prices">{{T("ფასები", "Pricing")}}</a><a href="#faq">{{T("კითხვები", "FAQ")}}</a></nav>
<div class="lang" aria-label="Language"><a href="/" class="{{(english ? "" : "on")}}" hreflang="ka">KA</a><a href="/en" class="{{(english ? "on" : "")}}" hreflang="en">EN</a></div>
<button class="theme" id="theme" type="button" aria-label="{{T("ღია / მუქი", "Light / dark")}}" title="{{T("ღია / მუქი", "Light / dark")}}"><svg class="sun" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="4.5"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg><svg class="moon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg></button>
<a class="btn btn-p" href="{{PlayStoreUrl}}">{{T("ჩამოტვირთვა", "Download")}}</a>
</div></header>

<div class="hero"><div class="c">
<div>
<p class="kicker">{{T("OBD2 სკანერი და VIN ისტორია ერთ აპში", "OBD2 scanner and VIN history in one app")}}</p>
<h1>{{T("იცოდე, რა სჭირს მანქანას <span>ყიდვამდეც და მერეც</span>", "Know what's wrong with a car <span>before and after you buy it</span>")}}</h1>
<p class="lead">{{T("შეაერთე OBD2 ადაპტერი და ტელეფონი აგიხსნის შეცდომებს, სენსორებს და მანქანის მდგომარეობას. ჩაწერე VIN და ნახე აუქციონის ფოტოები, დაზიანება და ისტორია.", "Plug in an OBD2 adapter and your phone explains fault codes, sensor readings and the car's condition. Enter a VIN to see auction photos, damage and history.")}}</p>
<div class="cta"><a class="btn btn-p" href="{{PlayStoreUrl}}">{{I("play", 15)}} Google Play</a><a class="btn btn-o" href="#prices">{{T("ფასები", "Pricing")}}</a></div>
<div class="small-facts"><span><b>0 ₾</b> {{T("ძირითადი დიაგნოსტიკა", "basic diagnostics")}}</span><span><b>70+</b> {{T("მარკა", "makes")}}</span><span><b>BOG / TBC</b> {{T("ბარათით გადახდა", "card payments")}}</span></div>
</div>
<div class="stage" aria-hidden="true">
<div class="phone p1"><div class="scr"><div class="notch"></div><div class="sbar"></div>
<div class="vhero"><div class="t">TOYOTA Camry</div><div class="s">2018 · 2.5L · Gasoline</div><div class="chips"><span>Sedan</span><span>FWD</span><span>Automatic</span></div><div class="vinbox">4T1B11HK5JU•••••</div></div>
<div class="tiles"><div class="tile"><small>{{T("აუქციონი", "Auctions")}}</small><b>2</b></div><div class="tile"><small>{{T("ფოტოები", "Photos")}}</small><b>23</b></div></div>
<div class="photo">{{I("car", 56)}}</div>
<div class="facts"><div><span>{{T("დაზიანება", "Damage")}}</span><b>Front end</b></div><div><span>{{T("გარბენი", "Odometer")}}</span><b>48 210 mi</b></div><div><span>{{T("ტიტული", "Title")}}</span><b>Salvage</b></div></div>
</div></div>
<div class="phone p2"><div class="scr"><div class="notch"></div><div class="sbar"></div>
<div style="padding:0 11px;font-weight:800;font-size:14px">{{T("ჯანმრთელობა", "Health")}}</div>
<div class="gwrap"><svg width="136" height="136" viewBox="0 0 120 120"><circle cx="60" cy="60" r="48" fill="none" stroke="#e1e4e7" stroke-width="11"/><circle cx="60" cy="60" r="48" fill="none" stroke="#2f7d4f" stroke-width="11" stroke-linecap="round" stroke-dasharray="259 302" transform="rotate(-90 60 60)"/><text x="60" y="58" text-anchor="middle" font-size="30" font-weight="800" fill="#15171a">86</text><text x="60" y="78" text-anchor="middle" font-size="10" fill="#6b7177">{{T("კარგი", "Good")}}</text></svg></div>
<div class="dtc"><span class="code y">P0171</span><span>{{T("საწვავის ნაკლებობა (ბანკი 1)", "System too lean (Bank 1)")}}</span></div>
<div class="dtc"><span class="code r">P0420</span><span>{{T("კატალიზატორის ეფექტურობა", "Catalyst efficiency low")}}</span></div>
<div class="live"><div><small>RPM</small><b>820</b></div><div><small>{{T("ძრავი", "Engine")}}</small><b>90°</b></div><div><small>{{T("ბატარეა", "Battery")}}</small><b>14.1V</b></div></div>
</div></div>
</div>
</div></div>

<section style="padding-block:28px;border-top:0"><div class="c"><div class="row">
<div><b>150+</b><span>{{T("სერვისის განულების ინსტრუქცია", "service reset guides")}}</span></div>
<div><b>70+</b><span>{{T("მარკა და მოტოციკლები", "makes, incl. motorcycles")}}</span></div>
<div><b>Copart · IAAI</b><span>{{T("აუქციონის ფოტოები VIN-ით", "auction photos by VIN")}}</span></div>
<div><b>KA · EN</b><span>{{T("ქართული და ინგლისური", "Georgian and English")}}</span></div>
</div></div></section>

<section><div class="c">
<div class="head"><h2>{{T("ორი ინსტრუმენტი ერთ აპში", "Two tools in one app")}}</h2><p>{{T("შეამოწმე მანქანა, რომელიც გიდგას, და მანქანა, რომლის ყიდვასაც აპირებ.", "Check the car you drive and the car you're about to buy.")}}</p></div>
<div class="duo">
<div class="prod"><div class="top">{{I("activity", 26)}}<small>{{T("საჭიროა ადაპტერი", "needs an adapter")}}</small></div><h3>{{T("OBD სკანერი", "OBD scanner")}}</h3><p>{{T("ELM327 Bluetooth ადაპტერით, პირდაპირ მანქანის კომპიუტერიდან.", "Reads directly from the car's computer through an ELM327 Bluetooth adapter.")}}</p>
<ul class="ck">{{Li("შეცდომები ყველა მოდულში და მათი წაშლა", "Faults in every module, and clearing them")}}{{Li("ცოცხალი სენსორები და გრაფიკები", "Live sensors and graphs")}}{{Li("ჯანმრთელობის ქულა, ბატარეა, ინსპექცია", "Health score, battery, inspection readiness")}}{{Li("AI დიაგნოსტიკა და ყიდვისწინა შემოწმება", "AI diagnostics and pre-purchase check")}}</ul></div>
<div class="prod"><div class="top">{{I("search", 26)}}<small>{{T("ადაპტერი არ სჭირდება", "no adapter needed")}}</small></div><h3>{{T("VIN შემოწმება", "VIN check")}}</h3><p>{{T("ჩაწერე 17 სიმბოლო ან დაასკანერე კამერით.", "Type the 17 characters or scan them with the camera.")}}</p>
<ul class="ck">{{Li("მანქანის მონაცემები და მწარმოებლის გაწვევები", "Vehicle specs and manufacturer recalls")}}{{Li("Copart / IAAI აუქციონის ფოტოები და დაზიანება", "Copart / IAAI auction photos and damage")}}{{Li("გარბენი, ტიტული, გაყიდვის ფასი", "Mileage, title, sale price")}}{{Li("აშშ-ის ოფიციალური ტიტულის ისტორია", "Official US title history")}}</ul></div>
</div></div></section>

<section><div class="c">
<div class="head"><h2>{{T("როგორ მუშაობს", "How it works")}}</h2></div>
<div class="steps">
<div class="step"><b class="n">{{T("ნაბიჯი 1", "Step 1")}}</b><h3>{{T("შეიძინე ადაპტერი", "Get an adapter")}}</h3><p>{{T("ELM327 Bluetooth ადაპტერი ერთდება OBD2 ბუდეში, ჩვეულებრივ საჭის ქვემოთ.", "An ELM327 Bluetooth adapter plugs into the OBD2 port, usually under the steering wheel.")}}</p></div>
<div class="step"><b class="n">{{T("ნაბიჯი 2", "Step 2")}}</b><h3>{{T("დააკავშირე", "Connect")}}</h3><p>{{T("აირჩიე მანქანის ტიპი და მარკა, ჩართე ანთება და დააკავშირე ადაპტერი.", "Pick the vehicle type and make, switch the ignition on and pair the adapter.")}}</p></div>
<div class="step"><b class="n">{{T("ნაბიჯი 3", "Step 3")}}</b><h3>{{T("ნახე შედეგი", "Read the results")}}</h3><p>{{T("შეცდომები, სენსორები და ქულა გასაგებ ენაზე, საჭიროებისას AI-ის ახსნით.", "Faults, sensors and a score in plain language, with an AI explanation when you need it.")}}</p></div>
</div></div></section>

<section id="obd"><div class="c">
<div class="head"><h2>{{T("დიაგნოსტიკა", "Diagnostics")}}</h2><p>{{T("ეს ფუნქციები უფასოა. საჭიროა მხოლოდ ადაპტერი.", "These features are free. All you need is an adapter.")}}</p></div>
<div class="feats">{{free}}</div>
<div class="sub"><span class="tag">PRO</span><h3>{{T("ღრმა ანალიზი და AI", "Deeper analysis and AI")}}</h3></div>
<div class="feats">{{pro}}</div>
</div></section>

<section id="vin"><div class="c">
<div class="head"><h2>{{T("VIN შემოწმება", "VIN check")}}</h2><p>{{T("აშშ-იდან შემოყვანილი მანქანების დიდი ნაწილი აუქციონზე გაიყიდა. VIN-ით ნახავ, რა დაზიანებით და რა ფასად.", "Many cars imported from the US were sold at auction. The VIN shows with what damage and for how much.")}}</p></div>
<div class="tiers">
<div class="tier"><div class="t"><h3>{{T("უფასოდ", "Free")}}</h3><span class="lbl">{{T("ყველასთვის", "everyone")}}</span></div><div class="pr">0 ₾</div><p>{{T("ყოველ VIN-ზე.", "For every VIN.")}}</p>
<ul class="ck">{{Li("მარკა, მოდელი, წელი, ძრავი", "Make, model, year, engine")}}{{Li("მწარმოებლის გაწვევები (NHTSA)", "Manufacturer recalls (NHTSA)")}}{{Li("ინტერნეტში ნაპოვნი ამ VIN-ის ფოტოები", "Photos of this VIN found online")}}{{Li("ფოტოების ძებნა Google-ში", "One-tap Google image search")}}</ul></div>
<div class="tier hi"><div class="t"><h3>{{T("აუქციონის ისტორია", "Auction history")}}</h3><span class="lbl">VIN PRO</span></div><div class="pr">{{Gel(vinPrice)}} ₾<small> / {{month}}</small></div><p>{{T($"თვეში {q} მანქანა, ან {Gel(single)} ₾ ერთზე.", $"{q} cars a month, or {Gel(single)} ₾ for one.")}}</p>
<ul class="ck">{{Li("Copart / IAAI-ის ყველა ფოტო", "All Copart / IAAI photos")}}{{Li("ძირითადი და მეორადი დაზიანება", "Primary and secondary damage")}}{{Li("გარბენი, ტიტული, გასაღები", "Odometer, title, keys")}}{{Li("ჩანაწერი თუ არ მოიძებნა, არ გეთვლება", "Not counted if no record is found")}}</ul></div>
{{fullTier}}
</div></div></section>

<section id="prices"><div class="c">
<div class="head"><h2>{{T("ფასები", "Pricing")}}</h2><p>{{T("გეგმა ანგარიშზეა მიბმული და ყველა შენს მოწყობილობაზე მუშაობს.", "Plans are tied to your account and work on all your devices.")}}</p></div>
<div class="tiers">
<div class="tier"><div class="t"><h3>OBD PRO</h3></div><div class="pr">{{Gel(obd)}} ₾<small> / {{month}}</small></div><p>{{T("ღრმა დიაგნოსტიკა და AI.", "Deeper diagnostics and AI.")}}</p>
<ul class="ck">{{Li("ყიდვისწინა შემოწმება", "Pre-purchase check")}}{{Li("AI დიაგნოსტიკა და კონსულტაცია", "AI diagnostics and consultation")}}{{Li("ჰიბრიდის ბატარეის შემოწმება", "Hybrid battery check")}}{{Li("საწვავის ხარჯი", "Fuel economy")}}{{Li("მოდულების მონაცემები, ანგარიშები", "Module data and reports")}}</ul><a class="btn btn-o" href="/pro">{{T("არჩევა", "Choose")}}</a></div>
<div class="tier hi"><div class="t"><h3>{{T("სრული PRO", "Full PRO")}}</h3><span class="lbl">{{T("ხელსაყრელი", "best value")}}</span></div><div class="pr">{{Gel(fullPrice)}} ₾<small> / {{month}}</small></div><p>{{T("OBD PRO და VIN PRO ერთად.", "OBD PRO and VIN PRO together.")}}</p>
<ul class="ck">{{Li("ყველაფერი OBD PRO-დან", "Everything in OBD PRO")}}{{Li($"თვეში {q} VIN-ის ისტორია", $"{q} VIN histories a month")}}{{Li("გახსნილი VIN ხელახლა უფასოდ", "Reopen checked VINs for free")}}{{Li("ყველა მოწყობილობაზე", "On all your devices")}}</ul><a class="btn btn-p" href="/pro">{{T("არჩევა", "Choose")}}</a></div>
<div class="tier"><div class="t"><h3>VIN PRO</h3></div><div class="pr">{{Gel(vinPrice)}} ₾<small> / {{month}}</small></div><p>{{T("მათთვის, ვინც მანქანას ეძებს.", "For people shopping for a car.")}}</p>
<ul class="ck">{{Li($"თვეში {q} VIN-ის ისტორია", $"{q} VIN histories a month")}}{{Li("აუქციონის ფოტოები და დაზიანება", "Auction photos and damage")}}{{Li("გარბენი და ტიტული", "Odometer and title")}}{{Li("დამატებითი VIN პაკეტები", "Extra VIN packs")}}</ul><a class="btn btn-o" href="/pro">{{T("არჩევა", "Choose")}}</a></div>
</div>
<div class="extra"><span>+5 VIN <b>{{Gel(WebPaymentService.PriceFor(ProductCatalog.VinPack5))}} ₾</b></span><span>+10 VIN <b>{{Gel(WebPaymentService.PriceFor(ProductCatalog.VinPack10))}} ₾</b></span><span>{{T("1 VIN ანგარიში", "Single VIN report")}} <b>{{Gel(single)}} ₾</b></span><span>{{T("დამატებით VIN-ებს ვადა არ გასდის", "Extra VINs never expire")}}</span></div>
</div></section>

<section id="faq"><div class="c">
<div class="head"><h2>{{T("ხშირი კითხვები", "Questions")}}</h2></div>
<div class="faq">
<details><summary>{{T("რომელ მანქანებზე მუშაობს?", "Which cars does it work with?")}}</summary><p>{{T("OBD2 სტანდარტის თითქმის ყველა მსუბუქ ავტომობილზე: აშშ-ში 1996 წლიდან, ევროპაში ბენზინზე 2001-დან და დიზელზე 2004-დან, ასევე ახალ იაპონურ, კორეულ და ჩინურ მოდელებზე.", "Almost every passenger car with OBD2: US cars from 1996, European petrol cars from 2001 and diesels from 2004, plus newer Japanese, Korean and Chinese models.")}}</p></details>
<details><summary>{{T("რომელი ადაპტერი მჭირდება?", "Which adapter do I need?")}}</summary><p>{{T("ELM327-ზე დაფუძნებული Bluetooth OBD2 ადაპტერი. სანდო მწარმოებლის ადაპტერი უფრო სტაბილურად მუშაობს. VIN შემოწმებას ადაპტერი არ სჭირდება.", "An ELM327-based Bluetooth OBD2 adapter. A well-made adapter is more stable. The VIN check doesn't need one.")}}</p></details>
<details><summary>{{T("უსაფრთხოა შეცდომების წაშლა?", "Is clearing codes safe?")}}</summary><p>{{T("წაშლა მხოლოდ თიშავს ნათურას. თუ პრობლემა არ გამოსწორდა, შეცდომა ისევ გამოჩნდება. სერიოზული შეცდომის დროს სჯობს მიმართოთ ხელოსანს.", "Clearing only turns the light off. If the problem is still there, the code comes back. For serious faults, see a mechanic first.")}}</p></details>
<details><summary>{{T("რატომ არ ჩანს ზოგ VIN-ზე ფოტოები?", "Why are there no photos for some VINs?")}}</summary><p>{{T("ფოტოები არსებობს მხოლოდ მაშინ, თუ მანქანა აუქციონზე იყიდებოდა ან ინტერნეტშია გამოქვეყნებული.", "Photos only exist if the car went through an auction or was published online. In that case the check isn't counted.")}}</p></details>
<details><summary>{{T("როგორ გადავიხადო?", "How do I pay?")}}</summary><p>{{T("აპში ან ვებგვერდზე ქართული ბანკის ბარათით (BOG ან TBC), ან Google Play-ით. ბარათის მონაცემებს ბანკის გვერდზე წერ და OBD360 მათ ვერ ხედავს.", "In the app or on the website with a Georgian bank card (BOG or TBC), or through Google Play. Card details are entered on the bank's page; OBD360 never sees them.")}}</p></details>
<details><summary>{{T("როგორ გავაუქმო გამოწერა?", "How do I cancel?")}}</summary><p>{{T("ყოველთვიური გადახდა უქმდება გვერდზე „ჩემი გამოწერა“ ან აპში — PRO რჩება გადახდილი პერიოდის ბოლომდე. ერთჯერადი გადახდა 30 დღე მოქმედებს და თავისით არ გრძელდება.", "Cancel the monthly payment on the My subscription page or in the app — PRO stays on until the paid period ends. A one-time payment lasts 30 days and doesn't renew.")}} <a href="/account/subscription">{{T("ჩემი გამოწერა", "My subscription")}}</a></p></details>
<details><summary>{{T("სად ინახება ჩემი მონაცემები?", "Where is my data kept?")}}</summary><p>{{T("დიაგნოსტიკის ისტორია შენს ტელეფონზე ინახება. დეტალები", "Your diagnostic history stays on your phone. Details are in the")}} <a href="/privacy-policy">{{T("კონფიდენციალურობის პოლიტიკაშია", "privacy policy")}}</a>.</p></details>
</div></div></section>

<section><div class="c"><div class="final">
<div class="fl"><img src="/assets/logo.png" alt="OBD360" width="130" height="95"><div><h2>{{T("სცადე უფასოდ", "Try it free")}}</h2><p>{{T("შეცდომები, სენსორები და VIN-ის მონაცემები გადახდის გარეშე.", "Fault codes, live data and VIN specs at no cost.")}}</p></div></div>
<a class="btn btn-p" href="{{PlayStoreUrl}}">{{I("play", 15)}} Google Play</a>
</div></div></section>

<footer><div class="c">
<a class="logo" href="{{home}}" style="font-size:15px"><img src="/assets/logo.png" alt="" width="38" height="28" style="height:28px"><span>OBD360</span></a>
<nav><a href="/pro">PRO</a><a href="/support">{{T("დახმარება", "Support")}}</a><a href="/privacy-policy">{{T("კონფიდენციალურობა", "Privacy")}}</a><a href="/terms-of-use">{{T("წესები", "Terms")}}</a></nav>
<span>© {{DateTime.UtcNow.Year}} OBD360</span>
</div></footer>
</body></html>
""";
    }
}
