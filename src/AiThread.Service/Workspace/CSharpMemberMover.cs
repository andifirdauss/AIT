using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AiThread.Service.Workspace;

// Target is null when the move failed (Error says why). Deduplicated: members the target already declared with the same
// signature; they were only removed from the source. Signatures: the declarations of the moved members, without bodies.
public sealed record MemberMoveResult(
    string? Error,
    string Source,
    string? Target,
    IReadOnlyList<string> Moved,
    IReadOnlyList<string> Deduplicated,
    IReadOnlyList<string> Signatures)
{
    public bool Success => Error is null;

    public static MemberMoveResult Failed(string error) => new(error, string.Empty, null, [], [], []);
}

// Moves C# members (methods, properties, fields, events, indexers, nested types) from one file to another without a
// model: the member text is copied verbatim with its doc comments, the source type and its containing types become
// partial, and a new target file gets the source's usings, namespace and type headers. A model would have to repeat
// every moved line in its output, which large files do not survive. Content uses "\n" line endings (TextFileIo).
public static class CSharpMemberMover
{
    public static MemberMoveResult Move(string source, string? target, IReadOnlyCollection<string> requestedNames)
    {
        var names = requestedNames.Select(NormalizeName).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0) return MemberMoveResult.Failed("No member names to move.");

        var sourceText = SourceText.From(source);
        var sourceRoot = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(sourceText).GetRoot();

        var types = sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();
        var missing = names.Where(n => !types.Any(t => t.Members.Any(m => NamesOf(m).Contains(n)))).ToList();
        if (missing.Count > 0)
            return MemberMoveResult.Failed($"Not found as a member of a class, struct, record or interface in the source file: {string.Join(", ", missing)}.");

        // The type that declares every name; a class beats an interface in the same file that declares the same names.
        var owners = types.Where(t => names.All(n => t.Members.Any(m => NamesOf(m).Contains(n)))).ToList();
        if (owners.Count > 1 && owners.Count(t => t is not InterfaceDeclarationSyntax) == 1) owners = owners.Where(t => t is not InterfaceDeclarationSyntax).ToList();
        if (owners.Count != 1)
        {
            var declaring = types.Where(t => t.Members.Any(m => NamesOf(m).Any(names.Contains))).Select(t => t.Identifier.ValueText).Distinct();
            return MemberMoveResult.Failed(owners.Count == 0
                ? $"The members belong to more than one type ({string.Join(", ", declaring)}); move the members of one type per task."
                : $"More than one type declares these members ({string.Join(", ", owners.Select(o => o.Identifier.ValueText))}); move them from a file with one such type.");
        }
        var owner = owners[0];
        var matches = owner.Members.Where(m => NamesOf(m).Any(names.Contains)).ToList();

        foreach (var field in matches.OfType<BaseFieldDeclarationSyntax>())
        {
            var variables = field.Declaration.Variables.Select(v => v.Identifier.ValueText).ToList();
            if (!variables.All(names.Contains))
                return MemberMoveResult.Failed($"The field declaration '{field.Declaration}' declares {string.Join(", ", variables)}; move all of them together or none.");
        }

        var members = matches.OrderBy(m => m.SpanStart).ToList();
        var targetRoot = target is null ? null : (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(target).GetRoot();
        TypeDeclarationSyntax? targetType = null;
        if (targetRoot is not null)
        {
            var ownerName = QualifiedName(owner);
            targetType = targetRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().FirstOrDefault(t => QualifiedName(t) == ownerName);
            if (targetType is null)
                return MemberMoveResult.Failed($"The target file has no declaration of '{DisplayName(owner)}' to add the members to. Move them to a new file instead.");
            if (targetType.OpenBraceToken.IsMissing || targetType.CloseBraceToken.IsMissing)
                return MemberMoveResult.Failed($"The declaration of '{DisplayName(owner)}' in the target file has no body.");
        }

        var existing = targetType is null ? [] : targetType.Members.SelectMany(Keys).ToHashSet(StringComparer.Ordinal);
        var moved = new List<string>();
        var deduplicated = new List<string>();
        var texts = new List<string>();
        var signatures = new List<string>();
        var changes = new List<TextChange>();

        foreach (var member in members)
        {
            var start = RemovalStart(member);
            changes.Add(new TextChange(TextSpan.FromBounds(start, member.FullSpan.End), string.Empty));
            var label = string.Join(", ", NamesOf(member));
            if (Keys(member).All(existing.Contains))
            {
                deduplicated.Add(label);
                continue;
            }
            moved.Add(label);
            signatures.Add(Signature(member));
            texts.Add(TrimLeadingBlankLines(sourceText.ToString(TextSpan.FromBounds(start, member.FullSpan.End))));
        }

        // `partial` must stand right before the class/struct/record/interface keyword.
        foreach (var type in owner.AncestorsAndSelf().OfType<TypeDeclarationSyntax>())
            if (!type.Modifiers.Any(SyntaxKind.PartialKeyword))
                changes.Add(new TextChange(new TextSpan(type.Keyword.SpanStart, 0), "partial "));

        var newSource = sourceText.WithChanges(changes.OrderBy(c => c.Span.Start).ThenBy(c => c.Span.End)).ToString();
        string newTarget;
        if (targetType is null)
        {
            newTarget = NewFile(sourceRoot, sourceText, owner, texts);
        }
        else if (texts.Count == 0)
        {
            newTarget = target!;
        }
        else
        {
            var targetText = SourceText.From(target!);
            var close = targetType.CloseBraceToken;
            var line = targetText.Lines.GetLineFromPosition(close.SpanStart);
            var onOwnLine = string.IsNullOrWhiteSpace(targetText.ToString(TextSpan.FromBounds(line.Start, close.SpanStart)));
            var body = string.Join("\n", texts);
            var insertion = (targetType.Members.Count > 0 ? "\n" : string.Empty) + body;
            newTarget = onOwnLine
                ? targetText.WithChanges(new TextChange(new TextSpan(line.Start, 0), insertion)).ToString()
                : targetText.WithChanges(new TextChange(new TextSpan(close.SpanStart, 0), "\n" + insertion)).ToString();
        }

        return new MemberMoveResult(null, newSource, newTarget, moved, deduplicated, signatures);
    }

    // True when `content` declares `memberName` directly in a type named `typeName` (simple name, as in compiler messages).
    public static bool Declares(string content, string typeName, string memberName)
    {
        var type = SimpleTypeName(typeName);
        return CSharpSyntaxTree.ParseText(content).GetRoot().DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(t => t.Identifier.ValueText == type)
            .Any(t => t.Members.Any(m => NamesOf(m).Contains(memberName)));
    }

    // "Save", "Save()", "Save(int id)" and "TransactionRemovalService.Save" all mean the members named Save.
    public static string NormalizeName(string name)
    {
        var trimmed = name.Trim().Trim('`');
        var paren = trimmed.IndexOf('(');
        if (paren >= 0) trimmed = trimmed[..paren];
        var generic = trimmed.IndexOf('<');
        if (generic >= 0) trimmed = trimmed[..generic];
        var dot = trimmed.LastIndexOf('.');
        return (dot >= 0 ? trimmed[(dot + 1)..] : trimmed).Trim();
    }

    private static string SimpleTypeName(string typeName)
    {
        var name = typeName.Trim();
        var generic = name.IndexOf('<');
        if (generic >= 0) name = name[..generic];
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private static IReadOnlyList<string> NamesOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => [m.Identifier.ValueText],
        PropertyDeclarationSyntax p => [p.Identifier.ValueText],
        EventDeclarationSyntax e => [e.Identifier.ValueText],
        BaseFieldDeclarationSyntax f => f.Declaration.Variables.Select(v => v.Identifier.ValueText).ToList(),
        IndexerDeclarationSyntax => ["this"],
        BaseTypeDeclarationSyntax t => [t.Identifier.ValueText],
        DelegateDeclarationSyntax d => [d.Identifier.ValueText],
        _ => []
    };

    // What makes two members collide (CS0111/CS0102): the name, and for methods and indexers the parameter types.
    private static IEnumerable<string> Keys(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => [$"M:{m.ExplicitInterfaceSpecifier}{m.Identifier.ValueText}`{m.TypeParameterList?.Parameters.Count ?? 0}({Parameters(m.ParameterList)})"],
        IndexerDeclarationSyntax i => [$"I:{i.ExplicitInterfaceSpecifier}({Parameters(i.ParameterList)})"],
        _ => NamesOf(member).Select(n => "N:" + n)
    };

    private static string Parameters(BaseParameterListSyntax list) =>
        string.Join(",", list.Parameters.Select(p =>
            string.Concat(p.Modifiers.Where(m => m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword) || m.IsKind(SyntaxKind.InKeyword) || m.IsKind(SyntaxKind.ParamsKeyword)).Select(m => m.Text + " "))
            + string.Concat((p.Type?.ToString() ?? string.Empty).Where(c => !char.IsWhiteSpace(c)))));

    // The declaration without its body, for diffs and summaries.
    private static string Signature(MemberDeclarationSyntax member)
    {
        var text = member.ToString();
        var end = member switch
        {
            BaseMethodDeclarationSyntax { Body: { } body } => body.SpanStart,
            BaseMethodDeclarationSyntax { ExpressionBody: { } arrow } => arrow.SpanStart,
            BasePropertyDeclarationSyntax { AccessorList: { } accessors } => accessors.SpanStart,
            BaseTypeDeclarationSyntax { OpenBraceToken.IsMissing: false } type => type.OpenBraceToken.SpanStart,
            _ => member.Span.End
        };
        var signature = text[..(end - member.SpanStart)];
        var attributes = member.AttributeLists.Count > 0 ? member.AttributeLists.Last().Span.End - member.SpanStart : 0;
        return string.Join(' ', signature[attributes..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd(';');
    }

    // The member's own text starts after the last preprocessor directive in its leading trivia: a `#region` or `#if`
    // in front of it belongs to the surrounding code, which keeps it balanced.
    private static int RemovalStart(MemberDeclarationSyntax member)
    {
        var start = member.FullSpan.Start;
        foreach (var trivia in member.GetLeadingTrivia())
            if (trivia.IsDirective || trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                start = trivia.FullSpan.End;
        return start;
    }

    // Namespaces and type names from the outside in, e.g. "Company.Services.Outer`1.Inner".
    private static string QualifiedName(TypeDeclarationSyntax type) =>
        string.Join(".", type.AncestorsAndSelf().Reverse().Select(n => n switch
        {
            BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
            TypeDeclarationSyntax t => $"{t.Identifier.ValueText}`{t.TypeParameterList?.Parameters.Count ?? 0}",
            _ => null
        }).OfType<string>());

    private static string DisplayName(TypeDeclarationSyntax type) =>
        string.Join(".", type.AncestorsAndSelf().Reverse().Select(n => n switch
        {
            BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
            TypeDeclarationSyntax t => $"{t.Identifier.ValueText}{t.TypeParameterList}",
            _ => null
        }).OfType<string>());

    // Usings, namespace and partial type headers of the source, with the members inside. Attributes, base types and
    // constraints stay on the source's declaration (repeating them on another part is an error or redundant).
    private static string NewFile(CompilationUnitSyntax root, SourceText text, TypeDeclarationSyntax owner, IReadOnlyList<string> members)
    {
        var builder = new StringBuilder();
        AppendUsings(builder, text, root.Externs, root.Usings);

        var containers = owner.AncestorsAndSelf().Where(n => n is BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax).Reverse().ToList();
        var closers = new Stack<string>();
        foreach (var container in containers)
        {
            var indent = IndentOf(text, container);
            switch (container)
            {
                case FileScopedNamespaceDeclarationSyntax ns:
                    builder.Append($"namespace {ns.Name};\n\n");
                    AppendUsings(builder, text, ns.Externs, ns.Usings);
                    break;
                case NamespaceDeclarationSyntax ns:
                    builder.Append($"{indent}namespace {ns.Name}\n{indent}{{\n");
                    AppendUsings(builder, text, ns.Externs, ns.Usings);
                    closers.Push($"{indent}}}\n");
                    break;
                case TypeDeclarationSyntax type:
                    var modifiers = type.Modifiers.Select(m => m.Text).ToList();
                    if (!modifiers.Contains("partial")) modifiers.Add("partial");
                    var keyword = type.Keyword.Text;
                    if (type is RecordDeclarationSyntax record && !record.ClassOrStructKeyword.IsKind(SyntaxKind.None)) keyword += " " + record.ClassOrStructKeyword.Text;
                    var braceOnSameLine = text.Lines.GetLineFromPosition(type.OpenBraceToken.SpanStart).LineNumber == text.Lines.GetLineFromPosition(type.Identifier.SpanStart).LineNumber;
                    builder.Append($"{indent}{string.Join(' ', modifiers)} {keyword} {type.Identifier.ValueText}{type.TypeParameterList}")
                        .Append(braceOnSameLine ? " {\n" : $"\n{indent}{{\n");
                    closers.Push($"{indent}}}\n");
                    break;
            }
        }

        builder.Append(string.Join("\n", members));
        while (closers.Count > 0) builder.Append(closers.Pop());
        return builder.ToString();
    }

    // Copies the using block as written (aliases, statics and any #if around them included), indented as in the source.
    private static void AppendUsings(StringBuilder builder, SourceText text, SyntaxList<ExternAliasDirectiveSyntax> externs, SyntaxList<UsingDirectiveSyntax> usings)
    {
        var nodes = externs.Cast<SyntaxNode>().Concat(usings).ToList();
        if (nodes.Count == 0) return;
        var start = text.Lines.GetLineFromPosition(nodes[0].SpanStart).Start;
        builder.Append(text.ToString(TextSpan.FromBounds(start, nodes[^1].Span.End))).Append("\n\n");
    }

    private static string IndentOf(SourceText text, SyntaxNode node)
    {
        var line = text.Lines.GetLineFromPosition(node.SpanStart);
        var prefix = text.ToString(TextSpan.FromBounds(line.Start, node.SpanStart));
        return string.Concat(prefix.TakeWhile(c => c is ' ' or '\t'));
    }

    private static string TrimLeadingBlankLines(string text)
    {
        while (true)
        {
            var newline = text.IndexOf('\n');
            if (newline < 0 || !string.IsNullOrWhiteSpace(text[..newline])) break;
            text = text[(newline + 1)..];
        }
        return text.EndsWith('\n') ? text : text + "\n";
    }
}
