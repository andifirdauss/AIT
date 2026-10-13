using System.Text.Json.Nodes;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

// OpenAI function definitions offered to the planner.
public static class PlannerTools
{
    public const string ListDirectory = "list_directory";
    public const string FindFiles = "find_files";
    public const string SearchText = "search_text";
    public const string ReadFile = "read_file";
    public const string SearchMemory = "search_memory";
    public const string ReadMemory = "read_memory";
    public const string ReadThread = "read_thread";
    public const string RunBuild = "run_build";
    public const string AskUser = "ask_user";
    public const string SubmitPlan = "submit_plan";

    // Column the arguments of a tool call start at in the progress (longest tool name plus two spaces).
    public static readonly int NameColumn =
        new[] { ListDirectory, FindFiles, SearchText, ReadFile, SearchMemory, ReadMemory, ReadThread, RunBuild, AskUser }.Max(n => n.Length) + 2;

    // canBuild: build verification is enabled and the project has a build command AiThread can run.
    // canAsk: the developer can answer a question (setting on, and the prompt came from a session that can reply).
    // The list stays the same for the whole planner run so the provider's prompt cache keeps matching.
    public static JsonArray All(bool canBuild = false, bool canAsk = false)
    {
        var tools = Investigation();
        if (canBuild) tools.Insert(tools.Count - 1, RunBuildDefinition());
        if (canAsk) tools.Insert(tools.Count - 1, AskUserDefinition());
        return tools;
    }

    private static JsonArray Investigation() =>
    [
        ChatMessages.Function(ListDirectory,
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
        ChatMessages.Function(FindFiles,
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
        ChatMessages.Function(SearchText,
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
        ChatMessages.Function(ReadFile,
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
        ChatMessages.Function(SearchMemory,
            "Search memory labels (project facts, reusable knowledge and the project's business wiki pages) by keywords. Returns ids and labels only.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Keywords, e.g. \"user validation description\"." }
              },
              "required": ["query"]
            }
            """)),
        ChatMessages.Function(ReadMemory,
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
        ChatMessages.Function(ReadThread,
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
        SubmitPlanDefinition()
    ];

    public static JsonArray SubmitOnly() => [SubmitPlanDefinition()];

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
