# AIT

[Bahasa Indonesia](README.md) · **English**

A local proxy for coding CLIs (Claude Code, Codex CLI, Gemini CLI, opencode, Qwen Code, oh-my-pi, …) that splits one
request into very small tasks: the **planner** (the smartest model) plans, many **executors** (cheap models) work in
parallel, AIT builds the project, runs the project's related **unit tests** (a catalog shared per project; regressions
are fixed in the code) and (optionally) a **reviewer** checks the result until it passes, (optionally) a **tester** tries
the change in the running application — the UI through a visible Playwright browser, the API through HTTP requests —
then the **wrapper** summarizes the result for the user. Every model is reached through
[9router](https://github.com/decolua/9router).

AIT is shown in **Bahasa Indonesia** or **English** (the language box at the bottom of the sidebar): the web UI, the
`AIThread.exe` menu, CLI output, logs, API messages and Telegram.

> **The full usage guide is in [docs/en/](docs/en/README.md)** — installation, configuration, CLI integration, everyday
> usage, memory, how it works, troubleshooting and the API reference.

```
CLI (Claude Code, Codex, Gemini CLI, …) ──(base URL)──► AIT /v1/messages · /v1/chat/completions
                                                           /v1/responses · /v1beta/models/*
                                          │
                  ┌───────────────────────┴───────────────────────┐
                  │ Planner (read-only tool loop + run_build/tests)│
                  │  list_directory · find_files · search_text     │
                  │  read_file · search_memory · read_memory       │
                  │  └─► submit_plan: summary + CONTRACT + tasks   │
                  └───────────────────────┬───────────────────────┘
                     ┌──────────┬─────────┼─────────┬──────────┐
                     ▼          ▼         ▼         ▼          ▼     (parallel, dependsOn DAG)
                 Executor   Executor  Executor  Executor   Executor  1 task = 1 file
                 (view)     (js)      (api)     (service)  (sql)     SEARCH/REPLACE, retry on failure
                     └──────────┴─────────┼─────────┴──────────┘
                                          ▼
                  Verification: build (dotnet build, tsc, …) + related unit tests
                     (baseline in parallel with the planner) + Reviewer (optional)
                     errors/findings/regressions ─► (Tester triage) ─► fix tasks V1-1… ─► check again
                                          ▼
                  Testing (Auto Testing, optional): Tester → localhost
                     UI: non-headless Playwright browser · API: http_request + output
                                          ▼
                               Wrapper → reply to the CLI (streaming, in that CLI's format)
                                          ▼
                          Memory extractor → short memory (Garnet)
```

## Why it stays accurate with a small executor context

The classic multi-agent problem: the UI executor creates `id="description"` while the JS executor reads `#txtDesc`, or
the JS executor creates a new file because it does not know the existing one. AIT prevents that with:

1. **The planner must read the code first** (read-only tools, which may also read paths outside the root for checks)
   before `submit_plan`.
2. **The contract**: every name used across files (element ids, DTO properties, API parameters, DB columns, validation
   messages) is fixed once by the planner and sent to every executor. That is why tasks can be independent and run in
   parallel.
3. **The plan validator**: `edit`/`delete`/`move` files must exist, `create` files and `move` targets must not exist yet,
   deleted/moved files are not touched by other tasks, change paths may not leave the root or go into
   `bin/ obj/ wwwroot/lib/`, `dependsOn` without cycles. A wrong plan is sent back to the planner to be fixed.
4. **An executor only gets**: one target file (whole, or excerpts around `focus` for a large file), the instruction, the
   contract, and reference excerpts from other files chosen by the planner.
5. **Edits are applied to the latest file content** under a per-file lock (parallel tasks on the same file stay safe),
   all-or-nothing, and a SEARCH failure goes back to the executor to try again.

Execution time = the longest chain of tasks, not the sum of all tasks. The thread detail page shows the parallel timeline
and compares "real time" with "if sequential".

## Prerequisites

- .NET SDK 10
- SQL Server — defaults to LocalDB `(localdb)\MSSQLLocalDB` (change `ConnectionStrings:Default` when needed)
- Node.js 18+ and 9router — installed along with `npm i -g aithread` and started automatically by AIT when it is not
  running yet; without the npm `aithread` package: `npm install -g 9router` (dashboard at http://localhost:20128)

Redis/Docker is not needed: short memory uses [Garnet](https://microsoft.github.io/garnet/) (a Redis-protocol server from
Microsoft) running inside the AIT process — see
[docs/en/01-instalasi.md](docs/en/01-instalasi.md#1-short-memory-garnet).

## Running

```powershell
dotnet run --project src/AIThread.UI --launch-profile http   # http://localhost:5225
```

Database migrations are applied automatically on start.

To keep AIT alive when the browser, the terminal or Visual Studio is closed, run it in the background (publishes Release
to `%LOCALAPPDATA%\AIThread\app`, without a window):

```powershell
.\scripts\aithread.ps1 start          # also: stop · restart · status · logs -Follow · autostart-on · autostart-off
```

Details: [docs/en/01-instalasi.md](docs/en/01-instalasi.md#5-running-in-the-background).

Without the .NET SDK: `.\scripts\publish-exe.ps1 -Zip` builds a standalone `dist\AIThread\AIThread.exe`. Run without
arguments, the exe shows a menu (Web UI, Hide to the tray, log, restart, port, autostart); `AIThread.exe help` shows its
commands — [docs/en/01-instalasi.md](docs/en/01-instalasi.md#6-standalone-aithreadexe-without-net).

1. **Setting** → fill in the 9router URL (e.g. `http://localhost:20128`) and the API key from the 9router dashboard →
   *Test connection* → *Save*.
2. **Node** → add at least one Planner, one Executor and one Wrapper. Models/combos come from the 9router `/v1/models`.
   - Planner: the strongest model that supports *tool calling*.
   - Executor: a cheap & fast model; add several nodes to spread the load. `Max parallel` is limited per node across
     all threads.
   - Wrapper: a cheap model; also used for memory extraction and as the default passthrough model.
   - Reviewer (optional): a careful model; checks the executors' results against the plan, the contract and the build
     result.
   - Tester (required when the project has unit tests or uses Auto Testing): a strong model with *tool calling*; judges
     unit tests that fail after a change (regression, a wrong test, the environment, flaky) — see
     [docs/en/07-cara-kerja.md](docs/en/07-cara-kerja.md#unit-tests) — and, with *Auto Testing* in the Planner node's
     Edit, tries the change in the running application (the UI through a non-headless browser, the API through HTTP) —
     see [docs/en/02-konfigurasi.md](docs/en/02-konfigurasi.md#auto-testing-tester-node).
   - Tools Node (optional): a cheap model with *tool calling*; in the Planner node's Edit, wasteful tools (e.g.
     `read_file`, `search_text`) can be delegated here so their raw output does not weigh on the planner's tokens — see
     [docs/en/02-konfigurasi.md](docs/en/02-konfigurasi.md#planner-tools-node).
3. The **CLI** menu → choose your CLI's card (Claude Code, Codex CLI, Gemini CLI, opencode, Qwen Code, oh-my-pi) → turn
   on the *Per project* switch (when available) or *Global*. Each card writes AIT's entries into that CLI's configuration
   file — see [CLI integration](docs/en/03-integrasi-cli.md). An example for Claude Code, the `env` block in
   `.claude/settings.local.json` (or `~/.claude/settings.json`):

   ```json
   {
     "env": {
       "ANTHROPIC_BASE_URL": "http://localhost:5225",
       "ANTHROPIC_AUTH_TOKEN": "<proxy key or aithread-local>",
       "API_TIMEOUT_MS": "1800000",
       "ANTHROPIC_CUSTOM_HEADERS": "X-AIThread-Project: D:\\Source\\MyApp"
     }
   }
   ```

   A backup of the original file is kept once as `*.aithread-backup`; turning a switch off removes AIT's entries only.
   Codex is used through `codex --profile aithread`; oh-my-pi, opencode and Qwen Code through choosing the AIT model.
4. Open a new CLI session in that project and give instructions as usual. Progress (planner tools, the plan, the status
   of each task) shows in the CLI; the full details are in Dashboard → the thread.

Use an **http** URL for the proxy: Node based CLIs (e.g. Claude Code) do not trust the ASP.NET Core development
certificate.

## Menus

| Menu | Function |
|---|---|
| Dashboard | 9router / Redis / database / pipeline status, statistics (parallel speedup, tokens), run a test prompt, the thread list, live log (SSE) |
| Project | The projects found (unique per root folder: `.git` or any build file, whatever the language), the **project key** for sharing memory between developers, detected stacks, sessions per project, the project folder browser & scan, manual registration, the **unit test catalog** (scan, run, obsolete/quarantine, per-project settings) |
| Memory | Short memory (Garnet, ≤ 24 hours) and long memory (database); *project* and *knowledge* types; edit, promote, consolidate. The **Business memory** tab: a business wiki per module (business memory) with an editor, screenshots and a version history |
| Node | Planner / executor / reviewer / tester (unit test triage, Auto Testing) / wrapper / context keeper with their 9router model, temperature, max tokens, parallelism, priority, extra instructions, skills (found by the Skill Orchestrator in the skill store) |
| Setting | 9router connection & key, proxy key, passthrough model, orchestration limits, automatic build & verification, memory storage (short/long/business) & the business wiki writer, the Telegram bot, the Skill Orchestrator & skill store |

## Prompts from Telegram

Laptop left at the office/home? Send prompts from your phone through a private Telegram bot: **Setting → Telegram** →
paste the token from `@BotFather` → *Save Telegram*, send `/start` to the bot, then **Allow** your chat. A plain text
message runs a thread in the active project; progress shows live in the chat and the wrapper summary is sent when it is
done (`/projects`, `/status`, `/cancel`, `/diff`, `/new`). AIT uses long polling, so no public URL or port forwarding is
needed. Details: [docs/en/11-telegram.md](docs/en/11-telegram.md).

## Skills for nodes

**Setting → Skill**: choose the Skill Orchestrator model, paste a skill repository link (e.g.
`https://github.com/anthropics/skills`), then click *Fetch skills*. The model examines which skills are there, then the
skills are fetched into a local (offline) store. In the **Node** modal, describe the need ("I need you to be a reliable
engineer") and click *Find skills*. Matching skills are ticked and attached to the node, and their content goes into the
node's system prompt every time the node is called. Details:
[docs/en/07-cara-kerja.md](docs/en/07-cara-kerja.md#skills-on-a-node).

## How CLI requests are treated

A request from any CLI (Anthropic, OpenAI or Gemini format) is orchestrated when it has `tools`, its last message is user
text (not a tool result), **and its session comes from a project (a folder with `.git` or a build file)**. Other
supporting requests (session titles, compact, quota checks) and every prompt from a session outside a project are
forwarded as they are to the same 9router endpoint with the *passthrough model* (default: the Wrapper node's model).
Turning *Orchestration on* off makes AIT a plain proxy.

The check is done per CLI session (the decision is remembered for 30 minutes). Project = a root folder holding `.git` or
a build file (`.sln`, `pom.xml`, `package.json`, `go.mod`, …), looked up from the session's working directory (the
`X-AIThread-Cwd` header or the working directory line the CLI sends, e.g. `Primary working directory:`) up through its
parent folders to 6 levels, never into sub-folders; the `X-AIThread-Project` header can point at its root folder
directly. Found → the project is registered automatically at the first prompt. No marker (e.g. an empty folder) or a
parent folder holding several projects → the session is ignored: no orchestration, no memory.

## Memory

- Project memory is written like a `MEMORY.md`: after a thread finishes, the wrapper model writes one **change entry**
  (`Change`) — the feature that changed, **the files that changed** (from the thread's snapshot, not a model guess),
  validation and flow added/changed, notes. Once in long memory, entries are folded into a **module summary**
  (`Summary`: the current state of the module). The `Module · Layer · Kind` label + keywords stay for searching; the
  module and layer come from the **code map**, the module from the same module list as the business wiki. Memory in the
  old format (Rule/Flow/Pitfall/…) is migrated into module summaries (with a backup first). Entries are written in the
  language of the request (Indonesian or English), with headings in that language.
- **Short memory** is kept in Garnet (Redis protocol, inside AIT, survives restarts/crashes) without a TTL; a
  consolidation worker (every 10 minutes) moves the ones older than 24 hours to **long memory** in the database with
  the same id (idempotent).
- The planner gets the summary + the latest change entries of the modules concerned, written in full in its brief; plus
  a memory map (module → counts per layer/kind) and a label index (layer 1: modules from the files/terms in the prompt;
  layer 2: keywords), then calls `search_memory` (module/layer/kind/keywords) / `read_memory`, and `set_module` to
  correct the code map.
- *Knowledge memory* is not bound to a project and is visible to every project.
- Project memory is bound to a **project key** (git remote, `.aithread/project.json`, or a manual key), not to a folder:
  point the long memory of every developer to the same database (Setting → Memory storage) so a project's memory is
  gathered in one place even though everyone's root folder differs.
- **Business memory**: periodically (by default every 6 hours, or the *Update now* button) finished threads are
  summarized by AI into a business wiki per module — requirements, business rules, status & validation tables, and
  screenshots from prompts. It can be edited by hand (with a version history), is stored in its own shareable store,
  and is read by the planner before it changes a module's behaviour. Details:
  [docs/en/06-memory.md](docs/en/06-memory.md#business-memory-business-wiki).

## API & SSE

- CLI proxy: Anthropic `POST /v1/messages`, `POST /v1/messages/count_tokens`, `GET /v1/models`; OpenAI
  `POST /v1/chat/completions`, `POST /v1/responses`; Gemini `POST /v1beta/models/{model}:generateContent` /
  `:streamGenerateContent`
- SSE: `GET /api/stream?types=log,router,thread` (event type prefix) or `GET /api/stream?threadId={id}`. Events: `log`,
  `router.status`, `thread.created|updated|output`, `task.updated|output`, `memory.changed`, `project.changed`,
  `node.changed`, `telegram.changed`, `skill.changed`, `business.changed`, `ping`.
- REST: `/api/settings`, `/api/router`, `/api/nodes`, `/api/projects`, `/api/memory`, `/api/business`, `/api/threads`,
  `/api/dashboard`, `/api/telegram`, `/api/skills`.

Infrastructure configuration is in `src/AIThread.UI/appsettings.json` (connection strings, `AIThread:ShortMemoryHours`,
consolidation & monitor intervals, log retention, the whole-file line limit for executors, `AIThread:EmbeddedRedis`).

## Important notes

- Executors **write straight to the project files**. Run it in a git repo so changes are easy to review or undo.
- The commands AIT runs are the project's build (can be turned off in Setting) and the unit tests of its catalog;
  migrations and git commands that change the repository are not run — follow-up steps are named in the wrapper's
  summary.
- The proxy can write files in whatever folder is sent as the working directory. Let it listen on `localhost` only, and
  fill in the *Proxy API key* when other people can reach the machine.
- The 9router API key and the Telegram bot token are stored encrypted (ASP.NET Core Data Protection).
- The Telegram bot only serves private chats you allowed in the Setting menu — allow only your own account.

## Development

```powershell
dotnet build AIThread.slnx
dotnet test tests/AIThread.Tests
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --context AIThreadDbContext --project src/AIThread.Data --startup-project src/AIThread.Data
```

Every user-facing text is written in both languages — see
[Development → Two languages](docs/en/10-pengembangan.md#two-languages).
