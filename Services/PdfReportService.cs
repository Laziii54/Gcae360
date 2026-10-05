using System.Text;
using CarScanner.Models;
using CarScanner.Services.Localization;

namespace CarScanner.Services;

/// <summary>
/// Builds the diagnostic report PDF. On Android the report is drawn with the
/// system PDF API, so Georgian text, several pages and colours work. Other
/// platforms keep the simple text PDF.
/// </summary>
public sealed class PdfReportService
{
    private static bool IsEnglish =>
        string.Equals(
            Preferences.Default.Get(LocalizationService.LanguageKey, "English"),
            "English",
            StringComparison.OrdinalIgnoreCase);

    private static string L(string ka, string en) => IsEnglish ? en : ka;

    public async Task<string> ExportAsync(ObdScanReport report, CancellationToken token = default)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"GCar360-report-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        var sections = BuildSections(report);
        return await WriteAsync(report, sections, path, null, token);
    }

    /// <summary>
    /// Pre-purchase check report: the summary sections first (score, mileage,
    /// condition, history), then the full diagnostic sections.
    /// </summary>
    public async Task<string> ExportPrePurchaseAsync(
        ObdScanReport report,
        IEnumerable<(string Title, IEnumerable<(string Text, bool Bold, string? Color)> Lines)> summary,
        CancellationToken token = default)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"GCar360-prepurchase-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        var sections = summary
            .Select(section => new ReportSection(
                section.Title,
                section.Lines.Select(line => new ReportLine(line.Text, line.Bold, line.Color)).ToList()))
            .ToList();
        sections.AddRange(BuildSections(report));
        return await WriteAsync(report, sections, path, L("ყიდვისწინა შემოწმება", "Pre-purchase check"), token);
    }

    private static async Task<string> WriteAsync(
        ObdScanReport report,
        List<ReportSection> sections,
        string path,
        string? subtitle,
        CancellationToken token)
    {
#if ANDROID
        var bytes = await Task.Run(() => AndroidPdfWriter.Build(report, sections, subtitle), token);
#elif IOS
        var bytes = await Task.Run(() =>
            CarScanner.Platforms.iOS.Reporting.iOSPdfReportWriter.Build(
                report,
                sections,
                subtitle,
                IsEnglish), token);
#else
        var lines = new List<string>();
        foreach (var section in sections)
        {
            lines.Add(string.Empty);
            lines.Add(section.Title);
            lines.AddRange(section.Lines.Select(line => line.Text));
        }
        var bytes = MinimalPdfBuilder.Build(lines);
#endif
        await File.WriteAllBytesAsync(path, bytes, token);
        return path;
    }

    internal sealed record ReportLine(string Text, bool Bold = false, string? Color = null);

    internal sealed record ReportSection(string Title, List<ReportLine> Lines);

    private static List<ReportSection> BuildSections(ObdScanReport report)
    {
        static bool Unknown(string? value) =>
            string.IsNullOrWhiteSpace(value) || string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);
        string Known(string? value) => Unknown(value) ? L("უცნობია", "unknown") : value!;

        var sections = new List<ReportSection>();

        var vehicle = new List<ReportLine>
        {
            new($"{Known(report.Vehicle.Manufacturer)} {Known(report.Vehicle.Model)} · {Known(report.Vehicle.Year)}", true),
            new($"VIN: {Known(report.Vehicle.VIN)}")
        };
        if (!Unknown(report.Vehicle.Engine) || !Unknown(report.Vehicle.FuelType))
            vehicle.Add(new($"{L("ძრავი", "Engine")}: {Known(report.Vehicle.Engine)} · {L("საწვავი", "Fuel")}: {Known(report.Vehicle.FuelType)}"));
        if (!string.IsNullOrWhiteSpace(report.OdometerKm))
            vehicle.Add(new($"{L("გარბენი", "Mileage")}: {report.OdometerKm} km"));
        vehicle.Add(new($"{L("სკანი", "Scan")}: {report.StartedAt:dd.MM.yyyy HH:mm} – {report.FinishedAt:HH:mm}"));
        if (!Unknown(report.AdapterVoltage))
            vehicle.Add(new($"{L("აკუმულატორი", "Battery")}: {report.AdapterVoltage}"));
        if (!Unknown(report.Protocol))
            vehicle.Add(new($"{L("პროტოკოლი", "Protocol")}: {report.Protocol}"));
        sections.Add(new(L("ავტომობილი", "Vehicle"), vehicle));

        // Electronic service book: what was changed and when (kept by the app).
        var history = CarScanner.Services.Maintenance.MaintenanceTrackerService.HistoryFor(report.Vehicle);
        if (history.Count > 0)
        {
            var english = IsEnglish;
            var lines = new List<ReportLine>();
            foreach (var item in history)
            {
                var color = item.Status.State switch
                {
                    CarScanner.Services.Maintenance.PartState.Overdue => "#B91C1C",
                    CarScanner.Services.Maintenance.PartState.Soon => "#B45309",
                    _ => "#15803D"
                };
                var state = item.Status.State == CarScanner.Services.Maintenance.PartState.Unknown
                    ? string.Empty
                    : " · " + (item.Status.State == CarScanner.Services.Maintenance.PartState.Overdue
                        ? L("ვადა გავიდა", "overdue")
                        : L("დარჩა ", "left ") + CarScanner.ViewModels.MaintenanceText.ValueText(item.Status, english));
                lines.Add(new($"{(english ? item.Part.NameEn : item.Part.NameKa)}{state}", true, color));
                lines.Add(new($"  {L("ინტერვალი", "Interval")}: {CarScanner.ViewModels.MaintenanceText.IntervalText(item.Interval, english)}"));
                foreach (var record in item.Records.Take(6))
                {
                    var date = record.DateUtc?.ToLocalTime().ToString("dd.MM.yyyy") ?? "—";
                    var km = record.Km is { } value ? CarScanner.ViewModels.MaintenanceText.Km(value) : "—";
                    lines.Add(new($"  • {date} · {km}"));
                }
            }

            lines.Add(new(L(
                "ჩანაწერები მფლობელმა შეიყვანა აპში; ისინი სერვისის ცენტრის მიერ დადასტურებული არ არის.",
                "Records were entered by the owner in the app; they are not confirmed by a service centre."), false, "#64748B"));
            sections.Add(new(L("სერვისის ისტორია", "Service history"), lines));
        }

        var codes = report.StoredCodes.Select(code => (code, L("შენახული", "stored")))
            .Concat(report.PendingCodes.Select(code => (code, L("მოლოდინში", "pending"))))
            .Concat(report.PermanentCodes.Select(code => (code, L("მუდმივი", "permanent"))))
            .ToList();
        var dtc = new List<ReportLine>();
        if (codes.Count == 0)
        {
            dtc.Add(report.DtcServicesRead == 0
                ? new(L("შეცდომის კოდები ვერ წაიკითხა.", "Fault codes could not be read."), false, "#B45309")
                : new(L("✓ შეცდომის კოდი არ არის.", "✓ No fault codes."), true, "#15803D"));
        }
        else
        {
            foreach (var (code, status) in codes)
            {
                dtc.Add(new($"{code.Code}  ({status})", true, "#B91C1C"));
                if (!string.IsNullOrWhiteSpace(code.Description))
                    dtc.Add(new(code.Description));
            }
        }
        sections.Add(new(L("შეცდომის კოდები", "Fault codes"), dtc));

        var modules = report.Modules.Where(module => module.Responded).ToList();
        if (modules.Count > 0)
        {
            var lines = new List<ReportLine>();
            foreach (var module in modules)
            {
                var name = IsEnglish ? module.NameEn : module.NameKa;
                if (module.Codes.Count == 0)
                {
                    lines.Add(new($"✓ {name}", false, "#15803D"));
                    continue;
                }

                lines.Add(new($"{name}: {module.Codes.Count}", true, "#B91C1C"));
                foreach (var code in module.Codes)
                {
                    var title = IsEnglish
                        ? (string.IsNullOrWhiteSpace(code.TitleEn) ? code.TitleKa : code.TitleEn)
                        : (string.IsNullOrWhiteSpace(code.TitleKa) ? code.TitleEn : code.TitleKa);
                    lines.Add(new($"  {code.DisplayCode}{(string.IsNullOrWhiteSpace(title) ? string.Empty : " — " + title)}"));
                }
            }
            sections.Add(new(L("მოდულები (ABS, აირბაგი…)", "Modules (ABS, airbag…)"), lines));
        }

        var readiness = new List<ReportLine>();
        if (report.Readiness is null)
            readiness.Add(new(L("მონაცემი მიუწვდომელია.", "Data unavailable.")));
        else
        {
            readiness.Add(new($"Check Engine: {(report.Readiness.MilOn ? L("ჩართულია", "ON") : L("გამორთულია", "OFF"))}", true,
                report.Readiness.MilOn ? "#B91C1C" : "#15803D"));
            readiness.Add(new($"{L("ECU-ში შენახული კოდები", "Codes stored in the ECU")}: {report.Readiness.StoredDtcCount}"));
        }
        sections.Add(new(L("Check Engine / მზადყოფნა", "Check Engine / readiness"), readiness));

        if (report.FreezeFrames.Count > 0)
        {
            var frames = new List<ReportLine>();
            foreach (var frame in report.FreezeFrames)
            {
                frames.Add(new($"{L("კოდი", "Code")}: {Known(frame.TriggerCode)}", true));
                frames.AddRange(frame.Values.Select(value => new ReportLine($"{value.Key}: {value.Value}")));
            }
            sections.Add(new("Freeze Frame", frames));
        }

        if (report.Sensors.Count > 0)
        {
            var sensors = report.Sensors
                .Select(sensor => new ReportLine(
                    $"{sensor.Name}: {sensor.Value} {sensor.Unit}".TrimEnd(),
                    false,
                    sensor.IsProblem ? "#B91C1C" : null))
                .ToList();
            sections.Add(new(L("სენსორები (სკანის მომენტში)", "Sensors (at scan time)"), sensors));
        }

        var identifiers = report.Identifiers.Where(item => item.Available).ToList();
        if (identifiers.Count > 0)
            sections.Add(new(L("ECU მონაცემები", "ECU identifiers"),
                identifiers.Select(item => new ReportLine($"{item.Name}: {item.Value}")).ToList()));

        return sections;
    }

#if ANDROID
    private static class AndroidPdfWriter
    {
        private const int PageWidth = 595;
        private const int PageHeight = 842;
        private const int Margin = 40;
        private const int FooterSpace = 40;

        public static byte[] Build(ObdScanReport report, List<ReportSection> sections, string? subtitle = null)
        {
            using var document = new Android.Graphics.Pdf.PdfDocument();
            var regular = Android.Graphics.Typeface.Default;
            var bold = Android.Graphics.Typeface.Create(Android.Graphics.Typeface.Default, Android.Graphics.TypefaceStyle.Bold);

            using var textPaint = new Android.Graphics.Paint { AntiAlias = true, TextSize = 11 };
            using var fillPaint = new Android.Graphics.Paint { AntiAlias = true };
            textPaint.SetTypeface(regular);

            var pageNumber = 0;
            Android.Graphics.Pdf.PdfDocument.Page? page = null;
            Android.Graphics.Canvas? canvas = null;
            float y = 0;
            var contentWidth = PageWidth - Margin * 2;

            void FinishPage()
            {
                if (page is null || canvas is null) return;
                textPaint.SetTypeface(regular);
                textPaint.TextSize = 9;
                textPaint.Color = Android.Graphics.Color.ParseColor("#64748B");
                canvas.DrawText($"GCar360 · {pageNumber}", Margin, PageHeight - 20, textPaint);
                document.FinishPage(page);
                page = null;
                canvas = null;
            }

            void NewPage()
            {
                FinishPage();
                pageNumber++;
                using var info = new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(PageWidth, PageHeight, pageNumber).Create();
                var newPage = document.StartPage(info) ?? throw new InvalidOperationException("The PDF page could not be created.");
                page = newPage;
                var pageCanvas = newPage.Canvas ?? throw new InvalidOperationException("The PDF page has no canvas.");
                canvas = pageCanvas;
                y = Margin;

                if (pageNumber == 1)
                {
                    // Header band
                    fillPaint.Color = Android.Graphics.Color.ParseColor("#185FA5");
                    canvas.DrawRect(0, 0, PageWidth, 96, fillPaint);
                    textPaint.Color = Android.Graphics.Color.White;
                    textPaint.SetTypeface(bold);
                    textPaint.TextSize = 22;
                    canvas.DrawText("GCar360", Margin, 44, textPaint);
                    textPaint.SetTypeface(regular);
                    textPaint.TextSize = 13;
                    canvas.DrawText(subtitle ?? L("დიაგნოსტიკური ანგარიში", "Diagnostic report"), Margin, 68, textPaint);
                    textPaint.TextSize = 10;
                    var date = report.FinishedAt.ToString("dd.MM.yyyy HH:mm");
                    canvas.DrawText(date, PageWidth - Margin - textPaint.MeasureText(date), 68, textPaint);
                    y = 120;

                    // Overall status pill
                    var faults = report.StoredCodes.Count + report.PendingCodes.Count + report.PermanentCodes.Count +
                        report.Modules.Where(module => module.ModuleId != "ECM").Sum(module => module.Codes.Count);
                    var (statusText, statusColor) = !report.EcuResponded
                        ? (L("მანქანამ არ უპასუხა", "The car did not answer"), "#B45309")
                        : faults > 0
                            ? (L($"ნაპოვნია {faults} შეცდომა", $"{faults} fault code(s) found"), "#B91C1C")
                            : (L("შეცდომები არ არის", "No fault codes"), "#15803D");
                    fillPaint.Color = Android.Graphics.Color.ParseColor(statusColor);
                    canvas.DrawRoundRect(new Android.Graphics.RectF(Margin, y, PageWidth - Margin, y + 34), 10, 10, fillPaint);
                    textPaint.Color = Android.Graphics.Color.White;
                    textPaint.SetTypeface(bold);
                    textPaint.TextSize = 14;
                    canvas.DrawText(statusText, Margin + 14, y + 22, textPaint);
                    y += 52;
                }
            }

            void Ensure(float height)
            {
                if (canvas is null || y + height > PageHeight - FooterSpace)
                    NewPage();
            }

            void WriteWrapped(string text, float size, bool isBold, string color, float indent = 0)
            {
                textPaint.SetTypeface(isBold ? bold : regular);
                textPaint.TextSize = size;
                textPaint.Color = Android.Graphics.Color.ParseColor(color);
                var lineHeight = size * 1.45f;
                var maxWidth = contentWidth - indent;

                foreach (var paragraph in text.Replace("\r", string.Empty).Split('\n'))
                {
                    var rest = paragraph;
                    if (rest.Length == 0)
                    {
                        Ensure(lineHeight);
                        y += lineHeight;
                        continue;
                    }

                    while (rest.Length > 0)
                    {
                        var count = textPaint.BreakText(rest, true, maxWidth, null);
                        if (count <= 0) count = 1;
                        if (count < rest.Length)
                        {
                            var space = rest.LastIndexOf(' ', count - 1);
                            if (space > 0) count = space + 1;
                        }

                        Ensure(lineHeight);
                        canvas!.DrawText(rest[..count].TrimEnd(), Margin + indent, y + size, textPaint);
                        y += lineHeight;
                        rest = rest[count..];
                    }
                }
            }

            NewPage();

            foreach (var section in sections)
            {
                Ensure(60);
                y += 6;
                WriteWrapped(section.Title, 15, true, "#0F172A");
                fillPaint.Color = Android.Graphics.Color.ParseColor("#CBD5E1");
                canvas!.DrawRect(Margin, y, PageWidth - Margin, y + 1, fillPaint);
                y += 8;

                foreach (var line in section.Lines)
                    WriteWrapped(line.Text, 11, line.Bold, line.Color ?? "#1F2937", 4);

                y += 10;
            }

            y += 8;
            WriteWrapped(
                L("ანგარიში შექმნილია GCar360-ით მანქანიდან წაკითხული რეალური მონაცემებით.",
                  "Report created with GCar360 from real data read from the car."),
                9, false, "#64748B");

            FinishPage();

            using var stream = new MemoryStream();
            document.WriteTo(stream);
            document.Close();
            return stream.ToArray();
        }
    }
#elif !IOS
    private static class MinimalPdfBuilder
    {
        public static byte[] Build(IEnumerable<string> lines)
        {
            var safe = lines.Select(Escape).Take(80).ToArray();
            var content = new StringBuilder("BT /F1 10 Tf 40 760 Td 13 TL ");
            foreach (var l in safe) content.Append($"({l}) Tj T* ");
            content.Append("ET");
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream"
            };
            using var ms = new MemoryStream();
            void W(string x) { var b = Encoding.ASCII.GetBytes(x); ms.Write(b); }
            W("%PDF-1.4\n");
            var offs = new List<long> { 0 };
            for (var i = 0; i < objects.Count; i++) { offs.Add(ms.Position); W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
            var xref = ms.Position;
            W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            for (var i = 1; i < offs.Count; i++) W($"{offs[i]:D10} 00000 n \n");
            W($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
            return ms.ToArray();
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }
#endif
}
