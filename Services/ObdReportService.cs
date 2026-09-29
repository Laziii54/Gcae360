using CarScanner.Models;
using System.Text;
using System.Text.Json;

namespace CarScanner.Services;

public sealed class ObdReportService
{
    private readonly PdfReportService _pdf = new();
    public string BuildText(ObdScanReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GEORGIAN CAR SCANNER");
        sb.AppendLine(new string('=', 64));
        sb.AppendLine($"Scan: {report.StartedAt:yyyy-MM-dd HH:mm:ss} - {report.FinishedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Vehicle: {report.Vehicle.Manufacturer} {report.Vehicle.Model} {report.Vehicle.Year}");
        sb.AppendLine($"VIN: {report.Vehicle.VIN}");
        sb.AppendLine($"Engine: {report.Vehicle.Engine}");
        sb.AppendLine($"Fuel: {report.Vehicle.FuelType}");
        sb.AppendLine($"Protocol: {report.Protocol}");
        if (!string.IsNullOrWhiteSpace(report.AdapterName)) sb.AppendLine($"Adapter: {report.AdapterName}");
        if (!string.IsNullOrWhiteSpace(report.AdapterVoltage)) sb.AppendLine($"Voltage: {report.AdapterVoltage}");
        if (!string.IsNullOrWhiteSpace(report.OdometerKm)) sb.AppendLine($"OBD odometer: {report.OdometerKm} km");
        sb.AppendLine();
        sb.AppendLine("DTC");
        sb.AppendLine(new string('-', 64));
        AppendCodes(sb, "Stored", report.StoredCodes);
        AppendCodes(sb, "Pending", report.PendingCodes);
        AppendCodes(sb, "Permanent", report.PermanentCodes);
        foreach (var module in report.Modules.Where(module => module.Responded))
        {
            sb.AppendLine($"{module.NameEn}: {(module.Codes.Count == 0 ? "No faults" : module.Codes.Count.ToString())}");
            foreach (var code in module.Codes)
                sb.AppendLine($"  {code.DisplayCode} - {(string.IsNullOrWhiteSpace(code.TitleEn) ? code.TitleKa : code.TitleEn)}");
        }
        sb.AppendLine();
        sb.AppendLine("READINESS");
        if (report.Readiness == null) sb.AppendLine("Unavailable");
        else sb.AppendLine($"MIL: {(report.Readiness.MilOn ? "ON" : "OFF")}; Stored DTC count: {report.Readiness.StoredDtcCount}");
        sb.AppendLine($"Mode 05: {report.Mode05Summary}");
        sb.AppendLine($"Mode 06: {report.Mode06Summary}");
        sb.AppendLine();
        sb.AppendLine($"Supported PIDs: {report.PidStats.Advertised}; Decoded: {report.PidStats.Decoded}; Failed: {report.PidStats.Failed}; Unsupported decoder: {report.PidStats.UnsupportedDecoder}");
        foreach (var s in report.Sensors) sb.AppendLine($"  {s.Pid:X2} {s.Name}: {s.Value} {s.Unit} [{s.StatusText}]");
        if (report.Identifiers.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("ECU IDENTIFIERS");
            foreach (var i in report.Identifiers) sb.AppendLine($"  {i.Name}: {i.Value}");
        }
        if (report.FreezeFrames.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("FREEZE FRAME");
            foreach (var ff in report.FreezeFrames) { sb.AppendLine($"Trigger: {ff.TriggerCode}"); foreach (var x in ff.Values) sb.AppendLine($"  {x.Key}: {x.Value}"); }
        }
        if (!string.IsNullOrWhiteSpace(report.Notes)) { sb.AppendLine(); sb.AppendLine("NOTES"); sb.AppendLine(report.Notes); }
        return sb.ToString();
    }

    public string BuildCsv(ObdScanReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Type,Code,Status,Title,Description,Value,Unit");
        foreach (var code in report.StoredCodes) AppendCsvDtc(sb, code, "Stored");
        foreach (var code in report.PendingCodes) AppendCsvDtc(sb, code, "Pending");
        foreach (var code in report.PermanentCodes) AppendCsvDtc(sb, code, "Permanent");
        foreach (var sensor in report.Sensors) sb.AppendLine($"Sensor,,,{Csv(sensor.Name)},{Csv(sensor.Category)},{Csv(sensor.Value)},{Csv(sensor.Unit)}");
        return sb.ToString();
    }

    public string BuildJson(ObdScanReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

    public string BuildHtml(ObdScanReport report)
    {
        static string H(string? x) => System.Net.WebUtility.HtmlEncode(x ?? string.Empty);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Georgian Car Scanner Report</title><style>body{font-family:Arial,sans-serif;padding:24px;color:#111827}h1{margin-bottom:4px}.meta{color:#475569}.card{border:1px solid #d1d5db;border-radius:10px;padding:14px;margin:12px 0}table{border-collapse:collapse;width:100%}th,td{border:1px solid #d1d5db;padding:7px;text-align:left;font-size:13px}th{background:#f1f5f9}.danger{color:#b91c1c}.ok{color:#15803d}</style></head><body>");
        sb.Append($"<h1>Georgian Car Scanner</h1><div class='meta'>{H(report.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"))} — {H(report.FinishedAt.ToString("yyyy-MM-dd HH:mm:ss"))}</div>");
        sb.Append($"<div class='card'><b>Vehicle:</b> {H(report.Vehicle.Manufacturer)} {H(report.Vehicle.Model)} {H(report.Vehicle.Year)}<br><b>VIN:</b> {H(report.Vehicle.VIN)}<br><b>Protocol:</b> {H(report.Protocol)}<br><b>Adapter:</b> {H(report.AdapterName)}<br><b>Voltage:</b> {H(report.AdapterVoltage)}</div>");
        sb.Append("<div class='card'><h2>DTC</h2><table><tr><th>Status</th><th>Code</th><th>Description</th></tr>");
        foreach(var tuple in report.StoredCodes.Select(x=>("Stored",x)).Concat(report.PendingCodes.Select(x=>("Pending",x))).Concat(report.PermanentCodes.Select(x=>("Permanent",x)))) sb.Append($"<tr><td>{H(tuple.Item1)}</td><td class='danger'><b>{H(tuple.Item2.Code)}</b></td><td>{H(tuple.Item2.Description)}</td></tr>");
        if(!report.HasFaults) sb.Append("<tr><td colspan='3' class='ok'>No DTCs reported by responding ECU.</td></tr>");
        sb.Append("</table></div>");
        sb.Append($"<div class='card'><h2>Readiness</h2><p>{H(report.Readiness?.StatusText ?? "Unavailable")}</p><p>Mode 05: {H(report.Mode05Summary)}</p><p>Mode 06: {H(report.Mode06Summary)}</p></div>");
        if(report.Identifiers.Count>0){sb.Append("<div class='card'><h2>ECU Identifiers</h2><table><tr><th>Name</th><th>Value</th></tr>");foreach(var i in report.Identifiers) sb.Append($"<tr><td>{H(i.Name)}</td><td>{H(i.Value)}</td></tr>");sb.Append("</table></div>");}
        if(report.Sensors.Count>0){sb.Append("<div class='card'><h2>Live Data snapshot</h2><table><tr><th>PID</th><th>Name</th><th>Value</th><th>Unit</th><th>Status</th></tr>");foreach(var x in report.Sensors) sb.Append($"<tr><td>{x.Pid:X2}</td><td>{H(x.Name)}</td><td>{H(x.Value)}</td><td>{H(x.Unit)}</td><td>{H(x.StatusText)}</td></tr>");sb.Append("</table></div>");}
        if(!string.IsNullOrWhiteSpace(report.Notes)) sb.Append($"<div class='card'><h2>Notes</h2><p>{H(report.Notes).Replace("\n","<br>")}</p></div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public async Task<string> ExportAsync(ObdScanReport report, string format, CancellationToken token = default)
    {
        var ext = format.ToLowerInvariant() switch { "json" => "json", "csv" => "csv", "html" => "html", "pdf" => "pdf", _ => "txt" };
        if (ext == "pdf") return await _pdf.ExportAsync(report, token);
        var content = ext switch { "json" => BuildJson(report), "csv" => BuildCsv(report), "html" => BuildHtml(report), _ => BuildText(report) };
        var path = Path.Combine(FileSystem.CacheDirectory, $"gcs-report-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}");
        await File.WriteAllTextAsync(path, content, Encoding.UTF8, token);
        return path;
    }

    private static void AppendCodes(StringBuilder sb, string title, IReadOnlyList<DiagnosticCode> codes)
    {
        sb.AppendLine($"{title}: {(codes.Count == 0 ? "None" : codes.Count.ToString())}");
        foreach (var c in codes) sb.AppendLine($"  {c.Code} - {c.Description}");
    }

    private static void AppendCsvDtc(StringBuilder sb, DiagnosticCode code, string status) => sb.AppendLine($"DTC,{Csv(code.Code)},{Csv(status)},,,,");
    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    public Task<string> ExportPdfAsync(ObdScanReport report, CancellationToken token=default) => _pdf.ExportAsync(report, token);

}
