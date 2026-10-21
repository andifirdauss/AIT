using System.Collections.Immutable;
using System.Text.Json.Nodes;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

// OpenAI function definitions offered to the planner, and to the tools nodes it delegates tools to.
public static class PlannerTools
{
    public const string ListDirectory = "list_directory";
    public const string FindFiles = "find_files";
    public const string SearchText = "search_text";
    public const string FindSymbol = "find_symbol";
    public const string FindReferences = "find_references";
    public const string ReadFile = "read_file";
    public const string CodeOutline = "code_outline";
    public const string FunctionCalls = "function_calls";
    public const string SearchMemory = "search_memory";
    public const string ReadMemory = "read_memory";
    public const string ReadThread = "read_thread";
    public const string GitStatus = "git_status";
    public const string GitDiff = "git_diff";
    public const string GitLog = "git_log";
    public const string GitShow = "git_show";
    public const string GitBlame = "git_blame";
    public const string RunBuild = "run_build";
    public const string Investigate = "investigate";
    public const string AskUser = "ask_user";
    public const string SetModule = "set_module";
    public const string SubmitPlan = "submit_plan";

    // Ends a tools node's investigation (a reply without tool calls does too).
    public const string ReportFindings = "report_findings";

    // Tools a planner can hand to a tools node (PlannerToolDelegation), in the order they are offered.
    public static readonly ImmutableArray<string> Delegable =
        [ListDirectory, FindFiles, SearchText, FindSymbol, FindReferences, ReadFile, SearchMemory, ReadMemory, ReadThread, GitStatus, GitDiff, GitLog, GitShow, GitBlame, RunBuild];

    // Read-only git (GitTools), offered only when the project lives in a git repository.
    public static readonly ImmutableArray<string> Git = [GitStatus, GitDiff, GitLog, GitShow, GitBlame];

    // The skeleton of the code (CodeSkeletonTools): small output, so the planner always keeps them, and a tools node
    // that reads files gets them too, to read only the lines it needs.
    public static readonly ImmutableArray<string> Skeleton = [CodeOutline, FunctionCalls];

    // Column the arguments of a tool call start at in the progress (longest tool name plus two spaces).
    public static readonly int NameColumn = Delegable.Concat(Skeleton).Append(Investigate).Append(AskUser).Max(n => n.Length) + 2;

    // canBuild: build verification is enabled and the project has a build command AIThread can run.
    // canAsk: the developer can answer a question (setting on, and the prompt came from a session that can reply).
    // groups: tools delegated to tools nodes; the planner loses them and gets investigate instead.
    // canTest: Auto Testing is on, so submit_plan asks for the test section.
    // canGit: the project lives in a git repository (GitTools.IsRepository).
    // The list stays the same for the whole planner run so the provider's prompt cache keeps matching.
    public static JsonArray All(bool canBuild = false, bool canAsk = false, IReadOnlyList<ToolsNodeGroup>? groups = null, bool canTest = false, bool canGit = false)
    {
        var delegated = groups?.SelectMany(g => g.Tools).ToHashSet() ?? [];
        var tools = new JsonArray();
        foreach (var name in Delegable)
        {
            if (delegated.Contains(name) || !Offered(name, canBuild, canGit)) continue;
            tools.Add(Definition(name));
        }
        foreach (var name in Skeleton) tools.Add(Definition(name));
        tools.Add(SetModuleDefinition());
        if (groups is { Count: > 0 }) tools.Add(InvestigateDefinition(groups));
        if (canAsk) tools.Add(AskUserDefinition());
        tools.Add(SubmitPlanDefinition(canTest));
        return tools;
    }

    public static JsonArray SubmitOnly(bool canTest = false) => [SubmitPlanDefinition(canTest)];

    // Whether a delegable tool can run for this project, for the planner and for its tools nodes alike.
    public static bool Offered(string name, bool canBuild, bool canGit) =>
        name == RunBuild ? canBuild : canGit || !Git.Contains(name);

    // The tools one tools node may run: those delegated to it, plus the skeleton tools when it reads files.
    public static IReadOnlyList<string> ToolsNodeTools(IEnumerable<string> delegated)
    {
        var wanted = delegated.ToHashSet();
        var names = Delegable.Where(wanted.Contains).ToList();
        if (wanted.Contains(ReadFile)) names.AddRange(Skeleton);
        return names;
    }

    // The tools of one tools node, plus report_findings.
    public static JsonArray ForToolsNode(IEnumerable<string> tools)
    {
        var result = new JsonArray();
        foreach (var name in ToolsNodeTools(tools)) result.Add(Definition(name));
        result.Add(ReportFindingsDefinition());
        return result;
    }

    public static JsonArray ReportOnly() => [ReportFindingsDefinition()];

    // A new object on every call: a JsonNode can only belong to one array.
    private static JsonObject Definition(string name) => name switch
    {
        ListDirectory => ChatMessages.Function(ListDirectory,
            "List files and folders under a directory (build output, VCS and third-party folders are hidden).",
            Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Directory relative to the project root, or an absolute path anywhere on this machine to look outside the project (read-only). Use \".\" for the root." },
                "depth": { "type": "integer", "description": "How many levels to expand (1-4). Default 2." }
              },
              "required": ["path"]
            }
            """)),
        FindFiles => ChatMessages.Function(FindFiles,
            "Find files by glob pattern, e.g. \"**/*User*.cs\", \"src/**/Views/User/*.cshtml\", \"*.sql\".",
            Schema("""
            {
              "type": "object",
              "properties": {
                "pattern": { "type": "string", "description": "Glob pattern relative to the project root (relative to the given path when that is outside the project)." },
                "path": { "type": "string", "description": "Optional directory to search in (default: the project root). May be an absolute path outside the project (read-only)." }
              },
              "required": ["pattern"]
            }
            """)),
        SearchText => ChatMessages.Function(SearchText,
            "Search file contents (case-insensitive). Returns path:line: text for each match.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Text to search for, or a regular expression when regex is true." },
                "glob": { "type": "string", "description": "Optional glob limiting which files are searched, e.g. \"**/*.js\"." },
                "regex": { "type": "boolean", "description": "Treat query as a .NET regular expression." },
                "path": { "type": "string", "description": "Optional directory to search in (default: the project root). May be an absolute path outside the project (read-only)." }
              },
              "required": ["query"]
            }
            """)),
        FindSymbol => ChatMessages.Function(FindSymbol,
            "Find where a symbol is declared (class, interface, method, property, field, enum, function, ...). C# is parsed with Roslyn; JS/TS, Python and Java use declaration patterns. Returns path:line: kind signature (in Type). Faster and more precise than search_text for locating definitions.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Identifier, or Type.Member to restrict to members of one type, e.g. \"UserService\" or \"UserService.Save\". Case-sensitive." },
                "kind": { "type": "string", "description": "Optional filter: type, class, struct, record, interface, enum, delegate, method, constructor, property, field, event, enum member, local function, function, variable, type alias." },
                "path": { "type": "string", "description": "Optional directory to search in (default: the project root). May be an absolute path outside the project (read-only)." }
              },
              "required": ["name"]
            }
            """)),
        FindReferences => ChatMessages.Function(FindReferences,
            "Find every use of an identifier, ignoring comments and (for C#) string literals; declaration lines are marked [declaration]. Use it to find all places a contract name, method or property must change. Matches by name only, so same-named members of other types appear too.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Identifier to find (for Type.Member only the member name is used)." },
                "path": { "type": "string", "description": "Optional directory to search in (default: the project root). May be an absolute path outside the project (read-only)." }
              },
              "required": ["name"]
            }
            """)),
        ReadFile => ChatMessages.Function(ReadFile,
            "Read a file with line numbers (max 600 lines per call; use startLine/endLine for large files). Documents (.pdf, .docx, .xlsx, .pptx, .ipynb, .odt, .ods) are returned as extracted text with page/sheet/slide/cell headers.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "File path relative to the project root, or an absolute path anywhere on this machine to read a file outside the project." },
                "startLine": { "type": "integer", "description": "First line to read (1-based)." },
                "endLine": { "type": "integer", "description": "Last line to read (inclusive)." }
              },
              "required": ["path"]
            }
            """)),
        CodeOutline => ChatMessages.Function(CodeOutline,
            "The skeleton of code without its bodies: types, members and functions with their line ranges and signatures (C#, PHP, JavaScript/TypeScript, Python, Java, Kotlin). Call it before read_file on a file you do not know yet, then read only the line ranges you need. A file gives every declaration with startLine-endLine; a folder (\".\" = the whole project) gives one line per file with its types and functions; name alone finds a class in the whole project (all files of a partial class). Members marked [calls: N] have recorded calls: see function_calls.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "A file or folder relative to the project root (a file may also be an absolute path outside the project, read-only)." },
                "name": { "type": "string", "description": "Optional class or type name, e.g. \"UserService\" or \"OrderBL.Line\": without path it is looked up in the whole project; with a file path only that type is shown." }
              }
            }
            """)),
        FunctionCalls => ChatMessages.Function(FunctionCalls,
            "What a function or method calls and what calls it, as recorded by AIT for code read or changed earlier: each call with its line and where it is declared (functions, methods, HTTP endpoints → controller actions, views, SQL). Use it to follow a feature from the screen to the database, or to see the impact of a change, without reading every file on the way. Calls not recorded yet are queued; callers cover recorded functions only (find_references lists every use).",
            Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "File relative to the project root." },
                "name": { "type": "string", "description": "Function or method name, or Type.Member, e.g. \"p_InitForm\" or \"UserService.Save\"." }
              },
              "required": ["path", "name"]
            }
            """)),
        SearchMemory => ChatMessages.Function(SearchMemory,
            "Search memory (project facts, reusable knowledge and the project's business wiki pages) in two layers: module (optionally layer and kind) narrows the list, keywords rank inside it. Returns ids and labels only. Without a module, the modules named in the keywords are used, else all memories are ranked by keywords.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "module": { "type": "string", "description": "Module from the memory map or the code map, e.g. \"User\"." },
                "layer": { "type": "string", "enum": ["UI", "Entry", "Logic", "Data", "Infra", "Test"], "description": "Optional: only memories about this layer." },
                "kind": { "type": "string", "enum": ["Pitfall", "Rule", "Flow", "Convention", "Architecture", "Pattern"], "description": "Optional: only memories of this kind." },
                "keywords": { "type": "array", "items": { "type": "string" }, "description": "Search terms: entities, fields, identifiers, in English and in the developer's words, e.g. [\"Description\", \"Deskripsi\", \"UserValidator\"]." },
                "query": { "type": "string", "description": "Free text instead of keywords." }
              }
            }
            """)),
        ReadMemory => ChatMessages.Function(ReadMemory,
            "Read the full description of memories, or the content of business wiki pages, by id.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "ids": { "type": "array", "items": { "type": "string" }, "description": "Memory ids from the index or search_memory." }
              },
              "required": ["ids"]
            }
            """)),
        ReadThread => ChatMessages.Function(ReadThread,
            "Read the full record of an earlier turn of this conversation: request, plan, tasks with their results and the reply. Thread ids appear in the \"Session so far\" section of the brief.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "id": { "type": "string", "description": "Thread id, e.g. \"3f2a1b9c\"." }
              },
              "required": ["id"]
            }
            """)),
        GitStatus => ChatMessages.Function(GitStatus,
            "Show the uncommitted changes of the git repository: the current branch (ahead/behind its upstream) and every staged, modified and untracked file. Use it when the developer refers to their current, recent or uncommitted changes, then git_diff to see them.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Optional file or folder to limit it to, relative to the project root (an absolute path outside the project checks that repository)." }
              }
            }
            """)),
        GitDiff => ChatMessages.Function(GitDiff,
            "Show changes as a unified diff. By default everything not committed yet (staged and not staged) against HEAD; staged: only what is staged; base: compare with another commit or branch instead, or a range such as \"main...HEAD\" (what the current branch changed since it left main). Use stat first when you do not know how large the change is. Untracked files are not in a diff: git_status lists them.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Optional file or folder to limit the diff to, relative to the project root." },
                "base": { "type": "string", "description": "Optional commit, branch, tag or range to compare with, e.g. \"main\", \"HEAD~3\", \"a1b2c3d\", \"main...HEAD\". Default: HEAD." },
                "staged": { "type": "boolean", "description": "Only the staged changes (the index) instead of the working tree." },
                "stat": { "type": "boolean", "description": "Only the changed files with their added/removed line counts." }
              }
            }
            """)),
        GitLog => ChatMessages.Function(GitLog,
            "List commits, newest first: short hash, date, author and subject. Narrow it with path (the history of a file or folder, renames followed), revision, query (words in the message) or change (commits that added or removed a text such as a function name, to find when it appeared or disappeared).",
            Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Optional file or folder, relative to the project root." },
                "revision": { "type": "string", "description": "Optional branch, tag, commit or range, e.g. \"main\", \"main..HEAD\" (commits on the current branch that main does not have). Default: the current branch." },
                "limit": { "type": "integer", "description": "How many commits (1-100). Default 20." },
                "query": { "type": "string", "description": "Only commits whose message contains this text (case-insensitive)." },
                "change": { "type": "string", "description": "Only commits that added or removed this exact text in the code." },
                "files": { "type": "boolean", "description": "Also list the files each commit changed (A added, M modified, D deleted, R renamed)." }
              }
            }
            """)),
        GitShow => ChatMessages.Function(GitShow,
            "Show one commit: author, date, full message, the changed files and the diff.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "commit": { "type": "string", "description": "Commit hash, branch or tag, e.g. \"a1b2c3d\" or \"HEAD~1\"." },
                "path": { "type": "string", "description": "Optional file or folder to limit the diff to, relative to the project root." },
                "stat": { "type": "boolean", "description": "Only the message and the changed files, without the diff." }
              },
              "required": ["commit"]
            }
            """)),
        GitBlame => ChatMessages.Function(GitBlame,
            $"Show, for each line of a file, the commit, author and date that last changed it (lines not committed yet show 00000000 Not Committed Yet). Max {GitTools.MaxBlameLines} lines per call: give the range around the code in question, then git_show the commit to see why it changed.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "File path relative to the project root." },
                "startLine": { "type": "integer", "description": "First line (1-based)." },
                "endLine": { "type": "integer", "description": "Last line (inclusive)." }
              },
              "required": ["path"]
            }
            """)),
        RunBuild => RunBuildDefinition(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown planner tool.")
    };

    private static JsonObject RunBuildDefinition() =>
        ChatMessages.Function(RunBuild,
            "Build the project with its standard build command (e.g. dotnet build of the solution) and return the result with every error as file(line,col): code: message. Use it when the developer asks to build or to fix build errors, or to see the current errors before planning fixes. Takes seconds to minutes; never call it in parallel with itself.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "target": { "type": "string", "description": "Optional solution, project or manifest file (or its folder) relative to the project root, e.g. \"src/Api/Api.csproj\". Default: the main solution or project." }
              }
            }
            """));

    // agent is only asked for when there is a choice; its enum is the keys of the groups.
    private static JsonObject InvestigateDefinition(IReadOnlyList<ToolsNodeGroup> groups)
    {
        var holders = groups.Count == 1
            ? $"The sub agent holds: {string.Join(", ", groups[0].Tools)}."
            : "Sub agents: " + string.Join("; ", groups.Select(g => $"\"{g.Key}\" holds {string.Join(", ", g.Tools)}")) + ".";
        var properties = new JsonObject
        {
            ["question"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "One precise, self-contained question: what to find, what you already know (paths, names) and what exactly you need back, e.g. \"the line range of method Save in src/UserService.cs and its full signature\" or \"every caller of IUserService.Save with path:line\". Ask for verbatim code when you will put it in a contract, a focus range or an instruction."
            }
        };
        var required = new JsonArray { "question" };
        if (groups.Count > 1)
        {
            properties["agent"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(groups.Select(g => (JsonNode)g.Key).ToArray()),
                ["description"] = "The sub agent that holds the tools the question needs."
            };
            required.Insert(0, "agent");
        }

        return ChatMessages.Function(Investigate,
            "Delegate a read-only investigation to a cheaper sub agent. It runs the tools itself, as many steps as needed, and returns only its findings (path:line ranges and short exact excerpts), so raw tool output never fills your context. " +
            $"You do not have these tools yourself; use investigate for them. {holders} Several investigate calls run in parallel.",
            new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required });
    }

    private static JsonObject SetModuleDefinition() =>
        ChatMessages.Function(SetModule,
            "Correct the code map: the module (and optionally the layer) a file belongs to. Use it when the developer says so (\"treat UserBL as module Account\") or when the code clearly shows the map is wrong. It is remembered for every later thread and never overwritten automatically.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "File path relative to the project root, or a class/file name such as \"UserBL\"." },
                "module": { "type": "string", "description": "Module name, an existing one from the memory map when it fits." },
                "layer": { "type": "string", "enum": ["UI", "Entry", "Logic", "Data", "Infra", "Test"] }
              },
              "required": ["path", "module"]
            }
            """));

    private static JsonObject ReportFindingsDefinition() =>
        ChatMessages.Function(ReportFindings,
            "Finish the investigation and send your findings to the planner. Call it once, when you can answer the question (or know that the answer cannot be found).",
            Schema("""
            {
              "type": "object",
              "properties": {
                "findings": { "type": "string", "description": "The answer: facts with path:startLine-endLine, exact excerpts only where the question needs them, and what could not be found." }
              },
              "required": ["findings"]
            }
            """));

    private static JsonObject AskUserDefinition() =>
        ChatMessages.Function(AskUser,
            "Ask the developer one clarifying question and pause until they answer; their reply comes back as this tool's result. Only for a genuine ambiguity, a risky change they did not clearly ask for, or a fact the code and memory cannot tell you. Investigate first. Never ask to confirm a clear request.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "question": { "type": "string", "description": "One specific question in the developer's language, with the context needed to answer it (what you found and why it matters)." },
                "options": {
                  "type": "array",
                  "description": "2-4 concrete answers, your recommended one first. The developer may also answer in their own words.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "label": { "type": "string", "description": "Short answer, in the developer's language." },
                      "description": { "type": "string", "description": "What choosing it means for the change." }
                    },
                    "required": ["label"]
                  }
                }
              },
              "required": ["question"]
            }
            """));

    private static JsonObject SubmitPlanDefinition(bool canTest)
    {
        var schema = SubmitPlanSchema();
        if (canTest)
        {
            schema["properties"]!.AsObject()["test"] = TestSchema();
            schema["required"]!.AsArray().Add("test");
        }
        return ChatMessages.Function(SubmitPlan,
            "Submit the final plan. Call exactly once, after investigating. For requests that need no file change, send an empty tasks list and the full answer in \"answer\"." +
            (canTest ? " When tasks change files, \"test\" tells the tester how to check the change on the running app." : string.Empty),
            schema);
    }

    private static JsonObject TestSchema() => Schema("""
        {
          "type": "object",
          "description": "How the tester checks the finished change on the running app. Ignored when tasks is empty.",
          "properties": {
            "level": { "type": "string", "enum": ["ui", "api", "none"], "description": "ui: the change shows in a page (the tester drives a visible browser and may call endpoints too); api: only reachable through HTTP endpoints; none: nothing can be exercised from outside (say why in notes)." },
            "baseUrl": { "type": "string", "description": "Root URL of the app running on this machine, e.g. \"http://localhost:5000\" (localhost only)." },
            "notes": { "type": "string", "description": "What the tester must know first (menu path to the page, login, data that must exist), or why level is none." },
            "scenarios": {
              "type": "array",
              "description": "1-5 scenarios proving the request works: the main path first, then the edge cases the change handles.",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string", "description": "Short name, in the developer's language." },
                  "target": { "type": "string", "description": "Page path or endpoint the scenario starts at, e.g. \"/User\" or \"POST /api/users\"." },
                  "steps": { "type": "array", "items": { "type": "string" }, "description": "What to do, with the exact element ids, field values and request bodies from the contract." },
                  "expect": { "type": "string", "description": "What must be seen when it works: text, value, status code, response field." }
                },
                "required": ["title", "steps", "expect"]
              }
            }
          },
          "required": ["level"]
        }
        """);

    private static JsonObject SubmitPlanSchema() =>
        Schema("""
            {
              "type": "object",
              "properties": {
                "summary": { "type": "string", "description": "One or two sentences, in the developer's language, describing what will be done." },
                "answer": { "type": "string", "description": "Complete answer for requests that need no file changes." },
                "contract": {
                  "type": "array",
                  "description": "Names shared between tasks/files that every executor must use verbatim.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "name": { "type": "string", "description": "What it is, e.g. \"UI input id\", \"DTO property\", \"DB column\"." },
                      "value": { "type": "string", "description": "The exact identifier/value, e.g. \"txtDescription\", \"Description\", \"Description NVARCHAR(500) NULL\"." },
                      "note": { "type": "string" }
                    },
                    "required": ["name", "value"]
                  }
                },
                "tasks": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "id": { "type": "string", "description": "Short id: T1, T2, ..." },
                      "title": { "type": "string" },
                      "file": { "type": "string", "description": "Target file relative to the project root. Files can only be changed inside the project root." },
                      "action": { "type": "string", "enum": ["edit", "create", "delete", "move", "extract"], "description": "delete, move and extract are done by AIT without an executor; move keeps the content unchanged; extract moves C# members from file to to." },
                      "to": { "type": "string", "description": "For action move: the destination path relative to the project root; it must not exist yet. For action extract: the .cs file that receives the members (new, or already a partial part of the same class)." },
                      "members": { "type": "array", "items": { "type": "string" }, "description": "Only for action extract: names of the C# members to move (methods, properties, fields, events, nested types). All overloads of a name move together." },
                      "instruction": { "type": "string", "description": "Self-contained instruction for the executor. For delete, move and extract, a short reason is enough." },
                      "focus": {
                        "type": "array",
                        "description": "Line ranges of the target file around the change (needed for large files).",
                        "items": {
                          "type": "object",
                          "properties": { "startLine": { "type": "integer" }, "endLine": { "type": "integer" } },
                          "required": ["startLine", "endLine"]
                        }
                      },
                      "context": {
                        "type": "array",
                        "description": "Read-only excerpts from other files the executor must see (the files may also lie outside the project, given as absolute paths).",
                        "items": {
                          "type": "object",
                          "properties": {
                            "file": { "type": "string" },
                            "startLine": { "type": "integer" },
                            "endLine": { "type": "integer" },
                            "note": { "type": "string" }
                          },
                          "required": ["file"]
                        }
                      },
                      "dependsOn": { "type": "array", "items": { "type": "string" } }
                    },
                    "required": ["id", "title", "file", "action", "instruction"]
                  }
                }
              },
              "required": ["summary", "tasks"]
            }
            """);

    private static JsonObject Schema(string json) => (JsonObject)JsonNode.Parse(json)!;
}
