using System.Text.Json;

namespace GitHubAccountManager.Core;

public sealed class AccountSwitchService(GitService git, SshService ssh)
{
    private static readonly string[] ConfigKeys =
    [
        "user.name", "user.email", "user.useConfigOnly", "gpg.format", "user.signingkey", "commit.gpgsign"
    ];

    public async Task<OperationResult> SwitchAsync(string repository, AccountProfile account, UserSettings settings,
        bool identityOnly, bool dryRun, IProgress<string>? progress = null, CancellationToken token = default)
    {
        var root = await git.FindRootAsync(repository, token);
        if (root is null) return OperationResult.Fail("The selected folder is not a Git repository.");
        var status = await git.GetStatusAsync(root, settings.RemoteName, token);
        ParsedRemote? parsed = null;
        string? targetRemote = null;
        if (!identityOnly)
        {
            if (!RemoteParser.TryParse(status.RemoteUrl, settings.Accounts, account.HostName, out parsed) || parsed is null)
                return OperationResult.Fail("The current remote is missing or unsupported. Set a valid owner/repository or GitHub URL first.");
            if (!parsed.HostName.Equals(account.HostName, StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail($"Remote host '{parsed.HostName}' does not match account host '{account.HostName}'.");
            targetRemote = RemoteParser.BuildSshUrl(account, parsed);
        }
        if (dryRun)
            return OperationResult.Ok(identityOnly
                ? $"Would set identity to {account.GitUserName} <{account.GitEmail}>."
                : $"Would set identity and remote to {targetRemote}.");

        progress?.Report("Creating repository backup...");
        var snapshot = await CaptureAsync(root, settings.RemoteName, token);
        var backupPath = await SaveBackupAsync(root, snapshot, settings.BackupRetention, token);
        try
        {
            if (!identityOnly)
            {
                progress?.Report("Preparing SSH configuration...");
                await ssh.EnsureConfigurationAsync(settings.Accounts, token);
                var sshTest = await ssh.TestAccountAsync(account, settings.NetworkTimeoutSeconds, token);
                if (!sshTest.Success) throw new InvalidOperationException(sshTest.Message);
            }
            progress?.Report("Applying local Git identity...");
            var identity = await git.SetIdentityAsync(root, account, token);
            if (!identity.Success) throw new InvalidOperationException(identity.CombinedOutput);
            await ApplySigningAsync(root, account, token);
            if (!identityOnly)
            {
                progress?.Report("Updating fetch and push remotes...");
                var remote = await git.SetRemoteAsync(root, settings.RemoteName, targetRemote!, token);
                if (!remote.Success) throw new InvalidOperationException(remote.CombinedOutput);
                progress?.Report("Verifying repository access...");
                var verify = await git.RunAsync(root, ["ls-remote", settings.RemoteName], token,
                    TimeSpan.FromSeconds(settings.NetworkTimeoutSeconds));
                if (!verify.Success) throw new InvalidOperationException(verify.CombinedOutput);
            }
            return OperationResult.Ok($"Switched to {account.DisplayName}. Backup: {backupPath}");
        }
        catch (Exception exception)
        {
            progress?.Report("Switch failed; restoring previous settings...");
            try { await RestoreAsync(root, snapshot, token); }
            catch (Exception restoreError)
            {
                return OperationResult.Fail($"Switch failed: {exception.Message}\nRollback also failed: {restoreError.Message}\nBackup: {backupPath}");
            }
            return OperationResult.Fail($"Switch failed and was rolled back: {exception.Message}");
        }
    }

    public async Task<OperationResult> RestoreLatestAsync(string repository, CancellationToken token = default)
    {
        var directory = await GetBackupDirectoryAsync(repository, token);
        var latest = Directory.Exists(directory)
            ? new DirectoryInfo(directory).GetFiles("switch-*.json").OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (latest is null) return OperationResult.Fail("No backup was found.");
        var snapshot = JsonSerializer.Deserialize<RepositorySnapshot>(await File.ReadAllTextAsync(latest.FullName, token));
        if (snapshot is null) return OperationResult.Fail("The latest backup is invalid.");
        await RestoreAsync(repository, snapshot, token);
        return OperationResult.Ok($"Restored {latest.Name}.");
    }

    private async Task ApplySigningAsync(string repository, AccountProfile account, CancellationToken token)
    {
        if (!account.EnableCommitSigning)
        {
            await git.RunAsync(repository, ["config", "--local", "commit.gpgsign", "false"], token);
            return;
        }
        var signingKey = SshService.ExpandPath(account.SigningKeyPath);
        if (!File.Exists(signingKey)) throw new FileNotFoundException("Signing public key was not found.", signingKey);
        foreach (var command in new[]
                 {
                     new[] { "config", "--local", "gpg.format", "ssh" },
                     new[] { "config", "--local", "user.signingkey", signingKey },
                     new[] { "config", "--local", "commit.gpgsign", "true" }
                 })
        {
            var result = await git.RunAsync(repository, command, token);
            if (!result.Success) throw new InvalidOperationException(result.CombinedOutput);
        }
    }

    private async Task<RepositorySnapshot> CaptureAsync(string repository, string remoteName, CancellationToken token)
    {
        var values = new Dictionary<string, string?>();
        foreach (var key in ConfigKeys) values[key] = await git.GetConfigAsync(repository, key, token);
        return new RepositorySnapshot
        {
            RemoteName = remoteName,
            RemoteUrl = await git.GetRemoteUrlAsync(repository, remoteName, false, token),
            PushUrl = await git.GetRemoteUrlAsync(repository, remoteName, true, token),
            ConfigValues = values
        };
    }

    private async Task RestoreAsync(string repository, RepositorySnapshot snapshot, CancellationToken token)
    {
        foreach (var pair in snapshot.ConfigValues)
        {
            var arguments = string.IsNullOrEmpty(pair.Value)
                ? new[] { "config", "--local", "--unset-all", pair.Key }
                : ["config", "--local", pair.Key, pair.Value];
            await git.RunAsync(repository, arguments, token);
        }
        if (!string.IsNullOrWhiteSpace(snapshot.RemoteUrl))
        {
            await git.SetRemoteAsync(repository, snapshot.RemoteName, snapshot.RemoteUrl, token);
            if (!string.IsNullOrWhiteSpace(snapshot.PushUrl))
                await git.RunAsync(repository, ["config", "--local", $"remote.{snapshot.RemoteName}.pushurl", snapshot.PushUrl], token);
        }
        else
        {
            await git.RunAsync(repository, ["remote", "remove", snapshot.RemoteName], token);
        }
    }

    private async Task<string> SaveBackupAsync(string repository, RepositorySnapshot snapshot, int retention,
        CancellationToken token)
    {
        var directory = await GetBackupDirectoryAsync(repository, token);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"switch-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), token);
        foreach (var old in new DirectoryInfo(directory).GetFiles("switch-*.json")
                     .OrderByDescending(file => file.LastWriteTimeUtc).Skip(Math.Max(1, retention)))
            old.Delete();
        return path;
    }

    private async Task<string> GetBackupDirectoryAsync(string repository, CancellationToken token)
    {
        var result = await git.RunAsync(repository,
            ["rev-parse", "--path-format=absolute", "--git-path", "github-account-manager/backups"], token);
        if (!result.Success) throw new InvalidOperationException(result.CombinedOutput);
        return result.StandardOutput.Trim();
    }

    private sealed class RepositorySnapshot
    {
        public string RemoteName { get; set; } = "origin";
        public string RemoteUrl { get; set; } = "";
        public string PushUrl { get; set; } = "";
        public Dictionary<string, string?> ConfigValues { get; set; } = [];
    }
}
