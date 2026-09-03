using System.Text.Json;

namespace GitHubAccountManager.Core;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitHubAccountManager");
    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) return new UserSettings();
        await using var stream = File.OpenRead(SettingsPath);
        var settings = await JsonSerializer.DeserializeAsync<UserSettings>(stream, JsonOptions, cancellationToken);
        return settings ?? new UserSettings();
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var temporary = SettingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        File.Move(temporary, SettingsPath, true);
    }

    public static void Validate(UserSettings settings)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in settings.Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.DisplayName) || string.IsNullOrWhiteSpace(account.GitUserName) ||
                string.IsNullOrWhiteSpace(account.GitEmail) || string.IsNullOrWhiteSpace(account.GitHubUser) ||
                string.IsNullOrWhiteSpace(account.HostName) || string.IsNullOrWhiteSpace(account.SshAlias) ||
                string.IsNullOrWhiteSpace(account.PrivateKeyPath))
                throw new InvalidDataException("Every account must have a name, identity, host, alias, and key path.");
            if (!ids.Add(account.Id)) throw new InvalidDataException($"Duplicate account ID: {account.Id}");
            if (!aliases.Add(account.SshAlias)) throw new InvalidDataException($"Duplicate SSH alias: {account.SshAlias}");
        }
    }
}
