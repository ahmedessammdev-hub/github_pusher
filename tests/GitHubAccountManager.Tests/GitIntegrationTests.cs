using GitHubAccountManager.Core;
using Xunit;

namespace GitHubAccountManager.Tests;

public sealed class GitIntegrationTests
{
    [Fact]
    public async Task InitStatusCommitHistoryAndRemoteWorkTogether()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "GitHubAccountManagerTests");
        var testPath = Path.Combine(basePath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testPath);
        try
        {
            var git = new GitService(new ProcessRunner());
            var account = new AccountProfile { GitUserName = "Test User", GitEmail = "test@example.com" };
            Assert.True((await git.InitializeAsync(testPath)).Success);
            Assert.True((await git.SetIdentityAsync(testPath, account)).Success);
            await File.WriteAllTextAsync(Path.Combine(testPath, "README.md"), "# Test repository\n");
            Assert.True((await git.StageAllAsync(testPath)).Success);
            var unbornStatus = await git.GetStatusAsync(testPath);
            Assert.NotEqual("No", unbornStatus.Branch);
            Assert.True((await git.UnstageAsync(testPath, ["README.md"])).Success);
            Assert.True((await git.StageAllAsync(testPath)).Success);
            Assert.True((await git.CommitAsync(testPath, "Initial test commit")).Success);
            Assert.True((await git.SetRemoteAsync(testPath, "origin", "git@github-personal:owner/repository.git")).Success);
            var status = await git.GetStatusAsync(testPath);
            Assert.Equal("test@example.com", status.UserEmail);
            Assert.Equal("git@github-personal:owner/repository.git", status.RemoteUrl);
            Assert.Empty(status.Changes);
            var history = await git.GetHistoryAsync(testPath);
            Assert.Single(history);
            Assert.Equal("Initial test commit", history[0].Subject);
        }
        finally
        {
            if (Directory.Exists(testPath) && Path.GetFullPath(testPath).StartsWith(Path.GetFullPath(basePath), StringComparison.OrdinalIgnoreCase))
                SafeDelete(testPath);
        }
    }

    [Fact]
    public async Task Push_sets_upstream_and_writes_commit_to_remote()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "GitHubAccountManagerTests");
        var testRoot = Path.Combine(basePath, Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(testRoot, "project");
        var remotePath = Path.Combine(testRoot, "remote.git");
        Directory.CreateDirectory(projectPath);
        try
        {
            var runner = new ProcessRunner();
            var git = new GitService(runner);
            Assert.True((await runner.RunAsync("git", ["init", "--bare", remotePath], testRoot)).Success);
            Assert.True((await git.InitializeAsync(projectPath)).Success);
            Assert.True((await git.SetIdentityAsync(projectPath,
                new AccountProfile { GitUserName = "Test User", GitEmail = "test@example.com" })).Success);
            await File.WriteAllTextAsync(Path.Combine(projectPath, "README.md"), "# Published project\n");
            Assert.True((await git.StageAllAsync(projectPath)).Success);
            Assert.True((await git.CommitAsync(projectPath, "Initial commit")).Success);
            Assert.True((await git.SetRemoteAsync(projectPath, "origin", remotePath)).Success);

            var pushed = await git.PushAsync(projectPath);

            Assert.True(pushed.Success, pushed.CombinedOutput);
            var remoteHead = await runner.RunAsync("git", ["--git-dir", remotePath, "rev-parse", "refs/heads/main"]);
            Assert.True(remoteHead.Success, remoteHead.CombinedOutput);
            Assert.False(string.IsNullOrWhiteSpace(remoteHead.StandardOutput));
        }
        finally
        {
            if (Directory.Exists(testRoot) && Path.GetFullPath(testRoot).StartsWith(Path.GetFullPath(basePath), StringComparison.OrdinalIgnoreCase))
                SafeDelete(testRoot);
        }
    }

    private static void SafeDelete(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }
}
