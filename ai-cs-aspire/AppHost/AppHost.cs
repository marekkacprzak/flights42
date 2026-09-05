var builder = DistributedApplication.CreateBuilder(args);

var mcp = builder.AddProject<Projects.AiCsMcpServer>("ai-cs-mcp-server", launchProfileName: null)
    .WithHttpEndpoint(port: 3002, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development");

var server = builder.AddProject<Projects.AiCsServer>("ai-cs-server", launchProfileName: null)
    .WithHttpEndpoint(port: 3011, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("AI_SERVER_PUBLIC_URL", "http://localhost:3001")
    .WithEnvironment("BFF_EXECUTE_JS_URL", "http://127.0.0.1:3001/internal/execute-javascript")
    .WithEnvironment("Mcp__HotelsUrl", "http://127.0.0.1:3002/mcp")
    .WithEnvironment("MCP_HOTELS_URL", "http://127.0.0.1:3002/mcp")
    .WaitFor(mcp);

builder.AddJavaScriptApp("ai-cs-bff", "../..", "ai-cs-bff")
    .WithPnpm(install: false)
    .WithHttpEndpoint(port: 3001, env: "PORT", isProxied: false)
    .WithEnvironment("DOTNET_AGUI_URL", "http://127.0.0.1:3011")
    .WithEnvironment("MCP_URL", "http://127.0.0.1:3002/mcp")
    .WithExternalHttpEndpoints()
    .WaitFor(server)
    .WaitFor(mcp);

builder.Build().Run();
