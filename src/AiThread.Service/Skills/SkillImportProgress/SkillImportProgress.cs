using System.Collections.Concurrent;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Skills;

// Live progress text of sources being pulled (e.g. "batch 3/22"), shown next to their status. Memory only:
// it only matters while a pull runs.
public sealed partial class SkillImportProgress : ISkillImportProgress
{
    private readonly ConcurrentDictionary<Guid, string> _progress = new();
}
