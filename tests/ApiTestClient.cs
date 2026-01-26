using System.Net.Http.Json;
using System.Text.Json;

namespace Planara.Auth.Tests;

public static class ApiTestClient
{
    public static async Task<JsonDocument> PostAsync(
        this HttpClient client,
        string query,
        object? variables = null,
        CancellationToken ct = default)
    {
        var payload = new
        {
            query,
            variables
        };

        var resp = await client.PostAsJsonAsync("/graphql", payload, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return json ?? throw new InvalidOperationException("Empty GraphQL response");
    }

    public static JsonElement? GetErrors(this JsonDocument doc)
        => doc.RootElement.TryGetProperty("errors", out var e) ? e : null;

    public static JsonElement GetData(this JsonDocument doc)
        => doc.RootElement.GetProperty("data");
}