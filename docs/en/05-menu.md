# 5. Menu guide

Every page updates itself live through Server-Sent Events — no refresh needed. The sidebar menu is grouped into
*Workspace* (Dashboard, Project, Memory — used every day), *Configuration* (Setup, Node, Skill, CLI, Setting) and *Help* (Docs,
Log & diagnostics, Getting started). Memory, Setting, and Log & diagnostics are split into
sections picked from a submenu in the sidebar (the pages have no tabs): clicking the parent menu only folds/unfolds
the submenu, clicking a submenu item opens that page with that one section. The submenu of the open page is always
unfolded; the fold state of the other menus is remembered by the browser. The *Needs review* and *Incidents* badges
show in the submenu. Links such as `/Setting#telegram` or `/Memory#bisnis` still open their section. The pill at the bottom left of the sidebar shows the 9router status; the *Switch theme* button toggles
between dark and light mode, and the language box switches between Bahasa Indonesia and English
([Display language](02-konfigurasi.md#display-language)).

**Window buttons.** Every window, dialog, log panel and code block uses three macOS-style buttons at the left of the
title bar (their symbols show when the cursor is over them):

| Button | Dialog | Log panel | Code block (output, diff, build, config preview) |
|---|---|---|---|
| 🔴 red | close | clear the log view (new log entries keep coming) | inactive (gray) |
| 🟡 yellow | minimize to the bottom of the screen; the page behind it can be used, click its title to open it again | fold / unfold | fold / unfold |
| 🟢 green | full screen | full screen | full screen |

Double-click the title bar = full screen; Esc leaves full screen. The fold state of log panels is remembered by the
browser. Banners and notifications only have the red button.

**Setup not finished.** As long as a required [Setup](#setup) step is not finished, the *Setup* link in the sidebar shows
a number (e.g. `2/4`) and an *AIT setup is not finished* banner with the remaining steps shows at the top of every page.
The banner's red button hides it until another step is unfinished. When AIT is opened for the first time (no node
pipeline yet), the Dashboard opens the Setup page right away, once per browser.

**Getting started.** An illustrated popup with 9 steps: meeting AIT, installing AIT, preparing 9router, the 9router
connection, a model per role, Project, CLI, starting work, and done. Swipe, click a dot or use the ← → arrow keys to move
between steps; the *Do it in Setup* button opens the same step on the Setup page. Open it with the *Getting started*
menu in the sidebar's *Help* group (continues from the last step seen) or *Illustrated guide* on the Setup page.

## Dashboard

**Status cards**

| Card | Content |
|---|---|
| 9router | Online/Offline, latency, number of models & combos, API key status. The *Check 9router again* button forces a check. |
| Short memory | Connected or not, the number of active short memories, and the store in use (Setting menu → Memory storage). |
| Database | Connected or not, the number of long memories and threads. |
| Node pipeline | Ready / not complete, with its notes. |

**Statistics**: threads today (and in total), running threads, the success rate of the last 100 threads, the average
parallel speedup, tokens today (planner + executor + wrapper), the number of projects and memories.

**Latest threads**: the last 25 threads — thread ID (the first 8 characters, the same as `thread …` in the CLI answer;
the full ID in the tooltip), time & source (*CLI*, *Dashboard*, *Telegram*), project, prompt, status, succeeded/total
tasks, duration, speedup, tokens. The search box at the top right looks for a thread ID (whole or its start), prompt or
project name across all threads (at most 100 results). Click a row for the detail. The thread table in Project → Detail
has the same column and search box.

**Live log**: newest entries at the top. A level filter (*Info+* default, *All (debug)*, *Warning+*, *Error*) and a
*Pause* button. The debug log holds planner tool calls and passthrough requests.

## Setup

Every first step on one page (`/Setup`), so there is no need to move between Setting, Node, Project and CLI. The status
of each step is computed from the current state of AIT (not stored), so this page is also used to check again: *Check
again* checks everything, and the page checks again by itself when you come back from another tab (e.g. the 9router
dashboard). The first unfinished required step opens automatically; once it is done, that step folds and the next step
opens.

| # | Step | Required | Done when | What can be done in place |
|---|---|---|---|---|
| 1 | Database & short memory | Yes | the main database and short memory are connected | — (prepared automatically on start; links to Installation/Troubleshooting) |
| 2 | Connect 9router | Yes | 9router is online, the API key is accepted (or not required), and at least one model is available | *Start 9router*, a link to the 9router dashboard, Base URL + API key → **Test & save** (only saved when the connection and the key are right) |
| 3 | Choose a model for each role | Yes | the pipeline is ready (Planner, Executor, Wrapper active) | choose a **strong model** and a **cheap model** → **Create nodes & test** |
| 4 | Register a project | No | at least one project is registered | a manual path, *Browse…* (the same browser as the Project menu) |
| 5 | Point a coding CLI to AIT | Yes | at least one CLI points to AIT, globally or per project | the **global** switch of each CLI; per project through the CLI menu |
| 6 | Team-shared memory | No | Long or Business memory uses an external database | *Import team profile*, a link to Setting → Memory storage |

**Models (pipeline preset).** Two model choices replace filling in nodes one by one. The first choices are guessed from
the model names (e.g. `opus`/`fable` as the strong model, `haiku`/`flash` from the same provider as the cheap model) and
can be changed.

| Node | Model | Default | Details |
|---|---|---|---|
| `AIT Planner` | strong | always | temperature 0.2, 16,384 tokens, 2 parallel |
| `AIT Executor` | cheap | always | 0.1, 8,192 tokens, 6 parallel |
| `AIT Wrapper` | cheap | always | 0.3, 4,096 tokens, 4 parallel |
| `AIT Tools Node` | cheap | ticked | the planner delegates `read_file`, `search_text`, `find_references` to this node |
| `AIT Context Keeper` | cheap | ticked | summarizes the CLI conversation |
| `AIT Reviewer` | strong | not ticked | checks the executors' results |
| `AIT Tester` | strong | not ticked | judges unit tests that fail after a change ([triage](07-cara-kerja.md#after-the-build)) and runs [Auto Testing](02-konfigurasi.md#auto-testing-tester-node); temperature 0.1, 4,096 tokens, 1 parallel. Required when the project uses a unit test catalog or Auto Testing |

Every preset node has priority 10, so it is used before nodes you made yourself (default 100). Running the preset again
updates the same `AIT …` nodes (model, active); a role that is not ticked deactivates its preset node (not deleted), and
a deactivated Tools Node releases the planner's tool delegations to that node. Other nodes in the Node menu are not
changed. After saving, every active node is tested with one small call (*pong*) and the result shows per row.

**Team setup profile.** A card above the steps: *Export profile* downloads an `ait-setup-<time>.json` file; *Import
profile…* picks a file, shows a preview of the changes per part, asks for the secrets that are needed, then *Apply
profile*. See [Team setup profile](02-konfigurasi.md#team-setup-profile).

## Thread detail

| Part | Content |
|---|---|
| Header | Status, project, source, CLI session, time, working directory. The *📋 Log* button opens [Log & diagnostics](#log--diagnostics) narrowed to this thread. A *Cancel thread* button while it runs (also while testing); after it finishes, *🔁 Retry failed* and *↩ Undo file changes* (see [Usage](04-penggunaan.md#retrying-failed-tasks)), and *🧪 Test again* for a thread with a UI/API testing plan, with an optional username & password (see [Testing](07-cara-kerja.md#6-testing-auto-testing)). |
| Permission | Shown while the thread waits for [developer permission](04-penggunaan.md#developer-permission) (a command, a background process, an outside host, files outside the project, a branch/PR, commit/push): the targets and the *Allow once* / *Always allow* / *Deny* buttons, plus *Always allow the pattern* when the command can be patterned and *Allow all commands in this project* for a command permission. |
| Request | Your prompt (without the context the CLI inserts, e.g. Claude Code's `<system-reminder>` blocks). |
| Planner plan | The summary and the **contract** table. |
| Executor tasks | A **parallel timeline** (overlapping bars ran at the same time) and the task table. Click a task for its instruction, dependencies, summary, error, colored diff and live model output. |
| Verification | Shows when a build/reviewer/unit tests ran. The final result (*Passed* / *Issues remain* / *Could not be determined*), the build command, the reviewer model, then per round: the build result and its errors, reviewer findings, the unit test summary, problems that cannot be fixed automatically, and the ids of the fix tasks (`V1-1`, …) that also show in the task table. |
| Git | Shows in a git repository. The thread's origin (branch, starting commit, author) and the state of its change now (*Not committed* / *Staged* / *Committed* / *Pushed* / *Changed again outside AIT* / *Stashed* / *On another branch* / *Discarded outside AIT* / *Commit lost*), the commits holding it, then the state per file (*edited by the developer before the commit* when its commit was recognised by the lines). The *Sync* button asks git again; otherwise it updates by itself (see [Git state of a thread's change](07-cara-kerja.md#git-state-of-a-threads-change)). |
| Unit tests (project catalog) | Shows when catalog unit tests were run or restored. The result (*Passed* / *Failed* / *Skipped*), the mode (or *Testing only*), the baseline (how many ran, how many from earlier results, duration), the runner, then a table per test: number, title and file, the planner's decision, the result before and after the change (*(cache)* = an earlier result for the same code), the verdict, the failure message or the reason it was skipped, and the Tester's verdict. Below it the tests restored automatically and notes. The results per round are in the Verification card. |
| Testing | Shows when Auto Testing ran. The result (*Passed* / *Failed* / *Could not be checked* / *Skipped*), level (UI/API), application URL, browser, tester model, the username used to log in, the number of steps, the reason when it could not run, *🔐 Tester needs a login* when the tester stopped to ask for a login, the tester's summary, the verdict per scenario, browser errors, then one window per `http_request` with the URL, request body and response body (the 4th window and later are folded). |
| Wrapper result | The reply the CLI received. |
| Session context from the Context Keeper | The summary of the conversation up to this thread (CLI threads only) — the planner's starting point at the next prompt in the same chat. Shows a few seconds after the thread finishes. |
| Time & tokens | The planner/reviewer/tester/wrapper models, the number of planner steps, the time of planning / real execution / execution if sequential / verification / unit tests / testing / wrapping / total, speedup, tokens per role (tester tokens include unit test triage). |
| Activity | This thread's log, including every planner tool call and every validator rejection. |

## Project

The list of codebases AIT knows. One project = one root folder, i.e. a folder holding `.git` or a build file (`.sln`,
`pom.xml`, `package.json`, `go.mod`, etc.), whatever its language.

- **Automatic registration**: the first prompt from a session inside a project registers its project. Sessions in a
  folder without a marker are ignored (see [Usage](04-penggunaan.md#sessions-outside-a-project)).
- **Browse…**: opens a folder browser without copying paths:
  - location shortcuts: drives, *Home*, `source\repos`, and the parent folders of recently used projects;
  - the folder list — click to enter, *↑ Up* to go back, or type a path and press Enter. Project folders are marked with
    their stack (e.g. <kbd>.NET</kbd>, <kbd>Java (Maven)</kbd>, <kbd>Node.js</kbd>) or <kbd>git</kbd>;
  - *Projects in this folder*: the current folder when it is a project root, with the status *Registered* / *New*;
  - *Scan*: search every sub-folder to a depth of 2–8 (build, VCS, hidden and junction folders are skipped; a project is
    not scanned deeper; stops at 300 results, 50,000 folders or 20 seconds). *Select all unregistered* ticks every new
    result;
  - tick one or several projects → **Register selected**.
  The last opened folder is remembered by the browser for the next visit.
- **Register manually**: paste a folder path → *Register*. A folder with its own marker is registered as it is; a path
  inside a project registers that project; a folder with no marker at all can still be registered (sessions inside it
  are then orchestrated and have memory). Drives, the Home folder and system folders are refused. A file path is
  accepted too (its folder is used).
- Registration is needed before applying a per-project CLI integration.
- Columns: name & root (with the **project key** 🔑), **AIT**, stack, number of threads, CLI sessions, memories, last
  used. A *folder missing* badge shows when the project folder no longer exists; a *local key — not shared yet* badge
  means this project's memory cannot be shared with other developers yet (see
  [Project key](06-memory.md#sharing-memory-between-developers-project-key)).
- **AIT switch**: on = `<project>/.claude/settings.local.json` points Claude Code to this proxy. Turning it on writes the
  `env` block (the same as the per-project Claude Code switch in the [CLI](#cli) menu); turning it off removes AIT's
  keys only, other keys in the file stay. An *other URL* note means the file points to another proxy address (e.g. an
  old port) — turn it on again to update it. Open a new Claude Code session after changing it.
- **Delete**: the thread history stays (without a project). Memory that only exists on this machine is deleted too.
  Memory in a shared store (an external database) is kept for other developers, unless you agree to the second
  confirmation *also delete it for ALL developers*. When another clone of the same repository is still registered, its
  memory moves to that clone.

**Project detail**: the **AIT permissions** card (the *always* permissions given on this machine: kind, target, given
by, last used, a *pattern* / *all commands* badge, a *Revoke* button, and the *Add a command permission* form for an exact command or a
pattern like `npm install *`; see [Developer permission](04-penggunaan.md#developer-permission)), the stack, the
**Project key** card (the effective key, its source, and the *Manual key* field), the
**Git** card (git repositories only: the automatic `git fetch` interval in minutes (empty = the default 15, 0 = off),
the last fetch and its error, the number of threads followed, the *Fetch now* and *Sync* buttons; see
[Git state of a thread's change](07-cara-kerja.md#git-state-of-a-threads-change); then the team's shared
**Environments**: name, ref, *prod* mark, number of changes that reached it, *Save environments* and *Check now*; see
[Environments](07-cara-kerja.md#environments); then *Commits made outside AIT*: on/off (costs tokens), ref, commits per
pass, how many summarized/skipped/failed, the newest commits, *Summarize now*; see
[Commits made outside AIT](07-cara-kerja.md#commits-made-outside-ait)), the solution/build files found in the
project, CLI sessions (session id, number of threads, last active), this project's
threads, and a link to its memory. The session, thread, module, code map and module merge history tables show 10 rows
per page (*‹ Previous* / *Next ›*; the navigation only shows when there are more than 10). The open page stays when the
data refreshes automatically; changing the search or the code map filter goes back to page 1. Below the threads:

- **Code map**: the module, layer, area and source (rule/AI/manual) of each file, with a search and a module filter.
  *Change* / *Set file* (a path or a class name) stores a manual correction; *Scan* maps the files that are not mapped
  yet.
- **Module merge history**: automatic and manual merges with the number of memories/files/pages moved; *Undo* reverts
  them.

Below both columns, across the page:

- **Modules**: the project's module list (description, aliases, number of memories and files; click the file count to
  filter the code map). *Manage* renames (including upper/lower case), changes the description/aliases, or merges the
  module into another module; *Check duplicate modules* looks for modules split by accident. See
  [Module list & merging modules](06-memory.md#module-list--merging-modules).

### Unit tests

The **Unit tests** card (below Modules) shows this project's [unit test catalog](07-cara-kerja.md#unit-tests). The
catalog and its settings are shared by every developer of the project (through the project key) and stored together
with long memory.

- The info line: the mode, the rule for new tests, the recognized runners with their problems (e.g. *run npm install
  first*), and a warning when there is no active Tester node yet.
- A table with 15 rows per page: the `UT-…` number, the test title and file (the obsolete/quarantine reason below it),
  the module, the status (*Active* / *Obsolete* / *Quarantined* / *Removed*), the last result (when, by whom, duration,
  part of the failure message or the reason it was skipped), and the origin (*Made by AIT* / *Code scan* / *Rewritten*).
  Search number/title/file, filter status (including *Last failed*) and module.
- Click a title for the detail: purpose, scenario, targets, needs, selector, the origin thread, and the run history
  (before/after a change, manual, flaky; machine and developer). The content of the test file (the snapshot) is never
  shown.
- **Scan tests**: looks for tests in the code, registers new ones, follows the ones that moved file, and gives them a
  title, purpose and scenario with a cheap model. **Run all** / **Run** per test: runs it now (including a chosen
  quarantined test) and records the result in the catalog.
- An *Obsolete* test: **Restore** (active again) or **Confirm removal** — the status becomes *Removed*, the snapshot is
  deleted too, the test is never restored again, and a Dashboard thread removes its test code (it can be rolled back
  from that thread's page). A *Quarantined* test: **Activate**.
- **Settings**: *Tests run in every thread* (*Relevant only* — default, *All active tests*, *Off*), *Create new unit
  tests* (*Only when asked* — default, *Automatic for business logic changes*, *Never*), *Keep test snapshots* (on by
  default; turning it off deletes the existing snapshots), and *Test run time limit* (10–3600 seconds; empty = follows
  *Build timeout*).
- The card updates itself when a thread of this project finishes.

### Test accounts

The **Test accounts** card (below Unit tests) holds the accounts the Tester node uses to
[switch users](07-cara-kerja.md#test-accounts-switching-users) while testing, for example the requester, the manager,
the approver per level and the admin. Accounts are shared with every developer of the project through the project key;
without a project key the card explains that accounts cannot be saved yet. Every developer of the project may change
them.

- The table: username, role, attributes (`NIK: 123`, `Manager: atasan01`, …), notes, and who changed it last. **Add
  account** / **Change** opens a form with dynamic attributes; a new account starts with the attribute keys already used
  by other accounts in the project, so the columns stay consistent. Use test accounts, not real employee accounts.
- **Dev password**: empty = the default `Test@1234`. A value filled in (here or through the chat reply
  `dev password <value>`) is visible to every developer of the project, so it is only for a bypass password in dev.
- **Login as / impersonate**: the detection result (available/none/unknown, how, how to return, roles, the evidence
  file:line, source, confirmed or not). **Detect** runs the code scan + a cheap model again; **Change** fills it in or
  corrects it yourself and can mark it *checked*.

### Test scenarios

The **Test scenarios** card (below Test accounts) holds the project's
[multi-step scenarios](07-cara-kerja.md#multi-step-scenarios-excel): title, file name (slug), version, form columns,
number of steps, roles with their accounts (missing ones are marked), the last result, and a link to the origin thread.

- **Excel** downloads the scenario file to fill in; **Upload Excel** reads a filled file (wrong rows are shown per row
  number; new usernames become project test accounts).
- **Run** (active when every role has an account) asks to confirm the side effects, then opens the origin thread page
  where its progress shows. **Delete** deletes the scenario for every developer of the project.

### Code structure

At the very bottom, the **Code structure** window shows the project's content without compiling and without extra
dependencies. The planner uses the same data: the `code_outline` tool gives the skeleton of a file/folder/class before
the planner reads a file, and files the prompt names are outlined in the planner's brief right away (see
[How it works](07-cara-kerja.md#3-planner)):

- **Folder**: folder → file → class/type → member (method, constructor, property, event) and functions outside a class.
  **Class**: namespace (or folder, for files without a namespace such as old PHP and plain JavaScript) → class → member.
  A C# `partial` class spread over many files shows as one class; its file list is folded in *Declared in N files*.
- Click a declaration row to copy `path:line` (can be pasted straight into a prompt). The search looks up class, member
  and file names (top 100 results). The Folder/Class choice is remembered by the browser.
- **Function calls** (`N calls`): what a method/function calls, e.g. `p_InitForm` → `p_InitiateDetail`,
  `p_initiateData`; `p_initiateData` → `POST /Transaction/KlaimPending/InitiateData` → `KlaimPendingController.InitiateData`.
  Click a function to open its list; each call points to the file and line of its declaration (click to copy), or shows
  dimmed as *not found in the project yet*. See [Function calls](07-cara-kerja.md#function-calls) for when and how they
  are read.
- C# is read with Roslyn (syntax only, exact). PHP, JavaScript/TypeScript, Python and Java/Kotlin are read line by line
  with patterns, and the content of a class is decided from the indentation. Suits tidy code; closures and anonymous
  classes are not counted. Other files show as files only.
- Not read (marked *third party*): locations listed in `thirdpartylibs.xml` (Moodle and its plugins),
  `*.min.js`/`*.min.css`/`*.bundle.js`, the `amd/build` and `yui/build` folders, files over 2 MB, and minified
  JavaScript. Folders ignored everywhere in AIT (`bin`, `obj`, `node_modules`, `vendor` for PHP, etc.) do not show.
- The project is scanned when the window opens, then again when the last result is older than 1 minute or on *Scan
  again*. Besides that, every thread that finishes changing files (also threads cancelled/failed halfway, and rollbacks)
  rescans its project in the background without holding the thread. There is one queue per project: a new request
  cancels the previous scan (waiting or running) and takes over, with a 1-second pause so threads from several CLIs that
  finish at almost the same time end in one scan. Files with the same size and modification time reuse the previous
  result, so a rescan usually takes less than 1 second (the first scan ±1 second for ±1,100 C# files, ±2–4 seconds for
  ±14,000 PHP/JS files). Results are kept in the server's memory for the last 4 opened projects; at most 100,000 files
  per project.

## Memory

See [Memory](06-memory.md) for the concepts. The page has two submenu items: **Short & long memory** (with the foldable
*Needs review* part below the stat cards; `/Memory#review` opens it right away) and **Business memory** (`/Memory#bisnis`). As long as old memories wait for labels, the *Memory
label migration* banner shows the progress (waiting, labeled, needs review, the phase with the project and the number of
files being mapped, the backup location) with a *Run now* button.

The *Short & long memory* tab:

- **Statistics**: the number of short & long memories, project & knowledge, unlabeled (and needing review), the status
  of the short memory store.
- **Filters**: level, type, project, a search in label/description, module (the chosen project + knowledge), layer,
  kind, labeled/unlabeled, and *In &lt;environment&gt;* (a chosen project with environments: only memory whose change
  reached that environment). Coming from a project page? The project filter is already filled in.
- **Table**: level (short shows the estimated hours left before it moves to the DB), type, project, the label as one
  segmented tag `Module | Other module | Layer | Kind` followed by `#keyword` (old memory: an *unlabeled* mark + its old
  label) and an excerpt of the description, the creation time & a link to the source thread, the use count (long
  memory).
- **Actions**: *Open* (view/change), *→ DB* (move a short memory to long now), *Delete*.
- **Add memory / Open**: type, project, module (a dropdown of the module list; *＋ New module…* adds a new name created
  when saving; *Rename* renames the chosen module in every memory, the code map and the wiki), other modules (optional,
  at most 3, chosen from the same dropdown and shown as chips), kind, area (optional), layers (tick boxes), keywords
  (comma-separated, at most 16, counted), description. Knowledge memory is not bound to a project and may only be of
  kind Pitfall/Pattern/Architecture. New entries come in as short memory.
- **Consolidate now**: moves every short memory past its retention time.

The *Needs review* part (shown when something needs review; its count badge also shows on the *Short & long memory* submenu item):

- **Memories without a label**: memories that could not be labeled automatically (unclear module, or failed 3 times) —
  *Label it* opens the form.
- **Module merge suggestions**: modules that are probably the same but with low confidence — *Merge* or *Not a
  duplicate* (not suggested again). Choose a project to see them.
- **Possible duplicate memories**: pairs of memories with the same module & kind and very similar keywords, side by side
  with *Open* and *Delete* buttons. Nothing is deleted automatically.

The *Business memory* submenu item — see [Business memory](06-memory.md#business-memory-business-wiki):

- **Project**: choose a project; the info line shows the project key, the number of threads waiting to be summarized,
  the time of the last summary, and the store in use.
- **Modules**: the page list grouped per module, with a search. 🔒 = a hand-curated page (not changed by AI).
- **Page**: the wiki content (tables, numbered rules, screenshots), version, who changed it last (AI/manual). *Updated
  by AI* locks/unlocks the page from the AI wiki writer; *History* shows every version and can restore one of them;
  *Edit* and *Delete*.
- **New page / Edit**: module, title, summary, labels, Markdown content with *Preview*. Paste a screenshot (Ctrl+V) or
  *Insert image* — the image is uploaded to the store and inserted at the cursor. When the page was changed by someone
  else since you opened it, saving is refused (the editor stays open) so nobody's change is overwritten.
- **Update now**: summarizes this project's threads that are not in the wiki yet without waiting for the schedule.
- **Environment**: *Working wiki* (the default), a registered environment, or a remote branch (automatic, the *Remote
  branches* group) — reads the wiki as it holds in that
  environment (read-only); see [Wiki per environment](06-memory.md#wiki-per-environment).
- **Compare**: choose two sides (environments or the working wiki) → the pages that differ (line diff), the pages on one
  side only, and the changes on one side only with their author, branch and commit.

## Node

Seven columns: Planner, Tools Node (optional), Executor, Reviewer (optional), Tester (optional), Wrapper, Context Keeper
(optional). Every card shows the model, temperature, max tokens, max parallel, priority, a *📝 instructions* mark when
there are extra instructions (their content in a tooltip), a *🧩 n* mark when the node uses n skills (their names in a
tooltip), a *🛰 n tools* mark on a planner that delegates tools to a [tools node](02-konfigurasi.md#planner-tools-node)
(on a tools node card: *🛰 n planners* that use it; details in a tooltip), a *🧪 auto test* mark on a planner with
[Auto Testing](02-konfigurasi.md#auto-testing-tester-node) on (*🧪 no tester* when there is no active Tester node yet), a
*↪ name* mark when the node has a [fallback node](02-konfigurasi.md#fallback-node), with *Test*, *Edit* and *Delete*
buttons. In the Planner node's modal, the *Delegate tools to a tools node* part holds one choice per read tool (including the read-only `run_command` and `web_fetch`) (*Planner
directly* or a tools node), and the *Auto Testing* switch (with the name of the tester that will be used). In the node
modal, the *Skills* part shows the attached skills as chips. The *Search the store yourself* field opens a dropdown of
skills matching their name, description, tags, source or content (the matching part of the content is shown); click an
item to attach/detach it. The *What ability is needed?* field + **Find skills** asks the Skill Orchestrator to find
matching skills in the store (see [Skills on a node](07-cara-kerja.md#skills-on-a-node)). When a project has a unit test
catalog but there is no active Tester node, the pipeline status shows the warning *There is a unit test catalog, but no
active Tester node* (failing unit tests are then fixed in the code without triage). The *Guide* panel above the columns
explains what the numbers mean; the panel can be folded and its state is remembered by the browser. *Reload 9router
models* refreshes the model list (60-second cache). The field details are in
[Configuration](02-konfigurasi.md#node-menu).

Below them, the *Background task models* part holds two columns that are not pipeline nodes: **Wiki Writer** (the
business wiki writer: model, update automatically, interval, developer name — set only here) and **Skill Orchestrator** (model, skill character limit
per node). Their cards show the chosen model or *automatic → the active Planner node model*; **Edit** saves through the
same API as [Setting](#setting) / [Skill](#skill) (`/api/business/settings`, `/api/skills/settings`), so both menus
always show the same
choice. The *Store* button (Skill Orchestrator) opens the [Skill](#skill) menu.

## Skill

Its own sidebar menu (`/Skill`; formerly the Setting → Skill tab, old `/Setting#skills` links land here). The
**Skill Orchestrator** card holds the model and the skill character limit per node (saved with **Save Skill**; the same
model can also be chosen in the [Node](#node) menu). The **Skill sources** card: paste a link → **Pull skills**, live
status, *Sync* / *Delete*. The **Skill store** card: the skill list with a search, the number of nodes using each, and a
content preview (**View**). The field details are in [Configuration](02-konfigurasi.md#skill-menu); how it works is in
[Skills on a node](07-cara-kerja.md#skills-on-a-node).

## CLI

Points coding CLIs to the AIT proxy with one switch, without editing the CLI's own configuration files. The available
cards: **Claude Code**, **oh-my-pi (omp) — provider AIT** (`models.yml`), **Codex CLI — profile AIT** (`config.toml`,
used with `codex --profile aithread`), **Gemini CLI** (`.gemini/.env`), **opencode — provider AIT** (`opencode.json`),
and **Qwen Code — model AIT** (`settings.json`). The "provider/profile/model AIT" cards are global only and do not
redirect other sessions: that CLI gets an `aithread` choice next to its original provider.

- **AIT proxy URL** at the top: the address written to the CLI configuration (default = the application's http
  address). Changing it refreshes the status of every switch.
- Each CLI has one card holding:
  - **Global** — that CLI's configuration file at the user level (`~/.claude/settings.json` for Claude Code,
    `~/.omp/agent/models.yml` for omp, `~/.codex/config.toml`, `~/.gemini/.env`, `~/.config/opencode/opencode.json`,
    `~/.qwen/settings.json`). Turning on Claude Code or Gemini CLI globally asks for confirmation because all their
    sessions then go through AIT.
  - **Per project** — one switch per registered project (`<project>/.claude/settings.local.json` for Claude Code,
    `<project>/.gemini/.env` for Gemini CLI; other CLIs are global only). An *On*, *Off* or *Points to another address*
    badge (the file points to another URL, e.g. an old port — turn it off and on again).
  - *What will be written*: a preview of the content that will be written.
- Turning a switch off only removes what AIT wrote; the rest of the file stays. A backup of the original file is kept
  once as `*.aithread-backup`. AIT refuses to write (with a message) when the file already holds an entry with the same
  name made by you, or when the opencode/Qwen Code JSON holds comments.
- The Claude Code card also holds **Context Keeper for /clear** (*Install hook* / *Remove hook*; see
  [CLI integration](03-integrasi-cli.md#context-keeper-for-clear)).
- When the *Proxy API key* in Setting changes, turn it off and on again so the new key is written too.

Details per CLI: [CLI integration](03-integrasi-cli.md).

## Setting

The page is split into tabs. The open tab is recorded in the URL (e.g. `/Setting#telegram`), so it stays the same after
a refresh and can be linked directly.

- **9router connection** (`#router`) — URL, API key, *Test connection* (does not save); the status line shows whether
  the key is stored and accepted.
- **Proxy & orchestration** (`#proxy`) — the orchestration switch, progress & *Planner may ask*, *The planner may run
  commands* (`run_command`, also `start_process`) with its timeout, *The planner may read web pages & APIs*
  (`web_fetch`), *AIT may commit & push when asked*, the passthrough model,
  the proxy API key (with a link to the [CLI](#cli) menu, where the CLI integrations (Claude Code, omp, Codex, Gemini CLI,
  opencode, Qwen Code) and the Context Keeper hook are set), the planner/executor/parallel/timeout limits.
- **Build & verification** (`#build`) — *Automatic build & verification*, the build timeout, and the number of fix
  rounds.
- **Memory storage** (`#memory`) — the provider of short, long and business memory (built-in, Redis for short only, SQL
  Server, PostgreSQL, MySQL, Oracle, SQLite) with host/port/database/username/password, *Test connection* per card, a
  live status, and the option to copy the old memory. Saved with **Save memory storage**. The business wiki writer
  (automatic, interval, model, developer name) is set in the [Node](#node) menu → Wiki Writer column.
- **Telegram** (`#telegram`) — the bot token, on/off, a live connection status, and the chat table (*Allow* / *Delete* /
  *Revoke*).
- **Security & privacy** (`#keamanan`) — the secret masking switch and passthrough request masking, counts since AIT
  started (masked, can be restored, restored, credential file access refused), an example of `.aithread/project.json`
  rules, and a button that opens the full explanation. Saved with **Save security settings**
  ([Security & privacy](12-keamanan.md)).

The first three tabs are one form: **Save settings** on any of them saves the content of all three at once, and an
invalid field opens its own tab. The Telegram, Skill and Security & privacy tabs are saved separately with their own
buttons.

## Security & privacy (popup)

When AIT is opened for the first time, the **Security & privacy** popup explains what AIT does with your secrets and
data: masking before a model sees them, restoring them when files are written, blocked credential files, what is stored
and sent, and its limits. This popup only shows once per installation (recorded in the database, not in the browser),
and can be opened again with the **Security & privacy** button in the sidebar's Help group.

Details in [Configuration](02-konfigurasi.md) and [Prompts from Telegram](11-telegram.md).

## Log & diagnostics

Where to see what went wrong, and to bring a client's logs to whoever maintains AIT. Open it through *Log & diagnostics*
in the sidebar's *Help* group, or the *📋 Log* button of a thread detail (`/Log?threadId=…` narrows incidents and the
activity log to that thread).

| Tab | Content |
|---|---|
| Incidents | Errors AIT recorded with their **full stack trace** (inner exceptions included) and **companion files**. The same error within 10 minutes is folded into one (the *Times* column). Click a row for the detail: message, stack trace, companion files (*View* / *Download*), *Copy title + stack trace*, and *Delete incident*. Updates live; has a search (title, category, message, stack trace) and *Delete all*. |
| Activity log | The Dashboard log from the database: level filter, the last 200/500/1000 entries, search; the thread id on a row opens that thread's log. |
| Log files | The `AIThread.exe` log files (menu/tray mode) and `9router.log` in `%LOCALAPPDATA%\AIThread\logs`: the last 500/2000/5000 lines, line filter (continuation lines such as stack trace frames stay with their line), FAIL/ERROR lines red and WARN yellow. When AIT runs with `dotnet run`, the log goes to the console and there is no file. |

**What becomes an incident.** Every warning/error logged with an exception (background workers, the API, a crashed
thread such as *Internal error: An error occurred while saving the entity changes*, with its inner exception), except
cancellations. A failed executor task also becomes an incident of category `executor` with companion files: `task.md`
(file, node/model, error per attempt, instruction), `task-message.md` (the task message sent to the model), and
`attempt-<n>-output.txt` / `attempt-<n>-feedback.txt` (the model's raw output and AIT's answer per attempt). The task
error on the thread detail names its incident id. Incidents are kept as files in `%LOCALAPPDATA%\AIThread\diagnostics`
(at most 300, 30 days), apart from the database, so they can be exported even when the database fails.

**Export ZIP.** The *Export ZIP* button picks the period (1/3/7/30 days), the content (incidents, activity log, log
files) and optionally one thread id, then downloads `ait-diagnostics-<machine>-<time>.zip`:

```
README.md                      AIT version, OS/.NET, period, content
activity.log                   the database activity log, oldest first
logs/aithread-*.log            the AIThread.exe log files and 9router.log
incidents/<id>/incident.md     error + stack trace, ready to read
incidents/<id>/incident.json
incidents/<id>/files/*         companion files
```

Passwords, API keys, tokens and connection-string parts become `[secret:<label>]`; code, file paths and prompts can
still be inside. From a terminal: `AIThread.exe logs export [file.zip] [--days n]` — through the running instance (with
the activity log), or straight from the folders when AIT does not run (incidents and log files only).

## Docs

This documentation, shown in the application. The left column holds the document list (with a title search) and the
table of contents of the open page; the buttons below the page move to the previous/next document. Links between
documents and anchors (e.g. `#node-menu`) work like on GitLab. The content comes from the `docs/` folder (`docs/en/` in
English, falling back to the Indonesian page when a page is not translated) — a file change shows right after a
refresh.
