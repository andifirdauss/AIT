using System.Text.RegularExpressions;

namespace AIThread.Service.Testing;

// The OpenAPI documents a Swagger UI page (index.html, or the index.js newer Swashbuckle serves) points to, so the
// tester reads the real paths instead of guessing /swagger/v1/swagger.json: the "urls" of its config object
// ("/swagger/OutboundService/swagger.json") or its single "url".
public static partial class SwaggerDocuments
{
    private const int MaxDocuments = 20;

    public static IReadOnlyList<string> From(string? body)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains("swagger", StringComparison.OrdinalIgnoreCase)) return [];
        return DocumentUrl().Matches(body)
            .Select(m => m.Groups["url"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxDocuments)
            .ToList();
    }

    [GeneratedRegex(@"[""']?\burl[""']?\s*:\s*[""'](?<url>[^""']+?\.(?:json|ya?ml))[""']", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentUrl();
}
