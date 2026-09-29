namespace AiThread.Common.DTOs;

// Identifies the running instance, e.g. for scripts/aithread.ps1 (status, stop).
public record SystemInfoDto(int ProcessId, DateTime StartedAt, string Environment, string ContentRoot);
