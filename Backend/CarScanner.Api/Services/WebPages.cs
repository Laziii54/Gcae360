using System.Globalization;
using System.Net;

namespace CarScanner.Api.Services;

/// <summary>
/// The OBD360 PRO web page (plans, account, card payment) and the
/// payment result page. Georgian first, English underneath.
/// </summary>
public static class WebPages
{
    private static string Gel(string productId) =>
        (WebPaymentService.PriceFor(productId) ?? 0).ToString("0.00", CultureInfo.InvariantCulture) + " ₾";

    private const string Style = """
<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Noto+Sans+Georgian:wght@400;500;600;700;800&display=swap" rel="stylesheet">
<style>
:root{--bg:#f4f5f3;--card:#fff;--border:#dfe2df;--text:#17191c;--muted:#6b7177;--accent:#1d5bd6;--amber:#c97f00;--amber-soft:#f8ecd4;--ok:#2f7d4f;--err:#b3361f}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:16px/1.45 "Noto Sans Georgian",-apple-system,"Segoe UI",sans-serif}
main{max-width:560px;margin:0 auto;padding:20px 16px 48px}
h1{font-size:26px;margin:4px 0 2px;font-weight:800}h2{font-size:13px;margin:26px 0 10px;color:var(--muted);text-transform:uppercase;letter-spacing:.03em;font-weight:700}.muted{color:var(--muted);font-size:14px}
.card{background:var(--card);border:1px solid var(--border);border-radius:14px;padding:14px 16px;margin:10px 0;display:flex;gap:12px;align-items:flex-start;cursor:pointer}
.card input{margin-top:5px;accent-color:var(--accent)}.card .t{font-weight:700}.card .p{margin-left:auto;font-weight:700;white-space:nowrap}
.card.best{border-color:var(--accent)}.badge{display:inline-block;background:var(--accent);color:#fff;border-radius:8px;font-size:11px;padding:1px 8px;margin-left:6px;vertical-align:middle}
label.f{display:block;font-size:14px;color:var(--muted);margin:12px 0 4px}
input[type=email],input[type=password],input[type=text],select{width:100%;padding:12px;border-radius:10px;border:1px solid var(--border);background:var(--card);color:var(--text);font-size:16px}
button{width:100%;margin-top:18px;padding:14px;border:0;border-radius:12px;background:var(--accent);color:#fff;font-size:17px;font-weight:700;cursor:pointer}
button:disabled{opacity:.6}.msg{margin-top:12px;font-size:14px}.err{color:var(--err)}.ok{color:var(--ok)}
.smallbtn{width:auto;margin-top:10px;padding:9px 16px;font-size:13px;border-radius:9px;background:transparent;color:var(--accent);border:1.5px solid var(--accent);font-weight:700}
.smallbtn:disabled{opacity:.5;color:var(--muted);border-color:var(--border)}
.codewrap{margin-top:4px}
.hp{position:absolute;left:-9999px;top:-9999px;width:1px;height:1px;overflow:hidden}
footer{margin-top:28px;font-size:13px;color:var(--muted);text-align:center}footer a{color:var(--muted)}
.big{text-align:center;margin:30px 0 6px}.big svg{display:block;margin:0 auto}.center{text-align:center}
.grow{flex:1;min-width:0}.card .t{display:flex;flex-wrap:wrap;align-items:center;gap:4px 6px}.card .t .badge{margin-left:0}
.badge.ok{background:#e8f5ee;color:#1f8a4c;margin:3px 0 2px}.opt.on{border-color:var(--accent);box-shadow:0 0 0 1px var(--accent) inset}
.sum{background:var(--card);border:1px solid var(--border);border-radius:14px;padding:12px 16px;margin:10px 0}
.sum>div{display:flex;justify-content:space-between;gap:12px;padding:4px 0;font-size:14px}.sum span{color:var(--muted)}.sum b{text-align:right}
.sum .head{font-size:17px;align-items:center}.pill{font-size:12px;font-weight:700;border-radius:8px;padding:2px 9px}
.pill.ok{background:#e8f5ee;color:#1f8a4c}.pill.bad{background:#fdecea;color:#b3361f}.pill.off{background:var(--border);color:var(--muted)}
.consent{display:flex;gap:10px;align-items:flex-start;font-size:14px;margin:12px 0 0}.consent input{margin-top:4px;width:18px;height:18px;flex:none;accent-color:var(--accent)}
.consent a{color:var(--accent)}
.alert{border:1.5px solid #e9a99d;background:rgba(239,68,68,.08);border-radius:14px;padding:12px 14px;margin:12px 0;font-size:14px}.alert b{display:block;margin-bottom:2px}
a.btn{display:block;text-align:center;margin-top:12px;padding:13px;border-radius:12px;background:var(--accent);color:#fff;font-weight:700;text-decoration:none}
.ghost{background:transparent!important;color:var(--text)!important;border:1.5px solid var(--border)!important}
.danger{background:transparent;color:var(--err);border:1.5px solid var(--err)}
.dlg-bg{position:fixed;inset:0;background:rgba(0,0,0,.45);display:flex;align-items:center;justify-content:center;padding:16px}
.dlg{background:var(--card);border-radius:16px;padding:18px;max-width:380px;width:100%}.dlg .t{font-weight:700;font-size:17px}
.row2{display:grid;grid-template-columns:1fr 1fr;gap:10px}.row2 button{margin-top:6px}.dlg .danger{background:var(--err);color:#fff}
.legal{margin-top:8px}
[hidden]{display:none!important}
html.is-intl .ka,html.is-ge .en,html.is-intl .ka-only,html:not(.is-intl) .en-block,html.is-intl .gel,html:not(.is-intl) .usd{display:none}
.periodtoggle{display:flex;gap:8px;margin:0 0 12px}
.permbtn{flex:1;text-align:center;padding:9px 6px;background:var(--card);border:1.5px solid var(--border);border-radius:10px;cursor:pointer;font-size:13px;font-weight:700;color:var(--muted)}
.permbtn.active{border-color:var(--accent);background:#eaf1fc;color:var(--accent)}
.plantabs{display:flex;gap:8px;margin:0 0 12px;position:relative}
.tabbtn{flex:1;min-width:0;text-align:center;padding:11px 6px;background:var(--card);border:1.5px solid var(--border);border-radius:12px;cursor:pointer;font-size:13px;font-weight:700;color:var(--muted);position:relative;display:block}
.tabbtn.active{border-color:var(--accent);background:#eaf1fc;color:var(--accent)}
.tabbtn .save{position:absolute;top:-8px;right:4px;font-size:10px;font-weight:700;color:var(--amber);background:var(--amber-soft);border-radius:6px;padding:1px 6px}
.plandetail{background:var(--card);border:1.5px solid var(--accent);border-radius:14px;padding:16px;margin:0 0 16px}
.vh{position:absolute;opacity:0;width:1px;height:1px;pointer-events:none}
.pd-top{display:flex;justify-content:space-between;align-items:flex-start;gap:10px}
.pd-top .dtitle{font-size:16px;font-weight:700}.pd-top .dsub{font-size:12px;color:var(--muted);margin-top:2px}
.pd-price{text-align:right;white-space:nowrap}.pd-price .anchor{font-size:11px;color:#9aa0a6;text-decoration:line-through}
.pd-price .now{font-size:18px;font-weight:800;color:var(--accent)}
ul.ck{list-style:none;padding:0;margin:10px 0 0;display:grid;gap:6px}ul.ck li{padding-left:20px;position:relative;font-size:12.5px;color:var(--text)}
ul.ck li:before{content:"";position:absolute;left:1px;top:.5em;width:8px;height:4.5px;border-left:2px solid var(--ok);border-bottom:2px solid var(--ok);transform:rotate(-45deg)}
</style>
""";

    /// <summary>Seller details shown in the footer (bank requirement). Set on the server:
    /// Site__LegalName, Site__LegalId, Site__LegalAddress.</summary>
    public sealed record LegalInfo(string Name, string? Id, string? Address)
    {
        public static LegalInfo From(IConfiguration config)
        {
            static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            return new LegalInfo(
                Clean(config["Site:LegalName"]) ?? "ი/მ ზურაბ ლაზაშვილი",
                Clean(config["Site:LegalId"]),
                Clean(config["Site:LegalAddress"]) ?? "თბილისი, საქართველო");
        }

        public string FooterHtml()
        {
            // The registration/personal ID is deliberately never shown publicly.
            var parts = new List<string> { WebUtility.HtmlEncode(Name) };
            if (Address is not null) parts.Add(WebUtility.HtmlEncode(Address));
            parts.Add("<a href=\"mailto:obd360support@gmail.com\">obd360support@gmail.com</a>");
            return "<div class=\"legal\">" + string.Join(" · ", parts) + "</div>";
        }
    }

    private const string Footer = """
<footer><a href="/about">ჩვენს შესახებ · About us</a> · <a href="/terms-of-use">გამოყენების პირობები · Terms</a> · <a href="/privacy-policy">კონფიდენციალურობა · Privacy</a> · <a href="/account/subscription">ჩემი გამოწერა · My subscription</a> · <a href="/support">დახმარება · Support</a>
""";

    /// <summary>Text in both languages; the page shows Georgian in Georgia and English elsewhere.</summary>
    private static string T(string ka, string en) => $"<span class=\"ka\">{ka}</span><span class=\"en\">{en}</span>";

    /// <summary>Price in lari and in dollars; the page shows one of them.</summary>
    private static string Prices(string productId) =>
        $"<span class=\"gel\">{Gel(productId)}</span><span class=\"usd\">{WebPaymentService.FormatPrice(WebPaymentService.UsdPriceFor(productId) ?? 0, "USD")}</span>";

    public static string ProPage(IReadOnlyList<string> providers, bool autoRenewAvailable, LegalInfo legal)
    {
        string Plan(string id, string title, string titleEn, string ka, string en, bool best = false, bool check = false) => $"""
<label class="card{(best ? " best" : "")}" data-sub="{(ProductCatalog.IsSubscription(id) ? "1" : "0")}" data-gel="{Gel(id)}" data-usd="{WebPaymentService.FormatPrice(WebPaymentService.UsdPriceFor(id) ?? 0, "USD")}"><input type="radio" name="productId" value="{id}"{(check ? " checked" : "")}>
<div class="grow"><div class="t">{T(title, titleEn)}{(best ? $"<span class=\"badge\">{T("საუკეთესო", "Best value")}</span>" : "")}</div><div class="muted">{T(ka, en)}</div></div>
<div class="p">{Prices(id)}</div></label>
""";

        string Li(string ka, string en) => $"<li>{T(ka, en)}</li>";

        // A tab button (always visible, just highlighted when active) plus its
        // own detail panel (shown only for the active one). Both stay in the DOM;
        // JS (syncTabs, below) toggles .active / [hidden] on the form's change event.
        // baseKey identifies the plan ("obd"/"vin"/"full") across both billing
        // periods, so JS can keep the same plan selected when the period toggle
        // switches ("obd"+"month" <-> "obd"+"year"); key ("obd-month") is the
        // unique DOM id.
        (string Tab, string Detail) PlanTab(string id, string baseKey, string period, string title, string titleEn, string subKa, string subEn, string featuresHtml, bool check = false)
        {
            var key = $"{baseKey}-{period}";
            var days = ProductCatalog.PeriodDaysFor(id);
            var anchor = WebPaymentService.AnchorPriceFor(id);
            var price = WebPaymentService.PriceFor(id);
            var save = anchor is { } a && a > 0 && price is { } p
                ? $"<span class=\"save\">-{(int)Math.Round((1 - p / a) * 100)}%</span>"
                : "";
            var anchorHtml = anchor is { } a2
                ? $"<div class=\"anchor\">{a2.ToString("0.##", CultureInfo.InvariantCulture)} ₾</div>"
                : "";
            // The radio lives next to the (always-visible) tab label, never inside
            // the collapsible detail panel — a hidden ancestor would stop label-for
            // clicks from reaching it in some browsers.
            var tab = $"""
<span class="pmtab" data-period="{period}" style="display:contents"><input type="radio" id="p-{key}" name="productId" value="{id}" class="vh"{(check ? " checked" : "")}><label class="tabbtn{(check ? " active" : "")}" for="p-{key}" data-key="{key}" data-basekey="{baseKey}" data-period="{period}" data-days="{days}" data-sub="{(ProductCatalog.IsSubscription(id) ? "1" : "0")}" data-gel="{Gel(id)}" data-usd="{WebPaymentService.FormatPrice(WebPaymentService.UsdPriceFor(id) ?? 0, "USD")}">{T(title, titleEn)}{save}</label></span>
""";
            var detail = $"""
<div class="plandetail" id="d-{key}"{(check ? "" : " hidden")}>
<div class="pd-top"><div><div class="dtitle">{T(title, titleEn)}</div><div class="dsub">{T(subKa, subEn)}</div></div>
<div class="pd-price">{anchorHtml}<div class="now">{Prices(id)}</div></div></div>
{featuresHtml}</div>
""";
            return (tab, detail);
        }

        var hasBog = providers.Contains("bog");
        var bankOptions = string.Join("", providers.Select(p =>
            $"<option value=\"{p}\">{(p == "bog" ? "საქართველოს ბანკი (BOG)" : "თიბისი (TBC)")}</option>"));
        var bankSelect = providers.Count > 1
            ? $"<div class=\"ka-only\"><label class=\"f\" for=\"bank\">ბანკი</label><select name=\"provider\" id=\"bank\">{bankOptions}</select></div>"
            : $"<input type=\"hidden\" name=\"provider\" id=\"bank\" value=\"{providers.FirstOrDefault() ?? "tbc"}\">";

        var unavailable = providers.Count == 0
            ? $"<p class=\"msg err\">{T("გადახდა დროებით მიუწვდომელია. სცადე მოგვიანებით.", "Payments are temporarily unavailable. Please try again later.")}</p>"
            : !hasBog
                ? $"<p class=\"msg err en-block\">Card payments from outside Georgia are temporarily unavailable.</p>"
                : "";

        var renewal = autoRenewAvailable ? $$"""
<div id="renew">
<h2>{{T("გადახდის სახე", "Payment type")}}</h2>
<label class="card opt"><input type="radio" name="mode" value="auto" checked>
<div class="grow"><div class="t">{{T("ავტომატური განახლება", "Auto-renew")}}</div><span class="badge ok">{{T("რეკომენდებული", "Recommended")}}</span>
<div class="muted" id="autoSub"></div></div></label>
<label class="card opt"><input type="radio" name="mode" value="once">
<div class="grow"><div class="t" id="onceLabel"></div>
<div class="muted">{{T("ბარათი არ ინახება. ვადის ბოლოს თავად გაანახლებ.", "Your card is not saved. Renew it yourself when it ends.")}}</div></div></label>
<div class="sum" id="sum"><div><span>{{T("დღეს გადაიხდი", "Due today")}}</span><b id="sToday"></b></div><div id="sNextRow"><span>{{T("შემდეგი ჩამოჭრა", "Next charge")}}</span><b id="sNext"></b></div></div>
<label class="consent" id="consentRow"><input type="checkbox" id="consent"> <span><span id="consentPre"></span> <b id="sAmount"></b> <span id="consentPost"></span> <a href="/terms-of-use" target="_blank">{{T("პირობები", "Terms")}}</a></span></label>
</div>
""" : "";

        var obdFeatures = $"<ul class=\"ck\">{Li("ყველა მოდულის სკანირება (ABS, SRS, ტრანსმისია…)", "Scan every module (ABS, SRS, transmission…)")}{Li("AI დიაგნოსტიკა და კონსულტაცია", "AI diagnostics and consultation")}{Li("ყიდვისწინა შემოწმება", "Pre-purchase check")}</ul>";
        var vinFeatures = $"<ul class=\"ck\">{Li($"VIN: სრული ისტორია, {ProductCatalog.VinMonthlyQuota} ყოველთვე", $"VIN: full history, {ProductCatalog.VinMonthlyQuota} every month")}{Li("აუქციონის ფოტოები და დაზიანებები", "Auction photos and damage")}{Li("გარბენი და ტიტულის სტატუსი", "Mileage and title status")}</ul>";
        var fullFeatures = $"<ul class=\"ck\">{Li("PRO-ის ყველა ფუნქცია", "Everything in PRO")}{Li("VIN PRO-ის ყველა ფუნქცია", "Everything in VIN PRO")}</ul>";

        var obdTabM = PlanTab(ProductCatalog.ObdMonthly, "obd", "month", "OBD PRO", "OBD PRO", "მანქანის სრული დიაგნოსტიკა", "Full car diagnostics", obdFeatures);
        var obdTabY = PlanTab(ProductCatalog.ObdYearly, "obd", "year", "OBD PRO", "OBD PRO", "მანქანის სრული დიაგნოსტიკა", "Full car diagnostics", obdFeatures);
        var vinTabM = PlanTab(ProductCatalog.VinMonthly, "vin", "month", "VIN PRO", "VIN PRO", "მანქანის ისტორია VIN კოდით", "Car history by VIN", vinFeatures);
        var vinTabY = PlanTab(ProductCatalog.VinYearly, "vin", "year", "VIN PRO", "VIN PRO", "მანქანის ისტორია VIN კოდით", "Car history by VIN", vinFeatures);
        var fullTabM = PlanTab(ProductCatalog.FullMonthly, "full", "month", "სრული PRO", "Full PRO", "OBD PRO და VIN PRO ერთად", "OBD PRO and VIN PRO together", fullFeatures, check: true);
        var fullTabY = PlanTab(ProductCatalog.FullYearly, "full", "year", "სრული PRO", "Full PRO", "OBD PRO და VIN PRO ერთად", "OBD PRO and VIN PRO together", fullFeatures);

        var note = autoRenewAvailable
            ? T("გადახდის გაუქმება შეგიძლია ნებისმიერ დროს გვერდზე „ჩემი გამოწერა“ ან აპში. გადახდილი პერიოდი რჩება.",
                "Cancel the payment any time on “My subscription” or in the app. The paid period is kept.")
            : T("ერთჯერადი გადახდა. ავტომატური ჩამოჭრა არ ხდება — გეგმა 30 დღე მოქმედებს, ხელახალი ყიდვა ვადას აგრძელებს.",
                "One-time payment, no automatic renewal. A plan lasts 30 days; buying again extends it.");

        return $$"""
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>OBD360 PRO</title><link rel="icon" type="image/png" href="/assets/favicon.png?v=2"><link rel="apple-touch-icon" href="/assets/apple-touch-icon.png?v=2">{{Style}}
<script>
// Lari for Georgia, US dollars everywhere else (charged in USD by the bank).
(function(){var tz='';try{tz=Intl.DateTimeFormat().resolvedOptions().timeZone||'';}catch(e){}
var langs=(navigator.languages||[navigator.language||'']).map(function(l){return String(l).toLowerCase();});
var ge=tz==='Asia/Tbilisi'||langs.some(function(l){return l.indexOf('ka')===0;});
if(location.hash==='#usd')ge=false;
var h=document.documentElement;h.classList.add(ge?'is-ge':'is-intl');h.lang=ge?'ka':'en';})();
</script></head><body><main>
<a class="logo" href="/" style="display:flex;align-items:center;gap:8px;text-decoration:none;color:var(--text);font-weight:800;font-size:15px;margin-bottom:10px"><img src="/assets/logo.png?v=2" alt="" width="25" height="25"><span>OBD360</span></a>
<h1>OBD360 PRO</h1>
<div class="muted">{{T("აირჩიე გეგმა და ჩართე შენს OBD360 ანგარიშზე", "Choose a plan for your OBD360 account")}}</div>
{{unavailable}}
<form id="f">
<h2>{{T("გეგმები", "Plans")}}</h2>
<div class="periodtoggle">
<input type="radio" id="per-m" name="period" value="month" class="vh" checked><label for="per-m" id="lbl-m" class="permbtn active">{{T("თვიური", "Monthly")}}</label>
<input type="radio" id="per-y" name="period" value="year" class="vh"><label for="per-y" id="lbl-y" class="permbtn">{{T("წლიური", "Yearly")}}</label>
</div>
<div class="plantabs">{{obdTabM.Tab}}{{vinTabM.Tab}}{{fullTabM.Tab}}{{obdTabY.Tab}}{{vinTabY.Tab}}{{fullTabY.Tab}}</div>
{{obdTabM.Detail}}{{vinTabM.Detail}}{{fullTabM.Detail}}{{obdTabY.Detail}}{{vinTabY.Detail}}{{fullTabY.Detail}}
<h2>{{T("დამატებითი VIN", "Extra VIN checks")}}</h2>
<div class="muted">{{T("+5 და +10 პაკეტი VIN PRO / სრული PRO გეგმისთვისაა. ვადა არ გასდის.", "+5 and +10 packs are for VIN PRO / Full PRO and never expire.")}}</div>
{{Plan(ProductCatalog.VinPack5, "+5 VIN", "+5 VIN", "5 დამატებითი VIN ისტორია", "5 extra VIN histories")}}
{{Plan(ProductCatalog.VinPack10, "+10 VIN", "+10 VIN", "10 დამატებითი VIN ისტორია", "10 extra VIN histories")}}
{{Plan(ProductCatalog.VinSingle, "1 VIN ანგარიში", "1 VIN report", "გეგმის გარეშე", "No plan needed")}}
<h2>{{T("შენი ანგარიში", "Your account")}}</h2>
<div class="muted">{{T("იგივე ელ-ფოსტა, რომლითაც აპში ხარ რეგისტრირებული. პაროლი არ გჭირდება — ერთჯერად კოდს გამოგიგზავნით.", "The e-mail you registered in the app with. No password needed — we'll e-mail you a one-time code.")}}</div>
<label class="f" for="email">{{T("ელ-ფოსტა", "E-mail")}}</label><input type="email" id="email" name="email" autocomplete="email" required>
<div class="hp" aria-hidden="true"><label for="hpWebsite">Website</label><input type="text" id="hpWebsite" name="website" tabindex="-1" autocomplete="off"></div>
<button type="button" id="sendCode" class="smallbtn">{{T("კოდის გამოგზავნა", "Send code")}}</button>
<div id="codeStatus" class="msg"></div>
<div id="codeFieldWrap" class="codewrap" hidden>
<label class="f" for="code">{{T("კოდი ელფოსტიდან", "Code from your e-mail")}}</label>
<input type="text" id="code" name="code" inputmode="numeric" pattern="[0-9]*" maxlength="6" autocomplete="one-time-code" placeholder="123456">
<button type="button" id="resendCode" class="smallbtn" disabled>{{T("ხელახლა გაგზავნა", "Resend code")}}</button>
</div>
{{bankSelect}}
{{renewal}}
<button id="pay" type="submit"{{(providers.Count == 0 ? " disabled" : "")}}>{{T("გადახდა ბარათით", "Pay by card")}}</button>
<div id="m" class="msg"></div>
<p class="muted" id="noteP">{{note}}</p>
<p class="muted">{{T("გადახდა ხდება საქართველოს ბანკის დაცულ გვერდზე. ბარათის ნომერს OBD360 ვერ ხედავს.", "You pay on Bank of Georgia's secure page. OBD360 never sees your card number. Prices in US dollars.")}} Visa · Mastercard · Apple Pay · Google Pay</p>
</form>
{{Footer}}{{legal.FooterHtml()}}</footer>
</main>
<script>
const f=document.getElementById('f'),m=document.getElementById('m'),b=document.getElementById('pay');
const renew=document.getElementById('renew'),bank=document.getElementById('bank');
const intl=document.documentElement.classList.contains('is-intl');
const tr=(ka,en)=>intl?en:ka;
// The app links here with ?email=... so only the code is left to request.
(function(){var e=new URLSearchParams(location.search).get('email');if(e)document.getElementById('email').value=e;})();
const errors={INVALID_CODE:tr('კოდი არასწორია ან ვადაგასულია. გამოაგზავნე ახალი და სცადე ისევ.','The code is wrong or has expired. Send a new one and try again.'),
EMAIL_NOT_VERIFIED:tr('ჯერ დაადასტურე ელფოსტა OBD360 აპლიკაციაში (ანგარიში), შემდეგ სცადე ხელახლა.','Verify your email in the OBD360 app (Account) first, then try again.'),
VIN_PLAN_REQUIRED:tr('+5 და +10 პაკეტი მხოლოდ VIN PRO ან სრული PRO გეგმითაა.','Packs need VIN PRO or Full PRO.'),
AUTO_RENEW_UNAVAILABLE:tr('ყოველთვიური გადახდა ახლა ვერ ჩაირთო. აირჩიე „ერთჯერადი“.','Monthly payment is unavailable right now. Choose one-time.'),
NOT_AVAILABLE:tr('გადახდა დროებით მიუწვდომელია.','Payments are temporarily unavailable.'),
NO_ACCOUNT:tr('ამ ელფოსტაზე OBD360 ანგარიში ვერ მოიძებნა. ჯერ დარეგისტრირდი აპში.','No OBD360 account found for this e-mail. Register in the app first.'),
TOO_MANY:tr('ძალიან ბევრი მცდელობა. სცადე 15 წუთში.','Too many attempts. Try again in 15 minutes.')};
const sendCodeBtn=document.getElementById('sendCode'),resendCodeBtn=document.getElementById('resendCode'),
codeStatus=document.getElementById('codeStatus'),codeFieldWrap=document.getElementById('codeFieldWrap'),
emailInput=document.getElementById('email'),codeInput=document.getElementById('code'),
hpWebsite=document.getElementById('hpWebsite');
let resendTimer=null;
function startResendCooldown(seconds){
if(resendTimer)clearInterval(resendTimer);
let remaining=seconds;
const tick=()=>{if(remaining<=0){resendCodeBtn.disabled=false;resendCodeBtn.textContent=tr('ხელახლა გაგზავნა','Resend code');clearInterval(resendTimer);return;}
resendCodeBtn.disabled=true;resendCodeBtn.textContent=tr('ხელახლა გაგზავნა (','Resend (')+remaining+tr(')',')');remaining--;};
tick();resendTimer=setInterval(tick,1000);}
async function requestCode(isResend){
const email=emailInput.value.trim();
if(!email){codeStatus.className='msg err';codeStatus.textContent=tr('ჯერ შეიყვანე ელ-ფოსტა.','Enter your e-mail first.');return;}
const btn=isResend?resendCodeBtn:sendCodeBtn;btn.disabled=true;
codeStatus.className='msg';codeStatus.textContent=tr('იგზავნება…','Sending…');
try{
const r=await fetch('/api/web/checkout/code',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email,website:hpWebsite.value})});
const j=await r.json().catch(()=>({}));
if(j.success){
codeFieldWrap.hidden=false;sendCodeBtn.hidden=true;codeInput.focus();
codeStatus.className='msg ok';codeStatus.textContent=tr('კოდი გამოგზავნილია შენს ელფოსტაზე.','Code sent to your e-mail.');
startResendCooldown(60);
}else if(j.code==='COOLDOWN'||j.code==='HOURLY_LIMIT'){
codeFieldWrap.hidden=false;sendCodeBtn.hidden=true;
codeStatus.className='msg err';
codeStatus.textContent=j.code==='HOURLY_LIMIT'?tr('ძალიან ბევრი მცდელობა ამ საათში. სცადე მოგვიანებით.','Too many attempts this hour. Try again later.'):tr('კოდი უკვე გამოგზავნილია — დაელოდე და მერე ხელახლა სცადე.','A code was already sent — wait, then try again.');
startResendCooldown(j.retryAfterSeconds||60);
}else{
btn.disabled=false;
codeStatus.className='msg err';
codeStatus.textContent=errors[j.code]||tr('კოდის გაგზავნა ვერ მოხერხდა. სცადე ხელახლა.','Could not send the code. Please try again.');
}
}catch(e){btn.disabled=false;codeStatus.className='msg err';codeStatus.textContent=tr('კავშირის შეცდომა.','Connection error.');}
}
sendCodeBtn.addEventListener('click',()=>requestCode(false));
resendCodeBtn.addEventListener('click',()=>requestCode(true));
const AUTO_RENEW_AVAILABLE={{(autoRenewAvailable ? "true" : "false")}};
function selected(){return f.querySelector('input[name=productId]:checked');}
function period(){const r=f.querySelector('input[name=period]:checked');return r?r.value:'month';}
// Keeps all plan tabs for the active period visible; only the selected one is
// highlighted and shows its detail panel. A VIN-pack radio (no "p-" id) clears
// all of them, so the tabs never look "stuck" — any can still be clicked back.
function syncTabs(){const per=period();
document.getElementById('lbl-m').classList.toggle('active',per==='month');
document.getElementById('lbl-y').classList.toggle('active',per==='year');
document.querySelectorAll('.pmtab').forEach(s=>{s.hidden=(s.dataset.period!==per);});
const p=selected();const key=p&&p.id.indexOf('p-')===0?p.id.slice(2):null;
document.querySelectorAll('.plandetail').forEach(x=>{x.hidden=(x.id!=='d-'+key);});
document.querySelectorAll('.tabbtn').forEach(t=>{t.classList.toggle('active',t.dataset.key===key);});}
function auto(){const r=f.querySelector('input[name=mode]:checked');return !!renew&&!renew.hidden&&r&&r.value==='auto';}
function noteText(days){return AUTO_RENEW_AVAILABLE
?tr('გადახდის გაუქმება შეგიძლია ნებისმიერ დროს გვერდზე „ჩემი გამოწერა“ ან აპში. გადახდილი პერიოდი რჩება.','Cancel the payment any time on "My subscription" or in the app. The paid period is kept.')
:tr('ერთჯერადი გადახდა. ავტომატური ჩამოჭრა არ ხდება — გეგმა '+days+' დღე მოქმედებს, ხელახალი ყიდვა ვადას აგრძელებს.','One-time payment, no automatic renewal. A plan lasts '+days+' days; buying again extends it.');}
function update(){const p=selected();const card=p&&(document.querySelector('label[for="'+(p.id||'')+'"]')||p.closest('[data-sub]'));
const days=card&&card.dataset.days?parseInt(card.dataset.days,10):30;
const noteEl=document.getElementById('noteP');if(noteEl)noteEl.textContent=noteText(days);
if(!renew)return;const sub=card&&card.dataset.sub==='1';
const bogOk=intl||!bank||bank.value==='bog';renew.hidden=!(sub&&bogOk);
const price=card?(intl?card.dataset.usd:card.dataset.gel):'';
document.getElementById('sToday').textContent=price;document.getElementById('sAmount').textContent=price;
document.getElementById('onceLabel').textContent=tr('ერთჯერადი · '+days+' დღე','One-time · '+days+' days');
document.getElementById('autoSub').textContent=tr('ავტომატურად ყოველ '+days+' დღეში. გააუქმებ ნებისმიერ დროს.','Charged automatically every '+days+' days. Cancel any time.');
document.getElementById('consentPre').textContent=tr('ვეთანხმები, რომ ბარათი შეინახება საქართველოს ბანკთან და ყოველ '+days+' დღეში ჩამოიჭრება','I agree that my card is saved by Bank of Georgia and charged');
document.getElementById('consentPost').textContent=tr('გაუქმებამდე.','every '+days+' days until I cancel.');
const d=new Date();d.setDate(d.getDate()+(days-1));
document.getElementById('sNext').textContent=intl?d.toLocaleDateString('en-US',{day:'numeric',month:'short',year:'numeric'}):String(d.getDate()).padStart(2,'0')+'.'+String(d.getMonth()+1).padStart(2,'0')+'.'+d.getFullYear();
const a=auto();document.getElementById('sNextRow').hidden=!a;document.getElementById('consentRow').hidden=!a;
f.querySelectorAll('.opt').forEach(o=>o.classList.toggle('on',o.querySelector('input').checked));}
f.addEventListener('change',e=>{
if(e.target&&e.target.name==='period'){
const cur=selected();const curLbl=cur&&cur.id.indexOf('p-')===0?document.querySelector('label[for="'+cur.id+'"]'):null;
const baseKey=curLbl&&curLbl.dataset.basekey;
if(baseKey){const target=document.querySelector('.tabbtn[data-basekey="'+baseKey+'"][data-period="'+period()+'"]');
if(target)document.getElementById(target.getAttribute('for')).checked=true;} }
syncTabs();update();});
syncTabs();update();
f.addEventListener('submit',async e=>{e.preventDefault();
if(codeFieldWrap.hidden||!codeInput.value.trim()){m.className='msg err';m.textContent=tr('ჯერ გამოაგზავნე და შეიყვანე კოდი ელფოსტიდან.','Send and enter the e-mail code first.');return;}
if(auto()&&!document.getElementById('consent').checked){m.className='msg err';m.textContent=tr('მონიშნე თანხმობა ყოველთვიურ გადახდაზე.','Please tick the box to agree to the monthly payment.');return;}
m.className='msg';m.textContent=tr('მიმდინარეობს…','Please wait…');b.disabled=true;
const d=Object.fromEntries(new FormData(f));d.autoRenew=auto();d.currency=intl?'USD':'GEL';if(intl)d.provider='bog';delete d.mode;
try{const r=await fetch('/api/web/checkout',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(d)});
const j=await r.json().catch(()=>({}));
if(j.redirectUrl){location.href=j.redirectUrl;return;}
m.className='msg err';m.textContent=errors[j.code]||tr('გადახდა ვერ დაიწყო. სცადე ხელახლა.','Could not start the payment. Please try again.');}
catch{m.className='msg err';m.textContent=tr('კავშირის შეცდომა.','Connection error.');}
b.disabled=false;});
</script>
</body></html>
""";
    }

    /// <summary>"My subscription": status, payment history, cancel and pay-now.</summary>
    public static string SubscriptionPage(LegalInfo legal) => $$"""
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>OBD360 · ჩემი გამოწერა</title><link rel="icon" type="image/png" href="/assets/favicon.png?v=2"><link rel="apple-touch-icon" href="/assets/apple-touch-icon.png?v=2">{{Style}}</head><body><main>
<a class="logo" href="/" style="display:flex;align-items:center;gap:8px;text-decoration:none;color:var(--text);font-weight:800;font-size:15px;margin-bottom:10px"><img src="/assets/logo.png?v=2" alt="" width="25" height="25"><span>OBD360</span></a>
<h1>ჩემი გამოწერა</h1>
<div class="muted">My subscription · OBD360 PRO</div>
<form id="login">
<label class="f">ელ-ფოსტა · E-mail</label><input type="email" name="email" autocomplete="email" required>
<label class="f">პაროლი · Password</label><input type="password" name="password" autocomplete="current-password" required>
<button type="submit">შესვლა · Sign in</button>
</form>
<div id="view" style="display:none"></div>
<div id="m" class="msg"></div>
<div class="dlg-bg" id="dlg" style="display:none"><div class="dlg">
<div class="t">გავაუქმო გამოწერა? · Cancel?</div>
<p class="muted" id="dlgText"></p>
<div class="row2"><button type="button" class="ghost" id="keep">დატოვება · Keep</button><button type="button" class="danger" id="confirm">გაუქმება · Cancel</button></div>
</div></div>
{{Footer}}{{legal.FooterHtml()}}</footer>
</main>
<script>
const names={'com.georgiancarscanner.pro.monthly':'OBD PRO','com.georgiancarscanner.vin.monthly':'VIN PRO','com.georgiancarscanner.full.monthly':'სრული PRO',
'com.georgiancarscanner.vin.pack5':'+5 VIN','com.georgiancarscanner.vin.pack10':'+10 VIN','com.georgiancarscanner.vin.single':'1 VIN'};
const statusText={paid:'გადახდილი',failed:'ვერ ჩამოიჭრა',refunded:'დაბრუნებული'};
const v=document.getElementById('view'),m=document.getElementById('m'),login=document.getElementById('login'),dlg=document.getElementById('dlg');
let creds=null,last=null;
const esc=s=>String(s??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const day=s=>{if(!s)return '—';const d=new Date(s);return String(d.getDate()).padStart(2,'0')+'.'+String(d.getMonth()+1).padStart(2,'0')+'.'+d.getFullYear();};
const money=(a,c)=>a==null?'—':(c==='USD'?'$'+Number(a).toFixed(2):Number(a).toFixed(2)+' ₾');
async function call(path){const r=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(creds)});
const j=await r.json().catch(()=>({}));if(!r.ok||!j.success)throw j.code||'ERR';return j;}
function render(s){last=s;login.style.display='none';v.style.display='';
if(!s.hasSubscription){v.innerHTML='<div class="sum"><div><span>ყოველთვიური გამოწერა არ გაქვს. · No monthly subscription.</span></div></div><a class="btn" href="/pro">PRO გეგმები · Plans</a>'+hist(s);return;}
const name=esc(names[s.productId]||'PRO');
const st=s.status==='active'?'<span class="pill ok">აქტიური</span>':s.status==='past_due'?'<span class="pill bad">გადაუხდელი</span>':'<span class="pill off">გაუქმებული</span>';
let h='';
if(s.paymentFailed)h+='<div class="alert"><b>გადახდა ვერ მოხერხდა · Payment failed</b>ბარათიდან თანხა ვერ ჩამოიჭრა. ხელახლა ვცდით '+day(s.nextChargeAt)+'-ს. PRO მოქმედებს '+day(s.activeUntil)+'-მდე.</div>';
h+='<div class="sum"><div class="head"><b>'+name+'</b>'+st+'</div>'
+'<div><span>ფასი · Price</span><b>'+money(s.amount,s.currency)+' / თვე · mo</b></div>'
+(s.autoRenew?'<div><span>'+(s.paymentFailed?'შემდეგი ცდა · Next try':'შემდეგი ჩამოჭრა · Next charge')+'</span><b>'+day(s.nextChargeAt)+'</b></div>'
:'<div><span>მოქმედებს · Active until</span><b>'+day(s.activeUntil)+'</b></div>')
+(s.cardMask?'<div><span>ბარათი · Card</span><b>'+esc(s.cardMask)+'</b></div>':'')+'</div>';
if(s.paymentFailed)h+='<button type="button" id="retry">ახლავე გადახდა · Pay now · '+money(s.amount,s.currency)+'</button>';
if(s.autoRenew)h+='<a class="btn ghost" href="/pro">სხვა ბარათით გაგრძელება · Use another card</a><button type="button" class="danger" id="cancel">გამოწერის გაუქმება · Cancel subscription</button>'
+'<p class="muted">გაუქმების შემდეგ PRO მოქმედებს გადახდილი პერიოდის ბოლომდე. თანხა აღარ ჩამოიჭრება. · PRO stays on until the paid period ends.</p>';
else h+='<p class="muted">ავტომატური გადახდა გამორთულია. · Automatic payment is off.</p><a class="btn" href="/pro">ხელახლა ჩართვა · Subscribe again</a>';
v.innerHTML=h+hist(s);
const c=document.getElementById('cancel');if(c)c.onclick=()=>{document.getElementById('dlgText').textContent=name.replace(/&amp;/g,'&')+' დარჩება '+day(s.activeUntil)+'-მდე. ამის მერე თანხა აღარ ჩამოიჭრება და შენახული ბარათი წაიშლება.';dlg.style.display='';};
const r=document.getElementById('retry');if(r)r.onclick=async()=>{r.disabled=true;m.className='msg';m.textContent='მიმდინარეობს… · Please wait…';
try{render(await call('/api/web/subscription/retry'));m.textContent='';}catch(e){m.className='msg err';m.textContent=e==='TOO_MANY'?'ძალიან ბევრი ცდა. სცადე მოგვიანებით.':'ვერ მოხერხდა. სცადე მოგვიანებით.';r.disabled=false;} };}
function hist(s){if(!s.history||!s.history.length)return '';
return '<h2>გადახდების ისტორია · Payments</h2><div class="sum">'+s.history.map(x=>'<div><span>'+day(x.at)+' · '+esc(names[x.productId]||'')+'</span><b>'+money(x.amount,x.currency)+' · '+(statusText[x.status]||esc(x.status))+'</b></div>').join('')+'</div>';}
login.addEventListener('submit',async e=>{e.preventDefault();creds=Object.fromEntries(new FormData(login));m.className='msg';m.textContent='მიმდინარეობს… · Please wait…';
try{render(await call('/api/web/subscription'));m.textContent='';}catch(e){m.className='msg err';m.textContent=e==='INVALID_LOGIN'?'ელ-ფოსტა ან პაროლი არასწორია. · Wrong e-mail or password.':'კავშირის შეცდომა. · Connection error.';} });
document.getElementById('keep').onclick=()=>dlg.style.display='none';
document.getElementById('confirm').onclick=async()=>{dlg.style.display='none';
try{render(await call('/api/web/subscription/cancel'));m.className='msg ok';m.textContent='გამოწერა გაუქმდა. · Subscription cancelled.';}catch{m.className='msg err';m.textContent='ვერ გაუქმდა. სცადე ხელახლა ან მოგვწერე.';} };
</script>
</body></html>
""";

    /// <summary>
    /// Public "delete my account" page (Google Play data-deletion URL):
    /// how to delete in the app, or here with e-mail + password.
    /// <paramref name="formToken"/> is a short-lived anti-forgery token
    /// the endpoint checks.
    /// </summary>
    public static string DeleteAccountPage(LegalInfo legal, string formToken) => $$"""
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="index,follow">
<title>OBD360 · ანგარიშის წაშლა · Delete account</title><link rel="icon" type="image/png" href="/assets/favicon.png?v=2"><link rel="apple-touch-icon" href="/assets/apple-touch-icon.png?v=2">{{Style}}</head><body><main>
<a class="logo" href="/" style="display:flex;align-items:center;gap:8px;text-decoration:none;color:var(--text);font-weight:800;font-size:15px;margin-bottom:10px"><img src="/assets/logo.png?v=2" alt="" width="25" height="25"><span>OBD360</span></a>
<h1>ანგარიშის წაშლა</h1>
<div class="muted">Delete your OBD360 account</div>

<h2>აპში · In the app</h2>
<div class="sum">
<div><span>გახსენი OBD360 → <b>ანგარიში</b> → <b>ანგარიშის წაშლა</b> და დაადასტურე.</span></div>
<div><span>Open OBD360 → <b>Account</b> → <b>Delete account</b> and confirm.</span></div>
</div>

<h2>აქ, ვებ-გვერდზე · Here on the website</h2>
<p class="muted">შეიყვანე ანგარიშის ელ-ფოსტა და პაროლი. წაშლა სამუდამოა და ვერ აღდგება. · Enter the account's e-mail and password. Deletion is permanent and cannot be undone.</p>
<form id="del">
<input type="hidden" name="token" value="{{WebUtility.HtmlEncode(formToken)}}">
<label class="f">ელ-ფოსტა · E-mail</label><input type="email" name="email" autocomplete="email" required maxlength="254">
<label class="f">პაროლი · Password</label><input type="password" name="password" autocomplete="current-password" required maxlength="128">
<label class="consent"><input type="checkbox" name="confirm" required><span>ვადასტურებ, რომ მინდა ანგარიშის და მისი მონაცემების სამუდამოდ წაშლა. · I confirm that I want to permanently delete the account and its data.</span></label>
<button type="submit" class="danger" id="go">ანგარიშის წაშლა · Delete account</button>
</form>
<div id="m" class="msg"></div>

<h2>რა იშლება · What is deleted</h2>
<div class="sum">
<div><span>ანგარიში (ელ-ფოსტა, პაროლის ჰეში), სესიები, PRO უფლებები, VIN კრედიტები და გახსნილი VIN ისტორიები, ნაყიდი სრული ისტორიები, შენ მიერ შექმნილი QR ანგარიშები. რეიტინგის შედეგებში სახელი იცვლება „Deleted“-ით, VIN-ის ჰეში, GPS კოორდინატები და ვიდეოები იშლება. მოწყობილობის, გარბენის და სერვისის ტექნიკური ჩანაწერები ანგარიშს აღარ უკავშირდება.</span></div>
<div><span>Account (e-mail, password hash), sessions, PRO entitlements, VIN credits and opened VIN histories, purchased full histories and the QR reports you created. Leaderboard results are renamed “Deleted”; the VIN hash, GPS coordinates and videos are removed. Device, odometer and service technical records are unlinked from the account.</span></div>
<div><span>ბარათით გადახდების ჩანაწერები (თანხა, თარიღი, შეკვეთის ნომერი) ბუღალტრული მიზნით ინახება ანონიმურად; ავტომატური გადახდა უქმდება და შენახული ბარათი იშლება. Google Play / App Store გამოწერა ცალკე გააუქმე მაღაზიაში.</span></div>
<div><span>Card payment records (amount, date, order number) are kept anonymised for accounting; automatic payment is cancelled and the saved card is deleted. Cancel a Google Play / App Store subscription separately in the store.</span></div>
</div>
<p class="muted">კითხვები · Questions: <a href="mailto:obd360support@gmail.com">obd360support@gmail.com</a></p>
{{Footer}}{{legal.FooterHtml()}}</footer>
</main>
<script>
const f=document.getElementById('del'),m=document.getElementById('m'),go=document.getElementById('go');
f.addEventListener('submit',async e=>{e.preventDefault();
if(!confirm('წავშალო ანგარიში სამუდამოდ? · Delete the account permanently?'))return;
const d=Object.fromEntries(new FormData(f));go.disabled=true;m.className='msg';m.textContent='მიმდინარეობს… · Please wait…';
try{const r=await fetch('/api/web/account/delete',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email:d.email,password:d.password,token:d.token})});
const j=await r.json().catch(()=>({}));
if(r.ok&&j.success){f.style.display='none';m.className='msg ok';m.textContent='ანგარიში წაიშალა. · The account was deleted.';return;}
m.className='msg err';
m.textContent=j.code==='INVALID_LOGIN'?'ელ-ფოსტა ან პაროლი არასწორია. · Wrong e-mail or password.'
:j.code==='TOO_MANY'?'ძალიან ბევრი ცდა. სცადე ერთ საათში. · Too many attempts. Try again in an hour.'
:j.code==='EXPIRED_FORM'?'გვერდს ვადა გაუვიდა — განაახლე და სცადე თავიდან. · The page expired — reload and try again.'
:'ვერ მოხერხდა. სცადე მოგვიანებით ან მოგვწერე. · Failed. Try later or email us.';
}catch{m.className='msg err';m.textContent='კავშირის შეცდომა. · Connection error.';}
go.disabled=false;});
</script>
</body></html>
""";

    /// <summary>Monochrome outline status icon (same stroke style as the app icon set); takes the colour of the .ok / .err class.</summary>
    private static string StatusSvg(string kind)
    {
        var path = kind switch
        {
            "check" => "M5,12.5 L10,17 L19,7",
            "close" => "M6,6 L18,18 M18,6 L6,18",
            _ => "M12,3 A9,9 0 1 1 11.99,3 Z M12,11 L12,17 M12,7.5 L12,7.6"
        };
        return "<svg viewBox=\"0 0 24 24\" width=\"56\" height=\"56\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\"><path d=\"" + path + "\"/></svg>";
    }

    public static string ResultPage(WebOrder? order, DateTimeOffset? activeUntil)
    {
        string icon, title, text, cls;
        // Dollar orders come from abroad: the page is in English.
        var en = order?.Currency == "USD";
        string L(string ka, string english) => en ? english : ka;
        var name = WebUtility.HtmlEncode(en && order?.ProductId == ProductCatalog.FullMonthly ? "Full PRO" : ProductName(order?.ProductId));
        var untilText = activeUntil is { } until ? until.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : null;
        switch (order?.Status)
        {
            case "paid":
                icon = StatusSvg("check"); cls = "ok";
                title = L("გადახდა წარმატებულია", "Payment successful");
                text = ProductCatalog.IsConsumable(order.ProductId)
                    ? L($"{name} დაემატა შენს ანგარიშს. აპში გახსენი PRO გვერდი და დააჭირე „სტატუსის განახლებას“.",
                        $"{name} was added to your account. In the app, open the PRO page and tap “Refresh status”.")
                    : L($"{name} ჩაირთო{(untilText is null ? "" : " " + untilText + "-მდე")}. აპში გახსენი PRO გვერდი და დააჭირე „სტატუსის განახლებას“.",
                        $"{name} is on{(untilText is null ? "" : " until " + untilText)}. In the app, open the PRO page and tap “Refresh status”.");
                break;
            case "failed":
                icon = StatusSvg("close"); cls = "err";
                title = L("გადახდა ვერ შესრულდა", "Payment failed");
                text = L("თანხა არ ჩამოგეჭრა. შეგიძლია ხელახლა სცადო.", "You were not charged. You can try again.");
                break;
            case null:
                icon = StatusSvg("info"); cls = "err";
                title = "შეკვეთა ვერ მოიძებნა · Order not found";
                text = "";
                break;
            default:
                icon = "…"; cls = "";
                title = L("გადახდა მოწმდება", "Checking the payment");
                text = L("რამდენიმე წამში გვერდი განახლდება.", "This page refreshes in a few seconds.");
                break;
        }

        var refresh = order is { Status: not ("paid" or "failed") }
            ? "<meta http-equiv=\"refresh\" content=\"4\">"
            : "";

        // Monthly automatic payment: show the next charge date.
        var renewText = order is { Status: "paid", Recurring: true } && activeUntil is { } periodEnd
            ? "<p class=\"muted\">" + L("შემდეგი ავტომატური ჩამოჭრა: <b>", "Next automatic charge: <b>") +
              periodEnd.AddDays(-WebPaymentService.RenewDaysBefore).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) +
              "</b>. " + L("გაუქმება: <a href=\"/account/subscription\">ჩემი გამოწერა</a>.", "Cancel any time on <a href=\"/account/subscription\">My subscription</a>.") + "</p>"
            : "";

        return $$"""
<!doctype html><html lang="{{(en ? "en" : "ka")}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">{{refresh}}
<title>OBD360 PRO</title><link rel="icon" type="image/png" href="/assets/favicon.png?v=2"><link rel="apple-touch-icon" href="/assets/apple-touch-icon.png?v=2">{{Style}}</head><body><main class="center">
<div class="big {{cls}}">{{icon}}</div><h1>{{title}}</h1><p class="muted">{{text}}</p>{{renewText}}
<p><a href="/pro" class="muted">← OBD360 PRO</a></p></main></body></html>
""";
    }

    /// <summary>Result page after paying for a VIN full history in the app.</summary>
    public static string FullResultPage(WebOrder? order, bool reportReady)
    {
        string icon, title, text, cls;
        var vin = WebUtility.HtmlEncode(FullHistoryService.VinOf(order?.ProductId) ?? "");
        switch (order?.Status)
        {
            case "paid":
                icon = StatusSvg("check"); cls = "ok";
                title = "გადახდა წარმატებულია · Payment successful";
                text = reportReady
                    ? $"VIN {vin}-ის სრული ისტორია მზადაა. დაბრუნდი OBD360 აპში — რეპორტი ავტომატურად გამოჩნდება."
                    : $"VIN {vin}-ის სრული ისტორია მზადდება. დაბრუნდი OBD360 აპში — რამდენიმე წამში გამოჩნდება.";
                break;
            case "failed":
                icon = StatusSvg("close"); cls = "err";
                title = "გადახდა ვერ შესრულდა · Payment failed";
                text = "თანხა არ ჩამოგეჭრა. დაბრუნდი აპში და ხელახლა სცადე.";
                break;
            case null:
                icon = StatusSvg("info"); cls = "err";
                title = "შეკვეთა ვერ მოიძებნა · Order not found";
                text = "";
                break;
            default:
                icon = "…"; cls = "";
                title = "გადახდა მოწმდება · Checking the payment";
                text = "რამდენიმე წამში გვერდი განახლდება.";
                break;
        }

        var refresh = order is { Status: not ("paid" or "failed") } || (order?.Status == "paid" && !reportReady)
            ? "<meta http-equiv=\"refresh\" content=\"4\">"
            : "";

        return $$"""
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">{{refresh}}
<title>OBD360 · VIN</title><link rel="icon" type="image/png" href="/assets/favicon.png?v=2"><link rel="apple-touch-icon" href="/assets/apple-touch-icon.png?v=2">{{Style}}</head><body><main class="center">
<div class="big {{cls}}">{{icon}}</div><h1>{{title}}</h1><p class="muted">{{text}}</p>
<p class="muted">ეს ფანჯარა შეგიძლია დახურო.</p></main></body></html>
""";
    }

    private static string ProductName(string? productId) => productId switch
    {
        ProductCatalog.ObdMonthly => "OBD PRO",
        ProductCatalog.VinMonthly => "VIN PRO",
        ProductCatalog.FullMonthly => "სრული PRO",
        ProductCatalog.VinPack5 => "+5 VIN",
        ProductCatalog.VinPack10 => "+10 VIN",
        ProductCatalog.VinSingle => "1 VIN ანგარიში",
        _ => "PRO"
    };
}
