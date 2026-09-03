# Contributing

1. Create a focused branch.
2. Build with `dotnet build GitHubAccountManager.sln -c Release`.
3. Run `dotnet test GitHubAccountManager.sln -c Release`.
4. Avoid adding account details, tokens, private keys, generated settings, or build output.
5. Keep process execution argument-based; never construct shell command strings from user input.
6. Add tests for remote parsing, destructive operations, and rollback behavior.

Pull requests should explain user-visible behavior, safety implications, and manual verification performed.
