# 3. CLI integration

AIT is not tied to one coding CLI. Any CLI whose base URL can be changed can use AIT, as long as it sends one of the
four request formats below. 9router translates it to any provider (Claude, GPT, Gemini, …), so the model in use does
not have to match the CLI's "language":

| Format | AIT endpoint | Example CLIs |
|---|---|---|
| Anthropic Messages | `POST /v1/messages` | Claude Code, oh-my-pi (`anthropic-messages`), opencode (`@ai-sdk/anthropic`) |
| OpenAI Chat Completions | `POST /v1/chat/completions` | Qwen Code, Cursor, Continue, oh-my-pi (`openai-completions`) |
| OpenAI Responses | `POST /v1/responses` | Codex CLI |
| Gemini native | `POST /v1beta/models/{model}:generateContent` and `:streamGenerateContent` | Gemini CLI |

The orchestration rules are the same for every CLI: a request is orchestrated when it carries `tools`, its last message
is from the user (not a tool result), and its session comes from a project, i.e. a folder holding `.git` or a build file
(`.sln`, `pom.xml`, `package.json`, `go.mod`, etc.; see [Session check](07-cara-kerja.md#2-session-check-project-or-not)).
The reply is sent in the CLI's own format (stream or non-stream). Other requests, including every prompt from a session
outside a project (an empty folder, a notes folder, etc.), are forwarded to the same 9router endpoint with the
passthrough model, so the CLI keeps working as usual there, without memory.

The model name the CLI sends is ignored on passthrough and shown as it is on orchestrated replies; the model really used
is decided by Setting *Passthrough model* and the Nodes.

## Through the CLI menu

The recommended way. The **CLI** menu has cards for Claude Code, oh-my-pi, Codex CLI, Gemini CLI, opencode and Qwen
Code; the same *Global* switches are also in Setup → the *Point a coding CLI to AIT* step. All cards work the same way:
AIT only writes/removes its own entries, a backup of the original file is kept once as `*.aithread-backup`, and a file
that only holds AIT's entries is deleted when the switch is turned off. When *Proxy API key* is filled in, its value is
written too (empty = `aithread-local`). The proxy URL that is written is set in the *AIT proxy URL* field at the top of
the page; *What will be written* shows a preview.

| CLI | File | Scope | How to use it |
|---|---|---|---|
| [Claude Code](#claude-code) | `~/.claude/settings.json`, or `<project>/.claude/settings.local.json` | global + per project | open a new session |
| [Codex CLI](#codex-cli) | `~/.codex/config.toml` (or `$CODEX_HOME`) | global | `codex --profile aithread` (`-p aithread`); plain `codex` keeps its original provider |
| [Gemini CLI](#gemini-cli) | `~/.gemini/.env`, or `<project>/.gemini/.env` | global + per project | choose *Use Gemini API Key* in `/auth` |
| [opencode](#opencode) | `~/.config/opencode/opencode.json` (or an existing `opencode.jsonc`) | global | choose `aithread/aithread` with `/models` |
| [Qwen Code](#qwen-code) | `~/.qwen/settings.json` | global | choose the *AIT* model with `/model` |
| [oh-my-pi](#oh-my-pi) | `~/.omp/agent/models.yml` | global | choose the `AIT/AIThread` model with `/model` |

Turning on Claude Code or Gemini CLI globally asks for confirmation, because all their sessions then go through AIT. A
CLI without a card can still be used: point its base URL to AIT by hand (see [Other CLIs](#other-clis)).

## Claude Code

Claude Code reads environment variables from the `env` block in `settings.json`. AIT uses three (plus one optional)
variables:

| Variable | Value | Function |
|---|---|---|
| `ANTHROPIC_BASE_URL` | `http://localhost:5225` | Every Claude Code request is sent to AIT instead of Anthropic. |
| `ANTHROPIC_AUTH_TOKEN` | The proxy API key, or `aithread-local` when empty | Sent as `Authorization: Bearer`. Your Anthropic credentials are not sent to the proxy. |
| `API_TIMEOUT_MS` | `1800000` (30 minutes) | One thread can take several minutes; the Claude Code default (10 minutes) can be too short. |
| `ANTHROPIC_CUSTOM_HEADERS` | `X-AIThread-Project: <project root folder>` | Only for the per-project scope. Binds the session to the right project. |

### Per project

Only Claude Code sessions opened in that project go through AIT.

1. **Project** → *Browse…* → find the project folder (navigate folders or *Scan* sub-folders; project folders are
   marked with their stack or <kbd>git</kbd>) → tick it → *Register selected*. You can also paste a folder path in the
   field, then *Register*. A project is also registered automatically on the first prompt that comes from a session in
   the project folder.
2. Fastest: in the **Project** table, turn on the switch in the **AIT** column of that project (turn it off to revert).
   The switch uses the default proxy URL (the application's http address).
3. Or through the **CLI** menu → the *Claude Code* card → turn on the project's switch in the *Per project* list.
4. Open a new Claude Code session in that project for it to take effect.

The file written: `<project folder>/.claude/settings.local.json`, with the project folder = the project's root folder
(shown in the status box). `settings.local.json` is Claude Code's local setting and is usually *gitignored*.

> Claude Code reads `.claude/settings.local.json` from the folder you run it in. When you usually open Claude Code in a
> sub-folder of the project or in another folder, use the [manual way](#manual) and put the file in that folder.

### Global

Every Claude Code session on this computer goes through AIT. In the **CLI** menu, turn on the *Global* switch on the
Claude Code card (there is a confirmation). The file written: `%USERPROFILE%\.claude\settings.json`. Sessions outside a
project are still forwarded as they are, as explained at the start of this chapter.

### Shown status

| Status | Meaning |
|---|---|
| ✓ Claude Code already points to AIT | `ANTHROPIC_BASE_URL` in the file is the same as the proxy URL. |
| `Current ANTHROPIC_BASE_URL: … (not AIT)` | The file points somewhere else (e.g. straight to 9router). *Apply* overwrites it. |
| Not pointed yet | There is no `ANTHROPIC_BASE_URL` yet. |

Before the first change, the original file is backed up **once** as `settings.json.aithread-backup` /
`settings.local.json.aithread-backup`. Other keys in the file (model, hooks, etc.) are not changed, and your own
`ANTHROPIC_CUSTOM_HEADERS` is not overwritten.

AIT also adds `permissions.deny` rules for credential files (`Read(//**/*.pem)`, `Read(//**/.npmrc)`, `Read(~/.ssh/**)`,
`Read(~/.aws/**)`, etc.), so Claude Code itself does not read them. Your own permission rules are not touched. The full
list is in [Security & privacy](12-keamanan.md#blocked-credential-files).

### Manual

Add this to `.claude/settings.local.json` in the folder where you run `claude` (or to `~/.claude/settings.json` for
global):

```json
{
  "env": {
    "ANTHROPIC_BASE_URL": "http://localhost:5225",
    "ANTHROPIC_AUTH_TOKEN": "aithread-local",
    "API_TIMEOUT_MS": "1800000",
    "ANTHROPIC_CUSTOM_HEADERS": "X-AIThread-Project: D:\\Source\\MyApp"
  }
}
```

- Replace `aithread-local` with the proxy API key when you filled one in in Setting.
- `ANTHROPIC_CUSTOM_HEADERS` is optional. Without this header, AIT decides the project from the session's working
  directory (that folder or a parent folder). Another option: `X-AIThread-Cwd: <folder>` to set the working directory.
  The old `X-AIThread-Solution` header (a `.sln` path) is still accepted; its folder is used as a hint for the project.

To revert by hand, remove the keys above (and the `X-AIThread-Project` header, or `X-AIThread-Solution` from an old
version), or restore from the `*.aithread-backup` file.

### Context Keeper for /clear

A long Claude Code session is expensive because **every request reads the whole context again**. `/clear` makes the
context small again, but the thread of work is lost. This hook bridges the two, and applies to **every** Claude Code
session in its scope — including sessions that are not pointed to the AIT proxy:

1. You run `/clear`. The `SessionEnd` hook (reason `clear`) sends the location of the old session's transcript to AIT,
   which answers right away and works in the background.
2. AIT condenses the transcript: your prompts, the reply text, one line per tool call, tool errors and `/compact`
   summaries (tool results, thinking and sub-agents are skipped; at most 60,000 characters). The Context Keeper (the
   Context Keeper node, or the Wrapper) writes a session summary from it, together with the summary the old session
   itself used.
3. The `SessionStart` hook (source `clear`) of the new session in the same folder waits for the summary (±40 seconds at
   most) and inserts it as `additionalContext`. Claude Code continues from that summary with a small context.

The next `/clear` builds the summary from the previous summary plus the latest session, so the flow stays connected.

**Installing**: **CLI → Claude Code card → Hook: Context Keeper /clear & secret restorer** → choose the scope (Global is
recommended so it applies in every project) → **Install hook**. What is written to the settings file (your other hooks
are not touched):

```json
{
  "hooks": {
    "SessionEnd": [
      { "matcher": "clear", "hooks": [ { "type": "http", "url": "http://localhost:5225/api/hooks/claude/session-end", "timeout": 5 } ] }
    ],
    "SessionStart": [
      { "matcher": "clear", "hooks": [ { "type": "http", "url": "http://localhost:5225/api/hooks/claude/session-start", "timeout": 60 } ] }
    ],
    "PreToolUse": [
      { "matcher": "Edit|MultiEdit|Write|NotebookEdit|Bash", "hooks": [ { "type": "http", "url": "http://localhost:5225/api/hooks/claude/pre-tool-use", "timeout": 10 } ] }
    ]
  }
}
```

The `PreToolUse` hook is the **secret restorer**. The model only sees secrets as `{{AIT_SECRET:…}}`
([Security & privacy](12-keamanan.md)). Before Claude Code runs an Edit/Write/Bash that holds a placeholder, AIT replaces
it with the real value, and Claude Code still asks for your confirmation. A placeholder AIT does not recognize stops
that tool, so it is not written to a file as it is. A hook installed by an old version (without `PreToolUse`) is marked
*old version*; click **Install hook** again to update it.

The hook applies to Claude Code sessions opened after it is installed. **Remove hook** deletes AIT's entries only.

Notes:

- After `/clear`, Claude Code waits for the summary (usually a few seconds, depending on the Context Keeper model). When
  AIT is down or the summary is not done within 40 seconds, the new session still starts without a summary.
- The old and new sessions are matched by working directory. When two sessions in the same folder are `/clear`ed at the
  same time, the summaries can be swapped.
- The transcript content (condensed) is sent to the Context Keeper model through 9router. AIT only reads transcripts
  inside `~/.claude/projects`.
- Claude Code's own automatic `/compact` summary is not replaced; this hook only works on `/clear`.

### Claude Code notes

- While it points to AIT, that Claude Code session does **not** use your Anthropic account; every model comes from
  9router.
- The usage returned for orchestrated answers is an estimate of the context size, reported as `cache_read_input_tokens`
  (`input_tokens` = 0). Claude Code still counts the remaining context correctly, while cost trackers that read the
  transcript (burn rate, ccusage, etc.) do not count it as full Opus input.
- A system environment variable with the same name can clash with this setting. When the results are not as expected,
  check `ANTHROPIC_BASE_URL` in your Windows environment.
- Claude Code (Node.js) does not trust the ASP.NET Core development certificate; use an **http** proxy URL.

## Codex CLI

Codex does not allow providers in a project's `.codex/config.toml`, so it is global only. This block is added at the end
of the file (Codex only uses `wire_api = "responses"`; `stream_idle_timeout_ms` is lengthened because an orchestrated
thread can stay quiet for minutes):

```toml
# >>> AIThread (dikelola otomatis, lepas lewat menu CLI) >>>
[model_providers.aithread]
name = "AIT"
base_url = "http://localhost:5225/v1"
wire_api = "responses"
experimental_bearer_token = "aithread-local"   # the proxy API key when filled in
stream_idle_timeout_ms = 1800000

[profiles.aithread]
model_provider = "aithread"
# <<< AIThread <<<
```

- Run `codex --profile aithread` (`-p aithread`) for a session through AIT; plain `codex` keeps its original provider.
- AIT refuses to write when there already is a `[model_providers.aithread]` / `[profiles.aithread]` made by you, or when
  `model_providers`/`profiles` is written as an inline table (`model_providers = { ... }`).
- Codex must use plain HTTP (not WebSocket) to this provider; that is the default for custom providers.

## Gemini CLI

AIT writes two variables in `.gemini/.env`, global or per project (HTTP is allowed because it is `localhost`):

```dotenv
# >>> AIThread (dikelola otomatis, lepas lewat menu CLI) >>>
GOOGLE_GEMINI_BASE_URL="http://localhost:5225"
GEMINI_API_KEY="aithread-local"
# <<< AIThread <<<
```

- The base URL only applies with *Gemini API Key* authentication; a Google login does not go through AIT.
- Gemini CLI reads only **one** `.env`: the first one found from the working folder upward (`.gemini/.env`, then
  `.env`), and only then `~/.gemini/.env`. A project with its own `.env` in the root hides the global switch — use the
  per-project switch for that project. A system environment variable with the same name also wins over `.env`.
- AIT refuses to write when the file already holds a `GEMINI_API_KEY` or `GOOGLE_GEMINI_BASE_URL` made by you; turn it
  into a comment first.
- A per-project `.gemini/.env` holds the proxy API key — make sure it is not committed.
- The Gemini endpoints are only `generateContent` and `streamGenerateContent` (`countTokens` etc. are not proxied), and a
  Gemini stream is always SSE (`alt=sse`, which Gemini CLI uses).

## opencode

The `aithread` provider (Anthropic format through `@ai-sdk/anthropic`) is added to `provider` in the global
`opencode.json`, with `timeout: false` so a long thread is not cut off. Global only, because a project's `opencode.json`
is usually committed. AIT refuses to write when the file holds comments (it cannot be rewritten without removing them)
or already has an `aithread` provider made by you. Choose `aithread/aithread` with `/models`.

## Qwen Code

The `aithread` model (OpenAI-compatible, `baseUrl` = `<proxy>/v1`) is added to `modelProviders.openai` together with
`env.AITHREAD_API_KEY`, with a 30-minute `generationConfig.timeout`. Global only, because Qwen Code replaces (instead of
merging) `modelProviders` from the project settings. The rules about comments and ids already in use are the same as
for opencode. Qwen Code's built-in stream lifetime limit (15 minutes, `QWEN_STREAM_MAX_LIFETIME_MS`) still applies.
Choose the *AIT* model with `/model`.

## oh-my-pi

Turn on the *Global* switch on the *oh-my-pi (omp) — provider AIT* card. AIT adds one provider to
`~/.omp/agent/models.yml` (global only; omp has no per-project `models.yml`), without touching other providers:

```yaml
# >>> AIThread (dikelola otomatis, lepas lewat menu CLI) >>>
providers:
  AIT:
    baseUrl: "http://localhost:5225"
    api: anthropic-messages
    apiKey: "aithread-local"        # the proxy API key when filled in in Setting
    models:
      - id: AIThread
        name: AIThread
        contextWindow: 1000000
        maxTokens: 32000
# <<< AIThread <<<
```

- Choose the `AIT/AIThread` model with `/model` in omp (or `omp --model AIT/AIThread`); the omp status bar shows
  `AIThread`. The original Anthropic provider stays available, so you can switch per session.
- An old block from a previous version (provider `aithread/aithread`) shows as not active in the CLI menu; turn the
  *Global* switch on again to replace it with `AIT/AIThread`.
- When `models.yml` already has `providers:`, the `AIT` entry is inserted below it with the same indentation. AIT refuses
  to write when `providers` is on one line (`providers: { ... }`), when there already is an `AIT` provider made by you,
  or when an old `models.json` that omp has not migrated yet still exists.
- The project is still recognized from the working directory omp sends in every request.
- Tested with omp 18.4.4: omp sends `POST <baseUrl>/v1/messages?beta=true` with `Authorization: Bearer <apiKey>`. An
  `apiKey` value equal to the name of an environment variable is replaced with that variable's content.

## Other CLIs

A CLI without a card (Cursor, Continue, etc.) just needs to be pointed to the AIT endpoint of its format (the table at
the start of this chapter) with the proxy API key, or `aithread-local`, as the bearer token. Give it a long timeout
(±30 minutes), because one thread can take several minutes.

**Helping AIT recognize the session.** The working directory is looked up from the `X-AIThread-Cwd` header, then from
the request text: `Primary working directory:` (Claude Code), `<cwd>…</cwd>` (Codex), `I'm currently working in the
directory:` (Gemini CLI), or `Current working directory:` / `Working directory:` / `Current directory:` / `cwd:`. When
your CLI sends none of them, set the `X-AIThread-Cwd` header (or `X-AIThread-Project`). The session id is read from the
`X-AIThread-Session`, `x-claude-code-session-id`, `session_id` (Codex) or `x-session-id` header, from `prompt_cache_key`
(OpenAI), or from `metadata.user_id`; without a session id, the project decision is remembered per working directory.

## Verification

1. **Open a new session** in that CLI — the configuration is only read when a session starts. For Codex run
   `codex --profile aithread`; in oh-my-pi, opencode and Qwen Code choose the AIT model.
2. Type a simple prompt, e.g. "explain the structure of this project".
3. The answer should start with lines like:

   ```
   **AIT** · `MyApp` · thread `5e469b54`
   🧠 Planner · `…`
   ```

4. A new thread shows up in the AIT Dashboard with the log entry `New thread from CLI · MyApp`.

When the answer comes without the `**AIT**` line, see [Troubleshooting](08-troubleshooting.md#the-cli-does-not-go-through-ait).

## Reverting

- UI: the **CLI** menu → turn the same switch off (for Claude Code also through the **AIT** switch in the Project
  table). Only AIT's entries are removed — for Claude Code: `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`,
  `API_TIMEOUT_MS`, the `X-AIThread-Project`/`X-AIThread-Solution` header, and the `permissions.deny` rules AIT added;
  everything else is left alone.
- Manual: remove AIT's entries (the blocks marked `AIThread` in Codex, Gemini CLI and oh-my-pi), or restore from the
  `*.aithread-backup` file.

Open a new CLI session afterwards.

## Notes

- Turning *Orchestration on* off in Setting does not require changing the CLI configuration: the CLI still goes through
  AIT, but its requests are forwarded straight to 9router so the CLI works as a plain agent.
- Threads from all these CLIs show with the source *CLI* in the Dashboard and Telegram.
