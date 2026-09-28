namespace AiThread.Common.Entities;

// A codebase seen by the proxy, uniquely identified by its .sln/.slnx file.
public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string SolutionPath { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public ICollection<ThreadRun> Threads { get; set; } = [];
}
