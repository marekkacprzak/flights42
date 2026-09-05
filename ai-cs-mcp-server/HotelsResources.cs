using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace AiCsMcpServer;

[McpServerResourceType]
public sealed class HotelsResources
{
    private static readonly string HtmlPath = Path.Combine(
        AppContext.BaseDirectory,
        "wwwroot",
        "ui",
        "results.html");

    [McpServerResource(
        UriTemplate = HotelsTools.ResourceUri,
        Name = "Flights42 Hotel Results",
        MimeType = McpApps.HtmlMimeType)]
    [McpMeta("ui", """{"csp":{"resourceDomains":["http://127.0.0.1:3002"]}}""")]
    [Description("Hotel results rendered as an interactive MCP App.")]
    public static string GetHotelsResultsUi()
    {
        if (!File.Exists(HtmlPath))
        {
            string fallback = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "mcp-server",
                "dist",
                "index.html"));
            if (File.Exists(fallback))
            {
                return File.ReadAllText(fallback);
            }

            throw new FileNotFoundException(
                $"MCP App HTML not found at {HtmlPath} or {fallback}.");
        }

        return File.ReadAllText(HtmlPath);
    }
}
