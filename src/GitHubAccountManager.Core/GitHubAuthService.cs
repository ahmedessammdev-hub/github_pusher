namespace GitHubAccountManager.Core;

public sealed record GitHubAuthState(bool ManagerAvailable, bool IsAuthenticated, string UserName, string Message);

public sealed record GitHubCredentialResult(bool Success, string Token, string Message)
{
    public static GitHubCredentialResult Ok(string token) => new(true, token, "Authenticated credential loaded.");
    public static GitHubCredentialResult Fail(string message) => new(false, "", message);
}

public sealed class GitHubAuthService(IProcessRunner runner)
{
    public async Task<GitHubAuthState> GetStatusAsync(AccountProfile? account, CancellationToken token = default)
    {
        var availability = await runner.RunAsync("git", ["credential-manager", "--version"],
            timeout: TimeSpan.FromSeconds(10), cancellationToken: token);
        if (!availability.Success)
            return new(false, false, "", "Git Credential Manager is not available. Install or repair Git for Windows.");

        var list = await runner.RunAsync("git", ["credential-manager", "github", "list"],
            timeout: TimeSpan.FromSeconds(15), cancellationToken: token);
        if (!list.Success)
            return new(true, false, "", "No authenticated GitHub account was found.");

        var users = list.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.Contains(':') && !line.Contains(' '))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requested = account?.GitHubUser?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(requested) && users.Contains(requested, StringComparer.OrdinalIgnoreCase))
            return new(true, true, requested, $"Connected as {requested}");
        if (users.Length > 0)
            return new(true, false, users[0], string.IsNullOrWhiteSpace(requested)
                ? $"Authenticated account available: {users[0]}"
                : $"Signed in as {users[0]}, not {requested}");
        return new(true, false, "", "Not signed in to GitHub.");
    }

    public async Task<OperationResult> LoginAsync(AccountProfile account, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(account.GitHubUser))
            return OperationResult.Fail("Enter the GitHub username before signing in.");
        var hostUrl = BuildHostUrl(account.HostName);
        var result = await runner.RunAsync("git",
            ["credential-manager", "github", "login", "--url", hostUrl, "--username", account.GitHubUser.Trim(), "--browser", "--force"],
            timeout: TimeSpan.FromMinutes(10), cancellationToken: token);
        if (!result.Success)
            return OperationResult.Fail(result.CombinedOutput.DefaultIfEmpty("GitHub browser sign-in failed."));
        var status = await GetStatusAsync(account, token);
        return status.IsAuthenticated
            ? OperationResult.Ok($"Authenticated as {status.UserName}. Credentials are stored by Windows Credential Manager.")
            : OperationResult.Fail(status.Message);
    }

    public async Task<OperationResult> LogoutAsync(AccountProfile account, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(account.GitHubUser))
            return OperationResult.Fail("Enter the GitHub username first.");
        var result = await runner.RunAsync("git",
            ["credential-manager", "github", "logout", account.GitHubUser.Trim(), "--url", BuildHostUrl(account.HostName), "--no-ui"],
            timeout: TimeSpan.FromSeconds(30), cancellationToken: token);
        return result.Success
            ? OperationResult.Ok($"Signed out {account.GitHubUser} from this Windows user account.")
            : OperationResult.Fail(result.CombinedOutput.DefaultIfEmpty("GitHub sign-out failed."));
    }

    public async Task<GitHubCredentialResult> GetTokenAsync(AccountProfile account, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(account.GitHubUser))
            return GitHubCredentialResult.Fail("Enter the GitHub username first.");
        var input = $"protocol=https\nhost={NormalizeHost(account.HostName)}\nusername={account.GitHubUser.Trim()}\n\n";
        var result = await runner.RunAsync("git", ["credential", "fill"], timeout: TimeSpan.FromSeconds(30),
            cancellationToken: token, standardInput: input);
        if (!result.Success)
            return GitHubCredentialResult.Fail("No reusable GitHub credential was found. Use Sign in with GitHub first.");
        var password = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("password=", StringComparison.Ordinal));
        return password is null
            ? GitHubCredentialResult.Fail("The signed-in credential could not be read. Sign in again or enter a fine-grained token.")
            : GitHubCredentialResult.Ok(password["password=".Length..]);
    }

    private static string BuildHostUrl(string host) => $"https://{NormalizeHost(host)}";
    private static string NormalizeHost(string host) => string.IsNullOrWhiteSpace(host) ? "github.com" : host.Trim().TrimEnd('/');
}

internal static class AuthTextExtensions
{
    public static string DefaultIfEmpty(this string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
