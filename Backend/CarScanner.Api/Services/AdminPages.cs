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

    /// <summary>
    /// The only script on admin pages. Static (no user data); allowed by its
    /// SHA-256 hash in the CSP (AdminService.SecureHeaders). Forms that need a
    /// confirmation carry the HTML-attribute-encoded text in data-confirm.
    /// </summary>
    public const string ConfirmScript =
        "document.addEventListener('submit',function(e){var f=e.target;if(f&&f.getAttribute){var m=f.getAttribute('data-confirm');if(m&&!window.confirm(m)){e.preventDefault();}}},true);";

    /// <summary>Attribute-encoded data-confirm="…" for a form.</summary>
    private static string Confirm(string message) => " data-confirm=\"" + H(message) + "\"";

    private static string Page(string title, string body) => $$"""
<!doctype html><html lang="ka"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex,nofollow">
<title>{{H(title)}} · OBD360 ადმინი</title>{{Style}}</head><body>{{body}}<script>{{ConfirmScript}}</script></body></html>
""";

    // ============================================================
    // LOGIN
    // ============================================================

    public static string Login(string? error)
    {
        if (!AdminService.TwoStepEnabled)
            return Page("შესვლა", $$"""
<div class="login"><div class="panel">
<div style="font-weight:800;font-size:20px;margin-bottom:4px">OBD<span style="color:var(--accent)">360</span> · ადმინი</div>
<div class="msg err">შესვლა გამორთულია: ორსაფეხურიანი დაცვა სავალდებულოა. Set GCS_ADMIN_TOTP_SECRET.<br><span class="muted">Admin sign-in is disabled until GCS_ADMIN_TOTP_SECRET is set on the server.</span></div>
<ol class="muted" style="padding-left:18px">
<li>ტელეფონზე დააყენე Google Authenticator → „+“ → „Enter a setup key“, გასაღები:<div class="mono" style="font-size:15px;margin:8px 0;padding:10px;border:1px dashed var(--line);border-radius:10px">{{H(AdminService.NewTotpSecret())}}</div></li>
<li>Azure → App Service → Environment variables → <span class="mono">GCS_ADMIN_TOTP_SECRET</span> = იგივე გასაღები → Apply.</li>
<li>აპლიკაციის გადატვირთვის შემდეგ შედი მეილით, პაროლით და 6-ნიშნა კოდით.</li>
</ol>
</div></div>
""");
        return LoginForm(error);
    }

    private static string LoginForm(string? error) => Page("შესვლა", $$"""
<div class="login"><div class="panel">
<div style="font-weight:800;font-size:20px;margin-bottom:4px">OBD<span style="color:var(--accent)">360</span> · ადმინი</div>
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

    private static readonly (string Key, string Name)[] Tabs =
    {
        ("overview", "მიმოხილვა"),
        ("users", "მომხმარებლები"),
        ("subs", "გამოწერები"),
        ("payments", "გადახდები"),
        ("devices", "მოწყობილობები"),
        ("acceleration", "აჩქარების რეიტინგი"),
        ("service", "სერვისის სწავლება"),
        ("odometer", "გარბენის ადგილები"),
        ("ecu", "ECU მონაცემები"),
        ("crashes", "აპის შეცდომები"),
        ("log", "ჟურნალი"),
        ("security", "უსაფრთხოება"),
    };

    private static string Layout(AdminService.Session s, string tab, string title, string subtitle, string content, string? message, bool ok)
    {
        var nav = string.Concat(Tabs.Select(t =>
            $"<a class=\"{(t.Key == tab ? "on" : "")}\" href=\"/admin?tab={t.Key}\">{t.Name}</a>"));
        var note = message is null ? "" : $"<div class=\"msg {(ok ? "ok" : "err")}\">{H(message)}</div>";
        var twoStep = AdminService.TwoStepEnabled
            ? ""
            : "<div class=\"warnbox\">ორსაფეხურიანი დაცვა ჯერ ჩართული არ არის. გადადი <a href=\"/admin?tab=security\">„უსაფრთხოება“</a>-ზე და ჩართე — 2 წუთი სჭირდება.</div>";

        return Page(title, $$"""
<div class="shell"><aside><div class="brand">OBD<span>360</span></div>{{nav}}
<div class="who">{{H(s.Email)}}<form method="post" action="/admin/logout"><input type="hidden" name="csrf" value="{{H(s.Csrf)}}"><button class="ghost">გასვლა</button></form></div></aside>
<main><h1>{{H(title)}}</h1><p class="sub">{{subtitle}}</p>{{twoStep}}{{note}}{{content}}</main></div>
""");
    }

    private static string Kpi(string label, string value, string hint, string cls = "") =>
        $"<div class=\"kpi {cls}\"><div class=\"l\">{label}</div><div class=\"v\">{value}</div><div class=\"h\">{hint}</div></div>";

    /// <summary>ECU vs GPS speed chart, GPS difference, map link and the review video.</summary>
    private static string Evidence(AccelerationLeaderboardAdminItem item)
    {
        static List<double[]> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try { return System.Text.Json.JsonSerializer.Deserialize<List<double[]>>(json) ?? new(); }
            catch (System.Text.Json.JsonException) { return new(); }
        }

        var speed = Parse(item.SpeedTrace).Where(p => p.Length >= 2).ToList();
        var gps = Parse(item.GpsTrace).Where(p => p.Length >= 2).ToList();
        var html = new StringBuilder();

        if (speed.Count > 1)
        {
            const double w = 320, h = 120, pad = 4;
            var maxT = Math.Max(0.1, speed.Concat(gps).Max(p => p[0]));
            var minT = Math.Min(0, speed.Concat(gps).Min(p => p[0]));
            var maxV = Math.Max(10, speed.Concat(gps).Max(p => p[1]));
            string Line(IEnumerable<double[]> points) => string.Join(" ", points.Select(p =>
                $"{(pad + (p[0] - minT) / (maxT - minT) * (w - 2 * pad)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)},{(h - pad - p[1] / maxV * (h - 2 * pad)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"));
            html.Append($"""
<svg viewBox="0 0 {w} {h}" width="{w}" height="{h}" style="background:#f6f7f9;border-radius:8px;display:block;margin:6px 0">
<polyline fill="none" stroke="#1d5bd6" stroke-width="2" points="{Line(speed)}"/>
<polyline fill="none" stroke="#1f8a4c" stroke-width="2" stroke-dasharray="4 3" points="{Line(gps)}"/>
</svg><div class="muted"><span style="color:#1d5bd6">━ ECU</span> · <span style="color:#1f8a4c">┅ GPS</span> · მაქს. {maxV:0} km/h · სხვაობა {(item.GpsDiffKph is { } d ? d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "—")} km/h</div>
""");
        }

        var start = gps.FirstOrDefault(p => p.Length >= 4 && Math.Abs(p[2]) > 0.0001);
        if (start is not null)
        {
            var lat = start[2].ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture);
            var lon = start[3].ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture);
            html.Append($"<div><a href=\"https://www.google.com/maps?q={lat},{lon}\" target=\"_blank\" rel=\"noopener\">სტარტის ადგილი რუკაზე</a></div>");
        }

        if (item.HasVideo)
            html.Append($"<video controls preload=\"metadata\" width=\"320\" style=\"border-radius:8px;margin-top:6px\" src=\"/admin/acceleration/video/{Uri.EscapeDataString(item.AccountId)}/{Uri.EscapeDataString(item.SubmissionId)}\"></video>");
        else if (item.Status == "Pending")
            html.Append("<div class=\"muted\">ვიდეო ჯერ არ ატვირთულა</div>");

        return html.ToString();
    }

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
                "Approved" => "<span class=\"pill p-ok\">რეიტინგშია</span>",
                "Verified" => "<span class=\"pill p-ok\">Verified</span>",
                "Rejected" => "<span class=\"pill p-err\">უარყოფილი</span>",
                _ => item.HasVideo
                    ? "<span class=\"pill p-warn\">მოწმდება · ვიდეო მოვიდა</span>"
                    : "<span class=\"pill p-warn\">მოწმდება · ვიდეოს ელოდება</span>"
            };
            var demoPill = item.IsDemo ? " <span class=\"pill p-acc\">DEMO</span>" : string.Empty;
            // The row is identified by SubmissionId AND AccountId (ids are unique only per account).
            var sid = Uri.EscapeDataString(item.SubmissionId);
            var owner = $"<input type=\"hidden\" name=\"account\" value=\"{H(item.AccountId)}\">";
            var approve = $"<form class=\"inline\" method=\"post\" action=\"/admin/acceleration/{sid}/approve\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\">{owner}<button class=\"green\">დამოწმება</button></form>";
            var reject = $"<form class=\"inline\" method=\"post\" action=\"/admin/acceleration/{sid}/reject\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\">{owner}<button>უარყოფა</button></form>";
            var delete = $"<form class=\"inline\" method=\"post\" action=\"/admin/acceleration/{sid}/delete\"{Confirm("ნამდვილად წაიშალოს ეს შედეგი?")}><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\">{owner}<button class=\"red\">წაშლა</button></form>";
            rows.Append($"""
<tr><td><b>{H(item.UserName)}</b>{demoPill}<div class="muted mono">{H(item.SubmissionId)}</div></td>
<td>{H(item.VehicleModel)}</td><td><b>{item.DurationSeconds:0.00} წმ</b><br><small>{H(item.TestId == "quarter-mile" ? $"¼ მილი · {item.MaxSpeedKph} km/h" : item.TestId)}</small></td><td>{item.SampleCount}</td>
<td>{Evidence(item)}</td><td>{status}</td><td>{item.FinishedAt.ToLocalTime():dd.MM.yyyy HH:mm}</td>
<td>{approve}{reject}{delete}</td></tr>
""");
        }

        var content = $"""
<div class="grid">
{Kpi("მოლოდინში", N(pending), "შენს გადაწყვეტილებას ელოდება", pending > 0 ? "bad" : "")}
{Kpi("რეიტინგში", N(approved), "საჯარო რეიტინგში ჩანს", "good")}
{Kpi("DEMO", N(demo), "სატესტო ჩანაწერი")}
</div>
<h2>ყველა შედეგი</h2>
<div class="tbl"><table><thead><tr><th>მომხმარებელი</th><th>მანქანა</th><th>დრო</th><th>ECU ნიმუში</th><th>მტკიცებულება (ECU / GPS / ვიდეო)</th><th>სტატუსი</th><th>თარიღი</th><th>მართვა</th></tr></thead>
<tbody>{rows}</tbody></table></div>
""";
        return Layout(s, "acceleration", "აჩქარების რეიტინგი", "ყველა შედეგი GPS-ით მოწმდება · ტოპ-10 და საეჭვო შედეგები „მოწმდება“ სტატუსით ჩნდება და ვიდეოს ელოდება · „დამოწმება“ ვიდეოთი = Verified · ვიდეო და GPS კოორდინატები განხილვიდან 30 დღეში იშლება", content, message, ok);
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
                : $"<form class=\"inline\" method=\"post\" action=\"/admin/users/{H(u.AccountId)}/ban\"{Confirm($"დაბლოკო {u.Email}? ვეღარ შევა აპში.")}><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><input type=\"text\" name=\"reason\" placeholder=\"მიზეზი\" style=\"width:110px\"><button class=\"red\">დაბლოკვა</button></form>";

            actions += $"<form class=\"inline\" method=\"post\" action=\"/admin/users/{H(u.AccountId)}/temppass\"{Confirm($"დროებითი პაროლი: {u.Email}? ძველი პაროლი აღარ იმუშავებს.")}><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button>დროებითი პაროლი</button></form>";

            actions += $"<form class=\"inline\" method=\"post\" action=\"/admin/users/{H(u.AccountId)}/unbind\"{Confirm($"მოვხსნა მოწყობილობის მიბმა: {u.Email}?")}><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button>მოწყობილობის მოხსნა</button></form>";

            actions += $"""<form class="inline" method="post" action="/admin/users/{H(u.AccountId)}/grant"><input type="hidden" name="csrf" value="{csrf}"><select name="product"><option value="{ProductCatalog.FullMonthly}">სრული PRO</option><option value="{ProductCatalog.ObdMonthly}">OBD PRO</option><option value="{ProductCatalog.VinMonthly}">VIN PRO</option></select><input type="number" name="days" value="30" min="1" max="365"><button>PRO-ს მიცემა</button></form>""";

            rows.Append($"""
<tr class="{(u.Banned ? "banned" : "")}">
<td><b>{H(u.Email)}</b><div class="muted mono">{H(u.AccountId)}</div></td>
<td>{status}</td>
<td>{Tbilisi(u.CreatedAt)}</td>
<td>{(u.LastSeen is { } ls ? Tbilisi(ls) : "<span class=\"muted\">—</span>")}</td>
<td>{(string.IsNullOrEmpty(u.Plans) ? "<span class=\"muted\">უფასო</span>" : H(u.Plans))}{(u.Credits > 0 ? $"<div class=\"muted\">+{u.Credits} VIN კრედიტი</div>" : "")}</td>
<td>{u.Devices}{(u.NotificationsAllowed ? " ·" : "")}</td>
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
<p class="muted">„დაბლოკვა“ — ანგარიში აპიდან გამოვა და ვეღარ შევა. „PRO-ს მიცემა“ — უფასოდ ჩართავს არჩეულ გეგმას მითითებული დღეებით. „დროებითი პაროლი“ — ახალ პაროლს ერთხელ აჩვენებს (მეილის გაგზავნა სერვერს არ შეუძლია), ყველა სესიას ასრულებს და მოწყობილობას ხსნის. მომხმარებელს ახალ ტელეფონზე გადასვლა თავადაც შეუძლია (7 დღეში ერთხელ).</p>
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
                actions.Append($"<form class=\"inline\" method=\"post\" action=\"/admin/orders/{H(o.OrderId)}/refund\"{Confirm("დავუბრუნოთ " + o.Amount.ToString("0.00", CultureInfo.InvariantCulture) + " ₾ ბარათზე? ამის გაუქმება შეუძლებელია.")}><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button class=\"red\">დაბრუნება</button></form>");

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
    // CRASHES
    // ============================================================

    // ============================================================
    // ODOMETER PLACES (app "odometer finder")
    // ============================================================

    public static string Odometer(AdminService.Session s, IReadOnlyList<OdometerGroup> groups, string? message, bool ok)
    {
        static string Describe(string key)
        {
            var parts = key.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            string Get(string k) => parts.TryGetValue(k, out var v) ? v : "?";
            var module = Uri.UnescapeDataString(Get("mod"));
            var request = Get("svc") == "1A" ? $"1A {Get("id")}" : $"22 {Get("id").PadLeft(4, '0')}";
            return $"<b>{H(module)}</b> · TX 0x{H(Get("tx"))} → RX 0x{H(Get("rx"))} · <span class=\"mono\">{H(request)}</span> · ბაიტები {H(Get("off"))}+{H(Get("len"))}{(Get("le") == "1" ? " (LE)" : "")} · ×{H(Get("sc"))}{(Get("mi") == "1" ? " · მილი" : " · კმ")}";
        }

        string Buttons(OdometerGroup g)
        {
            string Form(string action, string label, string cls) =>
                $"<form method=\"post\" action=\"/admin/odometer/{g.Id}/{action}\" style=\"display:inline\"><input type=\"hidden\" name=\"csrf\" value=\"{H(s.Csrf)}\"><button class=\"{cls}\">{label}</button></form> ";
            return g.Status switch
            {
                "approved" => Form("reset", "მოლოდინში დაბრუნება", "ghost") + Form("reject", "უარყოფა", "ghost"),
                "rejected" => Form("reset", "მოლოდინში დაბრუნება", "ghost") + Form("approve", "დადასტურება", ""),
                _ => Form("approve", "დადასტურება", "") + Form("reject", "უარყოფა", "ghost")
            };
        }

        static string StatusPill(OdometerGroup g) => g.Status switch
        {
            "approved" => "<span class=\"pill p-acc\">დადასტურებული · ყველასთვის</span>",
            "rejected" => "<span class=\"pill p-err\">უარყოფილი</span>",
            _ when OdometerStore.AutoEnabled(g) => "<span class=\"pill p-acc\">ავტომატურად ჩართული (≥2 ანგარიში სხვადასხვა ტელეფონიდან/ქსელიდან ან ადმინი)</span>",
            _ => "<span class=\"pill\">მოლოდინში</span>"
        };

        var rows = string.Concat(groups.Select(g =>
            $"<tr><td><b>{H(g.Make)} {H(g.Model)}</b><div class=\"muted\">წლები: {H(string.Join(", ", g.Years))}</div></td>" +
            $"<td>{Describe(g.Key)}</td><td>{N(g.Devices)} · ანგარიში {N(g.Accounts)}{(g.Trusted ? " · ადმინი" : "")}</td><td>{StatusPill(g)}</td><td>{Buttons(g)}</td></tr>"));

        var content =
            "<div class=\"grid\" style=\"margin-bottom:12px\">" +
            Kpi("ადგილები", N(groups.Count), "სულ") +
            Kpi("მოლოდინში", N(groups.Count(g => g.Status == "pending")), "გადასახედი") +
            Kpi("დადასტურებული", N(groups.Count(g => g.Status == "approved")), "ყველასთვის") +
            "</div>" +
            (groups.Count == 0 ? "<div class=\"panel muted\">ჯერ არავის უპოვია.</div>"
                : $"<div class=\"tbl\"><table><tr><th>მოდელი</th><th>ადგილი</th><th>ტელეფონი</th><th>სტატუსი</th><th></th></tr>{rows}</table></div>") +
            "<p class=\"muted\">აპი აგზავნის მხოლოდ ადგილს (მოდული, მოთხოვნა, ბაიტები, მასშტაბი) — არა გარბენს და არა სრულ VIN-ს. ადგილი ამ მოდელზე ყველასთვის მუშაობს, როცა დაადასტურებ, როცა შენმა ადმინის ანგარიშმა იპოვა, ან როცა 2 სხვადასხვა შესულმა ანგარიშმა იპოვა სხვადასხვა ტელეფონიდან და სხვადასხვა ქსელიდან. ანონიმური ტელეფონები მას თავისით ვერ ჩართავენ. „უარყოფა“ მას სამუდამოდ თიშავს.</p>";

        return Layout(s, "odometer", "გარბენის ადგილები", "აპის „გარბენის ძებნის“ შედეგები მოდელების მიხედვით", content, message, ok);
    }

    // ============================================================
    // ECU OBSERVATIONS (opt-in, crowd-sourced raw module answers — the
    // raw material for adding ABS / service-coding / hybrid support to
    // a new make or model). Grouped by vehicle so one glance shows what
    // exists for e.g. "CHEVROLET VOLT" or "TOYOTA PRIUS".
    // ============================================================

    /// <summary>"ABS / მუხრუჭები", "სტანდარტული OBD2" (the 0x7DF broadcast), or "" (shown as "დაუჯგუფებელი") when the TX/RX pair is not in <see cref="ModuleAddressLabels"/>.</summary>
    private static string EcuCategory(EcuObservationRow r) =>
        r.TxCanId == 0x7DF ? "სტანდარტული OBD2" : ModuleAddressLabels.Label(r.Make, r.TxCanId, r.RxCanId);

    public static string EcuObservations(AdminService.Session s, IReadOnlyList<EcuObservationRow> rows, string? message, bool ok)
    {
        const string Unclassified = "დაუჯგუფებელი";
        var csrf = H(s.Csrf);
        var vehicles = rows
            .GroupBy(r => (r.Make, r.Model, r.Year))
            .OrderByDescending(g => g.Max(r => r.SourceCount))
            .ThenByDescending(g => g.Count())
            .ToList();

        static string RelPill(string reliability) => reliability switch
        {
            "verified" => "<span class=\"pill p-acc\">დამოწმებული</span>",
            "corroborated" => "<span class=\"pill p-ok\">დადასტურებული (3+)</span>",
            "repeated" => "<span class=\"pill p-warn\">გამეორებული (2)</span>",
            _ => "<span class=\"pill p-mute\">ერთჯერადი</span>"
        };

        string Row(EcuObservationRow r)
        {
            var response = r.Response.Length > 80 ? r.Response[..80] + "…" : r.Response;
            var verify = r.Review == "verified"
                ? $"<form class=\"inline\" method=\"post\" action=\"/admin/ecu/{r.Id}/unverify\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button class=\"ghost\">დამოწმების მოხსნა</button></form>"
                : $"<form class=\"inline\" method=\"post\" action=\"/admin/ecu/{r.Id}/verify\"{Confirm("ამ პასუხს ვადასტურებ, როგორც დოკუმენტირებულს — ამაზე ფუნქციის გაშენება შეიძლება?")}><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\"><button class=\"green\">დამოწმება</button></form>";

            return $"""
<tr><td class="mono">{r.TxCanId:X}→{r.RxCanId:X}{(r.Ext29 ? " ·29b" : "")}</td>
<td class="mono">{H(r.Request)}</td>
<td class="mono" style="max-width:220px;white-space:normal;word-break:break-all">{H(response)}</td>
<td>{H(r.Kind)} · {H(r.Status)}</td>
<td>{N(r.SourceCount)} წყარო · {N(r.SeenCount)}×</td>
<td>{RelPill(r.Reliability)}</td>
<td class="muted">{H(r.LastSeen)}</td>
<td>{verify}</td></tr>
""";
        }

        string CategorySection(string label, IEnumerable<EcuObservationRow> items)
        {
            var list = items.ToList();
            var rowsHtml = string.Concat(list.OrderByDescending(r => r.SourceCount).ThenBy(r => r.Request, StringComparer.Ordinal).Select(Row));
            var verified = list.Count(r => r.Review == "verified");
            return $"""
<details class="panel" style="margin:8px 0 0 10px">
<summary><b>{H(label)}</b> · {N(list.Count)} პასუხი{(verified > 0 ? $" · <span class=\"pill p-acc\">{N(verified)} დამოწმებული</span>" : "")}</summary>
<div class="tbl" style="margin-top:10px"><table><tr><th>მოდული (TX→RX)</th><th>მოთხოვნა</th><th>პასუხი</th><th>ტიპი</th><th>წყარო</th><th>სტატუსი</th><th>ბოლოს</th><th>მოქმედება</th></tr>{rowsHtml}</table></div>
</details>
""";
        }

        var body = vehicles.Count == 0
            ? "<div class=\"panel muted\">მონაცემები ჯერ არ არის — ჩაირთვება, როცა მომხმარებლები დათანხმდებიან მონაცემების გაზიარებას.</div>"
            : string.Concat(vehicles.Select(g =>
            {
                var categories = g
                    .GroupBy(r => { var c = EcuCategory(r); return string.IsNullOrEmpty(c) ? Unclassified : c; })
                    .OrderBy(c => c.Key == Unclassified ? 1 : 0).ThenByDescending(c => c.Count());
                var sectionsHtml = string.Concat(categories.Select(c => CategorySection(c.Key, c)));
                var verified = g.Count(r => r.Review == "verified");
                return $"""
<details class="panel" style="margin-bottom:10px">
<summary><b>{H(g.Key.Make)} {H(g.Key.Model)} {H(g.Key.Year)}</b> · {N(g.Count())} განსხვავებული პასუხი{(verified > 0 ? $" · <span class=\"pill p-acc\">{N(verified)} დამოწმებული</span>" : "")} · ბოლო აქტივობა {H(g.Max(r => r.LastSeen))}</summary>
{sectionsHtml}
</details>
""";
            }));

        var content = $"""
<div class="grid" style="margin-bottom:12px">
{Kpi("მანქანა", N(vehicles.Count), "მარკა/მოდელი/წელი")}
{Kpi("განსხვავებული პასუხი", N(rows.Count), "სულ")}
{Kpi("დამოწმებული", N(rows.Count(r => r.Review == "verified")), "ფუნქციის გაშენებისთვის მზადაა", "good")}
{Kpi("დადასტურებული (3+)", N(rows.Count(r => r.Reliability == "corroborated")), "3+ დამოუკიდებელი წყარო")}
</div>
<p class="muted">თითო ჯერზე მომხმარებელი აგზავნის მხოლოდ წაკითხვის პასუხებს (VIN და სერიული ნომრები გაფილტრულია) — თანხმობის შემდეგ ფონურად, ხელით არაფერი არ ჩაწერია. ყველა მანქანა დაჯგუფებულია მარკა → მოდელი → წელი → სისტემა (ABS, ძრავი, ჰიბრიდი და ა.შ.) მიხედვით — კატეგორია დგინდება ცნობილი მისამართების ცხრილით, არა გამოცნობით; რაც ცხრილში არ არის, „დაუჯგუფებელშია“. „დამოწმება“ = შემოწმებულია დოკუმენტაციასთან/რეალურ ტესტთან და შეიძლება მასზე ფუნქციის გაშენება.</p>
{body}
""";
        return Layout(s, "ecu", "ECU მონაცემები", "მარკა/მოდელი/წლისა და სისტემის მიხედვით დაჯგუფებული, თანხმობით შემოსული დიაგნოსტიკური პასუხები", content, message, ok);
    }

    public static string Crashes(AdminService.Session s, IReadOnlyList<CrashGroup> groups, string? message, bool ok)
    {
        var total = groups.Sum(g => g.Count);
        var fatal = groups.Sum(g => g.FatalCount);
        var rows = string.Concat(groups.Select(g =>
            "<details class=\"panel\" style=\"margin-bottom:8px\"><summary>" +
            $"<span class=\"pill {(g.FatalCount > 0 ? "p-err" : "p-acc")}\">{N(g.Count)}×</span> <b>{H(g.ExceptionType)}</b>" +
            $"<div class=\"mono muted\" style=\"margin-top:4px\">{H(g.Frame)}</div>" +
            $"<div class=\"muted\" style=\"margin-top:4px\">ბოლოს: {Tbilisi(g.LastAt)} · ვერსია: {H(string.Join(", ", g.AppVersions))} · {H(string.Join(", ", g.Platforms))} {H(g.OsVersion)} · " +
            (g.FatalCount > 0 ? "აპი დაიხურა: " + N(g.FatalCount) : "აპი არ დახურულა") + "</div></summary>" +
            $"<pre class=\"mono\" style=\"white-space:pre-wrap;font-size:11px;margin-top:8px\">{H(g.SampleStack)}</pre></details>"));

        var content =
            "<div class=\"grid\" style=\"margin-bottom:12px\">" +
            Kpi("შეცდომა · 30 დღე", N(total), "ყველა ჩანაწერი", total > 50 ? "bad" : "") +
            Kpi("აპი დაიხურა", N(fatal), "fatal", fatal > 10 ? "bad" : "") +
            Kpi("სხვადასხვა შეცდომა", N(groups.Count), "ჯგუფი") +
            "</div>" +
            (groups.Count == 0 ? "<div class=\"panel muted\">ბოლო 30 დღეში შეცდომა არ მოსულა.</div>" : rows) +
            "<p class=\"muted\">აპი შეცდომას ინახავს და შემდეგ გახსნისას აგზავნის: მხოლოდ ტიპი, კოდის ხაზები, ვერსია და პლატფორმა. მანქანის, ანგარიშის ან შეტყობინების ტექსტი არ იგზავნება.</p>";

        return Layout(s, "crashes", "აპის შეცდომები", "ბოლო 30 დღე · ერთნაირი შეცდომები ერთადაა", content, message, ok);
    }

    // ============================================================
    // SERVICE LEARNING (service search results + reset recordings)
    // ============================================================

    public static string ServiceLearning(
        AdminService.Session s,
        IReadOnlyList<ServiceSearchRow> searches,
        IReadOnlyList<ResetRecordingRow> recordings,
        string? message,
        bool ok)
    {
        static string At(string iso) => DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? Tbilisi(at) : iso;
        static string Pct(double? v) => v is { } x ? $"{x:0.#}%" : "—";

        var recRows = string.Concat(recordings.Select(r =>
            "<details class=\"panel\" style=\"margin-bottom:8px\"><summary>" +
            $"<span class=\"pill {(r.Candidates.Count > 0 ? "p-acc" : "p-err")}\">{N(r.Candidates.Count)} კანდიდატი</span> " +
            $"<b>{H(r.Make)} {H(r.Model)} {H(r.Year)}</b> · {H(r.Item)} · {(r.Mode == "ToolSession" ? "აპარატით" : "მენიუდან")}" +
            $"<div class=\"muted\" style=\"margin-top:4px\">{At(r.CreatedAt)} · VIN {H(r.VinPrefix)}… · ზეთი {Pct(r.ValueBefore)} → {Pct(r.ValueAfter)} · {N(r.FrameCount)} შეტყობინება{(r.Overflow ? " · ადაპტერი ვერ ასწრებდა" : "")}</div></summary>" +
            "<div class=\"tbl\" style=\"margin-top:8px\"><table><tr><th>ID</th><th>მონაცემი</th><th>ტიპი</th><th>შენიშვნა</th></tr>" +
            string.Concat(r.Candidates.Select(c => $"<tr><td class=\"mono\">{H(c.Id)}</td><td class=\"mono\">{H(c.Data)}</td><td>{H(c.Kind)}</td><td class=\"muted\">{H(c.Note)}</td></tr>")) +
            $"</table></div><p><a href=\"/admin/reset-recordings/{r.Id}.txt\">სრული ჩანაწერი (.txt)</a></p></details>"));

        var searchRows = string.Concat(searches.Select(r =>
        {
            int matched;
            try { matched = System.Text.Json.JsonSerializer.Deserialize<string[]>(r.MatchedKeys)?.Length ?? 0; }
            catch (System.Text.Json.JsonException) { matched = 0; }
            return $"<tr><td>{At(r.CreatedAt)}</td><td>{H(r.Make)} {H(r.Model)} {H(r.Year)}</td><td class=\"mono\">{H(r.VinPrefix)}</td><td>{Pct(r.DashboardPercent)}</td><td>{N(r.Answered)}/{N(r.Tried)}</td>" +
                   $"<td><span class=\"pill {(matched > 0 ? "p-acc" : "p-err")}\">{(matched > 0 ? "" + N(matched) : "—")}</span>{(r.Trusted ? " <span class=\"pill p-acc\">ადმინი</span>" : "")}</td></tr>";
        }));

        var content =
            "<div class=\"grid\" style=\"margin-bottom:12px\">" +
            Kpi("რესეტის ჩანაწერი", N(recordings.Count), "ბოლო 100") +
            Kpi("ზეთის ძებნა", N(searches.Count), "ბოლო 200") +
            Kpi("ნაპოვნი წყარო", N(searches.Count(x => x.MatchedKeys != "[]")), "პანელს დაემთხვა") +
            "</div>" +
            "<h3>რესეტის ჩანაწერები</h3>" +
            (recordings.Count == 0 ? "<div class=\"panel muted\">ჩანაწერები ჯერ არ არის.</div>" : recRows) +
            "<p class=\"muted\">ჩანაწერში მხოლოდ ის არის, რაც ადაპტერმა მოისმინა. ბრძანება მანქანაზე მხოლოდ შემოწმების შემდეგ, აპის ახალი ვერსიით ჩაირთვება.</p>" +
            "<h3>ზეთის ძებნა (მხოლოდ კითხვა)</h3>" +
            (searches.Count == 0 ? "<div class=\"panel muted\">ძებნა ჯერ არავის გაუშვია.</div>"
                : $"<div class=\"tbl\"><table><tr><th>დრო</th><th>მანქანა</th><th>VIN</th><th>პანელი</th><th>უპასუხა</th><th>დაემთხვა</th></tr>{searchRows}</table></div>") +
            "<p class=\"muted\">წყარო მოდელისთვის „ცნობილი“ ხდება, როცა 2 სხვადასხვა ტელეფონი (ან ადმინის ანგარიში) დაადასტურებს — მერე იმავე მოდელზე აპი მას ძებნის გარეშე იყენებს.</p>";

        return Layout(s, "service", "სერვისის სწავლება", "ზეთის ძებნის შედეგები და რესეტის ჩანაწერები", content, message, ok);
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
        var uri = $"otpauth://totp/OBD360:{Uri.EscapeDataString(s.Email)}?secret={newSecret}&issuer=OBD360&digits=6&period=30";
        var twoStep = AdminService.TwoStepEnabled
            ? "<div class=\"msg ok\">ორსაფეხურიანი დაცვა ჩართულია. შესვლისას Google Authenticator-ის კოდი სჭირდება.</div>"
            : $$"""
<div class="panel">
<b>ორსაფეხურიანი დაცვის ჩართვა (2 წუთი)</b>
<ol>
<li>ტელეფონზე დააყენე <b>Google Authenticator</b> (ან Microsoft Authenticator).</li>
<li>აპში: „+“ → „Enter a setup key“ → სახელი <b>OBD360</b>, გასაღები:<div class="mono" style="font-size:16px;margin:8px 0;padding:10px;border:1px dashed var(--line);border-radius:10px">{{H(newSecret)}}</div>ტიპი: Time based.</li>
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
