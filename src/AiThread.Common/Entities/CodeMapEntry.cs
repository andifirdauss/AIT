using AiThread.Common.Enums;

namespace AiThread.Common.Entities;

// Module and layer of one file, decided once and reused so the same file always gets the same answer. Keyed by the
// project key and the path relative to the repository root, so it holds on every developer's machine.
public class CodeMapEntry : BaseEntity
{
    public const int MaxPathLength = 400;

    public string ProjectKey { get; set; } = string.Empty;

    // Relative to the project root, forward slashes.
    public string Path { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;
    public MemoryLayer Layer { get; set; }

    // The app inside a repository that holds several (folder of the nearest manifest); null in a single-app repository.
    public string? Area { get; set; }

    public CodeMapSource Source { get; set; }
}
