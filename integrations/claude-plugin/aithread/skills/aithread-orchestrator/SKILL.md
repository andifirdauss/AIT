---
name: aithread-orchestrator
description: Use for every request that creates, changes, fixes or refactors code, tests or project files, including small ones, and whenever a hook mentions AIT or blocks an edit. Delegates the work to AIT (the aithread orchestrator) and installs AIT when it is missing. Not needed for questions or explanations that change nothing.
allowed-tools: Bash(aithread version), Bash(aithread doctor *), Bash(aithread chat *), Bash(aithread thread wait *)
---

# AIT orchestrator

AIT is a local service that carries out code changes: it plans, runs parallel executors, builds, tests, and keeps project memory. In a folder AIT is ready for, you do not edit files yourself. You write AIT a good brief, wait for it, and report the result.

AIT runs on Windows x64 only. On any other system, say so once and carry on without AIT.

## 1. Make AIT ready

Skip this section when the hook already said the folder is an AIT project.

Check that it is installed by running `aithread version`; a "not found" error means it is missing.

If it is missing, ask the developer once before installing anything. Say that this runs `npm i -g aithread` (a global package, needs Node.js 18+), that AIT then runs as a local server in the tray, and that its data stays in `%LOCALAPPDATA%\AIThread`.

- If they decline, run `node "${CLAUDE_SKILL_DIR}/../../hooks/gate.mjs" decline` and do their request normally. The plugin stays silent from then on.
- If they agree, run `npm i -g aithread`, and treat that one answer as consent for the setup steps below.

If it is installed, look at that version before anything else. Versions below 1.18.0 do not have the commands used here, and an unknown command starts a web server in your shell instead of failing. Ask the developer once before updating: say that this stops AIT, which cancels any thread it is running. If they agree, run `aithread stop` and then `npm i -g aithread@latest`. If they decline, carry on without AIT for this request.

Then repeat until `ready` is true:

1. Run `aithread doctor --start --json`.
2. Take the first entry of `fixes`. If it has a `command`, run it with `--json` where the command already shows it.
3. If it has no `command`, or its command is `aithread setup`, show the developer its `message` and stop: that step needs a person (installing LocalDB, or typing an API key on the Setup page).

Never ask for, read, or pass an API key or password yourself.

## 2. Delegate the change

AIT does not see this conversation, so the brief must stand on its own: the goal, the relevant decisions and constraints from the conversation, file names, error messages, and how to know it is done. Write in the language the developer used.

Send the brief on stdin, which keeps long prompts intact. In bash:

```bash
aithread chat -p - --detach --json <<'AIT_PROMPT'
<the brief>
AIT_PROMPT
```

In PowerShell:

```powershell
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
@'
<the brief>
'@ | aithread chat -p - --detach --json
```

In cmd.exe, put the brief in a temporary file outside the project and run `aithread chat -p - --detach --json < "<file>"`.

This prints `{threadId, sessionId, session, project, url}`. Keep `session`: later briefs in this conversation add `-r <session>` so AIT remembers the earlier turns. Run the command from the project folder.

## 3. Wait for it

The thread runs on the AIT server whatever happens to your command. Wait in repeatable steps. Keep `--timeout` at least 90 seconds below the time limit of your shell tool, or give that command a longer limit:

```
aithread thread wait <threadId> --timeout 240 --json
```

| Exit code | Meaning | What to do |
|---|---|---|
| 0 | completed | Report it (section 4) |
| 1 | failed or cancelled | Report it, see below |
| 3 | waiting (`waitingFor` says for what) | See below |
| 4 | still running | Run the same command again |

When `waitingFor` is `approval`, show the developer `approval.text` and ask them to choose. Then run `aithread thread approve <threadId> once|always|deny --json`. Only the developer makes this choice: never answer it yourself, and never use `-y`. If the result has `continues: true`, wait on the `threadId` it returned.

When `waitingFor` is `question`, AIT's planner asks something in `finalResponse`. Answer it from the conversation if the answer is clearly there, otherwise ask the developer. Send the answer the same way as a brief, adding `-r <session>`, and wait on the returned `threadId`.

## 4. Report

Tell the developer what `finalResponse` says, which `files` changed, and the `checks`, with the `url`. Do not redo the work or re-run what AIT already verified.

When a thread fails, report `error` and the failing tasks as they are, and ask how to proceed. Do not quietly make the change yourself, by edit tools or by writing files through the shell.

## When AIT is off

Direct edits are for when the developer asks for them or AIT is not available here. `aithread skill off` turns the plugin off for the current project, and `aithread skill on` turns it back on. That is the developer's choice to make, so offer it rather than running it.
