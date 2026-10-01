using AiThread.Common.Enums;

namespace AiThread.Common.DTOs;

// A coding CLI AiThread can point at itself. GlobalPath is the file written in Global scope; ProjectFile is the file
// name written inside a project folder (relative), or null when the CLI has no project-level file.
public record CliInfoDto(
    string Id,
    string Name,
    string Description,
    string GlobalPath,
    string? ProjectFile,
    string? Note);

public record CliListDto(string ProxyBaseUrl, IReadOnlyList<CliInfoDto> Clis);

// Enabled = the file points the CLI at this proxy. CurrentBaseUrl is set when it points somewhere else (e.g. an old
// port or the user's own gateway). Preview is what applying would write.
public record CliStatusDto(
    string CliId,
    CliScope Scope,
    string Path,
    bool FileExists,
    bool Enabled,
    string? CurrentBaseUrl,
    string Preview);

public record ProjectCliStatusDto(Guid ProjectId, bool Enabled, string? CurrentBaseUrl, string Path, string? Error);

public record CliToggleRequest(CliScope Scope, Guid? ProjectId, bool Enabled, string? ProxyBaseUrl);
