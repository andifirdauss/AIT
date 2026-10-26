using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIThread.API.Controllers;
using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Service.Implementations;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using AIThread.Service.Security;
using AIThread.Service.Workspace;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIThread.Tests;

// Secrets never reach a model (SecretRules, SecretGuard), come back only where AIT writes, and credential files are
// never read (SecretPaths, SecretPolicy, WorkspaceScope).
public sealed class SecretGuardTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    // One "machine": every guard of a test reads the same key file through the same protection keys.
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    public void Dispose() => _workspace.Dispose();

    private SecretGuard Guard(string? keyPath = null, IDataProtectionProvider? protection = null) => new(
        protection ?? _protection,
        Options.Create(new AIThreadOptions { SecretKeyPath = keyPath ?? Path.Combine(_workspace.Root, "secret-mask.key") }),
        NullLogger<SecretGuard>.Instance);

    private static string Placeholder(string text) => Regex.Match(text, @"\{\{AIT_SECRET:[^}]+\}\}").Value;

    [Fact]
    public void Appsettings_values_are_masked_and_the_setting_names_stay()
    {
        var guard = Guard();
        const string json = """
            {
              "ConnectionStrings": { "Default": "Server=.;Database=Toko;User Id=sa;Password=Sup3r#Secret;TrustServerCertificate=True" },
              "Jwt": { "Key": "k8sJwtSigningKey-2024-xyz", "Issuer": "toko" },
              "Smtp": { "Password": "mail-pass-123", "PasswordLength": "12" },
              "Identity": { "RequireUppercase": true }
            }
            """;

        var masked = guard.RedactFile("appsettings.json", json);

        Assert.DoesNotContain("Sup3r#Secret", masked);
        Assert.DoesNotContain("k8sJwtSigningKey-2024-xyz", masked);
        Assert.DoesNotContain("mail-pass-123", masked);
        Assert.Contains("Server=.;Database=Toko;User Id=sa;Password={{AIT_SECRET:Password:", masked);
        Assert.Contains("\"Issuer\": \"toko\"", masked);
        Assert.Contains("\"PasswordLength\": \"12\"", masked);
        Assert.Equal(json, guard.Restore(masked).Text);
    }

    [Fact]
    public void Env_and_config_lines_are_masked_but_code_is_left_alone()
    {
        var guard = Guard();

        var env = guard.RedactFile(".env", "DB_HOST=localhost\nDB_PASSWORD=hunter2pass\nAPP_KEY=base64:QUJDREVGRw==\n");
        Assert.Contains("DB_HOST=localhost", env);
        Assert.DoesNotContain("hunter2pass", env);
        Assert.DoesNotContain("QUJDREVGRw==", env);

        var yaml = guard.RedactFile("config/app.yml", "db:\n  user: app\n  password: yamlSecret9\njwt:\n  key: yamlJwtKey42\ni18n:\n  key: Errors\n");
        Assert.DoesNotContain("yamlSecret9", yaml);
        Assert.DoesNotContain("yamlJwtKey42", yaml);
        Assert.Contains("user: app", yaml);
        Assert.Contains("key: Errors", yaml);

        const string code = """
            var password = request.Password;
            if (user.PasswordHash == null) return;
            options.Password.RequiredLength = 8;
            def login(password: str) -> bool:
            """;
        Assert.Equal(code, guard.RedactFile("Login.cs", code));
        Assert.Equal(code, guard.Redact(code));

        var literal = guard.RedactFile("Seed.cs", "user.Password = \"Admin@12345\";");
        Assert.DoesNotContain("Admin@12345", literal);
    }

    [Fact]
    public void Known_token_formats_and_private_keys_are_masked_anywhere()
    {
        var guard = Guard();
        var github = "ghp_" + new string('a', 18) + "B1c2D3e4F5g6H7i8J9";
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
        const string key = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEAu1SU1LfVLPHCozMxH2Mo4lgOEePzNm0tRgeLezV6ffAt0gunVTLw\n-----END RSA PRIVATE KEY-----";

        var masked = guard.Redact($"token {github}\nAuthorization: Bearer {jwt}\n{key}\npostgres://app:pg-Pa55word@db:5432/toko");

        Assert.DoesNotContain(github, masked);
        Assert.DoesNotContain(jwt, masked);
        Assert.DoesNotContain("MIIEowIBAAKCAQEA", masked);
        Assert.DoesNotContain("pg-Pa55word", masked);
        Assert.Contains("postgres://app:{{AIT_SECRET:url-password:", masked);
        Assert.Contains("{{AIT_SECRET:jwt:", masked);
    }

    [Fact]
    public void A_secret_keeps_its_placeholder_across_restarts_and_is_masked_wherever_it_appears_later()
    {
        var keyPath = Path.Combine(_workspace.Root, "keys", "secret-mask.key");
        var protection = new EphemeralDataProtectionProvider();
        var first = Guard(keyPath, protection);
        var masked = first.RedactFile("appsettings.json", "{ \"ApiKey\": \"Zx9-Distinct-Value\" }");
        var placeholder = Placeholder(masked);

        // The same machine key: the same placeholder, and a restarted AIT can map it back after reading the file again.
        var second = Guard(keyPath, protection);
        Assert.Equal(placeholder, Placeholder(second.RedactFile("appsettings.json", "{ \"ApiKey\": \"Zx9-Distinct-Value\" }")));

        // Pasted into a prompt with no setting name around it.
        Assert.Equal($"pakai {placeholder} untuk test", first.Redact("pakai Zx9-Distinct-Value untuk test"));
    }

    [Fact]
    public void A_code_name_is_masked_where_it_is_the_secret_but_not_wherever_it_appears()
    {
        var guard = Guard();

        var masked = guard.Redact("ref_token: \"LunchClaimRateHeaderDto\"");
        Assert.DoesNotContain("LunchClaimRateHeaderDto", masked);

        // Class and file names stay readable, so the files behind them can still be read and edited.
        const string code = "public partial class LunchClaimRateHeaderDto // NewESS.Common/DTO/ESS/LunchClaimRateHeaderDto.cs";
        Assert.Equal(code, guard.Redact(code));
        Assert.False(SecretRules.IsDistinctive("NewESS.Common.Dto"));
        Assert.True(SecretRules.IsDistinctive("Zx9-Distinct-Value"));
    }

    [Fact]
    public async Task A_file_name_the_model_only_saw_masked_can_still_be_read_and_changed()
    {
        await using var harness = await ThreadOrchestratorTests.CreateAsync();
        const string file = "src/Rate9Header.cs";
        const string active = "    public bool BolActive { get; set; }";
        const string withMode = active + "\n    public string? TxtMode { get; set; }";
        harness.Project.Write(file, $"namespace Demo;\n\npublic class Rate9Header\n{{\n{active}\n}}\n");

        // An earlier output masked the name, so the model only knows the file by its placeholder.
        var placeholder = Placeholder(harness.Service<ISecretGuard>().Redact("ref_token: \"Rate9Header\""));
        var maskedFile = $"src/{placeholder}.cs";
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.ReadFile, new { path = maskedFile }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Tambah TxtMode.",
                Tasks = [ThreadOrchestratorTests.Step("T1", maskedFile, "Add TxtMode after BolActive.")]
            }))
            .OnExecutor(file, ScriptedRouter.Edit(active, withMode, "TxtMode ditambahkan."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("TxtMode ditambahkan."));

        var (result, _) = await harness.RunAsync("Tambah TxtMode di DTO header");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        var read = router.CallsTo(ScriptedAgent.Planner)[1].Messages.OfType<JsonObject>().Single(m => m["role"]?.GetValue<string>() == "tool");
        Assert.Contains("BolActive", read["content"]!.GetValue<string>());
        Assert.Contains("public string? TxtMode { get; set; }", harness.ReadFile(file));
    }

    [Fact]
    public void A_value_marked_as_not_secret_is_left_alone_and_its_old_placeholders_still_come_back()
    {
        var guard = Guard();
        var placeholder = Placeholder(guard.Redact("ref_token: \"Rate9Header\""));
        guard.Redact("pakai Rate9Header lagi");

        var seen = Assert.Single(guard.GetSightings());
        Assert.Equal("ref_token", seen.Label);
        Assert.Equal(2, seen.Count);
        Assert.Equal(11, seen.Length);
        Assert.DoesNotContain("Rate9Header", seen.Preview);
        Assert.Equal("Rate9Header", guard.Reveal(seen.Id));

        guard.SetAllowed([seen.Id]);
        Assert.Equal("ref_token: \"Rate9Header\"", guard.Redact("ref_token: \"Rate9Header\""));
        Assert.Equal("pakai Rate9Header lagi", guard.Redact("pakai Rate9Header lagi"));
        Assert.Equal("src/Rate9Header.cs", guard.Restore($"src/{placeholder}.cs").Text);

        guard.SetAllowed([]);
        Assert.Equal(placeholder, Placeholder(guard.Redact("ref_token: \"Rate9Header\"")));
    }

    [Fact]
    public async Task The_not_secret_list_is_stored_and_applied_again_with_the_settings()
    {
        await using var harness = await ThreadOrchestratorTests.CreateAsync();
        var guard = harness.Service<ISecretGuard>();
        var security = harness.Service<ISecurityService>();
        guard.Redact("ref_token: \"Rate9Header\"");
        var detected = Assert.Single((await security.GetSecretsAsync()).Items);
        Assert.False(detected.Allowed);

        Assert.True(await security.AllowSecretAsync(detected.Id));
        Assert.False(await security.AllowSecretAsync("0123456789"));
        var allowed = Assert.Single((await security.GetSecretsAsync()).Items);
        Assert.True(allowed.Allowed);
        Assert.Equal("ref_token", allowed.Label);

        // As after a restart: the list comes back from the database with the settings.
        guard.SetAllowed([]);
        await harness.Service<ISettingService>().GetRuntimeAsync();
        Assert.Equal("ref_token: \"Rate9Header\"", guard.Redact("ref_token: \"Rate9Header\""));

        Assert.True(await security.DisallowSecretAsync(detected.Id));
        Assert.DoesNotContain("Rate9Header", guard.Redact("ref_token: \"Rate9Header\""));
        Assert.False(Assert.Single((await security.GetSecretsAsync()).Items).Allowed);
    }

    [Fact]
    public void Unknown_placeholders_are_reported_instead_of_written()
    {
        var restore = Guard().Restore("Password={{AIT_SECRET:Password:0123456789}}");

        Assert.False(restore.Complete);
        Assert.Equal("{{AIT_SECRET:Password:0123456789}}", Assert.Single(restore.Unknown));
    }

    [Fact]
    public void Tool_input_is_restored_after_learning_the_file_it_changes()
    {
        var file = _workspace.Write("appsettings.json", "{ \"Smtp\": { \"Password\": \"old-Pass-77\" } }");
        var placeholder = Placeholder(Guard().RedactFile(file, File.ReadAllText(file)));

        // A fresh guard (e.g. after a restart) with the same key file.
        var input = new JsonObject { ["file_path"] = file, ["old_string"] = $"\"Password\": \"{placeholder}\"", ["new_string"] = "\"Password\": \"new\"" };
        var restore = Guard().RestoreJson(input, file);

        Assert.True(restore.Complete);
        Assert.Equal(1, restore.Restored);
        Assert.Equal("\"Password\": \"old-Pass-77\"", input["old_string"]!.GetValue<string>());
    }

    [Fact]
    public void Passthrough_requests_keep_signed_and_binary_fields_untouched()
    {
        var guard = Guard();
        var body = (JsonObject)JsonNode.Parse("""
            {
              "model": "claude",
              "messages": [
                { "role": "assistant", "content": [ { "type": "thinking", "thinking": "DB_PASSWORD=thinkSecret1", "signature": "sig" } ] },
                { "role": "user", "content": [ { "type": "tool_result", "tool_use_id": "t1", "content": "DB_PASSWORD=toolSecret22" } ] }
              ]
            }
            """)!;

        Assert.Equal(1, guard.RedactJson(body));
        var json = body.ToJsonString();
        Assert.Contains("thinkSecret1", json);
        Assert.DoesNotContain("toolSecret22", json);
    }

    [Fact]
    public void Model_messages_are_masked_in_place_and_counted_once_per_tally()
    {
        var guard = Guard();
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "DB_PASSWORD=msgSecret33" },
            new JsonObject { ["role"] = "assistant", ["content"] = null, ["tool_calls"] = new JsonArray
                { new JsonObject { ["id"] = "c1", ["type"] = "function", ["function"] = new JsonObject { ["name"] = "x", ["arguments"] = "{\"token\":\"argSecret-44x\"}" } } } }
        };

        using (guard.BeginTally())
        {
            guard.RedactMessages(messages);
            guard.RedactMessages(messages);
            Assert.Equal(2, guard.TallyCount);
        }

        Assert.DoesNotContain("msgSecret33", messages.ToJsonString());
        Assert.DoesNotContain("argSecret-44x", messages.ToJsonString());
        Assert.Equal(0, guard.TallyCount);
    }

    [Theory]
    [InlineData("Server=.;Database=App;Password=connSecret77", true)]
    [InlineData("Server=.;Database=App;Password=connSecret77", false)]
    [InlineData("Server=.;Password=connSecret77;Encrypt=False", true)]
    public void Tool_call_arguments_stay_a_json_object_after_masking(string connection, bool relaxed)
    {
        // A written appsettings.json is JSON inside the arguments' JSON: its quotes arrive escaped (\" or "), and a
        // value that ends the string must not take the escape with it.
        var guard = Guard();
        var file = new JsonObject { ["ConnectionStrings"] = new JsonObject { ["Db"] = connection } }.ToJsonString();
        var arguments = new JsonObject { ["path"] = "appsettings.json", ["content"] = file }
            .ToJsonString(relaxed ? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping } : null);
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "assistant", ["content"] = null, ["tool_calls"] = new JsonArray
                { new JsonObject { ["id"] = "c1", ["type"] = "function", ["function"] = new JsonObject { ["name"] = "write_file", ["arguments"] = arguments } } } }
        };

        var passthrough = new JsonObject { ["model"] = "m", ["messages"] = messages.DeepClone() };

        guard.RedactMessages(messages);
        guard.RedactJson(passthrough);

        foreach (var message in new[] { messages[0]!, passthrough["messages"]![0]! })
        {
            var masked = message["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>();
            var parsed = Assert.IsType<JsonObject>(JsonNode.Parse(masked));
            Assert.Equal("appsettings.json", parsed["path"]!.GetValue<string>());
            Assert.DoesNotContain("connSecret77", masked);
            Assert.Equal(file, guard.Restore(parsed["content"]!.GetValue<string>()).Text);
        }
    }

    [Fact]
    public void Masking_can_be_switched_off_but_placeholders_still_come_back()
    {
        var guard = Guard();
        var masked = guard.Redact("DB_PASSWORD=offSecret55");
        guard.Configure(enabled: false, maskPassthrough: true);

        Assert.Equal("DB_PASSWORD=plain66", guard.Redact("DB_PASSWORD=plain66"));
        Assert.False(guard.MaskPassthrough);
        Assert.Equal("DB_PASSWORD=offSecret55", guard.Restore(masked).Text);
    }

    [Theory]
    [InlineData("certs/server.pem", true)]
    [InlineData("keys/id_rsa", true)]
    [InlineData("keys/id_rsa.pub", false)]
    [InlineData(".npmrc", true)]
    [InlineData("deploy/.ssh/config", true)]
    [InlineData("src/appsettings.json", false)]
    [InlineData(".env", false)]
    public void Credential_files_are_protected(string relative, bool expected)
    {
        var scope = new WorkspaceScope(_workspace.Root);
        Assert.Equal(expected, scope.IsProtected(Path.Combine(_workspace.Root, relative.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void Project_rules_allow_and_deny_files_and_add_secret_keys()
    {
        _workspace.Write(".aithread/project.json", """
            {
              // comments are allowed
              "secrets": { "deny": ["config/production/**"], "allow": ["tests/certs/test.pem"], "keys": ["LicenseCode"] }
            }
            """);
        var scope = new WorkspaceScope(_workspace.Root);

        Assert.False(scope.IsProtected(Path.Combine(_workspace.Root, "tests", "certs", "test.pem")));
        Assert.True(scope.IsProtected(Path.Combine(_workspace.Root, "config", "production", "app.json")));
        Assert.Equal(["LicenseCode"], scope.Secrets.Keys);
        Assert.DoesNotContain("LC-998877", Guard().RedactFile("app.json", "{ \"LicenseCode\": \"LC-998877\" }", scope.Secrets.Keys));
    }

    [Fact]
    public async Task Workspace_tools_refuse_credential_files_and_mask_what_they_read()
    {
        _workspace.Write("certs/server.pem", "-----BEGIN CERTIFICATE-----");
        _workspace.Write("appsettings.json", "{ \"Password\": \"readSecret-77\" }");
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root), Guard());

        Assert.StartsWith("Error: 'certs/server.pem' is a protected credential file", await tools.ReadFileAsync("certs/server.pem", null, null));
        Assert.DoesNotContain("readSecret-77", await tools.ReadFileAsync("appsettings.json", null, null));
        Assert.DoesNotContain("readSecret-77", tools.SearchText("Password", null, false));
        Assert.Contains("server.pem  (protected: credential file)", tools.ListDirectory(".", 3));
        Assert.False(new WorkspaceScope(_workspace.Root).TryResolve("certs/server.pem", out _, out _));
    }

    [Fact]
    public void Claude_permission_rules_are_added_and_removed_without_touching_the_users_own()
    {
        var settings = (JsonObject)JsonNode.Parse("""{ "permissions": { "deny": ["Bash(rm -rf *)"], "allow": ["Read"] } }""")!;

        ClaudePermissionRules.Apply(settings);
        ClaudePermissionRules.Apply(settings);
        Assert.True(ClaudePermissionRules.IsApplied(settings));
        Assert.Equal(ClaudePermissionRules.Rules.Count + 1, settings["permissions"]!["deny"]!.AsArray().Count);

        ClaudePermissionRules.Remove(settings);
        Assert.False(ClaudePermissionRules.IsApplied(settings));
        Assert.Equal("Bash(rm -rf *)", Assert.Single(settings["permissions"]!["deny"]!.AsArray())!.GetValue<string>());
        Assert.NotNull(settings["permissions"]!["allow"]);
    }

    [Fact]
    public void Hooks_include_the_secret_restorer_and_older_installs_are_incomplete()
    {
        var settings = new JsonObject();
        ClaudeHookConfig.Apply(settings, "http://localhost:5225");

        var preToolUse = Assert.Single(settings["hooks"]!["PreToolUse"]!.AsArray())!;
        Assert.Equal(ClaudeHookConfig.PreToolUseMatcher, preToolUse["matcher"]!.GetValue<string>());
        Assert.Equal("http://localhost:5225/api/hooks/claude/pre-tool-use", preToolUse["hooks"]![0]!["url"]!.GetValue<string>());
        Assert.True(ClaudeHookConfig.IsComplete(settings));

        settings["hooks"]!.AsObject().Remove("PreToolUse");
        Assert.False(ClaudeHookConfig.IsComplete(settings));
    }

    [Fact]
    public void The_pre_tool_use_hook_restores_secrets_and_asks_or_denies()
    {
        var guard = Guard();
        var file = _workspace.Write("appsettings.json", "{ \"Password\": \"hookSecret-88\" }");
        var placeholder = Placeholder(guard.RedactFile(file, File.ReadAllText(file)));
        var controller = new ClaudeHooksController(null!, guard, new NullActivityLog())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        JsonObject Input(string text, string mode = "default") => new()
        {
            ["tool_name"] = "Edit",
            ["permission_mode"] = mode,
            ["tool_input"] = new JsonObject { ["file_path"] = file, ["old_string"] = text, ["new_string"] = "x" }
        };

        var asked = (OkObjectResult)controller.PreToolUse(Input($"\"Password\": \"{placeholder}\""));
        var output = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(asked.Value))!["hookSpecificOutput"]!;
        Assert.Equal("ask", output["permissionDecision"]!.GetValue<string>());
        Assert.Equal("\"Password\": \"hookSecret-88\"", output["updatedInput"]!["old_string"]!.GetValue<string>());

        var accepted = (OkObjectResult)controller.PreToolUse(Input($"\"Password\": \"{placeholder}\"", "acceptEdits"));
        Assert.Contains("\"allow\"", System.Text.Json.JsonSerializer.Serialize(accepted.Value));

        var denied = (OkObjectResult)controller.PreToolUse(Input("{{AIT_SECRET:Password:0123456789}}"));
        Assert.Contains("\"deny\"", System.Text.Json.JsonSerializer.Serialize(denied.Value));

        Assert.IsType<OkResult>(controller.PreToolUse(Input("no placeholder here")));
    }

    [Fact]
    public async Task Executor_sees_placeholders_and_its_edit_gets_the_secret_back()
    {
        await using var harness = await ThreadOrchestratorTests.CreateAsync();
        const string settings = "{\n  \"Smtp\": {\n    \"Host\": \"mail.local\",\n    \"Password\": \"Old-Pass#2024\"\n  }\n}\n";
        harness.Project.Write("appsettings.json", settings);
        var placeholder = Placeholder(harness.Service<ISecretGuard>().RedactFile("appsettings.json", settings));
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Ganti host SMTP.",
                Tasks = [ThreadOrchestratorTests.Step("T1", "appsettings.json", "Change the SMTP host to smtp.local.")]
            }))
            .OnExecutor("appsettings.json", ScriptedRouter.Edit(
                $"    \"Host\": \"mail.local\",\n    \"Password\": \"{placeholder}\"",
                $"    \"Host\": \"smtp.local\",\n    \"Password\": \"{placeholder}\"",
                "Host SMTP diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (result, observer) = await harness.RunAsync("Ganti host SMTP ke smtp.local");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        var written = harness.ReadFile("appsettings.json");
        Assert.Contains("\"Host\": \"smtp.local\"", written);
        Assert.Contains("\"Password\": \"Old-Pass#2024\"", written);

        var executor = Assert.Single(router.CallsTo(ScriptedAgent.Executor));
        Assert.DoesNotContain("Old-Pass#2024", executor.LastUser);
        Assert.Contains(placeholder, executor.LastUser);

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.DoesNotContain("Old-Pass#2024", thread.Tasks.Single().Diff);
        Assert.Contains("🔒 1 secret disamarkan", observer.Final);
    }

    private sealed class NullActivityLog : IActivityLogService
    {
        public void Write(ActivityLevel level, string category, string message, Guid? threadId = null, Guid? projectId = null) { }

        public Task<IReadOnlyList<AIThread.Common.DTOs.ActivityLogDto>> GetRecentAsync(int take, ActivityLevel? minLevel, Guid? threadId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AIThread.Common.DTOs.ActivityLogDto>>([]);
    }
}
