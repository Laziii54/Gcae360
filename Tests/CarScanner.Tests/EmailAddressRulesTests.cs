using CarScanner.Services;

namespace CarScanner.Tests;

/// <summary>Malformed addresses are refused before an account exists, by the app and by the server (same rule file).</summary>
public sealed class EmailAddressRulesTests
{
    [Theory]
    [InlineData("driver@example.com")]
    [InlineData("a@b.co")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    [InlineData("under_score%pct@example.org")]
    [InlineData("Driver.Name@Example.COM")]
    [InlineData("  driver@example.com  ")]
    [InlineData("gcar360support@gmail.com")]
    public void WellFormedAddresses_AreAccepted(string email) =>
        Assert.True(EmailAddressRules.IsValidForRegistration(email), email);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plainaddress")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@@example.com")]
    [InlineData("user@example")]                    // no dot in the domain
    [InlineData("abc@def")]
    [InlineData("user@.example.com")]
    [InlineData("user@example..com")]
    [InlineData("user..name@example.com")]
    [InlineData(".user@example.com")]
    [InlineData("user.@example.com")]
    [InlineData("user name@example.com")]
    [InlineData("User <a@b.com>")]
    [InlineData("a@b.c")]                           // one-letter top-level domain
    [InlineData("user@example.c0m")]
    [InlineData("user@-example.com")]
    [InlineData("user@example-.com")]
    [InlineData("user@exam_ple.com")]
    [InlineData("\"quoted\"@example.com")]
    [InlineData("user@[127.0.0.1]")]
    [InlineData("user(comment)@example.com")]
    [InlineData("a@b.com\nBcc: victim@example.com")]   // header injection attempt
    [InlineData("user@example.com,other@example.com")]
    [InlineData("ა@example.com")]                   // non-ASCII local part
    public void MalformedAddresses_AreRejected(string? email) =>
        Assert.False(EmailAddressRules.IsValidForRegistration(email), email ?? "<null>");

    [Fact]
    public void LengthLimits_Apply()
    {
        Assert.True(EmailAddressRules.IsValidForRegistration(new string('a', 64) + "@example.com"));
        Assert.False(EmailAddressRules.IsValidForRegistration(new string('a', 65) + "@example.com"));
        Assert.False(EmailAddressRules.IsValidForRegistration("a@" + string.Join(".", Enumerable.Repeat(new string('b', 60), 5)) + ".com"));
    }

    [Fact]
    public void TheServerAndTheApp_BothApplyTheSameRule_BeforeAnAccountIsCreated()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CarScanner.csproj")))
            dir = dir.Parent;
        var root = dir!.FullName;

        var program = File.ReadAllText(Path.Combine(root, "Backend/CarScanner.Api/Program.cs"));
        var register = program[program.IndexOf("\"/api/accounts/register\"", StringComparison.Ordinal)..];
        var check = register.IndexOf("EmailAddressRules.IsValidForRegistration(request.Email)", StringComparison.Ordinal);
        var create = register.IndexOf("accounts.RegisterAsync(", StringComparison.Ordinal);
        Assert.True(check > 0 && create > check, "the server must check the address before creating the account");

        Assert.Contains("EmailAddressRules.cs", File.ReadAllText(Path.Combine(root, "Backend/CarScanner.Api/CarScanner.Api.csproj")));   // same file, not a copy

        var page = File.ReadAllText(Path.Combine(root, "Views/AccountPage.xaml.cs"));
        var appCheck = page.IndexOf("register && !EmailAddressRules.IsValidForRegistration(email)", StringComparison.Ordinal);
        var send = page.IndexOf("_account.RegisterAsync(email, password)", StringComparison.Ordinal);
        Assert.True(appCheck > 0 && send > appCheck, "the app must check the address before sending the registration");
    }
}
