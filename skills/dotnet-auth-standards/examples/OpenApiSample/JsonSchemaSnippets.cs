using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Json.Schema;

namespace OpenApiSample;

/// <summary>Validate a JSON instance (for example MCP tool arguments) against a 2020-12 schema.</summary>
public static class SchemaCheck
{
    public const string Dialect2020 = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>Generate a schema from a C# type with System.Text.Json (built in since .NET 9).
    /// Unmapped members are disallowed so the schema carries additionalProperties:false.</summary>
    public static JsonObject Generate(Type type)
    {
        var options = new JsonSerializerOptions
        {
            // Not JsonSerializerDefaults.Web: it sets NumberHandling=AllowReadingFromString, and the exporter then
            // emits "type":["string","integer"] for numbers, so the schema accepts "5" for an integer.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(options, type,
            new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }).AsObject();
        schema["$schema"] = Dialect2020;        // say the dialect out loud
        return schema;
    }

    /// <summary>Compile a schema. Rejects any dialect other than 2020-12 and never fetches remote $ref targets.</summary>
    public static JsonSchema Compile(string schemaJson)
    {
        var declared = JsonNode.Parse(schemaJson)?["$schema"]?.GetValue<string>();
        if (declared is not null && declared != Dialect2020)
            throw new NotSupportedException($"Unsupported JSON Schema dialect: {declared}");

        // A fresh registry whose Fetch refuses every URI: an external $ref is never dereferenced
        // (MCP 2026-07-28 basic, "$ref Resolution": MUST NOT auto-dereference network URIs).
        var options = new BuildOptions
        {
            SchemaRegistry = new SchemaRegistry
            {
                Fetch = (uri, _) => throw new InvalidOperationException($"Remote $ref not allowed: {uri}"),
            },
        };
        return JsonSchema.FromText(schemaJson, options);
    }

    /// <summary>True when the instance is valid. A schema that cannot be resolved throws; it is never treated as "valid".</summary>
    public static bool IsValid(JsonSchema schema, string instanceJson)
    {
        using var instance = JsonDocument.Parse(instanceJson);
        var results = schema.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
        return results.IsValid;
    }
}
