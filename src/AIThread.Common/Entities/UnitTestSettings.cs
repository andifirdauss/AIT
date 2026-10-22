using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// How a project uses its unit test catalog, shared by every developer of the project (keyed by ProjectKey). A project
// without a row uses the defaults.
public class UnitTestSettings : BaseEntity
{
    public string ProjectKey { get; set; } = string.Empty;

    public UnitTestMode Mode { get; set; } = UnitTestMode.Relevant;
    public UnitTestAuthoring Authoring { get; set; } = UnitTestAuthoring.OnRequest;

    // Keep the source of passing tests in the catalog, to restore them on machines where they are missing. Off for
    // projects whose code must not be stored outside the repository.
    public bool KeepSnapshots { get; set; } = true;

    // Null: the build timeout of the Setting menu.
    public int? TimeoutSeconds { get; set; }

    public string? UpdatedBy { get; set; }
}
