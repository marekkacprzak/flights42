namespace AiCsServer.Infrastructure;

public static class FeatureFlags
{
    public static bool UseMcp { get; private set; }

    public static bool UseMcpApps { get; private set; } = true;

    public static bool UseApproval { get; private set; } = true;

    public static bool UseActionCards { get; private set; } = true;

    public static string McpHotelsUrl { get; private set; } = "http://127.0.0.1:3002/mcp";

    public static void Configure(IConfiguration configuration)
    {
        UseMcp = configuration.GetValue("USE_MCP", false)
            || configuration.GetValue("UseMcp", false);

        bool? useMcpApps = configuration.GetValue<bool?>("USE_MCP_APPS")
            ?? configuration.GetValue<bool?>("UseMcpApps");
        string? useMcpAppsEnv = Environment.GetEnvironmentVariable("USE_MCP_APPS");
        if (bool.TryParse(useMcpAppsEnv, out bool envFlag))
        {
            useMcpApps = envFlag;
        }
        UseMcpApps = useMcpApps ?? true;

        bool? useApproval = configuration.GetValue<bool?>("USE_APPROVAL")
            ?? configuration.GetValue<bool?>("UseApproval");
        string? useApprovalEnv = Environment.GetEnvironmentVariable("USE_APPROVAL");
        if (bool.TryParse(useApprovalEnv, out bool approvalEnvFlag))
        {
            useApproval = approvalEnvFlag;
        }
        UseApproval = useApproval ?? true;

        bool? useActionCards = configuration.GetValue<bool?>("USE_ACTION_CARDS")
            ?? configuration.GetValue<bool?>("UseActionCards");
        string? useActionCardsEnv = Environment.GetEnvironmentVariable("USE_ACTION_CARDS");
        if (bool.TryParse(useActionCardsEnv, out bool actionCardsEnvFlag))
        {
            useActionCards = actionCardsEnvFlag;
        }
        UseActionCards = useActionCards ?? true;

        string? url = configuration["Mcp:HotelsUrl"]
            ?? configuration["MCP_HOTELS_URL"]
            ?? Environment.GetEnvironmentVariable("MCP_HOTELS_URL");
        if (!string.IsNullOrWhiteSpace(url))
        {
            McpHotelsUrl = url.Trim();
        }
    }
}
