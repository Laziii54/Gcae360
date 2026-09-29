using System.Globalization;
using System.Net;

namespace CarScanner.Api.Services;

/// <summary>
/// The GCar360 PRO web page (plans, account, card payment) and the
/// payment result page. Georgian first, English underneath.
/// </summary>
public static class WebPages
{
    private static string Gel(string productId) =>
        (WebPaymentService.PriceFor(productId) ?? 0).ToString("0.00", CultureInfo.InvariantCulture) + " ₾";

    private const string Style = """
<style>
:root{--bg:#0b1016;--card:#141b24;--border:#26313f;--text:#f1f5f9;--muted:#94a3b8;--accent:#3b82f6;--ok:#22c55e;--warn:#f59e0b;--err:#ef4444}
@media (prefers-color-scheme: light){:root{--bg:#f4f6f8;--card:#fff;--border:#e2e8f0;--text:#0f172a;--muted:#64748b}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:16px/1.45 system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:560px;margin:0 auto;padding:20px 16px 48px}
h1{font-size:26px;margin:4px 0 2px}h2{font-size:18px;margin:26px 0 10px}.muted{color:var(--muted);font-size:14px}
.card{background:var(--card);border:1px solid var(--border);border-radius:14px;padding:14px 16px;margin:10px 0;display:flex;gap:12px;align-items:flex-start;cursor:pointer}
.card input{margin-top:5px;accent-color:var(--accent)}.card .t{font-weight:700}.card .p{margin-left:auto;font-weight:700;white-space:nowrap}
.card.best{border-color:var(--accent)}.badge{display:inline-block;background:var(--accent);color:#fff;border-radius:8px;font-size:11px;padding:1px 8px;margin-left:6px;vertical-align:middle}
label.f{display:block;font-size:14px;color:var(--muted);margin:12px 0 4px}
input[type=email],input[type=password],select{width:100%;padding:12px;border-radius:10px;border:1px solid var(--border);background:var(--card);color:var(--text);font-size:16px}
button{width:100%;margin-top:18px;padding:14px;border:0;border-radius:12px;background:var(--accent);color:#fff;font-size:17px;font-weight:700;cursor:pointer}
button:disabled{opacity:.6}.msg{margin-top:12px;font-size:14px}.err{color:var(--err)}.ok{color:var(--ok)}
footer{margin-top:28px;font-size:13px;color:var(--muted);text-align:center}footer a{color:var(--muted)}
.big{font-size:44px;text-align:center;margin:30px 0 6px}.center{text-align:center}
</style>
""";

    public static string ProPage(IReadOnlyList<string> providers)
    {
        string Plan(string id, string title, string en, string features, bool best = false, bool check = false) => $"""
<label class="card{(best ? " best" : "")}"><input type="radio" name="product" value="{id}"{(check ? " checked" : "")}>
<div><div class="t">{title}{(best ? "<span class=\"badge\">საუკეთესო</span>" : "")}</div><div class="muted">{en}</div><div class="muted">{features}</div></div>
<div class="p">{Gel(id)}</div></label>
""";

        var bankOptions = string.Join("", providers.Select(p =>
            $"<option value=\"{p}\">{(p == "bog" ? "საქართველოს ბანკი (BOG)" : "თიბისი (TBC)")}</option>"));
        var bankSelect = providers.Count > 1
            ? $"<label class=\"f\">ბანკი · Bank</label><select name=\"provider\">{bankOptions}</select>"
            : $"<input type=\"hidden\" name=\"provider\" value=\"{providers.FirstOrDefault() ?? "tbc"}\">";

        var unavailable = providers.Count == 0
            ? "<p class=\"msg err\">გადახდა დროებით მიუწვდომელია. სცადე მოგვიანებით. · Payments are temporarily unavailable.</p>"
            : "";

        return $$"""
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>GCar360 PRO</title>{{Style}}</head><body><main>
<h1>GCar360 PRO</h1>
<div class="muted">აირჩიე გეგმა და ჩართე შენს GCar360 ანგარიშზე · Choose a plan for your GCar360 account</div>
{{unavailable}}
<form id="f">
<h2>გეგმები · Plans (30 დღე / days)</h2>
{{Plan(ProductCatalog.ObdMonthly, "OBD PRO", "Full car diagnostics", "ყველა მოდულის სკანირება, ცოცხალი მონაცემები, ყიდვისწინა შემოწმება, AI")}}
{{Plan(ProductCatalog.VinMonthly, "VIN PRO", "Car history by VIN", $"{ProductCatalog.VinMonthlyQuota} VIN ისტორია: აუქციონის ფოტოები, დაზიანება, გარბენი")}}
{{Plan(ProductCatalog.FullMonthly, "სრული PRO", "OBD PRO + VIN PRO", $"ყველა OBD PRO ფუნქცია + {ProductCatalog.VinMonthlyQuota} VIN ისტორია", best: true, check: true)}}
<h2>დამატებითი VIN · Extra VIN checks</h2>
<div class="muted">+5 და +10 პაკეტი VIN PRO / სრული PRO გეგმისთვისაა. ვადა არ გასდის. · Packs are for VIN PRO / Full PRO and never expire.</div>
{{Plan(ProductCatalog.VinPack5, "+5 VIN", "5 extra VIN histories", "")}}
{{Plan(ProductCatalog.VinPack10, "+10 VIN", "10 extra VIN histories", "")}}
{{Plan(ProductCatalog.VinSingle, "1 VIN ანგარიში", "One VIN history", "გეგმის გარეშე · no plan needed")}}
<h2>შენი ანგარიში · Your account</h2>
<div class="muted">იგივე ელ-ფოსტა და პაროლი, რითაც აპში შედიხარ. · The e-mail and password you use in the app.</div>
<label class="f">ელ-ფოსტა · E-mail</label><input type="email" name="email" autocomplete="email" required>
<label class="f">პაროლი · Password</label><input type="password" name="password" autocomplete="current-password" required>
{{bankSelect}}
<button id="pay" type="submit"{{(providers.Count == 0 ? " disabled" : "")}}>გადახდა ბარათით · Pay by card</button>
<div id="m" class="msg"></div>
<p class="muted">ერთჯერადი გადახდა. ავტომატური ჩამოჭრა არ ხდება — გეგმა 30 დღე მოქმედებს, ხელახალი ყიდვა ვადას აგრძელებს. · One-time payment, no automatic renewal.</p>
</form>
<footer><a href="/terms-of-use">გამოყენების პირობები</a> · <a href="/privacy-policy">კონფიდენციალურობა</a> · <a href="/support">დახმარება</a></footer>
</main>
<script>
const f=document.getElementById('f'),m=document.getElementById('m'),b=document.getElementById('pay');
const errors={INVALID_LOGIN:'ელ-ფოსტა ან პაროლი არასწორია. · Wrong e-mail or password.',
VIN_PLAN_REQUIRED:'+5 და +10 პაკეტი მხოლოდ VIN PRO ან სრული PRO გეგმითაა. · Packs need VIN PRO or Full PRO.',
TOO_MANY:'ძალიან ბევრი მცდელობა. სცადე 15 წუთში. · Too many attempts, try later.'};
f.addEventListener('submit',async e=>{e.preventDefault();m.className='msg';m.textContent='მიმდინარეობს… · Please wait…';b.disabled=true;
const d=Object.fromEntries(new FormData(f));
try{const r=await fetch('/api/web/checkout',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(d)});
const j=await r.json().catch(()=>({}));
if(j.redirectUrl){location.href=j.redirectUrl;return;}
m.className='msg err';m.textContent=errors[j.code]||'გადახდა ვერ დაიწყო. სცადე ხელახლა. · Could not start the payment.';}
catch{m.className='msg err';m.textContent='კავშირის შეცდომა. · Connection error.';}
b.disabled=false;});
</script>
</body></html>
""";
    }

    public static string ResultPage(WebOrder? order, DateTimeOffset? activeUntil)
    {
        string icon, title, text, cls;
        var name = WebUtility.HtmlEncode(ProductName(order?.ProductId));
        switch (order?.Status)
        {
            case "paid":
                icon = "✓"; cls = "ok";
                title = "გადახდა წარმატებულია · Payment successful";
                text = ProductCatalog.IsConsumable(order.ProductId)
                    ? $"{name} დაემატა შენს ანგარიშს. აპში გახსენი PRO გვერდი და დააჭირე „სტატუსის განახლებას“."
                    : $"{name} ჩაირთო{(activeUntil is { } until ? " " + until.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + "-მდე" : "")}. აპში გახსენი PRO გვერდი და დააჭირე „სტატუსის განახლებას“.";
                break;
            case "failed":
                icon = "✕"; cls = "err";
                title = "გადახდა ვერ შესრულდა · Payment failed";
                text = "თანხა არ ჩამოგეჭრა. შეგიძლია ხელახლა სცადო.";
                break;
            case null:
                icon = "?"; cls = "err";
                title = "შეკვეთა ვერ მოიძებნა · Order not found";
                text = "";
                break;
            default:
                icon = "…"; cls = "";
                title = "გადახდა მოწმდება · Checking the payment";
                text = "რამდენიმე წამში გვერდი განახლდება.";
                break;
        }

        var refresh = order is { Status: not ("paid" or "failed") }
            ? "<meta http-equiv=\"refresh\" content=\"4\">"
            : "";

        return $$"""
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">{{refresh}}
<title>GCar360 PRO</title>{{Style}}</head><body><main class="center">
<div class="big {{cls}}">{{icon}}</div><h1>{{title}}</h1><p class="muted">{{text}}</p>
<p><a href="/pro" class="muted">← GCar360 PRO</a></p></main></body></html>
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
                icon = "✓"; cls = "ok";
                title = "გადახდა წარმატებულია · Payment successful";
                text = reportReady
                    ? $"VIN {vin}-ის სრული ისტორია მზადაა. დაბრუნდი GCar360 აპში — რეპორტი ავტომატურად გამოჩნდება."
                    : $"VIN {vin}-ის სრული ისტორია მზადდება. დაბრუნდი GCar360 აპში — რამდენიმე წამში გამოჩნდება.";
                break;
            case "failed":
                icon = "✕"; cls = "err";
                title = "გადახდა ვერ შესრულდა · Payment failed";
                text = "თანხა არ ჩამოგეჭრა. დაბრუნდი აპში და ხელახლა სცადე.";
                break;
            case null:
                icon = "?"; cls = "err";
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
<title>GCar360 · VIN</title>{{Style}}</head><body><main class="center">
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
