using System.Security.Cryptography;
using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Business;

// Images accepted on business pages: the common screenshot formats, checked by their bytes rather than by the
// declared media type, since they are served back to every developer's browser.
public static class BusinessImages
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public static bool IsSupported(string? mediaType) =>
        mediaType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";

    // The media type the bytes actually are, or null when they are not a supported image.
    public static string? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 6 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    // Throws AppValidationException for anything that is not a supported image within the size limit.
    public static string Validate(byte[] data)
    {
        if (data.Length == 0) throw new AppValidationException("Gambar kosong.");
        if (data.Length > MaxBytes) throw new AppValidationException($"Gambar maksimal {MaxBytes / 1024 / 1024} MB.");
        return Sniff(data) ?? throw new AppValidationException("Format gambar tidak didukung — gunakan PNG, JPEG, GIF, atau WebP.");
    }

    // The prompt's images as rows to keep with the thread; anything unreadable or unsupported is skipped.
    public static IEnumerable<ThreadAttachment> FromPrompt(Guid threadId, IReadOnlyList<ImageAttachment>? images)
    {
        if (images is null) yield break;
        foreach (var image in images)
        {
            byte[] data;
            try
            {
                data = Convert.FromBase64String(image.Data);
            }
            catch (FormatException)
            {
                continue;
            }

            if (data.Length == 0 || data.Length > MaxBytes || Sniff(data) is not { } mediaType) continue;
            yield return new ThreadAttachment { ThreadRunId = threadId, MediaType = mediaType, Data = data, Sha256 = Sha256(data) };
        }
    }
}
