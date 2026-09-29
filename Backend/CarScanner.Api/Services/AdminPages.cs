using System.Globalization;
using System.Net;
using System.Text;

namespace CarScanner.Api.Services;

/// <summary>HTML of the owner page (/admin). Georgian.</summary>
public static class AdminPages
{
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string N(int value) => value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
    private static string Gel(decimal value) => value.ToString("#,0.00", CultureInfo.InvariantCulture).Replace(',', ' ') + " ₾";
    private static string Tbilisi(DateTimeOffset at) => at.ToOffset(TimeSpan.FromHours(4)).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    private static string Pct(int part, int whole) => whole <= 0 ? "—" : Math.Round(100.0 * part / whole).ToString(CultureInfo.InvariantCulture) + "%";

    private const string Style = """
<style>
:root{--bg:#f3f5f9;--side:#0e1726;--card:#fff;--line:#e5e9f0;--text:#0f172a;--muted:#64748b;--accent:#2563eb;--accent-soft:#e8efff;--ok:#16a34a;--ok-soft:#e7f7ec;--warn:#d97706;--warn-soft:#fff4e0;--err:#dc2626;--err-soft:#fdecec}
@media (prefers-color-scheme: dark){:root{--bg:#0b111a;--card:#121a26;--line:#223044;--text:#e8eef6;--muted:#93a3b8;--accent-soft:#16264a;--ok-soft:#0f2a1a;--warn-soft:#2c2110;--err-soft:#2d1414}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
a{color:var(--accent);text-decoration:none}
.shell{display:grid;grid-template-columns:230px 1fr;min-height:100vh}
aside{background:var(--side);color:#cbd5e1;padding:20px 14px;position:sticky;top:0;height:100vh}
.brand{color:#fff;font-weight:800;font-size:18px;padding:0 10px 18px}.brand span{color:#60a5fa}
aside a{display:flex;gap:10px;align-items:center;color:#cbd5e1;padding:9px 12px;border-radius:10px;margin:2px 0;font-weight:500}
aside a:hover{background:rgba(255,255,255,.06)}aside a.on{background:#1e3a8a;color:#fff}
aside .who{position:absolute;bottom:18px;left:14px;right:14px;font-size:12px;color:#94a3b8;word-break:break-all}
aside .who button{margin-top:8px;width:100%}
main{padding:26px 28px 60px;min-width:0}
h1{font-size:22px;margin:0 0 4px}.sub{color:var(--muted);margin:0 0 20px}
h2{font-size:15px;margin:26px 0 10px;color:var(--muted);font-weight:600;text-transform:uppercase;letter-spacing:.04em}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(190px,1fr));gap:12px}
.kpi{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:14px 16px}
.kpi .l{color:var(--muted);font-size:12.5px}.kpi .v{font-size:26px;font-weight:800;margin:2px 0;letter-spacing:-.02em}.kpi .h{color:var(--muted);font-size:12px}
.kpi.bad{border-color:var(--err);background:var(--err-soft)}.kpi.good .v{color:var(--ok)}
.panel{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px 18px}
.two{display:grid;grid-template-columns:1fr 1fr;gap:12px}
.bars .row{display:grid;grid-template-columns:140px 1fr 48px;gap:10px;align-items:center;margin:6px 0;font-size:13px}
.bars .track{height:9px;background:var(--accent-soft);border-radius:6px;overflow:hidden}.bars .fill{height:100%;background:var(--accent);border-radius:6px}
.bars .name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.bars .n{text-align:right;color:var(--muted)}
svg.chart{width:100%;height:150px;display:block}
.tbl{background:var(--card);border:1px solid var(--line);border-radius:14px;overflow-x:auto}
table{border-collapse:collapse;width:100%;min-width:760px}
th,td{padding:10px 12px;border-bottom:1px solid var(--line);text-align:left;vertical-align:middle;font-size:13px}
th{color:var(--muted);font-weight:600;background:color-mix(in srgb,var(--card) 92%,var(--line))}tr:last-child td{border-bottom:0}
tr.problem td{background:var(--err-soft)}tr.banned td{opacity:.65}
.pill{display:inline-block;border-radius:999px;padding:2px 9px;font-size:11.5px;font-weight:700;white-space:nowrap}
.p-ok{background:var(--ok-soft);color:var(--ok)}.p-err{background:var(--err-soft);color:var(--err)}.p-warn{background:var(--warn-soft);color:var(--warn)}.p-mute{background:var(--accent-soft);color:var(--muted)}.p-acc{background:var(--accent-soft);color:var(--accent)}
button,.btn{border:0;border-radius:9px;padding:7px 12px;font-weight:600;cursor:pointer;background:var(--accent);color:#fff;font-size:12.5px;font-family:inherit}
button.red{background:var(--err)}button.ghost{background:transparent;color:inherit;border:1px solid rgba(148,163,184,.4)}button.green{background:var(--ok)}
form.inline{display:inline-flex;gap:6px;align-items:center;margin:2px 4px 2px 0}
select,input[type=text],input[type=number],input[type=search],input[type=email],input[type=password]{padding:8px 10px;border-radius:9px;border:1px solid var(--line);background:var(--card);color:var(--text);font:inherit}
input[type=number]{width:70px}
.search{display:flex;gap:8px;margin:0 0 14px}.search input{flex:1;max-width:380px}
.msg{padding:11px 14px;border-radius:12px;margin:0 0 16px;border:1px solid var(--line);background:var(--card)}.msg.ok{border-color:var(--ok);background:var(--ok-soft)}.msg.err{border-color:var(--err);background:var(--err-soft)}
.warnbox{padding:12px 14px;border-radius:12px;margin:0 0 16px;background:var(--warn-soft);border:1px solid var(--warn)}
.muted{color:var(--muted)}.mono{font-family:ui-monospace,Consolas,monospace;font-size:12.5px;word-break:break-all}
.login{max-width:400px;margin:9vh auto;padding:0 16px}.login .panel{padding:26px}.login input{width:100%;margin:6px 0 14px;padding:12px}.login button{width:100%;padding:12px;font-size:15px}
.code{font-size:22px;letter-spacing:.3em;text-align:center}
@media (max-width:860px){.shell{grid-template-columns:1fr}aside{position:sticky;top:0;z-index:5;height:auto;display:flex;align-items:center;overflow-x:auto;gap:4px;padding:8px 10px}
.brand{padding:6px 8px}aside a{white-space:nowrap;padding:8px 10px}aside .who{position:static;margin-left:6px;font-size:0}aside .who button{margin:0;width:auto;font-size:12px;white-space:nowrap}
main{padding:18px 14px 50px}.two{grid-template-columns:1fr}.grid{grid-template-columns:1fr 1fr;gap:8px}.kpi{padding:11px 12px}.kpi .v{font-size:21px}.bars .row{grid-template-columns:110px 1fr 40px}}
</style>
""";

    private static string Page(string title, string body) => $$"""
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex,nofollow">
<title>{{H(title)}} · GCar360 ადმინი</title>{{Style}}</head><body>{{body}}</body></html>
""";

    // ============================================================
    // LOGIN
    // ============================================================

    public static string Login(string? error) => Page("შესვლა", $$"""
<div class="login"><div class="panel">
<div style="font-weight:800;font-size:20px;margin-bottom:4px">GCar<span style="color:var(--accent)">360</span> · ადმინი</div>
<p class="muted" style="margin:0 0 18px">მხოლოდ მფლობელისთვის</p>
{{(error is null ? "" : $"<div class=\"msg err\">{H(error)}</div>")}}
<form method="post" action="/admin/login" autocomplete="on">
<label class="muted">ელ-ფოსტა</label><input type="email" name="email" autocomplete="username" required>
<label class="muted">პაროლი</label><input type="password" name="password" autocomplete="current-password" required>
{{(AdminService.TwoStepEnabled ? "<label class=\"muted\">6-ნიშნა კოდი (Google Authenticator)</label><input class=\"code\" type=\"text\" name=\"code\" inputmode=\"numeric\" autocomplete=\"one-time-code\" maxlength=\"6\" required>" : "")}}
<button type="submit">შესვლა</button></form>
</div></div>
""");

    // ============================================================
    // LAYOUT
    // ============================================================

    private static readonly (string Key, string Icon, string Name)[] Tabs =
    {
        ("overview", "📊", "მიმოხილვა"),
        ("users", "👤", "მომხმარებლები"),
        ("subs", "⭐", "გამოწერები"),
        ("payments", "💳", "გადახდები"),
        ("devices", "📱", "მოწყობილობები"),
        ("acceleration", "🏁", "0–100 რეიტინგი"),
        ("log", "🧾", "ჟურნალი"),
        ("security", "🔒", "უსაფრთხოება"),
    };

    private static string Layout(AdminService.Session s, string tab, string title, string subtitle, string content, string? message, bool ok)
    {
        var nav = string.Concat(Tabs.Select(t =>
            $"<a class=\"{(t.Key == tab ? "on" : "")}\" href=\"/admin?tab={t.Key}\"><span>{t.Icon}</span>{t.Name}</a>"));
        var note = message is null ? "" : $"<div class=\"msg {(ok ? "ok" : "err")}\">{H(message)}</div>";
        var twoStep = AdminService.TwoStepEnabled
            ? ""
            : "<div class=\"warnbox\">⚠️ ორსაფეხურიანი დაცვა ჯერ ჩართული არ არის. გადადი <a href=\"/admin?tab=security\">„უსაფრთხოება“</a>-ზე და ჩართე — 2 წუთი სჭირდება.</div>";

        return Page(title, $$"""
<div class="shell"><aside><div class="brand">GCar<span>360</span></div>{{nav}}
<div class="who">{{H(s.Email)}}<form method="post" action="/admin/logout"><input type="hidden" name="csrf" value="{{H(s.Csrf)}}"><button class="ghost">გასვლა</button></form></div></aside>
<main><h1>{{H(title)}}</h1><p class="sub">{{subtitle}}</p>{{twoStep}}{{note}}{{content}}</main></div>
""");
    }

    private static string Kpi(string label, string value, string hint, string cls = "") =>
        $"<div class=\"kpi {cls}\"><div class=\"l\">{label}</div><div class=\"v\">{value}</div><div class=\"h\">{hint}</div></div>";

    public static string Acceleration(
        AdminService.Session s,
        IReadOnlyList<AccelerationLeaderboardAdminItem> items,
        string? message,
        bool ok)
    {
        var csrf = H(s.Csrf);
        var pending = items.Count(x => x.Status == "Pending");
        var approved = items.Count(x => x.Status == "Approved");
        var demo = items.Count(x => x.IsDemo);
        var rows = new StringBuilder();
        foreach (var item in items)
        {
            var status = item.Status switch
            {
                "Approved" => "<span class=\"pill p-ok\">დამოწმებული</span>",
                "Rejected" => "<span class=\"pill p-err\">უარყოფილი</span>",
                _ => "<span class=\"pill p-warn\">მოლოდინში</span>"
            };
            var demoPill = item.IsDemo ? " <span class=\"pill p-acc\">DEMO</span>" : string.Empty;
            var approve = $"<form class=\"inline\" method=\"post\" action=\"/admin/acceleration/{H(item.SubmissionId)}/approve\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button class=\"green\">დამოწმება</button></form>";
            var reject = $"<form class=\"inline\" method=\"post\" action=\"/admin/acceleration/{H(item.SubmissionId)}/reject\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button>უარყოფა</button></form>";
            var delete = $"<form class=\"inline\" method=\"post\" action=\"/admin/acceleration/{H(item.SubmissionId)}/delete\" onsubmit=\"return confirm('ნამდვილად წაიშალოს ეს შედეგი?')\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button class=\"red\">წაშლა</button></form>";
            rows.Append($"""
<tr><td><b>{H(item.UserName)}</b>{demoPill}<div class="muted mono">{H(item.SubmissionId)}</div></td>
<td>{H(item.VehicleModel)}</td><td><b>{item.DurationSeconds:0.00} წმ</b></td><td>{item.SampleCount}</td>
<td>{(item.HasVerifiedVideo ? "✓ ვიდეო" : "—")}</td><td>{status}</td><td>{item.FinishedAt.ToLocalTime():dd.MM.yyyy HH:mm}</td>
<td>{approve}{reject}{delete}</td></tr>
""");
        }

        var content = $"""
<div class="grid">
{Kpi("მოლოდინში", N(pending), "შენს გადაწყვეტილებას ელოდება", pending > 0 ? "bad" : "")}
{Kpi("დამოწმებული", N(approved), "საჯარო რეიტინგში ჩანს", "good")}
{Kpi("DEMO", N(demo), "სატესტო ჩანაწერი")}
</div>
<h2>ყველა შედეგი</h2>
<div class="tbl"><table><thead><tr><th>მომხმარებელი</th><th>მანქანა</th><th>დრო</th><th>ECU ნიმუში</th><th>ვიდეო</th><th>სტატუსი</th><th>თარიღი</th><th>მართვა</th></tr></thead>
<tbody>{rows}</tbody></table></div>
""";
        return Layout(s, "acceleration", "0–100 რეიტინგი", "დამოწმება, უარყოფა და წაშლა · მხოლოდ დამოწმებული შედეგი ჩანს აპში", content, message, ok);
    }

    private static string Bars(IEnumerable<(string Label, int Count)> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return "<p class=\"muted\">მონაცემები ჯერ არ არის.</p>";
        var max = Math.Max(1, list.Max(i => i.Count));
        return "<div class=\"bars\">" + string.Concat(list.Select(i =>
            $"<div class=\"row\"><span class=\"name\" title=\"{H(i.Label)}\">{H(string.IsNullOrWhiteSpace(i.Label) ? "?" : i.Label)}</span><div class=\"track\"><div class=\"fill\" style=\"width:{Math.Max(2, 100 * i.Count / max)}%\"></div></div><span class=\"n\">{N(i.Count)}</span></div>")) + "</div>";
    }

    /// <summary>Simple bar chart; second series (optional) drawn in red below the axis.</summary>
    private static string Chart(IReadOnlyList<(string Label, int Up, int Down)> days)
    {
        if (days.Count == 0) return "<p class=\"muted\">მონაცემები ჯერ არ არის.</p>";
        const int w = 600, h = 150;
        var hasDown = days.Any(d => d.Down > 0);
        var axis = hasDown ? 100 : 135;
        var max = Math.Max(1, days.Max(d => Math.Max(d.Up, d.Down)));
        var bw = (double)w / days.Count;
        var sb = new StringBuilder($"<svg class=\"chart\" viewBox=\"0 0 {w} {h}\" preserveAspectRatio=\"none\">");
        sb.Append($"<line x1=\"0\" y1=\"{axis}\" x2=\"{w}\" y2=\"{axis}\" stroke=\"currentColor\" stroke-opacity=\".15\"/>");
        for (var i = 0; i < days.Count; i++)
        {
            var x = i * bw + bw * 0.18;
            var up = (axis - 8) * days[i].Up / (double)max;
            var down = (h - axis - 6) * days[i].Down / (double)max;
            sb.Append(FormattableString.Invariant($"<rect x=\"{x:0.#}\" y=\"{axis - up:0.#}\" width=\"{bw * 0.64:0.#}\" height=\"{up:0.#}\" rx=\"2\" fill=\"#2563eb\"><title>{H(days[i].Label)}: +{days[i].Up}</title></rect>"));
            if (days[i].Down > 0)
                sb.Append(FormattableString.Invariant($"<rect x=\"{x:0.#}\" y=\"{axis:0.#}\" width=\"{bw * 0.64:0.#}\" height=\"{down:0.#}\" rx=\"2\" fill=\"#dc2626\"><title>{H(days[i].Label)}: −{days[i].Down}</title></rect>"));
        }
        sb.Append("</svg>");
        sb.Append($"<div class=\"muted\" style=\"display:flex;justify-content:space-between;font-size:11.5px\"><span>{H(days[0].Label)}</span><span>{H(days[^1].Label)}</span></div>");
        return sb.ToString();
    }

    // ============================================================
    // OVERVIEW
    // ============================================================

    public static string Overview(AdminService.Session s, AdminService.Overview o, PlayStats play, string? message, bool ok)
    {
        var playBlock = play.Available
            ? $$"""
<div class="grid">
{{Kpi("სულ გადმოწერა", N(play.TotalInstalls), "Google Play · უნიკალური მომხმარებელი")}}
{{Kpi("დაყენებულია ახლა", N(play.ActiveDevices), "აქტიური მოწყობილობა")}}
{{Kpi("გადმოწერა · 30 დღე", "+" + N(play.Installs30), "ახალი ინსტალაცია", "good")}}
{{Kpi("წაშლა · 30 დღე", "−" + N(play.Uninstalls30), Pct(play.Uninstalls30, Math.Max(1, play.Installs30)) + " ახლებთან შედარებით")}}
</div>
<div class="panel" style="margin-top:12px"><b>ინსტალაცია (ლურჯი) და წაშლა (წითელი) დღეების მიხედვით</b>{{Chart(play.Days.Select(d => (d.Date.ToString("dd.MM", CultureInfo.InvariantCulture), d.DailyUserInstalls, d.DailyUserUninstalls)).ToList())}}
<p class="muted" style="margin:6px 0 0">Google Play-ის მონაცემები · ბოლო განახლება {{play.AsOf?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}} (Google ერთი-ორი დღის დაგვიანებით აქვეყნებს)</p></div>
"""
            : $"<div class=\"warnbox\">Google Play-ის გადმოწერა/წაშლის სტატისტიკა ჯერ არ ჩანს: {H(play.Note)}. როგორ ჩავრთო — იხილე „უსაფრთხოება“ გვერდის ბოლოს.</div>";

        var newPerDay = o.ActivePerDay.Select(d => (d.Day.ToString("dd.MM", CultureInfo.InvariantCulture), d.Count, 0)).ToList();

        var content = $$"""
<h2>Google Play</h2>
{{playBlock}}

<h2>აპის გამოყენება</h2>
<div class="grid">
{{Kpi("იყენებს დღეს", N(o.Active1), "ბოლო 24 საათში გახსნა")}}
{{Kpi("იყენებს ამ კვირაში", N(o.Active7), "ბოლო 7 დღეში")}}
{{Kpi("იყენებს ამ თვეში", N(o.Active30), "ბოლო 30 დღეში")}}
{{Kpi("აღარ იყენებს", N(o.Inactive30), "30+ დღეა არ გაუხსნია (ან წაშალა)")}}
{{Kpi("ნოტიფიკაციები ჩართული", N(o.NotificationsAllowed), Pct(o.NotificationsAllowed, o.Active30) + " აქტიურებიდან")}}
{{Kpi("შეხსენებები ჩართული", N(o.RemindersOn), "სკანირების შეხსენება აპში")}}
</div>
<div class="panel" style="margin-top:12px"><b>ახალი მოწყობილობები დღეების მიხედვით</b> <span class="muted">(აპი პირველად გაიხსნა)</span>{{Chart(newPerDay)}}</div>

<h2>ანგარიშები და გამოწერები</h2>
<div class="grid">
{{Kpi("ანგარიშები", N(o.Accounts), "+" + N(o.NewAccounts7) + " ბოლო 7 დღეში")}}
{{Kpi("აქტიური PRO", N(o.ActiveSubscriptions), $"OBD {o.SubsObd} · VIN {o.SubsVin} · სრული {o.SubsFull}", "good")}}
{{Kpi("ავტო-განახლებადი", N(o.SubsGoogle), "Google Play — თანხა ავტომატურად ჩამოეჭრება")}}
{{Kpi("ბარათით (30 დღე)", N(o.SubsWeb), "ავტომატურად არ გრძელდება")}}
{{Kpi("პრომო / ადმინი", N(o.SubsOther), "უფასოდ მიცემული")}}
{{Kpi("იწურება 7 დღეში", N(o.Expiring7), "გამოწერა მალე მთავრდება")}}
{{Kpi("დაბლოკილი", N(o.Banned), "ანგარიში")}}
{{Kpi("ანგარიშით შესული", N(o.SignedInDevices), "მოწყობილობა (30 დღე)")}}
</div>

<h2>შემოსავალი (ბარათით გადახდები)</h2>
<div class="grid">
{{Kpi("ამ თვეში", Gel(o.RevenueMonth), N(o.PaidMonth) + " გადახდა", "good")}}
{{Kpi("სულ", Gel(o.RevenueTotal), N(o.PaidTotal) + " გადახდა")}}
{{Kpi("სრული ისტორია ამ თვეში", N(o.FullReportsMonth), "გაყიდული რეპორტი")}}
{{Kpi("პრობლემები", N(o.Problems), "გადაიხადა, რეპორტი არ მიიღო", o.Problems > 0 ? "bad" : "")}}
</div>
<p class="muted">Google Play-ით შემოსული თანხა Play Console-ში ჩანს („Financial reports“).</p>

<h2>VIN და სტაბილურობა</h2>
<div class="grid">
{{Kpi("VIN ისტორია ამ თვეში", N(o.VinUnlocksMonth), "გახსნილი ანგარიში")}}
{{Kpi("დარჩენილი VIN კრედიტი", N(o.VinCreditsLeft), "ყველა მომხმარებელზე ჯამში")}}
{{Kpi("აპის შეცდომები · 7 დღე", N(o.Crashes7), "crash report", o.Crashes7 > 20 ? "bad" : "")}}
</div>
""";
        return Layout(s, "overview", "მიმოხილვა", "ყველაფერი ერთ გვერდზე · თბილისის დრო", content, message, ok);
    }

    // ============================================================
    // USERS
    // ============================================================

    public static string Users(AdminService.Session s, List<AdminService.UserRow> users, string? search, string? message, bool ok)
    {
        var csrf = H(s.Csrf);
        var rows = new StringBuilder();
        foreach (var u in users)
        {
            var status = u.Banned
                ? "<span class=\"pill p-err\">დაბლოკილი</span>"
                : u.LastSeen is { } seen && seen > DateTimeOffset.UtcNow.AddDays(-30)
                    ? "<span class=\"pill p-ok\">აქტიური</span>"
                    : "<span class=\"pill p-mute\">არააქტიური</span>";

            var actions = u.Banned
                ? $"<form class=\"inline\" method=\"post\" action=\"/admin/users/{H(u.AccountId)}/unban\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button class=\"green\">განბლოკვა</button></form>"
                : $"<form class=\"inline\" method=\"post\" action=\"/admin/users/{H(u.AccountId)}/ban\" onsubmit=\"return confirm('დაბლოკო {H(u.Email)}? ვეღარ შევა აპში.')\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><input type=\"text\" name=\"reason\" placeholder=\"მიზეზი\" style=\"width:110px\"><button class=\"red\">დაბლოკვა</button></form>";

            actions += $"<form class=\"inline\" method=\"post\" action=\"/admin/users/{H(u.AccountId)}/unbind\" onsubmit=\"return confirm('მოვხსნა მოწყობილობის მიბმა: {H(u.Email)}?')\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button>მოწყობილობის მოხსნა</button></form>";

            actions += $"""<form class="inline" method="post" action="/admin/users/{H(u.AccountId)}/grant"><input type="hidden" name="csrf" value="{csrf}"><select name="product"><option value="{ProductCatalog.FullMonthly}">სრული PRO</option><option value="{ProductCatalog.ObdMonthly}">OBD PRO</option><option value="{ProductCatalog.VinMonthly}">VIN PRO</option></select><input type="number" name="days" value="30" min="1" max="365"><button>PRO-ს მიცემა</button></form>""";

            rows.Append($"""
<tr class="{(u.Banned ? "banned" : "")}">
<td><b>{H(u.Email)}</b><div class="muted mono">{H(u.AccountId)}</div></td>
<td>{status}</td>
<td>{Tbilisi(u.CreatedAt)}</td>
<td>{(u.LastSeen is { } ls ? Tbilisi(ls) : "<span class=\"muted\">—</span>")}</td>
<td>{(string.IsNullOrEmpty(u.Plans) ? "<span class=\"muted\">უფასო</span>" : H(u.Plans))}{(u.Credits > 0 ? $"<div class=\"muted\">+{u.Credits} VIN კრედიტი</div>" : "")}</td>
<td>{u.Devices}{(u.NotificationsAllowed ? " · 🔔" : "")}</td>
<td>{actions}</td>
</tr>
""");
        }

        var table = users.Count == 0
            ? "<div class=\"panel muted\">ვერაფერი მოიძებნა.</div>"
            : $"<div class=\"tbl\"><table><tr><th>ანგარიში</th><th>სტატუსი</th><th>რეგისტრაცია</th><th>ბოლოს ნანახი</th><th>გეგმა</th><th>მოწყ.</th><th>მოქმედება</th></tr>{rows}</table></div>";

        var content = $"""
<form class="search" method="get" action="/admin"><input type="hidden" name="tab" value="users"><input type="search" name="q" value="{H(search)}" placeholder="მეილის ან ID-ის ძებნა"><button>ძებნა</button></form>
{table}
<p class="muted">„დაბლოკვა“ — ანგარიში აპიდან გამოვა და ვეღარ შევა. „PRO-ს მიცემა“ — უფასოდ ჩართავს არჩეულ გეგმას მითითებული დღეებით.</p>
""";
        return Layout(s, "users", "მომხმარებლები", $"{users.Count} ანგარიში (ბოლო 300)", content, message, ok);
    }

    // ============================================================
    // SUBSCRIPTIONS
    // ============================================================

    public static string Subscriptions(AdminService.Session s, List<AdminService.SubscriptionRow> subs, string? message, bool ok)
    {
        var rows = new StringBuilder();
        foreach (var x in subs)
        {
            var auto = x.Source is "google" or "apple";
            var soon = x.ExpiresAt is { } e && e < DateTimeOffset.UtcNow.AddDays(7);
            rows.Append($"""
<tr><td><b>{H(x.Email ?? "?")}</b></td><td>{H(AdminService.PlanName(x.ProductId))}</td>
<td><span class="pill {(auto ? "p-ok" : x.Source == "web" ? "p-acc" : "p-mute")}">{H(AdminService.SourceName(x.Source))}</span></td>
<td>{(x.ExpiresAt is { } ex ? Tbilisi(ex) : "—")}{(soon ? " <span class=\"pill p-warn\">მალე</span>" : "")}</td>
<td>{Tbilisi(x.UpdatedAt)}</td></tr>
""");
        }

        var autoCount = subs.Count(x => x.Source is "google" or "apple");
        var content = $"""
<div class="grid" style="margin-bottom:14px">
{Kpi("აქტიური", N(subs.Count), "ყველა წყარო", "good")}
{Kpi("ავტო-განახლებადი", N(autoCount), "Google Play / App Store")}
{Kpi("ბარათით", N(subs.Count(x => x.Source == "web")), "30 დღე, არ გრძელდება")}
{Kpi("უფასო", N(subs.Count(x => x.Source is "promo" or "admin")), "პრომო / ადმინი")}
</div>
{(subs.Count == 0 ? "<div class=\"panel muted\">აქტიური გამოწერა ჯერ არ არის.</div>" : $"<div class=\"tbl\"><table><tr><th>მომხმარებელი</th><th>გეგმა</th><th>წყარო</th><th>მოქმედებს</th><th>განახლდა</th></tr>{rows}</table></div>")}
<p class="muted">Google Play-ის გამოწერა ყოველთვიურად ავტომატურად გრძელდება, სანამ მომხმარებელი არ გააუქმებს; გაუქმებულიც ვადის ბოლომდე აქტიურია. გაუქმებისა და დაბრუნების დეტალები — Play Console → „Order management“.</p>
""";
        return Layout(s, "subs", "გამოწერები", "ახლა აქტიური PRO გეგმები", content, message, ok);
    }

    // ============================================================
    // PAYMENTS
    // ============================================================

    public static string Payments(AdminService.Session s, List<AdminService.OrderRow> orders, string? message, bool ok)
    {
        var csrf = H(s.Csrf);
        var sb = new StringBuilder();
        foreach (var o in orders)
        {
            var vin = FullHistoryService.VinOf(o.ProductId);
            var problem = vin is not null && o.Status == "paid" && o.ReportStatus != "ready";
            var report = vin is null ? "—" : o.ReportStatus switch
            {
                "ready" => "<span class=\"pill p-ok\">მიღებულია</span>",
                null => o.Status == "paid" ? "<span class=\"pill p-err\">არ მოსულა</span>" : "—",
                _ => "<span class=\"pill p-warn\">ელოდება</span>"
            };
            var pill = o.Status switch { "paid" => "p-ok", "failed" => "p-err", "refunded" => "p-warn", _ => "p-mute" };

            var actions = new StringBuilder();
            if (problem)
                actions.Append($"<form class=\"inline\" method=\"post\" action=\"/admin/orders/{H(o.OrderId)}/retry\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button>ხელახლა ცდა</button></form>");
            if (o.Status == "paid")
                actions.Append($"<form class=\"inline\" method=\"post\" action=\"/admin/orders/{H(o.OrderId)}/refund\" onsubmit=\"return confirm('დავუბრუნოთ {o.Amount.ToString("0.00", CultureInfo.InvariantCulture)} ₾ ბარათზე? ამის გაუქმება შეუძლებელია.')\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button class=\"red\">დაბრუნება</button></form>");

            sb.Append($"""
<tr class="{(problem ? "problem" : "")}"><td>{Tbilisi(o.CreatedAt)}</td><td>{H(o.Email ?? "?")}</td><td>{H(AdminService.PlanName(o.ProductId))}</td>
<td style="white-space:nowrap"><b>{o.Amount.ToString("0.00", CultureInfo.InvariantCulture)} ₾</b></td><td>{H(o.Provider.ToUpperInvariant())}</td>
<td><span class="pill {pill}">{H(AdminService.StatusName(o.Status))}</span></td><td>{report}</td><td>{actions}</td></tr>
""");
        }

        var content = $"""
<p class="muted">წითელ სტრიქონში მომხმარებელმა გადაიხადა, მაგრამ რეპორტი არ მიიღო: ჯერ „ხელახლა ცდა“, თუ არ გამოვიდა — „დაბრუნება“. აქ ჩანს მხოლოდ ბარათით (BOG/TBC) გადახდები; Google Play-ის შესყიდვები Play Console-შია.</p>
{(orders.Count == 0 ? "<div class=\"panel muted\">გადახდები ჯერ არ არის.</div>" : $"<div class=\"tbl\"><table><tr><th>თარიღი</th><th>მომხმარებელი</th><th>პროდუქტი</th><th>თანხა</th><th>ბანკი</th><th>სტატუსი</th><th>რეპორტი</th><th>მოქმედება</th></tr>{sb}</table></div>")}
""";
        return Layout(s, "payments", "გადახდები", "ბოლო 300 შეკვეთა", content, message, ok);
    }

    // ============================================================
    // DEVICES
    // ============================================================

    public static string Devices(AdminService.Session s, AdminService.Overview o, string? message, bool ok)
    {
        var content = $"""
<div class="grid" style="margin-bottom:12px">
{Kpi("ნანახი მოწყობილობა", N(o.Devices), "რაც აპი ამ ფუნქციით გაიხსნა")}
{Kpi("აქტიური · 30 დღე", N(o.Active30), Pct(o.Active30, o.Devices) + " ყველასგან", "good")}
{Kpi("ნოტიფიკაციები", N(o.NotificationsAllowed), Pct(o.NotificationsAllowed, o.Active30) + " აქტიურებიდან")}
{Kpi("ანგარიშით", N(o.SignedInDevices), Pct(o.SignedInDevices, o.Active30) + " აქტიურებიდან")}
</div>
<div class="two">
<div class="panel"><b>აპის ვერსია</b>{Bars(o.Versions)}</div>
<div class="panel"><b>Android-ის ვერსია</b>{Bars(o.Os)}</div>
<div class="panel"><b>ტელეფონის მოდელი</b>{Bars(o.Models)}</div>
<div class="panel"><b>ენა</b>{Bars(o.Languages)}</div>
</div>
<p class="muted">აპი თითო მოწყობილობიდან 6 საათში ერთხელ აგზავნის ანონიმურ ჩანაწერს (შემთხვევითი ID-ის ჰეში, ვერსია, ენა, ნოტიფიკაციის ნებართვა). სახელი, ტელეფონი ან მდებარეობა არ იგზავნება. მონაცემები ითვლება აპის ამ ვერსიიდან.</p>
""";
        return Layout(s, "devices", "მოწყობილობები", "ბოლო 30 დღე", content, message, ok);
    }

    // ============================================================
    // LOG
    // ============================================================

    public static string Log(AdminService.Session s, List<AdminService.AuditRow> log, string? message, bool ok)
    {
        var rows = string.Concat(log.Select(r =>
        {
            var bad = r.Action.Contains("failed") || r.Action.Contains("blocked");
            return $"<tr><td>{Tbilisi(r.At)}</td><td><span class=\"pill {(bad ? "p-err" : "p-acc")}\">{H(AdminService.ActionName(r.Action))}</span></td><td>{H(r.Email)}</td><td class=\"mono\">{H(r.Target)}</td><td class=\"mono\">{H(r.Ip)}</td></tr>";
        }));
        var content = log.Count == 0
            ? "<div class=\"panel muted\">ჩანაწერები ჯერ არ არის.</div>"
            : $"<div class=\"tbl\"><table><tr><th>დრო</th><th>მოქმედება</th><th>ვინ</th><th>ობიექტი</th><th>IP</th></tr>{rows}</table></div>";
        return Layout(s, "log", "ჟურნალი", "ყველა შესვლა, შესვლის მცდელობა და ადმინის მოქმედება (ბოლო 200)", content, message, ok);
    }

    // ============================================================
    // SECURITY
    // ============================================================

    public static string Security(AdminService.Session s, string newSecret, string? message, bool ok)
    {
        var uri = $"otpauth://totp/GCar360:{Uri.EscapeDataString(s.Email)}?secret={newSecret}&issuer=GCar360&digits=6&period=30";
        var twoStep = AdminService.TwoStepEnabled
            ? "<div class=\"msg ok\">✓ ორსაფეხურიანი დაცვა ჩართულია. შესვლისას Google Authenticator-ის კოდი სჭირდება.</div>"
            : $$"""
<div class="panel">
<b>ორსაფეხურიანი დაცვის ჩართვა (2 წუთი)</b>
<ol>
<li>ტელეფონზე დააყენე <b>Google Authenticator</b> (ან Microsoft Authenticator).</li>
<li>აპში: „+“ → „Enter a setup key“ → სახელი <b>GCar360</b>, გასაღები:<div class="mono" style="font-size:16px;margin:8px 0;padding:10px;border:1px dashed var(--line);border-radius:10px">{{H(newSecret)}}</div>ტიპი: Time based.</li>
<li>Azure → App Service → Environment variables → <b>Add</b>: სახელი <span class="mono">GCS_ADMIN_TOTP_SECRET</span>, მნიშვნელობა — იგივე გასაღები. Apply → Confirm.</li>
<li>აპლიკაცია თავიდან ჩაიტვირთება. შემდეგი შესვლიდან მეილს და პაროლს დაემატება 6-ნიშნა კოდი.</li>
</ol>
<p class="muted">ეს გასაღები ყოველ ჯერზე ახალია — გამოიყენე ის, რაც ახლა ჩანს, და არავის აჩვენო. ტელეფონის დაკარგვის შემთხვევაში Azure-ში წაშალე GCS_ADMIN_TOTP_SECRET და თავიდან ჩართე.</p>
<details><summary class="muted">otpauth ბმული (ზოგი აპი ამასაც იღებს)</summary><div class="mono">{{H(uri)}}</div></details>
</div>
""";

        var ips = Environment.GetEnvironmentVariable("GCS_ADMIN_IPS");
        var content = $$"""
{{twoStep}}
<h2>დაცვის სხვა ფენები</h2>
<div class="panel"><ul style="margin:0;padding-left:18px">
<li>შესვლა მხოლოდ <span class="mono">GCS_ADMIN_EMAILS</span>-ში ჩაწერილ მეილს შეუძლია.</li>
<li>15 წუთში 8-ზე მეტი არასწორი მცდელობა — IP იბლოკება. ყველა მცდელობა იწერება „ჟურნალში“.</li>
<li>სესია 4 საათშია ვადაგასული და მიბმულია ამ ბრაუზერზე — სხვა ბრაუზერში cookie არ იმუშავებს.</li>
<li>ყველა ღილაკი დაცულია CSRF ტოკენით; გვერდი არ იხსნება სხვა საიტის შიგნით და საძიებოებში არ ჩანს.</li>
<li>IP შეზღუდვა: {{(string.IsNullOrWhiteSpace(ips) ? "გამორთულია. თუ მუდამ ერთი ინტერნეტიდან შედიხარ, Azure-ში დაამატე <span class=\"mono\">GCS_ADMIN_IPS</span> = შენი IP (რამდენიმე — მძიმით)." : "ჩართულია: <span class=\"mono\">" + H(ips) + "</span>")}}</li>
</ul></div>

<h2>Google Play-ის სტატისტიკის ჩართვა (გადმოწერა / წაშლა)</h2>
<div class="panel"><ol style="margin:0;padding-left:18px">
<li>Play Console → <b>Download reports</b> → Statistics → დააჭირე <b>„Copy Cloud Storage URI“</b> (მსგავსია: gs://pubsite_prod_rev_0123…).</li>
<li>Play Console → <b>Users and permissions</b> → service account-ს (რომელიც გადახდების შესამოწმებლად გაქვს) მიეცი უფლება <b>„View app information and download bulk reports“</b>.</li>
<li>Azure → Environment variables → <span class="mono">GCS_PLAY_REPORTS_BUCKET</span> = დაკოპირებული URI.</li>
</ol><p class="muted" style="margin-bottom:0">რამდენიმე საათში „მიმოხილვაზე“ გამოჩნდება გადმოწერები, წაშლები და აქტიური მოწყობილობები.</p></div>
""";
        return Layout(s, "security", "უსაფრთხოება", "როგორაა დაცული ეს გვერდი", content, message, ok);
    }
}
