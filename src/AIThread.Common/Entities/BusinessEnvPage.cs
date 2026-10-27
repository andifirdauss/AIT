namespace AIThread.Common.Entities;

// A business wiki page as it holds for one environment: the newest revision of the working page (BusinessPage) whose
// source threads have all reached that environment (BusinessEnvWriter). Version is that revision's version. Kept next
// to the wiki, keyed by ProjectKey, so every developer sees the same page per environment.
public class BusinessEnvPage : BaseEntity
{
    public string ProjectKey { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public Guid PageId { get; set; }

    public string Module { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string Content { get; set; } = string.Empty;
    public int Version { get; set; }

    // The threads of every revision up to Version, comma-separated.
    public string? SourceThreadIds { get; set; }
}
