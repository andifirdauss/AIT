namespace AIThread.Service.Workspace.Documents;

// Turns one document format into plain text for read_file. Output uses "\n" line endings; read_file numbers the lines.
public interface IDocumentExtractor
{
    // Lower-case extensions with the dot, e.g. ".docx".
    IReadOnlyCollection<string> Extensions { get; }
    Task<string> ExtractAsync(string fullPath, CancellationToken cancellationToken);
}
