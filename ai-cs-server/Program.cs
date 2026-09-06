using System.ClientModel;
using System.Diagnostics;
using AiCsServer.Agents;
using AiCsServer.Data;
using AiCsServer.Infrastructure;
using AiCsServer.Tools;
using AiCsServer.Routes;
using AiCsServer.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

PublicUrl.Configure(builder.Configuration);
FeatureFlags.Configure(builder.Configuration);
ExecuteJavaScriptTools.Configure(builder.Configuration);
LibSqlDatabase.Initialize(builder.Configuration, builder.Environment);
BookedFlightsStore.EnsureLoaded();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddAGUIServer();
builder.Services.AddHttpClient();

var endpoint = builder.Configuration["LLM_ENDPOINT"];
if (string.IsNullOrWhiteSpace(endpoint))
{
    endpoint = "http://192.168.8.199:1234/v1";
}

var apiKey = builder.Configuration["OPENAI_API_KEY"];
if (string.IsNullOrWhiteSpace(apiKey))
{
    apiKey = "lm-studio";
}

var model = builder.Configuration["OPENAI_CHAT_MODEL"];
if (string.IsNullOrWhiteSpace(model))
{
    model = "qwen/qwen3.8-27b";
}

HttpClient httpClient = new(
    new StripLmStudioReasoningHandler(new HttpClientHandler()),
    disposeHandler: true)
{
    Timeout = Timeout.InfiniteTimeSpan,
};

IChatClient chatClient = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(httpClient),
        })
    .GetChatClient(model)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: AgentCatalog.OpenTelemetrySourceName)
    .Use(inner => new ApprovalInterruptChatClient(inner))
    .Build();

PackageTourWorkflow.ChatClient = chatClient;
AgentCatalog.RegisterAgents(builder, chatClient);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseCors();
app.Use(async (context, next) =>
{
    long started = Stopwatch.GetTimestamp();
    string path = context.Request.Path + context.Request.QueryString;
    Console.WriteLine($"[ai-cs-server] → {context.Request.Method} {path}");
    try
    {
        await next();
    }
    finally
    {
        double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Console.WriteLine(
            $"[ai-cs-server] ← {context.Request.Method} {path} {context.Response.StatusCode} {(int)ms}ms");
    }
});
app.UseMiddleware<AgentModeRemapMiddleware>();
app.UseDashboardCacheHit();
AgentCatalog.MapAgUiEndpoints(app);
app.MapBookings();
app.MapCharts();
app.MapImages(app.Configuration);
app.MapDashboardAgUi();

app.MapGet("/health", () => Results.Json(new
{
    ok = true,
    backend = LibSqlDatabase.Backend,
    dbPath = LibSqlDatabase.DbPath,
    useMcp = FeatureFlags.UseMcp,
    useMcpApps = FeatureFlags.UseMcpApps,
    useApproval = FeatureFlags.UseApproval,
    publicUrl = PublicUrl.Base,
}));

var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                   ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
Console.WriteLine($"ai-cs-server listening; backend={LibSqlDatabase.Backend}; db={LibSqlDatabase.DbPath}; UseMcp={FeatureFlags.UseMcp}; UseMcpApps={FeatureFlags.UseMcpApps}; UseApproval={FeatureFlags.UseApproval}; OTLP={otelEndpoint ?? "(unset)"}");

app.Run();
