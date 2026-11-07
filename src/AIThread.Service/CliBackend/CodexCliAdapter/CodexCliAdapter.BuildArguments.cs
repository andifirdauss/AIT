namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
{
    // One stateless model turn: nothing saved (--ephemeral), read-only sandbox, the shell and web search off, no
    // AGENTS.md, and the agent's own system prompt instead of Codex's (model_instructions_file). The prompt is read from
    // stdin ("-", last). Images go before the other flags: --image takes several values.
    public static IReadOnlyList<string> BuildArguments(string model, string instructionsFile, IReadOnlyList<string> images, bool ignoreUserConfig)
    {
        var arguments = new List<string> { "exec" };
        foreach (var image in images)
        {
            arguments.Add("--image");
            arguments.Add(image);
        }
        arguments.AddRange(
        [
            "--json",
            "--ephemeral",
            "--skip-git-repo-check",
            "--sandbox", "read-only",
            "--color", "never",
            "-c", $"model_instructions_file={TomlLiteral(instructionsFile)}",
            "-c", "features.shell_tool=false",
            "-c", "web_search=\"disabled\"",
            "-c", "project_doc_max_bytes=0"
        ]);
        if (ignoreUserConfig) arguments.Add("--ignore-user-config");
        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model.Trim());
        }
        arguments.Add("-");
        return arguments;
    }
}
