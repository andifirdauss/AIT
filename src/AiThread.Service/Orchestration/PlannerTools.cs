using System.Collections.Immutable;
using System.Text.Json.Nodes;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

// OpenAI function definitions offered to the planner, and to the tools nodes it delegates tools to.
public static class PlannerTools
{
    public const string ListDirectory = "list_directory";
    public const string FindFiles = "find_files";
    public const string SearchText = "search_text";
    public const string FindSymbol = "find_symbol";
    public const string FindReferences = "find_references";
    public const string ReadFile = "read_file";
    public const string SearchMemory = "search_memory";
    public const string ReadMemory = "read_memory";
    public const string ReadThread = "read_thread";
    public const string RunBuild = "run_build";
    public const string Investigate = "investigate";
    public const string AskUser = "ask_user";
    public const string SetModule = "set_module";
    public const string SubmitPlan = "submit_plan";

    // Ends a tools node's investigation (a reply without tool calls does too).
    public const string ReportFindings = "report_findings";

    // Tools a planner can hand to a tools node (PlannerToolDelegation), in the order they are offered.
    public static readonly ImmutableArray<string> Delegable =
        [ListDirectory, FindFiles, SearchText, FindSymbol, FindReferences, ReadFile, SearchMemory, ReadMemory, ReadThread, RunBuild];

    // Column the arguments of a tool call start at in the progress (longest tool name plus two spaces).
    public static readonly int NameColumn = Delegable.Append(Investigate).Append(AskUser).Max(n => n.Length) + 2;

    // canBuild: build verification is enabled and the project has a build command AiThread can run.
    // canAsk: the developer can answer a question (setting on, and the prompt came from a session that can reply).
    // groups: tools delegated to tools nodes; the planner loses them and gets investigate instead.
    // The list stays the same for the whole planner run so the provider's prompt cache keeps matching.
    public static JsonArray All(bool canBuild = false, bool canAsk = false, IReadOnlyList<ToolsNodeGroup>? groups = null)
    {
        var delegated = groups?.SelectMany(g => g.Tools).ToHashSet() ?? [];
        var tools = new JsonArray();
        foreach (var name in Delegable)
        {
            if (delegated.Contains(name) || (name == RunBuild && !canBuild)) continue;
            tools.Add(Definition(name));
        }
        tools.Add(SetModuleDefinition());
        if (groups is { Count: > 0 }) tools.Add(InvestigateDefinition(groups));
        if (canAsk) tools.Add(AskUserDefinition());
        tools.Add(SubmitPlanDefinition());
        return tools;
    }

    public static JsonArray SubmitOnly() => [SubmitPlanDefinition()];

    // The tools of one tools node, plus report_findings.
    public static JsonArray ForToolsNode(IEnumerable<string> tools)
    {
        var wanted = tools.ToHashSet();
        var result = new JsonArray();
        foreach (var name in Delegable.Where(wanted.Contains)) result.Add(Definition(name));
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

    private static JsonObject SubmitPlanDefinition() =>
        ChatMessages.Function(SubmitPlan,
            "Submit the final plan. Call exactly once, after investigating. For requests that need no file change, send an empty tasks list and the full answer in \"answer\".",
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
            """));

    private static JsonObject Schema(string json) => (JsonObject)JsonNode.Parse(json)!;
}
