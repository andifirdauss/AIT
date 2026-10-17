namespace AiThread.Common.Entities;

// A module of a project (or, under MemoryLabels.KnowledgeKey, a technology or domain of knowledge memory): the first
// part of every memory label, also the module of business wiki pages. Kept with long memory, keyed by ProjectKey, so
// every developer of the project uses the same list. A merged module stays as a redirect (MergedInto) so names that
// still arrive in its old spelling land on the right module.
public class ProjectModule : BaseEntity
{
    public string ProjectKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // Other names for the module, comma-separated: translations and spellings ("Pengguna,Users" for User).
    public string? Aliases { get; set; }

    // One line on what the module covers; shown to the models that pick modules.
    public string? Description { get; set; }

    public string? MergedInto { get; set; }
}
