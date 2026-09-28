using AiThread.Service.Workspace;

namespace AiThread.Tests;

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
}
