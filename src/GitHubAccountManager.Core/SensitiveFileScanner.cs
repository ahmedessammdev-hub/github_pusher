using System.Text.RegularExpressions;

namespace GitHubAccountManager.Core;

public static partial class SensitiveFileScanner
{
    private static readonly string[] SensitiveNames =
    [
        ".env", ".env.local", "id_rsa", "id_ed25519", "credentials.json", "service-account.json"
    ];

    [GeneratedRegex(@"(?i)(^|/)(\.env(?:\..+)?|id_(?:rsa|ed25519)|credentials\.json|service-account\.json)$|\.(?:pem|p12|pfx|key)$")]
    private static partial Regex SensitivePattern();

    public static IReadOnlyList<string> FindSuspiciousPaths(IEnumerable<GitFileChange> changes) =>
        changes.Select(change => change.Path.Replace('\\', '/'))
            .Where(path => SensitiveNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase) || SensitivePattern().IsMatch(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
