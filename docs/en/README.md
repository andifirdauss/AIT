# AIT documentation

AIT is a local proxy between coding agents (**Claude Code**, **Codex CLI**, **Gemini CLI**, oh-my-pi, …) and
**9router**. Every prompt you type is planned by one strong model (the planner), carried out in parallel by many cheap
models (executors) with the smallest possible context, then summarized (the wrapper) before it is sent back to your
agent in the format it understands (Anthropic, OpenAI or Gemini). AIT is not tied to one CLI; how to point each CLI
at it is in [CLI integration](03-integrasi-cli.md).

```
You ─► CLI ─► AIT ─► Planner ─► Executor × N (parallel) ─► Wrapper ─► CLI
                  │                                                │
                  └──────────── 9router (all models) ◄─────────────┘
```

CLI = Claude Code, Codex CLI, Gemini CLI, opencode, Qwen Code, oh-my-pi, or any other CLI whose base URL can be changed.

This documentation can also be read in the application through the **Docs** menu (http://localhost:5225/Docs).

## Quick start

1. Run 9router — [Installation](01-instalasi.md). Redis/Docker is not needed.
2. Run AIThread: `dotnet run --project src/AIThread.UI --launch-profile http`, then open http://localhost:5225.
   To keep it alive when the browser/terminal is closed, run it in the background: `.\scripts\aithread.ps1 start` —
   [Running in the background](01-instalasi.md#5-running-in-the-background). Without the .NET SDK: the standalone
   `AIThread.exe` with a menu and a tray icon — [Standalone AIThread.exe](01-instalasi.md#6-standalone-aithreadexe-without-net).
3. The **Setup** menu (opened by itself the first time AIT is used) does every first step on one page —
   [Menu guide → Setup](05-menu.md#setup):
   - **9router**: fill in the URL & API key → *Test & save*.
   - **Models**: choose a strong model and a cheap model → *Create nodes & test* (Planner, Executor, Wrapper, Tools Node
     and Context Keeper are created at once).
   - **Project** *(optional)* and **CLI**: turn on the switch of the CLI you use (Claude Code, Codex, Gemini CLI, opencode,
     Qwen Code, oh-my-pi) — [CLI integration](03-integrasi-cli.md).

   In a team, just **Import profile** from the team lead, then fill in your own API key/passwords —
   [Team setup profile](02-konfigurasi.md#team-setup-profile).
4. Open a **new** CLI session in the project folder and give instructions as usual — [Usage](04-penggunaan.md).
5. *(Optional)* **Setting → Telegram** to send prompts from your phone while away from the laptop —
   [Prompts from Telegram](11-telegram.md).

AIT can be shown in **Bahasa Indonesia** or **English**: choose it in the language box at the bottom of the sidebar —
[Display language](02-konfigurasi.md#display-language).

## Contents

| # | Document | Content |
|---|---|---|
| 1 | [Installation](01-instalasi.md) | Prerequisites, short memory (Garnet), 9router, database, running the application (also in the background), `appsettings.json` |
| 2 | [Configuration](02-konfigurasi.md) | Display language, Setting menu (9router, proxy, orchestration), Node menu (choosing a model per role), team setup profile (export/import) |
| 3 | [CLI integration](03-integrasi-cli.md) | Supported formats, pointing Claude Code, Codex, Gemini CLI, opencode, Qwen Code, oh-my-pi (or another CLI) to AIT through the CLI menu, verifying, reverting |
| 4 | [Everyday usage](04-penggunaan.md) | Workflow, reading progress, writing effective prompts, reviewing & cancelling |
| 5 | [Menu guide](05-menu.md) | Dashboard, Setup, thread detail, Project, Memory, Node, CLI, Setting |
| 6 | [Memory](06-memory.md) | Short vs long memory, project vs knowledge memory, labels (module · layer · kind + keywords), code map, module list & merging, label migration, life cycle, management |
| 7 | [How it works](07-cara-kerja.md) | Pipeline details: request classification, planner, contract, validator, executor, verification (build & reviewer), testing (Auto Testing: Playwright browser & API), wrapper, unit test catalog (baseline, regressions, Tester triage, automatic restore) |
| 8 | [Troubleshooting](08-troubleshooting.md) | Common error messages and their fixes |
| 9 | [API reference](09-referensi-api.md) | Proxy endpoints (Anthropic, OpenAI, Gemini), REST `/api`, SSE events |
| 10 | [Development](10-pengembangan.md) | Solution structure, build, tests, migrations, extension points, two languages |
| 11 | [Prompts from Telegram](11-telegram.md) | A private Telegram bot for remote prompts: setup, commands, live progress, security, troubleshooting |
| 12 | [Security & privacy](12-keamanan.md) | Masking secrets before they reach a model, restoring them when files are written, blocked credential files, per-project rules, data stored & sent, limits |

## Terms

| Term | Meaning |
|---|---|
| **Thread** | One orchestrated request: plan → tasks → summary. It has a detail page in the Dashboard. |
| **Planner** | The smartest node. Reads the code (read-only), writes the contract and splits the work into tasks. |
| **Executor** | A cheap node. Carries out one task = one file. Many executors run in parallel. |
| **Wrapper** | The node that collects every executor result and writes the reply to you. |
| **Contract** | The list of names used across files (element ids, DTO properties, API parameters, DB columns) — every executor must use them exactly. |
| **Task** | An instruction for one file (`edit`, `create`, `delete`, `move` or `extract`), which can depend on other tasks (`dependsOn`). `delete`, `move` and `extract` (moving C# members to another file, e.g. splitting a class into `partial` files) are run by AIT without a model. |
| **Node** | The configuration of one AI worker: role, 9router model, temperature, max tokens, parallel limit. |
| **Combo** | A combined model in 9router that falls back between providers automatically. It can be chosen like any model. |
| **Passthrough** | A CLI request that is not orchestrated (session title, compact, quota check, a session outside a project) and is forwarded straight to 9router. |
| **Project** | A root folder holding `.git` or a build file (`.sln`, `pom.xml`, `package.json`, `go.mod`, …). The unique identity of a codebase; memory is bound to it. |
| **Ignored session** | A session whose working directory is not part of a project (no marker). All its prompts pass through as they are — no planner/executor, project or memory. |
| **Short memory** | New memory, kept in Garnet (a Redis-protocol server inside AIT) for ±24 hours. |
| **Long memory** | Memory already moved from short memory to the database. |
| **Project memory / Knowledge memory** | Knowledge specific to one project / general patterns useful across projects. |
| **Memory label** | `Module · Layer · Kind` (e.g. `User · Logic · Rule`; several modules: `Order+Invoice · Logic · Flow`) + at most 16 keywords, PascalCase (capitalized abbreviations in module names are kept: `MKPP`, `KlaimDF`). The module filters, the keywords choose. |
| **Code map** | The module and layer of each project file, decided once (rules, a model or by hand) and reused for memory labels. |
