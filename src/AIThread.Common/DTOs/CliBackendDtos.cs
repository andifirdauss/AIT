namespace AIThread.Common.DTOs;

// An official coding CLI found (or not) on this machine as a model engine for nodes ("cli:<tool>:<model>").
// Path and Version are null when it is not installed; Error says why it could not be checked.
public record CliBackendStatusDto(string Tool, string Name, bool Installed, string? Path, string? Version, string? Error);
