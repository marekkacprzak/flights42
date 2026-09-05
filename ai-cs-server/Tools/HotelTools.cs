using System.ComponentModel;
using AiCsServer.Infrastructure;

namespace AiCsServer.Tools;

public static class HotelTools
{
    private const string HotelAssetBaseUrl = "/assets/hotels";
    private static string PublicUrl => AiCsServer.Infrastructure.PublicUrl.Base;

    public sealed record FindHotel(string Id, string Name, int Stars, string ImageUrl, string City);

    public sealed record FindHotelsResult(string City, IReadOnlyList<FindHotel> Hotels);

    public sealed record SearchHotel(string Id, string Name, int Stars, int PricePerNight, string Currency, string ImageUrl);

    public sealed record SearchHotelsResult(string City, IReadOnlyList<SearchHotel> Hotels);

    private sealed record HotelTemplate(string Name, int Stars, int BasePrice, string ImageUrl);

    private static readonly (string Id, string Name, int Stars, string ImageUrl)[] BaseHotels =
    [
        ("budget-hotel", "Budget Hotel", 3, $"{HotelAssetBaseUrl}/biz-hotel.svg"),
        ("biz-hotel", "Biz Hotel", 4, $"{HotelAssetBaseUrl}/skyline-suites.svg"),
        ("grand-palace", "Grand Palace", 5, $"{HotelAssetBaseUrl}/grand-palace.svg"),
    ];

    private static readonly Dictionary<string, HotelTemplate[]> HotelsByCity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hamburg"] =
        [
            new("Hafenpark Hamburg", 4, 159, $"{PublicUrl}/images/hotels/hotel-lobby.webp"),
            new("Hotel Atlantic Kempinski", 5, 289, $"{PublicUrl}/images/hotels/hotel-luxury.webp"),
            new("Motel One Alster", 3, 89, $"{PublicUrl}/images/hotels/hotel-exterior.webp"),
        ],
        ["london"] =
        [
            new("The Savoy", 5, 449, $"{PublicUrl}/images/hotels/hotel-interior.webp"),
            new("The Hoxton Shoreditch", 4, 219, $"{PublicUrl}/images/hotels/hotel-lobby.webp"),
            new("Premier Inn County Hall", 3, 119, $"{PublicUrl}/images/hotels/hotel-exterior.webp"),
        ],
        ["graz"] =
        [
            new("Schloss Eggenberg Boutique", 4, 149, $"{PublicUrl}/images/hotels/hotel-exterior.webp"),
            new("Hotel Wiesler", 4, 179, $"{PublicUrl}/images/hotels/hotel-lobby.webp"),
            new("B&B Hotel Graz", 3, 79, $"{PublicUrl}/images/hotels/hotel-interior.webp"),
        ],
    };

    private static readonly HotelTemplate[] FallbackHotels =
    [
        new("Grand Plaza Hotel", 5, 259, $"{PublicUrl}/images/hotels/hotel-lobby.webp"),
        new("Riverside Boutique", 4, 169, $"{PublicUrl}/images/hotels/hotel-interior.webp"),
        new("City Inn Express", 3, 89, $"{PublicUrl}/images/hotels/hotel-exterior.webp"),
    ];

    public static IReadOnlyList<FindHotel> FindHotelsForCity(string city)
    {
        string canonical = CityAliases.CanonicalCity(city);
        return BaseHotels.Select(hotel => new FindHotel(
            hotel.Id,
            $"{hotel.Name} {canonical}",
            hotel.Stars,
            hotel.ImageUrl,
            canonical)).ToArray();
    }

    [Description("Returns three hotel options for the given city. Each hotel has a different star rating (3, 4 and 5 stars).")]
    public static async Task<FindHotelsResult> FindHotels(
        [Description("The city to search hotels for.")] string city)
    {
        if (FeatureFlags.UseMcp)
        {
            try
            {
                HotelsMcpClient.McpFindHotelsResult? mcp = await HotelsMcpClient.FindHotelsAsync(city).ConfigureAwait(false);
                if (mcp is not null)
                {
                    string canonical = CityAliases.CanonicalCity(mcp.City);
                    FindHotel[] hotels = mcp.Hotels.Select(hotel => new FindHotel(
                        hotel.Id,
                        hotel.Name,
                        hotel.Stars,
                        hotel.ImageUrl,
                        canonical)).ToArray();
                    return new FindHotelsResult(mcp.City, hotels);
                }
            }
            catch
            {
            }
        }

        return new FindHotelsResult(city, FindHotelsForCity(city));
    }

    private static int HashString(string value)
    {
        int hash = 0;
        foreach (char ch in value)
        {
            hash = (hash * 31 + ch);
        }

        return Math.Abs(hash);
    }

    private static HotelTemplate[] Rotate(HotelTemplate[] items, int offset)
    {
        if (items.Length == 0)
        {
            return items;
        }

        int start = offset % items.Length;
        return [.. items.Skip(start), .. items.Take(start)];
    }

    public static SearchHotelsResult SearchHotelsCore(string city)
    {
        string key = city.Trim().ToLowerInvariant();
        int seed = HashString(key);
        HotelTemplate[] list = HotelsByCity.TryGetValue(key, out HotelTemplate[]? known)
            ? known
            : Rotate(FallbackHotels, seed);

        SearchHotel[] hotels = list.Select((hotel, index) => new SearchHotel(
            $"hotel-{index + 1}",
            hotel.Name,
            hotel.Stars,
            hotel.BasePrice + ((seed + index * 11) % 25),
            "EUR",
            hotel.ImageUrl)).ToArray();

        return new SearchHotelsResult(city, hotels);
    }

    [Description("""
        Returns a deterministic mocked list of three hotels for a city.
        Use it to populate the "Hotels" tile of the dashboard.
        Output: { city, hotels: { id, name, stars, pricePerNight, currency, imageUrl }[] }.
        The list is stable per city, so re-rendering the same dashboard does not change it.
        Date inputs are accepted but ignored — pricing is independent of stay length in this mock.
        """)]
    public static SearchHotelsResult SearchHotels(
        [Description("City name, e.g. \"Hamburg\".")] string city,
        [Description("Optional ISO date. Currently ignored by the mock.")] string? checkIn = null,
        [Description("Optional ISO date. Currently ignored by the mock.")] string? checkOut = null)
    {
        _ = checkIn;
        _ = checkOut;
        return SearchHotelsCore(city);
    }
}
