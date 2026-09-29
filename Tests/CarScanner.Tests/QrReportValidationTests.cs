using CarScanner.Api.Services;

namespace CarScanner.Tests;

public sealed class QrReportValidationTests
{
    [Fact]
    public void ValidReport_IsAccepted()
    {
        Assert.Null(QrReportStore.Validate(ValidRequest()));
    }

    [Fact]
    public void FutureInspectionDate_IsRejected()
    {
        Assert.Equal("invalid inspection date", QrReportStore.Validate(ValidRequest() with
        {
            CheckedAt = DateTime.UtcNow.AddHours(1)
        }));
    }

    [Fact]
    public void PublicHtml_EncodesUntrustedReportText()
    {
        var report = new QrPublicReport("<script>alert(1)</script>", "***123456", DateTime.UtcNow,
            DateTime.UtcNow.AddDays(30), "კარგია", "summary", "none", "ready");
        var html = QrReportStore.RenderHtml(report);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    private static QrReportCreateRequest ValidRequest() => new(
        "Toyota Prius 2014", "JTDBR32E720123456", DateTime.UtcNow,
        "შეცდომები არ არის", "ECU პასუხი მიღებულია", "კოდები არ არის", "MIL გამორთულია");
}
