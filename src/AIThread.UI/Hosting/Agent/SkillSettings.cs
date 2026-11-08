using System.Text.Json;
using AIThread.Common.Projects;

namespace AIThread.UI.Hosting.Agent;

// The developer's choices about the Claude Code plugin, read by its hook (integrations/claude-plugin/.../gate.mjs) too.
// Declined: they said no to installing AIT, so the plugin stays silent until 'aithread skill on'. DisabledRoots: folders
// where the plugin neither speaks nor blocks.
public sealed class SkillSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string FilePath => Path.Combine(AppPaths.HomeDir, "skill.json");

    public bool Declined { get; set; }

    public List<string> DisabledRoots { get; set; } = [];

    public bool IsDisabled(string folder) => DisabledRoots.Any(root => ProjectFolders.IsInside(folder, root));

    public void Disable(string folder)
    {
        var path = ProjectFolders.Normalize(folder);
        if (!IsDisabled(path)) DisabledRoots.Add(path);
    }

    // Every entry holding the folder or inside it goes, or the folder would stay off.
    public void Enable(string folder)
    {
        Declined = false;
        DisabledRoots.RemoveAll(root => ProjectFolders.IsInside(folder, root) || ProjectFolders.IsInside(root, folder));
    }

    public static SkillSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<SkillSettings>(File.ReadAllText(FilePath), Json) ?? new SkillSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new SkillSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.HomeDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
