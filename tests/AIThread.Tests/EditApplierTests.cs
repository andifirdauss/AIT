using AIThread.Service.Workspace;

namespace AIThread.Tests;

public class EditApplierTests
{
    private static TextFileState File(string content) => new(true, content, false, "\n");

    [Fact]
    public void Replaces_exact_text()
    {
        var result = EditApplier.Apply(File("a\nb\nc\n"), [new EditBlock("b", "B")]);

        Assert.True(result.Success);
        Assert.Equal("a\nB\nc\n", result.Content);
    }

    [Fact]
    public void Rejects_ambiguous_match()
    {
        var result = EditApplier.Apply(File("x\n}\ny\n}\n"), [new EditBlock("}", "};")]);

        Assert.False(result.Success);
        Assert.Contains("2 times", Assert.Single(result.Errors));
    }

    [Fact]
    public void Tolerates_trailing_whitespace_differences()
    {
        var result = EditApplier.Apply(File("int a = 1;   \nint b = 2;\n"), [new EditBlock("int a = 1;\nint b = 2;", "int a = 10;\nint b = 20;")]);

        Assert.True(result.Success);
        Assert.Equal("int a = 10;\nint b = 20;\n", result.Content);
    }

    [Fact]
    public void Reindents_when_model_got_indentation_wrong()
    {
        var content = "class A\n{\n    void M()\n    {\n        Call();\n    }\n}\n";
        var result = EditApplier.Apply(File(content), [new EditBlock("Call();", "Call();\nOther();")]);
        Assert.True(result.Success);
        Assert.Contains("        Call();\nOther();", result.Content); // exact substring match keeps the model text as-is

        var indented = EditApplier.Apply(File(content), [new EditBlock("void M()\n{\n    Call();\n}", "void M()\n{\n    Call();\n    Other();\n}")]);
        Assert.True(indented.Success);
        Assert.Contains("    void M()\n    {\n        Call();\n        Other();\n    }", indented.Content);
    }

    [Fact]
    public void Whole_file_block_creates_new_file_but_not_overwrite()
    {
        var created = EditApplier.Apply(new TextFileState(false, "", false, "\n"), [new EditBlock("", "SELECT 1;")]);
        Assert.True(created.Success);
        Assert.True(created.CreatesFile);
        Assert.Equal("SELECT 1;\n", created.Content);

        var overwrite = EditApplier.Apply(File("existing\n"), [new EditBlock("", "new")]);
        Assert.False(overwrite.Success);
    }

    [Fact]
    public void Is_all_or_nothing()
    {
        var result = EditApplier.Apply(File("a\nb\n"), [new EditBlock("a", "A"), new EditBlock("missing", "x")]);

        Assert.False(result.Success);
        Assert.Contains("not found", Assert.Single(result.Errors));
    }

    [Fact]
    public void Deletes_lines_with_empty_replacement()
    {
        var result = EditApplier.Apply(File("keep\nremove me   \nkeep too\n"), [new EditBlock("remove me", "")]);

        Assert.True(result.Success);
        Assert.Equal("keep\n   \nkeep too\n", result.Content);
    }
}
