using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiCsServer.Dashboard;

namespace AiCsServer.Data;

public static class DashboardCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string ComputeRequestHash(IEnumerable<JsonElement> messages)
    {
        List<string> userTexts = new();
        foreach (JsonElement message in messages)
        {
            if (!message.TryGetProperty("role", out JsonElement roleEl))
            {
                continue;
            }

            if (!string.Equals(roleEl.GetString(), "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!message.TryGetProperty("content", out JsonElement content))
            {
                continue;
            }

            string text = ExtractText(content);
            if (text.Length > 0)
            {
                userTexts.Add(text);
            }
        }

        string joined = string.Join("\n---\n", userTexts);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<DashboardSpec?> ReadAsync(string hash)
    {
        IReadOnlyList<object[]> rows = await LibSqlDatabase.QueryAsync(
            "SELECT spec_json FROM dashboard_cache WHERE hash = ?", hash).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return null;
        }

        string json = Convert.ToString(rows[0][0]) ?? "";
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("spec", out JsonElement specEl))
            {
                return JsonSerializer.Deserialize<DashboardSpec>(specEl.GetRawText(), JsonOptions);
            }

            return JsonSerializer.Deserialize<DashboardSpec>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static async Task WriteAsync(string hash, DashboardSpec spec)
    {
        string payload = JsonSerializer.Serialize(new { spec }, JsonOptions);
        await LibSqlDatabase.ExecuteAsync(
            """
            INSERT INTO dashboard_cache (hash, spec_json)
            VALUES (?, ?)
            ON CONFLICT(hash) DO UPDATE SET spec_json = excluded.spec_json
            """,
            hash,
            payload).ConfigureAwait(false);
    }

    private static string ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? "";
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            StringBuilder builder = new();
            foreach (JsonElement part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object &&
                    part.TryGetProperty("text", out JsonElement text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }

            return builder.ToString();
        }

        return "";
    }
}
