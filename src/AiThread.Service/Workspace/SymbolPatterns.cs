// Heuristic declaration finder for non-C# languages; regex per line, so accuracy is lower than Roslyn.
using System.Text.RegularExpressions;

namespace AiThread.Service.Workspace;

public sealed record SymbolDeclaration(int Line, string Kind, string Signature, string? Container);

public static class SymbolPatterns
{
    private static readonly HashSet<string> JsTsExt = new(StringComparer.OrdinalIgnoreCase)
        { ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx", ".vue", ".svelte" };
    private static readonly HashSet<string> PyExt = new(StringComparer.OrdinalIgnoreCase) { ".py" };
    private static readonly HashSet<string> JavaExt = new(StringComparer.OrdinalIgnoreCase) { ".java", ".kt" };

    private static readonly HashSet<string> MethodKeywords = new(StringComparer.Ordinal)
        { "if", "for", "while", "switch", "catch", "return", "function", "new", "await", "else" };

    private static readonly Regex ContainerPattern =
        new(@"\b(class|interface|enum|record|object)\s+([A-Za-z_$][\w$]*)", RegexOptions.CultureInvariant);

    private const int MaxSignature = 200;

    public static bool Supports(string extension) =>
        JsTsExt.Contains(extension) || PyExt.Contains(extension) || JavaExt.Contains(extension);

    public static bool IsCommentLine(string extension, string line)
    {
        var t = line.TrimStart();
        if (PyExt.Contains(extension))
            return t.StartsWith('#');
        if (JsTsExt.Contains(extension) || JavaExt.Contains(extension))
            return t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal) || t.StartsWith('*');
        return false;
    }

    public static IReadOnlyList<SymbolDeclaration> FindDeclarations(string extension, IReadOnlyList<string> lines, string name)
    {
        if (!Supports(extension) || string.IsNullOrEmpty(name))
            return [];

        var n = Regex.Escape(name);
        var result = new List<SymbolDeclaration>();

        if (JsTsExt.Contains(extension))
        {
            var patterns = new List<(string Kind, Regex Re)>
            {
                ("class", Make($@"^\s*(export\s+)?(default\s+)?(abstract\s+)?class\s+{n}\b")),
                ("interface", Make($@"^\s*(export\s+)?(declare\s+)?interface\s+{n}\b")),
                ("type alias", Make($@"^\s*(export\s+)?(declare\s+)?type\s+{n}\b\s*(<[^=]*>)?\s*=")),
                ("enum", Make($@"^\s*(export\s+)?(declare\s+)?(const\s+)?enum\s+{n}\b")),
                ("function", Make($@"^\s*(export\s+)?(default\s+)?(async\s+)?function\s*\*?\s*{n}\s*[<(]")),
                ("variable", Make($@"^\s*(export\s+)?(const|let|var)\s+{n}\b\s*[=:]")),
                ("method", Make($@"^\s*((public|private|protected|static|readonly|async|override|abstract|get|set)\s+)*\*?{n}\s*(<[^>]*>)?\s*\([^;]*$")),
            };
            return Scan(lines, extension, name, patterns, (kind, line) =>
            {
                if (kind == "variable" && (line.Contains("=>") || line.Contains("function")))
                    return "function";
                return kind;
            }, line =>
            {
                var t = line.TrimStart();
                foreach (var kw in MethodKeywords)
                    if (t.StartsWith(kw, StringComparison.Ordinal)) return true;
                return MethodKeywords.Contains(name);
            });
        }

        if (PyExt.Contains(extension))
        {
            var classRe = Make($@"^\s*class\s+{n}\b");
            var funcRe = Make($@"^(\s*)(async\s+)?def\s+{n}\s*\(");
            var varRe = Make($@"^{n}\s*(:[^=]+)?=(?!=)");
            var patterns = new List<(string Kind, Regex Re)>
            {
                ("class", classRe),
                ("function", funcRe),
                ("variable", varRe),
            };
            return Scan(lines, extension, name, patterns, (kind, _) => kind, null, funcRe);
        }

        // Java / Kotlin
        {
            var typeRe = Make($@"^\s*((public|protected|private|internal|abstract|static|final|sealed|non-sealed|open|data|inner)\s+)*(class|interface|enum|record|object|@interface)\s+{n}\b");
            var ktFunRe = Make($@"^\s*((public|private|protected|internal|override|suspend|inline|open)\s+)*fun\s+(<[^>]*>\s*)?{n}\s*\(");
            var javaMethodRe = Make($@"^\s*((public|protected|private|static|final|abstract|synchronized|native|default)\s+)*[\w<>\[\],.?]+(\s+[\w<>\[\],.?]+)*\s+{n}\s*\(");
            var ctorRe = Make($@"^\s*((public|protected|private)\s+)?{n}\s*\(");
            var patterns = new List<(string Kind, Regex Re)>
            {
                ("type", typeRe),
                ("method", ktFunRe),
                ("method", javaMethodRe),
                ("constructor", ctorRe),
            };
            return ScanJava(lines, extension, name, typeRe, ktFunRe, javaMethodRe, ctorRe);
        }

        IReadOnlyList<SymbolDeclaration> ScanJava(IReadOnlyList<string> lines2, string ext, string nm,
            Regex typeRe, Regex ktFunRe, Regex javaMethodRe, Regex ctorRe)
        {
            var output = new List<SymbolDeclaration>();
            for (var i = 0; i < lines2.Count; i++)
            {
                var line = lines2[i];
                if (IsCommentLine(ext, line)) continue;
                var m = TryMatch(typeRe, line);
                if (m is not null)
                {
                    var kw = m.Groups[m.Groups.Count - 1].Value;
                    _ = kw;
                    var keyword = KeywordOf(line);
                    var kind = keyword switch
                    {
                        "@interface" => "interface",
                        "object" => "class",
                        _ => keyword,
                    };
                    output.Add(Build(i, kind, line, lines2, ext));
                    continue;
                }
                m = TryMatch(ktFunRe, line);
                if (m is not null)
                {
                    output.Add(Build(i, "method", line, lines2, ext));
                    continue;
                }
                var t = line.TrimStart();
                if (!(t.StartsWith("return", StringComparison.Ordinal) || t.StartsWith("new", StringComparison.Ordinal)
                      || t.StartsWith("throw", StringComparison.Ordinal) || t.StartsWith("else", StringComparison.Ordinal))
                    && TryMatch(javaMethodRe, line) is not null)
                {
                    output.Add(Build(i, "method", line, lines2, ext));
                    continue;
                }
                if (TryMatch(ctorRe, line) is not null && InsideClassNamed(lines2, i, nm))
                {
                    output.Add(Build(i, "constructor", line, lines2, ext));
                }
            }
            return output;
        }

        SymbolDeclaration Build(int index, string kind, string line, IReadOnlyList<string> all, string ext) =>
            new(index + 1, kind, Signature(line), FindContainer(all, ext, index));

        IReadOnlyList<SymbolDeclaration> Scan(IReadOnlyList<string> all, string ext, string nm,
            List<(string Kind, Regex Re)> pats, Func<string, string, string> mapKind,
            Func<string, bool>? skip, Regex? pyFuncRe = null)
        {
            var output = new List<SymbolDeclaration>();
            for (var i = 0; i < all.Count; i++)
            {
                var line = all[i];
                if (IsCommentLine(ext, line)) continue;
                foreach (var (kindRaw, re) in pats)
                {
                    Match? m;
                    try { m = re.Match(line); }
                    catch (RegexMatchTimeoutException) { break; }
                    if (!m.Success) continue;

                    if (skip is not null && kindRaw == "method" && skip(line)) break;

                    var kind = mapKind(kindRaw, line);
                    if (ReferenceEquals(re, pyFuncRe))
                    {
                        var indented = m.Groups[1].Value.Length > 0;
                        var container = FindContainer(all, ext, i);
                        kind = indented && container is not null ? "method" : "function";
                    }
                    else if (PyExt.Contains(ext) && kindRaw == "variable")
                    {
                        kind = "variable";
                    }
                    output.Add(new SymbolDeclaration(i + 1, kind, Signature(line), FindContainer(all, ext, i)));
                    break;
                }
            }
            return output;
        }

        static Regex Make(string pattern) => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        static Match? TryMatch(Regex re, string line)
        {
            try
            {
                var m = re.Match(line);
                return m.Success ? m : null;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        static string KeywordOf(string line)
        {
            var m = Regex.Match(line, @"\b(@interface|class|interface|enum|record|object)\s+[A-Za-z_$][\w$]*");
            return m.Success ? m.Groups[1].Value : "class";
        }

        static bool InsideClassNamed(IReadOnlyList<string> all, int index, string nm)
        {
            var width = Indent(all[index]);
            for (var j = index - 1; j >= 0; j--)
            {
                var l = all[j];
                if (string.IsNullOrWhiteSpace(l)) continue;
                if (Indent(l) >= width) continue;
                var m = ContainerPattern.Match(l);
                if (m.Success) return m.Groups[2].Value == nm;
                width = Indent(l);
            }
            return false;
        }

        static int Indent(string line)
        {
            var w = 0;
            foreach (var c in line)
            {
                if (c == ' ') w++;
                else if (c == '\t') w += 4;
                else break;
            }
            return w;
        }

        static string Signature(string line)
        {
            var s = line.Trim().TrimEnd('{').TrimEnd();
            return s.Length > MaxSignature ? s[..MaxSignature] + "…" : s;
        }

        static string? FindContainer(IReadOnlyList<string> all, string ext, int index)
        {
            var width = Indent(all[index]);
            if (width == 0) return null;
            for (var j = index - 1; j >= 0; j--)
            {
                var l = all[j];
                if (IsCommentLine(ext, l) || string.IsNullOrWhiteSpace(l)) continue;
                if (Indent(l) >= width) continue;
                var m = ContainerPattern.Match(l);
                if (m.Success) return m.Groups[2].Value;
                width = Indent(l);
                if (width == 0) break;
            }
            return null;
        }
    }
}
