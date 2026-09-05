using System.ComponentModel;
using System.Text.Json;

namespace AiCsServer.Tools;

public static class RenderA2uiTool
{
    public const string ToolName = "renderA2uiTool";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> SingleChildComponents = new(StringComparer.Ordinal)
    {
        "Card", "Button", "Modal",
    };

    private static readonly HashSet<string> MultiChildComponents = new(StringComparer.Ordinal)
    {
        "Row", "Column", "List",
    };

    [Description("""
        Render the final answer to the user as an A2UI surface.
        Input is { messages: A2uiMessage[] } for a single surface with exactly one createSurface,
        exactly one updateComponents (must define id "root"), and optional updateDataModel messages.
        MapResult emits ACTIVITY_SNAPSHOT activityType a2ui-surface with { operations: messages }.
        """)]
    public static JsonElement RenderA2ui(
        [Description("Ordered list of A2UI v0.9 messages for a single surface.")] JsonElement messages)
    {
        if (messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("renderA2uiTool: messages array must not be empty");
        }

        List<JsonElement> list = messages.EnumerateArray().ToList();
        List<JsonElement> createSurface = list.Where(m => m.TryGetProperty("createSurface", out _)).ToList();
        if (createSurface.Count != 1)
        {
            throw new InvalidOperationException(
                $"renderA2uiTool: expected exactly one createSurface message, got {createSurface.Count}");
        }

        List<JsonElement> updateComponents = list.Where(m => m.TryGetProperty("updateComponents", out _)).ToList();
        if (updateComponents.Count != 1)
        {
            throw new InvalidOperationException(
                $"renderA2uiTool: expected exactly one updateComponents message, got {updateComponents.Count}");
        }

        string surfaceId = createSurface[0].GetProperty("createSurface").GetProperty("surfaceId").GetString()
            ?? throw new InvalidOperationException("renderA2uiTool: createSurface.surfaceId missing");

        foreach (JsonElement message in list)
        {
            if (GetMessageSurfaceId(message) != surfaceId)
            {
                throw new InvalidOperationException(
                    $"renderA2uiTool: all messages must share the same surfaceId (expected \"{surfaceId}\")");
            }
        }

        JsonElement componentsEl = updateComponents[0].GetProperty("updateComponents").GetProperty("components");
        bool rootDefined = componentsEl.EnumerateArray().Any(c =>
            c.TryGetProperty("id", out JsonElement id) && id.GetString() == "root");
        if (!rootDefined)
        {
            throw new InvalidOperationException(
                "renderA2uiTool: updateComponents.components must define a component with id \"root\"");
        }

        ValidateChildShape(componentsEl);
        ValidateReferentialIntegrity(componentsEl);

        var payload = new
        {
            surfaceId,
            messages = list.Select(m => JsonSerializer.Deserialize<object>(m.GetRawText())).ToArray(),
        };
        return JsonSerializer.SerializeToElement(payload, JsonOptions);
    }

    private static string GetMessageSurfaceId(JsonElement message)
    {
        foreach (string key in new[] { "createSurface", "updateComponents", "updateDataModel", "deleteSurface" })
        {
            if (message.TryGetProperty(key, out JsonElement nested) &&
                nested.TryGetProperty("surfaceId", out JsonElement sid))
            {
                return sid.GetString() ?? "";
            }
        }

        throw new InvalidOperationException("renderA2uiTool: encountered message without recognizable type");
    }

    private static void ValidateChildShape(JsonElement components)
    {
        foreach (JsonElement component in components.EnumerateArray())
        {
            if (!component.TryGetProperty("component", out JsonElement nameEl) ||
                nameEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string name = nameEl.GetString() ?? "";
            string id = component.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? "<unknown>"
                : "<unknown>";

            if (SingleChildComponents.Contains(name) &&
                component.TryGetProperty("children", out JsonElement children) &&
                children.ValueKind == JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"renderA2uiTool: component \"{id}\" ({name}) uses \"children\", but {name} takes a SINGLE \"child\" (one component id). To show multiple elements, wrap them in a Column or Row and set that container's id as \"child\".");
            }

            if (MultiChildComponents.Contains(name) &&
                component.TryGetProperty("child", out JsonElement child) &&
                child.ValueKind == JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"renderA2uiTool: component \"{id}\" ({name}) uses \"child\", but {name} takes a \"children\" array of component ids.");
            }
        }
    }

    private static void ValidateReferentialIntegrity(JsonElement components)
    {
        HashSet<string> definedIds = new(StringComparer.Ordinal);
        foreach (JsonElement component in components.EnumerateArray())
        {
            if (component.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
            {
                definedIds.Add(id.GetString()!);
            }
        }

        foreach (string referencedId in CollectReferencedChildIds(components))
        {
            if (!definedIds.Contains(referencedId))
            {
                throw new InvalidOperationException(
                    $"renderA2uiTool: component id \"{referencedId}\" is referenced via child/children but is not defined in updateComponents.components");
            }
        }
    }

    private static IEnumerable<string> CollectReferencedChildIds(JsonElement components)
    {
        foreach (JsonElement component in components.EnumerateArray())
        {
            if (component.TryGetProperty("child", out JsonElement child) && child.ValueKind == JsonValueKind.String)
            {
                yield return child.GetString()!;
            }

            if (component.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in children.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        yield return entry.GetString()!;
                    }
                }
            }
        }
    }
}
