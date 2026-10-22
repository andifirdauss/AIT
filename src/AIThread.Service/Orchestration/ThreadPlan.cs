using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AIThread.Service.Orchestration;

// The planner's output, delivered through the submit_plan tool.
public sealed class ThreadPlan
{
    public string Summary { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public List<ContractItem> Contract { get; set; } = [];
    public List<PlanTask> Tasks { get; set; } = [];

    // Only while the planner node has Auto Testing on: how the tester node can check the change on the running app.
    public TestPlan? Test { get; set; }

    // Only while the project has a unit test catalog: decisions about registered tests, new tests, or (without tasks)
    // the tests to run.
    public PlanTests? Tests { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    // Accepts the plan either at the root or wrapped as {"plan": {...}}.
    public static bool TryParse(string json, out ThreadPlan? plan, out string? error)
    {
        plan = null;
        error = null;
        try
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (node is JsonObject obj && obj["plan"] is JsonObject inner && obj["tasks"] is null) node = inner;
            if (node is not JsonObject) throw new JsonException("The plan must be a JSON object.");

            plan = node.Deserialize<ThreadPlan>(JsonOptions);
            if (plan is null) throw new JsonException("Empty plan.");
            plan.Contract ??= [];
            plan.Tasks ??= [];
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // For models that ignore tool calling and print the plan as text.
    public static bool TryParseFromText(string text, out ThreadPlan? plan)
    {
        plan = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return false;

        if (!TryParse(text[start..(end + 1)], out var parsed, out _) || parsed is null) return false;
        if (parsed.Tasks.Count == 0 && string.IsNullOrWhiteSpace(parsed.Answer)) return false;
        plan = parsed;
        return true;
    }
}

public sealed class ContractItem
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed class PlanTask
{
    public const string Edit = "edit";
    public const string Create = "create";
    public const string Delete = "delete";
    public const string Move = "move";
    public const string Extract = "extract";

    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Action { get; set; } = Edit;

    // Destination of a move or an extract, relative to the project root.
    public string? To { get; set; }

    // Extract only: names of the C# members to move from File to To (all overloads of a name move together).
    public List<string>? Members { get; set; }

    public string Instruction { get; set; } = string.Empty;
    public List<LineRange>? Focus { get; set; }
    public List<ContextRef>? Context { get; set; }
    public List<string>? DependsOn { get; set; }

    [JsonIgnore]
    public bool IsCreate => string.Equals(Action, Create, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsDelete => string.Equals(Action, Delete, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsMove => string.Equals(Action, Move, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsExtract => string.Equals(Action, Extract, StringComparison.OrdinalIgnoreCase);

    // Delete, move and extract are done by AIThread itself; no executor model is involved.
    [JsonIgnore]
    public bool IsFileOperation => IsDelete || IsMove || IsExtract;

    // The file as shown to people and models: "a.cs → b.cs" for a move or an extract.
    [JsonIgnore]
    public string Target => (IsMove || IsExtract) && !string.IsNullOrWhiteSpace(To) ? $"{File} → {To}" : File;
}

// The planner's picture of how the finished change shows from outside: in pages (ui: the tester drives a visible
// browser and may call endpoints too), only through HTTP endpoints (api), or not at all (none, with the reason in Notes).
public sealed class TestPlan
{
    public const string Ui = "ui";
    public const string Api = "api";
    public const string None = "none";

    public string Level { get; set; } = None;

    // Root of the app running on this machine, e.g. "http://localhost:5000". Loopback only (TestUrls).
    public string? BaseUrl { get; set; }

    // What the tester must know first (menu path, login, data that must exist), or why nothing can be tested.
    public string? Notes { get; set; }

    public List<TestScenario> Scenarios { get; set; } = [];

    [JsonIgnore]
    public bool IsRunnable => Level is Ui or Api;
}

public sealed class TestScenario
{
    public string Title { get; set; } = string.Empty;

    // Page path or endpoint the scenario starts at: "/User", "POST /api/users".
    public string? Target { get; set; }

    public List<string> Steps { get; set; } = [];
    public string Expect { get; set; } = string.Empty;

    // The test account (username) or the role of one that does the scenario, from "# Test accounts"; null: any login.
    public string? Actor { get; set; }

    // What the step does in a flow (submit, approve, reject, edit, ...); set by the tester's follow-up suggestions.
    public string? Action { get; set; }
}

// The unit test part of a plan. A registered test the change concerns is kept unless a decision says otherwise.
public sealed class PlanTests
{
    public const string Keep = "keep";
    public const string Update = "update";
    public const string Obsolete = "obsolete";
    public const string All = "all";

    public List<PlanTestDecision> Decisions { get; set; } = [];
    public List<PlanNewTest> New { get; set; } = [];

    // Only with an empty task list: the test numbers to run, or "all".
    public List<string> Run { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Decisions.Count == 0 && New.Count == 0 && Run.Count == 0;

    public PlanTestDecision? DecisionFor(string number) =>
        Decisions.FirstOrDefault(d => string.Equals(d.Test, number, StringComparison.OrdinalIgnoreCase));
}

public sealed class PlanTestDecision
{
    // The test's number, e.g. "UT-0012".
    public string Test { get; set; } = string.Empty;

    // keep | update | obsolete
    public string Decision { get; set; } = PlanTests.Keep;
    public string? Reason { get; set; }
}

// A test the plan adds. Name is the test as it appears in the code: the method name, or the it()/test() title.
public sealed class PlanNewTest
{
    public string Title { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string>? Targets { get; set; }

    // database | network | config
    public List<string>? Requires { get; set; }
}

public sealed class LineRange
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public sealed class ContextRef
{
    public string File { get; set; } = string.Empty;
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public string? Note { get; set; }
}
