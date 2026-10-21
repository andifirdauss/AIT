using AIThread.Service.Orchestration;

namespace AIThread.Tests;

public class InvestigationLogTests
{
    [Fact]
    public void Read_ranges_of_a_file_are_merged()
    {
        var log = new InvestigationLog();
        log.Read("Views/User/Index.cshtml", 40, 80);
        log.Read("views/user/index.cshtml", 10, 39);
        log.Read("Views/User/Index.cshtml", 120, 150);
        log.Read("Views/User/Index.cshtml", 140, null);

        var file = Assert.Single(log.Files);
        Assert.Equal([new ReadRange(10, 80), new ReadRange(120, null)], file.Ranges);
        Assert.Contains("- `Views/User/Index.cshtml` (10-80, 120-…)", log.Brief(2_000));
    }

    [Fact]
    public void Lookups_keep_the_start_of_their_result_once()
    {
        var log = new InvestigationLog();
        log.Lookup(PlannerTools.FindSymbol, "`btnSave`", "Views/User/Index.cshtml:42 element btnSave\nwwwroot/js/user.js:15 usage\n\nmore\nmore\nlast");
        log.Lookup(PlannerTools.FindSymbol, "`btnSave`", "again");
        log.Lookup(PlannerTools.CodeOutline, "`Services/UserService.cs`", null);

        Assert.Equal(2, log.Lookups.Count);
        var brief = log.Brief(2_000);
        Assert.Contains("- find_symbol `btnSave` → Views/User/Index.cshtml:42 element btnSave; wwwroot/js/user.js:15 usage; more; more", brief);
        Assert.DoesNotContain("last", brief);
        Assert.EndsWith("- code_outline `Services/UserService.cs`", brief);
    }

    [Fact]
    public void Json_round_trips_and_an_empty_log_stores_nothing()
    {
        Assert.Null(new InvestigationLog().ToJson());

        var log = new InvestigationLog();
        log.Read("a.cs", 1, 20);
        log.Finding("Where is button A declared?", "Views/A.cshtml:12, type=\"button\"");

        var copy = InvestigationLog.FromJson(log.ToJson());

        Assert.NotNull(copy);
        Assert.Equal(["a.cs"], copy.FilePaths());
        Assert.Contains("- Q: Where is button A declared?\n  A: Views/A.cshtml:12, type=\"button\"", copy.Brief(2_000));
        Assert.Null(InvestigationLog.FromJson("not json"));
    }

    [Fact]
    public void Brief_is_clipped_after_the_files()
    {
        var log = new InvestigationLog();
        log.Read("a.cs", 1, 20);
        log.Finding("question", new string('x', 2_000));

        var brief = log.Brief(200);

        Assert.StartsWith("Files read (line ranges):\n- `a.cs` (1-20)", brief);
        Assert.EndsWith(" …", brief);
    }
}
