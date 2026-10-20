namespace AiThread.Service.Skills;

public sealed partial class SkillFetcher
{
    private async Task CloneAsync(string cloneUrl, string? reference, string? subPath, string destination, CancellationToken cancellationToken)
    {
        var checkout = destination + "-git";
        SkillPaths.DeleteDirectory(checkout);
        try
        {
            var arguments = new List<string> { "clone", "--depth", "1", "--quiet" };
            if (!string.IsNullOrWhiteSpace(reference)) arguments.AddRange(["--branch", reference]);
            arguments.AddRange([cloneUrl, checkout]);
            await RunGitAsync(arguments, cancellationToken);

            var source = checkout;
            if (!string.IsNullOrWhiteSpace(subPath))
            {
                source = Path.GetFullPath(Path.Combine(checkout, subPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!source.StartsWith(Path.GetFullPath(checkout), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source))
                    throw new InvalidDataException($"Folder '{subPath}' tidak ditemukan di repository.");
            }

            SkillPaths.DeleteDirectory(Path.Combine(source, ".git"));
            SkillPaths.DeleteDirectory(destination);
            Directory.Move(source, destination);
        }
        finally
        {
            SkillPaths.DeleteDirectory(checkout);
        }
    }
}
