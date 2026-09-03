[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet restore GitHubAccountManager.sln
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet test tests\GitHubAccountManager.Tests\GitHubAccountManager.Tests.csproj -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet publish src\GitHubAccountManager.App\GitHubAccountManager.App.csproj -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $exe = Join-Path $root 'dist\GitHubAccountManager.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Expected executable was not produced: $exe" }
    $hash = Get-FileHash -LiteralPath $exe -Algorithm SHA256
    "{0}  GitHubAccountManager.exe" -f $hash.Hash.ToLowerInvariant() | Set-Content -LiteralPath (Join-Path $root 'dist\SHA256SUMS.txt') -Encoding ASCII
    Write-Host "Built: $exe" -ForegroundColor Green
} finally {
    Pop-Location
}
