using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AiCsServer.Tools;

public static class ExecuteJavaScriptTools
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string _url = "http://127.0.0.1:3001/internal/execute-javascript";

    private static HttpClient CreateClient()
    {
        SocketsHttpHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders =
            {
                Accept = { new MediaTypeWithQualityHeaderValue("application/json") },
            },
        };
    }

    public static void Configure(IConfiguration configuration)
    {
        string? fromEnv = Environment.GetEnvironmentVariable("BFF_EXECUTE_JS_URL");
        string? fromConfig = configuration["Bff:ExecuteJavaScriptUrl"]
            ?? configuration["BFF_EXECUTE_JS_URL"];
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            _url = fromEnv.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            _url = fromConfig.Trim();
        }
    }

    [Description("""
        Runs a snippet of JavaScript inside a hardened QuickJS sandbox to aggregate flight data into
        chart-ready { name, value } pairs.

        The snippet is executed AS AN ES MODULE, so top-level await is supported. There is NO
        wrapping function — write straight-line statements; do NOT use return.

        TWO host functions are exposed:

          await loadFlights(from: string, to: string): Promise<Flight[]>
          submitResult(items: { name: string, value: number }[]): void

        where from/to are city names with the first letter uppercase (e.g. "Graz", "Hamburg")
        and each Flight has the shape { id: number, from: string, to: string, date: string,
        delay: number } (date is ISO, delay is minutes).

        Workflow inside the snippet:
          1. Call await loadFlights(from, to) once for every connection the request needs.
          2. Aggregate the loaded arrays into the chart-ready { name, value }[] shape.
          3. Call submitResult(items) EXACTLY ONCE with that array.

        Returns { data, code, title }; forward data and title to renderChart.
        """)]
    public static async Task<object> ExecuteJavaScript(
        [Description("Module body. Use await loadFlights(from, to) then submitResult(items).")] string code,
        [Description("Human-readable chart title.")] string title)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, _url);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { code, title }, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new
                {
                    ok = false,
                    data = Array.Empty<object>(),
                    title,
                    code = "",
                    message = $"BFF execute-javascript failed: {(int)response.StatusCode} {body}",
                };
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return new
                {
                    ok = false,
                    data = Array.Empty<object>(),
                    title,
                    code = "",
                    message = "BFF execute-javascript returned empty body",
                };
            }

            return JsonSerializer.Deserialize<object>(body, JsonOptions)
                ?? new
                {
                    ok = false,
                    data = Array.Empty<object>(),
                    title,
                    code = "",
                    message = "BFF execute-javascript returned null JSON",
                };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                data = Array.Empty<object>(),
                title,
                code = "",
                message = ex.Message,
            };
        }
    }
}
