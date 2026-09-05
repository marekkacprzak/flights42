using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AiCsMcpServer;

[McpServerToolType]
public sealed class HotelsTools
{
    public const string ResourceUri = "ui://hotels/results.html";
    public const string AssetBaseUrl = "http://127.0.0.1:3002/assets/hotels";

    private static readonly (string Id, string Name, int Stars, string ImageUrl)[] BaseHotels =
    [
        ("biz-hotel", "Biz Hotel", 3, $"{AssetBaseUrl}/biz-hotel.svg"),
        ("skyline-suites", "Skyline Suites", 4, $"{AssetBaseUrl}/skyline-suites.svg"),
        ("grand-palace", "Grand Palace", 5, $"{AssetBaseUrl}/grand-palace.svg"),
    ];

    [McpServerTool(Name = "findHotels")]
    [McpAppUi(ResourceUri = ResourceUri)]
    [Description("Find three demo hotels for a city. Use this when the user asks for hotels in a specific city.")]
    public static CallToolResult FindHotels(
        [Description("The city to search hotels for.")] string city)
    {
        string trimmed = (city ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("city is required.", nameof(city));
        }

        List<object> hotels = BaseHotels
            .Select(hotel => (object)new
            {
                id = hotel.Id,
                name = $"{hotel.Name} {trimmed}",
                stars = hotel.Stars,
                imageUrl = hotel.ImageUrl,
            })
            .ToList();

        for (int i = hotels.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (hotels[i], hotels[j]) = (hotels[j], hotels[i]);
        }

        var result = new
        {
            city = trimmed,
            hotels,
        };

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = $"{hotels.Count} hotels found",
                },
            ],
            StructuredContent = JsonSerializer.SerializeToElement(result),
        };
    }
}
