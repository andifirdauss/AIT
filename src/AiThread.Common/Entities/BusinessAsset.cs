namespace AiThread.Common.Entities;

// An image (screenshot) of a business page: pasted in the editor, or picked by the wiki writer from the images
// attached to a developer's prompt. Stored in the business memory store itself so every developer sees it.
public class BusinessAsset : BaseEntity
{
    public string ProjectKey { get; set; } = string.Empty;

    // Page it was added to; null until a page references it.
    public Guid? PageId { get; set; }

    public string MediaType { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];

    // Hex SHA-256 of Data: the same image is stored once per project.
    public string Sha256 { get; set; } = string.Empty;

    public string? Caption { get; set; }
    public Guid? SourceThreadId { get; set; }
    public string? CreatedBy { get; set; }
}
