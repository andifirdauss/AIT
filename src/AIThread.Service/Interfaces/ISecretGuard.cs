using System.Text.Json.Nodes;
using AIThread.Service.Security;

namespace AIThread.Service.Interfaces;

// Masks secrets before text reaches a model and writes them back when AIT writes a file or calls a tool (SecretGuard).
public interface ISecretGuard
{
    bool Enabled { get; }

    // Requests forwarded without orchestration (passthrough) are masked too.
    bool MaskPassthrough { get; }

    // Distinct secrets masked in the current tally (BeginTally), 0 outside one.
    int TallyCount { get; }

    void Configure(bool enabled, bool maskPassthrough);

    string Redact(string text);

    string? RedactNullable(string? text);

    string RedactFile(string path, string content, IReadOnlyCollection<string>? extraKeys = null);

    void RedactMessages(JsonArray messages);

    int RedactJson(JsonNode? node);

    void Learn(string path, string content, IReadOnlyCollection<string>? extraKeys = null);

    SecretRestore Restore(string text);

    SecretRestore RestoreJson(JsonNode? node, string? filePath = null);

    IDisposable BeginTally();

    SecurityCounters GetCounters();

    // The values masked since AIT started, for the list in Setting → Keamanan & privasi.
    IReadOnlyList<SecretSighting> GetSightings();

    // Values marked as not secret, by placeholder id: no longer masked.
    void SetAllowed(IEnumerable<string> ids);

    string? Reveal(string id);
}
