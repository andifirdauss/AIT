using System.Text;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // What the tester reads under a response so it does not guess paths: after a 404 the routes in the code closest to
    // the path (or where to find the real ones), after a Swagger UI page the OpenAPI documents it names. Null: nothing
    // to add.
    public static string? ResponseHint(string method, string path, int? status, string? body, IReadOnlyList<CodeRoute> routes)
    {
        var hint = new StringBuilder();
        if (status == 404)
        {
            if (AspNetRoutes.Matching(method, path, routes).Count > 0)
            {
                hint.Append("AIT: the code declares this route, so the running app has probably not loaded the change (it needs a restart or a rebuild), or a filter answers 404 for this request.\n");
            }
            else if (AspNetRoutes.Closest(method, path, routes) is { Count: > 0 } closest)
            {
                hint.Append($"AIT: no route in the code serves {method} {path}. The routes that come closest:\n");
                foreach (var route in closest) hint.Append($"- {route.Method} {route.Path} — {route.Handler} ({route.File}:{route.Line})\n");
                hint.Append("Call one of these instead of trying other spellings.\n");
            }
            else
            {
                hint.Append("AIT: do not guess other spellings of the path. When the app serves Swagger UI, open /swagger/index.html: it names its OpenAPI documents (the \"urls\" of its config object, or index.js), and those list the real paths.\n");
            }
        }
        var documents = SwaggerDocuments.From(body);
        if (documents.Count > 0)
            hint.Append("AIT: this Swagger UI page reads these OpenAPI documents; GET one to see the real paths: ").Append(string.Join(", ", documents)).Append('\n');
        return hint.Length == 0 ? null : hint.ToString();
    }
}
