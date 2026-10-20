using System.Xml;
using System.Xml.Linq;

namespace AIThread.Service.Workspace.Structure;

// Third-party and generated code that lives inside the project's own folders, so WorkspaceScope's folder rules do not
// catch it: locations declared in thirdpartylibs.xml files (Moodle and its plugins), minified bundles, and the build
// output of AMD/YUI modules. Such files are listed but never parsed.
public sealed class ThirdPartyFilter
{
    public const string DeclarationFile = "thirdpartylibs.xml";

    private static readonly string[] GeneratedFragments = ["/amd/build/", "/yui/build/"];
    private static readonly string[] GeneratedSuffixes = [".min.js", ".min.mjs", ".min.css", ".bundle.js"];

    // Relative paths, forward slashes: a folder ends with "/", anything else is one file.
    private readonly List<string> _declared;

    private ThirdPartyFilter(List<string> declared) => _declared = declared;

    public int DeclaredLocations => _declared.Count;

    // relativeFiles: the project's files as walked; the declaration files among them are read.
    public static ThirdPartyFilter Load(string root, IEnumerable<string> relativeFiles)
    {
        var declared = new List<string>();
        foreach (var file in relativeFiles.Where(f => Path.GetFileName(f).Equals(DeclarationFile, StringComparison.OrdinalIgnoreCase)))
        {
            var folder = Path.GetDirectoryName(file)?.Replace('\\', '/') ?? string.Empty;
            foreach (var location in Locations(Path.Combine(root, file)))
            {
                var relative = (folder.Length == 0 ? location : $"{folder}/{location}").Trim('/');
                if (relative.Length == 0 || relative.Contains("..", StringComparison.Ordinal)) continue;
                declared.Add(Directory.Exists(Path.Combine(root, relative)) ? relative + "/" : relative);
            }
        }
        return new ThirdPartyFilter(declared);
    }

    public bool IsThirdParty(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        if (GeneratedSuffixes.Any(s => path.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return true;
        if (GeneratedFragments.Any(f => ("/" + path).Contains(f, StringComparison.OrdinalIgnoreCase))) return true;
        foreach (var location in _declared)
        {
            if (location.EndsWith('/')
                    ? path.StartsWith(location, StringComparison.OrdinalIgnoreCase)
                    : path.Equals(location, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // <library><location>adodb</location>…</library>; a broken file only loses its own declarations.
    private static IEnumerable<string> Locations(string path)
    {
        try
        {
            return XDocument.Load(path).Descendants("location")
                .Select(e => e.Value.Trim().Replace('\\', '/'))
                .Where(v => v.Length > 0)
                .ToList();
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
