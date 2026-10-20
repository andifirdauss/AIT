using AIThread.Service.Orchestration;

namespace AIThread.Tests;

public class TranscriptDigestTests
{
    private const string Cwd = @"D:\Proj";

    private static readonly string[] Transcript =
    [
        """{"type":"user","message":{"role":"user","content":"<system-reminder>CLAUDE.md says hi</system-reminder>Fix save distribution in PositionController"}}""",
        """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"private reasoning"},{"type":"text","text":"Looking at the service."},{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"D:\\Proj\\Services\\PositionService.cs"}}]}}""",
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"huge file content"}]}}""",
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t2","name":"Edit","input":{"file_path":"D:\\Proj\\Services\\PositionService.cs","old_string":"a","new_string":"b"}}]}}""",
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t2","is_error":true,"content":"String to replace not found"}]}}""",
        """{"type":"assistant","isSidechain":true,"message":{"content":[{"type":"text","text":"subagent chatter"}]}}""",
        """{"type":"user","isMeta":true,"message":{"content":"Caveat: local command output"}}""",
        """{"type":"user","message":{"content":"<command-name>/model</command-name>"}}""",
        """{"type":"user","isCompactSummary":true,"message":{"content":"Summary of earlier work"}}""",
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t3","name":"Bash","input":{"command":"dotnet test","description":"Run tests"}}]}}""",
        "not json at all",
        ""
    ];

    [Fact]
    public void Keeps_requests_replies_and_tool_calls_but_not_results_thinking_or_subagents()
    {
        var summary = TranscriptDigest.Build(Transcript, Cwd);

        Assert.Contains("[user] Fix save distribution in PositionController", summary.Digest);
        Assert.Contains("[assistant] Looking at the service.", summary.Digest);
        Assert.Contains("[tool] Read Services/PositionService.cs", summary.Digest);
        Assert.Contains("[tool] Edit Services/PositionService.cs", summary.Digest);
        Assert.Contains("[tool error] String to replace not found", summary.Digest);
        Assert.Contains("[compacted conversation summary]\nSummary of earlier work", summary.Digest);
        Assert.Contains("[tool] Bash dotnet test", summary.Digest);

        Assert.DoesNotContain("CLAUDE.md", summary.Digest);
        Assert.DoesNotContain("private reasoning", summary.Digest);
        Assert.DoesNotContain("huge file content", summary.Digest);
        Assert.DoesNotContain("subagent chatter", summary.Digest);
        Assert.DoesNotContain("Caveat", summary.Digest);
        Assert.DoesNotContain("<command-name>", summary.Digest);

        Assert.Equal(["Fix save distribution in PositionController"], summary.Requests);
        Assert.Equal(["Services/PositionService.cs"], summary.FilesChanged);
    }

    [Fact]
    public void Oversized_transcript_keeps_the_first_request_and_the_latest_part()
    {
        var lines = new List<string> { """{"type":"user","message":{"content":"The original goal"}}""" };
        for (var i = 0; i < 400; i++)
            lines.Add("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"reply " + i + " " + new string('x', 500) + "\"}]}}");

        var digest = TranscriptDigest.Build(lines, Cwd).Digest;

        Assert.True(digest.Length <= TranscriptDigest.MaxDigestChars);
        Assert.StartsWith("[user] The original goal", digest);
        Assert.Contains("earlier part of the session omitted", digest);
        Assert.Contains("reply 399", digest);
        Assert.DoesNotContain("reply 0 ", digest);
    }
}
