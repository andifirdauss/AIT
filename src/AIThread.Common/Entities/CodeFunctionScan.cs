using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// What one function of a project calls, read by the tools node when the planner read the function or a thread changed
// it. Key identifies the function (path + symbol + signature) across scans; BodyHash tells whether it changed since.
// Lines are as of the scan. Kept on this machine only: it follows the files of this clone.
public class CodeFunctionScan : BaseEntity
{
    public const int MaxPathLength = 800;

    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Key { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    // "Type.Member" for a member, the bare name for a function outside any type.
    public string Symbol { get; set; } = string.Empty;
    public string? Signature { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string BodyHash { get; set; } = string.Empty;

    public FunctionScanStatus Status { get; set; }
    public string? Error { get; set; }
    public string? Model { get; set; }
    public int Tokens { get; set; }

    public List<CodeFunctionCall> Calls { get; set; } = [];
}

// One call inside a function. Target is what the code names ("clsGlobal.showLoading", "POST /Klaim/Save"); the
// Resolved* columns point at its declaration when AIT found it in the project.
public class CodeFunctionCall : BaseEntity
{
    public Guid CodeFunctionScanId { get; set; }
    public CodeFunctionScan? CodeFunctionScan { get; set; }

    public CodeCallKind Kind { get; set; }
    public string Target { get; set; } = string.Empty;
    public int Line { get; set; }

    public string? ResolvedPath { get; set; }
    public string? ResolvedSymbol { get; set; }
    public int? ResolvedLine { get; set; }
}
