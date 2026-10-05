#if IOS
using CarScanner.Models;
using CoreGraphics;
using Foundation;
using UIKit;

namespace CarScanner.Platforms.iOS.Reporting;

/// <summary>
/// Native multi-page PDF renderer. UIKit/CoreText provides proper Georgian
/// shaping and embeds the glyphs used by the report.
/// </summary>
internal static class iOSPdfReportWriter
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double Margin = 40;
    private const double FooterSpace = 42;

    public static byte[] Build(
        ObdScanReport report,
        List<CarScanner.Services.PdfReportService.ReportSection> sections,
        string? subtitle,
        bool isEnglish)
    {
        var bounds = new CGRect(0, 0, PageWidth, PageHeight);
        using var renderer = new UIGraphicsPdfRenderer(
            bounds,
            UIGraphicsPdfRendererFormat.DefaultFormat);

        using var data = renderer.CreatePdf(context =>
        {
            var pageNumber = 0;
            double y = 0;
            var pageOpen = false;

            static UIColor Color(string hex)
            {
                var value = hex.TrimStart('#');
                if (value.Length != 6)
                    return UIColor.Black;
                return UIColor.FromRGB(
                    Convert.ToByte(value[..2], 16),
                    Convert.ToByte(value.Substring(2, 2), 16),
                    Convert.ToByte(value.Substring(4, 2), 16));
            }

            static UIFont Font(double size, bool bold) =>
                (bold ? UIFont.BoldSystemFontOfSize((nfloat)size) : UIFont.SystemFontOfSize((nfloat)size))
                ?? UIFont.PreferredBody;

            static UIStringAttributes Attrs(UIFont font, UIColor color) =>
                new() { Font = font, ForegroundColor = color };

            static void DrawAt(string text, CGPoint point, UIFont font, UIColor color) =>
                new NSString(text).DrawString(point, Attrs(font, color));

            void Footer()
            {
                if (!pageOpen)
                    return;
                DrawAt($"GCar360 · {pageNumber}", new CGPoint(Margin, PageHeight - 27), Font(9, false), UIColor.FromRGB(100, 116, 139));
            }

            void NewPage()
            {
                Footer();
                context.BeginPage();
                pageOpen = true;
                pageNumber++;
                y = Margin;

                if (pageNumber != 1)
                    return;

                var cg = context.CGContext;
                cg.SetFillColor(Color("#185FA5").CGColor);
                cg.FillRect(new CGRect(0, 0, PageWidth, 96));

                DrawAt("GCar360", new CGPoint(Margin, 22), Font(22, true), UIColor.White);
                DrawAt(
                    subtitle ?? (isEnglish ? "Diagnostic report" : "დიაგნოსტიკური ანგარიში"),
                    new CGPoint(Margin, 52),
                    Font(13, false),
                    UIColor.White);

                var date = report.FinishedAt.ToString("dd.MM.yyyy HH:mm");
                var dateFont = Font(10, false);
                var dateSize = new NSString(date).GetSizeUsingAttributes(Attrs(dateFont, UIColor.White));
                DrawAt(date, new CGPoint(PageWidth - Margin - dateSize.Width, 54), dateFont, UIColor.White);

                y = 120;
                var faults = report.StoredCodes.Count +
                             report.PendingCodes.Count +
                             report.PermanentCodes.Count +
                             report.Modules.Where(module => module.ModuleId != "ECM")
                                 .Sum(module => module.Codes.Count);
                var status = !report.EcuResponded
                    ? (isEnglish ? "The car did not answer" : "მანქანამ არ უპასუხა", "#B45309")
                    : faults > 0
                        ? (isEnglish ? $"{faults} fault code(s) found" : $"ნაპოვნია {faults} შეცდომა", "#B91C1C")
                        : (isEnglish ? "No fault codes" : "შეცდომები არ არის", "#15803D");

                var pill = UIBezierPath.FromRoundedRect(
                    new CGRect(Margin, y, PageWidth - Margin * 2, 34),
                    10);
                Color(status.Item2).SetFill();
                pill.Fill();
                DrawAt(status.Item1, new CGPoint(Margin + 14, y + 8), Font(14, true), UIColor.White);
                y += 52;
            }

            void Ensure(double height)
            {
                if (!pageOpen || y + height > PageHeight - FooterSpace)
                    NewPage();
            }

            void WriteWrapped(
                string text,
                double size,
                bool bold,
                string color,
                double indent = 0)
            {
                var font = Font(size, bold);
                var attrs = Attrs(font, Color(color));
                var lineWidth = PageWidth - Margin * 2 - indent;

                foreach (var paragraph in text.Replace("\r", string.Empty).Split('\n'))
                {
                    if (paragraph.Length == 0)
                    {
                        Ensure(size * 1.45);
                        y += size * 1.45;
                        continue;
                    }

                    var measured = new NSString(paragraph).GetBoundingRect(
                        new CGSize(lineWidth, 10_000),
                        NSStringDrawingOptions.UsesLineFragmentOrigin,
                        attrs,
                        null).Size;
                    var height = Math.Max(size * 1.45, Math.Ceiling(measured.Height / (size * 1.45)) * size * 1.45);
                    Ensure(height);
                    new NSString(paragraph).DrawString(
                        new CGRect(Margin + indent, y, lineWidth, height + size),
                        attrs);
                    y += height;
                }
            }

            NewPage();
            foreach (var section in sections)
            {
                Ensure(60);
                y += 6;
                WriteWrapped(section.Title, 15, true, "#0F172A");
                context.CGContext.SetFillColor(Color("#CBD5E1").CGColor);
                context.CGContext.FillRect(new CGRect(Margin, y, PageWidth - Margin * 2, 1));
                y += 8;

                foreach (var line in section.Lines)
                    WriteWrapped(line.Text, 11, line.Bold, line.Color ?? "#1F2937", 4);
                y += 10;
            }

            y += 8;
            WriteWrapped(
                isEnglish
                    ? "Report created with GCar360 from real data read from the car."
                    : "ანგარიში შექმნილია GCar360-ით მანქანიდან წაკითხული რეალური მონაცემებით.",
                9,
                false,
                "#64748B");
            Footer();
        });

        return data.ToArray();
    }
}
#endif
