using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Skills;

// Where pulled skills live offline: <root>\<source id>\..., plus a .staging folder for pulls in progress.
public sealed partial class SkillPaths(IOptions<AIThreadOptions> options) : ISkillPaths
{
    public string Root { get; } = string.IsNullOrWhiteSpace(options.Value.SkillsDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", "skills")
        : Path.GetFullPath(options.Value.SkillsDirectory);

    public string StagingRoot => Path.Combine(Root, ".staging");
}
