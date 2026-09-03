using GitHubAccountManager.Core;
using Xunit;

namespace GitHubAccountManager.Tests;

public sealed class AccountSwitchTests
{
    [Fact]
    public async Task IdentitySwitchCreatesBackupAndCanRestore()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "GitHubAccountManagerTests");
        var path = Path.Combine(basePath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var runner = new ProcessRunner(); var git = new GitService(runner);
            var account = new AccountProfile
            {
                Id = "work", DisplayName = "Work", GitUserName = "Work User", GitEmail = "work@example.com",
                GitHubUser = "work-user", HostName = "github.com", SshAlias = "github-work", PrivateKeyPath = "unused"
            };
            Assert.True((await git.InitializeAsync(path)).Success);
            await git.RunAsync(path, ["config", "--local", "user.name", "Original User"]);
            await git.RunAsync(path, ["config", "--local", "user.email", "original@example.com"]);
            var service = new AccountSwitchService(git, new SshService(runner));
            var settings = new UserSettings { Accounts = [account], BackupRetention = 3 };
            var switched = await service.SwitchAsync(path, account, settings, identityOnly: true, dryRun: false);
            Assert.True(switched.Success, switched.Message);
            Assert.Equal("work@example.com", await git.GetConfigAsync(path, "user.email"));
            var restored = await service.RestoreLatestAsync(path);
            Assert.True(restored.Success, restored.Message);
            Assert.Equal("original@example.com", await git.GetConfigAsync(path, "user.email"));
        }
        finally
        {
            if (Directory.Exists(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(basePath), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(path, true);
            }
        }
    }
}
