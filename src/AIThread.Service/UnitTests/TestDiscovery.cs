using System.Text.RegularExpressions;
using AIThread.Service.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AIThread.Service.UnitTests;

// A test found in a file. Selector is what the runner is given (see UnitTestCase.Selector); StartLine includes the
// attributes, decorators and doc comments above the test, EndLine its last line (1-based).
public sealed record DiscoveredTest(
    string Framework,
    string? TestProject,
    string File,
    string? ClassName,
    string MethodName,
    string Selector,
    int StartLine,
    int EndLine);

// Finds the tests of a project without running anything: C# through Roslyn syntax, the other languages by their
// declaration patterns ([Fact]/[Test]/[TestMethod], @Test, def test_, it()/test(), function test*, func TestX, #[test]).
public static partial class TestDiscovery
{
    private const long MaxFileBytes = 1024 * 1024;

    private static readonly HashSet<string> CSharpTestAttributes = new(StringComparer.Ordinal)
    {
        "Fact", "Theory", "SkippableFact", "SkippableTheory", "Test", "TestCase", "TestCaseSource", "TestMethod", "DataTestMethod"
    };

    // The project files (relative, forward slashes) that may hold tests of the suites.
    public static IReadOnlyList<(string File, TestSuite Suite)> Candidates(IReadOnlyList<TestSuite> suites, string root, IEnumerable<string> files)
    {
        var result = new List<(string, TestSuite)>();
        foreach (var file in files)
        {
            if (!LooksLikeTest(file)) continue;
            if (TestCommandResolver.SuiteFor(suites, root, file) is { } suite) result.Add((file, suite));
        }
        return result;
    }

    // The tests in one file; empty when it holds none or cannot be read.
    public static IReadOnlyList<DiscoveredTest> InFile(string root, string file, TestSuite suite)
    {
        var full = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
        string content;
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists || info.Length > MaxFileBytes) return [];
            content = File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return InContent(file, content, suite);
    }

    public static IReadOnlyList<DiscoveredTest> InContent(string file, string content, TestSuite suite)
    {
        var tests = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => CSharp(file, content, suite),
            ".java" or ".kt" => Java(file, content, suite),
            ".js" or ".mjs" or ".cjs" or ".jsx" or ".ts" or ".tsx" or ".mts" or ".cts" => JavaScript(file, content, suite),
            ".py" => Python(file, content, suite),
            ".php" => Php(file, content, suite),
            ".go" => Go(file, content, suite),
            ".rs" => Rust(file, content, suite),
            _ => []
        };
        // A title used twice in one file (two describe blocks) runs as one selector; the first declaration stands for it.
        return tests.DistinctBy(t => t.Selector).ToList();
    }

    // The lines StartLine..EndLine of content.
    public static string Slice(string content, int startLine, int endLine)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var from = Math.Clamp(startLine, 1, lines.Length);
        var to = Math.Clamp(endLine, from, lines.Length);
        return string.Join("\n", lines[(from - 1)..to]);
    }

    private static bool LooksLikeTest(string file)
    {
        var name = Path.GetFileName(file);
        var path = file.Replace('\\', '/');
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".cs" => true,
            ".py" => name.StartsWith("test_", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_test.py", StringComparison.OrdinalIgnoreCase),
            ".php" => name.EndsWith("Test.php", StringComparison.Ordinal),
            ".go" => name.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase),
            ".rs" => true,
            ".java" or ".kt" => path.Contains("src/test/", StringComparison.OrdinalIgnoreCase),
            ".js" or ".mjs" or ".cjs" or ".jsx" or ".ts" or ".tsx" or ".mts" or ".cts" =>
                name.Contains(".test.", StringComparison.OrdinalIgnoreCase) || name.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/__tests__/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("__tests__/", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    // --- C# ---

    private static List<DiscoveredTest> CSharp(string file, string content, TestSuite suite)
    {
        if (!content.Contains('[')) return [];
        var tree = CSharpSyntaxTree.ParseText(content);
        var lines = Lines(content);
        var tests = new List<DiscoveredTest>();
        foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!method.AttributeLists.SelectMany(l => l.Attributes).Any(a => CSharpTestAttributes.Contains(AttributeName(a)))) continue;
            if (method.Parent is not TypeDeclarationSyntax type) continue;
            var className = QualifiedName(type);
            var start = tree.GetLineSpan(method.FullSpan).StartLinePosition.Line + 1;
            // Leading blank lines belong to the gap before the method.
            while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start - 1])) start++;
            var end = tree.GetLineSpan(method.Span).EndLinePosition.Line + 1;
            tests.Add(new DiscoveredTest(suite.Framework, suite.Manifest, file, className, method.Identifier.ValueText,
                $"{className}.{method.Identifier.ValueText}", start, end));
        }
        return tests;
    }

    private static string AttributeName(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            SimpleNameSyntax s => s.Identifier.ValueText,
            _ => attribute.Name.ToString()
        };
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }

    // Namespace and types from the outside in, nested types joined with "+" as the test platform names them.
    private static string QualifiedName(TypeDeclarationSyntax type)
    {
        var types = type.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.ValueText);
        var names = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Select(n => n.Name.ToString()).Reverse().ToList();
        var typeName = string.Join("+", types);
        return names.Count == 0 ? typeName : $"{string.Join(".", names)}.{typeName}";
    }

    // --- Java / Kotlin ---

    private static List<DiscoveredTest> Java(string file, string content, TestSuite suite)
    {
        var lines = Lines(content);
        var package = JavaPackage().Match(content) is { Success: true } p ? p.Groups[1].Value : null;
        var tests = new List<DiscoveredTest>();
        string? currentClass = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (JavaClass().Match(lines[i]) is { Success: true } declared) currentClass = declared.Groups[1].Value;
            if (!JavaTestAnnotation().IsMatch(lines[i])) continue;

            for (var j = i; j < Math.Min(lines.Length, i + 6); j++)
            {
                var method = JavaMethod().Match(lines[j]);
                if (!method.Success) continue;
                var name = method.Groups["name"].Value.Trim('`');
                var className = currentClass is null ? Path.GetFileNameWithoutExtension(file) : currentClass;
                var qualified = package is null ? className : $"{package}.{className}";
                tests.Add(new DiscoveredTest(suite.Framework, suite.Manifest, file, qualified, name, $"{qualified}#{name}",
                    ExtendUp(lines, i, ["@", "//", "/*", "*"]) + 1, BlockEnd(lines, j) + 1));
                i = j;
                break;
            }
        }
        return tests;
    }

    // --- JavaScript / TypeScript ---

    private static List<DiscoveredTest> JavaScript(string file, string content, TestSuite suite)
    {
        var lines = Lines(content);
        var tests = new List<DiscoveredTest>();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = JsTest().Match(lines[i]);
            if (!match.Success) continue;
            var title = Regex.Unescape(match.Groups["title"].Value);
            tests.Add(new DiscoveredTest(suite.Framework, suite.Manifest, file, null, title, $"{file}::{title}",
                ExtendUp(lines, i, ["//"]) + 1, BlockEnd(lines, i) + 1));
        }
        return tests;
    }

    // --- Python ---

    private static List<DiscoveredTest> Python(string file, string content, TestSuite suite)
    {
        var lines = Lines(content);
        var tests = new List<DiscoveredTest>();
        // Node ids are relative to the folder pytest runs in.
        var relative = Path.GetRelativePath(suite.Directory, Path.Combine(suite.Root, file)).Replace('\\', '/');
        (string Name, int Indent)? currentClass = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var indent = Indent(line);
            if (currentClass is { } open && indent <= open.Indent && !line.TrimStart().StartsWith('@') && !line.TrimStart().StartsWith('#')) currentClass = null;

            if (PythonClass().Match(line) is { Success: true } declared)
            {
                currentClass = declared.Groups["name"].Value.StartsWith("Test", StringComparison.Ordinal) ? (declared.Groups["name"].Value, indent) : null;
                continue;
            }
            var function = PythonTest().Match(line);
            if (!function.Success) continue;
            var inClass = currentClass is { } owner && indent > owner.Indent;
            // A test function in a class that is not a Test* class is not collected by pytest.
            if (indent > 0 && !inClass) continue;
            var name = function.Groups["name"].Value;
            var className = inClass ? currentClass!.Value.Name : null;
            var selector = className is null ? $"{relative}::{name}" : $"{relative}::{className}::{name}";
            tests.Add(new DiscoveredTest(suite.Framework, suite.Manifest, file, className, name, selector,
                ExtendUp(lines, i, ["@", "#"]) + 1, IndentEnd(lines, i) + 1));
        }
        return tests;
    }

    // --- PHP ---

    private static List<DiscoveredTest> Php(string file, string content, TestSuite suite)
    {
        var lines = Lines(content);
        var ns = PhpNamespace().Match(content) is { Success: true } n ? n.Groups[1].Value : null;
        var tests = new List<DiscoveredTest>();
        string? currentClass = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (PhpClass().Match(lines[i]) is { Success: true } declared) currentClass = declared.Groups[1].Value;
            var method = PhpMethod().Match(lines[i]);
            if (!method.Success || currentClass is null) continue;
            var name = method.Groups[1].Value;
            var annotated = Enumerable.Range(Math.Max(0, i - 4), Math.Min(4, i)).Any(j => lines[j].Contains("@test", StringComparison.Ordinal) || lines[j].Contains("#[Test", StringComparison.Ordinal));
            if (!name.StartsWith("test", StringComparison.Ordinal) && !annotated) continue;
            var className = ns is null ? currentClass : $"{ns}\\{currentClass}";
            tests.Add(new DiscoveredTest(suite.Framework, suite.Manifest, file, className, name, $"{className}::{name}",
                ExtendUp(lines, i, ["#[", "//", "/*", "*"]) + 1, BlockEnd(lines, i) + 1));
        }
        return tests;
    }

    // --- Go, Rust ---

    private static List<DiscoveredTest> Go(string file, string content, TestSuite suite)
    {
        var lines = Lines(content);
        var package = Path.GetDirectoryName(file)?.Replace('\\', '/') ?? string.Empty;
        var tests = new List<DiscoveredTest>();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = GoTest().Match(lines[i]);
            if (!match.Success) continue;
            var name = match.Groups[1].Value;
            tests.Add(new DiscoveredTest(suite.Framework, suite.Manifest, file, package.Length == 0 ? null : package, name, name,
                ExtendUp(lines, i, ["//"]) + 1, BlockEnd(lines, i) + 1));
        }
        return tests;
    }

    private static List<DiscoveredTest> Rust(string file, string content, TestSuite suite)
    {
        if (!content.Contains("#[test]", StringComparison.Ordinal)) return [];
        var lines = Lines(content);
        var tests = new List<DiscoveredTest>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("#[test]", StringComparison.Ordinal)) continue;
            for (var j = i + 1; j < Math.Min(lines.Length, i + 5); j++)
            {
                var match = RustFn().Match(lines[j]);
                if (!match.Success) continue;
                var name = match.Groups[1].Value;
                tests.Add(new DiscoveredTest(suite.Framework, suite.Manifest, file, Path.GetFileNameWithoutExtension(file), name, name,
                    ExtendUp(lines, i, ["#[", "//"]) + 1, BlockEnd(lines, j) + 1));
                i = j;
                break;
            }
        }
        return tests;
    }

    // --- line helpers ---

    private static string[] Lines(string content) => content.Replace("\r\n", "\n").Split('\n');

    private static int Indent(string line) => line.TakeWhile(c => c is ' ' or '\t').Sum(c => c == '\t' ? 4 : 1);

    // The first line of the comments and attributes directly above line index.
    private static int ExtendUp(string[] lines, int index, string[] prefixes)
    {
        var start = index;
        while (start > 0)
        {
            var previous = lines[start - 1].TrimStart();
            if (previous.Length == 0 || !prefixes.Any(p => previous.StartsWith(p, StringComparison.Ordinal))) break;
            start--;
        }
        return start;
    }

    // The line the block opened on (or after) line index closes on: braces counted outside strings and line comments.
    private static int BlockEnd(string[] lines, int index)
    {
        var depth = 0;
        var opened = false;
        for (var i = index; i < lines.Length; i++)
        {
            var line = lines[i];
            char? quote = null;
            for (var c = 0; c < line.Length; c++)
            {
                var ch = line[c];
                if (quote is not null)
                {
                    if (ch == '\\') c++;
                    else if (ch == quote) quote = null;
                    continue;
                }
                if (ch is '"' or '\'' or '`') quote = ch;
                else if (ch == '/' && c + 1 < line.Length && line[c + 1] == '/') break;
                else if (ch == '{')
                {
                    depth++;
                    opened = true;
                }
                else if (ch == '}' && opened && --depth == 0) return i;
            }
        }
        return index;
    }

    // Python: the last line before the next line indented no deeper than line index.
    private static int IndentEnd(string[] lines, int index)
    {
        var indent = Indent(lines[index]);
        var end = index;
        for (var i = index + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            if (Indent(lines[i]) <= indent) break;
            end = i;
        }
        return end;
    }

    [GeneratedRegex(@"^\s*package\s+([\w.]+)\s*;?", RegexOptions.Multiline)]
    private static partial Regex JavaPackage();

    [GeneratedRegex(@"\bclass\s+(\w+)")]
    private static partial Regex JavaClass();

    [GeneratedRegex(@"^\s*@(?:Test|ParameterizedTest|RepeatedTest|TestFactory|TestTemplate)\b")]
    private static partial Regex JavaTestAnnotation();

    // Java: "public void shouldAdd() {", Kotlin: "fun `adds two numbers`() {" or "fun adds() {".
    [GeneratedRegex(@"(?:\bfun\s+(?<name>`[^`]+`|\w+)\s*\(|^\s*(?:(?:public|protected|private|static|final|synchronized|abstract)\s+)*[\w<>\[\],.? ]+\s+(?<name>\w+)\s*\()")]
    private static partial Regex JavaMethod();

    // it('title', ...), test("title", ...), it.only(`title`, ...); the title is one string literal.
    [GeneratedRegex(@"^\s*(?:it|test)(?:\.(?:only|skip|concurrent|todo))?\s*\(\s*(?<q>['""`])(?<title>(?:\\.|(?!\k<q>).)*)\k<q>")]
    private static partial Regex JsTest();

    [GeneratedRegex(@"^\s*class\s+(?<name>\w+)")]
    private static partial Regex PythonClass();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(?<name>test\w*)\s*\(")]
    private static partial Regex PythonTest();

    [GeneratedRegex(@"^\s*namespace\s+([\w\\]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex PhpNamespace();

    [GeneratedRegex(@"^\s*(?:final\s+|abstract\s+)*class\s+(\w+)")]
    private static partial Regex PhpClass();

    [GeneratedRegex(@"^\s*(?:public\s+)?(?:static\s+)?function\s+(\w+)\s*\(")]
    private static partial Regex PhpMethod();

    [GeneratedRegex(@"^func\s+(Test\w*)\s*\(\s*\w+\s+\*testing\.T\s*\)")]
    private static partial Regex GoTest();

    [GeneratedRegex(@"^\s*(?:pub\s+)?(?:async\s+)?fn\s+(\w+)\s*\(")]
    private static partial Regex RustFn();
}
