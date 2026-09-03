# Security policy

## Reporting a vulnerability

Please do not publish credentials, private keys, tokens, repository URLs, or diagnostic logs in a public issue. Report security concerns privately to the maintainer through GitHub's private vulnerability reporting feature.

## Data handling

- Account profiles are stored in `%APPDATA%\GitHubAccountManager\settings.json`.
- GitHub tokens entered in the UI are kept in memory for one operation and are not persisted or logged.
- Private key contents and passphrases are never read into application logs.
- SSH configuration files are backed up before changes.
- Repository account-switch snapshots are stored under the repository Git metadata and are retention-limited.

Always use a fine-grained GitHub token with the minimum repository-administration permission required, and revoke it when it is no longer needed.
