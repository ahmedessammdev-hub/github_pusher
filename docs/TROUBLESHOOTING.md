# Troubleshooting

## The selected folder is not a repository

Use **Initialize Git** or browse to a folder that contains a working tree. Bare repositories are not supported by the GUI.

## SSH authenticates as the wrong account

Save all account profiles, run **Setup SSH**, then **Test SSH**. If the key has a passphrase, start the Windows OpenSSH Authentication Agent and use **Add to agent**.

## Push fails after SSH succeeds

SSH authentication confirms the account, not repository write permission. Run **Push dry-run** and verify that the selected account has write access and that branch protection allows the operation.

## Pull is rejected

The application intentionally uses `git pull --ff-only`. Resolve divergent history explicitly in a terminal through merge or rebase rather than allowing an unexpected automatic merge.

## Antivirus warning

Use release artifacts produced by the repository workflow and compare the SHA-256 hash. Unsigned self-contained executables can trigger reputation warnings; code signing is planned before a stable release.
