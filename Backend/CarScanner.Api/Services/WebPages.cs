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
                Clean(config["Site:LegalAddress"]));
        }

        public string FooterHtml()
        {
            var parts = new List<string> { WebUtility.HtmlEncode(Name) };
            if (Id is not null) parts.Add("ს/კ · ID " + WebUtility.HtmlEncode(Id));
            if (Address is not null) parts.Add(WebUtility.HtmlEncode(Address));
            parts.Add("<a href=\"mailto:obd360support@gmail.com\">obd360support@gmail.com</a>");
            return "<div class=\"legal\">" + string.Join(" · ", parts) + "</div>";
        }
    }

    private const string Footer = """
<footer><a href="/terms-of-use">გამოყენების პირობები · Terms</a> · <a href="/privacy-policy">კონფიდენციალურობა · Privacy</a> · <a href="/account/subscription">ჩემი გამოწერა · My subscription</a> · <a href="/support">დახმარება · Support</a>
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
<div class="grow"><div class="t">{{T("ყოველთვიური", "Monthly")}}</div><span class="badge ok">{{T("რეკომენდებული", "Recommended")}}</span>
<div class="muted">{{T("ავტომატურად ყოველ 30 დღეში. გააუქმებ ნებისმიერ დროს.", "Charged automatically every 30 days. Cancel any time.")}}</div></div></label>
<label class="card opt"><input type="radio" name="mode" value="once">
<div class="grow"><div class="t">{{T("ერთჯერადი · 30 დღე", "One-time · 30 days")}}</div>
<div class="muted">{{T("ბარათი არ ინახება. ვადის ბოლოს თავად გაანახლებ.", "Your card is not saved. Renew it yourself when it ends.")}}</div></div></label>
<div class="sum" id="sum"><div><span>{{T("დღეს გადაიხდი", "Due today")}}</span><b id="sToday"></b></div><div id="sNextRow"><span>{{T("შემდეგი ჩამოჭრა", "Next charge")}}</span><b id="sNext"></b></div></div>
<label class="consent" id="consentRow"><input type="checkbox" id="consent"> <span>{{T("ვეთანხმები, რომ ბარათი შეინახება საქართველოს ბანკთან და ყოველ 30 დღეში ჩამოიჭრება", "I agree that my card is saved by Bank of Georgia and charged")}} <b id="sAmount"></b> {{T("გაუქმებამდე.", "every 30 days until I cancel.")}} <a href="/terms-of-use" target="_blank">{{T("პირობები", "Terms")}}</a></span></label>
</div>
""" : "";

        var note = autoRenewAvailable
            ? T("ყოველთვიური გადახდა შეგიძლია გააუქმო გვერდზე „ჩემი გამოწერა“ ან აპში. გადახდილი პერიოდი რჩება.",
                "Cancel the monthly payment any time on “My subscription” or in the app. The paid period is kept.")
            : T("ერთჯერადი გადახდა. ავტომატური ჩამოჭრა არ ხდება — გეგმა 30 დღე მოქმედებს, ხელახალი ყიდვა ვადას აგრძელებს.",
                "One-time payment, no automatic renewal. A plan lasts 30 days; buying again extends it.");

        return $$"""
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>OBD360 PRO</title>{{Style}}
<script>
// Lari for Georgia, US dollars everywhere else (charged in USD by the bank).
(function(){var tz='';try{tz=Intl.DateTimeFormat().resolvedOptions().timeZone||'';}catch(e){}
var langs=(navigator.languages||[navigator.language||'']).map(function(l){return String(l).toLowerCase();});
var ge=tz==='Asia/Tbilisi'||langs.some(function(l){return l.indexOf('ka')===0;});
if(location.hash==='#usd')ge=false;
var h=document.documentElement;h.classList.add(ge?'is-ge':'is-intl');h.lang=ge?'ka':'en';})();
</script></head><body><main>
<h1>OBD360 PRO</h1>
<div class="muted">{{T("აირჩიე გეგმა და ჩართე შენს OBD360 ანგარიშზე", "Choose a plan for your OBD360 account")}}</div>
{{unavailable}}
<form id="f">
<h2>{{T("გეგმები (30 დღე)", "Plans (30 days)")}}</h2>
{{Plan(ProductCatalog.ObdMonthly, "OBD PRO", "OBD PRO", "ყველა მოდულის სკანირება, ცოცხალი მონაცემები, ყიდვისწინა შემოწმება, AI", "Scan every module, live data, pre-purchase check, AI diagnostics")}}
{{Plan(ProductCatalog.VinMonthly, "VIN PRO", "VIN PRO", $"{ProductCatalog.VinMonthlyQuota} VIN ისტორია: აუქციონის ფოტოები, დაზიანება, გარბენი", $"{ProductCatalog.VinMonthlyQuota} VIN histories a month: auction photos, damage, mileage (US and Canada cars)")}}
{{Plan(ProductCatalog.FullMonthly, "სრული PRO", "Full PRO", $"ყველა OBD PRO ფუნქცია + {ProductCatalog.VinMonthlyQuota} VIN ისტორია", $"Everything in OBD PRO + {ProductCatalog.VinMonthlyQuota} VIN histories", best: true, check: true)}}
<h2>{{T("დამატებითი VIN", "Extra VIN checks")}}</h2>
<div class="muted">{{T("+5 და +10 პაკეტი VIN PRO / სრული PRO გეგმისთვისაა. ვადა არ გასდის.", "+5 and +10 packs are for VIN PRO / Full PRO and never expire.")}}</div>
{{Plan(ProductCatalog.VinPack5, "+5 VIN", "+5 VIN", "5 დამატებითი VIN ისტორია", "5 extra VIN histories")}}
{{Plan(ProductCatalog.VinPack10, "+10 VIN", "+10 VIN", "10 დამატებითი VIN ისტორია", "10 extra VIN histories")}}
{{Plan(ProductCatalog.VinSingle, "1 VIN ანგარიში", "1 VIN report", "გეგმის გარეშე", "No plan needed")}}
<h2>{{T("შენი ანგარიში", "Your account")}}</h2>
<div class="muted">{{T("იგივე ელ-ფოსტა და პაროლი, რითაც აპში შედიხარ.", "The e-mail and password you use in the app.")}}</div>
<label class="f" for="email">{{T("ელ-ფოსტა", "E-mail")}}</label><input type="email" id="email" name="email" autocomplete="email" required>
<label class="f" for="password">{{T("პაროლი", "Password")}}</label><input type="password" id="password" name="password" autocomplete="current-password" required>
{{bankSelect}}
{{renewal}}
<button id="pay" type="submit"{{(providers.Count == 0 ? " disabled" : "")}}>{{T("გადახდა ბარათით", "Pay by card")}}</button>
<div id="m" class="msg"></div>
<p class="muted">{{note}}</p>
<p class="muted">{{T("გადახდა ხდება საქართველოს ბანკის დაცულ გვერდზე. ბარათის ნომერს OBD360 ვერ ხედავს.", "You pay on Bank of Georgia's secure page. OBD360 never sees your card number. Prices in US dollars.")}} Visa · Mastercard · Apple Pay · Google Pay</p>
</form>
{{Footer}}{{legal.FooterHtml()}}</footer>
</main>
<script>
const f=document.getElementById('f'),m=document.getElementById('m'),b=document.getElementById('pay');
const renew=document.getElementById('renew'),bank=document.getElementById('bank');
const intl=document.documentElement.classList.contains('is-intl');
const tr=(ka,en)=>intl?en:ka;
const errors={INVALID_LOGIN:tr('ელ-ფოსტა ან პაროლი არასწორია.','Wrong e-mail or password.'),
EMAIL_NOT_VERIFIED:tr('ჯერ დაადასტურე ელფოსტა OBD360 აპლიკაციაში (ანგარიში), შემდეგ სცადე ხელახლა.','Verify your email in the OBD360 app (Account) first, then try again.'),
VIN_PLAN_REQUIRED:tr('+5 და +10 პაკეტი მხოლოდ VIN PRO ან სრული PRO გეგმითაა.','Packs need VIN PRO or Full PRO.'),
AUTO_RENEW_UNAVAILABLE:tr('ყოველთვიური გადახდა ახლა ვერ ჩაირთო. აირჩიე „ერთჯერადი“.','Monthly payment is unavailable right now. Choose one-time.'),
NOT_AVAILABLE:tr('გადახდა დროებით მიუწვდომელია.','Payments are temporarily unavailable.'),
TOO_MANY:tr('ძალიან ბევრი მცდელობა. სცადე 15 წუთში.','Too many attempts. Try again in 15 minutes.')};
function selected(){return f.querySelector('input[name=productId]:checked');}
function auto(){const r=f.querySelector('input[name=mode]:checked');return !!renew&&!renew.hidden&&r&&r.value==='auto';}
function update(){if(!renew)return;const p=selected();const card=p&&p.closest('label');const sub=card&&card.dataset.sub==='1';
const bogOk=intl||!bank||bank.value==='bog';renew.hidden=!(sub&&bogOk);
const price=card?(intl?card.dataset.usd:card.dataset.gel):'';
document.getElementById('sToday').textContent=price;document.getElementById('sAmount').textContent=price;
const d=new Date();d.setDate(d.getDate()+29);
document.getElementById('sNext').textContent=intl?d.toLocaleDateString('en-US',{day:'numeric',month:'short',year:'numeric'}):String(d.getDate()).padStart(2,'0')+'.'+String(d.getMonth()+1).padStart(2,'0')+'.'+d.getFullYear();
const a=auto();document.getElementById('sNextRow').hidden=!a;document.getElementById('consentRow').hidden=!a;
f.querySelectorAll('.opt').forEach(o=>o.classList.toggle('on',o.querySelector('input').checked));}
f.addEventListener('change',update);update();
f.addEventListener('submit',async e=>{e.preventDefault();
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
<title>OBD360 · ჩემი გამოწერა</title>{{Style}}</head><body><main>
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
<title>OBD360 · ანგარიშის წაშლა · Delete account</title>{{Style}}</head><body><main>
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
<title>OBD360 PRO</title>{{Style}}</head><body><main class="center">
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
<title>OBD360 · VIN</title>{{Style}}</head><body><main class="center">
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
