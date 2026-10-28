using AIThread.Service.Workspace;

namespace AIThread.Tests;

public class EditBlockParserTests
{
    [Fact]
    public void Parses_blocks_and_summary()
    {
        var output = """
            Some preamble the model should not have written.
            <<<<<<< SEARCH
                name: $('#txtName').val()
            =======
                name: $('#txtName').val(),
                description: $('#txtDescription').val()
            >>>>>>> REPLACE
            <<<<<<< SEARCH
            var a = 1;
            =======
            var a = 2;
            >>>>>>> REPLACE
            SUMMARY: Added description to the payload.
            """;

        var parsed = EditBlockParser.Parse(output);

        Assert.Empty(parsed.Errors);
        Assert.Equal(2, parsed.Blocks.Count);
        Assert.Equal("    name: $('#txtName').val()", parsed.Blocks[0].Search);
        Assert.Contains("description", parsed.Blocks[0].Replace);
        Assert.Equal("Added description to the payload.", parsed.Summary);
    }

    [Fact]
    public void Empty_search_means_whole_file()
    {
        var parsed = EditBlockParser.Parse("<<<<<<< SEARCH\n=======\nSELECT 1;\n>>>>>>> REPLACE\nSUMMARY: created");

        var block = Assert.Single(parsed.Blocks);
        Assert.True(block.IsWholeFile);
        Assert.Equal("SELECT 1;", block.Replace);
    }

    [Fact]
    public void Unclosed_block_is_reported()
    {
        var parsed = EditBlockParser.Parse("<<<<<<< SEARCH\nfoo\n=======\nbar");

        Assert.Empty(parsed.Blocks);
        Assert.NotEmpty(parsed.Errors);
    }

    [Fact]
    public void Handles_crlf_output()
    {
        var parsed = EditBlockParser.Parse("<<<<<<< SEARCH\r\na\r\n=======\r\nb\r\n>>>>>>> REPLACE\r\n");

        var block = Assert.Single(parsed.Blocks);
        Assert.Equal("a", block.Search);
        Assert.Equal("b", block.Replace);
    }

    [Theory]
    [InlineData("<<<<<<< SEARCH (line 69)\na\n=======\nb\n>>>>>>> REPLACE")]
    [InlineData("**<<<<<<< SEARCH**\na\n**=======**\nb\n**>>>>>>> REPLACE**")]
    [InlineData("- <<<<<<< SEARCH\na\n- =======\nb\n- >>>>>>> REPLACE")]
    [InlineData("<<<<<<< ORIGINAL\na\n=======\nb\n>>>>>>> UPDATED")]
    [InlineData("<<<<<<< SEARCH\na\n=======\nb >>>>>>> REPLACE")]
    [InlineData("<<<<<<< SEARCH\n```csharp\na\n```\n=======\n```csharp\nb\n```\n>>>>>>> REPLACE")]
    public void Marker_variants_of_cheap_models_are_read_as_meant(string output)
    {
        var parsed = EditBlockParser.Parse(output);

        Assert.Empty(parsed.Errors);
        var block = Assert.Single(parsed.Blocks);
        Assert.Equal("a", block.Search);
        Assert.Equal("b", block.Replace);
    }

    [Fact]
    public void A_broken_block_next_to_a_good_one_is_an_error_not_a_half_edit()
    {
        var parsed = EditBlockParser.Parse("<<<<<<< SEARCH\na\n=======\nb\n>>>>>>> REPLACE\n<<<<<<< SEARCH\nc\n=======\nd");

        Assert.Single(parsed.Blocks);
        Assert.Contains(parsed.Errors, e => e.Contains("Block #2") && e.Contains("not closed"));
    }

    [Fact]
    public void A_missing_divider_is_named()
    {
        var parsed = EditBlockParser.Parse("<<<<<<< SEARCH\na\n>>>>>>> REPLACE");

        Assert.Empty(parsed.Blocks);
        Assert.Contains(parsed.Errors, e => e.Contains("divider"));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("@@ -1,2 +1,2 @@\n-a\n+b", "unified diff")]
    [InlineData("```csharp\nclass A {}\n```", "code block without")]
    [InlineData("<<<< SEARCH\na\n====\nb\n>>>> REPLACE", "malformed")]
    [InlineData("I changed the method as requested.", "only text")]
    public void An_output_without_blocks_says_why(string output, string expected)
    {
        var parsed = EditBlockParser.Parse(output);

        Assert.Empty(parsed.Blocks);
        Assert.Contains(expected, Assert.Single(parsed.Errors));
    }
}
