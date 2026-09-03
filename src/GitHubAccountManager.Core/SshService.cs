using System.Text;

namespace GitHubAccountManager.Core;

public sealed class SshService(IProcessRunner runner)
{
    public string SshDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
    public string MainConfigPath => Path.Combine(SshDirectory, "config");
    public string ManagedConfigPath => Path.Combine(SshDirectory, "github-account-manager.conf");

    public async Task<OperationResult> EnsureConfigurationAsync(IEnumerable<AccountProfile> accounts,
        CancellationToken token = default)
    {
        Directory.CreateDirectory(SshDirectory);
        var builder = new StringBuilder("# Managed by GitHub Account Manager.\n");
        foreach (var account in accounts)
        {
            builder.AppendLine().AppendLine($"Host {account.SshAlias}")
                .AppendLine($"    HostName {account.HostName}")
                .AppendLine("    User git")
                .AppendLine($"    IdentityFile \"{ExpandPath(account.PrivateKeyPath).Replace('\\', '/')}\"")
                .AppendLine("    IdentitiesOnly yes");
        }
        var managedContent = builder.ToString();
        var existingManaged = File.Exists(ManagedConfigPath) ? await File.ReadAllTextAsync(ManagedConfigPath, token) : "";
        if (!string.Equals(existingManaged, managedContent, StringComparison.Ordinal))
        {
            await BackupIfExistsAsync(ManagedConfigPath, token);
            await WriteAtomicAsync(ManagedConfigPath, managedContent, token);
        }
        var include = "Include ~/.ssh/github-account-manager.conf";
        var main = File.Exists(MainConfigPath) ? await File.ReadAllTextAsync(MainConfigPath, token) : "";
        if (!main.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Trim().Equals(include, StringComparison.OrdinalIgnoreCase)))
        {
            await BackupIfExistsAsync(MainConfigPath, token);
            await WriteAtomicAsync(MainConfigPath, include + Environment.NewLine + main, token);
        }
        return OperationResult.Ok("SSH configuration is ready.");
    }

    public async Task<OperationResult> TestAccountAsync(AccountProfile account, int timeoutSeconds = 15,
        CancellationToken token = default)
    {
        var key = ExpandPath(account.PrivateKeyPath);
        if (!File.Exists(key)) return OperationResult.Fail($"Private key not found: {key}");
        var fingerprint = await runner.RunAsync("ssh-keygen", ["-lf", key], timeout: TimeSpan.FromSeconds(5), cancellationToken: token);
        if (!fingerprint.Success) return OperationResult.Fail("The selected file is not a valid SSH private key.");
        var result = await runner.RunAsync("ssh",
            ["-F", ManagedConfigPath, "-o", "BatchMode=yes", "-o", $"ConnectTimeout={timeoutSeconds}", "-T", $"git@{account.SshAlias}"],
            timeout: TimeSpan.FromSeconds(timeoutSeconds + 5), cancellationToken: token);
        var expected = $"Hi {account.GitHubUser}!";
        return result.CombinedOutput.Contains(expected, StringComparison.OrdinalIgnoreCase)
            ? OperationResult.Ok($"Authenticated as {account.GitHubUser}.")
            : OperationResult.Fail($"SSH did not authenticate as {account.GitHubUser}.\n{result.CombinedOutput}");
    }

    public Task<CommandResult> GenerateKeyAsync(AccountProfile account, CancellationToken token = default)
    {
        var key = ExpandPath(account.PrivateKeyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(key)!);
        if (File.Exists(key)) return Task.FromResult(new CommandResult(1, "", "The key already exists and will not be overwritten."));
        return runner.RunAsync("ssh-keygen", ["-t", "ed25519", "-N", "", "-C", account.GitEmail, "-f", key],
            timeout: TimeSpan.FromSeconds(30), cancellationToken: token);
    }

    public Task<CommandResult> AddToAgentAsync(AccountProfile account, CancellationToken token = default) =>
        runner.RunAsync("ssh-add", [ExpandPath(account.PrivateKeyPath)], timeout: TimeSpan.FromMinutes(2), cancellationToken: token);

    public static string ExpandPath(string value)
    {
        if (value == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (value.StartsWith("~/") || value.StartsWith("~\\"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..]);
        return Environment.ExpandEnvironmentVariables(value);
    }

    private static async Task BackupIfExistsAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return;
        var backup = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.bak";
        await using var input = File.OpenRead(path);
        await using var output = File.Create(backup);
        await input.CopyToAsync(output, token);
        foreach (var old in new DirectoryInfo(Path.GetDirectoryName(path)!).GetFiles(Path.GetFileName(path) + ".*.bak")
                     .OrderByDescending(file => file.LastWriteTimeUtc).Skip(10))
            old.Delete();
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), token);
        File.Move(temporary, path, true);
    }
}
