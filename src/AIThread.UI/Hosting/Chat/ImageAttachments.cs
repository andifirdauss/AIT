using System.Text.RegularExpressions;
using AIThread.Common.DTOs;
using AIThread.Common.Images;
using AIThread.Common.Localization;

namespace AIThread.UI.Hosting.Chat;

public sealed record ChatImage(string Name, string MediaType, byte[] Data);

// An "@path" token of the prompt: Start/Length cover the token with its @ (and quotes).
public sealed record PathToken(int Start, int Length, string Path);

// The images of the prompt being typed. Each attachment puts "[Image #N]" in the text; an image whose marker is gone is
// no longer sent. On send the markers are numbered again in the order they appear, so [Image #N] is Image N of the
// planner (the order of StartThreadRequest.Images).
public sealed partial class ImageAttachments
{
    private readonly Dictionary<int, ChatImage> _images = [];

    [GeneratedRegex(@"\[Image #(\d+)\]")]
    private static partial Regex MarkerPattern();

    // "@path" or "@"quoted path"", at the start of the text or after whitespace.
    [GeneratedRegex("""(?<=^|\s)@(?:"(?<q>[^"\r\n]+)"|(?<p>[^\s"]+))""")]
    private static partial Regex PathPattern();

    // A pasted or dropped path: "quoted", 'quoted' (PowerShell) or bare.
    [GeneratedRegex("""^\s*(?:"(?<q>[^"\r\n]+)"|'(?<s>[^'\r\n]+)'|(?<p>[^\s"']+))""")]
    private static partial Regex PastedPath();

    public static string Marker(int number) => $"[Image #{number}]";

    // Adds the image and returns its marker, or null with problem set when it is refused (too many images).
    public string? Add(ChatImage image, string text, out string? problem)
    {
        if (Active(text).Count >= PromptImages.MaxCount)
        {
            problem = PromptImages.TooMany;
            return null;
        }

        // The lowest number not in the text: one whose marker was deleted is taken again.
        var number = NextNumber(text);
        _images[number] = image;
        problem = null;
        return Marker(number);
    }

    public void Clear() => _images.Clear();

    public static int NextNumber(string text)
    {
        var number = 1;
        while (text.Contains(Marker(number))) number++;
        return number;
    }

    // Attached images still marked in the text, in the order their markers first appear.
    public IReadOnlyList<(int Number, ChatImage Image)> Active(string text) =>
        MarkerNumbers(text).Where(_images.ContainsKey).Select(n => (n, _images[n])).ToList();

    // The text and images as they are sent: markers numbered 1..n in order of appearance, each image once.
    public (string Text, IReadOnlyList<ImageAttachmentDto> Images) Build(string text)
    {
        var (renumbered, order) = Renumber(text, _images.Keys.ToHashSet());
        var images = order.Select(n => new ImageAttachmentDto(_images[n].MediaType, Convert.ToBase64String(_images[n].Data))).ToList();
        return (renumbered, images);
    }

    // Markers of attached images get the number of their first appearance; markers typed by hand for an image that is
    // not attached stay as they are. order: the old numbers in their new order.
    public static (string Text, IReadOnlyList<int> Order) Renumber(string text, IReadOnlySet<int> attached)
    {
        var order = new List<int>();
        foreach (var number in MarkerNumbers(text))
            if (attached.Contains(number)) order.Add(number);

        var renumbered = MarkerPattern().Replace(text, match =>
        {
            var number = int.Parse(match.Groups[1].Value);
            var index = order.IndexOf(number);
            return index < 0 ? match.Value : Marker(index + 1);
        });
        return (renumbered, order);
    }

    // The marker that ends at end (backspace) or starts at start (delete), so it is removed as a whole.
    public static (int Start, int Length)? MarkerEndingAt(string text, int end)
    {
        foreach (Match match in MarkerPattern().Matches(text))
            if (match.Index + match.Length == end) return (match.Index, match.Length);
        return null;
    }

    public static (int Start, int Length)? MarkerStartingAt(string text, int start)
    {
        foreach (Match match in MarkerPattern().Matches(text))
            if (match.Index == start) return (match.Index, match.Length);
        return null;
    }

    // "@path" tokens of image files (by extension); other @ tokens (@src/A.cs, @someone) stay text.
    public static IReadOnlyList<PathToken> FindPathTokens(string text) =>
        PathPattern().Matches(text)
            .Select(m => new PathToken(m.Index, m.Length, m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["p"].Value))
            .Where(t => PromptImages.LooksLikeImagePath(t.Path))
            .ToList();

    // Pasted text, or a file dragged onto the terminal (which types its path), that is nothing but paths of existing
    // image files: those paths, to attach instead of the text. Null for anything else, so a pasted sentence that only
    // mentions an image stays text. PowerShell's "& 'path'" of a dropped file is accepted.
    public static IReadOnlyList<string>? PastedImagePaths(string pasted, string currentDirectory)
    {
        var rest = pasted.Trim();
        if (rest.StartsWith("& ", StringComparison.Ordinal)) rest = rest[2..];
        var paths = new List<string>();
        while (rest.Length > 0)
        {
            var match = PastedPath().Match(rest);
            if (!match.Success) return null;
            var path = match.Groups["q"].Success ? match.Groups["q"].Value : match.Groups["s"].Success ? match.Groups["s"].Value : match.Groups["p"].Value;
            if (!PromptImages.LooksLikeImagePath(path)) return null;
            try
            {
                if (!File.Exists(Path.GetFullPath(path, currentDirectory))) return null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
            paths.Add(path);
            rest = rest[match.Length..].TrimStart();
        }
        return paths.Count > 0 ? paths : null;
    }

    // The image path token that ends right before end, as the editor checks it when a space or Enter follows.
    public static PathToken? PathTokenEndingAt(string text, int end) =>
        FindPathTokens(text[..end]).LastOrDefault(t => t.Start + t.Length == end);

    // Reads and checks a file; problem says why it is refused (missing, too large, not png/jpg/gif/webp).
    public static ChatImage? Load(string path, string currentDirectory, int number, out string? problem)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path, currentDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problem = Lang.T($"Path gambar tidak valid: {path}", $"Invalid image path: {path}");
            return null;
        }

        var file = new FileInfo(full);
        if (!file.Exists)
        {
            problem = Lang.T($"File gambar tidak ditemukan: {path}", $"Image file not found: {path}");
            return null;
        }
        if (file.Length > PromptImages.MaxBytes)
        {
            problem = Lang.T($"{file.Name} terlalu besar ({PromptImages.Size(file.Length)}); maksimal {PromptImages.Size(PromptImages.MaxBytes)}.", $"{file.Name} is too large ({PromptImages.Size(file.Length)}); the maximum is {PromptImages.Size(PromptImages.MaxBytes)}.");
            return null;
        }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = Lang.T($"Tidak bisa membaca {file.Name}: {ex.Message}", $"Cannot read {file.Name}: {ex.Message}");
            return null;
        }

        return FromBytes(file.Name, data, number, out problem);
    }

    public static ChatImage? FromBytes(string name, byte[] data, int number, out string? problem)
    {
        if (PromptImages.Problem(data, number) is { } refused)
        {
            problem = $"{name}: {refused}";
            return null;
        }

        problem = null;
        return new ChatImage(name, PromptImages.DetectMediaType(data)!, data);
    }

    private static IEnumerable<int> MarkerNumbers(string text) =>
        MarkerPattern().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Distinct();
}
