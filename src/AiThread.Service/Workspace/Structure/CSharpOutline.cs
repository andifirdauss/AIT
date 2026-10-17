using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiThread.Service.Workspace.Structure;

// Types and members of a C# file from Roslyn syntax only (no compilation): fast, and exact for declarations.
public static partial class CSharpOutline
{
    private const int MaxSignature = 160;

    public static FileOutline Parse(string content)
    {
        var tree = CSharpSyntaxTree.ParseText(content);
        var types = new List<OutlineType>();
        foreach (var node in tree.GetRoot().DescendantNodes(n => n is not BaseMethodDeclarationSyntax and not BasePropertyDeclarationSyntax))
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax type:
                    types.Add(new OutlineType(
                        type.Identifier.ValueText,
                        KindOf(type),
                        NamespaceOf(type),
                        type.Parent is BaseTypeDeclarationSyntax parent ? parent.Identifier.ValueText : null,
                        LineOf(type.Identifier),
                        type.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)),
                        type is TypeDeclarationSyntax withMembers ? Members(withMembers) : [],
                        EndLineOf(type)));
                    break;
                case DelegateDeclarationSyntax @delegate:
                    types.Add(new OutlineType(@delegate.Identifier.ValueText, "delegate", NamespaceOf(@delegate),
                        @delegate.Parent is BaseTypeDeclarationSyntax owner ? owner.Identifier.ValueText : null,
                        LineOf(@delegate.Identifier), false, [], EndLineOf(@delegate)));
                    break;
            }
        }
        return new FileOutline(types, []);
    }

    private static List<OutlineMember> Members(TypeDeclarationSyntax type)
    {
        var members = new List<OutlineMember>();
        foreach (var member in type.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax m:
                    members.Add(new OutlineMember(m.Identifier.ValueText, "method", LineOf(m.Identifier),
                        Signature($"{m.ReturnType} {m.Identifier.ValueText}{m.TypeParameterList}{m.ParameterList}"), EndLineOf(m)));
                    break;
                case ConstructorDeclarationSyntax c:
                    members.Add(new OutlineMember(c.Identifier.ValueText, "constructor", LineOf(c.Identifier),
                        Signature($"{c.Identifier.ValueText}{c.ParameterList}"), EndLineOf(c)));
                    break;
                case PropertyDeclarationSyntax p:
                    members.Add(new OutlineMember(p.Identifier.ValueText, "property", LineOf(p.Identifier), Signature($"{p.Type} {p.Identifier.ValueText}"), EndLineOf(p)));
                    break;
                case EventDeclarationSyntax e:
                    members.Add(new OutlineMember(e.Identifier.ValueText, "event", LineOf(e.Identifier), Signature($"event {e.Type} {e.Identifier.ValueText}"), EndLineOf(e)));
                    break;
                case EventFieldDeclarationSyntax ef:
                    foreach (var variable in ef.Declaration.Variables)
                        members.Add(new OutlineMember(variable.Identifier.ValueText, "event", LineOf(variable.Identifier),
                            Signature($"event {ef.Declaration.Type} {variable.Identifier.ValueText}"), EndLineOf(ef)));
                    break;
            }
        }
        return members;
    }

    private static string KindOf(BaseTypeDeclarationSyntax type) => type switch
    {
        RecordDeclarationSyntax => "record",
        ClassDeclarationSyntax => "class",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        EnumDeclarationSyntax => "enum",
        _ => "type"
    };

    // Nested namespace blocks join outermost first: namespace A { namespace B { … } } → "A.B".
    private static string? NamespaceOf(SyntaxNode node)
    {
        var names = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Select(n => n.Name.ToString()).Reverse().ToList();
        return names.Count == 0 ? null : string.Join('.', names);
    }

    private static int LineOf(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    // Span leaves out leading comments and attributes' trivia, so the end is the closing brace or semicolon.
    private static int EndLineOf(SyntaxNode node) => node.SyntaxTree.GetLineSpan(node.Span).EndLinePosition.Line + 1;

    private static string Signature(string text)
    {
        var line = Whitespace().Replace(text, " ").Trim();
        return line.Length > MaxSignature ? line[..MaxSignature] + "…" : line;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
