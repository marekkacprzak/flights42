namespace AiCsServer.Dashboard;

public static class DashboardRequestAmbient
{
    private static readonly AsyncLocal<string?> CatalogId = new();
    private static readonly AsyncLocal<string?> RequestHash = new();

    public static string? CurrentCatalogId
    {
        get => CatalogId.Value;
        set => CatalogId.Value = value;
    }

    public static string? CurrentRequestHash
    {
        get => RequestHash.Value;
        set => RequestHash.Value = value;
    }
}
