namespace AIThread.Service.Workspace.Structure;

// The declarations of one source file, as shown in Project > Detail → Struktur kode. Signature is one trimmed line.
// EndLine: the last line of the body (the line itself for a declaration without one).
public sealed record OutlineMember(string Name, string Kind, int Line, string? Signature, int EndLine);

// Container: the enclosing type of a nested type. Namespace: the file's namespace, when the language has one.
public sealed record OutlineType(
    string Name,
    string Kind,
    string? Namespace,
    string? Container,
    int Line,
    bool IsPartial,
    IReadOnlyList<OutlineMember> Members,
    int EndLine);

// Functions: declarations outside any type (PHP/JS/Python functions, JS object-literal methods).
public sealed record FileOutline(IReadOnlyList<OutlineType> Types, IReadOnlyList<OutlineMember> Functions)
{
    public static readonly FileOutline Empty = new([], []);

    public int MemberCount => Types.Sum(t => t.Members.Count) + Functions.Count;
}

public static class OutlineLanguages
{
    public const string CSharp = "C#";
    public const string Php = "PHP";
    public const string JavaScript = "JavaScript";
    public const string TypeScript = "TypeScript";
    public const string Python = "Python";
    public const string Java = "Java";
    public const string Kotlin = "Kotlin";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = CSharp,
        [".php"] = Php,
        [".js"] = JavaScript,
        [".mjs"] = JavaScript,
        [".cjs"] = JavaScript,
        [".jsx"] = JavaScript,
        [".ts"] = TypeScript,
        [".tsx"] = TypeScript,
        [".py"] = Python,
        [".java"] = Java,
        [".kt"] = Kotlin
    };

    // Null for files that are listed but not parsed (views, styles, config, …).
    public static string? Of(string path) => ByExtension.GetValueOrDefault(Path.GetExtension(path));

    public static FileOutline Outline(string language, string content) => language == CSharp
        ? CSharpOutline.Parse(content)
        : PatternOutline.Parse(language, content);
}
