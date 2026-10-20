using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // The sections a file holds; a hand-edited profile may leave some out.
    private static IReadOnlyList<SetupProfileSection> SectionsOf(SetupProfileFile profile)
    {
        var sections = new List<SetupProfileSection>();
        if (profile.Router is not null) sections.Add(SetupProfileSection.Router);
        if (profile.Orchestration is not null) sections.Add(SetupProfileSection.Orchestration);
        if (profile.Nodes is { Count: > 0 }) sections.Add(SetupProfileSection.Nodes);
        if (profile.Memory is not null) sections.Add(SetupProfileSection.Memory);
        if (profile.Business is not null) sections.Add(SetupProfileSection.Business);
        if (profile.Skills is not null) sections.Add(SetupProfileSection.Skills);
        return sections;
    }
}
