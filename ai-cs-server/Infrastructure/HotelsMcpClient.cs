using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AiCsServer.Infrastructure;

public static class HotelsMcpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public sealed record McpHotel(string Id, string Name, int Stars, string ImageUrl);

    public sealed record McpFindHotelsResult(string City, IReadOnlyList<McpHotel> Hotels);

    public static async Task<McpFindHotelsResult?> FindHotelsAsync(
        string city,
        CancellationToken cancellationToken = default)
    {
        await using McpClient client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(FeatureFlags.McpHotelsUrl),
                Name = "ai-cs-server-hotels",
                TransportMode = HttpTransportMode.StreamableHttp,
            }),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        CallToolResult result = await client.CallToolAsync(
            "findHotels",
            new Dictionary<string, object?> { ["city"] = city },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.StructuredContent is JsonElement structured
            && structured.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            return JsonSerializer.Deserialize<McpFindHotelsResult>(structured.GetRawText(), JsonOptions);
        }

        string text = string.Join(
            "\n",
            result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<McpFindHotelsResult>(text, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
