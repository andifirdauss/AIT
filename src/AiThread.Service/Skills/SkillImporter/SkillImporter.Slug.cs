namespace AiThread.Service.Skills;

public sealed partial class SkillImporter
{
    public static string Slug(string name)
    {
        var slug = NonSlug().Replace(name.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        return slug.Length == 0 ? "skill" : slug;
    }
}
