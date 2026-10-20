namespace AIThread.Service.Workspace.Structure;

public sealed record ClassLocation(string Path, int Line);

public sealed record ClassMember(OutlineMember Member, string Path);

// One type of the class view: every declaration with the same group, container and name, so a C# partial class spread
// over many files (or a PHP class declared twice) is one entry with all its files and members.
public sealed class StructureClass(string group, string name, string? container, string kind)
{
    public string Group { get; } = group;
    public string Name { get; } = name;
    public string? Container { get; } = container;
    public string Kind { get; } = kind;
    public bool IsPartial { get; set; }
    public List<ClassLocation> Locations { get; } = [];
    public List<ClassMember> Members { get; } = [];
}

// Key: the namespace, or "/" + folder for files without one (old-style PHP, plain JavaScript).
public sealed record StructureGroup(string Key, string Name, bool IsFolder, int Types);

public sealed class StructureClassIndex
{
    public const string RootFolder = "(root)";

    private readonly Dictionary<string, List<StructureClass>> _byGroup;

    private StructureClassIndex(Dictionary<string, List<StructureClass>> byGroup)
    {
        _byGroup = byGroup;
        Groups = byGroup
            .Select(g => new StructureGroup(g.Key, g.Key.StartsWith('/') ? g.Key[1..] : g.Key, g.Key.StartsWith('/'), g.Value.Count))
            .OrderBy(g => g.IsFolder)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<StructureGroup> Groups { get; }

    public IReadOnlyList<StructureClass> TypesIn(string group) => _byGroup.GetValueOrDefault(group) ?? [];

    public StructureClass? Find(string group, string name, string? container) =>
        TypesIn(group).FirstOrDefault(t => t.Name == name && string.Equals(t.Container, container, StringComparison.Ordinal));

    public static StructureClassIndex Build(StructureSnapshot snapshot)
    {
        var byGroup = new Dictionary<string, List<StructureClass>>(StringComparer.Ordinal);
        var byKey = new Dictionary<(string Group, string? Container, string Name), StructureClass>();
        foreach (var file in snapshot.Files.Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (file.Outline is not { Types.Count: > 0 } outline) continue;
            var folder = Path.GetDirectoryName(file.Path)?.Replace('\\', '/');
            foreach (var type in outline.Types)
            {
                var group = type.Namespace ?? "/" + (string.IsNullOrEmpty(folder) ? RootFolder : folder);
                if (!byKey.TryGetValue((group, type.Container, type.Name), out var entry))
                {
                    entry = new StructureClass(group, type.Name, type.Container, type.Kind);
                    byKey[(group, type.Container, type.Name)] = entry;
                    if (!byGroup.TryGetValue(group, out var list)) byGroup[group] = list = [];
                    list.Add(entry);
                }
                entry.IsPartial |= type.IsPartial;
                entry.Locations.Add(new ClassLocation(file.Path, type.Line));
                entry.Members.AddRange(type.Members.Select(m => new ClassMember(m, file.Path)));
            }
        }
        foreach (var list in byGroup.Values)
            list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(Display(a), Display(b)));
        return new StructureClassIndex(byGroup);
    }

    private static string Display(StructureClass type) => type.Container is null ? type.Name : $"{type.Container}.{type.Name}";
}
