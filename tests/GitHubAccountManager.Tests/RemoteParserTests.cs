using GitHubAccountManager.Core;
using Xunit;

namespace GitHubAccountManager.Tests;

public sealed class RemoteParserTests
{
    private static AccountProfile Account() => new()
    {
        Id = "personal", DisplayName = "Personal", GitUserName = "Test User", GitEmail = "test@example.com",
        GitHubUser = "test-user", HostName = "github.com", SshAlias = "github-personal", PrivateKeyPath = "~/.ssh/test-key"
    };

    [Theory]
    [InlineData("git@github.com:owner/repository.git")]
    [InlineData("https://github.com/owner/repository.git")]
    [InlineData("git@github-personal:owner/repository.git")]
    [InlineData("ssh://git@github.com/owner/repository.git")]
    [InlineData("owner/repository")]
    public void ParsesSupportedFormats(string input)
    {
        var account = Account();
        Assert.True(RemoteParser.TryParse(input, [account], "github.com", out var parsed));
        Assert.Equal("owner/repository", parsed!.RepositoryPath);
        Assert.Equal("git@github-personal:owner/repository.git", RemoteParser.BuildSshUrl(account, parsed));
        Assert.Equal("https://test-user@github.com/owner/repository.git", RemoteParser.BuildHttpsUrl(account, parsed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("owner")]
    [InlineData("owner/repo/extra")]
    [InlineData("owner name/repo")]
    [InlineData("owner/repo name")]
    public void RejectsInvalidFormats(string input) =>
        Assert.False(RemoteParser.TryParse(input, [Account()], "github.com", out _));

    [Fact]
    public void DetectsSensitivePaths()
    {
        var found = SensitiveFileScanner.FindSuspiciousPaths(
            [new("??", ".env"), new(" M", "src/app.cs"), new("??", "certificates/prod.key")]);
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void RejectsDuplicateAliases()
    {
        var first = Account(); var second = Account(); second.Id = "work";
        Assert.Throws<InvalidDataException>(() => SettingsService.Validate(new UserSettings { Accounts = [first, second] }));
    }
}
