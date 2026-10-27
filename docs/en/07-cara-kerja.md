# 7. How it works

This document explains what happens behind one prompt, so you can judge the results and tune the configuration the
right way.

## 1. Request classification

A coding CLI sends many requests, not only your prompts, to one of the proxy endpoints: `POST /v1/messages` (Claude
Code, oh-my-pi, opencode), `/v1/chat/completions` (Qwen Code, etc.), `/v1/responses` (Codex), or `/v1beta/models/…`
(Gemini CLI) — see [CLI integration](03-integrasi-cli.md). All of them are first turned into one internal shape
(`ProtocolAdapter`), so the rules below are the same for every format. AIT **orchestrates** a request only when every
condition holds:

- *Orchestration on* is on in Setting;
- the request carries a `tools` list (the mark of a main agent request);
- the last message comes from the user, **not** a `tool_result`;
- the prompt text is not empty after `<system-reminder>` blocks are removed;
- the prompt is not a Claude Code side request: `<title-request>` (naming the session) and `<recap>` (a summary when the
  developer comes back) are sent by Claude Code with `tools`, but they are not tasks for the planner;
- **the session comes from a project**, i.e. a folder with `.git` or a build file (see section 2).

Text attached as a text-sourced `document` block (a paste or an attached text file) becomes part of the prompt, wrapped
in `<document title="…">`; binary documents (e.g. PDF) are not read.

Otherwise (session titles, session summaries, `/compact`, quota checks, sessions outside a project, etc.) the request is
forwarded as it is to the same 9router endpoint with the **passthrough model** — AIT creates no thread and calls no
planner. `POST /v1/messages/count_tokens` is forwarded too; when that fails, the token count is estimated (±4 characters
per token).

When the *Proxy API key* is filled in, a request without a matching key (`Authorization: Bearer` or `x-api-key`) is
refused with `401`.

## 2. Session check: project or not

Before the planner is called, AIT checks the **session** the prompt comes from:

1. The session id is taken from the `X-AIThread-Session` / `x-claude-code-session-id` / `x-session-id` header, or from
   `metadata.user_id`; the working directory from the `X-AIThread-Cwd` header, or from a `Primary working directory:` /
   `Working directory:` / `Current working directory:` / `cwd:` line (or a `<cwd>` tag) in the system prompt.
2. **Project = the root folder** holding `.git` or a build file, whatever its language: `.sln`/`.slnx`/`.csproj`,
   `pom.xml`, `build.gradle(.kts)`, `package.json`, `pyproject.toml`/`requirements.txt`, `go.mod`, `Cargo.toml`,
   `composer.json`, `Gemfile`, `pubspec.yaml`, `CMakeLists.txt`, and the like. Only the working directory and **its
   parent folders** (up to 6 levels) are looked at, never its sub-folders. The order of choice:
   1. a registered project that contains that folder (the one with the deepest root), so a project registered by hand
      or by an old version is still recognized;
   2. the nearest repository root (a `.git` folder);
   3. without `.git`: the outermost aggregating file (`.sln`/`.slnx`, `settings.gradle`, a `pom.xml` with `<modules>`, a
      `package.json` with `workspaces`, `pnpm-workspace.yaml`, `go.work`, etc.);
   4. otherwise: the nearest manifest.

   The `X-AIThread-Project` header (a root folder) becomes the starting point of the search instead of the working
   directory (when it gives no project, the working directory is used); the old `X-AIThread-Solution` header (a `.sln`
   path, its folder is used) is still accepted. Drives, the Home folder and system folders never become a project, even
   when a stray `package.json` or `.git` is there.
3. **A project is found** → it is registered automatically when it does not exist yet (the session's first prompt), then
   the prompt is orchestrated. **The workspace root** = the project's root folder, so a session opened in
   `repo/services/api` still works on `repo`; the planner is told the session was opened in `services/api`.
4. **No marker** (or the working directory does not exist/is unknown) → the session is **ignored**: each of its prompts
   is forwarded straight to 9router with the passthrough model, so the CLI keeps working as a plain agent. No thread,
   project or memory is created, for example for an empty folder where you only ask for a text file. A parent folder
   that only holds several projects (e.g. `a`, `b`, `c`) is ignored too because it is ambiguous, and the log names the
   projects inside it so you open the session in one of them.

The stack is decided from the manifests found (depth 3): `.NET`, `Java (Maven)`, `JVM (Gradle)`, `Node.js`, `Python`,
`Go`, `Rust`, `PHP`, `Ruby`, and others; one project may be mixed. The stack is used for the badge in the Project menu,
the planner's brief, and the list of hidden build folders (e.g. `target/` for Maven, `dist/` for Node.js).

This decision is **remembered per session** for 30 minutes — the next prompts in the same session do not scan the disk
again. A session connected to a project is extended every time it is used; an ignored session is checked again after
about 5 seconds (useful when you just added `.git` or a build file). The dashboard log records one line per decision:
`Session xxxxxxxx connected to project …` or `Session xxxxxxxx (…) is not part of a project (the reason) — the prompt
skips orchestration…`.

When the check fails (e.g. the database is down), the prompt is also forwarded without orchestration and the error is
logged.

## 3. Planner

The planner gets a **brief**, built from the most stable parts to the most specific ones (so the provider's prompt cache
can be reused between steps and between threads, and the request comes after all the material it refers to):

1. project info: name, root, stack, the solution/build files found, and the build command AIT can run (see
   [Verification](#5-verification-build-and-reviewer));
2. the folder structure 2 levels deep (at most 200 entries), then the folder the session was opened in (when it is below
   the root). The session folder is deliberately below the structure: it differs per session, so if it were above, the
   structure would not be cached across sessions;
3. the context the CLI inserts (in Claude Code: the content of `CLAUDE.md`, reminders, attached files), at most 30,000
   characters;
4. the **session context** from the Context Keeper when this chat already has earlier AIT threads (see
   [Session context](#session-context-context-keeper)), at most 12,000 characters;
5. the earlier conversation: without a session context, the last 12 messages; with a session context, only the messages
   after the thread whose summary is used. Each message at most 1,500 characters, at most 12,000 characters in total.
   Then the **investigation notes** of the last AIT thread in this chat (see
   [Session context](#session-context-context-keeper)), at most 8,000 characters;
6. the **code map** of the files the request names (module · layer), the **memory map** per module, and the **memory
   index** (id + label, at most 25) from the label search: the memories of those files' modules first, then sorted by
   keyword. When the request names no file (a follow-up like "apply it to the others too"), its label is taken from the
   session: the files changed and then the files read by the planner of the last AIT thread in this chat (or the ones
   its prompt named), otherwise from the latest conversation and then from the session context; the prompt of that last
   thread is used as keywords too;
7. the **outline** of those files (at most 6 files, 12,000 characters): classes, members and functions with their
   `start-line-end-line` range and signature, read straight from disk (the same as `code_outline`). The planner thus
   knows a file's skeleton before reading it, and only needs to read the line ranges it needs. Files whose module is not
   mapped yet are included too;
8. **the changes in this session**: the files changed by earlier AIT threads in this chat (the last 10 threads that
   really changed files; threads that only answered/read and rolled-back threads are left out), grouped per module (files
   without a module in the code map are grouped per thread), newest first, with the unit tests that cover them — see
   [Referring to earlier changes](#referring-to-earlier-changes);
9. the **unit tests** from the project's [unit test catalog](#unit-tests) (when active): suites, the rule for new tests,
   and the tests related to the request's files/modules with their last results (at most 30);
10. the developer's latest request (without `<system-reminder>`), at most 40,000 characters.

When a part exceeds its limit, its middle is dropped; the start and the end of the text stay.

Then the planner works in tool rounds (at most *Planner steps*). Every tool is **read-only**, except `run_build`, which
runs the project's build (it does not change code; only offered when *Automatic build & verification* is on and the
project has a build command) and `run_tests`, which runs the catalog's unit tests (also without changing code). Relative
paths are counted from the project root; the read tools (`list_directory`, `find_files`, `search_text`, `find_symbol`,
`find_references`, `code_outline` for a file, `read_file`) also accept **absolute paths outside the root**, so the
planner can check another folder or project when you ask for it (e.g. "check `D:/project/otherproject`"). Outside the
root, results are shown with absolute paths:

| Tool | Limits |
|---|---|
| `list_directory(path, depth)` | depth 1–4, at most 300 entries |
| `find_files(pattern, path?)` | a glob, e.g. `**/*User*.cs`; at most 200 results; `path` = the search folder (default the root; outside the root, the pattern is relative to that folder) |
| `search_text(query, glob?, regex?, path?)` | case-insensitive; files ≤ 1 MB; at most 80 results; `path` as in `find_files` |
| `find_symbol(name, kind?, path?)` | declaration locations (class, method, property, field, enum, function, …) as `file:line: kind signature (in Type)`; `name` may be `Type.Member`; C# through Roslyn (syntax, no build), JS/TS/Python/Java through regex patterns (lower accuracy); at most 60 results |
| `find_references(name, path?)` | every use of an identifier, comments ignored (C#: string literals too), the declaration line marked `[declaration]`; matched by name only; at most 120 results |
| `code_outline(path?, name?)` | the code skeleton without bodies, from [Code structure](05-menu.md#project): a **file** → every class/member/function with `start-line-end-line` and signature, read from disk at that moment; a function whose calls are already recorded gets `[calls: N]` (`, changed since` when its body changed since it was read). A **folder** (`.` = the whole project) → one line per file: classes (member count) and functions. **`name` only** → that class across the whole project, including every file of a `partial` class (more than 6 files → only their locations). C#, PHP, JS/TS, Python, Java, Kotlin; files ≤ 2 MB; at most 24,000 characters, a folder at most 300 files |
| `function_calls(path, name)` | the calls of one function/method (`name` or `Type.Member`) already recorded by [Function calls](#function-calls): line, kind, target and the location of its declaration, plus its recorded **callers** (at most 40). Not recorded yet or its body changed → queued for the Tools Node. Only files inside the project; at most 5 overloads |
| `read_file(path, startLine?, endLine?)` | numbered lines; at most 600 lines per call; files ≤ 2 MB |
| `search_memory(query)` | at most 25 labels |
| `read_memory(ids)` | the full description |
| `read_thread(id)` | the full record of an earlier thread in this project (request, plan, tasks, reply) |
| `git_status(path?)` | the current branch (ahead/behind upstream) and every staged, modified and untracked file; at most 300 entries |
| `git_diff(path?, base?, staged?, stat?)` | a unified diff. Default: everything not committed yet (staged + unstaged) against `HEAD`; `staged` = the index only; `base` = a commit/branch/tag or a range (e.g. `main...HEAD`: this branch's changes since it split from `main`); `stat` = the file list + line counts only. Untracked files are not in the diff (see `git_status`) |
| `git_log(path?, revision?, limit?, query?, change?, files?)` | newest commits first: hash, date, author, subject; default 20, at most 100. `path` = the history of a file/folder (renames followed), `query` = text in the commit message, `change` = commits that added/removed that text in the code (`git log -S`), `files` = the file list per commit |
| `git_show(commit, path?, stat?)` | one commit: author, date, full message, file list and diff; `stat` = without the diff |
| `git_blame(path, startLine?, endLine?)` | the last commit, author and date per line (uncommitted lines: `Not Committed Yet`); at most 200 lines per call, files ≤ 2 MB |
| `run_build(target?)` | builds the project, the result is a status + every error `file(line,column): code: message` (at most 40). `target` = another solution/project/manifest inside the root. Used when you ask for a build or to fix build errors |
| `run_tests(tests?, file?, all?)` | only when the project has an active [unit test catalog](#unit-tests): runs catalog tests (a number `UT-0012`, one test file, or all) and returns the result per test (passed, failed + message, skipped + reason). Used when the request really is about tests |
| `investigate(question, agent?)` | only present when the planner node [delegates tools to a tools node](02-konfigurasi.md#planner-tools-node); the tools node runs the delegated tools (the ones missing from this list) in its own loop, at most 12 steps, then returns short findings (usually under 1,500 characters, cut at 6,000). In the progress: `investigate  #1 "…"`, then a `#1 › read_file …` line per tools node tool, and `#1 › complete  n steps · 8.4s (model 6.1s, report 2.0s, queued 1.2s) · tokens · model` (aligned with the tool column; when it fails `#1 › failed  …`). The first time = the whole investigation, *model* = the total time the tools node took to answer, *report* = the part of *model* for the last turn that writes the findings (it has no tool line of its own), *queued* (only shown when present) = waiting for a *parallel* slot of the tools node. The `[↑ prompt ↓ completion cache% time]` badge on the first tool line of each turn (planner, tools node, tester) shows the tokens and how long the model took to answer that turn. The planner's closing line `done in 45.2s (model 30.1s · tools 12.0s · queued 2.0s)` separates the planner model's time, the tool time (including investigate; a turn that calls several investigates at once waits for the slowest) and the wait for a planner slot. In the CLI progress, long paths are shortened to `first-folder/…/folder/file` (at most 48 characters) and search queries are cut at 60 characters so one tool stays one line; the activity log keeps the full version |
| `ask_user(question, options?)` | stops the thread with one question to the developer (see [Questions from the planner](#questions-from-the-planner)); only offered when *Planner may ask* is on and the prompt comes from a session that can answer |
| `submit_plan(plan)` | ends the planning |

The folders `.git .vs .idea .nuget bin obj node_modules packages TestResults artifacts` and `wwwroot/lib/` are hidden from
every tool. The content of `.git` is never read directly; the `git_*` tools run `git` itself.

**Git tools** are only offered when the project is inside a git repository (a `.git` in the root or a parent folder),
and they need `git` on `PATH`. All five only read: their arguments are fixed (no shell), commit/branch names that start
with `-` or contain spaces are refused, paths always come after `--`, and git runs without optional locks
(`GIT_OPTIONAL_LOCKS=0`, so `git_status` does not rewrite the index), pager, colors, external diff, textconv or signature
checks, with a 30-second time limit. The planner uses them when the request mentions uncommitted changes, commits or
branches ("review my changes", "continue what I was doing"). When the project is in a sub-folder of the repository, the
results are limited to the project folder and their paths are relative to the project root. Commit, checkout, merge and
push are still up to you. Git output is at most 24,000 characters; when it is cut, the planner is advised to narrow it
(`path`, `stat`).

Several tools in one round run in parallel. The tool list does not change during a thread (changing it would invalidate
the prompt cache). At the last step the planner is asked to call `submit_plan`; only when it ignores that, one extra step
runs with `submit_plan` as the only tool.

### Questions from the planner

Like coding agents in general, the planner can ask, but **rarely**. It only asks when:

- the request has two or more reasonable readings that lead to really different changes;
- the plan would delete data, change/drop a schema, remove features, or go far beyond the request, while you did not
  clearly ask for that; or
- it needs a fact that is not in the code, the memory or the conversation (a business rule, a value, an external system).

A clear request is never confirmed again; things that can be guessed from the code (names, style) are decided by the
planner and the assumption is named in the summary. The planner investigates the code first so its question is specific.

1. The planner calls `ask_user` with a question and 2–4 options (its recommendation first). The thread stops with the
   status **Waiting for an answer**, and the reply is a numbered question:

   ```
   ❓ Question from the planner

   Is the Description column required or optional?

   1. Optional — NULL in the database, no validation
   2. Required — NOT NULL, validation in the service and the form

   Reply with the number of your choice or write your own answer.
   ```

2. **The next message in the same session is the answer**: a number (`2`) or free text. The same thread continues; the
   planner carries on its conversation from the point where it asked (it does not read the code again), then the plan,
   execution and wrapper run as usual. The wrapper and the reviewer see the original request with its clarification.
3. An answer that turns out to be a new request is accepted as it is too; the planner adjusts its plan.

Limits:

- At most 2 questions per thread, one at a time, and not at the planner's last step.
- Questions are kept in the process memory: when AIT restarts, or the question is not answered within **2 hours**, the
  thread is closed as *Cancelled* and the next message runs as a new thread (the chat history with the question is still
  read by the planner). The *Cancel* button in the thread detail also closes a thread waiting for an answer.
- The session is recognized by the client's session id (Claude Code, Codex, Telegram, …); clients that send no session
  id, and prompts from the Dashboard, are never asked. A prompt in another project in the same session closes the pending
  question.
- Turn it off with Setting → *Planner may ask* when you want the planner to always decide by itself.

### Developer permission

Three things make the planner stop and ask permission, through the same mechanism as a question (status **Awaiting
input**, the next message of the session is the answer, or the buttons on the thread page for a Dashboard thread):

- **`run_command`** — `CommandPolicy` splits the command without a shell. A pipeline whose every part is on the read-only
  allowlist runs at once (through Git Bash, or `cmd.exe` when there is none). Shell operators, variable expansion,
  commands off the allowlist, or a `curl` that sends data/writes files need the command allowed; a `curl` to an outside
  host needs the host allowed. Allowed → the command runs and its output becomes the tool result; denied → the planner
  is told.
- **Tasks outside the project root** — the validator accepts absolute paths outside the root (except system/program
  folders and credential files) and records their folders in the plan's `outsideFolders`. A folder without a saved
  permission holds the plan; allowed → the plan is used as it is (no new model call), `WorkspaceScope.AllowWrites`
  opens the folder to the executors, and the journal records the absolute path so a rollback still works. Denied → the
  planner plans again.
- **Commit/push** — the plan's `publish` section (only when the developer asks). After verification, unit tests and
  testing pass, `GitPublisher` groups the thread's files per repository; without a saved permission, the answer ends
  with the permission card, and a `1`/`2`/`3` reply in the same session runs it.

*Always* permissions are kept in the `ApprovalGrants` table (the local database, not shared with the team). A permission
pause does not count toward the planner's question limit and does not depend on *Planner may ask*.

### Referring to earlier changes

Requests like *"test this change"*, *"review the earlier one"* or *"undo that one"* refer to an earlier change without
naming which. The planner reads them as the files in the *changes in this session* part — **not the whole `git diff`**,
because another CLI session (another tab) may be changing the same folder. The session is decided from the
`**AIT** · thread …` anchors in the chat history, so two tabs = two sessions that never mix.

- One module, or the request names a module/file/feature → used right away.
- More than one module → the planner [asks](#questions-from-the-planner) first: one option per module (at most the 3
  latest, with their threads and file counts) plus *all*. You may also answer another module or several numbers at once.
- When it cannot ask (Dashboard, Telegram, or *Planner may ask* is off) → the latest module is used and that assumption is
  named in the summary.

### Shape of a plan

```json
{
  "summary": "Adds a Description field to the User form …",
  "answer": null,
  "contract": [
    { "name": "UI input id", "value": "txtDescription" },
    { "name": "DTO property", "value": "Description" },
    { "name": "DB column", "value": "Description NVARCHAR(500) NULL", "note": "table Users" }
  ],
  "tasks": [
    {
      "id": "T1",
      "title": "Add description textarea to the user form",
      "file": "src/Web/Views/User/Index.cshtml",
      "action": "edit",
      "instruction": "After the txtName form group add …",
      "focus": [{ "startLine": 40, "endLine": 60 }],
      "context": [],
      "dependsOn": []
    },
    {
      "id": "T2",
      "title": "Send description in saveUser",
      "file": "src/Web/wwwroot/js/user.js",
      "action": "edit",
      "instruction": "In saveUser add description: $('#txtDescription').val() …",
      "context": [{ "file": "src/Web/Views/User/Index.cshtml", "startLine": 1, "endLine": 12, "note": "form markup" }]
    }
  ]
}
```

- The **contract** is the key to accuracy: every cross-file name is fixed once, so tasks can run in parallel without
  seeing each other.
- `focus` marks the area of a large target file; `context` attaches excerpts of other files (read-only) for the executor.
- A request without file changes → empty `tasks` and the answer in `answer`; no executor runs. When the developer only
  asks for testing, empty `tasks` come with `tests.run` and/or `test` (see [Threads that only test](#threads-that-only-test)).
- With an active unit test catalog, the `tests` field holds the decision per registered test and the new tests (see
  [Planner decisions](#planner-decisions)).

### Plan validation

A plan is refused and sent back to the planner (costing one step) when:

- there are more than 40 tasks, or `summary` is empty while there are tasks;
- task ids are duplicated, `instruction` is empty (except for `delete`/`move`/`extract`), `action` is not
  `edit`/`create`/`delete`/`move`/`extract`;
- a path (including the `to` target) is outside the root (changes are only allowed inside the root; reading outside it
  is allowed), points to a folder, or is in a hidden folder;
- `edit` for a file that does not exist, `create` for a file that already exists, or two tasks create/move a file to the
  same path;
- `delete`/`move` for a file that does not exist; `move` without `to`, to a file that already exists, or only changing
  upper/lower case;
- `extract` without `members`, without `to`, not a `.cs` file, or to a file that a `create` task also creates (several
  `extract`s may fill the same new file);
- a file that is deleted or moved is also touched by another task. A file produced by a `move` or a new file produced by
  an `extract` may be `edit`ed as long as that task depends (`dependsOn`) on its `move`/`extract` task;
- `dependsOn` points to a task that does not exist, to itself, or forms a cycle;
- the `tests` part is invalid (see [Planner decisions](#planner-decisions)).

Paths are normalized (`\` → `/`, relative to the root). Rejections are recorded in the thread's activity log.

## 4. Parallel execution

- Tasks without dependencies run right away; other tasks start as soon as all their `dependsOn` tasks **succeeded**. When
  one of them fails, the waiting tasks are **skipped**.
- Two limits apply at the same time: *Max parallel* (per thread) and each node's *Max parallel* (across all threads).
- The executor is chosen: most free slots → lowest priority → in turn. When that executor still fails (429/408/5xx) after
  9router's retries, the task continues on its [fallback node](02-konfigurasi.md#fallback-node) (when one is chosen).
- Before a file is written for the first time, its original content is recorded so the thread can be undone
  ([rollback](04-penggunaan.md#undoing-file-changes-rollback)). After every write, the hash of the written content is
  recorded too.
- Execution time = the longest chain of tasks. The Dashboard compares it with the total working time of the executors
  (speedup).

### Deleting, moving files, and moving C# members

Besides `edit` and `create`, a task can be:

- `delete` — deletes one file;
- `move` — moves/renames one file to the path `to`. Its content does not change (byte for byte);
- `extract` — moves C# members (`members`: methods, properties, fields, events, nested types) from a `.cs` file to another
  `.cs` file (`to`). See [Moving C# members](#moving-c-members-extract).

These three actions are run **directly by AIT without a model** (no tokens, no executor node; in the thread detail the
node column says *no model*), under the same file lock as the executors. A folder that becomes empty after a file is
taken from it is deleted too (never the project root). When the content of a moved file also needs to change
(namespace, imports, class name), the planner adds an `edit` task on the new path that depends on the `move` task;
references to that file in other files are changed through their own `edit` tasks. Like executor writes, the original
file content is recorded first, so deletes and moves are reverted too on
[rollback](04-penggunaan.md#undoing-file-changes-rollback).

#### Moving C# members (`extract`)

To split a large class into several `partial` files (or move methods to another file), the planner uses `extract`, not
`create` + `edit`. An executor cannot be relied on to copy or delete thousands of lines: it only sees excerpts of a large
file and its output is limited by *max tokens*, so the move stops halfway and the build fails on duplicate members.

AIT does `extract` with a C# parser (Roslyn), in milliseconds:

- Members are copied **as they are** (body, documentation comments, indentation). Every overload with the same name moves
  along; a field with several variables in one declaration must be moved together.
- The source class (and its enclosing class when nested) is made `partial` when it is not yet.
- A new target file gets the same `using`s, namespace (block or *file-scoped* style, following the source), and class
  header without attributes, base class/interfaces and constraints (they belong in the main part).
- An existing target file must hold a `partial` part of the same class; members are added at the end of the class. A
  member that already exists in the target with the same signature is only removed from the source (not duplicated).
- `#region`/`#if` around members stay in the source file so they stay balanced. Constructors and operators cannot be
  moved.
- Several `extract`s from the same source file run in parallel; the file lock makes each work on the latest content.

### What the executor sees

1. The executor role prompt (+ the node's extra instructions, + the node's skill content; see
   [Skills on a node](#skills-on-a-node)).
2. The part of the plan that is the same for every task: the contract and the plan summary (as context only). This part
   is deliberately at the front so the executors of one plan share the same prompt start in the provider's prompt cache.
3. The title, action and path of the target file; the instruction.
4. Reference excerpts from `context` (at most 250 lines per excerpt, at most 40,000 characters in total).
5. The content of the target file:
   - a file ≤ `ExecutorFullFileMaxLines` (400) lines → whole;
   - a larger file → excerpts around `focus` ±40 lines with a `... (lines X-Y not shown) ...` marker; without `focus` →
     the first 400 lines;
   - a new file → a note that the file does not exist yet.

### Executor answer format

```
<<<<<<< SEARCH
        <input type="text" id="txtName" class="form-control" />
    </div>
=======
        <input type="text" id="txtName" class="form-control" />
    </div>
    <div class="mb-3">
        <label for="txtDescription">Description</label>
        <textarea id="txtDescription" class="form-control" rows="3"></textarea>
    </div>
>>>>>>> REPLACE
SUMMARY: Added the txtDescription textarea after the name field.
```

A block with an empty SEARCH means creating a new file (the full content in the REPLACE part).

### Applying an edit

1. The target file is **read again inside a per-file lock** — another task on the same file may already have changed it.
2. Each block is matched step by step: the exact text → line by line ignoring trailing spaces → ignoring indentation (the
   replacement's indentation is adjusted). A match in more than one place is **refused**, not guessed.
3. **All-or-nothing**: the file is only written when every block succeeds.
4. The BOM and line ending style (CRLF/LF) of the original file are kept; a new file uses the system line ending.
5. When it fails (no valid block, SEARCH not found, output cut off), the executor gets the error message with the current
   file content and tries again up to *Executor retries* times.

The diff shown in the thread detail is built from those blocks.

## 5. Verification: build and reviewer

After execution, when at least one task succeeded, AIT checks the result (thread status *Verifying*). This stage runs
when there is one of:

- a **build command**: *Automatic build & verification* is on in Setting and the project has a build command;
- an active **Reviewer node** (the one with the lowest priority is used);
- **unit tests**: the project has an active [unit test catalog](#unit-tests) and a recognized suite.

The build command is chosen from the project's manifests, **not by a model** (a model may only choose another manifest
inside the root through `run_build`):

| Stack | Command | Default target |
|---|---|---|
| .NET | `dotnet build <file> --no-incremental -v:q -p:GenerateFullPaths=true -p:CopyRetryCount=1 -p:NuGetAudit=false` | the shallowest `.sln`/`.slnx`, otherwise the shallowest `.csproj`/`.fsproj`/`.vbproj` |
| .NET Framework (old-format/non-SDK csproj, e.g. ASP.NET MVC 5) | `MSBuild.exe <file> -restore -t:Rebuild -m -nr:false -p:RestorePackagesConfig=true …` from Visual Studio / Build Tools (found through `vswhere`, then the standard `Program Files\Microsoft Visual Studio` folders) | the same as .NET; a solution counts as old-format when one of its projects is old-format or it holds a web site project |
| Node.js | `tsc --noEmit` (a local TypeScript in `node_modules`), otherwise `npm run build` | the shallowest `package.json` that has one of them |
| Go | `go build ./...` | `go.mod` |
| Rust | `cargo check --message-format short` | `Cargo.toml` |
| Java (Maven) | `mvnw`/`mvn -q -B compile` | `pom.xml` |

Other stacks (Python, PHP, Gradle, …) have no build command; they are only verified by the Reviewer when there is one.

A .NET build is always a **full rebuild** (clean + build), so old output does not hide errors. The exception: when a
`.dll`/`.exe` in the project's `bin` folder is in use by a running program (the app itself, IIS without shadow copy, the
compiler server in `bin\roslyn`), the build runs **incrementally without clean** (`dotnet build` / `msbuild -t:Build`).
With a clean, every file around the locked file (e.g. `*.runtimeconfig.json`, `*.deps.json`) would be deleted and not
copied again because the copy fails, so the app could not run again without another build. A copy to `bin` that fails
because the file is locked (`MSB3021`/`MSB3027`) still counts as passed: the code was compiled. An old-format project
cannot be built with `dotnet build` because it imports targets owned by Visual Studio (e.g.
`Microsoft.WebApplication.targets`). When the Visual Studio MSBuild is not there, that project is not built at all (it
is not replaced by another stack's build such as an `npm run build` next to it). A build that fails **only** because of
a missing toolchain (`MSB4019` target import missing, `MSB4236` SDK missing, `NETSDK1045` target framework not
supported) is reported as "the build cannot run" (🟡): it is not a code problem, so no fix task is made.

One verification round:

1. **Build** (when there is a command). One build per project at a time (two builds fight over `obj/`). The build is
   stopped together with its child processes when it exceeds *Build timeout* or the thread is cancelled. Errors are read
   from the output (MSBuild/Roslyn, tsc, Maven formats, and `file:line:column` for Go/Rust); when no error is recognized,
   the last 120 lines of output are used.
2. **Reviewer** (when there is an active node). It gets the request, the plan, the contract, the instruction and diff of
   every task (at most 150 lines per task), and the build result, then answers in JSON: approved, or a list of problems
   per file — missed requirements, names that do not match between files/the contract, typos, missing `using`/imports,
   and the causes of build errors. Style, refactors and things outside the request are not reported. A reviewer that
   cannot be reached or answers with something other than JSON only makes the review skipped.
3. **Unit tests** (when the build passed or there is no build command): the relevant tests are run and compared with the
   results before the change; failures are diagnosed by the Tester node (when there is one) — see
   [After the build](#after-the-build).
4. **Fix tasks**. Problems are grouped per file into `V<round>-<n>` tasks (e.g. `V1-1`) that executors work on like
   regular tasks, with an instruction holding the build errors (line, column, code, message), the reviewer's findings,
   and the file's original task instruction as context. *Focus* is pointed at the start of the file (where the
   `using`s/imports are) and at the line of every error or line the reviewer quotes. Names quoted by an error message
   (e.g. `OracleEntities` in `CS7036 … 'OracleEntities.OracleEntities(string)'`) are looked up elsewhere in the same file:
   at most 6 lines that use them go into the *focus* and the instruction as a pattern to follow. Without this, the
   executor of a large file only sees ±40 lines around the error and does not know the way other methods do it. Then the
   next round starts.

   A name that is not found (`CS0103`, `CS0117`, `CS1061`) is fixed in **the file that should declare it**, not in the
   calling file, when exactly one other task (a C# file) names it in its instruction and that file does not hold the name
   at all yet. Its instruction names the caller's location, and the calling line (±5 lines) comes along as a reference
   excerpt so the signature matches. Without that, the executor of the calling file could only answer "the call already
   follows the contract" and the fix round would be wasted.

   Exception: a C# duplicate member error (`CS0111`, `CS0102`) between a file **filled** by this thread (a `create` task
   or an `extract` target) and a file that existed before means a member move is not finished. For that an `extract` task
   (without a model) is made from the old file to the new file, so the copy in the old file is the one deleted — not the
   copy in the new file. Other fix tasks on both files wait for this task, and reviewer findings for those two files are
   postponed to the next round (the reviewer checks again after the move).

Limits and rules:

- At most *Fix rounds* rounds (default 2). The value `0` = only verify and report. Unit test regressions have their own
  limit: 3 fix rounds (still off when this value is `0`).
- Build errors are only fixed automatically in **files this thread changed** (or, for a name that is not found, in the
  file that the plan says should declare it). Errors in other files (that may have existed before the thread), in
  generated folders, outside the root, or without a file (e.g. a NuGet package not found) are reported as they are. The
  reviewer may still point at another file when it is clearly broken by the change.
- `MSB3021`/`MSB3026`/`MSB3027` errors (an output file locked by a running application) count as **passed**: the code
  compiled.
- A build that timed out or cannot run (the toolchain is not installed) is not repeated in the next round.
- Verification stops when no fix task succeeded, or the remaining problems cannot be fixed automatically.

The result: **Passed**, **Issues remain**, or **Could not be determined** (the build cannot run and there is no review).
The thread is still *Completed*; remaining problems are recorded in the thread's error column and named by the wrapper.
The details of every round show in the *Verification* card of the thread detail.

## 6. Testing (Auto Testing)

When the Planner node in use has **Auto Testing** on (the Planner node modal) and there is an active **Tester** node, AIT
tries the change in the **running** application after verification (thread status *Testing*). The active Tester node
with the lowest priority is used.

### The testing plan from the planner

When Auto Testing is on, the planner's prompt gets an *Auto Testing* part and `submit_plan` requires a `test` field for a
plan that changes files. There the planner judges **what the change looks like from the outside**:

```json
"test": {
  "level": "ui",
  "baseUrl": "http://localhost:5000",
  "notes": "Menu Master → User.",
  "scenarios": [
    {
      "title": "Save a user with a description",
      "target": "/User",
      "steps": ["Click Add", "Fill #txtDescription with 'AIT test'", "Click Save"],
      "expect": "The new row in the table shows the description 'AIT test'"
    }
  ]
}
```

- `level`: `ui` when the change shows on a page (the tester controls a browser and may call endpoints too), `api` when it
  can only be reached through an HTTP endpoint, `none` when it cannot be tried from outside (a refactor, a build script,
  a library, a database script only) — the reason is in `notes`. For background jobs and external systems see below.

#### Background jobs and external systems

Code that only runs in the background (Hangfire/Quartz jobs, hosted services, queue consumers, scheduled syncs) or whose
effect only shows in an external system is **not tested by triggering its job**. When the project has a unit test catalog
(or a test project can be made for it, see [Planner decisions](#planner-decisions)), the planner adds a **dry-run unit
test**:

- the test calls the method the job runs **directly**, with the external API, database, queue and clock faked
  (mock/fake): nothing leaves the process, no data is really written;
- its input is exactly the changed case (a record from the external API, an empty field, an error response), then its
  result is asserted;
- when that method cannot be called without its job framework (static, creates its own `HttpClient`/`DbContext`), the
  planner adds a small refactor so a fake can be passed in;
- `level` = `none` (or still `ui`/`api` for the part that really shows in the application) and `notes` names the test.
  Its result is in the *🧪 Unit test* part, not a manual step for the developer.

That test joins the catalog, so it keeps guarding against regressions in later threads. Without a unit test catalog (mode
*Off* or *Create new unit tests* = *Never*) testing is still skipped with the planner's reason.
- `baseUrl`: found by the planner itself from `Properties/launchSettings.json`, `appsettings*.json` (Kestrel/`urls`),
  `package.json` scripts and dev server configuration (vite, next, angular), `.env` (`PORT`), or a `docker-compose` port.
  It must be an address of this machine: `localhost`, `*.localhost`, `127.0.0.1`, or `[::1]`.
- `scenarios`: 1–6 scenarios (the main path first, then the edge cases the change handles), each with `steps` that use
  names from the contract and an `expect` that can be seen (text, a value, a status code, a response field).
- The validator sends the plan back to the planner when `test` is missing, `level` is unknown, `baseUrl` is empty or not
  localhost, there is no scenario, or a scenario has no `expect`. Without an active Tester node the planner is not asked
  for this part, and a `test` part sent anyway is ignored. A plan without tasks is only tested when the developer really
  only asks for testing (see [Threads that only test](#threads-that-only-test)).

### How testing runs

1. **Skipped** when `level` = `none` (progress: *Testing · skipped* + the planner's reason), or when the last build still
   fails (the application cannot load the change yet). It does not run at all when no task succeeded (except for a
   thread that only tests).
2. **Application check**: before calling a model, AIT sends a `GET` to `baseUrl` (5 seconds). Any response (also a 404
   or a redirect to the login) means the application is running. Without a response, **AIT starts the application**
   (see [Running the application](#running-the-application)) and waits until `baseUrl` answers. When it still does not
   answer, every scenario is *could not be checked* without any token used, with the reason (the command is not
   recognized, the exit code and the last output, or the time limit).
3. The **Tester** works in its own tool loop (at most 40 steps). It sees the request, the plan summary, the contract, the
   diff of every task that succeeded (at most 80 lines per task), the [endpoints from the code](#endpoints-from-the-code),
   and the testing plan — no other code. Its tools:

| Tool | Level | Details |
|---|---|---|
| `http_request(method, url, headers?, body?)` | ui, api | `url` = a path relative to `baseUrl` or an absolute localhost URL. An object/array body is sent as JSON. Cookies from earlier responses are sent along (log in once, then call protected endpoints). An application with Windows Authentication (IIS/Negotiate) automatically uses the Windows account running AIT. Redirects are not followed (a 302 shows its `Location`). 30-second timeout; the body is read up to 512 KB, JSON is tidied. |
| `browser_open(url)` | ui | Opens a page in a **browser shown on the screen** (non-headless). Localhost only. |
| `browser_snapshot()` | ui | The page as a Playwright *accessibility snapshot*: elements with their `ref` (e.g. `[ref=e12]`), text, and field values. |
| `browser_click`, `browser_fill`, `browser_select` | ui | The element is chosen by a `ref` from the last snapshot, or a CSS/Playwright selector (`#txtDescription`, `text=Save`); `element` = the element name shown in the progress. `browser_fill` can press Enter afterwards. |
| `browser_press(key)`, `browser_wait(text?, gone?, seconds?)` | ui | Presses a key (`Enter`, `Escape`, `Control+A`, …); waits for text to appear/disappear or a few seconds (at most 15). |
| `report_test(summary, scenarios)` | — | Ends the testing with `passed` / `failed` / `blocked` per scenario with its explanation. |
| `ask_login(reason, loginUrl?)` | — | Only for threads from a chat (Claude Code/Codex/…, with a session id). Stops the testing to ask the developer for a login — see [Login](#login). |

   Every browser action is answered with the URL, the title, what happened since the previous action — console errors,
   uncaught JavaScript errors, page requests (`xhr`/`fetch`/document) that returned 4xx/5xx or failed, dialogs
   (`alert`/`confirm` are accepted automatically) — then a snapshot (at most 12,000 characters). Actions run slowly
   (250 ms per step) so they can be followed by eye, with a 10-second timeout; an element that is not found is reported to
   the tester with the latest snapshot. New tabs or popups are followed automatically. Several tools in one turn run in
   order.
4. **Progress** in the CLI: the phase `🧪 Testing · UI · http://localhost:5000 · model`, one line per tool (e.g.
   `browser_fill  Description ← "AIT test"`), and for `http_request` the status, duration, content type and the first
   8 lines of the response body below it.

### Endpoints from the code

Endpoint paths are not guessed from class names. When testing starts (after the change is applied, so new endpoints are
included), `Testing/AspNetRoutes` reads the ASP.NET route attributes (Core MVC/Web API and Web API 2) in every `.cs` file
of the project with Roslyn, without a build:

- `[controller]` = the class name without `Controller` (`OutletUnionApiController` → `OutletUnionApi`), `[action]` = the
  method name (`[ActionName]`, an `Async` suffix removed), `[area]` = `[Area]`, `{version:apiVersion}` = `[ApiVersion]`
  (`"1.0"` → `1`); `{controller}`/`{action}` parameters in a template are filled with the same names.
- A class `[Route]`/`[RoutePrefix]` is combined with `[HttpGet("…")]`, `[HttpPost]` + `[Route("…")]`, `[AcceptVerbs]` on
  the method; a method template starting with `/` or `~/` is not combined. A class without a route uses the route of its
  base class (also in another file). A controller with no route attribute at all is taken to use the default route
  `{controller}/{action}`.

The result is used in two places:

- **The tester's brief** gets an `# Endpoints in the code` part (at most 30): the routes in the files the thread changed
  and the routes the scenarios aim at. A scenario target that no route serves but that clearly resembles one route (the
  same segments, one or two written slightly differently, e.g. `OutletUnion` for `OutletUnionApi`) is replaced with that
  route, also in its steps, with a note of the path the planner wrote.
- **After a 404**, the `http_request` answer to the tester gets an AIT note: the most similar route in the code, or when
  that path does exist in the code, that the application probably has not loaded the change yet (it needs a
  restart/rebuild). Without a similar route, the tester is pointed to `/swagger/index.html`; a response of a Swagger UI
  page always gets the list of OpenAPI documents it names (`Testing/SwaggerDocuments`, e.g.
  `/swagger/OutboundService/swagger.json`).

The planner is given the same token rules when it writes a `target`.

### Running the application

When `baseUrl` does not answer, AIT starts the application itself for that testing (`Testing/AppLaunchResolver` +
`AppLauncher`). The command is fixed per stack and never comes from a model:

| Order | Condition | Command |
|---|---|---|
| 1 | `.aithread/project.json` has `"run"`, e.g. `{ "key": "eep", "run": "npm run dev:all" }` | that command, through `cmd /c` (Windows) or `sh -c`, in the project root |
| 2 | An ASP.NET Core project (`Microsoft.NET.Sdk.Web`): the one with a `Project` launch profile in `Properties/launchSettings.json` with the port of `baseUrl`, or the only one | `dotnet run --project <csproj> --launch-profile <profile> --urls <baseUrl origin>` in the project folder. A profile holding only that address comes first (`http` for `http://localhost:5211`, not `https` which also holds it); that profile's environment variables win over AIT's defaults. Without a matching profile: `--no-launch-profile` |
| 3 | Laravel (`artisan` in the root) | `php artisan serve --host=<host> --port=<port>` |
| 4 | A `package.json` (the shallowest) with a `dev`, `start` or `serve` script | `npm run <script>`; `node_modules` must exist already |

- Extra environment: `PORT`, `ASPNETCORE_URLS` (the `baseUrl` origin), `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT` =
  `Development`, `BROWSER=none`. A dev server that does not read `PORT` (e.g. vite) still uses the port of its
  configuration; the planner takes `baseUrl` from the same configuration.
- AIT waits until `baseUrl` answers (checked every second, at most 3 minutes, because `dotnet run` builds first). A
  process that stops before answering is reported with its exit code (Windows crash codes also in hex, e.g.
  `0xC0000005`); one that does not answer within 3 minutes is stopped. One sentence on the cause shows in the report,
  then the last 15 lines of the application's output in an **Application output** block (the final answer and the thread
  detail). Build warnings (`warning NU1903: …`, `warning CS…`, `npm warn …`) are left out, so the start error (e.g. a
  stack trace) is not hidden. When another application answers at that URL in the meantime (started by the developer or
  another thread), that application is used.
- Progress: `🚀 The application is not running, AIT starts it: …` then `🟢 The application answers at …`. The testing
  report (the final answer and the thread detail) says *started by AIT* with its command.
- After testing (also when cancelled) that process is stopped together with all its child processes. An application
  that was already running before testing is never stopped.
- Not supported automatically: old .NET Framework projects (IIS Express), several .NET web projects without a matching
  port, and other stacks (Python, Go, Java, Docker, …). Use `"run"` in `.aithread/project.json` or start it yourself. For
  changes that load right away, a `dotnet watch`/dev server you run yourself is still the fastest.

### Tester browser

- **Microsoft Edge** (built into Windows) is used, then **Google Chrome**, then Playwright's **Chromium** (downloaded once,
  ±150 MB, to `%LOCALAPPDATA%\ms-playwright` when neither exists). The Playwright driver is installed with AIT (the
  `.playwright` folder next to `AIThread.exe`), but without its `node.exe`, so the package does not grow by ±90 MB.
  Node.js for the driver is looked up when the tester browser is first opened: the `PLAYWRIGHT_NODEJS_PATH` env → the
  driver's own `node.exe` (dev builds) → `node` on `PATH` (version 20+) → an earlier download → downloading the official
  Node.js once (±36 MB, checked against the SHA-256 from nodejs.org) to
  `%LOCALAPPDATA%\AIThread\node\v<version>\node.exe`.
- Its profile is separate from your everyday browser, in `%LOCALAPPDATA%\AIThread\tester-browser\<browser>`. Persistent
  cookies are kept between testings, but do not rely on them for logins: session cookies are lost when the window
  closes. Use [Login](#login).
- Only one tester browser is open at a time; UI testing of other threads waits for its turn. The window closes 2 seconds
  after testing finishes. API testing does not open a browser.
- The browser is only visible when AIT runs in your Windows session (the `AIThread.exe` tray/menu), not as a service.

### Login

When the application asks for a login and the tester has no account, **the tester asks you in the chat**:

1. The tester calls `ask_login` (e.g. *`/User` redirects to the login page*). Testing stops there; scenarios not checked
   yet become *could not be checked — Login needed*. The thread still **completes** as usual (wrapper, changed files,
   footer), and the *Testing* part at the end of the answer closes with:

   ```
   🔐 Tester needs a login — /User redirects to the login page (`/Account/Login`)

   Reply to this message with `login <username> <password>` (a test account) to run the testing again with that account. …
   ```

2. You reply in the same chat, e.g. `login admin secret123`. Another accepted format: a short labeled reply such as
   `username: admin` + `password: secret123` (any order). Other messages count as regular prompts.
3. AIT runs **that thread's testing again** with that account — without the planner and without changing code — and
   shows its progress and the new *Testing* part as the reply. When the login is rejected, the tester may ask again.

Password handling:

- The tester (the model) **never sees the password**. It only gets the username and the `{{password}}` placeholder; AIT
  replaces it with the real value when `browser_fill` or `http_request` runs, then masks it again in everything that goes
  back to the model, in the progress, the log and the testing report.
- The password is **not written to disk** (database, log, report). It is kept in the AIT process memory for that chat
  session and that project for 8 hours, so the next threads in the same session log in right away without asking again.
  It is lost when AIT restarts.
- The chat history holding the `login …` reply is sent along with every later prompt; AIT masks that password before the
  history reaches the planner or another model.
- Your reply is still recorded in the CLI chat history (e.g. the Claude Code transcript). Use a **test account**, not a
  personal account.
- The question waits 2 hours. A thread from the Dashboard or Telegram cannot be asked; fill in the username & password in
  the **🧪 Test again** modal of the thread detail (not stored either).
- **Captcha**: in a development environment a captcha is usually not checked as long as it is filled in, so the tester
  fills a captcha field with any value (e.g. `1234`) and continues; tick widgets (reCAPTCHA, hCaptcha, Turnstile) are
  clicked once. When the application still refuses, the scenarios behind it are *could not be checked* with a note that
  the captcha is not bypassed in this environment — the tester does not ask anything in the chat for that.
- The old alternative still works: credentials written in the Tester node's *Extra instructions* are used by the tester
  (but they are stored as they are, sent to the model, and included in the setup profile export — avoid it for
  passwords).

### Test accounts (switching users)

A business flow rarely ends with one user: the requester submits, the level 1 manager approves, HR at level 2, and so
on. For that every project has **test accounts** (Project > Detail → *Test accounts*), shared with every developer of the
project through the project key and stored with long memory like the unit test catalog.

- Each account: a **username**, a **role** (*Requester*, *Manager*, *HR*, *Administrator*, …), **free attributes** (NIK,
  Unit, Approval level, Manager = the username of their manager, …), and notes. A username is unique per project,
  case-insensitive.
- **There is no password per account.** The project has one **Dev password**: empty = the default `Test@1234` (long
  enough, with an upper-case letter, a digit and a symbol, so it passes common browser validation). This value is not a
  secret and is visible to every developer of the project; for a real password keep using the `login <user> <password>`
  reply (memory only).
- The planner's and tester's briefs get a `# Test accounts` part (username · role · attributes, no password). The planner
  gives each scenario an **`actor`** field (a username or a role from that list); the validator rejects an unknown actor.
  A flow across several users is written as several scenarios in order, each with its actor.
- The tester tool **`switch_user(account)`** makes one account active:
  - **browser**: the open window is closed, and the next browser tool opens **that account's browser profile**
    (`%LOCALAPPDATA%\AIThread\tester-browser\accounts\<project>-<username>\<browser>`), separate per project because
    `localhost` cookies are not separated per port. That account's login lasts for the next runs;
  - **HTTP**: `http_request` uses that account's cookie jar (the sessions of other accounts stay intact in the same run);
  - **placeholders**: `{{username}}` = the active account's username, `{{password}}` = the project's Dev password. The
    model never sees their values, and the values are masked again in every output.
- Progress `👤 switch_user`, the report names `accounts pengaju01, atasan01` and the actor per scenario
  (`✅ **Approve** (atasan01) — …`). When the default dev password is used, the report adds a note and links to the *Test
  accounts* card.
- **Dev password rejected**: the tester calls `ask_login` with `kind: "dev_password"`. The final answer asks for the reply
  `dev password <value>` (or `password dev <value>`). That value is stored as the project's Dev password (source *chat*),
  then that thread's testing is repeated with the test accounts. The `login <user> <password>` reply can still be used
  instead (for that session only).

#### Login as / impersonate

Many internal applications have an admin *log in as another user* feature. Whether it exists, how to use it and which
roles may use it differ per project, so AIT **detects it per project**:

1. **A code scan without a model** (`ImpersonationScanner`): patterns such as `Impersonate`, `LoginAs`, `SwitchUser`,
   `RunAs`, `SignInAs`, *login sebagai*, with a few lines around them (where `[Authorize(Roles = …)]`/`IsInRole` usually
   is). Build/third-party folders and files protected by the secret policy are skipped.
2. **A cheap model** (Tools node → Wrapper → Planner, like `MemoryAi`) reads those findings and answers: available or not,
   *how* (a menu/URL/endpoint, with `{{target}}` for the target user), *how to return*, and the *roles* allowed. Without
   findings no model is called (result: *none*).
3. It runs **once** at the first test in a project with more than one test account (progress
   `🔎 Looking for a login as feature…`), or with the **Detect** button in the *Test accounts* card.
4. **The tester's observation**: when the tester sees a *login as* feature that is not recorded yet, it reports it in
   `report_test` (`impersonation`), and AIT records it (source *tester*), unless the developer already filled it in or
   confirmed it.
5. The result shows in the *Test accounts* card with its evidence (file:line), and can be **changed/confirmed** by the
   developer. An unconfirmed result is still used, and the tester's brief says so.

When `switch_user` goes to an account that does not hold that role, the tester is given a shortcut: log in in that
account's profile as an admin-role test account (the dev password), then use the login as *how* to enter as the target
account; when that fails, log out and log in normally. Roles are matched case-insensitively. SSO (Microsoft/Google) is out
of scope: scenarios that need it are marked *could not be checked* with the reason.

### Follow-up tests and values between steps

After testing, the Tester can **suggest follow-up tests** for a flow that is not finished: approve per level, edit or
cancel before approving, reject and submit again, the notifications that follow. Suggestions are not run automatically,
because they can create real data.

- `report_test` gets `follow_ups` (at most 5 per run): title, reason, `needs` (what the developer must prepare, e.g. *an
  account with the HR role*), and scenarios with actors. The final answer shows **🧭 Suggested follow-up tests**
  numbered, with their actors, needs and last run result.
- **Running**: reply `run follow-up test <number>` (also `follow-up test #1`, `run follow-up 3`, or the Indonesian
  `jalankan test lanjutan <number>`, `test lanjutan #1`, `lanjut test 2`) in the same chat. AIT looks up the latest thread
  of that session that has a suggestion with that number. Or use the **Run** button in the Testing card of the thread page
  (`POST /api/threads/{id}/follow-ups/{number}/run`).
- A follow-up test run = testing that thread again with the scenarios of the suggestion, without the planner and without
  changing code, using the project's test accounts. Its result is stored **on that suggestion** (the thread's main report
  stays), and new suggestions from that run are added with the next numbers (at most 20 per thread). So a flow can go on
  step by step: submit → approve level 1 → approve level 2.
- **Values between steps**: the tester tool `remember(name, value)` stores a value the application shows (e.g. the
  document number `REQ-0123` as `requestNo`). Later steps and follow-up tests write `{{requestNo}}`, and AIT fills in its
  value. Values are stored with the thread (at most 30, `username`/`password`/`target` are reserved), show in the Testing
  card, and are carried to *Test again* and to follow-up tests.
- A thread that only tests (without tasks) can now also have its testing repeated and its follow-up tests run.

### Multi-step scenarios (Excel)

When a follow-up test needs data only the developer knows (who the approver of each level is, the form values to test),
that suggestion becomes a **scenario** filled in through an Excel file. The model builds its structure (steps, an actor
per role, `fields` = form inputs whose values must be chosen), and **the Excel file is written by AIT** (ClosedXML), not
by the model, so its format can always be read back.

1. **Create**: reply `create scenario for follow-up test <number>` (or `buat skenario test lanjutan <number>`) in the
   chat. AIT writes `.aithread/testing/<name>.xlsx` in the project and names its path with what still has to be filled in.
   Or on the thread page: **Create Excel scenario** on the suggestion (the file downloads right away).
2. **Fill in** that file (sheet names follow the [display language](02-konfigurasi.md#display-language) and are read in
   both languages):
   - **Guide** (*Panduan*): how to fill it in and a warning about side effects;
   - **Accounts** (*Akun*): one row per role (*Requester*, *Manager*, …) with its username and the project's attribute
     columns. Yellow cells = need to be filled in. A username that does not exist yet is **added automatically** as a
     project test account when the file is read;
   - **Steps** (*Langkah*): one row per step: actor (a dropdown of the roles from the Accounts sheet), action (a dropdown
     of the action codes *submit/approve/reject/edit/revisi/batal/lihat/lainnya*), target, step, one column per form
     field (yellow cells), and the expected result. Rows may be added, deleted or reordered (at most 30 steps);
   - the hidden `_AIT` sheet holds the scenario's id and version. Do not rename the sheets.
3. **Run**: reply `run scenario <name>`. AIT reads the file `.aithread/testing/<name>.xlsx` first (the version goes up
   when its content changed), then runs the scenario in its origin thread. From the Web UI: Project > Detail → *Test
   scenarios* → **Upload Excel**, then **Run**.
4. **The first time per project** AIT asks to confirm the side effects: real submits and approvals create data and can
   send emails/notifications when the dev database holds a copy of production data. Reply `run scenario <name> agree`
   (or `jalankan skenario <name> setuju`, or confirm the dialog in the Web UI). The agreement is recorded with the
   developer's name and not asked again.

When it runs, **each step is one Tester session** (a limit of 40 tool steps per session), with the actor = that role's
username, the instruction + `Fill <field>: <value>` per form column, and the values remembered by earlier steps
(`{{requestNo}}`). Free text the tester types starts with `[AIT TEST]` so test data is easy to find and clean up. The
scenario **stops at the first step that does not pass**; the steps after it are marked *could not be checked — The
previous step did not pass*. The combined result (`n of m steps passed`) is stored on the scenario, on its origin
suggestion, and the remembered values go to the thread.

Scenarios are stored per project key in the long memory store (shared with the team, included in memory export/import),
so other developers can run them without filling them in again. A scenario imported from another machine points to a
thread on that machine; create it again from a thread on this machine to run it.

### Results

*Passed* (no failed scenario and at least one passed), *Failed* (a scenario failed), *Could not be checked* (no scenario
could be checked: the application is down, the browser did not open, a login is needed), or *Skipped*. A failed scenario
makes the status line *🟡 Partly completed* and is recorded in the thread's error column (`Testing: n scenario(s)
failed.`), but does **not** trigger fix tasks; the thread status stays *Completed*. The wrapper gets the result and
suggests the next step. The final answer holds a **🧪 Testing** part (the verdict per scenario, browser errors, and **API
output**) — see [Reading the output](04-penggunaan.md#reading-the-output-in-the-cli). The full details, including the
request and response bodies of every `http_request` (at most 30 requests), are in the *Testing* card of the thread detail.

- **Data**: the tester is asked to use existing data, to mark the data it creates (e.g. an `AIT test` prefix), to delete
  it when the application offers a way, and not to change other data. Point the tested application to a development
  database.
- **Test again**: thread detail → **🧪 Test again** (a *Completed* thread with a UI/API testing plan) runs the testing
  again from the stored plan — the code, the verification and the final answer do not change. Useful after the
  application was started or restarted. Its modal has optional username & password fields (see [Login](#login)). Its
  result replaces the Testing card.

## 7. Wrapper

The wrapper gets your request, the plan summary, the contract, the result of every task including fix tasks (summary,
error, at most 60 diff lines per task), the verification summary per round (including unit tests), and the testing
result, then writes the part of the answer that needs explaining, in the language of your request: 1–3 sentences on what
was done, advice for failed tasks or remaining verification problems, and follow-up steps when needed. The other facts
are written by AIT itself from the results, not by a model: the status line (tasks, build, reviewer, unit tests,
testing) and the *Needs attention* list before the wrapper's text, the *Changed files* table, the *Unit test* and
*Testing* parts, and the footer (duration, tokens, memory, wiki, the providers' quota left, the thread link) after it —
see [Reading the output in the CLI](04-penggunaan.md#reading-the-output-in-the-cli). The wrapper's text is streamed to
the CLI.

When there is no active wrapper or the wrapper fails, the CLI shows the plan summary instead of its text; what is stored
as the thread's answer (Dashboard, Telegram) is an automatic summary (✅/❌/⏭️ per task, plus the verification result).

After that memory extraction runs and is waited for at most 30 seconds so the footer can name the number of new
memories; past that the footer writes *new memory is still being processed* and the extraction still finishes in the
background.

## 8. After the thread

- The final status: *Completed* when a task succeeded (or a direct answer), *Failed* when none did. A thread that stopped
  at a planner question has the status *Waiting for an answer* until it is answered, cancelled or expires.
- Time (including verification, unit tests and testing; time waiting for an answer is not counted), tokens per role
  (planner with its cache share, executors including fix tasks and test restores, reviewer, tester including unit test
  triage, wrapper), and every task are stored in the database and shown in the Dashboard.
- Unit test results, new tests and obsolete decisions are recorded in the project catalog — see
  [When the thread finishes](#when-the-thread-finishes).
- Memory extraction is waited for before the answer's footer (at most 30 seconds, then it continues in the background) —
  see [Memory](06-memory.md).
- A *Completed* thread waits to be summarized into the project's business wiki by the wiki writer (periodically, by
  default every 6 hours) — see [Business memory](06-memory.md#business-memory-business-wiki). Images attached to the
  prompt are kept until then, so screenshots can make it into their module's page.
- For a CLI thread (also a failed one), the Context Keeper updates the session context in the background.
- When files were changed (also by a thread cancelled/failed halfway), the project's Code structure is rescanned and the
  calls of the changed functions are read (see [Function calls](#function-calls)), both in the background.
- In a git repository, the thread's change is followed from then on: committed, discarded, or on another branch — see
  [Git state of a thread's change](#git-state-of-a-threads-change).

## Git state of a thread's change

One project is touched by many developers on many branches. AIT records where a thread's change comes from and follows
where it moves in git. AIT only **reads** git (it never commits, checks out, resets or pushes), except for the periodic
`git fetch` (see below).

**Origin** is recorded when the thread starts: the branch (`HEAD` when detached), the commit the thread started from,
and the developer's git identity (`user.name <user.email>`; the repository's config wins over the global one). The
origin goes along to the memory made from that thread (see [Git origin of memory](06-memory.md#git-origin-of-memory)).

**Fingerprint**: whenever an executor writes a file, AIT keeps git's *blob id* of its content (`git hash-object`, with
the repository's `.gitattributes`/`autocrlf`, as a commit would store it). A blob id does not change with a squash merge,
rebase or cherry-pick, so AIT still recognises the thread's change when its commit SHA differs. When the developer edits
the file again before committing, the blob differs; AIT then matches the **lines** the thread's tasks added and removed
(from the task diffs): when the file in `HEAD` holds ≥ 80% of the added lines and no longer holds ≥ 80% of the removed
ones, the change counts as committed *with the developer's edits*.

| State | Meaning |
|---|---|
| Not committed | The file still holds exactly what the thread wrote, not staged |
| Staged | `git add` done, not committed |
| Committed | `HEAD` holds what the thread wrote (or its lines, marked *edited by the developer*); its commit is recorded, a squash commit too |
| Pushed | Committed and in a remote branch (as of the last fetch) |
| Changed again outside AIT | The file changed again and is not committed (neither what the thread wrote nor the original) |
| Stashed | Taken away by `git stash` (also `stash -u`); back to normal after `stash pop` |
| On another branch | The change is in a commit of another branch (local/remote) or a tag, not of the branch checked out now, e.g. after switching branches |
| Discarded outside AIT | The file is back to what it was before the thread (discard, `checkout --`, reset, or its commit reverted) and no branch/tag holds the change |
| Commit lost | It was committed, but no branch/tag holds that commit any more (`reset --hard`, rebase, force-push, branch deleted) |

A thread takes its state from its files: all discarded/lost = *Commit lost* when a commit was lost, else *Discarded*;
any changed again = *Changed again outside AIT*; none left in the working tree = *Stashed* or *On another branch*;
otherwise the least advanced state (e.g. 2 files pushed, 1 not committed = *Not committed*). Files git ignores do not
count.

**When it is checked**:

- when the thread finishes (a missing fingerprint is taken then);
- when the `.git` folder changes (`HEAD`, index, refs, reflog: commit, checkout, merge, reset, stash, fetch), 2 seconds
  after the last change;
- every 5 minutes (edits in the working tree do not touch `.git`);
- before the planner builds the brief section `# Changes in this session`;
- by hand: the **Sync** button on the Git card of the thread page or of Projects > Detail.

Threads are followed for 30 days after they finish, at most the 200 newest per project. A thread rolled back through AIT
is no longer followed.

**Automatic `git fetch`**: so other developers' pushes and merges show, AIT runs `git fetch --all --prune` for the
projects in use (seen or with a followed thread within 30 days), by default every **15 minutes**, one project at a
time. The interval is set per project in [Projects > Detail → Git](05-menu.md#project) (0 = off), next to a
*Fetch now* button. Fetch never asks for credentials: a remote that needs a login fails and is named once in the
activity log.

**Shared with the team**: the git state of every thread (with its branch, author, commits and the blob of each file) is
written to the `ThreadChanges` table in the long memory database. Other developers using the same store know where that
change is too, and their AIT checks its own clone: the change counts as in their branch when `HEAD` holds one of its
commits or all of its blobs (a squash merge).

**Effect on the planner**:

- A thread whose change is *Discarded*, *On another branch* or *Stashed* no longer counts as "this change" in the brief
  section `# Changes in this session` (its files are not in the working tree).
- Memory from a change that is *Discarded* or *Commit lost* is not read by the planner (it stays in the Memory menu).
- Memory from a change that is not in the branch checked out (on another branch, or not committed yet on its author's
  machine) is still read, but marked `(not in this branch: …)` and put after the other memory; the planner is asked to
  check the files before relying on it. See [Memory and git](06-memory.md#memory-and-git).

## Environments

Remote branches can be made **environments** (dev, staging, staging2, prod, …) in
[Projects > Detail → Git](05-menu.md#project): a name, a ref (e.g. `origin/develop`) and a *prod* mark. The list is
**shared** by every developer of the project key (kept in the long memory database), so one person sets it up.

After every `git fetch` (automatic or *Fetch now*), every 5 minutes, or with *Check now*, AIT looks for every change the
team shares (`ThreadChanges`, from any developer) in each environment's ref of this clone. A change **reached** an
environment when:

1. the environment's history holds one of its commits (merge, fast-forward); or
2. the environment's tree holds the blob of every file it wrote and no longer holds the files it removed (squash merge,
   cherry-pick); or
3. the environment's history once held those blobs (the file changed again since; at most 50 changes per environment per
   pass).

What is seen goes to the shared `ChangePresences` table (thread, environment, the environment commit that brought it,
who saw it). Once there it stays; any developer's AIT that fetches adds to it. Changing an environment's ref or removing
it forgets what was seen in it. The name `work` cannot be used (AIT uses it for the working wiki).

Its effects:

- **The business wiki per environment** — see [Wiki per environment](06-memory.md#wiki-per-environment).
- **The Memory menu**: the *In &lt;environment&gt;* filter shows only memory whose change reached that environment
  (memory without a thread still shows).

## Commits made outside AIT

Commits developers make **without AIT** can be summarized into memory and the business wiki too. It is **off by
default**, because every commit costs tokens of the memory node (the cheap node: Tools → Wrapper → Planner). Turn it on
in [Projects > Detail → Git](05-menu.md#project), section *Commits made outside AIT*; the settings are shared by every
developer of the project key.

| Setting | Meaning |
|---|---|
| Ref | The branch whose commits are read, e.g. `origin/develop`. Empty = the *prod* environment's ref, or `origin/HEAD` |
| Since | Only commits after it was turned on (turning it on again starts from then), so old history is not summarized at once |
| Commits per pass | At most this many commits summarized per pass (default 10, at most 50) |

Each pass (after a `git fetch`, every 5 minutes, or *Summarize now*) AIT reads that ref's commits, oldest first, without
merge commits, and **leaves out** AIT's own: a commit or squash commit of a thread change the team shares, and a commit
whose every file holds exactly what an AIT thread wrote. The rest is:

1. **claimed** in the shared `IngestedCommits` table — one commit is taken by one AIT only, even when several developers
   have it on;
2. summarized by the model from its diff (at most 14,000 characters, secrets masked) and message into one **change
   entry** (`Change`) in the same format as a thread's; its author and ref become the memory's origin, and the Memory menu
   names its source *commit &lt;id&gt;*;
3. recorded as a `ThreadChange` (marked *ingested*), so git state, [environments](#environments) and the wiki per
   environment treat it like a thread;
4. summarized into the **business wiki** by the wiki writer of the AIT that claimed it, with the next threads.

A commit with nothing worth keeping (formatting, a version bump, generated files) is skipped by the model. A commit that
fails to be summarized is marked *failed* and not tried again automatically. The newest commits and their state show on
the Git card.

## Unit tests

Every project has a **unit test catalog** shared by every developer of that project (scope `ProjectKey`, stored with
long memory, so it moves to an external DB along with it). The goal: a developer working in an unfamiliar project still
runs the tests left by earlier developers, so their change does not break behaviour that is already guarded. The catalog
and its settings are in [Project > Detail → Unit tests](05-menu.md#unit-tests).

Tests are run **by AIT itself, without a model**. A model is only used to give a title and purpose to tests found by
*Scan tests* (a cheap node, like memory labels), to decide the fate of each test per change (the planner), to diagnose
failing tests (the Tester node), and to write or fix test code (executors).

### Recognized runners

The command is fixed per stack and never comes from a model, the same as the build command. Test names only come in as a
filter escaped by AIT.

| Stack | Suite recognized from | Command | Result |
|---|---|---|---|
| .NET | a project (`.csproj`/`.fsproj`/`.vbproj`, also those listed in a `.sln`/`.slnx`) that references xUnit, NUnit, MSTest or `Microsoft.NET.Test.Sdk` | `dotnet test <project> --logger trx` (+ `--filter`) | per test (TRX) |
| .NET Framework (old-format csproj) | the same | `vstest.console.exe` from Visual Studio on the test assembly in `bin` (it must be built already) | per test (TRX) |
| .NET, an SDK-style test project that references an old-format project (`ProjectReference`, up to 4 levels) | the same | built first by **the Visual Studio MSBuild** (`-restore -p:RestorePackagesConfig=true`, `SolutionDir` = the nearest solution above it, output to a folder owned by AIT), then `vstest.console.exe` on the resulting assembly | per test (TRX) |
| Node.js | a `package.json` with jest, vitest or mocha (a dependency or the `test` script) | the runner from `node_modules` run through `node` with a JSON reporter (+ a title filter) | per test |
| Other Node.js | another `test` script (other than the `npm init` default) | `npm test` | exit code only: every test of that suite passes or fails together |
| Python | a `pyproject.toml`/`setup.py`/`requirements.txt`/`Pipfile` folder holding `test_*.py`/`*_test.py` | `python -m pytest --junitxml` (a `.venv`/`venv` virtualenv is used when present) | per test (JUnit XML) |
| PHP | `composer.json` + `phpunit.xml(.dist)` | `vendor/bin/phpunit --log-junit` (+ `--filter`) | per test (JUnit XML) |
| Java (Maven) | a `pom.xml` with a `src/test` folder | `mvnw`/`mvn -B -q test` (+ `-Dtest=`) | per test (surefire) |
| JVM (Gradle) | a `build.gradle(.kts)` with a `src/test` folder | `gradlew`/`gradle test` (+ `--tests`) | per test (JUnit XML) |
| Go | a `go.mod` with `*_test.go` files | `go test -json -count=1 ./...` (+ `-run`) | per test |
| Rust | `Cargo.toml` | `cargo test --no-fail-fast` | per test |

- A suite that cannot run on this machine is marked with its reason (e.g. *jest is not in node_modules (run npm install
  first)*, *vendor/bin/phpunit does not exist*, *the test assembly has not been built yet*). AIT does not install
  packages.
- **Why a mixed test project does not use `dotnet test`:** `dotnet` cannot restore `packages.config` and has no Visual
  Studio targets (`Microsoft.WebApplication.targets`, etc.), so it fails to build old .NET Framework projects. The Visual
  Studio MSBuild used for the solution build (verification) is used here too, including its restore and the same NuGet
  feed credentials, so a new test project needs no separate login. The build step uses part of the test run time limit;
  its failure (restore, compile) shows as *The tests cannot run* with the end of the MSBuild output. Without Visual
  Studio/Build Tools that suite is marked as unable to run with its reason.
- One project runs its builds or tests one at a time (both fight over `obj/`/`bin`), with the *Test run time limit*
  (default the same as *Build timeout*). The variable `CI=true` is set so runners do not wait for input.
- When more than 120 tests are chosen, the filter is dropped and the whole suite runs (the command line would be too
  long on Windows); the results are still matched per test.

### Catalog content

One row per test: the number `UT-0001` (in order per project; the number of a removed test is not reused), the title and
purpose in the developer's language, the scenario (*given / when / then* with concrete values), module, the files under
test (targets), framework, test file and name, needs (`database`, `network`, `config`), status, origin, creator and
origin thread, the last result (when, by whom, duration, failure message, reason it was skipped), and the history of the
last 20 runs per test.

| Status | Meaning |
|---|---|
| Active | Run when relevant. |
| Quarantined | Flaky (passes when repeated, or according to the Tester); not run by threads until activated again. Can still be run manually. |
| Obsolete | The feature under test was removed by a thread. Must have a reason, is not run, and waits for the developer to confirm its removal. |
| Removed | Its removal is confirmed. Still recorded (its number is not reused) and never restored. |

Origin: *AIT* (made through a thread), *Scan* (already in the repo), *Rewritten* (restored from its scenario — see
[Restoring](#restoring-lost-tests)).

The catalog also stores a **snapshot** of the test file content (with its test method and the supporting files it needs:
the test `.csproj`, fixtures, runner config) for restoring. The snapshot is taken when the test is registered and updated
every time the test passes. Snapshots are **never shown** in the UI or sent through `/api`; their only way out is a
[memory export](06-memory.md#export--import). Turn off *Keep test snapshots* for a project whose code must not be stored
outside the repository (existing snapshots are deleted too).

### Filling the catalog

- **Automatic**: the first thread of a project that has a test suite but an empty catalog scans the code first (without
  a model). The title is formed from the method name (`Save_WithoutName_IsRejected` → *Save without name is rejected*).
- **Scan tests** (Project > Detail): scans again, registers new tests, follows tests that moved file, and gives them a
  title, purpose, scenario, module and targets with a cheap node (Tools → Wrapper → Planner, like memory labels).
- Static detection: C# through Roslyn (`[Fact]`, `[Theory]`, `[Test]`, `[TestCase]`, `[TestMethod]`, …), other stacks
  through patterns (`it(`/`test(`, `def test_`, `@Test`/`@ParameterizedTest`, `func TestX`, `#[test]`, PHPUnit `test*`
  methods). Targets are guessed from the test file/class name (`UserServiceTests` → `UserService.*`) and the code map.
- **Through a thread**: new tests designed by the planner (see below) join the catalog when the thread finishes, with the
  planner's scenario.

### The flow in a thread

```text
0. Restoring lost tests (AIT, before the planner)
1. Planner  ──┬── baseline of the relevant tests (AIT, in parallel, files not changed yet)
2. Execution  │   (implementation + test files, in parallel)
3. Build      │
4. Unit tests (AIT) ── failed? ── Tester triage ── fix tasks ── repeat 3–4 (at most 3 rounds)
5. UI/API testing (Auto Testing), 6. Wrapper + record the results in the catalog
```

This stage runs when the project has a `ProjectKey`, the catalog mode is not *Off*, and there is a recognized test
suite. The catalog is read for at most 10 seconds; a store that cannot be reached makes the unit tests skipped instead of
holding the thread.

**Relevant tests** = active tests whose test file, target files or module overlap with the files touched. The *All
active tests* mode takes all of them. Before there is a plan, the estimate comes from the files the prompt names and
their code map; once the plan is made, the tests related to the task files and the `update`d tests are added, and the
`obsolete` ones are taken out.

**The baseline** (the results before the change) runs **in parallel with the planner**, because the files have not
changed yet. Execution waits for the baseline to finish (progress `🧪 Unit test · baseline · n related tests`). Results
are reused without running:

- when the content of the test file and its target files is exactly the same as when the last result was recorded (a
  hash) and that result is not older than 14 days — the same code gives the same result, whoever runs it;
- when that test was skipped because of the environment on this machine less than 24 hours ago (without waiting for a
  connection timeout again).

### Planner decisions

The planner's brief gets a `# Unit tests` part: suites, the rule for new tests, and the relevant tests (at most 30) with
their number, title, purpose, file → targets, and last result. `submit_plan` gets a `tests` field:

```json
"tests": {
  "decisions": [
    { "test": "UT-0012", "decision": "update", "reason": "Request: \"the name may be empty for guest users\"" },
    { "test": "UT-0015", "decision": "obsolete", "reason": "The PDF export feature is removed as requested." }
  ],
  "new": [
    {
      "title": "Saving a guest user without a name is accepted",
      "purpose": "A guest user may be saved without a name.",
      "scenario": "given a guest user with name \"\", when Save, then it is saved without an error",
      "file": "tests/Shop.Tests/UserTests.cs",
      "name": "Save_GuestWithoutName_IsAccepted",
      "targets": ["src/Shop/UserService.cs"],
      "requires": []
    }
  ]
}
```

| Decision | Meaning | Consequence |
|---|---|---|
| `keep` (default) | The behaviour under test does not change | It must keep passing; when it fails, the **code** is fixed |
| `update` | The request **deliberately** changes the behaviour under test | An edit task on its test file is required in the same plan, with a reason that quotes the request |
| `obsolete` | The request removes the feature under test | A reason is required. The test is not run, becomes *Obsolete* after the thread finishes, and waits for a removal confirmation |
| `new` | A new test | Title, scenario, file, the test name exactly as in the code, targets; a create/edit task on that test file is required |

- New tests are made **only when the developer asks for them**, unless the *Create new unit tests* setting is *Automatic
  for business logic changes* (a Logic layer that has no test yet) or *Never*. With Auto Testing on, a change the Tester
  cannot check in the running application **always** gets a dry-run test (except with *Never*) — see
  [Background jobs and external systems](#background-jobs-and-external-systems).
- **A project without a test project**: when a new test is needed, the planner plans its test project as well, without
  asking: for .NET an xUnit project `<Name>.Tests` next to the project under test (`Microsoft.NET.Test.Sdk`, `xunit`,
  `xunit.runner.visualstudio`, Moq when fakes are needed, a `ProjectReference` to the project under test); for Node.js
  the runner the package already uses, or Vitest; pytest for Python, PHPUnit for PHP, JUnit for Java. A `.slnx` solution
  gets a `<Project Path=…>` line; a classic `.sln` is left alone (`dotnet test` builds the test project itself). The
  planner's brief says *No test project yet*, and `run_tests` is not offered. After the executors finish, AIT reads the
  suites again from the manifests on disk (at most 3 folders deep), then the new tests run and are registered as usual.
  A plan that adds no test runs no unit test at all.
- Cases are designed by the planner from the request and the business rules, not from the implementation. The executor
  of a test file only sees the contract and the instruction (not the implementation code) and follows the style of the
  other test files in the same suite.
- The planner may not change or retire another developer's tests just so its change passes; when in doubt it asks with
  `ask_user`.
- The validator rejects a plan when a test number is not in the catalog, a test has two decisions, an
  `update`/`obsolete` has no reason, an `update` has no edit task on its test file, a new test has no
  name/title/scenario/file or no task that writes its file, there are more than 15 new tests, new tests while they are
  turned off, or new tests in a plan without tasks.

The `run_tests(tests?, file?, all?)` tool runs catalog tests (numbers, one test file, or all) and returns their results to
the planner: passed, failed with the message, or skipped with the reason. Used only for requests that really are about
tests (*"fix the failing tests"*, *"why does UT-0012 fail"*). Its results also become the baseline, and are reused by
`tests.run` in the same thread (tests do not run twice; progress: *n from the planner's run_tests*). This tool cannot be
delegated to a tools node.

**Testing a change** (*"test this change"*, *"run the tests for feature X"*) only uses the catalog tests that cover that
change (their test files and targets). Tests of other features are not run as a substitute — their passing proves
nothing about this change. When nothing covers it yet, the request counts as a request for tests: the planner designs
`new` tests for that change (dependencies on a database, API or other service are mocked), or asks first with `ask_user`
when the scope is unclear. When there is no related test, the planner's brief names the test files the catalog does have
(at most 10).

### After the build

Unit tests run in every verification round **after the build passes** (a project without a build command counts as
passed): the relevant tests, the `update`d tests, the tests in files touched by fix tasks, and the new tests (looked up
in their file after the executors finish; ones not found are reported as *new test not found*). A failed test is
**repeated once**; when it then passes, it is marked *flaky* and not counted as failed.

| Result | Meaning | Action |
|---|---|---|
| ✅ passed | | |
| ✅ fixed | Failed before the change, passes now | reported |
| ❌ regression | Passed before the change (the baseline, or the catalog's last result), fails now; or an `update` test that fails | fixed |
| ❌ new test failed | A `new` test fails | fixed |
| 🟠 already failed before | Fails before and after the change | only reported, not this change's fault |
| 〰️ flaky | Failed then passed when repeated, or according to the Tester | quarantined when the thread finishes |
| ⏭️ skipped | See [Skipped tests](#skipped-tests) | reported with the reason |

**Tester triage.** When there is a regression or a failing new test and an active **Tester** node, the Tester (a strong
model) gets the request, the plan summary, the planner's decision per test, the diff of every changed file (at most 150
lines per file), and the code, purpose and failure message of each test, then decides per test:

| Verdict | Consequence |
|---|---|
| `regression` | A fix task on the **code** the Tester points at |
| `test_wrong` | A fix task on the **test file** — only for this thread's `new` or `update` tests. For an old test of another developer, the code is fixed and a note goes into the report |
| `plan_wrong` | The behaviour was deliberately changed by the request but the planner marked it `keep`: the test becomes `update`, is fixed to follow the new behaviour, and the developer is told in the report |
| `environment` | Not a code fault: the test becomes *skipped* with the Tester's reason |
| `flaky` | Flaky: quarantined when the thread finishes |

Everything passes = 0 Tester tokens. **Without a Tester node** (or when the Tester fails to answer) the rules are
mechanical: what is fixed is the test's target file that the thread changed, or the only non-test file the thread
changed; when that cannot be decided, the failure is reported without a fix. Progress: `🩺 Triage · n failed tests ·
model`.

**Fixes** use the same `V<round>-<n>` tasks as build/reviewer (title *Fix failing unit tests*, an *Unit tests that fail
after the change* part in its instruction: the test, its purpose, the failure message, and a *do not change the test*
direction for code fixes). Unit test fix rounds are limited to **3** (separate from build/reviewer *Fix rounds*, but `0`
there turns both off); remaining regressions are reported as they are. Every round rebuilds and then runs the tests
again.

Progress per round in the `🔍 Verification` phase:

```text
│  ├─ ✅ Build succeeded (9.8s)
│  ├─ 🔴 Unit test · 14 tests · 12 passed, 1 regressions, 1 already failed before the change (8.2s)
│  │     UT-0012 Saving a user without a name is rejected — Assert.False() Failure …
│  ├─ 🩺 Triage · 1 failed test · cc/claude-opus
│  ├─ 🔧 Fix 1 · 1 task
```

### Skipped tests

A *skipped* result **always comes with a reason** and is never counted as a regression:

| Source | Example reason |
|---|---|
| Failed because of the environment, recognized from the error message (database connection refused, unknown host, empty connection string, missing config file) | *Needs a database that is not available on this machine: Login failed for user 'app'* |
| Tester triage `environment` | *Environment (according to the Tester): the payment service is not running* |
| Skipped by the test itself (`[Fact(Skip = "…")]`, `@Disabled`, `pytest.skip`, `it.skip`) | the reason from its code, or *Skipped by the runner without a reason.* |
| The runner cannot run | *jest is not in node_modules (run npm install first).* |
| Skipped because of the environment on this machine less than 24 hours ago | the earlier reason + *(not tried again: the last run on this machine was skipped too)* |

**Exception: new tests from the same thread.** A test written for this change is a dry-run test (every database,
network and config is faked), so a failure because of a database/network/config is **never skipped**: that test counts as
not isolated and is fixed (a fix task on its test file, at most 3 rounds) with the instruction *fake or inject every
database, network call, config and clock it touches*. A Tester `environment` verdict for a new test is treated the same
**when its message really is a database/network/config error while the test runs**. A restore or build that cannot run
on this machine (a NuGet/npm feed that needs credentials: `NU1301 401 Unauthorized`, a missing SDK or targeting pack) is
still *skipped* with its reason, because no change in the test code could fix it. Without this exception a thread could
finish as *passed* without a single new test ever having run.

The detected needs (`database`, `network`, `config`) are recorded on that test. Running a test from Project > Detail
always tries again. A test the runner does not report at all (its name or file changed) shows as *not reported by the
runner*; run *Scan tests* to follow the change.

### Restoring lost tests

Tests are a standard part of a project, so catalog tests (active or quarantined) whose file or method does not exist in a
developer's repository are **restored automatically before the planner runs** (progress `🧷 Unit test restore · n
tests`), stopping at the first layer that succeeds:

1. **Moved file**: a test with the same name is found in another file → the catalog follows its new file.
2. **Git notes** (without a model): the commit the snapshot was taken at is checked against your branch. Not there yet →
   *UT-0012 comes from commit `abc12345` (branch feature/x), which is not on this branch yet; pull/merge that branch …*.
   Already there but its file was deleted afterwards → advice to mark that test obsolete when the deletion was on
   purpose. The restore still continues so the guard is there right now.
3. **Snapshot** (without a model, exact): a lost file is written from the snapshot together with the supporting files
   that are also lost (a new test project is registered with `dotnet sln add`). A method lost from a file that still
   exists is inserted again through Roslyn for C#; other stacks through a small executor task (`R1`, …) that copies the
   method code from the snapshot.
4. **Rewrite from the scenario** (uses a model, the last fallback): an executor writes a new test from the catalog
   scenario with another test file as a style example. Its origin becomes *Rewritten* and it should be reviewed by the
   developer.

Existing files are **never overwritten**. Restores are **not part of a thread's rollback** (they are not part of the
request): restored files show up as new files in `git status` for you to commit. A restored test that fails in the
baseline is reported as *already failed before the change* (your code may have changed since the test was made), not as
a regression.

### Threads that only test

A request that only asks for testing without code changes (*"run all unit tests"*, *"please test the Homecoming
feature"*) does not end in a direct answer. The planner sends a plan without tasks with `tests.run` (test numbers, or
`["all"]`) and/or an [Auto Testing](#6-testing-auto-testing) `test` part; AIT runs the unit tests and/or the Tester
against the existing behaviour, without execution, build or review, then the Wrapper explains the result. The status
line: `🧪 **Testing done** · no code changes · unit tests passed (12)` (or *some failed*). Failed tests are recorded in
the thread's error column.

| What is asked | What runs |
|---|---|
| A form / page | A plan without tasks + `test.level = ui`: the Tester tries its flow in the browser (AIT starts the application when it is not running) |
| An API | A plan without tasks + `test.level = api`: the Tester sends requests, the responses show in *API output* |
| A function / scheduler that already has tests in the catalog | A plan without tasks + `tests.run` |
| A function / scheduler **without** tests (or one that cannot be seen from the application: Hangfire/Quartz jobs, hosted services, consumers) | The planner writes a new **dry-run test**: its tasks are only test files (and a test project when there is none yet), **the code under test is not changed**. That test runs after the build and joins the catalog. Its test file stays in the repository; roll the thread back when you do not want to keep it |

The planner may not answer such a request without tests or tell the developer to test it themselves. A plan without
tasks that holds `tests.new` is rejected by the validator (*tests.new needs tasks that write the tests*), so new tests
are never lost silently. Needs a project key and a unit test catalog that is not *Off*.

### When the thread finishes

The results are recorded in the catalog, also when every task failed (a cancelled thread does not record its results):

- the last result of every test that ran (the result after the change wins; one that only ran in the baseline stores
  that result without a hash), the run history (the `baseline`, `after`, `manual`, `flaky` phases), and the snapshots of
  passing tests;
- new tests join the catalog with the next number, the module from the code map, and a snapshot;
- `obsolete` decisions (only when a task succeeded) and the quarantine of flaky tests.

The final answer holds the results in the status line (`unit tests passed (12)`, `unit test: 1 regressions`, …; a
regression or a failing new test makes the status *🟡 Partly completed*) and the **🧪 Unit test** part: only the tests
that need attention (at most 10: failed, fixed, already failed before, flaky, skipped with the reason), tests marked
obsolete, restored tests, and notes (git, Tester decisions). Tests that keep passing are only counted. The full details
are in the *Unit tests* card of the thread detail.

A thread **rollback** also undoes its changes in the catalog: new tests added by that thread are removed from the catalog
and its `obsolete` decisions go back to *Active*.

Current limit: **🔁 Retry failed** does not run unit tests (only build/reviewer); run the tests from Project > Detail
afterwards.

## Function calls

AIT records what each function calls, bit by bit, only in code that is really touched:

- **Triggers**: functions in the lines the planner or a tools node reads (`read_file`, files inside the project only),
  functions the thread changes (line blocks that differ from the content before the thread), and functions the planner
  asks about through `function_calls` that are not recorded yet or changed. There is no scan of the whole project.
- **Reader**: the first active **Tools Node** (with its fallback node). Without an active Tools Node, nothing is read.
  One model call holds several functions of one file (at most 20 functions / ±16,000 characters; a long function is cut
  at 8,000 characters). The model names the calls, their kind (`function`, `method`, `http`, `view`, `sql`, `other`) and
  their lines; libraries/frameworks (jQuery DOM, LINQ, logging, …) are ignored.
- **Checks**: a call whose name is not written on the line given (±1 line) is dropped; for `http`, its URL part must be in
  the function body. The declaration **location** is looked up by AIT itself from Code structure, not by the model: the
  same file first, then the class named (`clsKlaimDFBL.GetByDocNo` → that class's file, including partial classes), then
  a name that is unique in the project. A URL is matched to `{Controller}Controller.{Action}` (the last two segments, so an
  area in front does not matter) or to a script file with that path. Uncertain ones are left *not found yet*.
- **Thrifty**: a function whose body is the same as when it was last read is not sent again; a failed one is tried again
  no sooner than 1 hour later. The queue is processed one file at a time in the background; the planner and executors do
  not wait.
- Results are stored in the local database (the `CodeFunctionScans` and `CodeFunctionCalls` tables), following the clone's
  files on this machine; they show in Project → detail → *Code structure*.
- **Used by the planner**: `function_calls` gives the calls of a function with its recorded callers, and `code_outline`
  marks functions that already have records with `[calls: N]`. The planner can follow a flow (screen → endpoint → action
  → logic) without reading every file on its path, then confirm it with the code. The more a project is worked on through
  AIT, the more complete its records get.

## Session context (Context Keeper)

The planner carries no memory between prompts: every prompt starts a new planner. So a long chat stays connected without
sending the whole conversation, AIT stores a **session summary** per thread:

1. The first line of every orchestrated reply is the marker ``**AIT** · `ProjectName` · thread `3f2a1b9c` `` (always
   sent, also when progress is turned off). This marker is stored in the CLI chat too. The old marker
   ``**AIThread** · …`` from replies before the rename is still recognized.
2. After a thread finishes, the Context Keeper writes a session summary (goals, decisions & limits, a work log per
   thread, key facts as file pointers, unfinished things) and stores it on that thread.
3. On the next prompt, the proxy looks for markers in the history the CLI sends, takes the summary of the latest thread
   that has one, and gives it to the planner with only the messages after that thread.

Because the marker used is always one still in the chat, the context follows the chat automatically: after a *rewind* or
an edited message, the summary goes back to the last remaining thread; `/clear` or a new chat starts from empty; resuming
an old session restores its summary. The search is limited to the same project and threads from the last 60 days.

- **Model**: the active *Context Keeper* node with the lowest priority; without that node the Wrapper is used. Without
  both, or when the model fails, the new thread is recorded as it is in a *Not yet summarized* part and summarized at the
  next update.
- **Keeping the summary from drifting**: the summary is usually updated from the previous summary, but every 8th thread
  (and every time the previous summary does not come from the thread right before) it is rebuilt from the records of at
  most the last 15 threads in the database.
- **Details are not lost**: the planner can call `read_thread` for the full record of any thread, and the summary holds
  file pointers, not file content, so the planner reads the latest version.
- **Analysis results are stored too** (`ThreadRun.InvestigationJson`): while planning, AIT records the files the planner
  read with their line ranges (merged, at most 40 files), lookups (`find_symbol`, `find_references`, `search_text`,
  `find_files`, `function_calls` with the start of their results; `code_outline` without results, at most 30), and the
  findings of `investigate` sub agents (at most 8). These notes go into the brief of the next prompt in the same chat, the
  files read are outlined too, the Context Keeper turns them into *Key facts*, and `read_thread` shows them. A follow-up
  question ("what type is button A?") is then answered from the earlier analysis; the planner only reads again the line
  ranges it needs to confirm. A thread that continues after a planner question carries on the notes from before the
  pause.
- The latest summary can be seen in the thread detail (the *Session context from the Context Keeper* panel).

For plain Claude Code sessions (including ones that do not go through the proxy), the Context Keeper can also bridge
`/clear` through Claude Code's official hooks — see [Context Keeper for /clear](03-integrasi-cli.md#context-keeper-for-clear).

## Skills on a node

A skill is a ready-made instruction (a folder with a `SKILL.md`, in the Claude Code / Agent Skills style) that makes a
model better at one kind of work. AIT keeps them in an offline **skill store** and attaches them to nodes.

1. **Fetching a link** (Setting → Skill). The link is recognized, then downloaded to a staging folder:
   - GitHub/GitLab as a ZIP archive, without git. A `/tree/<ref>/<folder>` link only takes that folder.
   - A `.git` URL or another git host through `git clone --depth 1`, which is also the fallback when the ZIP download
     fails.
   - An `.md` file is taken as it is.
   - A web page: its text and links are read by a model.
2. **The Skill Orchestrator examines the content**, using the model chosen in the Skill tab (default: the active Planner
   node).
   - A repository with `SKILL.md`: each skill gets keyword tags, and entries that are not skills are dropped.
   - A repository without `SKILL.md`: the model points out which Markdown files are skills (e.g. `agents/*.md`, Cursor
     rules).
   - A web page: skills written on the page are stored as `SKILL.md`, and up to 5 linked repositories are fetched as child
     sources.
3. **The store** is in `%LOCALAPPDATA%\AIThread\skills\<source id>` (change it with `AIThread:SkillsDirectory`). The
   content of `SKILL.md` is stored in the database too. *Sync* fetches a source again; the same skill (folder + file) stays
   attached to its nodes, while a skill that disappeared from the source is detached from nodes.
4. **Choosing skills for a node** (Node modal → *Find skills*). The need you write is matched by the Skill Orchestrator
   against the name, description and tags of every skill (a large store is filtered by keywords first to 150
   candidates). The result is at most 6 suggestions with reasons; suggestions scoring ≥ 0.6 are already ticked. The chosen
   ones show as chips and are stored as node–skill relations (at most 10 per node), **not** as text in *Extra
   instructions*.
5. **At runtime**, a node's system prompt holds the role prompt, then *Additional instructions*, then an *Active skills*
   part. That part holds one `<skill name="…" folder="…">` block per skill with the full content of its `SKILL.md`, and the
   model is asked to apply the workflow and checklist of that skill. The total skill content is limited by *Skill
   character limit per node* (default 40,000, split evenly). A longer skill is cut with a pointer to its full file. The
   planner is also told the skill folder, so it can read supporting files (references, scripts, templates) with
   `read_file` and attach them to tasks as `context` for executors. This applies to every role: Planner, Executor,
   Reviewer, Wrapper and Context Keeper.

## Security limits

- The planner can only read; an executor can only write its task's target file. Writes are limited to the workspace root
  and cannot touch hidden folders. The planner's reads may go outside the root (absolute paths), so it can read files the
  account running AIT can read, except credential files (keys, certificates, SSH keys, `.npmrc`, `~/.aws`, user-secrets,
  AIT data), which are never read or written. A "check" request on another folder only produces an answer; changing files
  outside the root is not supported.
- **Secrets are masked** before any text is sent to a model: file content, prompts and chat history, tool output,
  application responses while testing. Passwords, API keys, tokens, JWTs, private keys and connection string passwords
  are replaced with `{{AIT_SECRET:…}}`, then their real values are put back only when an executor writes a file (or the
  Tester calls the application). Logs, memory, the wiki and exports are stored masked too. Details and limits are in
  [Security & privacy](12-keamanan.md).
- The commands AIT runs are the project's **build command** (the table in
  [Verification](#5-verification-build-and-reviewer)) and the **unit test runners** of the catalog, with fixed programs and
  arguments; a model only chooses a manifest inside the root. A build runs whatever the project itself defines (MSBuild
  targets, the `build` script in `package.json`, Maven plugins), so turn off *Automatic build & verification* for a
  project you do not trust. There are no other shell commands (package installs, migrations). The exception is
  `git clone --depth 1` when fetching a skill source from a non-GitHub/GitLab git host (or when the ZIP download fails).
  That clone uses the URL you pasted, without a credential prompt, and its `.git` folder is discarded.
- **Auto Testing** runs a browser through Playwright (Edge/Chrome/Chromium with a Node.js driver process: `node` on
  `PATH`, or downloaded once from nodejs.org when there is none) and HTTP requests — both only to addresses of this
  machine (`localhost`, `127.0.0.1`, `[::1]`); other URLs are refused, whatever the plan or the model's answer says. Links
  the page itself follows are not limited. The tester acts like a user of your application: it can create or change data
  through that application, so turn Auto Testing on only for an application running with development data. HTTPS
  certificates are not checked (localhost development certificates), and the tester's HTTP requests use the Windows
  account running AIT when the application asks for Windows Authentication. The testing login password only exists in
  AIT's memory and is not sent to a model (see [Login](#login)).
- Skill content goes into the node model's system prompt. Fetch skills only from sources you trust, because a skill can
  steer the model's behaviour.
- AIT writes to whatever folder is sent as the working directory. Let the application listen on `localhost` only, and
  fill in the *Proxy API key* when other people can reach the machine.
- The 9router API key is stored encrypted with ASP.NET Core Data Protection.
