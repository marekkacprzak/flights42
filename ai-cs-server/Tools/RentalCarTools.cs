using System.ComponentModel;

namespace AiCsServer.Tools;

public static class RentalCarTools
{
    private static string PublicUrl => AiCsServer.Infrastructure.PublicUrl.Base;

    private sealed record CarModel(string Name, string Image);

    private sealed record CarTemplate(string Category, int BasePrice, IReadOnlyList<CarModel> Models);

    public sealed record RentalCar(string Id, string Category, string Model, int PricePerDay, string Currency, string ImageUrl);

    public sealed record RentalCarSearchResult(string City, IReadOnlyList<RentalCar> Cars);

    private static readonly CarTemplate[] Templates =
    [
        new("Compact", 39,
        [
            new("VW Polo", "vw-polo.webp"),
            new("Opel Corsa", "opel-corsa.webp"),
            new("Renault Clio", "renault-clio.webp"),
        ]),
        new("Estate", 69,
        [
            new("Skoda Octavia", "skoda-octavia.webp"),
            new("VW Passat Variant", "vw-passat-variant.webp"),
            new("Ford Mondeo Turnier", "ford-mondeo.webp"),
        ]),
        new("Premium", 119,
        [
            new("BMW 5", "bmw-5.webp"),
            new("Mercedes E-Class", "mercedes-e-class.webp"),
            new("Audi A6", "audi-a6.webp"),
        ]),
    ];

    private static int HashString(string value)
    {
        int hash = 0;
        foreach (char ch in value)
        {
            hash = (hash * 31 + ch);
        }

        return Math.Abs(hash);
    }

    public static RentalCarSearchResult SearchRentalCarsCore(string city)
    {
        int seed = HashString(city.ToLowerInvariant());
        RentalCar[] cars = Templates.Select((template, index) =>
        {
            CarModel model = template.Models[(seed + index) % template.Models.Count];
            return new RentalCar(
                $"car-{index + 1}",
                template.Category,
                model.Name,
                template.BasePrice + ((seed + index * 7) % 20),
                "EUR",
                $"{PublicUrl}/images/cars/{model.Image}");
        }).ToArray();

        return new RentalCarSearchResult(city, cars);
    }

    [Description("""
        Returns a deterministic mocked list of three rental cars available in a city.
        Use it to populate the "Rent a car" tile of the dashboard.
        Output: { city, cars: { id, category, model, pricePerDay, currency, imageUrl }[] }.
        The list is stable per city, so re-rendering the same dashboard does not change it.
        """)]
    public static RentalCarSearchResult SearchRentalCars(
        [Description("City name, e.g. \"Hamburg\".")] string city)
    {
        return SearchRentalCarsCore(city);
    }
}
