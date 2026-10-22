using System.Collections.Immutable;
using System.Text.Json.Nodes;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

// OpenAI function definitions offered to the tester node: the browser for ui tests, HTTP requests for both levels.
public static class TesterTools
{
    public const string HttpRequest = "http_request";
    public const string BrowserOpen = "browser_open";
    public const string BrowserSnapshot = "browser_snapshot";
    public const string BrowserClick = "browser_click";
    public const string BrowserFill = "browser_fill";
    public const string BrowserSelect = "browser_select";
    public const string BrowserPress = "browser_press";
    public const string BrowserWait = "browser_wait";

    // Ends the test with a verdict per scenario.
    public const string ReportTest = "report_test";

    // Ends the test to ask the developer for a login; only when the developer can answer in the chat.
    public const string AskLogin = "ask_login";

    // Acts as one of the project's test accounts; only when the project has some.
    public const string SwitchUser = "switch_user";

    // Keeps a value (a document number) for later steps and follow-up tests, used as {{name}}.
    public const string Remember = "remember";

    // Placeholders AIT fills itself; remember may not take these names.
    public static readonly ImmutableArray<string> ReservedNames = ["username", "password", "target"];

    public static readonly ImmutableArray<string> Browser =
        [BrowserOpen, BrowserSnapshot, BrowserClick, BrowserFill, BrowserSelect, BrowserPress, BrowserWait];

    public static readonly ImmutableArray<string> Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    // ui: the browser and http_request; api: http_request only. canAskLogin adds ask_login, canSwitchUser switch_user.
    public static IReadOnlyList<string> Available(string level, bool canAskLogin = false, bool canSwitchUser = false) =>
        (level == TestPlan.Ui ? [.. Browser, HttpRequest] : new[] { HttpRequest })
        .Concat([Remember])
        .Concat(canSwitchUser ? [SwitchUser] : [])
        .Concat(canAskLogin ? [AskLogin] : []).ToList();

    // The same list for the whole run, so the provider's prompt cache keeps matching.
    public static JsonArray For(string level, bool canAskLogin = false, bool canSwitchUser = false)
    {
        var tools = new JsonArray();
        foreach (var name in Available(level, canAskLogin, canSwitchUser)) tools.Add(name == AskLogin ? AskLoginDefinition() : Definition(name));
        tools.Add(ReportDefinition());
        return tools;
    }

    public static JsonArray ReportOnly() => [ReportDefinition()];

    // A new object on every call: a JsonNode can only belong to one array.
    private static JsonObject Definition(string name) => name switch
    {
        HttpRequest => ChatMessages.Function(HttpRequest,
            "Send an HTTP request to the app and get the status, the main headers and the body. Cookies set by earlier responses are sent again (log in once, then call protected endpoints). Redirects are not followed: a 302 shows its Location.",
            Schema($$"""
            {
              "type": "object",
              "properties": {
                "method": { "type": "string", "enum": [{{string.Join(", ", Methods.Select(m => $"\"{m}\""))}}] },
                "url": { "type": "string", "description": "Path resolved against the app's base URL, e.g. \"/api/users?page=1\", or an absolute localhost URL." },
                "headers": { "type": "object", "additionalProperties": { "type": "string" }, "description": "Extra request headers, e.g. {\"Accept\": \"application/json\"}." },
                "body": { "description": "Request body: a JSON object or array is sent as application/json; a string is sent as it is (set Content-Type in headers for form data)." }
              },
              "required": ["method", "url"]
            }
            """)),
        BrowserOpen => ChatMessages.Function(BrowserOpen,
            "Open a page of the app in the visible tester browser and get its snapshot. Only localhost URLs.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "url": { "type": "string", "description": "Path resolved against the app's base URL, e.g. \"/User\", or an absolute localhost URL." }
              },
              "required": ["url"]
            }
            """)),
        BrowserSnapshot => ChatMessages.Function(BrowserSnapshot,
            "Get the current page again: URL, title, new console errors and failed requests, and the accessibility snapshot with element refs.",
            Schema("""{ "type": "object", "properties": {} }""")),
        BrowserClick => ChatMessages.Function(BrowserClick,
            "Click an element (button, link, checkbox, tab, row) and get the page after it.",
            ElementSchema(null)),
        BrowserFill => ChatMessages.Function(BrowserFill,
            "Replace the value of a text field (input, textarea, contenteditable) and get the page after it.",
            ElementSchema("""
                "text": { "type": "string", "description": "The value to type; an empty string clears the field." },
                "submit": { "type": "boolean", "description": "Press Enter afterwards." }
            """, "text")),
        BrowserSelect => ChatMessages.Function(BrowserSelect,
            "Choose options of a <select> by value or visible label and get the page after it.",
            ElementSchema("""
                "values": { "type": "array", "items": { "type": "string" }, "description": "Option values or labels; several only for a multiple select." }
            """, "values")),
        BrowserPress => ChatMessages.Function(BrowserPress,
            "Press a key or a combination in the focused element, e.g. \"Enter\", \"Escape\", \"Tab\", \"Control+A\", and get the page after it.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "key": { "type": "string" }
              },
              "required": ["key"]
            }
            """)),
        BrowserWait => ChatMessages.Function(BrowserWait,
            "Wait until a text appears (or disappears) on the page, or simply wait some seconds, then get the page. Use it after actions that load data.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "text": { "type": "string", "description": "Text to wait for; leave out to wait the given seconds." },
                "gone": { "type": "boolean", "description": "Wait until the text disappears instead (e.g. a loading message)." },
                "seconds": { "type": "number", "description": "Maximum wait, 0.5-15. Default 5." }
              }
            }
            """)),
        Remember => ChatMessages.Function(Remember,
            "Keep a value the app showed (the number of a document you created, an id) for later steps and follow-up tests: write {{name}} in a later tool call and AIT puts the value in. Values stay with the thread, so a follow-up test can approve the document this run submitted.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Letters, digits and _, starting with a letter, e.g. \"requestNo\"." },
                "value": { "type": "string", "description": "The value as the app shows it, e.g. \"REQ-0123\"." }
              },
              "required": ["name", "value"]
            }
            """)),
        SwitchUser => ChatMessages.Function(SwitchUser,
            "Act as one of the project's test accounts (\"# Test accounts\"): the browser window closes and the next browser tool opens that account's own browser profile, http_request uses its own cookies, and {{username}} / {{password}} stand for its login. Switch before a step another user must do (an approver, the requester again).",
            Schema("""
            {
              "type": "object",
              "properties": {
                "account": { "type": "string", "description": "The username from \"# Test accounts\"." }
              },
              "required": ["account"]
            }
            """)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown tester tool.")
    };

    // The element of an action: a ref from the latest snapshot, or a selector; element is what the developer reads.
    private static JsonObject ElementSchema(string? extraProperties, params string[] extraRequired)
    {
        var schema = Schema($$"""
            {
              "type": "object",
              "properties": {
                "element": { "type": "string", "description": "What the element is, in a few words (e.g. \"Save button\", \"Description field\"); shown to the developer." },
                "ref": { "type": "string", "description": "Ref of the element in the latest snapshot, e.g. \"e12\"." },
                "selector": { "type": "string", "description": "CSS or Playwright selector when the element has no clear ref, e.g. \"#txtDescription\" or \"text=Simpan\"." }{{(extraProperties is null ? string.Empty : "," + extraProperties)}}
              },
              "required": ["element"]
            }
            """);
        foreach (var name in extraRequired) schema["required"]!.AsArray().Add(name);
        return schema;
    }

    private static JsonObject AskLoginDefinition() =>
        ChatMessages.Function(AskLogin,
            "Stop and ask the developer for a login, when the app asks for one and the brief gives none, or the one it gives is refused. The test ends here; the developer answers in the chat and the whole test runs again with that login. Never guess credentials.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "reason": { "type": "string", "description": "What needs the login and what you saw, in the developer's language, e.g. \"/User mengalihkan ke halaman login\" or \"login ditolak: Password salah\"." },
                "loginUrl": { "type": "string", "description": "The login page or endpoint, e.g. \"/Account/Login\"." },
                "kind": { "type": "string", "enum": ["login", "dev_password"], "description": "dev_password: a test account's login was refused with the project's development password (the developer gives the right one, AIT keeps it for the project). login (default): any other missing or refused login." }
              },
              "required": ["reason"]
            }
            """));

    private static JsonObject ReportDefinition() =>
        ChatMessages.Function(ReportTest,
            "Finish the test and report a verdict for every scenario of the plan. Call it once, when every scenario is checked or cannot be.",
            Schema("""
            {
              "type": "object",
              "properties": {
                "summary": { "type": "string", "description": "One or two sentences on what works and what does not, in the developer's language." },
                "scenarios": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "title": { "type": "string", "description": "The scenario title from the plan." },
                      "status": { "type": "string", "enum": ["passed", "failed", "blocked"] },
                      "details": { "type": "string", "description": "What you saw: for failed what happened instead, for blocked why it could not be checked." },
                      "actor": { "type": "string", "description": "The test account you checked it as (switch_user), if any." }
                    },
                    "required": ["title", "status", "details"]
                  }
                },
                "follow_ups": {
                  "type": "array",
                  "description": "0-5 further tests worth running next, when what you checked starts a flow that goes on (an approval per level, edit or cancel before approval, reject and resubmit, the notification or report that follows). Not for what the scenarios already covered.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "title": { "type": "string", "description": "Short name in the developer's language, e.g. \"Approve level 1 oleh atasan\"." },
                      "why": { "type": "string", "description": "Why it matters, one sentence." },
                      "needs": { "type": "string", "description": "What the developer must provide first (an account with a role, data), if anything." },
                      "scenarios": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "properties": {
                            "title": { "type": "string" },
                            "actor": { "type": "string", "description": "Username or role from \"# Test accounts\" that does it." },
                            "target": { "type": "string" },
                            "steps": { "type": "array", "items": { "type": "string" }, "description": "May use {{name}} values you remembered." },
                            "expect": { "type": "string" }
                          },
                          "required": ["title", "steps", "expect"]
                        }
                      }
                    },
                    "required": ["title", "scenarios"]
                  }
                },
                "impersonation": {
                  "type": "object",
                  "description": "Only when you saw a \"login as\" / impersonate feature in the app (a button or menu to enter as another user) that the brief does not mention: how to use it, so AIT keeps it for the project.",
                  "properties": {
                    "how": { "type": "string", "description": "Menu path, URL or endpoint, with {{target}} for the user to enter as." },
                    "back": { "type": "string", "description": "How to return to the original account, if shown." },
                    "roles": { "type": "array", "items": { "type": "string" }, "description": "Roles of the account you saw it with, e.g. [\"Administrator\"]." }
                  },
                  "required": ["how"]
                }
              },
              "required": ["summary", "scenarios"]
            }
            """));

    private static JsonObject Schema(string json) => (JsonObject)JsonNode.Parse(json)!;
}
