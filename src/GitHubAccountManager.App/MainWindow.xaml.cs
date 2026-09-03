using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GitHubAccountManager.Core;
using Microsoft.Win32;

namespace GitHubAccountManager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += (_, _) => EnableImmersiveDarkTitleBar();
        Loaded += async (_, _) => await _viewModel.InitializeAsync(GetStartupPath());
    }

    private static string GetStartupPath()
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var repoIndex = Array.FindIndex(arguments, value => value.Equals("--repo", StringComparison.OrdinalIgnoreCase));
        if (repoIndex >= 0 && repoIndex + 1 < arguments.Length && Directory.Exists(arguments[repoIndex + 1]))
            return Path.GetFullPath(arguments[repoIndex + 1]);
        return Environment.CurrentDirectory;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select a project folder", InitialDirectory = Directory.Exists(_viewModel.RepositoryPath) ? _viewModel.RepositoryPath : Environment.CurrentDirectory };
        if (dialog.ShowDialog(this) == true) await _viewModel.InitializeAsync(dialog.FolderName);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_viewModel.RepositoryPath)) { ShowError("Select a valid folder first."); return; }
        var windowsTerminal = FindExecutable("wt.exe");
        var start = windowsTerminal is null
            ? new ProcessStartInfo("powershell.exe", "-NoExit")
            : new ProcessStartInfo(windowsTerminal, "-d .");
        start.WorkingDirectory = _viewModel.RepositoryPath; start.UseShellExecute = true; Process.Start(start);
    }

    private async void Fetch_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.FetchAsync());
    private async void Pull_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.PullAsync());
    private async void Push_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.PushAsync(false));
    private async void PushDryRun_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.PushAsync(true));
    private async void Stash_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Stash all tracked and untracked changes?")) Show(await _viewModel.StashAsync());
    }
    private async void StageAll_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.StageAllAsync());
    private async void StageSelected_Click(object sender, RoutedEventArgs e)
    {
        var paths = ChangesGrid.SelectedItems.Cast<GitFileChange>().Select(change => change.Path).ToArray();
        if (paths.Length == 0) { ShowError("Select at least one file."); return; }
        Show(await _viewModel.StageAsync(paths));
    }
    private async void UnstageSelected_Click(object sender, RoutedEventArgs e)
    {
        var paths = ChangesGrid.SelectedItems.Cast<GitFileChange>().Select(change => change.Path).ToArray();
        if (paths.Length == 0) { ShowError("Select at least one file."); return; }
        Show(await _viewModel.UnstageAsync(paths));
    }
    private async void Commit_Click(object sender, RoutedEventArgs e) { if (ConfirmSensitiveFiles()) Show(await _viewModel.CommitAsync(false)); }
    private async void CommitPush_Click(object sender, RoutedEventArgs e) { if (ConfirmSensitiveFiles()) Show(await _viewModel.CommitAsync(true)); }

    private void AddAccount_Click(object sender, RoutedEventArgs e) => _viewModel.AddAccount();
    private async void AccountSelection_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        await _viewModel.RefreshGitHubAuthenticationAsync();
    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Delete this account profile? SSH keys will not be deleted.")) _viewModel.DeleteSelectedAccount();
    }
    private async void SaveAccounts_Click(object sender, RoutedEventArgs e)
    {
        try { Show(await _viewModel.SaveAccountsAsync()); } catch (Exception exception) { ShowError(exception.Message); }
    }
    private async void SignInGitHub_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.SignInGitHubAsync());
    private async void RefreshAuth_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshGitHubAuthenticationAsync();
    private async void SignOutGitHub_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Sign this GitHub account out of Windows Credential Manager? SSH keys will not be deleted."))
            Show(await _viewModel.SignOutGitHubAsync());
    }
    private async void PreviewSwitch_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.SwitchAccountAsync(false, true));
    private async void IdentitySwitch_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Change the local commit identity for this repository?")) Show(await _viewModel.SwitchAccountAsync(true, false));
    }
    private async void FullSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Change local identity, SSH account, fetch URL and push URL? A rollback backup will be created.")) Show(await _viewModel.SwitchAccountAsync(false, false));
    }
    private async void SetupSsh_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Update the managed SSH aliases? Existing SSH files receive timestamped backups.")) Show(await _viewModel.SetupSshAsync());
    }
    private async void TestSsh_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.TestSshAsync());
    private async void GenerateKey_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Generate an unencrypted Ed25519 key at the configured path? Existing keys are never overwritten.")) Show(await _viewModel.GenerateKeyAsync());
    }
    private async void AddAgent_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.AddToAgentAsync());
    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Restore the latest account-switch backup for this repository?")) Show(await _viewModel.RestoreAsync());
    }

    private async void Init_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Initialize Git in the selected folder?")) Show(await _viewModel.InitializeRepositoryAsync());
    }
    private async void SetRemote_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Set both fetch and push URLs using the selected account?")) Show(await _viewModel.SetRemoteAsync());
    }
    private async void GitIgnore_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.CreateGitIgnoreAsync());
    private async void CreateGitHub_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Create this repository on GitHub and connect the selected folder?")) return;
        var result = await _viewModel.CreateGitHubRepositoryAsync(TokenBox.Password);
        TokenBox.Clear(); Show(result);
    }
    private async void PublishGitHub_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Create a new GitHub repository, replace the local origin if present, stage all non-ignored files, commit them, and push the current branch?")) return;
        var result = await _viewModel.PublishCurrentFolderAsync(TokenBox.Password);
        TokenBox.Clear(); Show(result);
    }
    private async void UploadKey_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Upload the selected account public key after verifying token ownership?")) return;
        var result = await _viewModel.UploadPublicKeyAsync(TokenBox.Password);
        TokenBox.Clear(); Show(result);
    }

    private async void CreateBranch_Click(object sender, RoutedEventArgs e) => Show(await _viewModel.CreateBranchAsync());
    private async void SwitchBranch_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Switch branches? Uncommitted changes must be compatible with the target branch.")) Show(await _viewModel.SwitchBranchAsync());
    }
    private async void Diagnostics_Click(object sender, RoutedEventArgs e) => await _viewModel.RunDiagnosticsAsync();
    private void ClearLog_Click(object sender, RoutedEventArgs e) => _viewModel.ClearLog();
    private void CopyLog_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(_viewModel.LogText)) Clipboard.SetText(_viewModel.LogText); }

    private bool ConfirmSensitiveFiles()
    {
        var files = _viewModel.FindSensitiveChanges();
        return files.Count == 0 || Confirm("Potentially sensitive files are present:\n\n" + string.Join("\n", files) + "\n\nContinue only if they are ignored or intentionally staged.");
    }
    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator).Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
    }

    private void EnableImmersiveDarkTitleBar()
    {
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private bool Confirm(string message) => MessageBox.Show(this, message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private void Show(OperationResult result) { if (!result.Success) ShowError(result.Message); else _viewModel.AppendLog(result.Message); }
    private void ShowError(string message) => MessageBox.Show(this, message, "GitHub Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
    private void ShowInfo(string message) => MessageBox.Show(this, message, "GitHub Account Manager", MessageBoxButton.OK, MessageBoxImage.Information);
}
