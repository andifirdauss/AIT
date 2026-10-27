using System.Text;

namespace AIThread.Service.Workspace.Commands;

// One word of a command line after quote removal. Quoted: part of it was quoted, so a shell does not expand it.
// Expands: an unquoted (or double-quoted) $ or backtick, i.e. the shell would substitute something AIT cannot check.
// Glob: an unquoted * ? [ or {, which the shell turns into file names.
public sealed record CommandWord(string Text, bool Quoted, bool Expands, bool Glob);

// A command line split the way bash would split it, without running a shell: pipeline segments of words. Anything
// that makes the line more than a pipeline of plain commands (; && || & redirects, subshells, a newline) is reported
// as Operator, so it is never treated as read-only.
public sealed class CommandLine
{
    private CommandLine(IReadOnlyList<IReadOnlyList<CommandWord>> segments, string? @operator)
    {
        Segments = segments;
        Operator = @operator;
    }

    public IReadOnlyList<IReadOnlyList<CommandWord>> Segments { get; }

    // The first shell operator met, or "unbalanced quote"; null for a plain pipeline.
    public string? Operator { get; }

    public static CommandLine Parse(string command)
    {
        var segments = new List<List<CommandWord>> { new() };
        var word = new StringBuilder();
        var inWord = false;
        var quoted = false;
        var expands = false;
        var glob = false;

        void Flush()
        {
            if (inWord) segments[^1].Add(new CommandWord(word.ToString(), quoted, expands, glob));
            word.Clear();
            inWord = quoted = expands = glob = false;
        }

        CommandLine Stop(string op)
        {
            Flush();
            return new CommandLine(segments, op);
        }

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            switch (c)
            {
                case '\'':
                {
                    var end = command.IndexOf('\'', i + 1);
                    if (end < 0) return Stop("unbalanced quote");
                    word.Append(command, i + 1, end - i - 1);
                    inWord = quoted = true;
                    i = end;
                    break;
                }
                case '"':
                {
                    var closed = false;
                    for (i++; i < command.Length; i++)
                    {
                        var d = command[i];
                        if (d == '"')
                        {
                            closed = true;
                            break;
                        }
                        if (d == '\\' && i + 1 < command.Length && command[i + 1] is '"' or '\\' or '$' or '`')
                        {
                            word.Append(command[++i]);
                            continue;
                        }
                        if (d is '$' or '`') expands = true;
                        word.Append(d);
                    }
                    if (!closed) return Stop("unbalanced quote");
                    inWord = quoted = true;
                    break;
                }
                case '\\':
                    if (i + 1 < command.Length) word.Append(command[++i]);
                    inWord = true;
                    break;
                case ' ' or '\t':
                    Flush();
                    break;
                case '|':
                    if (i + 1 < command.Length && command[i + 1] == '|') return Stop("||");
                    Flush();
                    segments.Add([]);
                    break;
                case ';' or '&' or '<' or '>' or '`' or '\n' or '\r' or '(' or ')':
                    return Stop(c switch { '\n' or '\r' => "newline", '&' when i + 1 < command.Length && command[i + 1] == '&' => "&&", _ => c.ToString() });
                case '$':
                    expands = true;
                    word.Append(c);
                    inWord = true;
                    break;
                case '*' or '?' or '[' or '{':
                    glob = true;
                    word.Append(c);
                    inWord = true;
                    break;
                default:
                    word.Append(c);
                    inWord = true;
                    break;
            }
        }

        Flush();
        return new CommandLine(segments, null);
    }
}
