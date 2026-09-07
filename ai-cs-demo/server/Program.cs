using System.ClientModel;
using AiCsDemo.Server;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddAGUIServer();

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
    model = "qwen/qwen3-vl-8b";
}

const string AgentName = "weatherAgent";
const string Instructions =
    "You are a friendly weather assistant.\n\n" +
    "When the user asks about the weather in a city,\n" +
    "look it up. Then answer in one short, natural sentence that mentions the\n" +
    "condition and the temperature in degrees Celsius.";

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
    .AsIChatClient();

builder
    .AddAIAgent(AgentName, (services, name) => chatClient.AsAIAgent(
        instructions: Instructions,
        name: name,
        description: "Weather Assistant",
        tools:
        [
            AIFunctionFactory.Create(
                WeatherTool.GetWeather,
                name: "getWeather",
                description: "Returns the current weather and temperature for a given city.")
        ],
        services: services))
    .WithInMemorySessionStore(withIsolation: false);

var app = builder.Build();

app.UseCors();
app.MapAGUIServer(AgentName, "/chat");

app.Run();
