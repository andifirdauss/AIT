using AiThread.Service.Interfaces;
using AiThread.Service.Workspace.Structure;

namespace AiThread.Service.Orchestration;

// Planner tools code_outline and function_calls, and the outline of the files a request names in the planner brief:
// the skeleton of the code (types, members and their line ranges) and the calls the tools node recorded, so the
// planner reads only the lines it needs. A file is outlined from disk on every call; the project snapshot (Struktur
// kode) is only used to list a folder and to find a class by name.
public sealed partial class CodeSkeletonTools(ICodeStructureService structure, IFunctionCallStore store, IFunctionCallQueue queue) : ICodeSkeletonTools
{
    private const int MaxOutputChars = 24_000;
    private const int MaxFolderFiles = 300;
    private const int MaxFunctionsListed = 12;
    private const int MaxClassFiles = 6;
    private const int MaxClassMatches = 40;
    private const int MaxOverloads = 5;
    private const int MaxCallers = 40;

    private sealed record OutlinedFile(string Language, string[] Lines, FileOutline Outline);
}
