using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using GitHubAccountManager.Core;

namespace GitHubAccountManager.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IProcessRunner _runner = new ProcessRunner();
    private readonly SettingsService _settingsService = new();
    private readonly GitService _git;
    private readonly SshService _ssh;
    private readonly AccountSwitchService _switcher;
    private readonly GitHubAuthService _githubAuth;
    private UserSettings _settings = new();
    private string _repositoryPath = Environment.CurrentDirectory;
    private string _branch = "Not loaded";
    private string _currentAccount = "Not detected";
    private string _identity = "Not configured";
    private string _remoteUrl = "Not configured";
    private string _upstream = "None";
    private string _syncState = "—";
    private string _statusMessage = "Ready";
    private string _logText = "";
    private bool _isBusy;
    private AccountProfile? _selectedAccount;
    private string? _selectedBranch;
    private string _githubAuthStatus = "Checking authentication...";
    private bool _isGitHubAuthenticated;

    public MainViewModel()
    {
        _git = new GitService(_runner);
        _ssh = new SshService(_runner);
        _switcher = new AccountSwitchService(_git, _ssh);
        _githubAuth = new GitHubAuthService(_runner);
    }

    public ObservableCollection<GitFileChange> Changes { get; } = [];
    public ObservableCollection<AccountProfile> Accounts { get; } = [];
    public ObservableCollection<string> Branches { get; } = [];
    public ObservableCollection<CommitInfo> History { get; } = [];

    public string RepositoryPath { get => _repositoryPath; set => Set(ref _repositoryPath, value); }
    public string Branch { get => _branch; private set => Set(ref _branch, value); }
    public string CurrentAccount { get => _currentAccount; private set => Set(ref _currentAccount, value); }
    public string Identity { get => _identity; private set => Set(ref _identity, value); }
    public string RemoteUrl { get => _remoteUrl; private set => Set(ref _remoteUrl, value); }
    public string Upstream { get => _upstream; private set => Set(ref _upstream, value); }
    public string SyncState { get => _syncState; private set => Set(ref _syncState, value); }
    public int ChangeCount => Changes.Count;
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    public string LogText { get => _logText; private set => Set(ref _logText, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public AccountProfile? SelectedAccount { get => _selectedAccount; set => Set(ref _selectedAccount, value); }
    public string? SelectedBranch { get => _selectedBranch; set => Set(ref _selectedBranch, value); }
    public string GitHubAuthStatus { get => _githubAuthStatus; private set => Set(ref _githubAuthStatus, value); }
    public bool IsGitHubAuthenticated { get => _isGitHubAuthenticated; private set => Set(ref _isGitHubAuthenticated, value); }
    public string CommitMessage { get; set; } = "";
    public string RemoteInput { get; set; } = "";
    public string NewBranchName { get; set; } = "";
    public string NewRepositoryName { get; set; } = "";
    public string NewRepositoryDescription { get; set; } = "";
    public string InitialCommitMessage { get; set; } = "Initial commit";
    public bool NewRepositoryPrivate { get; set; } = true;
    public bool UseSshRemote { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync(string? path = null)
    {
        _settings = await _settingsService.LoadAsync();
        Accounts.Clear();
        foreach (var account in _settings.Accounts) Accounts.Add(account);
        SelectedAccount = Accounts.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(path)) RepositoryPath = path;
        await RefreshAsync();
        await RefreshGitHubAuthenticationAsync();
    }

    public async Task RefreshAsync()
    {
        await BusyAsync("Refreshing repository...", async () =>
        {
            var root = await _git.FindRootAsync(RepositoryPath);
            if (root is null)
            {
                Branch = "Not a repository"; CurrentAccount = "—"; Identity = "—"; RemoteUrl = "—";
                Upstream = "—"; SyncState = "—"; Changes.Clear(); Branches.Clear(); History.Clear();
                StatusMessage = "Choose Initialize Git or select another folder.";
                return;
            }
            RepositoryPath = root;
            var status = await _git.GetStatusAsync(root, _settings.RemoteName);
            Branch = string.IsNullOrWhiteSpace(status.Branch) ? "Detached HEAD" : status.Branch;
            Identity = string.IsNullOrWhiteSpace(status.UserEmail) ? "Not configured" : $"{status.UserName} <{status.UserEmail}>";
            RemoteUrl = string.IsNullOrWhiteSpace(status.RemoteUrl) ? "Not configured" : status.RemoteUrl;
            RemoteInput = status.RemoteUrl;
            Upstream = string.IsNullOrWhiteSpace(status.Upstream) ? "None" : status.Upstream;
            SyncState = status.Ahead == 0 && status.Behind == 0 ? "Up to date" : $"↑ {status.Ahead}  ↓ {status.Behind}";
            CurrentAccount = DetectAccount(status)?.DisplayName ?? "Not detected";
            Changes.Clear(); foreach (var change in status.Changes) Changes.Add(change); OnPropertyChanged(nameof(ChangeCount));
            Branches.Clear(); foreach (var branch in await _git.GetBranchesAsync(root)) Branches.Add(branch);
            History.Clear(); foreach (var commit in await _git.GetHistoryAsync(root)) History.Add(commit);
            StatusMessage = "Repository loaded.";
        });
    }

    public void AddAccount()
    {
        var account = new AccountProfile
        {
            DisplayName = "New account", GitUserName = Environment.UserName, HostName = "github.com",
            SshAlias = $"github-{Accounts.Count + 1}", PrivateKeyPath = $"~/.ssh/github-{Accounts.Count + 1}"
        };
        Accounts.Add(account); SelectedAccount = account;
    }

    public void DeleteSelectedAccount()
    {
        if (SelectedAccount is null) return;
        var index = Accounts.IndexOf(SelectedAccount); Accounts.Remove(SelectedAccount);
        SelectedAccount = Accounts.Count == 0 ? null : Accounts[Math.Clamp(index, 0, Accounts.Count - 1)];
    }

    public async Task<OperationResult> SaveAccountsAsync()
    {
        _settings.Accounts = Accounts.ToList();
        return await BusyResultAsync("Saving accounts...", async () =>
        {
            await _settingsService.SaveAsync(_settings);
            return OperationResult.Ok("Accounts saved to the current Windows user profile.");
        });
    }

    public async Task RefreshGitHubAuthenticationAsync()
    {
        GitHubAuthStatus = "Checking authentication...";
        var state = await _githubAuth.GetStatusAsync(SelectedAccount);
        IsGitHubAuthenticated = state.IsAuthenticated;
        GitHubAuthStatus = state.Message;
    }

    public async Task<OperationResult> SignInGitHubAsync()
    {
        if (SelectedAccount is null) return OperationResult.Fail("Select an account first.");
        var saved = await SaveAccountsAsync();
        if (!saved.Success) return saved;
        var result = await BusyResultAsync("Waiting for GitHub browser sign-in...", () => _githubAuth.LoginAsync(SelectedAccount));
        await RefreshGitHubAuthenticationAsync();
        return result;
    }

    public async Task<OperationResult> SignOutGitHubAsync()
    {
        if (SelectedAccount is null) return OperationResult.Fail("Select an account first.");
        var result = await BusyResultAsync("Signing out of GitHub...", () => _githubAuth.LogoutAsync(SelectedAccount));
        await RefreshGitHubAuthenticationAsync();
        return result;
    }

    public async Task<OperationResult> SwitchAccountAsync(bool identityOnly, bool dryRun)
    {
        if (SelectedAccount is null) return OperationResult.Fail("Select an account first.");
        var saved = await SaveAccountsAsync();
        if (!saved.Success) return saved;
        OperationResult result = OperationResult.Fail("No operation was run.");
        await BusyAsync(dryRun ? "Preparing preview..." : "Switching account...", async () =>
        {
            var progress = new Progress<string>(message => { StatusMessage = message; AppendLog(message); });
            result = await _switcher.SwitchAsync(RepositoryPath, SelectedAccount, _settings, identityOnly, dryRun, progress);
            AppendLog(result.Message);
        });
        if (result.Success && !dryRun) await RefreshAsync();
        return result;
    }

    public Task<OperationResult> RestoreAsync() => BusyResultAsync("Restoring latest backup...",
        () => _switcher.RestoreLatestAsync(RepositoryPath));
    public Task<OperationResult> SetupSshAsync() => BusyResultAsync("Updating SSH configuration...",
        () => _ssh.EnsureConfigurationAsync(Accounts));
    public Task<OperationResult> TestSshAsync() => SelectedAccount is null
        ? Task.FromResult(OperationResult.Fail("Select an account first."))
        : BusyResultAsync("Testing SSH...", () => _ssh.TestAccountAsync(SelectedAccount, _settings.NetworkTimeoutSeconds));

    public async Task<OperationResult> GenerateKeyAsync()
    {
        if (SelectedAccount is null) return OperationResult.Fail("Select an account first.");
        return await RunCommandAsync("Generating SSH key...", () => _ssh.GenerateKeyAsync(SelectedAccount));
    }
    public async Task<OperationResult> AddToAgentAsync()
    {
        if (SelectedAccount is null) return OperationResult.Fail("Select an account first.");
        return await RunCommandAsync("Adding key to ssh-agent...", () => _ssh.AddToAgentAsync(SelectedAccount));
    }

    public Task<OperationResult> StageAllAsync() => RunGitAsync("Staging all files...", () => _git.StageAllAsync(RepositoryPath));
    public Task<OperationResult> StageAsync(IEnumerable<string> paths) => RunGitAsync("Staging selected files...", () => _git.StageAsync(RepositoryPath, paths));
    public Task<OperationResult> UnstageAsync(IEnumerable<string> paths) => RunGitAsync("Unstaging files...", () => _git.UnstageAsync(RepositoryPath, paths));
    public Task<OperationResult> FetchAsync() => RunGitAsync("Fetching...", () => _git.FetchAsync(RepositoryPath));
    public Task<OperationResult> PullAsync() => RunGitAsync("Pulling with fast-forward only...", () => _git.PullAsync(RepositoryPath));
    public Task<OperationResult> PushAsync(bool dryRun) => RunGitAsync(dryRun ? "Testing push..." : "Pushing...", () => _git.PushAsync(RepositoryPath, _settings.RemoteName, dryRun));
    public Task<OperationResult> StashAsync() => RunGitAsync("Stashing tracked and untracked files...", () => _git.StashAsync(RepositoryPath, $"GitHub Account Manager {DateTime.Now:g}"));

    public async Task<OperationResult> CommitAsync(bool push)
    {
        if (string.IsNullOrWhiteSpace(CommitMessage)) return OperationResult.Fail("Enter a commit message.");
        var result = await RunGitAsync("Committing...", () => _git.CommitAsync(RepositoryPath, CommitMessage.Trim()), false);
        if (!result.Success) return result;
        CommitMessage = ""; OnPropertyChanged(nameof(CommitMessage));
        if (push) result = await RunGitAsync("Pushing commit...", () => _git.PushAsync(RepositoryPath, _settings.RemoteName));
        await RefreshAsync(); return result;
    }

    public async Task<OperationResult> InitializeRepositoryAsync()
    {
        if (!Directory.Exists(RepositoryPath)) return OperationResult.Fail("Folder does not exist.");
        var result = await RunCommandAsync("Initializing Git...", () => _git.InitializeAsync(RepositoryPath));
        if (result.Success) await RefreshAsync(); return result;
    }

    public async Task<OperationResult> SetRemoteAsync()
    {
        if (SelectedAccount is null) return OperationResult.Fail("Select an account to determine the SSH host.");
        if (!RemoteParser.TryParse(RemoteInput, Accounts, SelectedAccount.HostName, out var parsed) || parsed is null)
            return OperationResult.Fail("Enter owner/repository or a supported GitHub URL.");
        if (!parsed.HostName.Equals(SelectedAccount.HostName, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("The remote host does not match the selected account.");
        var url = RemoteParser.BuildSshUrl(SelectedAccount, parsed);
        var result = await RunGitAsync("Setting remote...", () => _git.SetRemoteAsync(RepositoryPath, _settings.RemoteName, url));
        if (result.Success) await RefreshAsync(); return result;
    }

    public async Task<OperationResult> CreateGitIgnoreAsync()
    {
        var path = Path.Combine(RepositoryPath, ".gitignore");
        if (File.Exists(path)) return OperationResult.Fail(".gitignore already exists; it was not overwritten.");
        const string content = "bin/\nobj/\n.vs/\n.idea/\n.vscode/\n.env\n.env.*\n*.user\n*.suo\n*.pfx\n*.p12\n*.pem\n*.key\nlogs/\ndist/\n";
        await File.WriteAllTextAsync(path, content);
        await RefreshAsync(); return OperationResult.Ok("Created a safe starter .gitignore.");
    }

    public Task<OperationResult> CreateGitHubRepositoryAsync(string token) =>
        CreateGitHubRepositoryAsync(token, publishCurrentFolder: false);

    public Task<OperationResult> PublishCurrentFolderAsync(string token) =>
        CreateGitHubRepositoryAsync(token, publishCurrentFolder: true);

    private async Task<OperationResult> CreateGitHubRepositoryAsync(string token, bool publishCurrentFolder)
    {
        if (SelectedAccount is null) return OperationResult.Fail("Select an account first.");
        if (!Directory.Exists(RepositoryPath)) return OperationResult.Fail("Select an existing project folder first.");
        if (string.IsNullOrWhiteSpace(NewRepositoryName)) return OperationResult.Fail("Enter a repository name.");
        if (!RemoteParser.TryParse($"{SelectedAccount.GitHubUser}/{NewRepositoryName.Trim()}", Accounts,
                SelectedAccount.HostName, out _))
            return OperationResult.Fail("Repository name may contain letters, numbers, dots, hyphens, and underscores only.");
        if (publishCurrentFolder && string.IsNullOrWhiteSpace(InitialCommitMessage))
            return OperationResult.Fail("Enter a commit message for the files being published.");
        var credential = await ResolveGitHubCredentialAsync(token);
        if (!credential.Success) return OperationResult.Fail(credential.Message);
        var api = new GitHubApiService(new HttpClient { Timeout = TimeSpan.FromSeconds(_settings.NetworkTimeoutSeconds) });
        OperationResult result = OperationResult.Fail("Not started.");
        await BusyAsync("Verifying GitHub account...", async () =>
        {
            result = await api.VerifyTokenAsync(SelectedAccount.HostName, credential.Token, SelectedAccount.GitHubUser);
            if (!result.Success) return;

            var root = await _git.FindRootAsync(RepositoryPath);
            if (root is null)
            {
                StatusMessage = "Initializing local Git repository...";
                var initialized = await _git.InitializeAsync(RepositoryPath);
                if (!initialized.Success) { result = OperationResult.Fail(initialized.CombinedOutput); return; }
                root = await _git.FindRootAsync(RepositoryPath);
                if (root is null) { result = OperationResult.Fail("Git initialized but its repository root could not be detected."); return; }
            }
            RepositoryPath = root;

            if (publishCurrentFolder)
            {
                StatusMessage = "Checking files before publishing...";
                var identity = await _git.SetIdentityAsync(root, SelectedAccount);
                if (!identity.Success) { result = OperationResult.Fail(identity.CombinedOutput); return; }
                var localStatus = await _git.GetStatusAsync(root, _settings.RemoteName);
                var sensitive = SensitiveFileScanner.FindSuspiciousPaths(localStatus.Changes);
                if (sensitive.Count > 0)
                {
                    result = OperationResult.Fail("Publishing stopped before staging because potentially sensitive files were found:\n\n" +
                                                  string.Join("\n", sensitive) +
                                                  "\n\nAdd them to .gitignore or remove them, then try again.");
                    return;
                }
                var hasCommits = await _git.HasCommitsAsync(root);
                if (localStatus.Changes.Count > 0)
                {
                    StatusMessage = "Staging project files...";
                    var staged = await _git.StageAllAsync(root);
                    if (!staged.Success) { result = OperationResult.Fail(staged.CombinedOutput); return; }
                    StatusMessage = "Creating commit...";
                    var committed = await _git.CommitAsync(root, InitialCommitMessage.Trim());
                    if (!committed.Success) { result = OperationResult.Fail(committed.CombinedOutput); return; }
                    hasCommits = true;
                }
                if (!hasCommits)
                {
                    result = OperationResult.Fail("The folder has no files or commits to publish. Add at least one project file first.");
                    return;
                }
            }

            StatusMessage = "Creating repository...";
            result = await api.CreateRepositoryAsync(SelectedAccount.HostName, credential.Token, new GitHubRepositoryRequest
            {
                Name = NewRepositoryName.Trim(), Description = NewRepositoryDescription.Trim(), IsPrivate = NewRepositoryPrivate
            });
            if (!result.Success) return;
            var parsed = new ParsedRemote(SelectedAccount.HostName, SelectedAccount.GitHubUser, NewRepositoryName.Trim());
            var remoteUrl = UseSshRemote
                ? RemoteParser.BuildSshUrl(SelectedAccount, parsed)
                : RemoteParser.BuildHttpsUrl(SelectedAccount, parsed);
            StatusMessage = "Connecting origin...";
            var remote = await _git.SetRemoteAsync(root, _settings.RemoteName, remoteUrl);
            if (!remote.Success)
            {
                result = OperationResult.Fail($"Created {parsed.RepositoryPath} on GitHub, but setting origin failed:\n{remote.CombinedOutput}");
                return;
            }
            if (publishCurrentFolder)
            {
                StatusMessage = "Pushing the current branch...";
                var pushed = await _git.PushAsync(root, _settings.RemoteName);
                if (!pushed.Success)
                {
                    result = OperationResult.Fail($"Created and connected {parsed.RepositoryPath}, but push failed:\n{pushed.CombinedOutput}");
                    return;
                }
            }
            result = OperationResult.Ok(publishCurrentFolder
                ? $"Created {parsed.RepositoryPath}, connected origin, and pushed the current branch."
                : $"Created and connected {parsed.RepositoryPath}. No files were pushed.");
            AppendLog(result.Message);
        });
        if (await _git.FindRootAsync(RepositoryPath) is not null) await RefreshAsync();
        return result;
    }

    public async Task<OperationResult> UploadPublicKeyAsync(string token)
    {
        if (SelectedAccount is null) return OperationResult.Fail("Select an account first.");
        var credential = await ResolveGitHubCredentialAsync(token);
        if (!credential.Success) return OperationResult.Fail(credential.Message);
        var publicKeyPath = SshService.ExpandPath(string.IsNullOrWhiteSpace(SelectedAccount.SigningKeyPath)
            ? SelectedAccount.PrivateKeyPath + ".pub" : SelectedAccount.SigningKeyPath);
        if (!File.Exists(publicKeyPath)) return OperationResult.Fail($"Public key not found: {publicKeyPath}");
        var api = new GitHubApiService(new HttpClient { Timeout = TimeSpan.FromSeconds(_settings.NetworkTimeoutSeconds) });
        var verified = await api.VerifyTokenAsync(SelectedAccount.HostName, credential.Token, SelectedAccount.GitHubUser);
        if (!verified.Success) return verified;
        return await api.UploadSshKeyAsync(SelectedAccount.HostName, credential.Token,
            $"GitHub Account Manager - {Environment.MachineName}", await File.ReadAllTextAsync(publicKeyPath));
    }

    private Task<GitHubCredentialResult> ResolveGitHubCredentialAsync(string token) =>
        !string.IsNullOrWhiteSpace(token) && SelectedAccount is not null
            ? Task.FromResult(GitHubCredentialResult.Ok(token.Trim()))
            : SelectedAccount is null
                ? Task.FromResult(GitHubCredentialResult.Fail("Select an account first."))
                : _githubAuth.GetTokenAsync(SelectedAccount);

    public Task<OperationResult> CreateBranchAsync() => string.IsNullOrWhiteSpace(NewBranchName)
        ? Task.FromResult(OperationResult.Fail("Enter a branch name."))
        : RunGitAsync("Creating branch...", () => _git.CreateBranchAsync(RepositoryPath, NewBranchName.Trim()));
    public Task<OperationResult> SwitchBranchAsync() => string.IsNullOrWhiteSpace(SelectedBranch)
        ? Task.FromResult(OperationResult.Fail("Select a branch."))
        : RunGitAsync("Switching branch...", () => _git.SwitchBranchAsync(RepositoryPath, SelectedBranch));

    public IReadOnlyList<string> FindSensitiveChanges() => SensitiveFileScanner.FindSuspiciousPaths(Changes);

    public async Task RunDiagnosticsAsync()
    {
        await BusyAsync("Running diagnostics...", async () =>
        {
            AppendLog($"Application: {Environment.ProcessPath}"); AppendLog($"Windows: {Environment.OSVersion}");
            AppendLog($"Working folder: {RepositoryPath}"); AppendLog($"Git available: {await _git.IsAvailableAsync()}");
            var ssh = await _runner.RunAsync("ssh", ["-V"], timeout: TimeSpan.FromSeconds(5));
            AppendLog($"SSH available: {ssh.ExitCode >= 0} {ssh.CombinedOutput}");
            AppendLog($"Settings: {_settingsService.SettingsPath}");
            AppendLog("Diagnostics complete. No tokens or key contents were collected.");
        });
    }

    public void ClearLog() => LogText = "";
    public void AppendLog(string message) => LogText += $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}\n";

    private AccountProfile? DetectAccount(RepositoryStatus status)
    {
        var byEmail = Accounts.FirstOrDefault(account => account.GitEmail.Equals(status.UserEmail, StringComparison.OrdinalIgnoreCase));
        if (byEmail is not null) return byEmail;
        return Accounts.FirstOrDefault(account => status.RemoteUrl.Contains(account.SshAlias, StringComparison.OrdinalIgnoreCase));
    }

    private Task<OperationResult> RunGitAsync(string description, Func<Task<CommandResult>> action, bool refresh = true) =>
        RunCommandAsync(description, action, refresh);
    private async Task<OperationResult> RunCommandAsync(string description, Func<Task<CommandResult>> action, bool refresh = true)
    {
        OperationResult operation = OperationResult.Fail("Operation failed.");
        await BusyAsync(description, async () =>
        {
            var result = await action(); operation = result.Success ? OperationResult.Ok(result.CombinedOutput.DefaultIfEmpty("Completed.")) : OperationResult.Fail(result.CombinedOutput);
            AppendLog($"{description} {(result.Success ? "OK" : "FAILED")}\n{result.CombinedOutput}");
        });
        if (refresh && operation.Success) await RefreshAsync(); return operation;
    }
    private async Task<OperationResult> BusyResultAsync(string description, Func<Task<OperationResult>> action)
    {
        OperationResult result = OperationResult.Fail("Operation failed.");
        await BusyAsync(description, async () => { result = await action(); AppendLog(result.Message); }); return result;
    }
    private async Task BusyAsync(string description, Func<Task> action)
    {
        if (IsBusy) throw new InvalidOperationException("Another operation is still running.");
        IsBusy = true; StatusMessage = description;
        try { await action(); if (StatusMessage == description) StatusMessage = "Ready"; }
        catch (Exception exception) { AppendLog("ERROR: " + exception.Message); StatusMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

internal static class TextExtensions
{
    public static string DefaultIfEmpty(this string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
