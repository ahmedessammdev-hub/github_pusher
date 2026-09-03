using System.Diagnostics;
using System.Text;

namespace GitHubAccountManager.Core;

public interface IProcessRunner
{
    Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments, string? workingDirectory = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default, string? standardInput = null);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments,
        string? workingDirectory = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default,
        string? standardInput = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput is not null,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) return new(-1, "", $"Could not start {executable}.");
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput);
                process.StandardInput.Close();
            }
        }
        catch (Exception exception)
        {
            return new(-1, "", exception.Message);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            var reason = cancellationToken.IsCancellationRequested ? "Operation cancelled." : "Operation timed out.";
            return new(-2, await outputTask, reason + Environment.NewLine + await errorTask);
        }
        return new(process.ExitCode, await outputTask, await errorTask);
    }
}
