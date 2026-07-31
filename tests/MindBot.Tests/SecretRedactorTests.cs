using MindBot.Core.Logging;

namespace MindBot.Tests;

public class SecretRedactorTests
{
    private const string Token = "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    [Fact]
    public void Redact_BareToken_IsRemoved()
    {
        var redactor = new SecretRedactor(Token);

        var result = redactor.Redact($"failed to authenticate with {Token}");

        Assert.DoesNotContain(Token, result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void Redact_FileDownloadUrl_StripsTheEmbeddedToken()
    {
        var redactor = new SecretRedactor(Token);

        var result = redactor.Redact($"GET https://api.telegram.org/file/bot{Token}/photos/file_1.jpg failed");

        Assert.DoesNotContain(Token, result);
        Assert.Contains("https://api.telegram.org/file/bot***/", result);
    }

    [Fact]
    public void Redact_ApiUrl_StripsTheEmbeddedToken()
    {
        var redactor = new SecretRedactor(Token);

        var result = redactor.Redact($"POST https://api.telegram.org/bot{Token}/sendMessage");

        Assert.DoesNotContain(Token, result);
    }

    [Fact]
    public void Redact_UrlWithADifferentToken_IsStillRedacted()
    {
        // The URL pattern must not depend on knowing the configured token: a token from any
        // source that reaches a log line has to be caught.
        var redactor = new SecretRedactor("some-other-configured-token");

        var result = redactor.Redact("https://api.telegram.org/file/bot987654321:ZZZsomeOtherSecret/doc.pdf");

        Assert.DoesNotContain("987654321:ZZZsomeOtherSecret", result);
        Assert.Contains("bot***", result);
    }

    [Fact]
    public void Redact_MultipleOccurrences_AreAllRemoved()
    {
        var redactor = new SecretRedactor(Token);

        var result = redactor.Redact($"{Token} and again {Token}");

        Assert.DoesNotContain(Token, result);
    }

    [Fact]
    public void Redact_NullOrEmpty_IsPassedThrough()
    {
        var redactor = new SecretRedactor(Token);

        Assert.Null(redactor.Redact(null));
        Assert.Equal(string.Empty, redactor.Redact(string.Empty));
    }

    [Fact]
    public void Redact_UnrelatedText_IsUnchanged()
    {
        var redactor = new SecretRedactor(Token);

        const string message = "Add note 202607311200.md";

        Assert.Equal(message, redactor.Redact(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    public void Redact_BlankOrTrivialToken_DoesNotBlankOutEveryLine(string? token)
    {
        // A missing or absurdly short token must not turn ordinary log lines into asterisks.
        var redactor = new SecretRedactor(token);

        const string message = "Telegram long-polling started as @mybot.";

        Assert.Equal(message, redactor.Redact(message));
    }
}
