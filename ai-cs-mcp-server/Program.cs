using AiCsMcpServer;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.WebHost.UseUrls("http://127.0.0.1:3002");

builder.Services.AddCors(options =>
{
    options.AddPolicy("McpBrowserClient", policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .WithHeaders(
                "Accept",
                "Authorization",
                "Content-Type",
                "Last-Event-ID",
                "mcp-protocol-version",
                "mcp-session-id")
            .WithExposedHeaders("mcp-session-id");
    });
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "Flights42 Hotels MCP Server",
            Version = "1.0.0",
        };
        options.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Resources = new ResourcesCapability(),
        };
    })
    .WithHttpTransport()
    .WithTools<HotelsTools>()
    .WithResources<HotelsResources>()
    .WithMcpApps();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Use(async (context, next) =>
{
    string? origin = context.Request.Headers.Origin.FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(origin))
    {
        context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        context.Response.Headers.Append("Vary", "Origin");
    }
    else
    {
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
    }

    context.Response.Headers["Access-Control-Allow-Headers"] =
        "Accept, Authorization, Content-Type, Last-Event-ID, mcp-protocol-version, mcp-session-id";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
    context.Response.Headers["Access-Control-Expose-Headers"] = "mcp-session-id";

    if (string.Equals(
            context.Request.Headers["Access-Control-Request-Private-Network"].FirstOrDefault(),
            "true",
            StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    }

    if (HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    await next();
});

app.UseCors("McpBrowserClient");
app.UseStaticFiles();

app.MapMcp("/mcp").RequireCors("McpBrowserClient");

Console.WriteLine("Flights42 Hotels MCP server listening on http://127.0.0.1:3002/mcp");
Console.WriteLine("Transport: Streamable HTTP (MapMcp). Not WebSockets. Middleware expects HTTP /mcp.");

app.Run();
