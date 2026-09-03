namespace GitHubAccountManager.Core;

public sealed class AccountProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "New account";
    public string GitUserName { get; set; } = "";
    public string GitEmail { get; set; } = "";
    public string GitHubUser { get; set; } = "";
    public string HostName { get; set; } = "github.com";
    public string SshAlias { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string SigningKeyPath { get; set; } = "";
    public bool EnableCommitSigning { get; set; }
    public override string ToString() => DisplayName;
}

public sealed class UserSettings
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "en";
    public string RemoteName { get; set; } = "origin";
    public int NetworkTimeoutSeconds { get; set; } = 30;
    public int BackupRetention { get; set; } = 20;
    public List<AccountProfile> Accounts { get; set; } = [];
}

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
    public string CombinedOutput => string.Join(Environment.NewLine,
        new[] { StandardOutput, StandardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed record GitFileChange(string Code, string Path)
{
    public bool IsStaged => Code.Length > 0 && Code[0] != ' ' && Code[0] != '?';
    public bool IsUntracked => Code == "??";
}

public sealed record CommitInfo(string Hash, string Author, DateTimeOffset Date, string Subject);

public sealed class RepositoryStatus
{
    public required string RootPath { get; init; }
    public string Branch { get; init; } = "";
    public string Upstream { get; init; } = "";
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public string UserName { get; init; } = "";
    public string UserEmail { get; init; } = "";
    public string RemoteUrl { get; init; } = "";
    public string PushUrl { get; init; } = "";
    public IReadOnlyList<GitFileChange> Changes { get; init; } = [];
}

public sealed record ParsedRemote(string HostName, string Owner, string Repository)
{
    public string RepositoryPath => $"{Owner}/{Repository}";
}

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message) => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

public sealed class GitHubRepositoryRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsPrivate { get; set; } = true;
}
