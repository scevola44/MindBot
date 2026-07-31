using MindBot.Core.Options;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

public class TelegramOptionsValidatorTests
{
    private readonly TelegramOptionsValidator _validator = new();

    [Fact]
    public void Validate_MissingBotToken_Fails()
    {
        var options = new TelegramOptions { BotToken = "", AllowedUserIds = "123" };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("TELEGRAM__BOTTOKEN"));
    }

    [Fact]
    public void Validate_MissingAllowedUserIds_Fails()
    {
        var options = new TelegramOptions { BotToken = "abc", AllowedUserIds = "" };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("TELEGRAM__ALLOWEDUSERIDS"));
    }

    [Fact]
    public void Validate_NonNumericAllowedUserIds_Fails()
    {
        var options = new TelegramOptions { BotToken = "abc", AllowedUserIds = "123,not-a-number" };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var options = new TelegramOptions { BotToken = "abc", AllowedUserIds = "123, 456" };

        var result = _validator.Validate(null, options);

        Assert.False(result.Failed);
    }
}

public class GitOptionsValidatorTests
{
    private const string VaultRoot = "/data/vault";

    private readonly GitOptionsValidator _validator = new(
        Microsoft.Extensions.Options.Options.Create(new VaultOptions { Root = VaultRoot }));

    [Fact]
    public void Validate_MissingRemoteUrl_Fails()
    {
        var options = new GitOptions { RemoteUrl = "", Branch = "bot-inbox", SshKeyPath = "/tmp/does-not-exist" };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("GIT__REMOTEURL"));
    }

    [Fact]
    public void Validate_MissingBranch_Fails()
    {
        var options = new GitOptions { RemoteUrl = "git@example.com:x/y.git", Branch = "", SshKeyPath = "/tmp/does-not-exist" };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("GIT__BRANCH"));
    }

    [Fact]
    public void Validate_SshKeyDoesNotExist_Fails()
    {
        var options = new GitOptions
        {
            RemoteUrl = "git@example.com:x/y.git",
            Branch = "bot-inbox",
            SshKeyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("GIT__SSHKEYPATH"));
    }

    [Fact]
    public void Validate_WorldReadableSshKey_Fails()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        File.WriteAllText(path, "fake-key");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        try
        {
            var options = new GitOptions { RemoteUrl = "git@example.com:x/y.git", Branch = "bot-inbox", SshKeyPath = path };

            var result = _validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures, f => f.Contains("GIT__SSHKEYPATH"));
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_ValidSshKeyPermissions_Succeeds()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        File.WriteAllText(path, "fake-key");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        try
        {
            var options = new GitOptions { RemoteUrl = "git@example.com:x/y.git", Branch = "bot-inbox", SshKeyPath = path };

            var result = _validator.Validate(null, options);

            Assert.False(result.Failed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_RecoveryPathInsideVault_Fails()
    {
        // A bundle written inside the vault would be swept onto the branch by 'git add -A' —
        // committing the very commits it exists to rescue.
        var options = new GitOptions
        {
            RemoteUrl = "git@example.com:x/y.git",
            Branch = "bot-inbox",
            SshKeyPath = "/tmp/does-not-exist",
            RecoveryPath = VaultRoot + "/recovery",
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("GIT__RECOVERYPATH"));
    }

    [Fact]
    public void Validate_RecoveryPathEqualToVaultRoot_Fails()
    {
        var options = new GitOptions
        {
            RemoteUrl = "git@example.com:x/y.git",
            Branch = "bot-inbox",
            SshKeyPath = "/tmp/does-not-exist",
            RecoveryPath = VaultRoot,
        };

        var result = _validator.Validate(null, options);

        Assert.Contains(result.Failures, f => f.Contains("GIT__RECOVERYPATH"));
    }

    [Fact]
    public void Validate_RelativeRecoveryPath_Fails()
    {
        var options = new GitOptions
        {
            RemoteUrl = "git@example.com:x/y.git",
            Branch = "bot-inbox",
            SshKeyPath = "/tmp/does-not-exist",
            RecoveryPath = "recovery",
        };

        var result = _validator.Validate(null, options);

        Assert.Contains(result.Failures, f => f.Contains("GIT__RECOVERYPATH"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositivePushRetryCount_Fails(int retryCount)
    {
        var options = new GitOptions
        {
            RemoteUrl = "git@example.com:x/y.git",
            Branch = "bot-inbox",
            SshKeyPath = "/tmp/does-not-exist",
            PushRetryCount = retryCount,
        };

        var result = _validator.Validate(null, options);

        Assert.Contains(result.Failures, f => f.Contains("GIT__PUSHRETRYCOUNT"));
    }
}

public class StateOptionsValidatorTests
{
    private const string VaultRoot = "/data/vault";

    private readonly StateOptionsValidator _validator = new(
        Microsoft.Extensions.Options.Options.Create(new VaultOptions { Root = VaultRoot }));

    [Fact]
    public void Validate_DefaultOptions_Succeed()
    {
        var result = _validator.Validate(null, new StateOptions());

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_DatabaseInsideVault_Fails()
    {
        // 'git add -A' would otherwise commit the bot's own state database into the vault.
        var options = new StateOptions { DatabasePath = VaultRoot + "/.mindbot/state.db" };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("STATE__DATABASEPATH"));
    }

    [Fact]
    public void Validate_RelativeDatabasePath_Fails()
    {
        var result = _validator.Validate(null, new StateOptions { DatabasePath = "state/mindbot.db" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("STATE__DATABASEPATH"));
    }

    [Fact]
    public void Validate_MissingDatabasePath_Fails()
    {
        var result = _validator.Validate(null, new StateOptions { DatabasePath = "" });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_NonPositiveConversationExpiry_Fails()
    {
        var result = _validator.Validate(null, new StateOptions { ConversationExpiryMinutes = 0 });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("STATE__CONVERSATIONEXPIRYMINUTES"));
    }
}

public class VaultOptionsValidatorTests
{
    private readonly VaultOptionsValidator _validator = new();

    [Fact]
    public void Validate_MissingRoot_Fails()
    {
        var result = _validator.Validate(null, new VaultOptions { Root = "" });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_RelativeRoot_Fails()
    {
        var result = _validator.Validate(null, new VaultOptions { Root = "relative/path" });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_AbsoluteRoot_Succeeds()
    {
        var result = _validator.Validate(null, new VaultOptions { Root = "/data/vault" });

        Assert.False(result.Failed);
    }
}
