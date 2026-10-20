namespace AIThread.Common.Enums;

// Where the key that identifies a project across machines (and so its shared memory) comes from.
public enum ProjectKeySource
{
    // .aithread/project.json committed in the repository.
    File = 1,

    // Typed in the Project menu.
    Manual = 2,

    // The repository's git remote (origin first).
    GitRemote = 3,

    // Fallback from the root path: valid on this machine only, so its memory is not shared.
    Local = 4
}
