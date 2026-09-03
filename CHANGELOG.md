# Changelog

All notable changes are documented here. This project follows Semantic Versioning.

## 0.1.2 - 2026-09-03

### Added

- Browser-based GitHub sign-in, authentication status, refresh, and sign-out through Git Credential Manager.
- Automatic reuse of the securely stored credential for repository creation and SSH public-key upload when no token is entered.
- Tests for authentication status and secure credential retrieval.
- Added one-click **Create remote & push** with Git initialization, secret checks, initial commit support, HTTPS/SSH remote selection, and upstream push.

## 0.1.0 - 2026-09-03

### Added

- Single-file Windows WPF application with a dark dashboard.
- Repository discovery, initialization, status, staging, commits, fetch, fast-forward pull, push, dry-run push, and stash.
- Branch creation/switching and commit history.
- Multi-account local identity and SSH switching with access verification, backups, and rollback.
- SSH alias management, key generation, agent loading, and SSH commit signing.
- GitHub and GitHub Enterprise repository creation using an in-memory fine-grained token.
- Sensitive-file warnings, diagnostics, and xUnit unit/integration tests.
