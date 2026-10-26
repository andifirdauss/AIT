# 9. API reference

Every endpoint is on the AIT host (default `http://localhost:5225`). Enums are sent as strings (`"Planner"`, `"Short"`,
`"Completed"`, …) and JSON properties use camelCase.

Messages in responses (`message`, validation errors, statuses) follow the machine's
[display language](02-konfigurasi.md#display-language); field names and enum values do not change.

## Coding CLI proxy (Claude Code, Codex, Gemini CLI, …)

| Method | Path | Details |
|---|---|---|
| `POST` | `/v1/messages` | Anthropic Messages format. Main agent requests from a session inside a project (a folder with `.git` or a build file) are orchestrated (stream or non-stream); the rest — including sessions outside a project — are forwarded to 9router. `/v1/v1/messages` is accepted too. |
| `POST` | `/v1/messages/count_tokens` | Forwarded to 9router; fallback estimate `{ "input_tokens": n }`. |
| `POST` | `/v1/chat/completions` | OpenAI Chat Completions format; the same orchestration/passthrough rules. |
| `POST` | `/v1/responses` | OpenAI Responses format (Codex CLI); the same rules. |
| `POST` | `/v1beta/models/{model}:generateContent`, `:streamGenerateContent` | Native Gemini format (the model & stream mode from the URL); the same rules. Other actions → `404`. |
| `GET` | `/v1/models` | The 9router model list; one response holds both the Anthropic and OpenAI fields. |
| `GET` | `/v1beta/models` | The 9router model list in Gemini format. |

Orchestrated replies and error messages use the format of the calling client. Auth (only checked when the *Proxy API
key* is filled in): `Authorization: Bearer <key>`, `x-api-key: <key>`, `x-goog-api-key: <key>`, or `?key=<key>`
(Gemini). Optional headers: `X-AIThread-Project` (the project root folder; `X-AIThread-Solution` with a `.sln`/`.slnx`
path from an old version is still accepted), `X-AIThread-Cwd` (working directory), `X-AIThread-Session` (session id).

An orchestrated response is one text block. The stream version uses the standard events of each format; for Anthropic:
`message_start`, `content_block_start`, `ping` (every 10 seconds while working), `content_block_delta` (`text_delta`),
`content_block_stop`, `message_delta` (`stop_reason: end_turn`), `message_stop`. Anthropic errors follow the format
`{ "type": "error", "error": { "type": "...", "message": "..." } }`.

## Claude Code hooks

Called by the HTTP hooks installed from Setting (see
[Context Keeper for /clear](03-integrasi-cli.md#context-keeper-for-clear)). Body = the Claude Code hook input (JSON); the
response is **not** wrapped in `ApiResponse`. Not redirected to HTTPS.

| Method | Path | Details |
|---|---|---|
| `POST` | `/api/hooks/claude/session-end` | Only `reason: "clear"`: records the session, then summarizes its transcript in the background. Replies with an empty `200` right away. `transcript_path` must be inside `~/.claude/projects` (or `$CLAUDE_CONFIG_DIR/projects`). |
| `POST` | `/api/hooks/claude/session-start` | Only `source: "clear"`: waits for the summary of the session `/clear`ed in the same `cwd` (at most 40 seconds), then replies `{ "hookSpecificOutput": { "hookEventName": "SessionStart", "additionalContext": "..." } }`. Without a summary: an empty `200`. |
| `POST` | `/api/hooks/claude/pre-tool-use` | The secret restorer ([Security & privacy](12-keamanan.md#restoring-the-real-values)). `{{AIT_SECRET:…}}` placeholders in `tool_input` are replaced with their real values and answered with `permissionDecision: "ask"` (or `"allow"` when `permission_mode` already accepts without asking) with `updatedInput`. An unrecognized placeholder is answered with `"deny"`. Without placeholders: an empty `200`. Only answers requests from loopback. |

## REST `/api`

Every response is wrapped:

```json
{ "success": true, "data": { }, "message": "Optional" }
```

Validation error → `400`, 9router error → `502`, not found → `404`, with `success: false` and `message`.

### Setting & 9router

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/settings` | — (also `uiLanguage` = `id`/`en`) |
| `PUT` | `/api/settings` | `nineRouterBaseUrl`, `nineRouterApiKey`, `clearNineRouterApiKey`, `proxyApiKey`, `passthroughModel`, `orchestrationEnabled`, `streamProgressToClient`, `plannerMaxSteps`, `executorMaxRetries`, `maxParallelTasks`, `requestTimeoutSeconds`, `buildEnabled` (default `true`), `buildTimeoutSeconds` (30–3600, default 300), `maxFixRounds` (0–5, default 2) |
| `PUT` | `/api/settings/language` | `{ "language": "id" \| "en" }` — this machine's display language ([Display language](02-konfigurasi.md#display-language)) |
| `POST` | `/api/settings/test-router` | `{ "baseUrl"?: "...", "apiKey"?: "..." }` — empty = use the stored values |
| `GET` | `/api/settings/claude?scope=User\|Project&projectId=` | the status of the Claude Code settings file |
| `POST` | `/api/settings/claude/apply` | `{ "scope": "Project", "projectId": "...", "proxyBaseUrl": "http://localhost:5225", "timeoutMs": null }` |
| `POST` | `/api/settings/claude/revert` | the same body as apply |
| `GET` | `/api/settings/claude/hooks?scope=User\|Project&projectId=&baseUrl=` | the status of the Context Keeper `/clear` hook in the Claude Code settings file |
| `POST` | `/api/settings/claude/hooks/apply` | installs the `/clear` hook; the same body as apply (`proxyBaseUrl` = the AIT address) |
| `POST` | `/api/settings/claude/hooks/revert` | removes AIT's `/clear` hook; other hooks stay |
| `GET` | `/api/security` | security status: `maskingEnabled`, `passthroughMasking`, `noticeAcknowledgedAt`, `masked`, `knownSecrets`, `restored`, `blockedReads` |
| `PUT` | `/api/security` | `{ "maskingEnabled": true, "passthroughMasking": true }` |
| `POST` | `/api/security/acknowledge` | the Security & privacy popup has been read (once per installation) |
| `GET` | `/api/router/status?refresh=true` | the 9router status (without `refresh` the last monitor result is used) |
| `GET` | `/api/router/models?refresh=true` | 9router models & combos (60-second cache) |

### Setup

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/setup/status?refresh=true` | the setup steps from the current state: `steps[]` (`id` = `system`/`router`/`nodes`/`project`/`cli`/`memory`, `title`, `required`, `done`, `detail`), `requiredCount`, `requiredDone`, `isComplete`. `refresh` = check 9router now (otherwise the last monitor result is used) |
| `POST` | `/api/setup/router` | `{ "baseUrl": "...", "apiKey"?: "..." }` → `status` (the shape of `/api/router/status`), `saved`, `message`. Tested first; only saved when 9router is online and the key is accepted. An empty `apiKey` = use the stored key |
| `POST` | `/api/setup/preset` | `{ "strongModel": "...", "cheapModel"?: "...", "toolsNode": true, "reviewer": false, "tester": false, "contextKeeper": true }` → `nodes[]` (`node`, `created`). Creates/updates the `AIT …` nodes (see [Setup](05-menu.md#setup)); an empty `cheapModel` = the strong model |
| `GET` | `/api/setup/profile` | the team setup profile file `aithread-setup` v1 (a JSON download, enums as names, empty fields not written) |
| `POST` | `/api/setup/profile/import` | `{ "profile": { … }, "sections"?: ["Router", "Orchestration", "Nodes", "Memory", "Business", "Skills"], "nodeMode": "Merge"\|"Replace", "secrets"?: { "nineRouterApiKey", "shortPassword", "longPassword", "businessPassword" }, "copyExistingMemory": true, "dryRun": true }` → `dryRun`, `sections` (the ones in the file), `changes[]` (`section`, `text`), `secrets[]` (`id`, `label`, `stored`), `warnings[]`, `errors[]`. Empty `sections` = all; `dryRun` defaults to `true` |

### CLI

The backend of the **CLI** menu. `{id}` = `claude-code`, `oh-my-pi-provider`, `codex`, `gemini-cli`, `opencode` or
`qwen-code`; another id → `404`. `scope` = `Global` or `Project` (only `claude-code` and `gemini-cli` have `Project`; for
the others `projectFile` in the CLI list is `null`, `/projects` returns an empty list, and the `Project` scope → `400`).

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/cli` | the CLI list (`id`, `name`, `description`, `globalPath`, `projectFile`, `note`) and the default `proxyBaseUrl` |
| `GET` | `/api/cli/{id}/status?scope=&projectId=&proxyBaseUrl=` | the file status: `path`, `fileExists`, `enabled`, `currentBaseUrl`, `preview` |
| `GET` | `/api/cli/{id}/projects?proxyBaseUrl=` | the `enabled`/`currentBaseUrl`/`path`/`error` status of each registered project |
| `PUT` | `/api/cli/{id}/toggle` | `{ "scope": "Project", "projectId": "...", "enabled": true, "proxyBaseUrl"?: "..." }` — `enabled: false` removes what AIT wrote |

### Node

| Method | Path | Body |
|---|---|---|
| `GET` | `/api/nodes` | — |
| `GET` | `/api/nodes/pipeline` | — (`isReady`, counts per role, `issues`) |
| `GET` | `/api/nodes/{id}` | — |
| `POST` | `/api/nodes` | `name`, `role` (`Planner`, `ToolsNode`, `Executor`, `Reviewer`, `Tester`, `Wrapper`, `ContextKeeper`), `model`, `temperature`, `maxTokens`, `maxConcurrency`, `priority`, `systemPrompt`, `isActive`, `skillIds` (optional, order = the order in the prompt, at most 10; not sent/`null` = the node's skills are not changed), `fallbackNodeId` (optional; a node with the same role, `null` = no fallback; 400 when the role differs, it is the node itself, or the chain loops), `toolDelegations` (optional, Planner only: `[{ tool, toolsNodeId }]`, `tool` one of `list_directory`, `find_files`, `search_text`, `find_symbol`, `find_references`, `read_file`, `search_memory`, `read_memory`, `read_thread`, `run_build`; `toolsNodeId` a node with the `ToolsNode` role; not sent/`null` = delegations are not changed; 400 when a tool is unknown/duplicated or the target is not a Tools Node), `autoTesting` (optional, Planner only: `true`/`false`; not sent/`null` = not changed; other roles are always `false`). The node response holds `toolDelegations: [{ tool, toolsNodeId, toolsNodeName, toolsNodeActive }]` and `autoTesting` |
| `PUT` | `/api/nodes/{id}` | the same as POST |
| `DELETE` | `/api/nodes/{id}` | — |
| `POST` | `/api/nodes/{id}/test` | — (`success`, `reply`, `latencyMs`, `tokens`, `error`) |

### Project

| Method | Path | Body |
|---|---|---|
| `GET` | `/api/projects` | — (each project: `name`, `rootPath`, `stacks[]`, `exists`, thread/session/memory counts, `key`, `keySource` = `File`/`Manual`/`GitRemote`/`Local`, `manualKey`) |
| `GET` | `/api/projects/{id}` | — (`project`, `modules[]` = the solution/build files inside the project, `sessions[]`) |
| `POST` | `/api/projects` | `{ "path": "D:\\Source\\MyApp" }` (a folder; a path inside a project registers that project, a file path uses its folder). `400` for a drive, Home, or a system folder |
| `POST` | `/api/projects/batch` | `{ "paths": ["D:\\Source\\A", "D:\\Source\\B"] }` → `registered`, `alreadyRegistered`, `errors` |
| `GET` | `/api/projects/browse?path=` | without `path` = the drive list. Result: `path`, `parent`, `folders[]` (`name`, `path`, `isProject`, `stacks[]`), `projects[]` (project roots in that folder: `name`, `path`, `stacks[]`, `isRegistered`, `projectId`), `quickLocations[]` |
| `GET` | `/api/projects/scan?path=&depth=4` | `depth` 1–8. Result: `root`, `projects[]` (the same shape as `browse`), `foldersScanned`, `truncated`, `elapsedMs` |
| `PUT` | `/api/projects/{id}/key` | `{ "key": "eep" }` — a manual key (empty/`null` = remove); `.aithread/project.json` still wins. Memory under the old local key moves along (so do its module list & code map) |
| `GET` | `/api/projects/{id}/codemap?search=&module=` | the code map: `path`, `module`, `layer` (`UI`/`Entry`/`Logic`/`Data`/`Infra`/`Test`), `area`, `source` (`Heuristic`/`AI`/`Manual`), `updatedAt` (at most 500 rows) |
| `PUT` | `/api/projects/{id}/codemap` | `{ "path": "src/Bl/UserBL.cs" \| "UserBL", "module": "Account", "layer": "Logic" \| null }` — a manual correction, never overwritten; a class/file name is looked up in the project files (400 when it does not exist or matches more than one) |
| `POST` | `/api/projects/{id}/codemap/scan` | maps the source files not mapped yet (at most 3,000) → `files`, `mapped`, `modules` |
| `GET` | `/api/projects/{id}/structure?rescan=false` | Code structure: `folders`, `files`, `parsedFiles`, `thirdPartyFiles`, `types`, `members`, `languages[]` (`language`, `files`, `types`), `scanMs`, `reparsed`, `scannedAt`, `truncated`, `functionsWithCalls`. Scans when the last result is older than 1 minute or `rescan=true` |
| `GET` | `/api/projects/{id}/structure/folder?path=` | the content of one folder (empty `path` = the root): `folders[]` (`name`, `path`, `files`, `types`, `thirdParty`), `files[]` (`name`, `path`, `language`, `types`, `members`, `thirdParty`, `parsed`), `hiddenFiles` (more than 1,000 files) |
| `GET` | `/api/projects/{id}/structure/file?path=` | `types[]` (`name`, `kind`, `namespace`, `container`, `line`, `isPartial`, `members[]`) and `functions[]`; a member: `name`, `kind` (`method`/`constructor`/`property`/`event`/`function`), `line`, `endLine`, `signature`, `key` (methods/functions with a body; for `/structure/calls`), `calls` (the number of calls already read by the Tools Node; `null` = never read) |
| `GET` | `/api/projects/{id}/structure/calls?key=` | the calls of one function: `path`, `symbol`, `startLine`, `endLine`, `status` (`Done`/`Failed`), `error`, `model`, `scannedAt`, `changed` (the function body differs since it was read), `calls[]` (`kind` = `Function`/`Method`/`Http`/`View`/`Sql`/`Other`, `target`, `line`, `resolvedPath`, `resolvedSymbol`, `resolvedLine`). `404` when it was never read |
| `GET` | `/api/projects/{id}/structure/groups` | the Class view: `key`, `name`, `isFolder` (files without a namespace are grouped per folder; `key` starts with `/`), `types` |
| `GET` | `/api/projects/{id}/structure/types?group=` | `name`, `container`, `kind`, `files`, `members`, `isPartial` |
| `GET` | `/api/projects/{id}/structure/class?group=&name=&container=` | a class merged from all its declarations: `files[]` (`path`, `line`), `members[]` (+ `path` when the class is spread over several files) |
| `GET` | `/api/projects/{id}/structure/search?q=` | at least 2 characters; at most 100: `kind` (`type`/`member`/`file`), `name`, `detail`, `path`, `line` |
| `DELETE` | `/api/projects/{id}?includeSharedMemory=false` | threads are kept; memory on this machine is deleted, memory in a shared store only when `includeSharedMemory=true`; when another clone with the same key is still registered, the memory moves to that clone. The unit test catalog follows the memory |

### Unit tests

A project's [unit test](07-cara-kerja.md#unit-tests) catalog, shared through the project key (`400` when the project has
no key yet). Responses **never** hold the snapshot of a test file's content; only `hasSnapshot`.

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/projects/{id}/unit-tests` | — → `projectKey`, `settings` (`mode` = `Relevant`/`All`/`Off`, `authoring` = `OnRequest`/`AutoLogic`/`Off`, `keepSnapshots`, `timeoutSeconds`, `updatedBy`, `updatedAt`), `suites[]` (`framework`, `stack`, `manifest`, `perTestResults`, `problem`), `testerAvailable`, `tests[]`: `id`, `number`, `displayNumber` (`UT-0012`), `title`, `purpose`, `scenario`, `module`, `layers[]`, `keywords[]`, `targetFiles[]`, `framework`, `testProject`, `testFile`, `className`, `methodName`, `selector`, `requires[]`, `state` (`Active`/`Quarantined`/`Obsolete`/`Removed`), `origin` (`Ait`/`Scanned`/`Regenerated`), `createdBy`, `createdThreadId`, `createdAt`, `obsoleteReason`, `obsoleteThreadId`, `obsoleteBy`, `obsoleteAt`, `quarantineReason`, `lastResult` (`Unknown`/`Passed`/`Failed`/`Error`/`Skipped`/`NotFound`), `lastRunAt`, `lastRunBy`, `lastDurationMs`, `lastMessage`, `lastSkipReason`, `hasSnapshot` |
| `PUT` | `/api/projects/{id}/unit-tests/settings` | `{ "mode": "Relevant", "authoring": "OnRequest", "keepSnapshots": true, "timeoutSeconds": null }` — `timeoutSeconds` 10–3600 or `null` (follows *Build timeout*). `keepSnapshots: false` deletes the existing snapshots |
| `POST` | `/api/projects/{id}/unit-tests/scan` | — → `found`, `added`, `updated`, `moved`, `missing` (active tests no longer found in the code), `labeled`, `warnings[]`. Labels tests with a cheap model |
| `POST` | `/api/projects/{id}/unit-tests/run` | `{ "ids": ["..."], "all": false }` (`all` = every active test; `ids` may hold quarantined/obsolete tests) → `ran`, `passed`, `failed`, `skipped`, `durationMs`, `tests[]` (`id`, `displayNumber`, `title`, `result`, `durationMs`, `message`, `skipReason`), `warnings[]`. The results and the snapshots of passing tests are recorded in the catalog |
| `GET` | `/api/projects/{id}/unit-tests/{testId}/history` | the run history (at most 20): `phase` (`baseline`/`after`/`manual`/`flaky`), `result`, `durationMs`, `message`, `skipReason`, `machine`, `runBy`, `threadId`, `runAt` |
| `POST` | `/api/projects/{id}/unit-tests/{testId}/reactivate` | — → the test. An obsolete or quarantined test is active again; `400` for other statuses |
| `GET` | `/api/projects/{id}/test-accounts` | — → `projectKey`, `canSave`, `problem` (the reason when there is no project key), `accounts[]` (`id`, `username`, `role`, `attributes` a string object, `notes`, `createdBy`, `updatedBy`, `createdAt`, `updatedAt`), `settings` (the `devPassword` in use, `devPasswordIsDefault`, `devPasswordSource` = `default`/`developer`/`chat`, `impersonation`, `sideEffectsAcknowledgedBy`, `sideEffectsAcknowledgedAt`, `updatedBy`, `updatedAt`), `attributeKeys[]` |
| `POST` | `/api/projects/{id}/test-accounts` | `{ "username": "atasan01", "role": "Manager", "attributes": { "Unit": "Sales" }, "notes": null }` → the account. A username is unique per project (case-insensitive), without spaces; at most 30 attributes, 200 accounts |
| `PUT` | `/api/projects/{id}/test-accounts/{accountId}` | the same as `POST` → the account |
| `DELETE` | `/api/projects/{id}/test-accounts/{accountId}` | — → `true` |
| `PUT` | `/api/projects/{id}/testing-settings` | `{ "devPassword": "123" }` — empty/`null` = back to the default `Test@1234` → `settings` |
| `PUT` | `/api/projects/{id}/testing-settings/impersonation` | `{ "status": "available", "how": "GET /Account/LoginAs?username={{target}}", "back": null, "roles": ["Administrator"], "confirmed": true }` — `status` `available`/`none`/`unknown`; `available` requires `how` and `roles` → `settings` |
| `POST` | `/api/projects/{id}/testing-settings/impersonation/detect` | — → `settings`. A code scan + a cheap model; the result is not confirmed (`impersonation.source` = `scan`) |
| `GET` | `/api/projects/{id}/test-scenarios` | — → scenarios[]: `id`, `slug`, `title`, `description`, `version`, `level`, `baseUrl`, `steps[]` (`order`, `role`, `action`, `target`, `instruction`, `values` an object field → value, `expect`), `roles[]` (`role`, `username`), `fields[]`, `sourceThreadId`, `sourceFollowUp`, `missing[]`, `lastOutcome`, `lastRunAt`, `lastRunBy`, `createdBy`, `updatedBy`, `createdAt`, `updatedAt` |
| `POST` | `/api/threads/{id}/follow-ups/{number}/scenario` | — → the scenario. Creates a scenario from a follow-up test suggestion (the same one again when it was made before) |
| `GET` | `/api/projects/{id}/test-scenarios/{scenarioId}/excel` | — → an `.xlsx` file (`<slug>.xlsx`) to fill in; sheet names in the display language |
| `POST` | `/api/projects/{id}/test-scenarios/import` | multipart `file` (.xlsx, at most 5 MB; sheets in either language) → `scenario`, `accountsAdded[]`, `warnings[]`. 400 with the list of wrong rows |
| `POST` | `/api/projects/{id}/test-scenarios/{scenarioId}/run` | `{ "acknowledge": true }` → the origin thread id (progress through SSE on that thread). 400 when a role has no account yet, there is no origin thread, or the side effects are not confirmed yet (`acknowledge` false) |
| `DELETE` | `/api/projects/{id}/test-scenarios/{scenarioId}` | — → `true` |
| `POST` | `/api/projects/{id}/unit-tests/{testId}/confirm-removal` | `{ "removeCode": true }` → `test`, `threadId`. Obsolete tests only: the status becomes `Removed`, the snapshot is deleted, and when `removeCode` is set and its test file exists, a Dashboard thread starts to remove that test's code (`threadId`) |

### Memory

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/memory` | `level`, `type`, `projectId`, `search`, `module`, `layer`, `kind`, `labeled` (`true`/`false`), `needsReview`, `page`, `pageSize` (at most 200) |
| `GET` | `/api/memory/stats` | — (+ `unlabeledCount`, `reviewCount`) |
| `GET` | `/api/memory/{level}/{id}` | — |
| `POST` | `/api/memory` | `{ "type": "Project", "projectId": "...", "module": "User", "extraModules": ["Invoice"], "kind": "Rule", "keywords": ["Description", "Deskripsi"], "layers": ["Logic"], "area": null, "description": "..." }` → a short memory. `module`, `kind`, at least one keyword (at most 16) and `description` are required; `extraModules` is optional (at most 3); knowledge only `Pitfall`/`Pattern`/`Architecture`. The module is matched to the module list (name/alias/plural) or added; everything is normalized to PascalCase (capitalized abbreviations in module names are kept: `MKPP`, `KlaimDF`). A memory response holds `extraModules[]`; the `module` filter of `GET /api/memory` matches the other modules too |
| `PUT` | `/api/memory/{level}/{id}` | the same as POST; an unlabeled memory becomes labeled (its old label is kept in `legacyLabel`) |
| `DELETE` | `/api/memory/{level}/{id}` | — |
| `POST` | `/api/memory/{id}/promote` | — (short → long) |
| `POST` | `/api/memory/consolidate` | — (the number moved) |
| `GET` | `/api/memory/export` | `level`, `type`, `projectId`, `search`, `includeUnitTests` (default `true`) → an `aithread-memory` JSON file; the unit test catalog in `unitTests[]` (with snapshots), left out when `search` is filled in or `type=Knowledge` (see [Memory](06-memory.md#export--import)) |
| `POST` | `/api/memory/import` | body = an export file (v1–v4); query `mode` (`Skip` default / `Overwrite`; does not apply to unit tests), `dryRun` (`true` = summary only). The result also holds `unitTestsAdded`, `unitTestsUpdated`, `unitTestsRenumbered`, `unitTestsSkipped` |
| `GET` | `/api/memory/modules?projectId=&knowledge=false` | the module list: `name`, `aliases[]`, `description`, `memories`, `files` (`knowledge=true` = the knowledge memory modules) |
| `PUT` | `/api/memory/modules/{name}?projectId=&knowledge=` | `{ "name": "KlaimDF", "description": "...", "aliases": ["Pengguna"] }`; every field is optional. `name` renames the module (it may change only upper/lower case): memories, the code map and the wiki follow; a name used by another module is refused |
| `POST` | `/api/memory/modules/merge?knowledge=` | `{ "projectId": "...", "from": "Pengguna", "into": "User" }` → the merge history |
| `POST` | `/api/memory/modules/check?projectId=&knowledge=` | checks for duplicate modules now → the number of merges + suggestions |
| `GET` | `/api/memory/merges?projectId=&knowledge=` | the history: `from`, `into`, `status` (`Applied`/`Undone`/`Suggested`/`Rejected`), `reason`, `confidence`, `decidedBy`, the counts of `memories`/`files`/`pages` |
| `POST` | `/api/memory/merges/{id}/undo` \| `accept` \| `reject` | undoes an applied merge; accepts or rejects a suggestion |
| `GET` | `/api/memory/review?projectId=` | `memories[]` (needing review), `merges[]` (suggestions), `duplicates[]` (`first`, `second`, `similarity`) |
| `GET` | `/api/memory/relabel` | the label migration status: `running`, `phase`, `pending`, `labeled`, `needsReview`, `lastRunAt`, `lastMessage`, `backupPath`, `detail` (what that phase is working on, e.g. `KN2018_KCO (1/5) · 12/40 files named in memory`) |
| `POST` | `/api/memory/relabel` | runs the label migration now (in the background) |
| `GET` | `/api/memory/changelog` | the status of migrating old memory to module summaries: `running`, `pendingFacts`, `pendingModules`, `shortFacts` (still in short memory), `summaries`, `done`/`total`/`module` (the running pass), `lastRunAt`, `lastMessage`, `backupPath`, `aiAvailable`, `totalFacts`/`migratedFacts` (the overall progress, kept after a restart) |
| `GET` | `/api/settings/memory-storage` | — (`shortMemory`, `longMemory`, `businessMemory` with the status of each; passwords are never returned) |
| `PUT` | `/api/settings/memory-storage` | `{ "shortMemory": {…}, "longMemory": {…}, "businessMemory": {…}, "copyExistingData": true }` — `businessMemory` may be left out (the store does not change). Result: `settings`, `copiedShort`, `copiedLong`, `copiedBusiness`, `warnings[]` |
| `POST` | `/api/settings/memory-storage/test` | `{ "level": "Short"\|"Long"\|"Business", "store": {…} }` |

A memory item carries `projectKey`; `projectId`/`projectName` point to the project on this machine (another clone of the
same project when the memory was written by another developer). Structured labels: `label` (the display `Module · Layer ·
Kind`, or the old label when unlabeled), `module`, `layers[]`, `kind`, `area`, `keywords[]`, `labeled`, `legacyLabel`,
`reviewReason`.

### Business memory (business wiki)

See [Business memory](06-memory.md#business-memory-business-wiki). `projectId` is always the id of the project on this
machine; pages are stored per project key.

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/business/projects/{projectId}` | — (`projectKey`, `keyIsShared`, `pages[]` without content, `pendingThreads`, `lastDigestAt`, `digestRunning`, `storeDescription`) |
| `GET` | `/api/business/pages/{id}` | — (`page`, the Markdown `content`, a ready-to-show `html`) |
| `POST` | `/api/business/pages` | `{ "projectId": "...", "module": "TO", "title": "Transfer Order", "summary": "...", "labels": "to, transfer order", "content": "# …", "autoUpdate": true, "changeNote": "…" }` |
| `PUT` | `/api/business/pages/{id}` | the same body + `version` (the version being edited). The page was changed by someone else → `409` |
| `DELETE` | `/api/business/pages/{id}` | — ; with its revisions and the images no other page uses |
| `GET` | `/api/business/pages/{id}/revisions` | — (`version`, `title`, `changeNote`, `author`, `editKind` = `Ai`/`Manual`/`Restore`, `sourceThreadIds[]`) |
| `GET` | `/api/business/pages/{id}/revisions/{version}` | — (+ `content`) |
| `POST` | `/api/business/pages/{id}/revisions/{version}/restore` | — ; becomes a new version |
| `POST` | `/api/business/preview` | `{ "content": "# …" }` → HTML (the same as a stored page) |
| `POST` | `/api/business/assets` | `{ "projectId": "...", "pageId": null, "data": "data:image/png;base64,…", "caption": "Form TO" }` — PNG/JPEG/GIF/WebP at most 5 MB (checked from the file content); the same image is stored once per project. Result: `id`, `url` |
| `GET` | `/api/business/assets/{id}` | the image file (permanent cache) |
| `POST` | `/api/business/digest?projectId=` | summarizes the waiting threads now → `threads`, `pagesCreated`, `pagesUpdated`, `images`, `warnings[]`. `400` when another summary is running |
| `GET` | `/api/business/settings` | — (`enabled`, `intervalHours`, `model`, `developerName`, `effectiveDeveloperName`) |
| `PUT` | `/api/business/settings` | `{ "enabled": true, "intervalHours": 6, "model": null, "developerName": "Andi" }` |

### Thread & dashboard

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/threads` | `take` (default 30, at most 200), `projectId` |
| `GET` | `/api/threads/{id}` | — (the summary, `planJson`, the final response, metrics including `reviewerModel`/`reviewerTokens`/`verificationMs`, tasks, `verificationJson`: `outcome` `passed`/`failed`/`unverified`, `command`, `reviewerModel`, `rounds[]` holding `build` (`status`, `headline`, `errors[]`, …), `review` (`approved`, `summary`, `issues[]`, `error`), `unitTests` (the unit test summary of the round, the same shape as `unitTestJson.rounds[]`), `fixTasks[]`, `unfixable[]`; `unitTestMs` and `unitTestJson`: `outcome` `passed`/`failed`/`skipped`, `mode`, `testOnly`, `suites[]`, `problems[]`, `restored[]` (`number`, `file`, `how` = `moved`/`snapshot`/`method`/`task`/`regenerate`/`missing`, `note`), `notes[]`, `baselineRan`, `baselineCached`, `baselineMs`, `rounds[]` (`round`, `ran`, `passed`, `failed`, `skipped`, `durationMs`, `commands[]`, `regressions[]`, `preexisting[]`, `triage`, `testerModel`, `fixTargets[]`), `tests[]` (`id`, `number`, `title`, `file`, `decision`, `baseline`, `baselineCached`, `after`, `verdict` = `ok`/`fixed`/`regression`/`preexisting`/`new-failing`/`failing`/`flaky`/`skipped`/`notfound`/`missing`/`obsolete`, `message`, `skipReason`, `triage`, `triageReason`, `durationMs`), `testerModel`, `summary`; `testerModel`/`testerTokens` (including unit test triage)/`testingMs`, and `testingJson`: `outcome` `passed`/`failed`/`blocked`/`skipped`, `level`, `baseUrl`, `browser`, `testerModel`, `summary`, `error`, `steps`, `scenarios[]` (`title`, `status`, `details`), `requests[]` (`method`, `url`, `requestBody`, `status`, `reason`, `contentType`, `location`, `responseBody`, `durationMs`, `error`; the login password masked as `{{password}}`), `browserErrors[]`, `loginUser`, `loginNeeded`/`loginUrl` (the tester stopped to ask for a login), `askedLogin` (the question was sent to the chat)) |
| `POST` | `/api/threads` | `{ "projectId": "...", "prompt": "..." }` → the thread id, runs in the background |
| `POST` | `/api/threads/{id}/cancel` | — |
| `POST` | `/api/threads/{id}/retry` | — → the thread id. Runs the plan tasks that did not succeed again in the background (without the planner). 400 when the thread is running, was rolled back, or there is nothing to retry. |
| `POST` | `/api/threads/{id}/test` | `{ "username"?: "...", "password"?: "..." }` (optional; a login for this testing, not stored) → the thread id. Runs Auto Testing again from the stored testing plan in the background (the code is not changed). 400 when only one of username/password is filled in, the thread is running, is not *Completed*, was rolled back, has no UI/API testing plan, or there is no active Tester node. |
| `POST` | `/api/threads/{id}/follow-ups/{number}/run` | — → the thread id. Runs one follow-up test suggested by the Tester (the `followUps` part of `testingJson`) in the background: the scenarios of that suggestion, the project's test accounts, the remembered values (`values`). Its result is in `followUps[].lastRun`; new suggestions from that run are added. 400 for the same reasons as `/test`, or when the suggestion number does not exist. |
| `GET` | `/api/threads/{id}/rollback` | — → a preview `{ canRollback, reason, files[]: { filePath, action, problem } }`; `action` = `restore` / `delete` / `unchanged` / `conflict`. Changes nothing. |
| `POST` | `/api/threads/{id}/rollback` | — → `{ restored, deleted, files[] }`. All or nothing: 400 without touching any file when there is a `conflict`. |
| `GET` | `/api/dashboard/summary` | — |
| `GET` | `/api/dashboard/logs` | `take` (default 200), `minLevel` (`Debug`/`Info`/`Success`/`Warning`/`Error`), `threadId` |
| `GET` | `/api/health` | — |

### System

Used by `scripts/aithread.ps1` for the background instance.

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/system` | — (`processId`, `startedAt`, `environment`, `contentRoot`) |
| `POST` | `/api/system/shutdown` | — ; requires the `X-AIThread-Shutdown` header and only from localhost, otherwise `403`. Shuts AIT down cleanly: running threads are cancelled and recorded first. |

### Telegram

The Telegram card on the Setting page — see [Prompts from Telegram](11-telegram.md).

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/telegram` | — (`enabled`, `hasBotToken`, `botTokenMasked`, `state` = `Disabled`/`Starting`/`Running`/`Error`, `botUsername`, `error`, `lastPollAt`, `chats[]`) |
| `PUT` | `/api/telegram` | `{ "enabled": true, "botToken": "123456789:AAE…", "clearBotToken": false }` — an empty `botToken` = keep the stored token. A new token is verified with Telegram (`getMe`); rejected → `400`. |
| `POST` | `/api/telegram/chats/{id}/allow` | — ; allows the chat, the bot sends a confirmation to that chat |
| `DELETE` | `/api/telegram/chats/{id}` | — ; deletes the chat. An allowed chat has its access revoked and is told. |

`chats[]`: `id`, `chatId`, `displayName`, `username`, `isAllowed`, `activeProjectId`, `activeProjectName`, `createdAt`,
`lastMessageAt`; chats waiting for permission come first. The token is never returned (only `botTokenMasked`).

### Skill

The Skill tab on the Setting page and the skill suggestions in the Node modal — see
[Skills on a node](07-cara-kerja.md#skills-on-a-node).

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/skills/settings` | — (`model`, `maxPromptChars`, `storeDirectory`) |
| `PUT` | `/api/skills/settings` | `{ "model": "cc/claude-opus-4", "maxPromptChars": 40000 }` — `model` `null` = the model of the active Planner node |
| `GET` | `/api/skills/sources` | — (`id`, `url`, `name`, `kind` = `GitHub`/`GitLab`/`Git`/`RawFile`/`Web`, `ref`, `subPath`, `status` = `Queued`/`Fetching`/`Analyzing`/`Ready`/`Failed`, `error`, `skillCount`, `lastPulledAt`) |
| `POST` | `/api/skills/sources` | `{ "url": "https://github.com/anthropics/skills" }` — an invalid or existing link → `400`; the fetch runs in the background |
| `POST` | `/api/skills/sources/{id}/sync` | — ; fetches again (`400` while it is still being processed) |
| `DELETE` | `/api/skills/sources/{id}` | — ; deletes the source, its skills (and their node attachments), and its folder |
| `GET` | `/api/skills?query=` | the skill list (`id`, `sourceId`, `sourceName`, `name`, `description`, `tags`, `relativeDir`, `nodeCount`, `snippet`); `query` searches the name, description, tags, source and skill content — matches in the name/description come first, ones that only match in the content carry a `snippet` (text around the keyword) |
| `GET` | `/api/skills/{id}` | the detail + `content`, `folder`, `entryFile`, `files[]`, `nodeNames[]` |
| `POST` | `/api/skills/suggest` | `{ "prompt": "I need you to be a reliable engineer", "role": "Executor" }` → at most 6 suggestions (`id`, `name`, `description`, `sourceName`, `reason`, `score` 0–1, `recommended`). An empty store → `400`; 9router failed → `502`. |

`GET /api/nodes` and `GET /api/nodes/{id}` include `skills[]` (`id`, `name`, `description`, `sourceName`) in prompt order,
plus `fallbackNodeId` and `fallbackNodeName`.

## SSE `/api/stream`

```
GET /api/stream?types=log,router,thread     # filter by event type prefix
GET /api/stream?threadId={id}               # every event of one thread
```

Every event is named after its type, with the data `{ "data": …, "threadId": "…", "timestamp": "…" }`. A `ping` event is
sent every 15 seconds when it is quiet.

| Event | `data` |
|---|---|
| `log` | a log entry: `level`, `category`, `message`, `threadId`, `projectId`, `createdAt` |
| `router.status` | the 9router status (sent every monitor interval; also when the connection opens without `threadId`) |
| `thread.created` | `id`, `projectId`, `status` |
| `thread.updated` | `id`, `status`, `planSummary`, `totalMs`, `error` |
| `thread.output` | planner activity: `{ "kind": "planner.tool", "tool": "read_file", "detail": "…" }` |
| `task.updated` | the full task object (status, node, duration, diff, error, …) |
| `task.output` | a piece of executor model output: `taskId`, `taskKey`, `text` |
| `memory.changed` | `id`, `action` (`created`, `updated`, `deleted`, `promoted`, `consolidated`, `imported`, `relabel`, `summary`, `changelog`, `merged`, `unmerged`, `suggested`, `rejected`) |
| `project.changed` | `id`, `action` |
| `node.changed` | `id`, `action` |
| `telegram.changed` | `action` (`status`, `created`, `allowed`, `deleted`, `project`); the chat `id` for chat changes, `status` for bot status changes |
| `skill.changed` | `sourceId`, `status` (the source status), `action` (`added`, `deleted`) when present, `error` when it failed |
| `business.changed` | `action` (`created`, `updated`, `deleted`, `running`, `finished`, `settings`), the page `id` and/or `projectKey` |
| `ping` | — |

Example in the browser:

```js
const source = new EventSource('/api/stream?types=task,thread');
source.addEventListener('task.updated', e => console.log(JSON.parse(e.data).data));
```
