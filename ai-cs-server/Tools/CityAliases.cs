namespace AiCsServer.Tools;

public static class CityAliases
{
    private sealed record CityAliasGroup(string Canonical, IReadOnlyList<string> Aliases);

    private static readonly IReadOnlyList<CityAliasGroup> Groups =
    [
        new("Wien", ["Vienna"]),
        new("Rome", ["Rom"]),
        new("Munich", ["München", "Muenchen"]),
        new("Prague", ["Prag"]),
        new("Cologne", ["Köln", "Koeln"]),
        new("Florence", ["Florenz"]),
        new("Venice", ["Venedig"]),
        new("Milan", ["Mailand"]),
        new("Naples", ["Neapel"]),
        new("Geneva", ["Genf"]),
        new("Lisbon", ["Lissabon"]),
        new("Warsaw", ["Warschau"]),
        new("Copenhagen", ["Kopenhagen"]),
        new("Athens", ["Athen"]),
        new("Brussels", ["Brüssel", "Bruessel"]),
    ];

    private static CityAliasGroup? FindGroup(string city)
    {
        string lower = city.Trim().ToLowerInvariant();
        return Groups.FirstOrDefault(group =>
            group.Canonical.Equals(lower, StringComparison.OrdinalIgnoreCase) ||
            group.Aliases.Any(alias => alias.Equals(lower, StringComparison.OrdinalIgnoreCase)));
    }

    public static IReadOnlyList<string> CityCandidates(string city)
    {
        string trimmed = city.Trim();
        string lower = trimmed.ToLowerInvariant();
        CityAliasGroup? group = FindGroup(trimmed);
        if (group is null)
        {
            return [trimmed];
        }

        List<string> ordered = [group.Canonical, .. group.Aliases];
        return [trimmed, .. ordered.Where(name => !name.Equals(lower, StringComparison.OrdinalIgnoreCase))];
    }

    public static string CanonicalCity(string city)
    {
        string trimmed = city.Trim();
        return FindGroup(trimmed)?.Canonical ?? trimmed;
    }
}
