using AIThread.Service.Memory;

namespace AIThread.Tests;

// The files a request names, when a repository holds the same names twice (a template and its sample).
public class ReferenceAreasTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    // The sample comes first on disk ("sample" < "src") and has more matching files, but only the template has every
    // name the request uses.
    private string[] Files()
    {
        _workspace.Write("Clean.slnx", "<Solution />");
        _workspace.Write("sample/Sample.slnx", "<Solution />");
        string[] files =
        [
            "sample/src/Sample.Core/ContributorId.cs",
            "sample/src/Sample.UseCases/ContributorDTO.cs",
            "sample/src/Sample.Web/ContributorRecord.cs",
            "sample/src/Sample.Web/Projects/ContributorRecord.Extra.cs",
            "src/Clean.Core/ContributorId.cs",
            "src/Clean.Core/ContributorStatus.cs",
            "src/Clean.UseCases/ContributorDTO.cs",
            "src/Clean.Web/ContributorRecord.cs"
        ];
        foreach (var file in files) _workspace.Write(file, "class X {}");
        return files;
    }

    private static readonly string[] Names = ["ContributorStatus", "ContributorDto", "ContributorRecord", "ContributorId"];

    [Fact]
    public void The_solution_holding_most_of_the_names_comes_first()
    {
        var areas = ReferenceAreas.Group(_workspace.Root, Files(), Names);

        Assert.Equal([".", "sample"], areas.Select(a => a.Folder));
        Assert.Equal(("Clean.slnx", 4), (areas[0].Solution, areas[0].Names.Count));
        Assert.Equal(("Sample.slnx", 3), (areas[1].Solution, areas[1].Names.Count));
        Assert.StartsWith("src/", ReferenceAreas.Order(_workspace.Root, Files(), Names)[0]);
    }

    [Fact]
    public void The_brief_names_each_solution_and_what_it_lacks()
    {
        var brief = ReferenceAreas.Brief(_workspace.Root, Files(), Names, canAsk: true);

        Assert.NotNull(brief);
        Assert.Contains("- the root (Clean.slnx): 4 file(s), has ContributorStatus, ContributorDto, ContributorRecord, ContributorId", brief);
        Assert.Contains("- sample (Sample.slnx): 4 file(s), has ContributorDto, ContributorRecord, ContributorId; lacks ContributorStatus", brief);
        Assert.Contains("ask the developer", brief);
    }

    [Fact]
    public void One_solution_needs_no_brief_and_keeps_its_order()
    {
        _workspace.Write("App.slnx", "<Solution />");
        string[] files = ["src/B/Order.cs", "src/A/Order.cs"];
        foreach (var file in files) _workspace.Write(file, "class X {}");

        Assert.Null(ReferenceAreas.Brief(_workspace.Root, files, ["Order"], canAsk: true));
        Assert.Equal(files, ReferenceAreas.Order(_workspace.Root, files, ["Order"]));
    }
}
