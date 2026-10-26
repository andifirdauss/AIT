# 10. Development

The code conventions and the use of MCP are in `CLAUDE.md` in the repo root.

## Solution structure

| Project | Content |
|---|---|
| `src/AIThread.Common` | Entities, enums, DTOs, `AIThreadOptions`, `Localization/Lang` (the two languages). |
| `src/AIThread.Data` | `AIThreadDbContext` (SQL Server through `IDbContextFactory`), EF migrations, `RedisShortMemoryStore`, `EmbeddedRedisServer` (Garnet inside the process), `Memory/` (memory stores that can be switched from the Setting menu: `SwitchableShortMemoryStore`, `LongMemoryStore`, `SqlShortMemoryStore`, `MemoryStoreConnection` for SQL Server/PostgreSQL/MySQL/Oracle/SQLite/Redis), `InProcessEventBus`. |
| `src/AIThread.Service` | The main services (`NineRouterService`, `NodeService`, `ProjectService`, `MemoryService`, `SettingService`, etc.), `Orchestration/`, `Workspace/`, `Proxy/`, `Background/`. |
| `src/AIThread.API` | REST controllers `/api/*`, the proxy controllers `AnthropicProxyController` / `OpenAiProxyController` / `GeminiProxyController` (`/v1/*`, `/v1beta/*`), `StreamController` (SSE), infrastructure. |
| `src/AIThread.UI` | The ASP.NET Core MVC host. Views are only skeletons; data is loaded by `wwwroot/js/pages/*.js` through `/api` + SSE. |
| `tests/AIThread.Tests` | xUnit unit tests, plus end-to-end orchestration tests (`ThreadOrchestratorTests`): `OrchestrationHarness` uses the real service layer on top of a temporary SQLite and a temporary project folder, only the models are faked by `ScriptedRouter` (replies per agent, executors per file). The unit test catalog tests (`UnitTestOrchestrationTests`) fake the test runner through `ITestRunner` in `configureServices`. |

Dependency flow: `UI → API → Service → Data → Common`.

## Service conventions

Every class in `AIThread.Service` registered in DI (`DependencyInjection.AddServiceLayer`) uses the pattern **Interface +
Service + partial class per method**:

- **Interface** `I<Name>` in `Interfaces/` (namespace `AIThread.Service.Interfaces`) holds every public instance member.
  Registration: `services.AddSingleton<I<Name>, <Name>>()`. Consumers (constructor parameters, fields,
  `GetRequiredService<>`, tests through `harness.Service<I<Name>>()`) always use the interface, not the concrete class.
- **Service** `partial` in a folder named after the class, below its original folder. The namespace still follows the
  original folder, e.g. `Orchestration/PlannerAgent/` stays `AIThread.Service.Orchestration`.
  - `<Name>.cs`: the main declaration, i.e. the primary constructor, base list, fields, constants, properties, nested
    types, and supporting types (records/DTOs) that really belong to the class.
  - `<Name>.<Method>.cs`: one file per method, public or private. Overloads go into one file, including
    `[GeneratedRegex]`. The header is just `public sealed partial class <Name>` without the base list.
- A new method means a new `<Name>.<Method>.cs` file. A new public member is added to `I<Name>` too.
- Not using this pattern: static helpers (e.g. `FinalReport`, `CSharpOutline`), models/records, and hosted workers in
  `Background/`.

```text
Interfaces/IThreadService.cs
Implementations/ThreadService/
  ThreadService.cs                 primary constructor, fields, SummaryProjection
  ThreadService.GetRecentAsync.cs
  ThreadService.GetDetailAsync.cs
  ThreadService.Cancel.cs
  ThreadService.ToTaskDto.cs
```

## Commands

```powershell
dotnet build AIThread.slnx
dotnet test tests/AIThread.Tests
dotnet run --project src/AIThread.UI --launch-profile http
```

EF migrations (a local tool in `dotnet-tools.json`):

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add <MigrationName> --context AIThreadDbContext --project src/AIThread.Data --startup-project src/AIThread.Data
dotnet tool run dotnet-ef migrations has-pending-model-changes --context AIThreadDbContext --project src/AIThread.Data --startup-project src/AIThread.Data
```

`--context` is required because `AIThread.Data` also holds `ShortMemoryDbContext` and `LongMemoryDbContext` (the external
memory stores). Neither has migrations: their tables are created by `MemorySchema` on first use, so changes to
`LongMemory` must stay compatible with the tables that already exist in an external database.

The background instance (`scripts/aithread.ps1`, see [Installation](01-instalasi.md#5-running-in-the-background)) uses
the publish output in `%LOCALAPPDATA%\AIThread\app`, so building/debugging from the repo still works — as long as it uses
another port (`--urls`) or the background instance is stopped first. `Background/ThreadLifecycleWorker` keeps thread
statuses right when the process stops: a clean shutdown cancels running threads, and on start threads left behind by a
process that died suddenly are marked *Cancelled*.

The standalone `AIThread.exe`: `scripts/publish-exe.ps1` uses the profile
`src/AIThread.UI/Properties/PublishProfiles/standalone.pubxml` (win-x64, self-contained, single-file; `AssemblyName`
becomes `AIThread` only in that profile, a regular build stays `AIThread.UI`) and builds into `dist/.build`, so it does
not clash with a locked `bin/`. The menu, the tray (pure Win32, no WinForms to keep RAM low), autostart and the CLI
commands are in `src/AIThread.UI/Hosting/`; `Program.cs` only calls `Cli.RunAsync`. Without arguments and without a
terminal/`--urls`/`ASPNETCORE_*` env, the host runs as usual (`dotnet run`, Visual Studio, the background script do not
change). Testing the menu needs a real console: run the exe with `menu --port <test port>` so it does not touch the port
5225 instance.

Migrations are applied automatically when the application starts (`InitializeDataLayerAsync`).
`DesignTimeDbContextFactory` uses the default LocalDB connection string.

## Orchestration code map

| File | Responsibility |
|---|---|
| `Proxy/ProtocolAdapter.cs` | Turns Anthropic / OpenAI chat / OpenAI Responses / Gemini requests into the canonical shape (Anthropic style) and decides the 9router path per format. |
| `Proxy/ClaudeRequestInspector.cs` | Sorts canonical requests: orchestration candidate vs passthrough; takes the working directory, session, prompt, history. |
| `Implementations/SetupService/`, `SetupProfileService/`, `API/Controllers/SetupAssistantController.cs` | The Setup menu: step status from the current state (`GetStatusAsync`, defensive like the dashboard), saving 9router after testing it, the `AIT …` node preset (matched by name + role). Team profile: export without secrets, import = a plan per part (dry run text + actions) then run in order; nodes are created first, links (fallbacks, delegations, skills) are connected by name. UI: `pages/setup.js`, the shared project browser `js/project-browser.js` + `_ProjectBrowser.cshtml`; CLI: `Hosting/SetupCommand.cs`. |
| `Implementations/ProjectService/` | `ResolveSessionAsync`: the gate per session (project or ignored, 30-minute cache, auto-register); folder browser, scan, registration. A project's identity = `RootPath`. |
| `Workspace/ProjectLocator.cs` | The marker table (`.git`, manifests per ecosystem), deciding the root from the working directory (nearest repo → outermost aggregator → nearest manifest), blocked folders (drive/Home/system), recursive scan, manifest & stack list, build folders per stack. |
| `Orchestration/ThreadOrchestrator/` | The main thread flow: project → unit test restore → planner (+ parallel baseline) → execution → verification → testing → wrapper → memory, status & errors; threads that only test (`TestOnlyAsync`); *Test again* (`RetestInBackgroundAsync`). |
| `Orchestration/PlannerAgent/`, `PlannerTools.cs` | The planner (9router tool loop), failover between planner nodes, tool definitions (JSON schema), the brief (stable → specific order for the prompt cache), pausing & resuming on `ask_user`, the `investigate` tool for delegated tools. |
| `Orchestration/PlannerToolRunner/` | Running the read tools, `run_build` and `run_tests` (used by the planner and tools nodes; `run_tests` by the planner only), the progress text per tool (`Describe`). |
| `Workspace/GitTools.cs` | The planner's read-only git tools (`git_status`/`git_diff`/`git_log`/`git_show`/`git_blame`): a `git` process with fixed arguments, validated revisions, paths after `--`, no optional locks/pager/external diff, limited output. `IsRepository` decides whether the git tools are offered (`PlannerTools.Offered`). |
| `Orchestration/ToolsNodeAgent/` | The tools node: its own tool loop for one `investigate` question, ended by `report_findings`; delegations are stored in `PlannerToolDelegation` (validated by `NodeService`). |
| `Orchestration/PlannerQuestions.cs`, `PendingQuestionStore/` | Planner questions: the numbered text format, answer → tool result, `PendingQuestionStore` (per session, in memory, 2 hours), closing threads that were not answered. |
| `Orchestration/ThreadPlan.cs`, `PlanValidator.cs` | The plan model and its validation rules. |
| `Orchestration/ExecutionEngine/` | Parallel DAG scheduling, executor choice, skipping failed dependencies. |
| `Orchestration/ExecutorAgent/` | Building the executor input, retries, applying edits under a lock. |
| `Orchestration/TesterAgent/`, `TesterTools.cs`, `Testing/` | Auto Testing: the Tester node runs the plan's `test` part (`TestPlan`, validated by `PlanValidator`) in its own tool loop — `http_request` and the `browser_*` tools — then `report_test`; `TestingReport` is stored in `ThreadRun.TestingJson`. `Testing/TestBrowserLauncher` opens a non-headless Edge/Chrome/Chromium through Microsoft.Playwright (a persistent profile, one browser at a time), `PlaywrightTestBrowser` = actions + `ref` snapshots, `TestUrls` = localhost only. Login: `ask_login` → a question at the end of the answer → the reply `login <user> <password>` (`TestLogin.Parse`) caught by `ThreadOrchestrator.RunAsync` → `TestWithLoginAsync` (streamed re-test); `TestLoginStore` (memory: questions per session, logins per session+project for 8 hours, `Redact` of the chat history); the tester only sees `{{password}}` (`TesterAgent.Fill`/`Mask`). |
| `Verification/TestCommandResolver.cs`, `TestResultParser.cs`, `TestEnvironment.cs`, `TestRunner/`, `RootGates.cs` | Unit tests without a model: suites per stack from manifests + fixed commands (`dotnet test` TRX, `vstest.console`, jest/vitest/mocha JSON, pytest/PHPUnit/Maven/Gradle JUnit XML, `go test -json`, `cargo test`, `npm test` exit code only), result parsers per test, recognizing environment failures (→ *skipped* + reason), process + timeout (`CI=true`), and one build/test per root (`RootGates`, also used by `BuildRunner`). |
| `UnitTests/` | The unit test catalog per `ProjectKey`: `UnitTestCatalog` (CRUD, `UT-0001` numbering, results, a 20-run history, `RevertThreadAsync` for rollback), `UnitTestScanner` + `TestDiscovery` (Roslyn for C#, patterns for other stacks; labels through `MemoryAi`), `UnitTestExecutor` (grouping per suite, repeating a failure once → flaky, `UnitTestMatcher` matching runner results to tests), `UnitTestRestorer` (moved file → git notes → snapshot / Roslyn method insert → executor task `R<n>` from a snapshot or scenario), `UnitTestSnapshot` (internal snapshot, never in a DTO), `UnitTestSelection` (relevant tests). Tables in `Data/Memory/UnitTestDb.cs` (`IUnitTestDb`, following the long memory store through `LongMemoryStore.CreateUnitTestsAsync`). |
| `Orchestration/UnitTestStage/` | Unit tests in a thread: `OpenAsync` (settings, suites, automatic scan of an empty catalog, a 10-second limit), `RestoreAsync`, the baseline in parallel with the planner + hash cache (`StartBaseline`/`CompleteBaselineAsync`), `Brief` (`# Unit tests` in the planner's brief), `CheckAsync` per verification round (classification, triage `TesterAgent.TriageAsync`, mechanical rules, `ReviewIssue` → `FixTaskFactory`), `RunOnlyAsync`/`RunForPlannerAsync`, `FinishAsync` (results, new tests, obsolete, quarantine, `UnitTestReport` in `ThreadRun.UnitTestJson`). The 3-fix-round limit is in `VerificationStage`. |
| `Implementations/UnitTestService/`, `API/Controllers/UnitTestsController.cs` | The catalog API for Project > Detail (`pages/project-unittests.js`): settings, scan, manual runs, history, reactivate, confirm removal (a Dashboard thread removes the test code). Catalog export/import is in `MemoryTransferService` (from format v3). |
| `Orchestration/WrapperAgent/`, `MemoryExtractor/` | The final summary and the change entry (`Change`) per thread; the changed files from `ThreadFileSnapshots`, the text assembled by `Memory/ChangeLog` (headings in the entry's language). `Memory/ModuleSummaryWriter` folds entries into module summaries (`Summary`) during consolidation. |
| `Orchestration/ContextKeeper/`, `ContextPackBuilder.cs`, `ThreadAnchor.cs` | The CLI chat session context: thread markers in replies, looking up the summary from the history, updating the summary after a thread, the `read_thread` tool. |
| `Orchestration/SessionHandoff/`, `TranscriptDigest.cs`, `Implementations/ClaudeHookConfig.cs`, `API/Controllers/ClaudeHooksController.cs` | The Context Keeper for `/clear`: the `SessionEnd`/`SessionStart` hooks, transcript summaries, writing the `hooks` block in the Claude Code settings. |
| `Orchestration/AgentPrompts.cs` | **Every role prompt** (planner + the *Auto Testing* and *Unit tests* parts, executor, reviewer, tester + unit test triage, unit test labels, wrapper, memory, context keeper). |
| `Orchestration/ThreadRecorder.cs`, `ClientProgress.cs` | Persistence + SSE events; CLI progress as a *vertical stepper* in a ```` ````text ```` block (wrapped at 90 columns). |
| `Orchestration/FinalReport.cs` | The parts of the final answer AIT writes from the results: the status line + *Needs attention*, the *Changed files* table (from the diff), the *Unit test* part, the *Testing* part + *API output*, the footer (duration, tokens, memory, wiki, *Quota left*, the thread link). |
| `Implementations/RouterQuotas.cs` | The pure part of provider quotas: node/combo model → 9router provider (an alias map `cc`→`claude`, `ag`→`antigravity`, …), parsing `/api/combos`, `/api/providers/client`, `/api/usage/{id}`, and choosing the limits shown. Its HTTP is in `NineRouterService.GetQuotasAsync` (60-second cache, 10-second timeout). |
| `Implementations/RouterCliToken.cs` | The 9router CLI token (`x-9r-cli-token` = the first 16 hex of sha256(`machine-id` + `9r-cli-auth` + `auth/cli-secret`) from the 9router data folder), sent by `GetDashboardJsonAsync` only to a 9router on localhost so the quota can be read even with *Require login* on. |
| `Workspace/*` | The path sandbox, repo read tools, the SEARCH/REPLACE parser & applier, text I/O (BOM/line endings), diffs, the project locator. |
| `Security/` | Secret security: `SecretRules` (secret patterns + the `{{AIT_SECRET:<label>:<id>}}` placeholder, pure), `SecretPaths` (credential files & folders refused by `WorkspaceScope`), `SecretPolicy` (`"secrets"` in `.aithread/project.json`: `deny`/`allow`/`keys`), `SecretGuard` (an in-memory vault, HMAC ids with a Data Protection key, `Redact`/`RedactFile`/`RedactMessages`/`RedactJson` + `Restore`/`RestoreJson`/`Learn`, a tally per thread). Wired into `NineRouterService.ChatAsync` (every model call), `WorkspaceTools`, `PlannerToolRunner`, `ExecutorAgent` (restores blocks before applying them), `TesterAgent`, `ThreadOrchestrator.Redacted`, `ActivityLogService`, `MemoryExtractor`, `BusinessPageStore`, memory export, the `AgentProxyHandler` passthrough, and the `PreToolUse` hook (`ClaudeHooksController`). `ClaudePermissionRules` writes Claude Code's `permissions.deny`. Settings + popup: `SecurityService`, `SecurityController`, `Views/Shared/_SecurityNotice.cshtml`. |
| `API/Infrastructure/AgentProxyHandler.cs` | The proxy logic shared by every format: auth, classification, orchestration, passthrough to 9router. |
| `API/Infrastructure/AnthropicSseWriter.cs`, `ClientStreamWriters.cs` | Writing orchestrated replies (stream, non-stream, errors) in the Anthropic, OpenAI chat, OpenAI Responses or Gemini format. |
| `Background/TelegramPollingWorker.cs`, `Telegram/TelegramBotRuntime/` | Telegram bot `getUpdates` long polling, restarting when the Telegram settings are saved, backoff while offline, the status for the Setting card. |
| `Telegram/TelegramApi/` | A minimal Bot API client: JSON with `Content-Length`, the token read from the runtime settings on every call, resending as plain text when HTML is rejected. Its HttpClient has no logger because the token is in the URL. |
| `Telegram/TelegramBot/` | Chat permissions, commands (`/projects`, `/status`, `/cancel`, `/diff`, `/new`), starting threads (`ThreadSource.Telegram`, session `telegram:<chat>`, the conversation history for the planner). |
| `Telegram/TelegramThreadReporter/` | The live status message per thread (the event bus as the trigger, the database as the data source) and the final result message. |
| `Telegram/TelegramFormatter.cs`, `TelegramMessages.cs` | Wrapper Markdown → Telegram HTML (no `<code>` inside `<b>`/`<i>`, split per 4096 characters) and every bot text. |

## Documentation in the application

The **Docs** menu (`/Docs`, `/Docs/{file-name-without-.md}`) renders `docs/*.md` with Markdig through `DocsService`.

- Folder location: `AIThread:DocsPath` when filled in; otherwise the repo's `docs/` (two levels above `src/AIThread.UI`,
  used during development so changes show right away), then the copy of `docs/` in the output/publish folder.
- `README.md` is the index page; the navigation order follows the file names, so keep the number prefix (`11-...md`).
- The title is taken from the first `# ` heading. `file-name.md#anchor` links become `/Docs/file-name#anchor`
  automatically; anchors follow the GitHub/GitLab rules. Links to files outside `docs/` cannot be opened from the web —
  write them as text.
- The English translation is in `docs/en/` with the same file names; when the display language is English,
  `DocsService` uses the `docs/en/` file when it exists, otherwise the Indonesian file. Change both together. Anchors
  differ per language (they come from the headings), so anchor links from the UI/C# are written per language
  (`Lang.T("/Docs/…#anchor-id", "/Docs/…#anchor-en")`).

## Two languages

Every text a developer reads is written as an Indonesian + English pair where it is used; there are no resource files.

- C#: `Lang.T("Indonesia", "English")` (`AIThread.Common.Localization`). `Lang.Current` = the machine's language
  (`SystemSetting.UiLanguage`, set by `Lang.Set` on start and when it changes); `Lang.Culture` for date & number
  formats. A `const string` holding user text becomes a property `static string X => Lang.T(…)`, and `static readonly`
  fields / property initializers holding text become `=>` so they follow a language change.
- Razor: `@Lang.T("…", "…")`, inline markup `@Html.Raw(Lang.T("…", "…"))`.
- JS: `t('…', '…')`, `isEn`, `locale` from `window.AIThread`; templates with expressions are written
  `isEn ? \`English ${x}\` : \`Indonesia ${x}\``. Do not name a local variable `t`.
- Tests: `using var _ = Lang.Use("en");` switches the language only within that scope (AsyncLocal).
- Chat reply parsers (`RetryReply`, `TestFollowUpReply`, `TestScenarioReply`, `TestLogin`) accept both languages, and
  files read back (Excel scenario sheets) recognize sheet/column names in both languages.
- English only: prompts to models, planner tool error messages, and developer logs (`ILogger`). When a prompt needs a
  default language, send `TextLanguage.Ui` (e.g. a `# Language` / `# UI language` part).
- Memory uses the language of its content (`TextLanguage.Guess`), not the display language: `ChangeLog.Render(…, english)`.

## Common extension points

**Tuning model behaviour** — change `AgentPrompts.cs`. Prompts to models are written in English; user text is always in
two languages (see [Two languages](#two-languages)). For per-team adjustments without changing code, use a node's *Extra
instructions*.

**Adding a planner tool**
1. Add its definition in `PlannerTools.Definition()` (a constant name + JSON schema) and its name in
   `PlannerTools.Delegable` (the planner's read tool order; every read tool can be delegated to a tools node), and in the
   `delegableTools` list in `wwwroot/js/pages/nodes.js`.
2. Handle it in `PlannerToolRunner.RunAsync` (and `Describe` for the progress text).
3. The tool must be read-only and use `WorkspaceScope` for every path.

**Adding a plan validation rule** — `PlanValidator.Validate`; write the error message in English, explaining how to fix
it (the message is sent back to the planner). Add a test in `PlanValidatorTests`.

**Changing the executor edit format** — `EditBlockParser`, `EditApplier`, the `AgentPrompts.Executor` prompt and the
related tests must change together.

**A new SSE event** — add a constant in `AppEventTypes`, publish it through `IEventBus.Publish`, then handle it in
`AIThread.stream(...)` on the page concerned.

**Adding a Telegram bot command**
1. Add it to `TelegramMessages.Commands` (the `/` menu in Telegram is updated when the bot starts polling).
2. Handle it in `TelegramBot.HandleCommandAsync`; write the reply text in `TelegramMessages` as Telegram HTML (in both
   languages with `Lang.T`) and escape input with `TelegramFormatter.Escape`.
3. Do not put `<code>`/`<pre>` inside `<b>`/`<i>`/`<s>`/`<a>` — Telegram rejects such messages.

## End-to-end testing without model costs

The pipeline can be tested with a fake 9router server that implements `GET /api/health`, `GET /v1/models` and
`POST /v1/chat/completions` (OpenAI SSE stream, including `tool_calls`), with different answers depending on the system
prompt content (`You are the PLANNER`, `You are an EXECUTOR`, `You are the WRAPPER`, `maintain the memory`). Point Setting
→ Base URL to that fake server, then send a CLI request (e.g. the Anthropic format to `/v1/messages`) or
`POST /api/threads` (`{ "projectId", "prompt" }`). Use a separate database (change `ConnectionStrings:Default`) so test
data does not mix.

The Telegram bot is tested the same way: point `AIThread:Telegram:ApiBaseUrl` to a fake Bot API server that answers
`getMe`, `deleteWebhook`, `setMyCommands`, `getUpdates` (long poll with `offset`), `sendMessage`, `editMessageText` and
`answerCallbackQuery`, then push message/button updates into its `getUpdates` queue. Lower `PollTimeoutSeconds` (e.g.
`2`) so the tests are fast. The fake server should validate HTML like Telegram does (supported tags, entities, no `code`
inside emphasis, the 4096-character limit).
