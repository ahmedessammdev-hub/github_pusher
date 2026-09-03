using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GitHubAccountManager.Core;

public sealed class GitHubApiService(HttpClient client)
{
    public async Task<OperationResult> VerifyTokenAsync(string host, string token, string expectedUser,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(host, token, HttpMethod.Get, "/user");
        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) return OperationResult.Fail($"GitHub authentication failed: {(int)response.StatusCode}");
        using var json = JsonDocument.Parse(content);
        var login = json.RootElement.GetProperty("login").GetString() ?? "";
        return login.Equals(expectedUser, StringComparison.OrdinalIgnoreCase)
            ? OperationResult.Ok($"Authenticated as {login}.")
            : OperationResult.Fail($"The token belongs to '{login}', not '{expectedUser}'.");
    }

    public async Task<OperationResult> CreateRepositoryAsync(string host, string token, GitHubRepositoryRequest model,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(host, token, HttpMethod.Post, "/user/repos");
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            name = model.Name,
            description = model.Description,
            @private = model.IsPrivate,
            auto_init = false
        }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) return OperationResult.Fail($"Repository creation failed: {(int)response.StatusCode}\n{content}");
        using var json = JsonDocument.Parse(content);
        return OperationResult.Ok(json.RootElement.GetProperty("full_name").GetString() ?? model.Name);
    }

    public async Task<OperationResult> UploadSshKeyAsync(string host, string token, string title, string publicKey,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(host, token, HttpMethod.Post, "/user/keys");
        request.Content = new StringContent(JsonSerializer.Serialize(new { title, key = publicKey.Trim() }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return OperationResult.Fail($"SSH key upload failed: {(int)response.StatusCode}\n{content}");
        return OperationResult.Ok("SSH public key uploaded to the verified account.");
    }

    private static HttpRequestMessage CreateRequest(string host, string token, HttpMethod method, string path)
    {
        var baseUrl = host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? "https://api.github.com"
            : $"https://{host}/api/v3";
        var request = new HttpRequestMessage(method, baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("GitHubAccountManager/0.1.2");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }
}
