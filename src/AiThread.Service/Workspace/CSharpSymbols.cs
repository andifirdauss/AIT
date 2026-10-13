// C# declaration and reference finder for SymbolSearch; Roslyn syntax only (no compilation), so names are matched by
// identifier text, not by bound symbol. Comments, strings and doc comments are never matched.
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AiThread.Service.Workspace;

public sealed record SymbolReference(int Line, string Text, bool IsDeclaration);

public static class CSharpSymbols
{
    private const int MaxSignature = 200;
    private const int MaxLineText = 200;

    public static IReadOnlyList<SymbolDeclaration> FindDeclarations(string content, string name)
    {
        if (string.IsNullOrEmpty(name)) return [];

        var text = SourceText.From(content);
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        var result = new List<SymbolDeclaration>();

        foreach (var token in MatchingIdentifiers(root, name))
        {
            var node = DeclarationOf(token);
            if (node is null) continue;

            result.Add(new SymbolDeclaration(
                LineOf(text, token.SpanStart),
                KindOf(node),
                Signature(text, node, token),
                node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText));
        }

        return result;
    }

    // One entry per line that mentions the name as an identifier.
    public static IReadOnlyList<SymbolReference> FindReferences(string content, string name)
    {
        if (string.IsNullOrEmpty(name)) return [];

        var text = SourceText.From(content);
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        var byLine = new SortedDictionary<int, bool>();

        foreach (var token in MatchingIdentifiers(root, name))
        {
            var line = LineOf(text, token.SpanStart);
            var isDeclaration = DeclarationOf(token) is not null;
            byLine[line] = byLine.TryGetValue(line, out var seen) ? seen || isDeclaration : isDeclaration;
        }

        return byLine
            .Select(entry => new SymbolReference(entry.Key, Truncate(text.Lines[entry.Key - 1].ToString().Trim(), MaxLineText), entry.Value))
            .ToList();
    }

    private static IEnumerable<SyntaxToken> MatchingIdentifiers(SyntaxNode root, string name) =>
        root.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == name);

    // The declaration whose own name is this token (types and members), or null for a plain use.
    private static SyntaxNode? DeclarationOf(SyntaxToken token) => token.Parent switch
    {
        BaseTypeDeclarationSyntax t when t.Identifier == token => t,
        DelegateDeclarationSyntax d when d.Identifier == token => d,
        MethodDeclarationSyntax m when m.Identifier == token => m,
        ConstructorDeclarationSyntax c when c.Identifier == token => c,
        PropertyDeclarationSyntax p when p.Identifier == token => p,
        EventDeclarationSyntax e when e.Identifier == token => e,
        EnumMemberDeclarationSyntax e when e.Identifier == token => e,
        LocalFunctionStatementSyntax f when f.Identifier == token => f,
        VariableDeclaratorSyntax v when v.Identifier == token && v.Parent?.Parent is BaseFieldDeclarationSyntax => v,
        _ => null,
    };

    private static string KindOf(SyntaxNode node) => node switch
    {
        ClassDeclarationSyntax => "class",
        StructDeclarationSyntax => "struct",
        RecordDeclarationSyntax => "record",
        InterfaceDeclarationSyntax => "interface",
        EnumDeclarationSyntax => "enum",
        DelegateDeclarationSyntax => "delegate",
        MethodDeclarationSyntax => "method",
        ConstructorDeclarationSyntax => "constructor",
        PropertyDeclarationSyntax => "property",
        EventDeclarationSyntax => "event",
        EnumMemberDeclarationSyntax => "enum member",
        LocalFunctionStatementSyntax => "local function",
        VariableDeclaratorSyntax { Parent.Parent: EventFieldDeclarationSyntax } => "event",
        _ => "field",
    };

    // The declaration header without attributes, body, accessors or initializer, on one line.
    private static string Signature(SourceText text, SyntaxNode node, SyntaxToken identifier)
    {
        var declaration = node is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax field } ? field : node;
        var start = HeaderStart(declaration);
        var end = node switch
        {
            BaseTypeDeclarationSyntax t => !t.OpenBraceToken.IsMissing ? t.OpenBraceToken.SpanStart
                : t is TypeDeclarationSyntax { SemicolonToken.IsMissing: false } td ? td.SemicolonToken.SpanStart : t.Span.End,
            BaseMethodDeclarationSyntax m => m.Body?.SpanStart ?? m.ExpressionBody?.SpanStart ?? m.Span.End,
            LocalFunctionStatementSyntax f => f.Body?.SpanStart ?? f.ExpressionBody?.SpanStart ?? f.Span.End,
            PropertyDeclarationSyntax p => p.AccessorList?.SpanStart ?? p.ExpressionBody?.SpanStart ?? p.Span.End,
            EventDeclarationSyntax e => e.AccessorList?.SpanStart ?? e.Span.End,
            DelegateDeclarationSyntax d => d.SemicolonToken.IsMissing ? d.Span.End : d.SemicolonToken.SpanStart,
            VariableDeclaratorSyntax => identifier.Span.End,
            _ => node.Span.End,
        };

        if (end <= start) return identifier.ValueText;

        var signature = Regex.Replace(text.ToString(TextSpan.FromBounds(start, end)), @"\s+", " ").Trim().TrimEnd(';', '{').TrimEnd();
        return Truncate(signature, MaxSignature);
    }

    private static int HeaderStart(SyntaxNode node)
    {
        var attributes = node switch
        {
            MemberDeclarationSyntax m => m.AttributeLists,
            LocalFunctionStatementSyntax f => f.AttributeLists,
            _ => default,
        };

        return attributes.Count == 0 ? node.SpanStart : attributes.Last().GetLastToken().GetNextToken().SpanStart;
    }

    private static int LineOf(SourceText text, int position) => text.Lines.GetLineFromPosition(position).LineNumber + 1;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
