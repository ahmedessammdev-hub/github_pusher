using System.Text.RegularExpressions;

namespace GitHubAccountManager.Core;

public static partial class RemoteParser
{
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")]
    private static partial Regex OwnerPattern();
    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepositoryPattern();

    public static bool TryParse(string? value, IEnumerable<AccountProfile> accounts, string defaultHost,
        out ParsedRemote? remote)
    {
        remote = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var input = value.Trim().Trim('"', '\'');
        string host;
        string path;

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "http" or "https" or "git" or "ssh"))
        {
            host = uri.Host;
            path = uri.AbsolutePath.Trim('/');
        }
        else
        {
            var scp = Regex.Match(input, "^(?:[^@/:]+@)?(?<host>[^:]+):(?<path>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (scp.Success)
            {
                host = scp.Groups["host"].Value;
                path = scp.Groups["path"].Value;
            }
            else
            {
                var parts = input.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2) return false;
                host = defaultHost;
                path = input;
            }
        }

        var matchingAlias = accounts.FirstOrDefault(account =>
            string.Equals(account.SshAlias, host, StringComparison.OrdinalIgnoreCase));
        if (matchingAlias is not null) host = matchingAlias.HostName;
        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        var pathParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pathParts.Length != 2) return false;
        var owner = pathParts[0];
        var repository = pathParts[1];
        if (!OwnerPattern().IsMatch(owner) || owner.EndsWith('-') || !RepositoryPattern().IsMatch(repository) ||
            repository is "." or "..") return false;
        remote = new(host, owner, repository);
        return true;
    }

    public static string BuildSshUrl(AccountProfile account, ParsedRemote remote) =>
        $"git@{account.SshAlias}:{remote.RepositoryPath}.git";

    public static string BuildHttpsUrl(AccountProfile account, ParsedRemote remote)
    {
        var user = Uri.EscapeDataString(account.GitHubUser.Trim());
        return $"https://{user}@{remote.HostName}/{remote.RepositoryPath}.git";
    }
}
