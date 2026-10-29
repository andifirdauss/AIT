# 8. Troubleshooting

The first step for almost any problem: open **Dashboard → Live log** (choose *All (debug)*) or the thread detail page →
*Activity*.

Messages are shown in the [display language](02-konfigurasi.md#display-language); the table below gives their English
text.

## 9router

**The 9router card is *Offline*: "No connection could be made…" / "Timeout (5 seconds)"**
9router is not running or the URL is wrong. Click **Start 9router** on the Dashboard (or run `9router`), make sure the
dashboard opens at the same URL as Setting → Base URL, then *Test connection*.

**AIT does not start 9router on start**
See the log entries of the *9router* category on the Dashboard. Common causes: the Base URL is not a local address;
*Node.js was not found* (install Node.js 18+); *9router is not installed yet* (click **Start 9router**, or set
`AIThread:NineRouter:AutoInstall`); *Port … is already in use* by another program (change the 9router port and Base URL,
or stop that program); or 9router stopped while starting, with the details in `%LOCALAPPDATA%\AIThread\logs\9router.log`.

**"9router HTTP 401 for model '…': Missing API key" while Setting looks connected**
No API key is **stored** yet. *Test connection* only tests the key typed in the form without saving it, and the *Online*
status only proves 9router is alive (the health check and the model list need no key). Fill in the API key in Setting →
click **Save** → make sure the status line shows `API key OK`. The 9router card on the Dashboard shows *Online · check API
key* as long as the key is not right.

**"The API key was rejected by 9router" / "Invalid API key"**
The key is wrong or was deleted in 9router. Copy it again from the 9router dashboard, fill it in in Setting, click
**Save**.

**The 9router key "disappears" after moving to another computer/Windows account**
The key is encrypted with that Windows account's Data Protection. Fill the key in again in Setting.

**"The provider account for model '…' in 9router needs to log in again (OAuth access token has expired …)"**
9router is alive and AIT's API key is accepted, but the provider account behind that model (e.g. a Claude subscription)
has logged out: the OAuth token expired or the provider key was revoked. 9router passes it on as HTTP 503 holding
`authentication_error` / `[401]`. AIT does not retry (waiting does not help) and switches straight to the
[fallback node](02-konfigurasi.md#fallback-node) when there is one. Open the 9router dashboard → Providers, reconnect
that account, then **Retry failed** in the thread detail.

**"9router HTTP 4xx/5xx for model '…'"**
The model is not available or its provider has a problem. Click *Test* on the node concerned; choose another model/combo
when needed. Temporary errors (429, 5xx, connection) are already retried automatically twice before they are reported.
For 429, the `Retry-After` header from 9router is honoured (at most 30 seconds; longer is not waited for), without the
header the pauses are ±5 and ±10 seconds. When an executor task still fails (e.g. `upstream_503` *The model is having
problems*), wait a moment, then reply `retry` in the chat (or **Retry failed** in the thread detail): only the failed
tasks run again, without the planner. So you do not have to retry by hand, set up a
[fallback node](02-konfigurasi.md#fallback-node) for the executors.

**The planner hits 429 / a rate limit**
Usually because the provider account (e.g. a Claude subscription in 9router) runs out of its per-minute quota — every
planner step sends the whole conversation again, so input tokens pile up fast. Most of it is served from the prompt cache
(see [Planner token cost](02-konfigurasi.md#planner-token-cost)), but a provider's per-minute quota may still count every
input token: one `cc/claude-opus-…` planner thread with 10–20 steps can use 0.2–1 million tokens. The planner token
column in the thread detail shows the real usage with its cache share. Solutions:
- Add a **second** Planner node (another model/provider; it may be inactive) and choose it as the *Fallback node* in the
  main planner's form. AIT switches to that node and continues the investigation; a `⚠️ Planner … switching to …` line
  shows in the progress. The same works for executors, reviewers and wrappers (see
  [Fallback node](02-konfigurasi.md#fallback-node)).
- A thread that already failed does not need to start over: reply `retry` in the chat, or open the thread detail →
  **Retry failed**.
- Or make the planner model a 9router **combo** (e.g. Claude → another model) so the fallback happens in 9router.
- Reduce the planner node's slots when several threads run at once, or lower Setting → *Planner steps*.

**The footer does not show *Quota left***
- A 9router on another computer with *Require login* on (the 9router default): the `/api/usage` API refuses AIT, and the
  AIT log writes *Provider quota cannot be read* once. Turn *Require login* off on that 9router. A 9router on the same
  computer needs no change because AIT logs in with the local CLI token. When the log says *rejects the local CLI token*,
  restart 9router (or turn *Require login* off).
- The node's model comes from a plain API key account (e.g. a *compatible* provider) or a provider whose quota 9router
  cannot read — this part is only for subscriptions/OAuth that 9router shows on its *Usage* page.
- 9router does not answer within 10 seconds; the footer is sent without the quota. The next thread tries again.

**"Model '…' did not respond within N seconds"**
Raise Setting → *Timeout (seconds)*, or use a faster model.

## The CLI does not go through AIT

Answers come without the `**AIT** · …` line and no new thread shows in the Dashboard:

1. Open a **new CLI session** — the configuration is only read when a session starts.
2. CLI menu → your CLI's card: the switch must be on with the *On* badge (not *Points to another address*).
3. Make sure the session uses the AIT provider/model: Codex is run with `codex --profile aithread`; in oh-my-pi, opencode
   and Qwen Code the AIT model is chosen with `/model`/`/models`; Gemini CLI uses *Use Gemini API Key* in `/auth`. See
   [CLI integration](03-integrasi-cli.md).
4. For the per-project scope, make sure the CLI runs in that project folder. Claude Code reads
   `.claude/settings.local.json` from the folder it runs in (see the path in the status box); when it differs, copy the
   `env` block to the folder where you run `claude`. Gemini CLI reads only one `.env` — a project's own `.env` can hide
   the global switch.
5. Make sure no Windows environment variable overrides it (e.g. `ANTHROPIC_BASE_URL` for Claude Code,
   `GOOGLE_GEMINI_BASE_URL`/`GEMINI_API_KEY` for Gemini CLI).
6. Setting → *Orchestration on* must be on. When it is off, requests still go through AIT but are forwarded without
   orchestration (visible as the debug log `Passthrough (orchestration disabled)`).
7. Look in the log for the line `Session xxxxxxxx (…) is not part of a project (…)`. It means the session is **ignored on
   purpose** because its working directory is not part of a project — see [Project](#project).

The debug log `Passthrough (auxiliary request without tools)` for session title/compact requests is normal.

## Errors shown in the CLI

| Message | Cause | Solution |
|---|---|---|
| `authentication_error: Invalid AIT proxy key` | The key the CLI sends (e.g. `ANTHROPIC_AUTH_TOKEN` in Claude Code) differs from the proxy API key. | Apply the integration again after changing the key, then open a new session. |
| `AIT: the passthrough model is not set…` (503) | There is no passthrough model and no active Wrapper/Executor node. | Fill in *Passthrough model* or activate a node. |
| `❌ AIT failed: No active Planner node yet…` | The pipeline is not complete. | Add/activate nodes in the Node menu. |
| `❌ AIT failed: The planner did not produce a valid plan within N steps.` | The planner ran out of steps or kept sending plans the validator rejected. | See the rejection reasons in the thread's *Activity*. Raise *Planner steps*, use a stronger planner model, or make the prompt clearer. |
| `❌ AIT failed to start the thread: …` | A problem before the thread was created, usually the database or a project deleted while the session ran. | Check the Database card on the Dashboard; register the project again when needed. |
| CLI timeout | The thread took longer than the CLI's time limit. | Raise that limit (Claude Code: `API_TIMEOUT_MS`; Codex: `stream_idle_timeout_ms`; Qwen Code: `QWEN_STREAM_MAX_LIFETIME_MS`), or split the prompt. |

## Project

**A prompt is not orchestrated: the log `Session … is not part of a project (…)`**
The session counts as outside a project, so the prompt is forwarded directly and no memory is made. The reason in
brackets shows the cause:

- *there is no project marker*: there is no `.git` or build file (`.sln`, `pom.xml`, `package.json`, `go.mod`, etc.) in
  the working directory or its parent folders (up to 6 levels). This is normal for an empty folder or a notes folder;
- *this folder holds the projects a, b, …*: the working directory only holds several projects, none is chosen because it
  is ambiguous. Open the session in one of them;
- *this folder is too broad*: the working directory is a drive, the Home folder or a system folder;
- *unknown working directory*: the client does not send it; set the `X-AIThread-Cwd` header.

Solution: run the CLI in the project folder, add `.git` (`git init`) or a build file to that folder, or register the
folder with *Register manually* in the Project menu (even a folder without a marker works). You can also apply the
*per-project* integration (which sends the `X-AIThread-Project` header). An ignored session is checked again after about
5 seconds.

**Manual registration: "Folder … not found" or "This folder is too broad to be a project"**
The path is wrong, or the folder is a drive/Home/system folder. Fill in the path of the project folder, or use
*Browse…* → *Scan* to find it.

**Scan: "stopped early (time/count limit)"**
A scan is limited to 300 results, 50,000 folders and 20 seconds. Go into a more specific folder or lower the depth.

**A folder does not show in the browser**
Hidden/system folders, folders starting with `.` or `$`, build folders (`bin`, `obj`, `node_modules`, …), and
junctions/symlinks are hidden on purpose. Type its full path in the path field and press Enter.

**The wrong project is detected**
The root is chosen from the nearest repository root (`.git`), then the outermost aggregating file, then the nearest
manifest (see [Session check](07-cara-kerja.md#2-session-check-project-or-not)); a registered project that contains the
folder comes first. When the root you want is different (e.g. one module of a monorepo as its own project), register
that folder with *Register manually*: a folder with its own marker is registered as it is, and sessions inside it use it.
The per-project integration (the `X-AIThread-Project` header) also locks its folder.

**A *folder missing* badge in the Project menu**
The project folder was moved/deleted. Register the new path; delete the old entry when it is not needed (remember: the
project's memory is deleted with it).

**An old project shows twice or merged after an update**
A project's identity moved from the `.sln` file to the root folder. An automatic migration merges two solutions in the
same folder into one project (the last used one), with its threads and memory.

## Executor tasks

| Message on the task | Meaning | Solution |
|---|---|---|
| `the SEARCH text was not found…` | The executor did not copy the file text exactly, even after retries. | Raise *Executor retries*, use an executor model that follows the format better, or raise `ExecutorFullFileMaxLines` for large files. |
| `the SEARCH text occurs N times…` / `match several places` | The SEARCH snippet is too short, so it is ambiguous. | Usually solved by a retry; otherwise the same as above. |
| `Output was cut off (max tokens)…` | The output exceeded the node's *Max tokens*. | Raise the executor's *Max tokens* (especially for `create` tasks). |
| `No valid SEARCH/REPLACE block in the output.` | The model answered outside the format. | Change the executor model; add an instruction in the node's *Extra instructions*. |
| `Model '…' returned an empty answer through 9router` | The provider behind that model answered without text and without a tool call (the output went into *reasoning*, the account has a problem, or max tokens ran out on reasoning). It is not retried automatically because it usually repeats; *Test* in the Node menu fails with the same message too. | Choose another model or set up a [fallback node](02-konfigurasi.md#fallback-node) (used right away), check its provider in 9router, then `retry`. |
| `Dependency did not succeed: T3` | The task was skipped because a task it waits for failed. | Fix the failed task, then repeat this part with a new prompt. |

A failed task writes nothing to a file (edits are all-or-nothing).

## Build & verification

| Symptom | Meaning | Solution |
|---|---|---|
| `the build cannot run — 'dotnet' could not be started…` | The project's toolchain (`dotnet`, `node`/`npm`, `go`, `cargo`, `mvn`) is not on the AIT process's PATH. | Install the toolchain or add it to PATH, then restart AIT. The standalone exe needs no .NET SDK to run, but building a .NET project still needs it. |
| `compilation succeeded, but an output file is locked…` | `MSB3021`/`MSB3027`: a running application (e.g. the project you are debugging) locks files in `bin/`. The code compiled, so verification counts as passed. | No action needed. Stop the application when you want the latest output copied. |
| `the build exceeded the time limit…` | The build took longer than *Build timeout*; it is stopped and not repeated in the next round. | Raise *Build timeout*. The first build (package restore) is usually the longest. |
| A slow build in every round | The restore waits for an unreachable NuGet feed. | AIT already turns off the vulnerability audit (`NuGetAudit=false`); check `nuget.config` when the restore still waits for a feed. |
| `… — in a file this thread did not change` | An error in a file the thread did not change, which may have existed before; not fixed automatically. | Ask for it explicitly: "build and fix all errors". The planner will call `run_build` and plan the fixes. |
| `The build failed without a recognisable error message` | The build output holds no recognized error format (often with a custom `npm run build` script). | See the output in the *Verification* card of the thread detail. |
| Verification *Issues remain* after every round | The executors did not manage to fix it within *Fix rounds*. | Continue with the next prompt (the planner sees the earlier thread), raise the fix rounds, or use a stronger executor model. |
| The reviewer is too fussy or triggers changes that were not asked for | The reviewer model reports things outside the request. | Add limits in the Reviewer node's *Extra instructions*, change its model, or deactivate that node (verification still uses the build). |
| Verification does not show | No task succeeded, *Automatic build & verification* is off and there is no Reviewer node, or the project's stack has no build command. | See [Verification](07-cara-kerja.md#5-verification-build-and-reviewer). |

## Unit tests

| Symptom | Meaning | Solution |
|---|---|---|
| Unit tests never run / the Unit tests card is empty | The project has no project key yet, the catalog mode is *Off*, or there is no recognized suite (see [Recognized runners](07-cara-kerja.md#recognized-runners)). A long memory store that does not answer within 10 seconds also makes that thread skip its unit tests (recorded in the application log). | Check the Unit tests card in Project > Detail; press **Scan tests**. Make sure the project has a key and the long memory store connection is healthy. |
| A suite marked ⚠️ `… is not in node_modules (run npm install first)` / `vendor/bin/phpunit does not exist` / `The test assembly has not been built yet` / `vstest.console.exe … was not found` | Its runner is not installed on this machine yet; AIT does not install packages. | Run `npm install` / `composer install` / build the solution once, or install Visual Studio (Test Platform) for an old .NET Framework project. |
| Every test of one suite is *Error: The tests cannot run (exit code …)* | The runner failed before running tests, usually because the test project cannot be built or its configuration is broken. The message and the end of the output are in the test detail. | Fix the test project's build (`dotnet test` builds it itself), then run it again. |
| A test is *skipped — Needs a database / a network connection / configuration …* | The test failed because this machine has no database, service or config file it needs; it is not counted as a regression. For the next 24 hours that test is skipped right away in threads without waiting for a timeout. | Normal for integration tests on a laptop without a DB. When the need is available, press **Run** in Project > Detail (it always tries again). A **new** test from the same thread is never skipped for this: it is fixed so it does not touch the database/network (see [Skipped tests](07-cara-kerja.md#skipped-tests)). |
| A test is *not reported by the runner* | The runner ran, but does not name that test: the method/test was renamed, moved, or the filter does not match. | **Scan tests** to follow the rename/move; an old test that really is gone shows in the scan result (`missing`) and can be marked obsolete through a thread. |
| `the test run exceeded the time limit` | The run took longer than *Test run time limit* (default = *Build timeout*). | Raise that limit in the project's unit test settings, or use the *Relevant only* mode. |
| Threads feel slower since there is a catalog | The baseline runs in parallel with the planner, but execution waits for it; the run after the build adds time per round. The *All active tests* mode runs the whole suite. | Use *Relevant only*. Results for the same code are reused (14 days) without running again. |
| A regression is not fixed: *the file to fix cannot be told without the tester's triage* | Without a Tester node, AIT can only guess the file to fix (the test's target file that changed, or the only non-test file that changed). | Add a Tester node (strong model, the `AIT Tester` preset), or fix it with the next prompt (*"fix UT-0012"*). |
| `Unit test regressions remain after 3 fix rounds` | Automatic fixes are limited to 3 rounds. | See the failure messages in the *🧪 Unit test* part; continue with the next prompt or fix it by hand. When the behaviour really changed on purpose, say so in the prompt so the planner decides `update`. |
| New test files show up in `git status` without being asked for | Catalog tests that do not exist in your repository yet (made by another developer and not pulled yet, or deleted) are restored automatically before the planner. The report names their origin commit/branch. | Commit those files. When you later pull a branch holding the same files, git refuses to overwrite untracked files: delete the restored copies (their content is the same) and pull again. When the test really was deleted on purpose, mark it obsolete and confirm its removal. |
| `UT-… not restored: no commit in this repository holds them` | That catalog test only ever lived in a working copy (another clone that did not commit it, a change that was thrown away, or a commit not fetched yet). Restoring it would break the build because the code it tests is missing too. | Commit and push the test from the clone it came from, then pull/fetch. When it really was dropped, mark it obsolete and confirm its removal so the note stops showing. |
| A test is *Rewritten (needs confirmation)* | There is no snapshot of the lost test (e.g. *Keep test snapshots* is off), so an executor wrote it again from the catalog scenario. | Review its test code before committing it; the result can differ from the original. |
| A test moves to the *Quarantined* status | The test failed then passed when repeated, or the Tester judged it flaky (depends on time, order, randomness). | Fix the test, then **Activate** it in Project > Detail. |

## Auto Testing

| Symptom | Meaning | Solution |
|---|---|---|
| The `🧪 Testing` phase does not show | *Auto Testing* is off on the Planner node in use, there is no active Tester node (the Node banner adds a note), no task succeeded, or the thread was made before Auto Testing was turned on. | Turn on *Auto Testing* in the Planner node's Edit and add a node with the Tester role. |
| `Testing · skipped` | The planner judged the change cannot be tried from outside (the reason is written), or the last build still fails. | Fix the build first; or say in the prompt which part must be tested. |
| The tester does not switch users / no `switch_user` | The project has no test accounts yet, or no project key yet. | Fill in accounts per role in Project > Detail → *Test accounts*. |
| `The application rejected the project dev password` | The application does not accept `Test@1234` (or the stored dev password). | Reply `dev password <value>` in the chat (stored for the project), or fill it in in the *Test accounts* card. For a real password, reply `login <user> <password>` (that session only). |
| Plan rejected: `actor '…' is neither a test account nor the role of one` | The planner wrote an actor that is not in the test account list. | The planner fixes it itself; when the role really does not exist yet, add an account with that role. |
| `The scenario file cannot be used yet: Steps sheet row N: …` | A row has no step/result, or an actor that is not in the Accounts sheet. | Fix the row named, save, then run/upload again. |
| `… cannot be read …; close it in Excel first` | The scenario file is locked by Excel. | Save and close the file in Excel, then reply `run scenario <name>` again. |
| `Scenario … has not run yet. The scenario runs against the running application …` | The first side-effect confirmation in this project. | Make sure dev emails/notifications do not reach real people, then reply `run scenario <name> agree`. |
| `The scenario is not complete yet: Role X has no test account yet` | The Accounts sheet is not filled in for that role. | Fill in the username in the Accounts sheet (yellow cell), save, run again. |
| A step is *could not be checked — The previous step did not pass* | The scenario stopped at the first failed step. | See the failed step; fix the application or the file content, then run again. |
| *Login as* is wrong/not used | The detection result is not right (a different role name, a wrong URL) or its status is *none*. | Open the *Test accounts* card → **Change**, fill in the right way and roles, tick *checked*. **Detect** repeats the code scan. |
| `The application cannot be reached at http://localhost:… AIT does not know yet how to run this project` | The application is not running and its stack is not recognized (see [Running the application](07-cara-kerja.md#running-the-application)), or there are several .NET web projects without a matching port. | Write its command in `.aithread/project.json`, e.g. `{ "run": "dotnet run --project src/Web" }`, or start the application yourself, then **🧪 Test again**. |
| `The application is not running at … and AIT failed to start it: … stopped with exit code …` | The application failed to start: the port is in use, the dev configuration is missing (e.g. an empty section in user secrets or `appsettings.Development.json` → a `NullReferenceException` in `Program.cs`), the dev database/Redis cannot be reached, or the build failed. The cause is in the **Application output** block. | Try the same command in a terminal; when it fails there too, complete the dev configuration on this machine. When it runs there, write the command you used in `.aithread/project.json` (`"run"`). Then **🧪 Test again**. |
| `… AIT failed to start it: … did not answer after 3 minutes` | The application runs on another port than the one the planner found, or its start is very slow. | Write its URL in the prompt or in the Planner node's *Extra instructions* (e.g. "the application runs at http://localhost:5080"), or start the application yourself. |
| `… node_modules does not exist yet` | The `dev`/`start` script exists, but its packages are not installed yet; AIT does not run `npm install`. | Run `npm install` once, then **🧪 Test again**. |
| `The tester browser cannot be opened` | Edge and Chrome were not found and Chromium failed to download (offline), or the `.playwright` folder next to `AIThread.exe` is missing. | Install Edge/Chrome, or connect to the internet once so Chromium downloads; reinstall AIT when `.playwright` is missing. |
| `… Node.js for Playwright was not found and could not be downloaded` | There is no `node` 20+ on `PATH` and Node.js cannot be downloaded from nodejs.org (offline/proxy). | Install Node.js 20+ (or point the `PLAYWRIGHT_NODEJS_PATH` env at a `node.exe`), restart AIT, then **🧪 Test again**. |
| The browser is not visible | AIT runs as a service / in another Windows session. | Run AIT from the tray or the `AIThread.exe` menu in your session. |
| Scenarios *could not be checked — Login needed* | The tester has no account. | Reply in the chat `login <username> <password>`; for a thread from the Dashboard/Telegram fill it in in the **🧪 Test again** modal. |
| A `login …` reply runs as a regular prompt | No login question is waiting in that session & project (past 2 hours, AIT restarted, or another chat), or its format is not the three words `login <username> <password>`. | Use **🧪 Test again** in the thread detail, or ask for testing again with a new prompt. A password with spaces: use the labeled format `username: …` / `password: …`. |
| Scenarios *could not be checked* because of a captcha | The tester already filled the captcha with any value (enough in a dev environment that bypasses captchas as long as they are filled in), but the application still checks it. | Turn on a captcha bypass in the development configuration (e.g. a flag in `appsettings.Development.json`/`.env`, or reCAPTCHA test keys), then **🧪 Test again**. |
| The login is rejected although the account is right | The login form uses 2FA/OTP or SSO on another host (not localhost). | Use a test account without 2FA in the development environment. An SSO page on another host cannot be opened by the tester. |
| A scenario fails although the diff is right | The application has not loaded the change yet (it runs without hot reload, or views/JS are cached). | Restart the application or use `dotnet watch`/a dev server, then **🧪 Test again**. |
| The tester goes around in circles on the same page | The tester model is too weak at reading snapshots. | Use a more careful tester model, or give clear element ids in the contract (the planner copies them into the scenario steps). |

## Redis & database

**The Redis card is *Not connected***
Short memory uses Garnet inside AIT. Look for the `Embedded Garnet` line in the AIThread log: when Garnet failed to start,
its cause is recorded there. When `AIThread:EmbeddedRedis:Enabled` = `false`, make sure the external Redis in
`ConnectionStrings:Redis` is alive. While short memory is not available, new memory goes straight into long memory; no
data is lost.

**The log `Port 6379 is already in use … short memory uses that server`**
Another program (usually an old `aithread-redis` Docker container) uses the short memory port, so AIT uses it instead of
Garnet. First move the short memories you want to keep (Memory menu → **→ DB**), then `docker rm -f aithread-redis` and
restart AIT. See [Installation](01-instalasi.md#1-short-memory-garnet).

**An external memory storage is *Not connected*** (Setting menu → Memory storage)
Click **Test connection** on its card: the server's error message (host not found, login failed, database does not exist)
is shown as it is. While an external short memory is down, new memory goes into long memory and the store is checked again
every 15 seconds. When an external long memory is down, threads still run without long memory (the activity log records
*Long memory … cannot be reached*), and consolidation waits until the server is back; or switch the provider back to
*Built-in*. The log `The saved short/long memory store is invalid` on start means the stored setting is invalid, and AIT
uses the built-in store.

**Saving the memory storage is refused with `permission denied` / `CREATE DATABASE`**
AIT creates the memory database and tables itself. Create the database by hand, then give the user the right to create
tables in that database (Oracle: the tables are created in the schema of the user who logs in).

**The Database card is *Not connected*** / the log `Database migration failed`
For LocalDB: `sqllocaldb start MSSQLLocalDB`. For another server: check `ConnectionStrings:Default`. Restart AIT after
fixing it so the migrations run.

## Telegram

A rejected token, a bot that does not reply, *This token is in use by another program…*, *This chat is not allowed yet*,
and other Telegram bot symptoms: see [Prompts from Telegram → Troubleshooting](11-telegram.md#troubleshooting).

## Skill

**A skill source *Failed*: `HTTP 404 — link not found or private repository`**
A private repository cannot be downloaded without credentials. For git hosts, `git clone` runs without a credential
prompt, so only repositories that can be cloned without a login (or with a credential helper already stored) succeed.

**`HTTP 403`/`429 — limited by the server`**
The GitHub API limits downloads without a login (60 per hour per IP). AIT automatically tries `git clone` instead. When
git is not installed, wait, then click *Sync*.

**A source stays long in the *Analyzed by AI…* status**
Not an error when its progress line (e.g. `864 skills · batch 7/22`) still moves. Skills are tagged per 40 skills, 4
batches at once, so a repository holding hundreds of `SKILL.md` files takes a few minutes, longer with a large model. A
faster Skill Orchestrator model (e.g. the executor model) speeds it up. A failed batch is stored without tags.

**`git was not found on PATH`**
Install Git, or use a GitHub/GitLab link (downloaded as a ZIP without git).

**`No skill was found at this link`**
The repository has no `SKILL.md` and the model found no Markdown file holding instructions for an AI either. Try a link
straight to its skill folder (`/tree/main/<folder>`) or to its `.md` file.

**Skills stored without tags / *Find skills* fails: `Choose the Skill Orchestrator model…`**
Choose the model in the Skill menu (or the Skill Orchestrator column of the Node menu), or add an active Planner node. Skills without tags can still be used; click *Sync*
once a model is available so the tags are made again.

**A node feels slow or expensive after getting skills**
Skill content is sent along with every model call of the node. Use fewer skills, or lower *Skill character limit per
node*.

## Application

**AIT stops when the browser, the terminal or Visual Studio is closed**
An instance from Visual Studio (the debugger stops when the browser it opened is closed) and `dotnet run` only live while
that program is open. Run it in the background: `.\scripts\aithread.ps1 start` — see
[Running in the background](01-instalasi.md#5-running-in-the-background).

**Port 5225 is already in use**
Run with `--urls http://localhost:<another port>`, then apply the CLI integrations again in the CLI menu.

**`aithread.ps1 start`: "Port 5225 is already in use by another process (PID …)"**
Usually an instance from Visual Studio / `dotnet run` is still alive. Stop that program, then `start` again. When the
program using the port is AIT, `.\scripts\aithread.ps1 stop` can stop it too.

**`aithread.ps1 start`: "AIT stopped while starting (exit N)"**
A part of the log is shown right away. Common causes: the database cannot be reached (see
[Redis & database](#redis--database)) or `appsettings.json` is invalid. The full log: `.\scripts\aithread.ps1 logs`.

**Code changes do not show in the background instance**
The background instance uses the publish output, not the `src/` folder. Run `.\scripts\aithread.ps1 restart`.

**The background log holds `Failed to determine the https port for redirect`**
Normal: the background instance only listens on http. This warning has no effect.

**A *Cancelled* thread: "AIT stopped while this thread was running."**
AIT was stopped (stop, restart, logoff, crash) while that thread ran. The results of tasks that already finished stay in
the files — review them with git, then send the prompt again when needed.

**A thread *Cancelled* after the planner asked**
Its message explains why: *AIT restarted before the planner's question was answered*, *not answered within 2 hours*,
*cancelled while waiting for an answer*, or *the next prompt runs as a new thread* (the answer came from another project).
Questions are only kept in the process memory, so send your answer again together with the request — the planner can
still read the earlier question from the chat history.

**An answer to the planner becomes a new thread instead**
Answers are recognized by the client's session id. Make sure you answer in the same session (chat), have not `/clear`ed
or opened a new session, and that your client sends a session id (Claude Code and Telegram always do; clients without a
session id are never asked).

**An https Base URL fails from the CLI**
Node.js based CLIs (e.g. Claude Code) do not trust the development certificate. Use the address `http://localhost:5225`.

**Pages do not update live**
The browser cut the SSE connection (e.g. too many AIT tabs open). Close other tabs or refresh the page.
