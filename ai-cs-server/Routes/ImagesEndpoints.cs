using System.Text.RegularExpressions;

namespace AiCsServer.Routes;

public static class ImagesEndpoints
{
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "cars",
        "hotels",
    };

    private static readonly Regex AllowedFilename = new(
        @"^[a-z0-9][a-z0-9-]*\.(?:webp|jpg|jpeg|png)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["webp"] = "image/webp",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
    };

    public static void MapImages(this WebApplication app, IConfiguration configuration)
    {
        string? configured = configuration["Images:Root"];
        string imagesRoot = string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine(
                app.Environment.ContentRootPath,
                "..",
                "ai-server",
                "src",
                "mastra",
                "public",
                "images"))
            : Path.GetFullPath(configured);

        app.MapGet("/images/{category}/{filename}", async (string category, string filename, CancellationToken cancellationToken) =>
        {
            if (!AllowedCategories.Contains(category) || !AllowedFilename.IsMatch(filename))
            {
                return Results.Text("image not found", statusCode: StatusCodes.Status404NotFound);
            }

            string ext = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
            if (!ContentTypes.TryGetValue(ext, out string? contentType))
            {
                return Results.Text("image not found", statusCode: StatusCodes.Status404NotFound);
            }

            string fullPath = Path.GetFullPath(Path.Combine(imagesRoot, category, filename));
            if (!fullPath.StartsWith(imagesRoot, StringComparison.Ordinal) || !File.Exists(fullPath))
            {
                return Results.Text("image not found", statusCode: StatusCodes.Status404NotFound);
            }

            byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return Results.File(bytes, contentType);
        });
    }
}
