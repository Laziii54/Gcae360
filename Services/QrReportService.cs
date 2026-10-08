using System.Net.Http.Json;
using CarScanner.Models;

namespace CarScanner.Services;

public sealed class QrReportSession
{
    public QrReportDraft? Draft { get; private set; }
    public void Set(QrReportDraft draft) => Draft = draft;
    public void Clear() => Draft = null;
}

/// <summary>The server refused the QR report (status code and its short reason).</summary>
public sealed class QrReportServerException(int statusCode, string reason)
    : Exception($"QR report server answered {statusCode}: {reason}")
{
    public int StatusCode { get; } = statusCode;
    public string Reason { get; } = reason;
}

public sealed class QrReportService
{
    private readonly AccountService _account;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public QrReportService(AccountService account) => _account = account;

    public async Task<QrReportLink> CreateAsync(QrReportDraft draft, CancellationToken token = default)
    {
        using var request = await _account.CreateAuthorizedRequestAsync(HttpMethod.Post, "/api/reports/qr");
        request.Content = JsonContent.Create(draft);
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = string.Empty;
            try { body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false); } catch { }
            var reason = System.Text.RegularExpressions.Regex.Match(body, "\"reason\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            throw new QrReportServerException((int)response.StatusCode, reason);
        }
        return await response.Content.ReadFromJsonAsync<QrReportLink>(cancellationToken: token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("QR ბმული ვერ შეიქმნა.");
    }
}
