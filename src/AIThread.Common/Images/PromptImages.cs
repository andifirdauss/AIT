using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Common.Images;

// Limits of the images attached to a prompt started by POST /api/threads (aithread chat), checked by the client before
// it attaches a file and again by the API. The format is read from the file signature, never from the name.
public static class PromptImages
{
    public const int MaxCount = 6;
    public const int MaxBytes = 6 * 1024 * 1024;

    // Extensions the chat treats as an image path (@path); the signature decides whether the file is accepted.
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".ico", ".svg", ".heic", ".avif"];

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    public static bool LooksLikeImagePath(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    // image/png, image/jpeg, image/gif or image/webp from the first bytes; null for anything else.
    public static string? DetectMediaType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature)) return "image/png";
        if (data.StartsWith(JpegSignature)) return "image/jpeg";
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)) return "image/gif";
        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    // Why the image cannot be attached, or null when it can. number: its [Image #N] for the message.
    public static string? Problem(ReadOnlySpan<byte> data, int number)
    {
        if (data.Length == 0) return Lang.T($"Gambar #{number} kosong.", $"Image #{number} is empty.");
        if (data.Length > MaxBytes)
            return Lang.T($"Gambar #{number} terlalu besar ({Size(data.Length)}); maksimal {Size(MaxBytes)}.", $"Image #{number} is too large ({Size(data.Length)}); the maximum is {Size(MaxBytes)}.");
        if (DetectMediaType(data) is null)
            return Lang.T($"Gambar #{number} bukan png, jpg, gif, atau webp.", $"Image #{number} is not a png, jpg, gif or webp.");
        return null;
    }

    public static string TooMany =>
        Lang.T($"Maksimal {MaxCount} gambar per prompt.", $"At most {MaxCount} images per prompt.");

    // The API side: every image decoded and checked; the first problem, or null when all are fine. The declared
    // MediaType must match the signature.
    public static string? Validate(IReadOnlyList<ImageAttachmentDto>? images)
    {
        if (images is null || images.Count == 0) return null;
        if (images.Count > MaxCount) return TooMany;

        for (var i = 0; i < images.Count; i++)
        {
            var number = i + 1;
            byte[] data;
            try
            {
                data = Convert.FromBase64String(images[i].Data ?? string.Empty);
            }
            catch (FormatException)
            {
                return Lang.T($"Gambar #{number} bukan base64 yang valid.", $"Image #{number} is not valid base64.");
            }

            if (Problem(data, number) is { } problem) return problem;
            if (!string.Equals(DetectMediaType(data), images[i].MediaType?.Trim(), StringComparison.OrdinalIgnoreCase))
                return Lang.T($"MediaType gambar #{number} tidak cocok dengan isinya ({DetectMediaType(data)}).", $"The MediaType of image #{number} does not match its content ({DetectMediaType(data)}).");
        }

        return null;
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} B"
    };
}
