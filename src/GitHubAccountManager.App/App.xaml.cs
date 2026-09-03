using System.Configuration;
using System.Data;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace GitHubAccountManager.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    public App() => DispatcherUnhandledException += OnUnhandledException;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\GitHubAccountManager", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("GitHub Account Manager is already running.", "GitHub Account Manager",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitHubAccountManager");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "crash.log"), $"[{DateTimeOffset.Now:O}] {e.Exception}\n\n");
        }
        catch { }
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}", "GitHub Account Manager",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        if (Current.MainWindow is null || !Current.MainWindow.IsVisible) Current.Shutdown(-1);
    }
}

