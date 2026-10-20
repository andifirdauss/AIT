using System.Text.RegularExpressions;

namespace AIThread.Service.Workspace.Structure;

// Types and members of PHP, JavaScript/TypeScript, Python and Java/Kotlin files from line patterns. No parser: what
// belongs to a type is decided by indentation (a type's block ends at a "}" on its own indentation, or at any line
// indented less; in Python at the next line indented no deeper). Members are taken only at the indentation of the
// type's first member, so helpers inside method bodies are not listed. A body ends where its braces close again
// (strings and comments skipped), in Python at the next line indented no deeper. Functions declared inside another
// function's body (callbacks such as "success: function") are left out. Good for formatted code; generated or
// minified files are skipped.
public static partial class PatternOutline
{
    private const int MaxSignature = 160;
    private const int MinifiedLineLength = 1_000;
    private const int MinifiedProbeLines = 30;

    // A declaration whose "{" is not found within this many lines has no body ("abstract function x();").
    private const int MaxHeadLines = 5;
    private const int MaxBlockLines = 5_000;

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "for", "foreach", "while", "switch", "catch", "return", "function", "new", "else", "elseif", "do", "try",
        "throw", "typeof", "delete", "await", "yield", "case", "super", "this", "import", "export", "with", "echo", "print",
        "isset", "unset", "empty", "list", "array", "match", "fn", "use", "require", "require_once", "include", "include_once"
    };

    public static FileOutline Parse(string language, string content)
    {
        var lines = content.Split('\n');
        var rules = Rules.For(language);
        if (rules is null) return FileOutline.Empty;
        if (rules.SkipMinified && LooksMinified(lines)) return FileOutline.Empty;

        string? ns = null;
        var types = new List<TypeBuilder>();
        var open = new Stack<TypeBuilder>();
        var functions = new List<OutlineMember>();
        var inBlockComment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            var trimmed = raw.TrimStart();
            if (trimmed.Length == 0) continue;
            if (inBlockComment)
            {
                if (trimmed.Contains("*/", StringComparison.Ordinal)) inBlockComment = false;
                continue;
            }
            if (rules.BlockComments && trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                if (!trimmed.Contains("*/", StringComparison.Ordinal)) inBlockComment = true;
                continue;
            }
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || (rules.BlockComments && trimmed.StartsWith('*'))
                || (rules.HashComments && trimmed.StartsWith('#')))
                continue;

            var indent = Indent(raw);
            var line = i + 1;
            while (open.Count > 0 && Closes(open.Peek(), indent, trimmed, rules)) open.Pop();
            if (trimmed[0] == '}') continue;

            if (rules.Namespace?.Match(raw) is { Success: true } nsMatch)
            {
                ns = nsMatch.Groups[1].Value;
                continue;
            }

            if (rules.Type.Match(raw) is { Success: true } typeMatch)
            {
                var type = new TypeBuilder(typeMatch.Groups[2].Value, NormalizeKind(typeMatch.Groups[1].Value), ns,
                    open.Count > 0 ? open.Peek().Name : null, line, indent, BlockEnd(lines, i, rules, indent));
                types.Add(type);
                if (OpensBlock(trimmed, rules)) open.Push(type);
                continue;
            }

            if (rules.Prototype?.Match(raw) is { Success: true } proto)
            {
                var owner = types.FirstOrDefault(t => t.Name == proto.Groups[1].Value && t.Container is null)
                            ?? Add(types, new TypeBuilder(proto.Groups[1].Value, "class", ns, null, line, indent, line));
                owner.Members.Add(new OutlineMember(proto.Groups[2].Value, "method", line, Signature(trimmed), BlockEnd(lines, i, rules, indent)));
                continue;
            }

            if (open.Count > 0 && indent > open.Peek().Indent)
            {
                var owner = open.Peek();
                if (MemberOf(rules, owner, raw) is not { } name) continue;
                owner.MemberIndent ??= indent;
                if (indent != owner.MemberIndent) continue;
                owner.Members.Add(new OutlineMember(name, IsConstructor(name, owner.Name) ? "constructor" : "method", line, Signature(trimmed),
                    BlockEnd(lines, i, rules, indent)));
                continue;
            }

            if (FunctionOf(rules, raw) is { } function)
                functions.Add(new OutlineMember(function, "function", line, Signature(trimmed), BlockEnd(lines, i, rules, indent)));
        }

        var outer = functions.Where(f => !functions.Any(o => o.Line < f.Line && f.Line <= o.EndLine)).ToList();
        return new FileOutline(types.Select(t => t.Build()).ToList(), outer);
    }

    // Last line (1-based) of the declaration starting at lines[start]. Brace languages: where the first "{" from here
    // closes again, skipping strings and comments; a ";" before any "{" (or no "{" within MaxHeadLines) means no body.
    // Python: the last line indented deeper than the declaration.
    private static int BlockEnd(string[] lines, int start, Rules rules, int indent)
    {
        if (!rules.Braces)
        {
            var last = start;
            for (var i = start + 1; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith('#')) continue;
                if (Indent(lines[i]) <= indent) break;
                last = i;
            }
            return last + 1;
        }

        var depth = 0;
        var opened = false;
        var quote = '\0';
        var blockComment = false;
        for (var i = start; i < lines.Length && i < start + MaxBlockLines; i++)
        {
            var text = lines[i];
            for (var j = 0; j < text.Length; j++)
            {
                var c = text[j];
                if (blockComment)
                {
                    if (c == '*' && j + 1 < text.Length && text[j + 1] == '/') { blockComment = false; j++; }
                    continue;
                }
                if (quote != '\0')
                {
                    if (c == '\\') j++;
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '/' && j + 1 < text.Length)
                {
                    if (text[j + 1] == '/') break;
                    if (text[j + 1] == '*') { blockComment = true; j++; continue; }
                }
                if (c == '#' && rules.HashComments) break;
                switch (c)
                {
                    case '"' or '\'' or '`':
                        quote = c;
                        break;
                    case '{':
                        depth++;
                        opened = true;
                        break;
                    case '}':
                        depth--;
                        if (opened && depth <= 0) return i + 1;
                        break;
                    case ';' when !opened:
                        return i + 1;
                }
            }
            // Only template literals span lines.
            if (quote is '"' or '\'') quote = '\0';
            if (!opened && i - start >= MaxHeadLines) return start + 1;
        }
        return opened ? Math.Min(lines.Length, start + MaxBlockLines) : start + 1;
    }

    private static TypeBuilder Add(List<TypeBuilder> types, TypeBuilder type)
    {
        types.Add(type);
        return type;
    }

    // Brace languages: a "}" on the type's own indentation, or anything indented less. Python: anything indented no
    // deeper than the class line, except decorators of the next member.
    private static bool Closes(TypeBuilder type, int indent, string trimmed, Rules rules) => rules.Braces
        ? indent < type.Indent || (indent == type.Indent && trimmed[0] == '}')
        : indent <= type.Indent && trimmed[0] != '@';

    private static bool OpensBlock(string trimmed, Rules rules)
    {
        if (!rules.Braces)
        {
            var colon = trimmed.LastIndexOf(':');
            return colon < 0 || trimmed[(colon + 1)..].Trim().Length == 0 || trimmed[(colon + 1)..].TrimStart().StartsWith('#');
        }
        var opens = trimmed.Count(c => c == '{');
        var closes = trimmed.Count(c => c == '}');
        // "class A {}" or "interface B;" opens nothing; "class A" with "{" on the next line does.
        return !(opens > 0 && opens == closes) && !trimmed.EndsWith(';');
    }

    private static string? MemberOf(Rules rules, TypeBuilder owner, string raw)
    {
        foreach (var pattern in rules.Members)
        {
            if (pattern.Match(raw) is not { Success: true } match) continue;
            var name = match.Groups[1].Value;
            return Keywords.Contains(name) ? null : name;
        }
        if (rules.Constructor?.Match(raw) is { Success: true } ctor && ctor.Groups[1].Value == owner.Name) return owner.Name;
        return null;
    }

    private static string? FunctionOf(Rules rules, string raw)
    {
        foreach (var pattern in rules.Functions)
        {
            if (pattern.Match(raw) is not { Success: true } match) continue;
            var name = match.Groups[1].Value;
            return Keywords.Contains(name) ? null : name;
        }
        return null;
    }

    private static bool IsConstructor(string name, string typeName) =>
        name is "__construct" or "constructor" or "__init__" || name == typeName;

    private static string NormalizeKind(string keyword) => keyword switch
    {
        "@interface" => "annotation",
        "object" => "object",
        _ => keyword
    };

    private static bool LooksMinified(string[] lines) => lines.Take(MinifiedProbeLines).Any(l => l.Length > MinifiedLineLength);

    private static int Indent(string line)
    {
        var width = 0;
        foreach (var c in line)
        {
            if (c == ' ') width++;
            else if (c == '\t') width += 4;
            else break;
        }
        return width;
    }

    private static string Signature(string trimmed)
    {
        var brace = trimmed.IndexOf('{');
        var text = (brace > 0 ? trimmed[..brace] : trimmed).Trim().TrimEnd(':').Trim();
        return text.Length > MaxSignature ? text[..MaxSignature] + "…" : text;
    }

    // endLine: of the declaration's own block; a JavaScript prototype "class" has none and ends with its last member.
    private sealed class TypeBuilder(string name, string kind, string? ns, string? container, int line, int indent, int endLine)
    {
        public string Name { get; } = name;
        public string? Container { get; } = container;
        public int Indent { get; } = indent;
        public int? MemberIndent { get; set; }
        public List<OutlineMember> Members { get; } = [];

        public OutlineType Build() => new(Name, kind, ns, Container, line, false, Members, Math.Max(endLine, Members.Count == 0 ? line : Members.Max(m => m.EndLine)));
    }

    // Members: matched only inside a type, at its member indentation. Functions: matched outside any type.
    private sealed record Rules(
        Regex Type,
        IReadOnlyList<Regex> Members,
        IReadOnlyList<Regex> Functions,
        Regex? Constructor = null,
        Regex? Namespace = null,
        Regex? Prototype = null,
        bool Braces = true,
        bool BlockComments = true,
        bool HashComments = false,
        bool SkipMinified = false)
    {
        private static readonly Rules Php = new(
            PhpType(), [PhpFunction()], [PhpFunction()], Namespace: PhpNamespace(), HashComments: true);

        private static readonly Rules Js = new(
            JsType(), [JsMethod(), JsObjectMethod(), JsArrowMember()], [JsFunction(), JsVariableFunction(), JsObjectMethod(), JsStrictMethod()],
            Prototype: JsPrototype(), SkipMinified: true);

        private static readonly Rules Python = new(
            PyClass(), [PyDef()], [PyDef()], Braces: false, BlockComments: false, HashComments: true);

        private static readonly Rules Java = new(
            JavaType(), [KotlinFun(), JavaMethod()], [KotlinFun()], Constructor: JavaConstructor(), Namespace: JavaPackage());

        public static Rules? For(string language) => language switch
        {
            OutlineLanguages.Php => Php,
            OutlineLanguages.JavaScript or OutlineLanguages.TypeScript => Js,
            OutlineLanguages.Python => Python,
            OutlineLanguages.Java or OutlineLanguages.Kotlin => Java,
            _ => null
        };
    }

    [GeneratedRegex(@"^\s*(?:(?:abstract|final|readonly)\s+)*(class|interface|trait|enum)\s+([A-Za-z_]\w*)")]
    private static partial Regex PhpType();

    [GeneratedRegex(@"^\s*(?:(?:public|protected|private|static|abstract|final)\s+)*function\s+&?\s*([A-Za-z_]\w*)\s*\(")]
    private static partial Regex PhpFunction();

    [GeneratedRegex(@"^\s*namespace\s+([\w\\]+)\s*[;{]")]
    private static partial Regex PhpNamespace();

    [GeneratedRegex(@"^\s*(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:abstract\s+)?(?:const\s+)?(class|interface|enum)\s+([A-Za-z_$][\w$]*)")]
    private static partial Regex JsType();

    // In a class body: "name(…" not ending the statement (signatures may span lines).
    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|readonly|async|override|abstract|get|set|declare)\s+)*\*?\s*([A-Za-z_$#][\w$]*)\s*(?:<[^>]*>)?\s*\([^;]*$")]
    private static partial Regex JsMethod();

    [GeneratedRegex(@"^\s*([A-Za-z_$][\w$]*)\s*:\s*(?:async\s+)?function\b")]
    private static partial Regex JsObjectMethod();

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|readonly)\s+)*([A-Za-z_$#][\w$]*)\s*(?::[^=]+)?=\s*(?:async\s+)?(?:\([^)]*\)|[A-Za-z_$][\w$]*)\s*(?::[^=]+)?=>")]
    private static partial Regex JsArrowMember();

    [GeneratedRegex(@"^\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s*\*?\s*([A-Za-z_$][\w$]*)\s*[<(]")]
    private static partial Regex JsFunction();

    [GeneratedRegex(@"^\s*(?:export\s+)?(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*(?::[^=]+)?=\s*(?:async\s+)?(?:function\b|\([^)]*\)\s*(?::[^=]+)?=>|[A-Za-z_$][\w$]*\s*=>)")]
    private static partial Regex JsVariableFunction();

    // Outside a class only a complete one-line head counts ("save(a, b) {"), so calls are not taken for methods.
    [GeneratedRegex(@"^\s*(?:async\s+)?([A-Za-z_$][\w$]*)\s*\([^()]*\)\s*\{\s*$")]
    private static partial Regex JsStrictMethod();

    [GeneratedRegex(@"^\s*([A-Za-z_$][\w$]*)\.prototype\.([A-Za-z_$][\w$]*)\s*=\s*(?:async\s+)?function\b")]
    private static partial Regex JsPrototype();

    [GeneratedRegex(@"^\s*(class)\s+([A-Za-z_]\w*)")]
    private static partial Regex PyClass();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+([A-Za-z_]\w*)\s*\(")]
    private static partial Regex PyDef();

    [GeneratedRegex(@"^\s*(?:(?:public|protected|private|internal|abstract|static|final|sealed|non-sealed|open|data|inner|enum|annotation|value)\s+)*(class|interface|enum|record|object|@interface)\s+([A-Za-z_]\w*)")]
    private static partial Regex JavaType();

    [GeneratedRegex(@"^\s*(?:(?:public|protected|private|static|final|abstract|synchronized|native|default)\s+)*(?:<[^>]+>\s+)?[\w<>\[\],.?]+\s+([A-Za-z_]\w*)\s*\(")]
    private static partial Regex JavaMethod();

    [GeneratedRegex(@"^\s*(?:(?:public|protected|private)\s+)?([A-Za-z_]\w*)\s*\(")]
    private static partial Regex JavaConstructor();

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|override|suspend|inline|open|abstract|operator|infix)\s+)*fun\s+(?:<[^>]*>\s*)?(?:[\w.]+\.)?([A-Za-z_]\w*)\s*\(")]
    private static partial Regex KotlinFun();

    [GeneratedRegex(@"^\s*package\s+([\w.]+)")]
    private static partial Regex JavaPackage();
}
