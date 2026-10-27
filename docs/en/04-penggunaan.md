# 4. Everyday usage

## Workflow

1. Make sure 9router and AIT are running (the *9router* pill in the AIT sidebar is green).
2. Open your CLI (Claude Code, Codex, Gemini CLI, …) in a project folder that is pointed to AIT.
3. Type your request as usual.
4. Follow the progress in the CLI, or open Dashboard → the thread for the details.
5. Review the changes with `git diff`.
6. Run the follow-up steps the summary mentions yourself (build, SQL/migration scripts, restart).
7. Continue with the next prompt — the earlier conversation and memory are read by the planner too.

## Reading the output in the CLI

An example for the prompt *"Add a description column to the user form: the field in the UI, the logic in JS, validation
in the service, and the database DDL."*:

`````
**AIT** · `SampleApp` · thread `5e469b54`
````text
🧠 Planner · cc/claude-opus
│  ├─ 🔖 Label · 1 file → User·UI · 3 relevant memories
│  ├─ list_directory  .
│  ├─ search_memory   "user description validation"
│  ├─ read_memory     2 items
│  ├─ read_file       src/Web/Views/User/Index.cshtml
│  ├─ read_file       src/Web/wwwroot/js/user.js
│  ├─ read_file       src/Service/UserService.cs
│  └─ done in 12.3s (model 9.8s · tools 2.4s)
│
📋 Plan · 4 tasks
│  Adds a Description field to the User form: the input in the view, sending the value in JS, validation
│  in the service, and a DDL script for the new column.
│  ├─ T1 src/Web/Views/User/Index.cshtml
│  │     Add description textarea to the user form
│  ├─ T2 src/Web/wwwroot/js/user.js
│  │     Send description in saveUser
│  ├─ T3 database/AddUserDescription.sql
│  │     DDL script for Users.Description
│  └─ T4 src/Service/UserService.cs ← T3
│        Validate description length
│
🚀 Execution · 2 parallel executors
│  ├─ ✅ T2   1.7s  Combo Executor  saveUser now sends description from #txtDescription.
│  ├─ ✅ T3   1.7s  GLM Executor    Created an idempotent script adding Users.Description.
│  ├─ ✅ T1   1.8s  GLM Executor    Added the txtDescription textarea after the name field.
│  ├─ ✅ T4   3.3s  Combo Executor  Added a 500-character limit for Description.
│  └─ 4/4 succeeded · 5.0s (1.7x faster than sequential)
│
🔍 Verification · dotnet build SampleApp.sln + reviewer cc/claude-sonnet
│  ├─ 🔨 Build failed: 1 error (14.2s)
│  │     src/Service/UserService.cs(48,13): CS0103: The name 'MaxDescriptionLength' does
│  │     not exist in the current context
│  ├─ 🔎 Reviewer: 1 issue — The description length limit constant is not declared.
│  │     src/Service/UserService.cs — MaxDescriptionLength is used but never declared.
│  ├─ 🔧 Fix 1 · 1 task
│  │     ✅ V1-1   2.1s  GLM Executor    Declared MaxDescriptionLength = 500.
│  ├─ ✅ Build succeeded (9.8s)
│  ├─ ✅ Reviewer: Every layer uses txtDescription/Description as in the contract.
│  └─ passed after 1 fix round(s)
│
📦 Summary · glm/glm-cheap
````

---

✅ **Completed** · 4/4 tasks · build passed · reviewer agrees

The **Description** field is added end to end. Run `database/AddUserDescription.sql` on the database before trying
the form.

**Changed files**

| File | Action | Lines | Task |
|---|---|---|---|
| `src/Web/Views/User/Index.cshtml` | changed | +4 | T1 |
| `src/Web/wwwroot/js/user.js` | changed | +1 −1 | T2 |
| `database/AddUserDescription.sql` | new | +9 | T3 |
| `src/Service/UserService.cs` | changed | +7 | T4, V1-1 |

⏱️  48.6s · 🪙  41.2k tokens · 🧠 2 memories used, +2 new memories · 📚 Wiki queued for the next summary (every 6 hours)

📊 **Quota left**
- **Antigravity · DS997**
  - 🟩🟩🟩🟩🟩🟩🟩🟩🟩⬜ 98% · Gemini 3.8 Flash (High) · reset 1h 56m
- **Claude Code · Andi**
  - 🟥⬜⬜⬜⬜⬜⬜⬜⬜⬜ 17% · session (5h) · reset 1h 20m
  - 🟩🟩🟩🟩🟩🟩⬜⬜⬜⬜ 69% · weekly (7d) · reset 4d 18h

🔗 Details & rollback: http://localhost:5225/Thread/Detail/5e469b54-…
`````

The output has three parts:

1. **The anchor line** `**AIT** · project · thread …`: the detected project name and the thread id (look it up in the
   Dashboard). This line only shows for sessions inside a project (a folder with `.git` or a build file), and always
   shows even when progress is hidden — AIT uses it to recognize earlier threads in the conversation. Old replies that
   still say `**AIThread**` are recognized too.
2. **Progress** in a text block (monospace font), shaped as a *vertical stepper*: each phase starts with an emoji, phase
   entries use `├─`, and a phase is closed by a `└─` line with its result. Long lines are cut at 90 columns.
3. **The final answer** after `---`.

AIT's own lines (progress, status line, tables, footer) follow the [display language](02-konfigurasi.md#display-language);
the Wrapper's text follows the language of your prompt.

| Phase / line | Meaning |
|---|---|
| `🧠 Planner` | The planner reads the code. The first entry `🔖 Label` is the result of the label lookup before the model is called: the files the prompt names with their module·layer (at most 4 shown), then the number of memories the two-layer search found for the planner's brief (see [Planner](07-cara-kerja.md#3-planner)). `from the session (thread …)` means the prompt names no file, so the label is taken from the earlier AIT thread in this chat. Each following entry is one tool call (read-only); tool names are left-aligned so their arguments line up. `run_build` shows on two lines: the command, then its result below it. When a thread continues after a planner question, the label line does not show again. |
| `🧷 Unit test restore` | Only when tests in the project catalog do not exist in your repository: one line per restored test (from a snapshot, inserted again, or rewritten by an executor) and git notes. See [Restoring](07-cara-kerja.md#restoring-lost-tests). |
| `📋 Plan` | The plan summary, then one entry per task: id, full relative path, dependencies (`← T3` = waits for T3), and the task title on the line below. |
| `🚀 Execution` | One line per finished task, in columns: status, id, duration, executor, summary. `✅` succeeded, `❌` failed (with the reason), `⏩` skipped because a task it waits for failed, `🟡` the node switched to its fallback. The closing line gives the real time and, when parallelism really saved time, how many times faster than sequential. |
| `🧪 Unit test · baseline` | Only with a [unit test catalog](07-cara-kerja.md#unit-tests): the results of the related tests before the change (run in parallel with the planner, or taken from an earlier result for the same code). |
| `🔍 Verification` | AIT builds the project, runs the related unit tests, and/or the Reviewer node checks the result (see [Verification](07-cara-kerja.md#5-verification-build-and-reviewer)). `🔨` build failed (at most 5 error lines), `🔎` reviewer findings, `✅`/`🔴 Unit test · 14 tests · 12 passed, 1 regressions` unit test results (failing tests below it), `🩺 Triage` the Tester node diagnoses failing tests, `🔧 Fix` = a round of fix tasks `V1-1`, …, `🟡` warnings. |
| `🧪 Testing` | Only with [Auto Testing](02-konfigurasi.md#auto-testing-tester-node): the Tester node tries the change in the running application. One line per tool (`browser_open`, `browser_fill  Description ← "AIT test"`, `http_request  POST /api/users`, …); below `http_request` the status, duration and the start of the response body show. `🌐` = the tester browser opened (its window shows on the screen), `🚀`/`🟢` = the application was not running, AIT started it and it answers, `🟡` = the application cannot be reached, `🔐` = the tester needs a login. The closing line: `passed 2/2 scenarios`, `1 of 3 scenarios failed`, or *skipped* with the reason. |
| `📦 Summary` | The Wrapper node is writing the final answer. Not shown when there is no active Wrapper. |

The final answer is written partly by AIT itself (from the results, not from a model) and partly by the Wrapper:

| Part | Made by | Content |
|---|---|---|
| Status line | AIT | `✅ Completed` / `🟡 Partly completed` / `❌ Failed`, the number of plan tasks that succeeded (fix tasks `V` are not counted), the last build and reviewer result, the unit test result (`unit tests passed (12)`, `unit test: 1 regressions`, …), and the testing result (`testing passed`, `testing: 1 failed`, …). A failed testing scenario, a unit test regression or a failing new test makes the status *Partly completed*. A thread that only tests uses `🧪 Testing done · no code changes`. |
| **Needs attention** | AIT | Only when there are failed/skipped tasks, build errors/reviewer findings, or unit tests that still fail. |
| Text | Wrapper | 1–3 sentences on what was done, advice for remaining problems, and follow-up steps when needed. Without a Wrapper node (or when the Wrapper fails), the plan summary is shown. |
| **Changed files** | AIT | One line per file from the diff: action (`new`/`changed`/`deleted`/`moved`), `+`/`−` lines, and the tasks that touched it. |
| **🧪 Unit test** | AIT | Only when catalog unit tests were run or restored: a summary, then the tests that need attention (at most 10) — `❌` regression/failed with its message, `✅` failed before the change and passes now, `🟠` already failed before the change, `〰️` flaky, `⏭️` skipped with the reason — tests marked obsolete (`🗃️`, waiting for a delete confirmation), restored tests (`♻️`), and notes. Tests that keep passing are only counted. |
| **🧪 Testing** | AIT | Only with Auto Testing: level, application URL, browser, the tester's summary, `✅`/`❌`/`🚫` per scenario with its explanation, *Browser errors* (at most 5), then **API output**: each request (at most 5) with status, duration and response body in a code block (at most 30 lines / 2,000 characters; the rest is in the thread detail). When the tester needs a login, it ends with the question **🔐 Tester needs a login** — see [Login for testing](#login-for-testing). |
| Footer | AIT | Total duration, total tokens (planner + executor + reviewer + wrapper + memory extraction), the memories the planner used and new memories, the business wiki queue (when *Business memory* is on), the providers' *Quota left*, and a link to the thread page for details & rollback. |

The footer waits for memory extraction to finish, at most 30 seconds. When it takes longer, the footer writes *new
memory is still being processed* and the extraction keeps running in the background. What is stored as the thread's
answer (Dashboard, Telegram, context keeper) is only the Wrapper's text or the automatic summary.

**Quota left** is read from 9router (`/api/usage/{connection}`) and only covers the provider accounts whose models are
used by the active nodes and their fallback nodes (combo members are counted too). Per 9router account, each limit (e.g.
Claude's 5-hour / 7-day windows) shows as a 10-square bar (1 square = 10%, any remainder at least 1 square) with the
percentage left and the reset time (`m` minutes, `h` hours, `d` days). Bar colors: 🟩 more than 50% left, 🟨 > 35%,
🟥 > 0%, and all ⬜ when used up — a terminal can only color emoji, and there is no gray square emoji. For per-model
quotas (e.g. Antigravity) only the models used by nodes are shown. Plain API key accounts and providers whose quota
9router cannot read are not shown. The result is cached for 60 seconds; when 9router does not answer within 10 seconds
or its dashboard requires a login, this part is skipped — see [Troubleshooting](08-troubleshooting.md#9router).

Progress can be hidden with Setting → *Show progress in the CLI*; the anchor and the final answer still show.

## Writing effective prompts

AIT is most effective for changes that touch several files/layers at once.

- **Name the layers you want**: "the field in the UI, JS, the API parameter, the service parameter & validation, DDL".
- **Point to an existing example** so the planner copies its pattern: "the same as the `Email` column in the same form".
- **State the limits**: "do not change the public API", "use an EF migration, not an SQL script", "at most 500
  characters".
- **One feature per prompt.** Large changes are more accurate when split into several prompts in sequence.
- **Questions can be asked directly** ("where is the user validation?"). The planner reads the code and answers without
  creating tasks — no executor runs.
- **Uncommitted changes can be referred to directly** ("review my changes", "continue what I was working on", "what
  changed since yesterday's commit?"). In a git repository, the planner reads `git status`, `git diff` and the commit
  history itself through the [git tools](07-cara-kerja.md#3-planner), so the diff does not have to be pasted.

## What AIT does not do

- It does not run migrations, checkout/merge/rebase/reset, or package installs (`npm install`, `composer install`, a
  manual restore) on its own. Besides the project's **build** (when *Automatic build & verification* is on) and the
  **unit tests** of the [project catalog](07-cara-kerja.md#unit-tests) (`dotnet test`, jest/vitest/mocha, pytest,
  PHPUnit, Maven/Gradle, `go test`, `cargo test`), the planner has a `run_command` tool for **read-only** commands
  (e.g. `curl` GET to see the shape of an API response, git reads, tool versions), `web_fetch` to read web pages, docs or
  APIs over GET, and `start_process` for a dev server that must keep running while it investigates. Commands that may change something,
  `curl` to an outside host, **commit/push**, and changes to files **outside the project folder** only run after you
  allow them, see [Developer permission](#developer-permission). Other steps are named in the summary.
- It runs your application only for testing. With [Auto Testing](02-konfigurasi.md#auto-testing-tester-node), AIT tries
  the application on localhost (browser and HTTP requests); one that is not running yet is started and then stopped
  again after testing ([Running the application](07-cara-kerja.md#running-the-application)). AIT does not fix code based
  on the testing results.
- It does not use the CLI's built-in tools: while orchestration is on, the CLI works as a conversation view. For work
  that needs to run commands other than the build, turn *Orchestration on* off for a while (the CLI becomes a plain agent
  through 9router) or revert the integration.
- It does not ask permission per edit inside the project — executors write straight to the files. Work in a git repo.

## Reviewing the result

- `git diff` / `git status` to see every change.
- Dashboard → click the thread → click a task row to see its instruction, summary, **diff**, error and model output
  (live output is only visible when the page is already open while the task runs).
- A failed task: read the reason on the `❌` line or in the task detail. Failed because of the provider/model (503, rate
  limit): reply `retry` (see [Retrying failed tasks](#retrying-failed-tasks)). Failed because of its content: try again
  with a more specific prompt for that part only, e.g. "continue the description validation in UserService that failed
  earlier".

Thread status:

| Status | Meaning |
|---|---|
| Completed | The planner answered directly, or at least one task succeeded (other failed tasks, build errors/reviewer findings left after verification, and failed testing scenarios are recorded in the error column). |
| Failed | No task succeeded, or the planner/9router failed. The cause shows in the CLI and in the thread detail. |
| Cancelled | Stopped by you. |
| Changes undone | The file changes of this thread were reverted with **Undo file changes** (see below). |

## Cancelling

- Stop the request in the CLI while the thread runs (e.g. **Esc** in Claude Code) — the request is cut and the thread is
  cancelled.
- Or open the thread detail → **Cancel thread**.

Unfinished tasks are stopped and write nothing. Tasks that **already** finished before the cancel stay in the files. To
revert them, use **Undo file changes** (below).

## Retrying failed tasks

Thread detail → **🔁 Retry failed** (shown when the thread is completed, failed or cancelled and still has plan tasks
that did not succeed), or straight from the CLI chat: reply **`retry`** (also `retry failed tasks`, `try again`,
`ulangi`, `ulangi yang gagal`, `coba lagi`). An answer that still has failed tasks writes this hint under *Needs
attention*.

- From the chat, what is retried is the last AIT thread in that chat (and the same project), with progress and the
  answer shown in the chat like a regular thread; **Esc** cancels it. Only that short reply on its own is recognized —
  "retry the validation in UserService" stays a regular prompt. When the last thread has no task that can be retried
  (a planner that failed before making a plan, or every task already succeeded), `retry` is also run as a regular
  prompt: the planner reads it together with the earlier conversation.

- The stored plan is used again; **the planner is not called**, so there is no planner cost.
- Only plan tasks that failed, were skipped or were cancelled are run again. Tasks that already succeeded are not
  repeated, and dependencies on them count as met.
- After that, verification (build/reviewer) and the wrapper run again over the whole thread result. Old fix tasks that
  did not succeed are marked *Skipped* because the new verification replaces them.
- Everything is recorded in the same thread; the count shows in *Retries* in the time & token panel.
- Useful after a rate limit, after adding an Executor node that did not exist yet, or after fixing a model
  configuration.

## Login for testing

When the application asks for a login, the end of the answer holds **🔐 Tester needs a login**. Reply in the same chat:

```
login admin secret123
```

AIT right away runs the testing of that thread again with that account and replies with the new testing result. The
login is remembered for the chat session (8 hours, in memory), so the next threads are not asked again. The password is
not written to disk and never sent to a model (the tester only sees `{{password}}`), but it does stay in your chat
history — use a test account. Details: [Login](07-cara-kerja.md#login).

## Testing again

Thread detail → **🧪 Test again** (a *Completed* thread made with Auto Testing that has a UI/API testing plan) runs the
scenarios of the testing plan again against the running application, without the planner and without changing code.
Useful when the application was not running at the first testing or had to be restarted to load the change. Its modal
has optional **username & password** fields for applications that need a login (used once, not stored). Its progress
shows in the *Testing* card and the thread activity (not in the CLI).

## Follow-up tests

When the Tester suggests **follow-up tests** (e.g. approve level 1, edit before approving), the final answer lists them
numbered. Run one by replying `run follow-up test <number>` (or `jalankan test lanjutan <number>`) in the same chat, or
with the **Run** button in the *Testing* card of the thread page. The tester uses the project's test accounts and the
values it remembered from earlier steps (e.g. a document number), and can then suggest the next step again. Details:
[Follow-up tests](07-cara-kerja.md#follow-up-tests-and-values-between-steps).

To choose the account of each role and the form values yourself, reply `create scenario for follow-up test <number>`:
AIT writes an Excel file in `.aithread/testing/`. Fill in its yellow cells, save it, then reply `run scenario <name>`
(the first time per project: `run scenario <name> agree`). The Indonesian versions of these commands are recognized too
(`buat skenario test lanjutan <number>`, `jalankan skenario <name> setuju`) —
[Display language](02-konfigurasi.md#display-language). Details:
[Multi-step scenarios](07-cara-kerja.md#multi-step-scenarios-excel).

## Project unit tests

When a project has tests (xUnit/NUnit/MSTest, jest/vitest/mocha, pytest, PHPUnit, JUnit, Go, Rust), the first thread
registers them in the project's **unit test catalog**, and from then on every thread runs the tests related to its change
before and after changing the code. A test that passed before and fails afterwards (a regression) is fixed in the
**code**, not by weakening the test. The catalog is shared by every developer of that project (see
[Unit tests](07-cara-kerja.md#unit-tests)).

Example prompts:

| Prompt | What happens |
|---|---|
| *"Add a member discount to OrderService, write its unit tests"* | The planner designs the test cases (title, scenario, values), executors write the code and the test file in parallel, AIT runs the new tests after the build, and the new tests join the catalog with a `UT-…` number. Without the words "unit test", new tests are only made when the project setting is *Automatic for business logic changes*. |
| *"Run all unit tests"*, *"run the tests of the order module"* | No code change: AIT runs the tests, then the Wrapper explains the result. |
| *"Why does UT-0012 fail? fix it"* | The planner runs that test with `run_tests`, reads the code, then plans the fix. |
| *"Remove the PDF export feature"* | The tests of that feature are marked *obsolete* with the reason and are not run anymore. Confirm their removal in Project > Detail → Unit tests. |

- Catalog tests that do not exist in your repository yet (made by another developer and not pulled by you yet, or
  deleted) are restored automatically before the planner runs and show up as new files in `git status`. Commit those
  files, or when the deletion was on purpose, mark the test obsolete.
- Tests that need a database, network or config that does not exist on your machine show as *skipped* with the reason,
  not as failed.
- With an active **Tester** node, a failing test is diagnosed first (wrong code, a wrong new test, or the environment)
  before it is fixed. Without a Tester, AIT fixes the code under test directly.

## Undoing file changes (rollback)

Thread detail → **↩ Undo file changes** returns every file this thread changed to its state before the thread ran.

How it works:

1. **Before** an executor writes a file for the first time in a thread, its original content is stored in the database
   as it is (byte for byte, including encoding and line endings). A file that did not exist is recorded as "did not
   exist". Later writes in the same thread (fix rounds, retries) do not overwrite this record.
2. **After** every write, the fingerprint (SHA-256 hash) of the content AIT wrote is recorded too. A file the thread
   deleted or moved (`delete`/`move` tasks) is recorded as "deleted by the thread". An `extract` task records its source
   and target files like a regular write.
3. This button opens a preview per file:
   - *Returned to the original content*: the file's current content is still exactly this thread's result, or the file
     was deleted/moved by this thread and its old path is still empty.
   - *Deleted*: the file was created (or moved to this path) by this thread and its content has not been changed by
     anyone else.
   - *Already the same as the original*: nothing to do.
   - *Changed again*: the file was edited after this thread (by you or another thread), or deleted.
4. **All or nothing**: when even one file is *Changed again*, the undo is refused and **no file is touched**. Revert that
   file by hand (e.g. with git), or undo the newer thread first, then try again.
5. After it succeeds, the thread status becomes *Changes undone*. That thread cannot be retried or undone again. In the
   [unit test catalog](07-cara-kerja.md#unit-tests), new tests added by that thread are removed and tests it marked
   obsolete are active again.

Limits:

- Only while the thread is not running.
- Only covers files AIT wrote, deleted or moved, not other changes (e.g. build output). Tests
  [restored automatically](07-cara-kerja.md#restoring-lost-tests) before the planner are not undone either: they are not
  part of the request.
- A folder that becomes empty after a file made by the thread is deleted is deleted too.
- Threads that ran before this feature existed have no records, so they cannot be undone.

## Running a prompt without a CLI

The *Run prompt* card in the Dashboard is hidden for now. To test a node configuration without a CLI, send
`POST /api/threads` with the body `{ "projectId": <project id>, "prompt": "…" }`; the reply is a thread id that can be
followed at `/Thread/Detail/<id>`.

## Running a prompt from Telegram

While the laptop is left running, send prompts from your phone through a private Telegram bot. Choose the project with
`/projects`, then send a plain text message: the bot shows the thread status updated live (plan, each task, a *Cancel*
button) and sends the wrapper summary when it is done; `/diff` shows the file changes. Setup and the command list are in
[Prompts from Telegram](11-telegram.md).

## Sessions outside a project

AIT only orchestrates sessions opened inside a project: a folder holding `.git` or a build file (`.sln`, `pom.xml`,
`package.json`, `go.mod`, etc.), or one of its sub-folders. The project's language is not limited. On the first prompt
of a session, AIT checks its working directory:

- **a project marker exists** → the project is registered (when it is not yet) and every prompt of that session is
  orchestrated;
- **no marker** → the session is **ignored**: prompts are forwarded straight to 9router, the CLI works as a plain agent,
  and no thread, project or memory is recorded. For example an empty folder where you only ask for a text file. The
  dashboard log shows
  `Session xxxxxxxx (…) is not part of a project (the reason) — the prompt skips orchestration and is passed through directly.`

A parent folder that only holds several projects (e.g. 4 repos side by side) is ignored too because it is ambiguous;
the log names the projects inside it. Open the CLI session inside one of them. Want a folder without a marker to be
orchestrated and have memory anyway? Register it by hand in the Project menu (*Register manually*).

The decision for an ignored session is checked again after about 5 seconds, so as soon as you add `.git` or a build
file, the next prompt is recognized.

## Several projects at once

Sessions from different projects may run at the same time (e.g. 4 CLI sessions in 4 projects, or a mix of Java, Node
and .NET). Each thread is bound to its own project root, project memory is kept apart, and the *Max parallel* limit of
each node is shared between all threads.

## Checking another folder or project

Name its full path in the prompt, e.g. *"check the structure of `D:/project/otherproject` and compare it with this
project"*. The planner may **read** (list folders, search text, read files) outside the project root, so it can answer
from there. When you ask for a **change** in another folder, the planner may create tasks with absolute paths; the
thread first stops and asks permission for that folder (its repository/project root), see
[Developer permission](#developer-permission). System/program folders and credential files are never changed.

## Developer permission

AIT stops and asks before doing something beyond the usual limits:

| Action | Permission target |
|---|---|
| Changing files outside the project folder | that folder's repository/project root (or the folder) |
| A `run_command` that may change something (POST/PUT/DELETE, writing files, installs, `git commit`, shell operators `;` `&&` `>`, …) | the exact command text, or a pattern (`npm install *`) |
| `start_process` (a background process: dev server, watcher) | the exact command text, or a pattern |
| `curl` GET/HEAD or `web_fetch` to a host other than localhost | `host:port` (one permission for both) |
| Creating a new branch | the repository + branch |
| Opening a pull/merge request | the repository + `base<-head` |
| Committing the thread's files | the repository root |
| Pushing the branch | the repository + `remote/branch` |

The reply is a numbered card, answered in the CLI (or Telegram) or with the buttons on the thread detail page:

```
🔐 AIT asks for permission
- Run a command `curl -X POST http://localhost:5000/api/orders` — …
1. Allow once — for this action only
2. Always allow — saved on this machine for this project
3. Deny
4. Always allow the pattern `curl -X *` — only when the command is safe to pattern
```

Reply `1`/`once`/`allow once`, `2`/`always`, or `3`/`deny` (Indonesian words work too). Any other reply counts as a
denial and reaches the planner as an instruction. **Always** permissions are kept per machine + project + target and
can be revoked in Project > Detail → **AIT permissions**. A push to `main`/`master`/`production`/`release/*` is
always asked (no always option).

**Pattern permissions.** For a command that is safe to pattern, the card offers option **4. Always allow the pattern**,
e.g. `npm install *` (reply `4`/`pattern`/`pola`). A pattern allows single commands starting with the same words — no
pipe, shell operator, `$()` or backtick; a combined command still needs an exact permission. Commands that delete or
move files, wrap another command (`sudo`, `bash -c`, `xargs`, …), publish (`push`, `publish`, `deploy`) or change the
git repository are never offered a pattern. Patterns can also be added by hand in the **AIT permissions** card (*Add a
command permission*).

**Read-only commands** run without asking: `curl` GET/HEAD to localhost (also with `| jq`), `git status/log/diff/show/…`,
`ls`/`cat`/`head`/`grep` (without `-r`), and version checks (`dotnet --info`, `node -v`, `npm ls`). A command
naming a credential file is always refused, and the output is masked like a file's content.

**Commit & push** only when you ask for it (e.g. *"change the greeting, then commit and push"*, or *"commit and push
this change"* after a few threads). AIT commits only the files the thread wrote after the verification passed (build,
unit tests, testing), with a message in the repository's style, then pushes to the branch's upstream (or `origin`)
without `--force`. When the verification failed, commit/push is not offered.

**Branch & pull request.** Ask to *"create branch `feature/x`"* or *"open a PR"* along with the commit/push. AIT creates
a new branch from the current one (`git switch -c`; an existing branch is never reused and uncommitted changes move
along), commits, pushes with `--set-upstream`, then opens the PR with `gh` (GitHub) or `glab` (GitLab) when installed.
When the CLI is missing or fails, the answer carries the compare/MR link (GitHub, GitLab, Bitbucket, Azure DevOps) to
open yourself. The default base is the remote's default branch. The *Create a branch* and *Open a pull request*
permissions can be *always*.

**Reading the web (`web_fetch`).** The planner can read library/API docs, web pages, or an endpoint's response over HTTP
GET. HTML becomes Markdown text, JSON is pretty-printed, binary files are refused, and the output is capped at about
24,000 characters and masked. localhost runs at once; another host asks for the host permission (`host:port`, the same
permission as `curl`), and a redirect to another host is checked again. Turn it off in Settings → *The planner may read
web pages & APIs*.

**Background processes (`start_process`).** For an investigation that needs a running server (e.g. *"run `npm run dev`
and check the response of `/api/users`"*), the planner can start a process in the background, wait until a localhost
URL answers or some text shows up, then read its output (`process_output`) and stop it (`stop_process`). It always asks
for the command permission (or a pattern), runs at most 3 processes per thread, and all of them are stopped
automatically once the plan is submitted, when the thread completes/fails/is cancelled, or after 30 minutes. It follows
the *The planner may run commands* setting.

## Follow-up conversation

Every turn, AIT sends the planner up to the last 12 messages of the conversation (cut when long) and the project's
memory index. So "now add it to the detail page too" is understood without repeating the context.

The planner also sees the files already changed by earlier threads in this chat, per module. *"Test this change"* or
*"review the earlier one"* refers to those changes, not to changes from another CLI session in the same folder. When
this chat has already changed several modules, the planner asks which module (or all) — see
[Referring to earlier changes](07-cara-kerja.md#referring-to-earlier-changes).

Now and then the planner asks back, for example when a request can be read two ways with very different results. AIT's
reply is then a numbered question (thread status *Waiting for an answer*); type the number of your choice or your
answer as the next prompt, and the same thread continues. See
[Questions from the planner](07-cara-kerja.md#questions-from-the-planner).
