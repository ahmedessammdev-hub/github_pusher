using GitHubAccountManager.Core;
using Xunit;

namespace GitHubAccountManager.Tests;

public sealed class GitHubAuthServiceTests
{
    [Fact]
    public async Task Status_matches_selected_authenticated_account()
    {
        var runner = new StubRunner(new CommandResult(0, "2.9.0", ""), new CommandResult(0, "octocat\nother-user\n", ""));
        var service = new GitHubAuthService(runner);

        var state = await service.GetStatusAsync(new AccountProfile { GitHubUser = "octocat" });

        Assert.True(state.ManagerAvailable);
        Assert.True(state.IsAuthenticated);
        Assert.Equal("octocat", state.UserName);
    }

    [Fact]
    public async Task Login_requires_a_username()
    {
        var service = new GitHubAuthService(new StubRunner());

        var result = await service.LoginAsync(new AccountProfile());

        Assert.False(result.Success);
        Assert.Contains("username", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Credential_token_is_parsed_without_exposing_other_fields()
    {
        var runner = new StubRunner(new CommandResult(0, "protocol=https\nhost=github.com\nusername=octocat\npassword=secret-token\n", ""));
        var service = new GitHubAuthService(runner);

        var result = await service.GetTokenAsync(new AccountProfile { GitHubUser = "octocat", HostName = "github.com" });

        Assert.True(result.Success);
        Assert.Equal("secret-token", result.Token);
        Assert.Contains("username=octocat", runner.LastStandardInput);
    }

    private sealed class StubRunner(params CommandResult[] results) : IProcessRunner
    {
        private readonly Queue<CommandResult> _results = new(results);
        public string LastStandardInput { get; private set; } = "";

        public Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments, string? workingDirectory = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default, string? standardInput = null)
        {
            LastStandardInput = standardInput ?? "";
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new CommandResult(-1, "", "No result configured."));
        }
    }
}
