using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Testing;
using AIThread.Service.Workspace;
using Microsoft.Extensions.DependencyInjection;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// Auto Testing: the planner's test section, the tester node's run against the app on localhost, and how the result
// reaches the developer. The app is a real HTTP listener; the browser is a fake that records what it was asked to do.
public class AutoTestingTests
{
    [Fact]
    public async Task Api_test_runs_after_verification_and_the_developer_sees_the_response()
    {
        await using var app = LocalApp.Start(request => request.Url!.AbsolutePath == "/api/greeting"
            ? (200, "application/json", """{"text":"Halo"}""")
            : (404, "text/plain", "not found"));
        await using var harness = await CreateTestingHarnessAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, call =>
            {
                Assert.Contains("Auto Testing", call.System);
                return router.SubmitPlan(PlanWithTest(TestPlan.Api, app.BaseUrl));
            })
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Tester, call =>
            {
                Assert.Equal([TesterTools.HttpRequest, TesterTools.ReportTest], call.ToolNames!);
                Assert.Contains("Base URL: " + app.BaseUrl, call.LastUser);
                Assert.Contains("+    public string Hello() => \"Halo\";", call.LastUser);
                return router.Tools(10,
                    (TesterTools.HttpRequest, new { method = "GET", url = "http://example.com/api/greeting" }),
                    // The scenario target copied into url, method and all.
                    (TesterTools.HttpRequest, new { method = "GET", url = "GET /api/greeting" }));
            })
            .On(ScriptedAgent.Tester, call =>
            {
                var outputs = ToolOutputs(call);
                Assert.Contains("is not on this machine", outputs[0]);
                Assert.Contains("HTTP 200 OK", outputs[1]);
                Assert.Contains("\"text\": \"Halo\"", outputs[1]);
                return router.Tool(TesterTools.ReportTest, new { summary = "Sapaan tampil.", scenarios = new[] { new { title = "Sapaan", status = "passed", details = "200 dengan text Halo." } } });
            })
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan sekarang Halo."));

        var (result, observer) = await harness.RunAsync("Sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Equal(["GET /", "GET /api/greeting"], app.Requests);

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal("tester-model", thread.TesterModel);
        Assert.Equal(40, thread.TesterTokens);
        Assert.NotNull(thread.TestingMs);
        Assert.Null(thread.Error);
        var report = JsonNode.Parse(thread.TestingJson!)!;
        Assert.Equal(TestingReport.Passed, report["outcome"]!.GetValue<string>());
        Assert.Equal(200, report["requests"]![0]!["status"]!.GetValue<int>());

        Assert.Contains("testing lolos", observer.Final);
        Assert.Contains("**Output API**", observer.Final);
        Assert.Contains("`GET /api/greeting` → **200 OK**", observer.Final);
        Assert.Contains("\"text\": \"Halo\"", observer.Final);
        Assert.Contains("✅ **Sapaan** — 200 dengan text Halo.", observer.Final);
        Assert.Contains(observer.Lines, l => l.Contains("🧪 Testing · API"));
        Assert.Contains(observer.Lines, l => l.Contains(TesterTools.HttpRequest) && l.Contains("GET /api/greeting"));
        Assert.Contains(observer.Lines, l => l.Contains("→ 200 OK"));
        Assert.Contains("# Testing", router.CallsTo(ScriptedAgent.Wrapper).Single().LastUser);
    }

    [Fact]
    public async Task Ui_test_drives_the_browser_and_a_failed_scenario_is_reported()
    {
        await using var app = LocalApp.Start(_ => (200, "text/html", "<html><body>Hi</body></html>"));
        var browser = new FakeBrowser();
        await using var harness = await CreateTestingHarnessAsync(services => services.AddSingleton<ITestBrowserLauncher>(new FakeLauncher(browser)));
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(PlanWithTest(TestPlan.Ui, app.BaseUrl)))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Tester, call =>
            {
                Assert.Contains(TesterTools.BrowserOpen, call.ToolNames!);
                return router.Tools(10,
                    (TesterTools.BrowserOpen, new { url = "/" }),
                    (TesterTools.BrowserFill, new { element = "Nama", selector = "#txtName", text = "AIT test" }),
                    (TesterTools.BrowserClick, new { element = "Simpan", @ref = "e3" }),
                    (TesterTools.BrowserClick, new { element = "Tanpa target" }));
            })
            .On(ScriptedAgent.Tester, call =>
            {
                var outputs = ToolOutputs(call);
                Assert.StartsWith("click e3 done", outputs[2]);
                Assert.Contains("give \"ref\"", outputs[3]);
                return router.Tool(TesterTools.ReportTest, new { summary = "Sapaan belum berubah.", scenarios = new[] { new { title = "sapaan", status = "failed", details = "Masih tampil Hi." } } });
            })
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan belum tampil."));

        var (result, observer) = await harness.RunAsync("Sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Equal([$"open {app.BaseUrl}/", "fill #txtName AIT test", "click e3"], browser.Actions);
        Assert.True(browser.Disposed);

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal("Testing: 1 skenario gagal.", thread.Error);
        var report = JsonNode.Parse(thread.TestingJson!)!;
        Assert.Equal(TestingReport.Failed, report["outcome"]!.GetValue<string>());
        Assert.Equal("Fake Browser", report["browser"]!.GetValue<string>());
        Assert.Equal("console error: boom", report["browserErrors"]![0]!.GetValue<string>());

        Assert.Contains("🟡 **Selesai sebagian**", observer.Final);
        Assert.Contains("testing: 1 gagal", observer.Final);
        Assert.Contains("❌ **Sapaan** — Masih tampil Hi.", observer.Final);
        Assert.Contains("**Error di browser**", observer.Final);
        Assert.Contains(observer.Lines, l => l.Contains(TesterTools.BrowserFill) && l.Contains("Nama ← \"AIT test\""));
    }

    [Fact]
    public async Task App_that_is_not_running_blocks_the_test_and_a_retest_runs_it_later()
    {
        var port = LocalApp.FreePort();
        var baseUrl = $"http://localhost:{port}";
        await using var harness = await CreateTestingHarnessAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(PlanWithTest(TestPlan.Api, baseUrl)))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan diganti."));

        var (result, observer) = await harness.RunAsync("Sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Empty(router.CallsTo(ScriptedAgent.Tester));
        var blocked = JsonNode.Parse((await harness.LoadThreadAsync(result.ThreadId)).TestingJson!)!;
        Assert.Equal(TestingReport.Blocked, blocked["outcome"]!.GetValue<string>());
        Assert.Contains("tidak bisa dihubungi", blocked["error"]!.GetValue<string>());
        Assert.Contains("testing tidak bisa dijalankan", observer.Final);

        // The developer starts the app and runs the test again from the thread page.
        await using var app = LocalApp.Start(_ => (200, "application/json", """{"text":"Halo"}"""), port);
        router
            .On(ScriptedAgent.Tester, router.Tool(TesterTools.HttpRequest, new { method = "GET", url = "/api/greeting" }))
            .On(ScriptedAgent.Tester, router.Tool(TesterTools.ReportTest, new { summary = "Oke.", scenarios = new[] { new { title = "Sapaan", status = "passed", details = "Halo." } } }));
        await harness.Orchestrator.RetestInBackgroundAsync(result.ThreadId, null, CancellationToken.None);

        var thread = await WaitForAsync(harness, result.ThreadId, t => t.Status == ThreadStatus.Completed && t.TestingJson!.Contains("\"passed\""));
        Assert.Equal(40, thread.TesterTokens);
        Assert.Contains("GET /api/greeting", app.Requests);
    }

    [Fact]
    public async Task App_that_is_not_running_is_started_with_the_project_run_command()
    {
        var port = LocalApp.FreePort();
        var baseUrl = $"http://localhost:{port}";
        var launcher = new FakeAppLauncher(port);
        await using var harness = await CreateTestingHarnessAsync(services => services.AddSingleton<IAppLauncher>(launcher));
        harness.Project.Write(ProjectKeyResolver.OverrideFile, """{ "run": "npm run dev" }""");
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(PlanWithTest(TestPlan.Api, baseUrl)))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Tester, router.Tool(TesterTools.HttpRequest, new { method = "GET", url = "/api/greeting" }))
            .On(ScriptedAgent.Tester, router.Tool(TesterTools.ReportTest, new { summary = "Oke.", scenarios = new[] { new { title = "Sapaan", status = "passed", details = "Halo." } } }))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan diganti."));

        try
        {
            var (result, observer) = await harness.RunAsync("Sapaan jadi Halo");

            Assert.Equal(ThreadStatus.Completed, result.Status);
            Assert.Equal("npm run dev", launcher.Command?.Display);
            Assert.Equal(harness.Project.Root, launcher.Command?.WorkingDirectory);
            Assert.Equal(port.ToString(), launcher.Command?.Environment["PORT"]);
            Assert.Contains(observer.Lines, l => l.Contains("AIT menjalankannya") && l.Contains("npm run dev"));
            var report = JsonNode.Parse((await harness.LoadThreadAsync(result.ThreadId)).TestingJson!)!;
            Assert.Equal(TestingReport.Passed, report["outcome"]!.GetValue<string>());
            Assert.Contains("GET /api/greeting", launcher.App!.Requests);
        }
        finally
        {
            if (launcher.App is { } app) await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Tester_asks_for_a_login_in_the_chat_and_never_sees_the_password()
    {
        const string password = "rahasia123";
        await using var app = LocalApp.Start(request => request.HttpMethod == "POST"
            ? (200, "application/json", $$"""{"ok":true,"echo":{{ReadBody(request)}}}""")
            : (200, "text/html", "<html>login</html>"));
        await using var harness = await CreateTestingHarnessAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(PlanWithTest(TestPlan.Api, app.BaseUrl)))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Tester, call =>
            {
                Assert.Contains(TesterTools.AskLogin, call.ToolNames!);
                Assert.Contains("call ask_login", call.LastUser);
                return router.Tool(TesterTools.AskLogin, new { reason = "/api/greeting butuh login", loginUrl = "/login" });
            })
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan diganti; testing menunggu login."));

        var (first, firstObserver) = await harness.RunAsync("Sapaan jadi Halo", ThreadSource.ClaudeCode, "chat-1");

        Assert.Equal(ThreadStatus.Completed, first.Status);
        Assert.Contains("🔐 **Tester butuh login** — /api/greeting butuh login (`/login`)", firstObserver.Final);
        Assert.Contains("`login <username> <password>`", firstObserver.Final);
        Assert.Contains("testing menunggu login", firstObserver.Final);
        var asked = JsonNode.Parse((await harness.LoadThreadAsync(first.ThreadId)).TestingJson!)!;
        Assert.True(asked["askedLogin"]!.GetValue<bool>());
        Assert.Equal("Butuh login.", asked["scenarios"]![0]!["details"]!.GetValue<string>());

        // The answer runs the same thread's test again, with the login typed in by AIT.
        router
            .On(ScriptedAgent.Tester, call =>
            {
                Assert.Contains("Username: {{username}} (= admin)", call.LastUser);
                Assert.Contains("Password: {{password}}", call.LastUser);
                // A refused login can be asked for again.
                Assert.Contains(TesterTools.AskLogin, call.ToolNames!);
                return router.Tool(TesterTools.HttpRequest, new { method = "POST", url = "/login", body = new { user = "{{username}}", password = "{{password}}" } });
            })
            .On(ScriptedAgent.Tester, call =>
            {
                Assert.Contains("\"password\": \"{{password}}\"", ToolOutputs(call)[0]);
                return router.Tool(TesterTools.ReportTest, new { summary = "Login berhasil.", scenarios = new[] { new { title = "Sapaan", status = "passed", details = "Halo." } } });
            });

        var (second, secondObserver) = await harness.RunAsync($"login admin {password}", ThreadSource.ClaudeCode, "chat-1");

        Assert.Equal(first.ThreadId, second.ThreadId);
        Assert.Contains(app.Bodies, b => b.Contains($"\"password\":\"{password}\"") && b.Contains("\"user\":\"admin\""));
        Assert.Contains(secondObserver.Lines, l => l.Contains("Testing ulang") && l.Contains("login admin"));
        Assert.Contains("**🧪 Testing** · API", secondObserver.Final);
        Assert.Contains("login `admin`", secondObserver.Final);
        Assert.Contains("✅ **Sapaan** — Halo.", secondObserver.Final);
        Assert.DoesNotContain(password, secondObserver.Final);
        var thread = await harness.LoadThreadAsync(first.ThreadId);
        Assert.Equal(ThreadStatus.Completed, thread.Status);
        Assert.DoesNotContain(password, thread.TestingJson);
        Assert.Equal("admin", JsonNode.Parse(thread.TestingJson!)!["loginUser"]!.GetValue<string>());

        // Later prompts of the chat carry the reply in their history; the planner never gets the password.
        router.On(ScriptedAgent.Planner, ScriptedRouter.Text("""{"summary": "Info.", "answer": "Sudah.", "tasks": []}"""));
        var observer = new RecordingObserver();
        await harness.Orchestrator.RunAsync(new ThreadRequest(ThreadSource.ClaudeCode, "lanjut", "lanjut", $"user: login admin {password}", harness.Project.Root,
            harness.ProjectId, "chat-1"), observer, CancellationToken.None);
        Assert.Contains("login admin {{password}}", router.CallsTo(ScriptedAgent.Planner).Last().LastUser);
        Assert.DoesNotContain(router.Calls, c => c.Messages.ToJsonString().Contains(password));
    }

    [Theory]
    [InlineData("login admin rahasia123", "admin", "rahasia123")]
    [InlineData("  LOGIN `budi@kantor.id` \"p@ss:w0rd\"  ", "budi@kantor.id", "p@ss:w0rd")]
    [InlineData("username: admin\npassword: rahasia123", "admin", "rahasia123")]
    [InlineData("pass=abc123, user=admin", "admin", "abc123")]
    [InlineData("login form perlu validasi", null, null)]
    [InlineData("login admin", null, null)]
    [InlineData("tambah kolom deskripsi", null, null)]
    public void Login_replies_are_recognised(string reply, string? username, string? password)
    {
        var login = TestLogin.Parse(reply);
        Assert.Equal(username, login?.Username);
        Assert.Equal(password, login?.Password);
        if (login is not null) Assert.DoesNotContain(login.Password, login.ToString());
    }

    [Fact]
    public void Login_store_answers_once_remembers_per_session_and_masks()
    {
        var clock = new ManualClock();
        var store = new TestLoginStore(clock);
        var project = Guid.NewGuid();
        var thread = Guid.NewGuid();

        store.Ask("s1", project, thread);
        Assert.Null(store.TakeAsk("s2", project));
        Assert.Equal(thread, store.TakeAsk("s1", project));
        Assert.Null(store.TakeAsk("s1", project));

        store.Remember("s1", project, new TestLogin("admin", "rahasia123"));
        Assert.Equal("admin", store.Recall("s1", project)?.Username);
        Assert.Null(store.Recall("s1", Guid.NewGuid()));
        Assert.Equal("login admin {{password}}", store.Redact("login admin rahasia123"));

        clock.Advance(TestLoginStore.LoginLifetime + TimeSpan.FromMinutes(1));
        Assert.Null(store.Recall("s1", project));
        Assert.Equal("rahasia123", store.Redact("rahasia123"));

        store.Ask("s1", project, thread);
        clock.Advance(TestLoginStore.AskLifetime + TimeSpan.FromMinutes(1));
        Assert.Null(store.TakeAsk("s1", project));
    }

    [Fact]
    public async Task Without_an_active_tester_the_planner_is_not_asked_for_a_test()
    {
        await using var harness = await CreateTestingHarnessAsync(tester: false);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, call =>
            {
                Assert.DoesNotContain("Auto Testing", call.System);
                // A test section sent anyway is dropped.
                return router.SubmitPlan(PlanWithTest(TestPlan.Api, "http://localhost:5000"));
            })
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan diganti."));

        var (result, _) = await harness.RunAsync("Sapaan jadi Halo");

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Null(thread.TestingJson);
        Assert.DoesNotContain("\"test\"", thread.PlanJson);
    }

    [Fact]
    public async Task Plan_without_test_section_is_sent_back_while_auto_testing_is_on()
    {
        await using var app = LocalApp.Start(_ => (200, "text/plain", "ok"));
        await using var harness = await CreateTestingHarnessAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Sapaan jadi Halo.", Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")] }))
            .On(ScriptedAgent.Planner, call =>
            {
                Assert.Contains("\"test\" is required", call.Messages.Last()!["content"]!.GetValue<string>());
                var plan = PlanWithTest(TestPlan.None, app.BaseUrl);
                plan.Test!.Notes = "Hanya refactor.";
                return router.SubmitPlan(plan);
            })
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan diganti."));

        var (result, observer) = await harness.RunAsync("Sapaan jadi Halo");

        Assert.Empty(router.CallsTo(ScriptedAgent.Tester));
        var report = JsonNode.Parse((await harness.LoadThreadAsync(result.ThreadId)).TestingJson!)!;
        Assert.Equal(TestingReport.Skipped, report["outcome"]!.GetValue<string>());
        Assert.Contains("**🧪 Testing** dilewati — Hanya refactor.", observer.Final);
    }

    [Fact]
    public void Submit_plan_asks_for_the_test_section_only_with_auto_testing()
    {
        static JsonObject Parameters(JsonArray tools) => tools.OfType<JsonObject>()
            .Single(t => t["function"]!["name"]!.GetValue<string>() == PlannerTools.SubmitPlan)["function"]!["parameters"]!.AsObject();

        Assert.Null(Parameters(PlannerTools.All())["properties"]!["test"]);
        var on = Parameters(PlannerTools.All(canTest: true));
        Assert.NotNull(on["properties"]!["test"]);
        Assert.Contains("test", on["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.NotNull(Parameters(PlannerTools.SubmitOnly(canTest: true))["properties"]!["test"]);

        Assert.DoesNotContain(TesterTools.BrowserOpen, TesterTools.Available(TestPlan.Api));
        Assert.Contains(TesterTools.BrowserOpen, TesterTools.Available(TestPlan.Ui));
    }

    [Fact]
    public void Test_section_is_validated_only_when_required()
    {
        using var workspace = new TempWorkspace();
        workspace.Write(GreeterFile, Greeter);
        var scope = new WorkspaceScope(workspace.Root);
        ThreadPlan Plan(TestPlan? test) => new() { Summary = "s", Tasks = [Step("T1", GreeterFile, "x")], Test = test };
        TestPlan Test(string level = "API", string? baseUrl = " http://localhost:5000/ ", params TestScenario[] scenarios) =>
            new() { Level = level, BaseUrl = baseUrl, Scenarios = scenarios.Length == 0 ? [new TestScenario { Title = "A", Steps = ["go"], Expect = "ok" }] : [.. scenarios] };
        IReadOnlyList<string> Errors(TestPlan? test) => PlanValidator.Validate(Plan(test), scope, requireTest: true);

        var valid = Plan(Test());
        Assert.Empty(PlanValidator.Validate(valid, scope, requireTest: true));
        Assert.Equal(TestPlan.Api, valid.Test!.Level);
        Assert.Equal("http://localhost:5000", valid.Test.BaseUrl);

        Assert.Contains(Errors(null), e => e.Contains("\"test\" is required"));
        Assert.Contains(Errors(Test("smoke")), e => e.Contains("level must be"));
        Assert.Contains(Errors(Test(baseUrl: null)), e => e.Contains("needs \"baseUrl\""));
        Assert.Contains(Errors(Test(baseUrl: "https://staging.example.com")), e => e.Contains("on this machine"));
        Assert.Contains(Errors(Test(scenarios: [new TestScenario { Title = "A", Steps = ["go"] }])), e => e.Contains("needs \"expect\""));
        Assert.Contains(Errors(new TestPlan { Level = "none" }), e => e.Contains("needs \"notes\""));
        Assert.Empty(Errors(new TestPlan { Level = "none", Notes = "Refactor only.", Scenarios = [new TestScenario()] }));

        var off = Plan(Test());
        Assert.Empty(PlanValidator.Validate(off, scope));
        Assert.Null(off.Test);
        // Without tasks a runnable test checks the existing behaviour (the developer only asked for a test); level none
        // has nothing to check and is dropped.
        var testOnly = new ThreadPlan { Summary = "s", Answer = "a", Test = Test() };
        Assert.Empty(PlanValidator.Validate(testOnly, scope, requireTest: true));
        Assert.Equal(TestPlan.Api, testOnly.Test!.Level);
        var answer = new ThreadPlan { Summary = "s", Answer = "a", Test = new TestPlan { Level = "none" } };
        Assert.Empty(PlanValidator.Validate(answer, scope, requireTest: true));
        Assert.Null(answer.Test);
    }

    [Theory]
    [InlineData("http://localhost:5000", true)]
    [InlineData("https://127.0.0.1:7001/app", true)]
    [InlineData("http://[::1]:8080", true)]
    [InlineData("http://shop.localhost:3000", true)]
    [InlineData("https://example.com", false)]
    [InlineData("http://10.0.0.5:5000", false)]
    [InlineData("ftp://localhost", false)]
    [InlineData("localhost:5000", false)]
    public void Only_urls_on_this_machine_are_local(string url, bool local) => Assert.Equal(local, TestUrls.IsLocal(url));

    [Theory]
    [InlineData("http://localhost:5000", "/api/users?page=1", "http://localhost:5000/api/users?page=1")]
    [InlineData("http://localhost/app", "/User/Index", "http://localhost/app/User/Index")]
    [InlineData("http://localhost:5000", "http://127.0.0.1:7000/x", "http://127.0.0.1:7000/x")]
    [InlineData("http://localhost:5000", "https://example.com/x", null)]
    public void Targets_resolve_against_the_base_url(string baseUrl, string target, string? expected) =>
        Assert.Equal(expected, TestUrls.Resolve(baseUrl, target)?.ToString());

    [Fact]
    public void Verdicts_match_scenarios_by_title_then_by_position()
    {
        var report = new TestingReport
        {
            Scenarios = [new TestScenarioResult { Title = "Simpan" }, new TestScenarioResult { Title = "Validasi" }, new TestScenarioResult { Title = "Hapus" }]
        };

        TesterAgent.ApplyVerdict(report, """
            {"summary": "Sebagian jalan.", "scenarios": [
              {"title": "validasi", "status": "FAILED", "details": "Pesan tidak muncul."},
              {"title": "Save", "status": "passed", "details": "Tersimpan."},
              {"title": "Lain", "status": "maybe", "details": "?"},
              {"title": "Ekstra", "status": "passed", "details": "x"}
            ]}
            """);

        Assert.Equal("Sebagian jalan.", report.Summary);
        Assert.Equal([TestingReport.Passed, TestingReport.Failed, TestingReport.Blocked, TestingReport.Passed], report.Scenarios.Select(s => s.Status));
        Assert.Equal("Tersimpan.", report.Scenarios[0].Details);
        Assert.Equal("Ekstra", report.Scenarios[3].Title);

        var silent = new TestingReport { Scenarios = [new TestScenarioResult { Title = "A" }] };
        TesterAgent.ApplyVerdict(silent, null, "I could not log in.");
        Assert.Equal("I could not log in.", silent.Summary);
        Assert.Equal(TestingReport.Blocked, silent.Scenarios[0].Status);
    }

    [Fact]
    public void Final_report_cuts_long_bodies_and_keeps_fences_intact()
    {
        var body = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"line {i} ```"));
        var report = new TestingReport
        {
            Outcome = TestingReport.Passed,
            Level = TestPlan.Api,
            BaseUrl = "http://localhost:5000",
            Scenarios = [new TestScenarioResult { Title = "A", Status = TestingReport.Passed, Details = "ok" }],
            Requests = [new HttpExchange { Method = "POST", Url = "http://localhost:5000/api/x?y=1", Status = 201, Reason = "Created", DurationMs = 12, ContentType = "text/plain", ResponseBody = body }]
        };

        var text = FinalReport.Testing(report);

        Assert.StartsWith("\n\n**🧪 Testing** · API · `http://localhost:5000`", text);
        Assert.Contains("`POST /api/x?y=1` → **201 Created** · 12 ms", text);
        Assert.Contains("````text\nline 1 ```", text);
        Assert.Contains("line 30 ```\n…\n````", text);
        Assert.DoesNotContain("line 31", text);
        Assert.Equal("testing lolos", FinalReport.TestingLabel(report));
    }

    [Fact]
    public void Thread_record_carries_the_test_verdicts_for_the_next_planner()
    {
        var report = new TestingReport
        {
            Outcome = TestingReport.Failed,
            Level = TestPlan.Ui,
            BaseUrl = "http://localhost:5000",
            Scenarios = [new TestScenarioResult { Title = "Simpan", Status = TestingReport.Failed, Details = "Pesan tidak muncul." }]
        };
        var brief = TestingReport.FromJson(report.ToJson())!.Brief();
        Assert.Equal("failed (ui at http://localhost:5000); [failed] Simpan: Pesan tidak muncul.", brief);
        Assert.Null(TestingReport.FromJson("not json"));

        var turn = new TurnRecord(Guid.NewGuid(), DateTime.UtcNow, "Completed", "Tambah deskripsi", "Plan", [], [], "Reply", null, brief);
        Assert.Contains("Testing: failed (ui at http://localhost:5000); [failed] Simpan", ContextPackBuilder.Describe(turn, 1_000, 1_000));
    }

    [Fact]
    public async Task Only_planners_keep_auto_testing()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var nodes = harness.Nodes;
        SaveNodeRequest Save(NodeRole role, bool? autoTesting) => new("P", role, "p-model", null, 4096, 2, 100, null, true, AutoTesting: autoTesting);

        var planner = await nodes.CreateAsync(Save(NodeRole.Planner, true));
        Assert.True(planner.AutoTesting);
        // null leaves it as it is.
        Assert.True((await nodes.UpdateAsync(planner.Id, Save(NodeRole.Planner, null)))!.AutoTesting);
        Assert.False((await nodes.UpdateAsync(planner.Id, Save(NodeRole.Executor, true)))!.AutoTesting);

        await nodes.UpdateAsync(planner.Id, Save(NodeRole.Planner, true));
        var pipeline = await nodes.GetPipelineStatusAsync();
        Assert.Contains(pipeline.Issues, i => i.Contains("belum ada node Tester aktif"));
    }

    private static async Task<OrchestrationHarness> CreateTestingHarnessAsync(Action<IServiceCollection>? services = null, bool tester = true)
    {
        var harness = await OrchestrationHarness.CreateAsync(configureServices: services);
        harness.Project.Write(GreeterFile, Greeter);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-model", autoTesting: true);
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        await harness.AddNodeAsync(NodeRole.Wrapper, "wrapper-model");
        if (tester) await harness.AddNodeAsync(NodeRole.Tester, "tester-model");
        return harness;
    }

    private static ThreadPlan PlanWithTest(string level, string baseUrl) => new()
    {
        Summary = "Sapaan jadi Halo.",
        Tasks = [Step("T1", GreeterFile, "Return \"Halo\" from Hello().")],
        Test = new TestPlan
        {
            Level = level,
            BaseUrl = baseUrl,
            Scenarios = [new TestScenario { Title = "Sapaan", Target = level == TestPlan.Api ? "GET /api/greeting" : "/", Steps = ["Open it."], Expect = "Halo" }]
        }
    };

    private static string ReadBody(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static List<string> ToolOutputs(RouterCall call) =>
        call.Messages.OfType<JsonObject>().Where(m => m["role"]?.GetValue<string>() == "tool").Select(m => m["content"]!.GetValue<string>()).ToList();

    private static async Task<ThreadRun> WaitForAsync(OrchestrationHarness harness, Guid threadId, Func<ThreadRun, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var thread = await harness.LoadThreadAsync(threadId);
            if (done(thread)) return thread;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Thread stayed {thread.Status}.");
            await Task.Delay(100);
        }
    }

    // An app on localhost answering every request with handle(request); Requests records "METHOD /path".
    private sealed class LocalApp : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        private LocalApp(Func<HttpListenerRequest, (int Status, string ContentType, string Body)> handle, int port)
        {
            BaseUrl = $"http://localhost:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _loop = Task.Run(async () =>
            {
                while (true)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                    {
                        return;
                    }
                    Requests.Enqueue($"{context.Request.HttpMethod} {context.Request.Url!.PathAndQuery}");
                    var (status, contentType, body) = handle(context.Request);
                    if (context.Request.HttpMethod == "POST") Bodies.Enqueue(body);
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.StatusCode = status;
                    context.Response.ContentType = contentType;
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            });
        }

        public string BaseUrl { get; }

        public ConcurrentQueue<string> Requests { get; } = new();

        // What the app answered to each POST (the handlers in these tests echo the request body).
        public ConcurrentQueue<string> Bodies { get; } = new();

        public static LocalApp Start(Func<HttpListenerRequest, (int, string, string)> handle, int? port = null) => new(handle, port ?? FreePort());

        public static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Close();
            await _loop;
        }
    }

    // "Starts" the app by opening a local listener on the port, so the URL answers the way a started app would.
    private sealed class FakeAppLauncher(int port) : IAppLauncher
    {
        public AppLaunchCommand? Command { get; private set; }

        public LocalApp? App { get; private set; }

        public Task<AppStartResult> StartAsync(AppLaunchCommand command, string baseUrl, CancellationToken cancellationToken)
        {
            Command = command;
            App = LocalApp.Start(_ => (200, "application/json", """{"text":"Halo"}"""), port);
            return Task.FromResult(new AppStartResult(null, null));
        }
    }

    private sealed class FakeLauncher(FakeBrowser browser) : ITestBrowserLauncher
    {
        public Task<ITestBrowser> LaunchAsync(Func<string, Task> notice, CancellationToken cancellationToken) => Task.FromResult<ITestBrowser>(browser);
    }

    private sealed class FakeBrowser : ITestBrowser
    {
        public List<string> Actions { get; } = [];

        public bool Disposed { get; private set; }

        public string Name => "Fake Browser";

        public IReadOnlyList<string> Problems => ["console error: boom"];

        public Task<string> OpenAsync(Uri url) => Act($"open {url}");

        public Task<string> SnapshotAsync() => Act("snapshot");

        public Task<string> ClickAsync(TestElement element) => Act($"click {element.Ref ?? element.Selector}");

        public Task<string> FillAsync(TestElement element, string text, bool submit) => Act($"fill {element.Ref ?? element.Selector} {text}");

        public Task<string> SelectAsync(TestElement element, IReadOnlyList<string> values) => Act($"select {element.Ref ?? element.Selector} {string.Join(",", values)}");

        public Task<string> PressAsync(string key) => Act($"press {key}");

        public Task<string> WaitAsync(string? text, bool gone, double seconds) => Act($"wait {text}");

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private Task<string> Act(string action)
        {
            Actions.Add(action);
            return Task.FromResult($"{action} done\nURL: http://localhost/\nPage snapshot:\n- button \"Simpan\" [ref=e3]\n");
        }
    }
}
