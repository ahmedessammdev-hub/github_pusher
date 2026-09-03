using System.Globalization;
using System.Text.RegularExpressions;

namespace GitHubAccountManager.Core;

public sealed class GitService(IProcessRunner runner)
{
    public async Task<bool> IsAvailableAsync(CancellationToken token = default) =>
        (await runner.RunAsync("git", ["--version"], timeout: TimeSpan.FromSeconds(5), cancellationToken: token)).Success;

    public async Task<string?> FindRootAsync(string path, CancellationToken token = default)
    {
        if (!Directory.Exists(path)) return null;
        var result = await RunAsync(path, ["rev-parse", "--show-toplevel"], token);
        return result.Success ? result.StandardOutput.Trim() : null;
    }

    public Task<CommandResult> InitializeAsync(string path, CancellationToken token = default) =>
        RunAsync(path, ["init", "-b", "main"], token);

    public async Task<bool> HasCommitsAsync(string repository, CancellationToken token = default) =>
        (await RunAsync(repository, ["rev-parse", "--verify", "HEAD"], token)).Success;

    public async Task<RepositoryStatus> GetStatusAsync(string repository, string remoteName = "origin",
        CancellationToken token = default)
    {
        var root = await FindRootAsync(repository, token) ?? throw new InvalidOperationException("The selected folder is not a Git repository.");
        var status = await RunAsync(root, ["status", "--porcelain=v1", "--branch", "-z"], token);
        if (!status.Success) throw new InvalidOperationException(status.CombinedOutput);
        var records = status.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var branch = ""; var upstream = ""; var ahead = 0; var behind = 0;
        var changes = new List<GitFileChange>();
        foreach (var record in records)
        {
            if (record.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = record[3..];
                const string noCommits = "No commits yet on ";
                const string initialCommit = "Initial commit on ";
                var pieces = heading.Split("...", 2, StringSplitOptions.None);
                branch = heading.StartsWith(noCommits, StringComparison.Ordinal)
                    ? heading[noCommits.Length..].Trim()
                    : heading.StartsWith(initialCommit, StringComparison.Ordinal)
                        ? heading[initialCommit.Length..].Trim()
                        : pieces[0].Split(' ', 2)[0];
                if (pieces.Length > 1)
                {
                    upstream = pieces[1].Split(' ', 2)[0];
                    var aheadMatch = Regex.Match(heading, @"ahead (?<n>\d+)");
                    var behindMatch = Regex.Match(heading, @"behind (?<n>\d+)");
                    if (aheadMatch.Success) int.TryParse(aheadMatch.Groups["n"].Value, out ahead);
                    if (behindMatch.Success) int.TryParse(behindMatch.Groups["n"].Value, out behind);
                }
            }
            else if (record.Length >= 3)
            {
                changes.Add(new(record[..2], record[3..]));
            }
        }
        return new RepositoryStatus
        {
            RootPath = root,
            Branch = branch,
            Upstream = upstream,
            Ahead = ahead,
            Behind = behind,
            UserName = await GetConfigAsync(root, "user.name", token),
            UserEmail = await GetConfigAsync(root, "user.email", token),
            RemoteUrl = await GetRemoteUrlAsync(root, remoteName, false, token),
            PushUrl = await GetRemoteUrlAsync(root, remoteName, true, token),
            Changes = changes
        };
    }

    public Task<CommandResult> StageAsync(string repository, IEnumerable<string> paths, CancellationToken token = default) =>
        RunAsync(repository, ["add", "--", .. paths], token);
    public Task<CommandResult> StageAllAsync(string repository, CancellationToken token = default) =>
        RunAsync(repository, ["add", "--all"], token);
    public async Task<CommandResult> UnstageAsync(string repository, IEnumerable<string> paths, CancellationToken token = default)
    {
        var selected = paths.ToArray();
        var result = await RunAsync(repository, ["restore", "--staged", "--", .. selected], token);
        if (result.Success) return result;
        var hasHead = await RunAsync(repository, ["rev-parse", "--verify", "HEAD"], token);
        return hasHead.Success ? result : await RunAsync(repository, ["rm", "--cached", "-r", "--", .. selected], token);
    }
    public Task<CommandResult> CommitAsync(string repository, string message, bool amend = false, CancellationToken token = default) =>
        RunAsync(repository, amend ? ["commit", "--amend", "-m", message] : ["commit", "-m", message], token);
    public Task<CommandResult> FetchAsync(string repository, CancellationToken token = default) =>
        RunAsync(repository, ["fetch", "--all", "--prune"], token, TimeSpan.FromMinutes(2));
    public Task<CommandResult> PullAsync(string repository, CancellationToken token = default) =>
        RunAsync(repository, ["pull", "--ff-only"], token, TimeSpan.FromMinutes(2));
    public Task<CommandResult> PushAsync(string repository, string remote = "origin", bool dryRun = false, CancellationToken token = default) =>
        RunAsync(repository, dryRun ? ["push", "--dry-run", remote, "HEAD"] : ["push", "-u", remote, "HEAD"], token, TimeSpan.FromMinutes(3));
    public Task<CommandResult> StashAsync(string repository, string message, CancellationToken token = default) =>
        RunAsync(repository, ["stash", "push", "--include-untracked", "-m", message], token);
    public Task<CommandResult> ApplyLatestStashAsync(string repository, CancellationToken token = default) =>
        RunAsync(repository, ["stash", "pop"], token);
    public Task<CommandResult> CreateBranchAsync(string repository, string branch, CancellationToken token = default) =>
        RunAsync(repository, ["switch", "-c", branch], token);
    public Task<CommandResult> SwitchBranchAsync(string repository, string branch, CancellationToken token = default) =>
        RunAsync(repository, ["switch", branch], token);

    public async Task<IReadOnlyList<string>> GetBranchesAsync(string repository, CancellationToken token = default)
    {
        var result = await RunAsync(repository, ["for-each-ref", "--format=%(refname:short)", "refs/heads"], token);
        return result.Success ? result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries) : [];
    }

    public async Task<IReadOnlyList<CommitInfo>> GetHistoryAsync(string repository, int count = 50, CancellationToken token = default)
    {
        var separator = "\u001f";
        var result = await RunAsync(repository,
            ["log", $"-{count}", $"--pretty=format:%h{separator}%an{separator}%aI{separator}%s"], token);
        if (!result.Success) return [];
        var commits = new List<CommitInfo>();
        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(separator);
            if (parts.Length == 4 && DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                commits.Add(new(parts[0], parts[1], date, parts[3]));
        }
        return commits;
    }

    public Task<CommandResult> SetIdentityAsync(string repository, AccountProfile account, CancellationToken token = default) =>
        RunSequenceAsync(repository,
            [["config", "--local", "user.name", account.GitUserName],
             ["config", "--local", "user.email", account.GitEmail],
             ["config", "--local", "user.useConfigOnly", "true"]], token);

    public async Task<CommandResult> SetRemoteAsync(string repository, string remoteName, string url, CancellationToken token = default)
    {
        var exists = await RunAsync(repository, ["remote", "get-url", remoteName], token);
        var set = await RunAsync(repository, exists.Success ? ["remote", "set-url", remoteName, url] : ["remote", "add", remoteName, url], token);
        if (!set.Success) return set;
        await RunAsync(repository, ["config", "--unset-all", $"remote.{remoteName}.pushurl"], token);
        return await RunAsync(repository, ["config", "--add", $"remote.{remoteName}.pushurl", url], token);
    }

    public async Task<string> GetConfigAsync(string repository, string key, CancellationToken token = default)
    {
        var result = await RunAsync(repository, ["config", "--local", "--get", key], token);
        return result.Success ? result.StandardOutput.Trim() : "";
    }

    public async Task<string> GetRemoteUrlAsync(string repository, string remoteName, bool push, CancellationToken token = default)
    {
        var arguments = push ? new[] { "remote", "get-url", "--push", remoteName } : ["remote", "get-url", remoteName];
        var result = await RunAsync(repository, arguments, token);
        return result.Success ? result.StandardOutput.Trim() : "";
    }

    public Task<CommandResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken token = default,
        TimeSpan? timeout = null) => runner.RunAsync("git", arguments, repository, timeout ?? TimeSpan.FromSeconds(30), token);

    private async Task<CommandResult> RunSequenceAsync(string repository, IEnumerable<IReadOnlyList<string>> commands,
        CancellationToken token)
    {
        CommandResult last = new(0, "", "");
        foreach (var command in commands)
        {
            last = await RunAsync(repository, command, token);
            if (!last.Success) return last;
        }
        return last;
    }
}
