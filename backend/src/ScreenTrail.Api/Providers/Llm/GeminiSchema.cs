using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenTrail.Api.Providers.Llm;

/// <summary>
/// The note's JSON schema in the form Gemini's <c>responseSchema</c> reads (E-03).
///
/// <c>research/prompts/schema.json</c> is the contract, the same file <see cref="ClaudeSchema"/> sends.
/// Gemini's response schema is a subset of OpenAPI's: a type in capitals, a description, an enum, the
/// properties of an object and those it requires, and the items of an array. Only those are sent. The
/// step is written out where it is referenced, a constant becomes an enum of one, and the lengths,
/// counts and ranges are said in the field's description, as the Claude request says them, because
/// <see cref="Summarize.DraftValidator"/> holds a draft to every one of them afterwards whichever model
/// wrote it.
/// </summary>
public static class GeminiSchema
{
    /// <summary>Keywords said in a description instead, and how each is said.</summary>
    private static readonly (string Keyword, string Phrase)[] Said =
    [
        ("minLength", "at least {0} characters"),
        ("maxLength", "at most {0} characters"),
        ("minItems", "at least {0} items"),
        ("maxItems", "at most {0} items"),
        ("minimum", "at least {0}"),
        ("maximum", "at most {0}"),
    ];

    private static readonly Lazy<JsonElement> Cached = new(Build);

    /// <summary>The schema as <c>generationConfig.responseSchema</c> takes it.</summary>
    public static JsonElement Wire() => Cached.Value;

    private static JsonElement Build()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("schema.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        var root = JsonNode.Parse(stream)!.AsObject();
        var definitions = root["$defs"] as JsonObject ?? [];

        return JsonSerializer.SerializeToElement(Convert(root, definitions));
    }

    private static JsonObject Convert(JsonObject schema, JsonObject definitions)
    {
        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            const string Local = "#/$defs/";
            if (!reference.StartsWith(Local, StringComparison.Ordinal) || definitions[reference[Local.Length..]] is not JsonObject defined)
            {
                throw new InvalidOperationException($"schema.json refers to {reference}, which it does not define.");
            }

            return Convert(defined, definitions);
        }

        var converted = new JsonObject();

        if (schema["const"] is JsonValue constant)
        {
            converted["type"] = "STRING";
            converted["enum"] = new JsonArray(constant.DeepClone());
        }
        else if (schema["enum"] is JsonArray values)
        {
            converted["type"] = "STRING";
            converted["enum"] = values.DeepClone();
        }
        else if (schema["type"]?.GetValue<string>() is { } type)
        {
            converted["type"] = type.ToUpperInvariant();
        }

        var notes = Said
            .Where(said => schema[said.Keyword] is JsonValue)
            .Select(said => string.Format(CultureInfo.InvariantCulture, said.Phrase, schema[said.Keyword]!.ToJsonString()))
            .ToList();
        var description = schema["description"]?.GetValue<string>();
        if (notes.Count > 0)
        {
            var constraint = "(" + string.Join("; ", notes) + ")";
            description = string.IsNullOrEmpty(description) ? constraint : description + " " + constraint;
        }

        if (!string.IsNullOrEmpty(description))
        {
            converted["description"] = description;
        }

        if (schema["properties"] is JsonObject properties)
        {
            var named = new JsonObject();
            foreach (var (field, child) in properties)
            {
                named[field] = Convert(child!.AsObject(), definitions);
            }

            converted["properties"] = named;
        }

        if (schema["required"] is JsonArray required)
        {
            converted["required"] = required.DeepClone();
        }

        if (schema["items"] is JsonObject items)
        {
            converted["items"] = Convert(items, definitions);
        }

        return converted;
    }
}
