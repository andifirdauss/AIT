# 2. Configuration

The runtime configuration is stored in the database and changed from the UI — no restart is needed.

For the first setup, the **Setup** menu does the required parts (9router connection, nodes, CLI) on one page — see
[Menu guide → Setup](05-menu.md#setup). In a team, the same settings are shared through a
[team setup profile](#team-setup-profile). This page explains every field in full.

## Display language

AIT is shown in **Bahasa Indonesia** (the default) or **English**. Choose it in the language box at the bottom of the
sidebar; the page reloads and the choice is stored in the database for this machine (`PUT /api/settings/language`). The
language applies to everything a developer reads: the web pages, the Docs menu (`docs/en/` for English), the
`AIThread.exe` menu and tray icon, AIT's output in the CLI (progress, final report), the activity log, validation
messages & API responses, Telegram messages, and the Excel scenario files.

- **Chat commands are recognized in both languages**, whatever language is chosen: `ulangi` / `retry`, `jalankan test
  lanjutan 2` / `run follow-up test 2`, `buat skenario test lanjutan 2` / `create scenario for follow-up test 2`,
  `jalankan skenario <name> setuju` / `run scenario <name> agree`, `password dev <value>` / `dev password <value>`.
- **AIT's output in the CLI follows the language of the prompt**, not the display language: the prompt "please add a
  date filter" is answered in English even when the display language is Bahasa Indonesia, and the other way around. The
  language is recognized from the words of the prompt (pasted code, logs and tags do not count); when the prompt does
  not show its language (e.g. `👍` or only pasted logs), the earlier user prompts of that conversation decide, then the
  display language. That language applies to the whole thread: progress, planner questions, the final report, the
  memory entry, and that thread's activity log entries.
- **Model answers** still follow the language of your prompt; the display language is only used when the prompt does
  not show its language (e.g. only pasted logs).
- **Memory** is written in the language of the developer's request, by default the display language. The section
  headings of a change entry (*Fitur*/*Feature*, *File berubah*/*Changed files*, …) follow the language of its content,
  not the display language, because memory is shared with the team — see [Memory → Language](06-memory.md#memory-language).
- Excel scenario files are written in the display language (sheets *Panduan/Akun/Langkah* or *Guide/Accounts/Steps*)
  and read in both languages, so a file from a teammate who uses the other language can still be uploaded.
- `AIThread.exe` reads the language from `cli.json` in `%LOCALAPPDATA%\AIThread`, kept in sync automatically when the
  language is changed from the web.

## Setting menu

### 9router connection

| Field | Details |
|---|---|
| Base URL | The 9router address, default `http://localhost:20128`. A `/v1` suffix (as the 9router dashboard shows it) is removed automatically. |
| API key | The key from the 9router dashboard. Stored **encrypted**; once saved it is only shown masked (e.g. `sk-abc••••1234`). Leave the field empty to keep the old key. |
| Delete stored API key | Tick it, then *Save settings* to delete the key. |

The **Test connection** button uses the values in the form **without saving them**. Once the result is right, click
**Save settings** — AIT only uses the stored key. When the page opens, the result line shows the status of the stored
configuration.

- `✓ Online · 12 ms · 48 models · 3 combos · API key OK` — ready to use.
- `… · this key is not saved yet — click Save` — the key you typed is valid, but not stored yet.
- `API key required, not saved yet` — 9router requires a key (*require API key* is on) and no key is stored. Every
  model call will fail with `401 Missing API key`.
- `API key rejected` — the key is wrong or was deleted in 9router.
- `no API key (not required)` — there is no key and 9router does not ask for one.
- `✕ Offline — …` — 9router is not running or the URL is wrong.

"Online" alone is not enough: the 9router health check and model list need no key, so the key status is checked
separately (with an empty request that 9router rejects before a model is called — no token cost). A key problem also
shows as *Online · check API key* (yellow) on the Dashboard and as a warning in the log.

### Proxy & orchestration

| Field | Default | Range | Details |
|---|---|---|---|
| Orchestration on | on | — | When off, every CLI request is forwarded to 9router with the *passthrough model* — the CLI works as a plain agent. |
| Show progress in the CLI | on | — | Shows the planner steps, the plan and the status of each task before the final summary. When off, only the summary is shown. |
| Planner may ask | on | — | The planner may stop the thread with one numbered question when the request is truly ambiguous, the change is risky and was not asked for, or a fact cannot be found in the code/memory. It is not a confirmation of every prompt. See [Questions from the planner](07-cara-kerja.md#questions-from-the-planner). |
| Passthrough model | automatic | — | The model for supporting requests (session title, `/compact`, quota check). *Automatic* = the model of the active Wrapper node, then the Executor. |
| Proxy API key | empty | — | When filled in, the CLI must send it as a bearer token (Claude Code: `ANTHROPIC_AUTH_TOKEN`); the CLI menu writes it automatically. The *Generate* button creates a random key. |
| Planner steps | 24 | 3–100 | The maximum number of planner tool rounds. At the last step the planner is forced to send its plan. |
| Executor retries | 1 | 0–5 | How many times an executor may try again when its edit fails to apply. |
| Max parallel | 8 | 1–64 | The limit of tasks running at the same time **within one thread**. |
| Timeout (seconds) | 600 | 30–3600 | The time limit of one model call to 9router. |

### Build & verification

| Field | Default | Range | Details |
|---|---|---|---|
| Automatic build & verification | on | — | AIT may run the project's build command (`dotnet build --no-incremental`, Visual Studio MSBuild for old-format .NET Framework projects, `tsc`/`npm run build`, `go build`, `cargo check`, `mvn compile`): as the planner's `run_build` tool and to check the executors' results. Build errors go to an executor as fix tasks. See [Verification](07-cara-kerja.md#5-verification-build-and-reviewer). |
| Build timeout (seconds) | 300 | 30–3600 | The limit of one build; a build that exceeds it is stopped together with its child processes. Also the limit of one [unit test](07-cara-kerja.md#unit-tests) run when the project does not fill in *Test run time limit*. |
| Fix rounds | 2 | 0–5 | The maximum number of build/review → fix task rounds. `0` = only verify and report. Unit test regressions have their own limit (3 rounds); `0` turns that off too. |

The *9router connection*, *Proxy & orchestration* and *Build & verification* tabs are one form: **Save settings** on any
of them saves all three. After changing the proxy API key, turn the active CLI switches in the CLI menu off and on again
so the new key is written too.

### Memory storage

The `/Setting#memory` tab: one card each for short memory, long memory and business memory (the business wiki), saved
with their own **Save memory storage** button. Changes apply right away without a restart. For a team, point the
**Long** and **Business memory** of every developer to the same database: memory is shared per
[project key](06-memory.md#sharing-memory-between-developers-project-key), not per root folder.

| Field | Default | Details |
|---|---|---|
| Provider | Built-in | Short: built-in Garnet, Redis, SQL Server, PostgreSQL, MySQL, Oracle, SQLite. Long & business: the main database, SQL Server, PostgreSQL, MySQL, Oracle, SQLite. |
| Host / Port | empty | The database/Redis server. An empty port = the provider's default port. |
| Database | empty | The database name; the service name for Oracle; the index for Redis; the file path for SQLite. |
| Username / Password | empty | The password is stored **encrypted**; leave it empty to keep the old password, or tick *Delete stored password*. |
| Extra options | empty | Extra connection string keywords (e.g. `SSL Mode=Require`). |
| Copy the existing memory | on | Copies the memory from the old store to the new store when the provider/server changes. The old store is not changed. |

A store that cannot be reached is rejected when saving. Details per provider in [Memory](06-memory.md#memory-storage).

### Telegram

The last tab of the Setting page (`/Setting#telegram`), for sending prompts from a phone — the full guide is in
[Prompts from Telegram](11-telegram.md).

| Field | Default | Details |
|---|---|---|
| Turn on the Telegram bot | off | Starts long polling to Telegram. Needs a stored token. |
| Bot token | empty | The token from `@BotFather`. Verified with Telegram (`getMe`) when saved, then stored **encrypted** and only shown masked. Leave the field empty to keep the old token. |
| Delete stored token | — | Tick it, then *Save Telegram* to delete the token (the bot stops automatically). |
| Chats | — | Private chats that have sent the bot a message. **Allow** your own chat; **Delete** strange chats; **Revoke** ends the access of an allowed chat. |

The **Save Telegram** button only saves this part (separate from *Save settings*) and applies right away without a
restart. Progress in Telegram is always shown, regardless of the *Show progress in the CLI* switch.

### Security & privacy

The `/Setting#keamanan` tab controls secret masking. The details are in [Security & privacy](12-keamanan.md).

| Field | Default | Details |
|---|---|---|
| Mask secrets before they are sent to a model and before they are stored | on | Passwords, API keys, tokens, JWTs, private keys and connection string passwords are replaced with `{{AIT_SECRET:…}}` before they are sent to a model, and put back when AIT writes a file. When off, everything is sent as it is. |
| Also mask passthrough requests | on | Applies to requests forwarded as they are to 9router. For Claude Code, install the hook in the CLI menu so placeholders are put back before Edit/Write/Bash. |

Blocking credential files (`.pem`, `.pfx`, SSH keys, `.npmrc`, `~/.aws`, …) always applies. Exceptions and extra setting
names are set per project in `.aithread/project.json` → `"secrets"`. The location of the placeholder key can be changed
with `AIThread:SecretKeyPath` in `appsettings.json` (default `%LOCALAPPDATA%\AIThread\secret-mask.key`).

## Node menu

The pipeline needs at least **1 Planner, 1 Executor, 1 Wrapper** that are active. The banner at the top of the page
shows whether the pipeline is ready, with its notes. The **Reviewer** node (see
[Verification](07-cara-kerja.md#5-verification-build-and-reviewer)) and **Context Keeper** node (see
[Session context](07-cara-kerja.md#session-context-context-keeper)) are optional, and so is a **Tools Node** (see
[Planner tools node](#planner-tools-node)). The **Tester** node is conditionally required: it is needed when the project
uses the [unit test catalog](#project-unit-tests) or [Auto Testing](#auto-testing-tester-node); otherwise it may be
absent.

The fastest way to fill every role at once: Setup → the *Choose a model for each role* step (just pick a strong model
and a cheap model) — see the [pipeline preset](05-menu.md#setup). The `AIT …` nodes it creates can be changed here like
any other node.

### Business memory

The **Wiki Writer** column in the *Background task models* part of the Node menu (`/Node`), changed with **Edit**.
Formerly a card in Setting → Memory storage; where the wiki is stored is still set there (the *Business memory* card).
See
[Business memory](06-memory.md#business-memory-business-wiki).

| Field | Default | Details |
|---|---|---|
| Update the business wiki automatically | on | A worker summarizes finished threads into business wiki pages per module. The *Update now* button in the Memory menu → Business memory still works while this is off. |
| Interval (hours) | 6 | 1–168. The pause between automatic summaries. |
| Wiki writer model | automatic | The 9router model that writes the wiki; empty = the model of the active Planner node. Can also be chosen in the Node menu (Wiki Writer column). Choose a careful model that can read images, so screenshots from prompts make it into the pages. |
| Developer name | `git config user.name` | Recorded on pages and in the revision history, so the team knows who (whose AIT) changed what. Empty = `user.name` from the global git config (read straight from `~/.gitconfig`); when git has none either, AIT asks for it once in a popup when the UI is first opened. |

### Node fields

| Field | Default (UI) | Details |
|---|---|---|
| Name | — | A free label, shown in progress and in the thread detail. |
| Role | Executor | Planner / Tools Node / Executor / Reviewer / Tester / Wrapper / Context Keeper. |
| Model / combo | — | Chosen from the 9router `/v1/models`, grouped: *Combo* first, then per provider. When 9router is offline, a text input appears to type a model id by hand. |
| Temperature | Planner 0.2 · Tools Node 0.1 · Executor 0.1 · Reviewer 0.1 · Tester 0.1 · Wrapper 0.3 · Context Keeper 0.2 | 0–2. Leave it empty for the same default. |
| Max tokens | Planner 16384 · Tools Node 4096 · Executor 8192 · Reviewer 4096 · Tester 4096 · Wrapper 4096 · Context Keeper 4096 | 256–200000. The output limit per call. |
| Max parallel | Planner 2 · Tools Node 4 · Executor 6 · Reviewer 2 · Tester 1 · Wrapper 4 · Context Keeper 2 | 1–64. How many requests to this node's model may run at the same time, counted **across all threads** (useful for provider rate limits). Not a number of processes/instances. |
| Priority | 100 | A smaller number comes first. |
| Fallback node | none | Another node with **the same role** that takes over when this node still fails after 9router retried. See [Fallback node](#fallback-node). |
| Extra instructions | empty | Appended at the end of the role's built-in prompt, e.g. the team's coding conventions. |
| Skills | empty | Skills from the store (at most 10). Search yourself with the *Search the store yourself* dropdown (matches the skill's name, description, tags or content; click to attach/detach), or describe the need in *What ability is needed?* and click **Find skills**: the Skill Orchestrator suggests matching skills with its reasons, you tick them, then **Apply** and **Save**. Skill content is pasted into the node's system prompt after the extra instructions; the *Extra instructions* field does not change. |
| Tool delegation | everything in the planner | Planner only: per read tool, choose *Planner directly* or a Tools Node. See [Planner tools node](#planner-tools-node). |
| Auto Testing | off | Planner only: the planner also writes a testing plan, then the Tester node tries the change in the running application (UI through a non-headless browser, API through HTTP). See [Auto Testing](#auto-testing-tester-node). |
| Active | on | An inactive node is not used. |

The **Test** button sends a short prompt ("Reply with exactly one word: pong") and shows the latency, tokens and reply — a
quick way to make sure the model and key are right.

### Node selection rules

- **Planner**: the active node with the lowest priority is used. When it hits a rate limit, the one that takes over is
  the [fallback node](#fallback-node) chosen for it, not automatically the next active planner.
- **Wrapper**: only one is used — the active node with the lowest priority.
- **Executor**: every active node is used. Each task goes to the executor with the most free slots, then the lowest
  priority, then in turn.
- The **Wrapper** is also used for memory extraction, and becomes the passthrough model when *Passthrough model* is set
  to automatic.
- **Context Keeper**: the active node with the lowest priority summarizes the CLI conversation after each thread. Without
  this node, the Wrapper summarizes.
- **Tools Node**: not chosen by priority; used only by a planner that delegates tools to it.
- **Reviewer**: the active node with the lowest priority checks the executors' results (diff, contract, build result)
  in every verification round. Without this node, verification only uses the build. A careful model matters more than a
  fast one: every finding it makes triggers a fix task.
- **Tester**: the active node with the lowest priority. Used to judge unit tests that fail after a change (projects with
  a [unit test catalog](#project-unit-tests)), and to test the application when the planner in use has *Auto Testing*
  on (see [Auto Testing](#auto-testing-tester-node)).

### Fallback node

Each node can be given one **fallback node** in the node form (optional). The flow:

1. A call to the node fails with an error that means "the model is not available right now": rate limit **429**,
   timeout **408**, or a server error **5xx**. A call that receives no data at all from 9router for 3 minutes (a
   connection left hanging after a network drop) counts as a 408 timeout too, without waiting for the *Request timeout*.
2. 9router first retries with a pause (as before).
3. When it still fails, the **same** call is sent to the fallback node. For the planner and executors, the conversation
   continues from the last point instead of starting over. When the fallback node fails too and has its own fallback,
   the chain is followed further. Each node is tried at most once per call.
4. The switch is recorded in the activity log and the progress, e.g. *Executor T2 `model-a` not available (rate limit
   429), switching to `model-b`…*. The executor column of the task table shows the node that really did the work.

The rules:

- A fallback node must have the same role, cannot be the node itself, and the chain cannot loop (A → B → A is rejected
  when saving).
- A fallback node **may be inactive**. An inactive node receives no regular work, so it suits a pure fallback that is
  used only when needed.
- Other errors (e.g. 400 invalid request, 401 wrong key) do not trigger a switch, because another node would most
  likely fail the same way.
- Applies to every role: Planner, Executor, Reviewer, Tester, Wrapper, Context Keeper, and memory extraction (which uses
  the Wrapper node).
- Deleting a node, or changing its role, automatically detaches it from other nodes that use it as a fallback.
- Before this feature existed, active planners were automatically each other's fallback. On update, a migration filled
  in the fallback nodes of the existing planners in priority order, so the behaviour stays the same and can now be
  changed.

### Planner tools node

The output of planner tools (file content from `read_file`, results of `search_text`/`find_references`, etc.) goes into
the planner's conversation and is **sent to the planner model again at every later step**. One 600-line `read_file` at
step 3 is paid again at steps 4, 5, … until `submit_plan`. A tools node moves that work to a cheap model:

1. Create a node with the **Tools Node** role and a cheap model that **supports tool calling**.
2. Edit the Planner node → the **Delegate tools to a tools node** part: for each tool, choose *Planner directly* or a
   tools node.
3. A delegated tool is **removed** from the planner's tool list. Instead the planner gets the `investigate(question)`
   tool (plus `agent` when there is more than one tools node). The tools node runs its tools itself in a loop (at most
   12 steps), then returns **short findings** — facts with `path:line`, usually under 1,500 characters (cut at 6,000).
   The tools node does not copy code: code the planner needs as exact text (a file it will edit, a pattern to mirror) is
   named by its line ranges in `excerpts`, and **AIT copies those lines from the file** and attaches them under *Exact
   excerpts* (at most 8 ranges, 400 lines; secrets stay masked as with `read_file`). The text is exact and the cheap
   model never retypes a file, which was its slowest turn. Raw output of the other tools never enters the planner's
   context. The tools node's brief holds the project's
   folder map (depth 4, no files) so it does not spend turns on `list_directory`/`find_files`, and the tools node is
   asked to run every independent search/read in one turn. The planner puts a correct plan first, then parallelism:
   independent questions are sent together and split per area so the same files are not read twice; questions that
   depend on an earlier answer stay in order.

A tools node can also hold `run_command` and `web_fetch` in **read-only** mode: read-only commands (curl GET to
localhost, git reads, version checks) and hosts already allowed run; anything else is not run and is reported to the
planner. The planner keeps `run_command` to ask for permission; a tools node never pauses the thread for permission.

| Tool | Advice |
|---|---|
| `read_file`, `search_text`, `find_references` | Delegate — the largest output, the most wasteful in the planner. |
| `list_directory`, `find_files`, `read_thread`, `read_memory` | Optional; delegate when they are often long in your project. |
| `find_symbol`, `search_memory` | Keep them in the planner — their results are short, an `investigate` call costs more. |
| `git_diff`, `git_show` | Delegate when you often ask for change reviews — a diff can be long. |
| `git_status`, `git_log`, `git_blame` | Keep them in the planner — their results are short. |
| `run_build` | Keep it in the planner unless you want the tools node to summarize the error list. |

`ask_user` and `submit_plan` cannot be delegated. Neither can `code_outline` and `function_calls`: their results are
short, so they always stay in the planner, letting it see the code's skeleton first and ask the tools node with the
right files, members and line ranges. A tools node that holds `read_file` gets these two tools automatically too.

The rules:

- A tool goes to one tools node per planner; a planner may use several tools nodes (e.g. one for reading code, one for
  memory). Several `investigate` calls in one step run in parallel, limited by the tools node's *Max parallel*.
- An **inactive** tools node = its tools are run by the planner itself again (the node banner adds a note). A quick way
  to compare results with and without a tools node.
- A tools node uses a [fallback node](#fallback-node) with the Tools Node role when it hits a rate limit. When it still
  fails, the planner gets the error and continues with what it already knows.
- Deleting a tools node or changing its role releases every delegation to that node; changing the role of a planner
  deletes its delegations.
- Trade-off: the planner only sees what the tools node reports. Use a tools node model that copies names and line
  numbers carefully; a model that is too weak makes the planner ask again (more steps).
- Tools node tokens are recorded separately (*Tools node tokens* in the thread detail) and count toward the thread's
  total tokens.

### Auto Testing (Tester node)

With **Auto Testing** on in the Planner node's modal, a change that passes verification is tried right away in the
running application:

1. From the change, the planner judges **how to test it** and writes it into the plan: `ui` (a page), `api` (an HTTP
   endpoint) or `none` (cannot be tried from outside), with the application's URL on localhost and 1–6 scenarios.
2. Once the executors and verification are done, the **Tester** node runs them:
   - **UI**: the page is opened in a **non-headless** Playwright browser — an Edge/Chrome window shows up on the screen
     and you can watch the tester fill in forms and click buttons;
   - **API**: HTTP requests are sent to the endpoint, and their **output** (status, duration, response body) shows in
     the CLI progress, in the final answer (*API output*) and in the *Testing* card of the thread detail.
3. The result per scenario (passed / failed / could not be checked) goes into the final answer; a failed scenario makes
   the status *Partly completed*.

What to prepare:

- Add a node with the **Tester** role (a model that supports *tool calling*; default max parallel 1), then turn on
  *Auto Testing* in the Planner node. Without an active Tester node, the pipeline banner adds a note and testing is
  skipped. The same node judges failing unit tests, so use a strong model (the `AIT Tester` preset in Setup).
- **An application that is not running yet is started by AIT** for that testing, then stopped again afterwards: an
  ASP.NET Core project (`dotnet run --urls <baseUrl>`), Laravel (`php artisan serve`), or the `dev`/`start`/`serve` script
  in `package.json`. For another stack, or when the command is special, write it in `.aithread/project.json` (committed
  by the team): `{ "run": "docker compose up web" }`. An application you already run yourself (e.g. `dotnet watch`) is
  used as it is and not stopped. When the application still does not answer, testing ends as *could not be checked* and
  can be repeated with **🧪 Test again** in the thread detail. See
  [Running the application](07-cara-kerja.md#running-the-application).
- Login: nothing to prepare. When the application asks for a login, the tester **asks in the chat**; reply
  `login <username> <password>` (a test account) and the testing runs again. The password is not stored on disk, not
  sent to a model, and remembered for that chat session. For a thread from the Dashboard/Telegram, fill it in in the
  **🧪 Test again** modal. See [Login](07-cara-kerja.md#login).
- **Test accounts** (optional, for multi-user flows such as multi-level approval): fill them in in Project > Detail →
  *Test accounts* (username, role, attributes; no password per account) and, when needed, the *Dev password* (default
  `Test@1234`). The tester switches accounts with `switch_user` and uses the project's *login as* feature when one is
  detected. See [Test accounts](07-cara-kerja.md#test-accounts-switching-users).
- Only `localhost`/`127.0.0.1`/`[::1]` addresses may be tested. Point the application to a development database.

Flow details, the tester's tools and its limits: [Testing (Auto Testing)](07-cara-kerja.md#6-testing-auto-testing).

### Project unit tests

A project that has unit tests (xUnit/NUnit/MSTest, jest/vitest/mocha, pytest, PHPUnit, Maven/Gradle, Go, Rust) uses the
[unit test catalog](07-cara-kerja.md#unit-tests) automatically: the first thread registers its tests, then every thread
runs the related tests before and after the change. There is no global switch; its settings are per project in
[Project > Detail → Unit tests](05-menu.md#unit-tests) and apply to every developer of that project (through the
project key, so the project needs a key — see [Project key](06-memory.md#sharing-memory-between-developers-project-key)).
A project that **has no test project yet** takes part too: when a change needs a new test, the planner creates the test
project as well (see [Planner decisions](07-cara-kerja.md#planner-decisions)). With Auto Testing on, a change in a
background job or one whose effect is in an external system is always checked with a dry-run unit test (see
[Background jobs and external systems](07-cara-kerja.md#background-jobs-and-external-systems)).

What to prepare:

- **The runner can run on this machine**: the .NET SDK (or Visual Studio for the `vstest.console` of old .NET Framework
  projects, whose test assembly is already built), `npm install` (jest/vitest/mocha in `node_modules`), `composer
  install` (`vendor/bin/phpunit`), Python with pytest (a `.venv`/`venv` virtualenv is used when present), Maven/Gradle
  (or the `mvnw`/`gradlew` wrapper), Go, Cargo. AIT does not install packages; a suite that cannot run is shown with its
  reason in the Unit tests card.
- A **Tester node** (strong model) to judge failing tests: a regression in the code, a wrong new test, the environment,
  or flaky. Without a Tester, AIT fixes the code under test directly, and the pipeline banner adds a note.
- Tests that need a database/network/config that does not exist on your machine do not have to be turned off: their
  result is *skipped* with a reason, not failed.
- For a project whose code must not be stored outside the repository, turn off *Keep test snapshots* (a lost test can
  then only be rewritten from its scenario).

### How *Max parallel* works

Each model call to a node uses one slot; when all slots are taken, the next call waits. Slots are shared between all
running threads (several projects at once).

| Role | What uses a slot |
|---|---|
| Executor | One task while it is being worked on, including retries. An executor with 6 slots works on at most 6 tasks at once. |
| Planner | One slot per model call; the slot is released while the planner runs a code-reading tool. With 2 slots, at most 2 threads wait for a planner answer at the same time. |
| Wrapper | One slot for the final summary and one for memory extraction per thread. |
| Context Keeper | One slot per session context update (once per CLI thread, in the background). |
| Tools Node | One slot per model call inside an `investigate`; released while the tools node runs a tool. |

On top of that, Setting → *Max parallel* limits the tasks running at once **per thread**. The number of tasks really
running at the same time = the smallest of: the tasks that are ready, *Max parallel* per thread, and the total free
slots of all executors. Passthrough calls (CLI supporting requests) and the *Test* button do not use slots.

### Planner token cost

The planner works through 9router (`/v1/chat/completions`, OpenAI format), and every step sends the whole conversation
again. The part that is the same as the previous step is served from the provider's **prompt cache**, which is much
cheaper than new input:

- Claude models (including `cc/…`): 9router adds `cache_control` itself on the tools, the system prompt and the last
  assistant message.
- OpenAI, DeepSeek, Gemini and the like: automatic prefix caching on the provider's side.

AIT keeps the start of the conversation the same so the cache keeps matching: the planner's tool list does not change
during a thread, the brief is built from the most stable parts (project, structure) to the request at the end, and
executor messages start with the same contract + plan summary for every task. Each request also carries a
`prompt_cache_key` `ait:<thread>:planner|tools|executor` so providers (OpenAI/Codex) route requests that start the same
to the same cache; a model whose provider rejects that field is called without the key automatically (logged once). The
thread detail shows the share, e.g. *Planner tokens 120,000 (85% cache)*. A percentage of 0 means the provider does not
report its cache or does not use one.

To save more:

- Lower Setting → *Planner steps*.
- Avoid `cc/…` subscription models (Claude through 9router) for the planner; use a model/combo with a generous quota.
- Add a second Planner node and choose it as the main planner's [fallback node](#fallback-node).
- Delegate tools with large output to a [tools node](#planner-tools-node) with a cheap model.

### Choosing models

| Role | Needs | Advice |
|---|---|---|
| Planner | The smartest, a large context, **must support tool calling** (function calling). | The top-class model you have in 9router. The quality of the plan & contract decides the accuracy of every executor. |
| Executor | Cheap, fast, follows the format. | Cheap/free models. Add several nodes from different providers so load and rate limits are spread; a combo gives automatic fallback. |
| Wrapper | Cheap, can summarize. | The same cheap model as the executors is enough. |
| Tools Node | Cheap, **must support tool calling**, copies paths, line numbers and identifiers carefully. | A cheap model that is strong at code; its context fits several files at once. |
| Tester | Judges diffs and test code carefully; **must support tool calling** for Auto Testing. | A strong model, planner class: its decision settles whether the code or the test is fixed. Only called when a unit test fails or Auto Testing runs. |
| Context Keeper | Cheap, copies file names & identifiers carefully, enough context for ±15 thread notes. | A cheap model that summarizes neatly; tool calling is not needed. Do not use a Claude subscription model, so it does not use up the quota. |

Tips:

- An executor's *Max tokens* must be enough for the whole edit output. A `create` task writes a whole file — raise it
  when "Output was cut off (max tokens)" shows up often.
- When the planner often fails to produce a valid plan, raise *Planner steps* or switch to a stronger model.
- A planner model that does not support tool calling is still tried: AIT reads a JSON plan from the text, and when it is
  still plain text after two reminders, that text is used as a direct answer.

Example setup (the model ids are only illustrations — choose from the dropdown for your providers):

| Name | Role | Model | Max parallel | Priority |
|---|---|---|---|---|
| Main planner | Planner | the strongest model, e.g. from the Claude Code provider | 2 | 1 |
| Executor A | Executor | a cheap combo | 6 | 10 |
| Executor B | Executor | a cheap model from another provider | 4 | 10 |
| Wrapper | Wrapper | a cheap model | 4 | 1 |

## Skill menu

The **Skill** menu in the sidebar (`/Skill`, formerly the Setting → Skill tab; old `/Setting#skills` links land here)
holds the Skill Orchestrator and the offline skill store. How it works is explained in
[Skills on a node](07-cara-kerja.md#skills-on-a-node).

| Field | Default | Details |
|---|---|---|
| Skill Orchestrator model | automatic | The 9router model that examines a link's content when skills are fetched, and that finds skills in the Node modal. Can also be chosen in the Node menu (Skill Orchestrator column). *Automatic* = the model of the active Planner node with the lowest priority. |
| Skill character limit per node | 40000 | 4000–200000. The total skill content pasted into one node's system prompt, split evenly between skills. |
| Skill sources | — | Paste a link, then click **Fetch skills**. Accepted: a GitHub/GitLab repository (`/tree/<branch>/<folder>` allowed), `owner/repo`, a `.git` URL, an `.md` file or a web page. Status: *Queued* → *Downloading…* → *Analyzed by AI…* → *Ready*/*Failed*. **Sync** fetches it again; **Delete** removes the source, its skills and its folder, and detaches those skills from nodes. |
| Skill store | — | The list of skills with a search (name, description, tags, source), the number of nodes using each, and a **View** button for the content and its supporting files. |

**Save Skill** only saves the model and the character limit. Sources are processed one by one in the background, and
their status changes live without a refresh. Store folder: `%LOCALAPPDATA%\AIThread\skills`, changeable through
`AIThread:SkillsDirectory` in `appsettings.json`.

## Team setup profile

One JSON file (format `aithread-setup` v1) holding the settings a team shares, so a new developer's setup does not start
from zero. The team lead sets up AIT once and exports it; other developers import it and only fill in their own
secrets.

| Part | Content | Not included |
|---|---|---|
| 9router connection | Base URL | API key |
| Proxy & orchestration | every field of the *Proxy & orchestration* and *Build & verification* tabs | proxy API key |
| Nodes | every node: role, model, temperature, tokens, parallel, priority, extra instructions, active, fallback node (name), tool delegations (tools node name), skills (name + source link) | — |
| Memory storage | provider, host, port, database, username, extra options for short, long and business memory. A SQLite file in the default location is written as *default* | password |
| Business memory | on, interval, model | developer name |
| Skill | Skill Orchestrator model, max characters, the links of every skill source | — |

Telegram (token and chats) never goes into a profile.

**Export.** Setup → *Export profile*, or `AIThread.exe setup export team.json` (the instance must be running).

**Import.** Setup → *Import profile…*, or `AIThread.exe setup import team.json` (add `--yes` for no confirmation):

1. **Preview.** The list of changes per part compared with this computer; nothing is changed. Parts can be ticked or
   skipped.
2. **Secrets.** Fill in the 9router API key and the database passwords that are needed. A field already stored for the
   same server may be left empty. For a **different** memory server, this computer's old password is never sent there:
   only the password you type is used (empty = no password).
3. **Apply.** In order: 9router → orchestration → memory storage → skills → nodes → business memory. A part that fails
   (e.g. the memory database cannot be reached) is reported, the other parts are still applied. Fix it, then import
   again.

| Rule | Details |
|---|---|
| Idempotent | Importing the same file again only applies what differs; with no difference, nothing changes. |
| Nodes | Matched by **name + role** (case is ignored). New nodes are created first, then fallback nodes and tool delegations are connected by name. For a node in the profile, the file's content applies (including the skill list). |
| Other nodes | *Leave them as they are* (default), or *Deactivate* so the pipeline is exactly the same as the profile (nodes are not deleted). |
| Missing links | A fallback node or tools node that is neither in the profile nor on this computer is cleared, with a warning. |
| Skills | New skill sources are added, then fetched in the background, so their skills do not exist yet at the first import. Import the profile again once the sources are *Ready* to attach the skills to nodes. |
| Memory | *Copy the existing memory to the new storage* (default on) is the same as the option in Setting → Memory storage. |
| Version | A file from a newer AIT (a higher format version) is rejected; update AIT first. |

Next: [CLI integration](03-integrasi-cli.md).
