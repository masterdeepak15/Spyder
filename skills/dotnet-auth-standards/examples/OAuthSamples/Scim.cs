using System.Text.Json;

namespace OAuthSamples;

/// <summary>SCIM RFC 7644 pieces that are easy to get wrong: media type, error body, PATCH envelope, paging.</summary>
public static class Scim
{
    public const string MediaType = "application/scim+json";
    public const string ErrorSchema = "urn:ietf:params:scim:api:messages:2.0:Error";
    public const string PatchOpSchema = "urn:ietf:params:scim:api:messages:2.0:PatchOp";

    /// <summary>3.12: JSON body with schemas, "status" as a STRING, optional scimType and detail.</summary>
    public static string ErrorBody(int status, string? scimType, string? detail)
    {
        var body = new Dictionary<string, object> { ["schemas"] = new[] { ErrorSchema }, ["status"] = status.ToString() };
        if (scimType is not null) body["scimType"] = scimType;
        if (detail is not null) body["detail"] = detail;
        return JsonSerializer.Serialize(body);
    }

    public static IResult Error(int status, string? scimType, string? detail) =>
        Results.Content(ErrorBody(status, scimType, detail), MediaType, statusCode: status);

    private static readonly string[] Ops = ["add", "remove", "replace"];

    /// <summary>3.5.2: schemas must carry the PatchOp URI; Operations is a non-empty array; op is add/remove/replace; remove needs a path.</summary>
    public static (bool Ok, string? ScimType, string? Detail) ValidatePatch(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return (false, "invalidSyntax", "body must be an object");
        if (!body.TryGetProperty("schemas", out var schemas) || schemas.ValueKind != JsonValueKind.Array
            || !schemas.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == PatchOpSchema))
            return (false, "invalidSyntax", "schemas must contain " + PatchOpSchema);
        if (!body.TryGetProperty("Operations", out var ops) || ops.ValueKind != JsonValueKind.Array || ops.GetArrayLength() == 0)
            return (false, "invalidSyntax", "Operations must be a non-empty array");
        foreach (var op in ops.EnumerateArray())
        {
            // RFC 7644 lists the op values in lower case; the text read for this skill does not say whether case matters, so any case is accepted here.
            if (!op.TryGetProperty("op", out var name) || name.ValueKind != JsonValueKind.String
                || !Ops.Contains(name.GetString()!, StringComparer.OrdinalIgnoreCase))
                return (false, "invalidSyntax", "op must be add, remove or replace");
            var hasPath = op.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String && path.GetString()!.Length > 0;
            if (string.Equals(name.GetString(), "remove", StringComparison.OrdinalIgnoreCase) && !hasPath)
                return (false, "noTarget", "remove requires a path");
        }
        return (true, null, null);
    }

    /// <summary>3.4.2.4: startIndex is 1-based (below 1 means 1); a negative count means 0; the server may cap count.</summary>
    public static (int StartIndex, int Count) NormalizePaging(int? startIndex, int? count, int serverMax)
    {
        var start = Math.Max(startIndex ?? 1, 1);
        var c = Math.Max(count ?? serverMax, 0);
        return (start, Math.Min(c, serverMax));
    }
}
