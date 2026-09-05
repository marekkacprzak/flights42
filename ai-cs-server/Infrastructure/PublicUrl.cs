namespace AiCsServer.Infrastructure;

public static class PublicUrl
{
    private static string _baseUrl = "http://localhost:3001";

    public static void Configure(IConfiguration configuration)
    {
        string? configured = configuration["AI_SERVER_PUBLIC_URL"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable("AI_SERVER_PUBLIC_URL");
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            _baseUrl = configured.TrimEnd('/');
            Environment.SetEnvironmentVariable("AI_SERVER_PUBLIC_URL", _baseUrl);
        }
    }

    public static string Base => _baseUrl;

    public static string Combine(string relativePath)
    {
        string path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        return _baseUrl + path;
    }
}
