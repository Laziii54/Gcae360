using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using CarScanner.Api.Services.Email;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarScanner.Tests;

/// <summary>
/// The real SMTP sender talking to a local in-process SMTP server. This is a code-level delivery test, not a
/// delivery to a real inbox: it shows exactly the message that would be handed to the mail provider.
/// </summary>
public sealed class SmtpDeliveryTests
{
    private sealed class SmtpSink : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public TaskCompletionSource<string> Message { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Commands { get; } = new();

        public SmtpSink()
        {
            _listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.Latin1);
                    using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                    await writer.WriteLineAsync("220 sink ESMTP");
                    var data = false;
                    var body = new StringBuilder();
                    string? line;
                    while ((line = await reader.ReadLineAsync()) is not null)
                    {
                        if (data)
                        {
                            if (line == ".")
                            {
                                data = false;
                                Message.TrySetResult(body.ToString());
                                await writer.WriteLineAsync("250 OK");
                            }
                            else
                            {
                                body.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n");
                            }

                            continue;
                        }

                        Commands.Add(line);
                        if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase)) { data = true; await writer.WriteLineAsync("354 go"); }
                        else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("221 bye"); break; }
                        else if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase)) await writer.WriteLineAsync("250 sink");
                        else await writer.WriteLineAsync("250 OK");
                    }
                }
                catch (Exception ex)
                {
                    Message.TrySetException(ex);
                }
            });
        }

        public void Dispose() => _listener.Stop();
    }

    private static string DecodeBase64Part(string raw, string contentType)
    {
        var m = Regex.Match(raw, "Content-Type: " + Regex.Escape(contentType) + @"[^\r\n]*\r\nContent-Transfer-Encoding: base64\r\n\r\n([\s\S]*?)\r\n--", RegexOptions.IgnoreCase);
        Assert.True(m.Success, contentType + " part not found");
        return Encoding.UTF8.GetString(Convert.FromBase64String(Regex.Replace(m.Groups[1].Value, "\\s+", "")));
    }

    [Fact]
    public async Task TheRealSender_DeliversAMultipartUtf8Message_ToAnSmtpServer()
    {
        using var sink = new SmtpSink();
        var options = new EmailOptions { Host = "127.0.0.1", Port = sink.Port, UseSsl = false, FromAddress = "no-reply@example.test", FromName = "OBD360" };
        var sender = new SmtpEmailSender(options);
        Assert.True(sender.IsConfigured);

        var rendered = VerificationEmailTemplate.Render(new VerificationEmailModel("482915", 15, "ka", "https://api.example.test/assets/logo.png",
            "https://api.example.test/verify-email?l=ka&t=TOKENTOKENTOKENTOKENTOKENTOKENTOKEN1", "support@example.test", 2026));
        await sender.SendAsync(new EmailMessage("driver@example.test", rendered.Subject, rendered.Html, rendered.PlainText), CancellationToken.None);

        var raw = await sink.Message.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(sink.Commands, c => c.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase) && c.Contains("no-reply@example.test"));
        Assert.Contains(sink.Commands, c => c.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase) && c.Contains("driver@example.test"));
        Assert.Contains("multipart/alternative", raw);

        var html = DecodeBase64Part(raw, "text/html");
        var text = DecodeBase64Part(raw, "text/plain");
        Assert.Equal(rendered.Html, html);                       // exactly what was rendered, Georgian intact
        Assert.Equal(rendered.PlainText, text);
        Assert.Contains("482915", html);
        Assert.Contains("482915", text);

        var subject = Regex.Match(raw, "Subject: ([\\s\\S]*?)\\r\\nContent-Type").Groups[1].Value.Replace("\r\n ", "");
        var decoded = string.Concat(Regex.Matches(subject, "=\\?utf-8\\?B\\?([^?]+)\\?=", RegexOptions.IgnoreCase)
            .Select(m => Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value))));
        Assert.Equal(rendered.Subject, decoded);
        Assert.Contains("Auto-Submitted: auto-generated", raw);
    }

    [Fact]
    public async Task AnUnreachableMailServer_BecomesSendFailed_NotACrash()
    {
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();                                              // nothing listens on this port any more

        var options = new EmailOptions { Host = "127.0.0.1", Port = port, UseSsl = false, FromAddress = "no-reply@example.test" };
        var file = Path.Combine(Path.GetTempPath(), "gcar-smtpdown-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new EmailVerificationStore($"Data Source={file}", TimeSpan.FromMinutes(15));
            var service = new EmailVerificationService(store, new SmtpEmailSender(options), options, NullLogger<EmailVerificationService>.Instance);
            var outcome = await service.SendAsync("66666666-6666-6666-6666-666666666666", "driver@example.test", "en", CancellationToken.None);
            Assert.Equal(VerificationSendStatus.SendFailed, outcome.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public async Task WithoutSettings_TheSenderRefusesToSend()
    {
        var sender = new SmtpEmailSender(new EmailOptions());
        Assert.False(sender.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(new EmailMessage("a@example.test", "s", "<p>h</p>", "t"), CancellationToken.None));
    }
}

/// <summary>
/// The app side: its language travels to the server, the prompt appears only after a registration the server
/// started an email for, and every new text exists in all supported languages.
/// </summary>
public sealed class AppEmailVerificationWiringTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root(), relative));

    [Fact]
    public void TheAppSendsItsLanguage_AndUsesTheRealEndpoints()
    {
        var service = Read("Services/AccountService.cs");
        Assert.Contains("\"X-GCS-Language\"", service);
        Assert.Contains("LanguageCatalog.Current.Code", service);
        Assert.Contains("/api/accounts/email/verify", service);
        Assert.Contains("/api/accounts/email/send-code", service);
        Assert.Contains("EmailVerificationState = isRegister", service);        // only a registration carries the server's answer
    }

    [Fact]
    public void ThePrompt_IsOnlyForANewRegistration_AndCanBeSkipped()
    {
        var page = Read("Views/AccountPage.xaml.cs");
        Assert.Matches(@"if \(register\)\s+await HandleRegistrationEmailAsync\(\);", page);
        Assert.Contains("case \"sent\":", page);
        Assert.Contains("case \"failed\":", page);
        Assert.Contains("if (code is null)", Read("Services/EmailVerificationFlow.cs"));   // "Later" leaves the account usable
    }

    [Fact]
    public void EveryNewText_HasAllSevenTranslations()
    {
        var source = Read("Services/Localization/LanguagePacks.Extra9.cs");
        Assert.Contains("ExtraRows9", Read("Services/Localization/LanguagePacks.cs"));

        var rows = Regex.Matches(source, "R\\(((?:\\s*\"(?:[^\"\\\\]|\\\\.)*\"\\s*,?)+)\\)");
        Assert.Equal(23, rows.Count);
        foreach (Match row in rows)
        {
            var literals = Regex.Matches(row.Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\"").Select(m => m.Groups[1].Value).ToArray();
            Assert.Equal(8, literals.Length);                                  // English + tr, de, pl, es, pt, fr, it
            Assert.All(literals, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        }
    }

    [Theory]
    [InlineData("Verify your email")]
    [InlineData("Enter a valid email address.")]
    [InlineData("Verify your email to continue")]
    [InlineData("This needs a verified email address. We can send a code to the address on your account.")]
    [InlineData("Send code")]
    [InlineData("Send again")]
    [InlineData("We couldn't send the verification email. Please try again in a moment.")]
    [InlineData("Email not verified yet.")]
    [InlineData("Email not verified")]
    [InlineData("Send new code")]
    [InlineData("Email verified.")]
    [InlineData("That code is not correct. Try again.")]
    [InlineData("The code has expired. Request a new one.")]
    [InlineData("Too many attempts. Request a new code.")]
    [InlineData("There is no active code. Request a new one.")]
    [InlineData("A new code was sent to your email.")]
    [InlineData("Please wait a moment and try again.")]
    [InlineData("Sending email is not available right now.")]
    [InlineData("This email address is already verified.")]
    public void TheEnglishTextsUsedByTheApp_AreInThePack(string english)
    {
        Assert.Contains("\"" + english + "\"", Read("Services/Localization/LanguagePacks.Extra9.cs"));
        Assert.Contains(english, Read("Services/AccountService.cs") + Read("Views/AccountPage.xaml.cs") + Read("Services/EmailVerificationFlow.cs"));   // and really used
    }
}
