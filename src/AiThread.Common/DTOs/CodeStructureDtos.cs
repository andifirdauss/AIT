namespace AiThread.Common.DTOs;

// Project > Detail → Struktur kode: folders and declarations of a project, read from the files without compiling.
// ParsedFiles: files whose declarations were read; ThirdPartyFiles: listed only (thirdpartylibs.xml, minified, builds).
public record CodeStructureSummaryDto(
    int Folders,
    int Files,
    int ParsedFiles,
    int ThirdPartyFiles,
    int Types,
    int Members,
    IReadOnlyList<StructureLanguageDto> Languages,
    long ScanMs,
    int Reparsed,
    DateTime ScannedAt,
    bool Truncated,
    int FunctionsWithCalls = 0);

public record StructureLanguageDto(string Language, int Files, int Types);

public record StructureFolderDto(
    string Path,
    IReadOnlyList<StructureFolderEntryDto> Folders,
    IReadOnlyList<StructureFileEntryDto> Files,
    int HiddenFiles);

// ThirdParty: every file below the folder is third-party code.
public record StructureFolderEntryDto(string Name, string Path, int Files, int Types, bool ThirdParty);

// Parsed: false when the language is supported but the file was skipped (third-party, too large, unreadable).
public record StructureFileEntryDto(string Name, string Path, string? Language, int Types, int Members, bool ThirdParty, bool Parsed);

public record StructureFileDto(
    string Path,
    string? Language,
    bool ThirdParty,
    bool Parsed,
    IReadOnlyList<StructureTypeDto> Types,
    IReadOnlyList<StructureMemberDto> Functions);

public record StructureTypeDto(
    string Name,
    string Kind,
    string? Namespace,
    string? Container,
    int Line,
    bool IsPartial,
    IReadOnlyList<StructureMemberDto> Members);

// Path: set where members of one type come from several files (partial classes). Key: the function's id for
// /structure/calls (methods and functions with a body). Calls: how many calls the tools node found in it; null while
// it was never read (the planner did not read it, no thread changed it).
public record StructureMemberDto(string Name, string Kind, int Line, string? Signature, string? Path = null, string? Key = null, int? Calls = null, int EndLine = 0);

// A call inside a function; Resolved*: its declaration in the project, when AIT found it.
public record StructureCallDto(string Kind, string Target, int Line, string? ResolvedPath, string? ResolvedSymbol, int? ResolvedLine);

// Status: "Done" or "Failed". Changed: the function's body is different now from when it was read (it is read again
// the next time the planner reads it or a thread changes it).
public record StructureFunctionCallsDto(
    string Key,
    string Path,
    string Symbol,
    int StartLine,
    int EndLine,
    string Status,
    string? Error,
    string? Model,
    DateTime ScannedAt,
    bool Changed,
    IReadOnlyList<StructureCallDto> Calls);

// Key: the namespace, or "/" + folder for files without one; pass it back as the group of the next calls.
public record StructureGroupDto(string Key, string Name, bool IsFolder, int Types);

public record StructureTypeSummaryDto(string Name, string? Container, string Kind, int Files, int Members, bool IsPartial);

public record StructureClassDto(
    string Group,
    string Name,
    string? Container,
    string Kind,
    bool IsPartial,
    IReadOnlyList<StructureLocationDto> Files,
    IReadOnlyList<StructureMemberDto> Members);

public record StructureLocationDto(string Path, int Line);

// Kind: "type", "member" or "file".
public record StructureSearchHitDto(string Kind, string Name, string? Detail, string Path, int Line);
