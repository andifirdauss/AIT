using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// ProjectName / ProjectRoot: the registered project holding the folder, else the project the folder looks like; null
// for NotAProject.
public record SkillGateDto(SkillGateState State, string? ProjectName, string? ProjectRoot);
