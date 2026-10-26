# Security & privacy

AIT reads your code so AI models can plan and change it. All of that content goes through AIT before it reaches a model,
so AIT can control what is sent along and what is stored. This page explains what AIT does, how to configure it, and its
limits.

A short explanation shows as the **Security & privacy** popup when AIT is opened for the first time. That popup can be
opened again at any time from the sidebar (the Help group).

```
Your files & prompts ──► AIT ── secret → {{AIT_SECRET:…}} ──► 9router ──► model
          ▲                │
          └── real value ◄─┘  (only when AIT / Claude Code writes to your files)
```

## Summary

| Protection | What it does |
|---|---|
| Secret masking | Passwords, API keys, tokens, JWTs, private keys, and passwords in connection strings or URLs are replaced with placeholders before they are sent to a model. |
| Restoring on write | The real values only exist in the AIT process memory, and are put back when an executor writes a file, the Tester calls the application, or Claude Code runs Edit/Write/Bash (through a hook). |
| Blocked credential files | Keys, certificates, SSH keys, token files and credential folders in the Windows profile are never read or changed by AIT's agents. |
| Not stored | Prompts, chat history, the activity log, the planner's investigation notes, memory, the business wiki and memory export files are masked before they are stored. |
| Local | The database, memory and logs are on your computer. There is no telemetry. |

## Secret masking

### What is recognized

- **Values of settings whose name points to a secret**, for example `Password`, `DB_PASSWORD`, `smtpPassword`, `ApiKey`,
  `Jwt:Key`, `ClientSecret`, `access_token`, `APP_KEY` or `passphrase`. This pattern is recognized in JSON
  (`"Password": "…"`), C#/Python/JS (`Password = "…"`), PHP (`'password' => '…'`), XML/web.config
  (`<add key="SmtpPassword" value="…"/>`, `password="…"`, `<Password>…</Password>`), and `.env` (`DB_PASSWORD=…`). In
  configuration files (`.yml`, `.yaml`, `.ini`, `.properties`, `.toml`, `.conf`, `.cfg`, `.env*`), unquoted lines such as
  `password: secret` are recognized too.
- **Passwords in connection strings**: `Password=…`, `Pwd=…`, `AccountKey=…`, `SharedAccessKey=…`. Other parts such as
  `Server` and `Database` stay visible.
- **Passwords in URLs**: `postgres://user:secret@host`.
- **Known token formats**: AWS access keys, GitHub/GitLab tokens, Anthropic/OpenAI/Google API keys, Slack, npm, Stripe and
  Telegram bot tokens, JWTs, and `-----BEGIN … PRIVATE KEY-----` blocks.
- **Secrets found before**: a value that is long and random enough (at least 8 characters with a mix of letters, digits
  or symbols) is masked wherever it shows up again, for example when a password from `appsettings.json` is pasted into a
  prompt. A value made of letters only (with `_`, `.`, `-`, `/` or `:` between them), such as `LunchClaimRateHeaderDto`,
  counts as a code name: it is still masked where it is the secret, but not elsewhere, so class and file names are not
  masked along with it.

Setting names that only *talk about* a secret are not masked, for example `PasswordLength`, `TokenEndpoint`,
`ApiKeyHeaderName` or `RequireUppercase`. Neither are values that refer to somewhere else, such as `${DB_PASS}`,
`%PASSWORD%`, `{{password}}` or `****`.

### Placeholder shape

```
"ConnectionStrings": { "Default": "Server=.;Database=Shop;User Id=sa;Password={{AIT_SECRET:Password:3f2a9c1b7e}};" }
"Jwt": { "Key": "{{AIT_SECRET:Key:91d0c4aa52}}" }
```

- The label (`Password`, `Key`, `DB_PASSWORD`, `jwt`, …) tells the model which value is hidden.
- The id is an HMAC-SHA256 of the value with a key that only exists on this computer
  (`%LOCALAPPDATA%\AIThread\secret-mask.key`, encrypted with ASP.NET Data Protection). The value cannot be guessed from
  the placeholder. The same secret always gets the same placeholder, also after AIT restarts, so the provider's prompt
  cache stays effective.

### Where it is masked

| Path | Details |
|---|---|
| Every AIT model call | The planner, tools nodes, executors, reviewer, Tester, wrapper, memory extraction, the wiki writer and the Skill Orchestrator are filtered at one point before anything is sent to 9router. |
| Planner tools | `read_file` and `search_text` read file content with the configuration file rules. `git_diff`/`git_show`/`git_blame`, symbol searches, build/test output and memory are filtered too. |
| Executors | The content of the file being worked on and reference excerpts from other files. |
| Tester | `http_request` responses and browser pages, for example a token from a login endpoint. |
| Prompts from the CLI | The prompt, the prompt context and the conversation history of Claude Code/Codex/Gemini, including files the CLI itself read. |
| Passthrough | Requests forwarded as they are to 9router (session titles, compact, sessions outside a project). Can be turned off separately in Setting. |

### Restoring the real values

The real values are kept in the AIT process memory (the vault), never on disk. They are only used again where AIT itself
acts:

- **Executors**: a SEARCH/REPLACE block holding placeholders gets the real values right before it is applied to the file.
  Before that AIT reads the target file again, so a placeholder copied from that file can still be restored after a
  restart. An unrecognized placeholder makes the edit refused and the executor is asked to try again. The diff shown in
  reports and stored with the thread keeps the placeholders.
- **Planner tools and the plan**: a path or name the model copied from masked output (`read_file`, `find_references`,
  …, and the task files of `submit_plan`) is restored before the tool runs and before the plan is validated, so that
  file can still be read and changed. The tool output is masked again.
- **Tester**: a token the application returned earlier (and that was masked) is put back in the next request, so a login
  → call API flow keeps working.
- **Claude Code** (the `PreToolUse` hook): before Edit, MultiEdit, Write, NotebookEdit or Bash runs, AIT replaces the
  placeholders in the tool input with their real values. Claude Code still asks for your confirmation, unless the session
  uses the `acceptEdits` mode (for file edits) or `bypassPermissions`. An unrecognized placeholder stops the tool with a
  message to the model, so it is not written to a file as it is. This hook endpoint only answers requests from this
  computer (loopback). Install the hook in the **CLI → Claude Code** menu
  ([CLI integration](03-integrasi-cli.md#context-keeper-for-clear)).

> Other CLIs (Codex, Gemini CLI, opencode, Qwen Code, oh-my-pi) have no such hook yet. In passthrough mode, the model
> could write a placeholder to a file. When that CLI often edits files holding secrets, turn off **Also mask passthrough
> requests** in Setting. Threads orchestrated by AIT stay safe because files are written by AIT's executors.

## Blocked credential files

AIT's agents (planner, tools nodes, executors) do not read and do not change the files below. `list_directory` and
`find_files` still show their names marked `(protected: credential file)`, and `search_text` skips them.

| Kind | Examples |
|---|---|
| Keys & certificates | `*.pem`, `*.key`, `*.pfx`, `*.p12`, `*.jks`, `*.keystore`, `*.snk`, `*.p8`, `*.ppk`, `*.kdbx` |
| SSH keys | `id_rsa`, `id_dsa`, `id_ecdsa`, `id_ed25519`, the `.ssh` folder |
| Token files | `.git-credentials`, `.npmrc`, `.pypirc`, `.netrc`, `.pgpass`, `.htpasswd`, `secrets.json`, `auth.json`, `credentials.json`, `client_secret.json`, `.dockercfg`, `.s3cfg` |
| Profile folders | `~/.aws`, `~/.azure`, `~/.kube`, `~/.docker`, `~/.gnupg`, `~/.config/gcloud`, `~/.codex`, `~/.omp`, `%APPDATA%\Microsoft\UserSecrets`, `%APPDATA%\Microsoft\Protect`, `%APPDATA%\Microsoft\Credentials`, `%APPDATA%\9router`, `%LOCALAPPDATA%\AIThread`, ASP.NET Data Protection keys |

When the **Claude Code** CLI is pointed to AIT (the CLI menu), the equivalent `permissions.deny` rules
(`Read(//**/*.pem)`, `Read(~/.ssh/**)`, …) are added to `settings.json`. That way Claude Code itself does not read them
either. Your own rules are not touched, and when the integration is removed, only AIT's rules are deleted.

## Per-project rules

Add a `secrets` part to `.aithread/project.json` (the same file as `run` for Auto Testing). This file is committed, so it
applies to the whole team:

```json
{
  "secrets": {
    "deny":  ["config/production/**", "*.secret.json"],
    "allow": ["tests/certs/test.pem"],
    "keys":  ["LicenseCode", "ConnectionKey"]
  }
}
```

| Key | Meaning |
|---|---|
| `deny` | Globs (relative to the project root, like `.gitignore`) for extra files that must not be read or changed. A name without `/` applies in every folder. |
| `allow` | Credential files that *may* be read, for example a dummy certificate for tests. Wins over `deny` and the built-in list. |
| `keys` | Extra setting names whose values are always masked, for names that are not detected automatically. |

## Setting

**Setting → Security & privacy**:

| Option | Default | Meaning |
|---|---|---|
| Mask secrets before they are sent to a model and before they are stored | On | Turning it off sends file content, prompts and tool output as they are. Blocking credential files still applies. |
| Also mask passthrough requests | On | See the note on other CLIs above. |

This tab also shows counts since AIT started: secrets masked, distinct secrets that can be put back, how many times they
were put back, and refused credential file accesses. At the end of every thread, the report footer shows `🔒 N secrets
masked` and the activity log records it in the `keamanan` category.

## Data stored and sent

- **Stored locally**: the AIT database (SQL Server), short memory (Garnet inside the process), logs in
  `%LOCALAPPDATA%\AIThread`, and file snapshots for rollback (the file content as it is, including secrets that really
  are in your files). Long memory and the business wiki can be moved to a team database (Setting → Memory storage); their
  content is already masked.
- **Sent out**: to 9router (then to the model providers you chose), to Telegram when it is turned on, a check for the
  latest version in the npm registry (from the browser), and downloads you ask for (skill sources from GitHub/GitLab,
  Node.js and a browser for Auto Testing). AIT sends no telemetry.
- **Keys & tokens**: the 9router API key and the Telegram bot token are stored encrypted (ASP.NET Data Protection) and
  never returned through the API. A login you give for testing only exists in memory, and the model only sees the
  password as `{{password}}` ([How it works → Testing](07-cara-kerja.md#login)).
- **The Tester** may only access `localhost`.

## Limits

- Masking works from patterns and setting names. A secret in an unusual format, or written straight in code without a
  clear name (e.g. `Connect("admin", "secret")`), can slip through. Keep secrets in user-secrets, environment variables or
  a secret manager, and add special setting names to `secrets.keys`.
- Turning masking off does not remove placeholders already sent; their values can still be restored while AIT runs or
  their source file still exists.
- The vault is in memory. After a restart, an old placeholder can only be restored from a file that still holds the same
  value (AIT reads the target file again before writing). A placeholder for a value that no longer exists anywhere is
  refused, not written as it is.
