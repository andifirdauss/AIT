namespace AiThread.Service.Verification;

public sealed partial class BuildRunner
{
    // Reported like a build that cannot start: fix tasks cannot help and the next round would fail the same way.
    private static string? MissingToolchain(BuildCommand command, IReadOnlyList<BuildDiagnostic> errors)
    {
        if (errors.Count == 0 || !errors.All(e => e.IsMissingToolchain)) return null;
        var first = errors[0];
        var message = first.Message.Length <= MaxToolchainMessage ? first.Message : first.Message[..MaxToolchainMessage] + "…";
        return $"this machine lacks build tooling the project needs ({first.Code}: {message}). This is an environment problem, not a code problem"
               + command.Stack switch
               {
                   ".NET" => "; .NET Framework projects build with MSBuild of Visual Studio or the Build Tools for Visual Studio.",
                   ".NET Framework" => "; install the Visual Studio workload that provides it (for ASP.NET MVC / Web API: \"ASP.NET and web development\").",
                   _ => "."
               };
    }
}
