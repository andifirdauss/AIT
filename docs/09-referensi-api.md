# 9. Referensi API

Semua endpoint berada di host AiThread (default `http://localhost:5225`). Enum dikirim sebagai string
(`"Planner"`, `"Short"`, `"Completed"`, …) dan properti JSON memakai camelCase.

## Proxy agen coding (Claude Code, Codex, Gemini CLI, …)

| Method | Path | Keterangan |
|---|---|---|
| `POST` | `/v1/messages` | Format Anthropic Messages. Request agen utama dari sesi di dalam sebuah project (folder dengan `.git` atau file build) diorkestrasi (stream atau non-stream); lainnya — termasuk sesi di luar project — diteruskan ke 9router. `/v1/v1/messages` juga diterima. |
| `POST` | `/v1/messages/count_tokens` | Diteruskan ke 9router; fallback estimasi `{ "input_tokens": n }`. |
| `POST` | `/v1/chat/completions` | Format OpenAI Chat Completions; aturan orkestrasi/passthrough sama. |
| `POST` | `/v1/responses` | Format OpenAI Responses (Codex CLI); aturan sama. |
| `POST` | `/v1beta/models/{model}:generateContent`, `:streamGenerateContent` | Format Gemini native (model & mode stream dari URL); aturan sama. Aksi lain → `404`. |
| `GET` | `/v1/models` | Daftar model 9router; satu respons memuat field Anthropic dan OpenAI. |
| `GET` | `/v1beta/models` | Daftar model 9router dalam format Gemini. |

Balasan orkestrasi dan pesan error memakai format klien yang memanggil. Auth (hanya diperiksa bila *Proxy API key*
diisi): `Authorization: Bearer <key>`, `x-api-key: <key>`, `x-goog-api-key: <key>`, atau `?key=<key>` (Gemini).
Header opsional: `X-AiThread-Project` (folder root project; `X-AiThread-Solution` dengan path `.sln`/`.slnx` dari versi
lama masih diterima), `X-AiThread-Cwd` (working directory), `X-AiThread-Session` (id sesi).

Respons orkestrasi berupa satu blok teks. Versi stream memakai event standar: `message_start`,
`content_block_start`, `ping` (tiap 10 detik selama bekerja), `content_block_delta` (`text_delta`),
`content_block_stop`, `message_delta` (`stop_reason: end_turn`), `message_stop`. Error mengikuti format
`{ "type": "error", "error": { "type": "...", "message": "..." } }`.

## Hook Claude Code

Dipanggil oleh HTTP hook yang dipasang dari Setting (lihat
[Context Keeper untuk /clear](03-integrasi-claude-code.md#context-keeper-untuk-clear)). Body = input hook Claude Code
(JSON); respons **tidak** dibungkus `ApiResponse`. Tidak dialihkan ke HTTPS.

| Method | Path | Keterangan |
|---|---|---|
| `POST` | `/api/hooks/claude/session-end` | Hanya `reason: "clear"`: mencatat sesi, lalu meringkas transcript-nya di latar. Membalas `200` kosong seketika. `transcript_path` harus berada di `~/.claude/projects` (atau `$CLAUDE_CONFIG_DIR/projects`). |
| `POST` | `/api/hooks/claude/session-start` | Hanya `source: "clear"`: menunggu ringkasan sesi yang di-`/clear` di `cwd` yang sama (maks 40 detik), lalu membalas `{ "hookSpecificOutput": { "hookEventName": "SessionStart", "additionalContext": "..." } }`. Tanpa ringkasan: `200` kosong. |

## REST `/api`

Semua respons dibungkus:

```json
{ "success": true, "data": { }, "message": "Opsional" }
```

Error validasi → `400`, error 9router → `502`, tidak ditemukan → `404`, dengan `success: false` dan `message`.

### Setting & 9router

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/settings` | — |
| `PUT` | `/api/settings` | `nineRouterBaseUrl`, `nineRouterApiKey`, `clearNineRouterApiKey`, `proxyApiKey`, `passthroughModel`, `orchestrationEnabled`, `streamProgressToClient`, `plannerMaxSteps`, `executorMaxRetries`, `maxParallelTasks`, `requestTimeoutSeconds`, `buildEnabled` (default `true`), `buildTimeoutSeconds` (30–3600, default 300), `maxFixRounds` (0–5, default 2) |
| `POST` | `/api/settings/test-router` | `{ "baseUrl"?: "...", "apiKey"?: "..." }` — kosong = pakai nilai tersimpan |
| `GET` | `/api/settings/claude?scope=User\|Project&projectId=` | status file settings Claude Code |
| `POST` | `/api/settings/claude/apply` | `{ "scope": "Project", "projectId": "...", "proxyBaseUrl": "http://localhost:5225", "timeoutMs": null }` |
| `POST` | `/api/settings/claude/revert` | body sama dengan apply |
| `GET` | `/api/settings/claude/hooks?scope=User\|Project&projectId=&baseUrl=` | status hook `/clear` Context Keeper di file settings Claude Code |
| `POST` | `/api/settings/claude/hooks/apply` | pasang hook `/clear`; body sama dengan apply (`proxyBaseUrl` = alamat AiThread) |
| `POST` | `/api/settings/claude/hooks/revert` | lepas hook `/clear` AiThread; hook lain tetap |
| `GET` | `/api/router/status?refresh=true` | status 9router (tanpa `refresh` memakai hasil monitor terakhir) |
| `GET` | `/api/router/models?refresh=true` | model & combo 9router (cache 60 detik) |

### CLI

Backend menu **CLI**. `{id}` = `claude-code` atau `oh-my-pi-provider`; id lain → `404`. `scope` = `Global` atau `Project`
(`oh-my-pi-provider` hanya `Global`; `projectFile` di daftar CLI bernilai `null` dan `/projects` mengembalikan daftar kosong).

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/cli` | daftar CLI (`id`, `name`, `description`, `globalPath`, `projectFile`, `note`) dan `proxyBaseUrl` default |
| `GET` | `/api/cli/{id}/status?scope=&projectId=&proxyBaseUrl=` | status file: `path`, `fileExists`, `enabled`, `currentBaseUrl`, `preview` |
| `GET` | `/api/cli/{id}/projects?proxyBaseUrl=` | status `enabled`/`currentBaseUrl`/`path`/`error` tiap project terdaftar |
| `PUT` | `/api/cli/{id}/toggle` | `{ "scope": "Project", "projectId": "...", "enabled": true, "proxyBaseUrl"?: "..." }` — `enabled: false` menghapus yang ditulis AiThread |

### Node

| Method | Path | Body |
|---|---|---|
| `GET` | `/api/nodes` | — |
| `GET` | `/api/nodes/pipeline` | — (`isReady`, jumlah per peran, `issues`) |
| `GET` | `/api/nodes/{id}` | — |
| `POST` | `/api/nodes` | `name`, `role` (`Planner`, `Executor`, `Reviewer`, `Wrapper`, `ContextKeeper`), `model`, `temperature`, `maxTokens`, `maxConcurrency`, `priority`, `systemPrompt`, `isActive` |
| `PUT` | `/api/nodes/{id}` | sama dengan POST |
| `DELETE` | `/api/nodes/{id}` | — |
| `POST` | `/api/nodes/{id}/test` | — (`success`, `reply`, `latencyMs`, `tokens`, `error`) |

### Project

| Method | Path | Body |
|---|---|---|
| `GET` | `/api/projects` | — (tiap project: `name`, `rootPath`, `stacks[]`, `exists`, jumlah thread/sesi/memory) |
| `GET` | `/api/projects/{id}` | — (`project`, `modules[]` = file solution/build di dalam project, `sessions[]`) |
| `POST` | `/api/projects` | `{ "path": "D:\\Source\\MyApp" }` (folder; path di dalam project mendaftarkan project itu, path file memakai foldernya). `400` untuk drive, Home, atau folder sistem |
| `POST` | `/api/projects/batch` | `{ "paths": ["D:\\Source\\A", "D:\\Source\\B"] }` → `registered`, `alreadyRegistered`, `errors` |
| `GET` | `/api/projects/browse?path=` | tanpa `path` = daftar drive. Hasil: `path`, `parent`, `folders[]` (`name`, `path`, `isProject`, `stacks[]`), `projects[]` (root project di folder itu: `name`, `path`, `stacks[]`, `isRegistered`, `projectId`), `quickLocations[]` |
| `GET` | `/api/projects/scan?path=&depth=4` | `depth` 1–8. Hasil: `root`, `projects[]` (bentuk sama dengan `browse`), `foldersScanned`, `truncated`, `elapsedMs` |
| `DELETE` | `/api/projects/{id}` | — (thread dipertahankan, memory project dihapus) |

### Memory

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/memory` | `level`, `type`, `projectId`, `search`, `page`, `pageSize` (maks 200) |
| `GET` | `/api/memory/stats` | — |
| `GET` | `/api/memory/{level}/{id}` | — |
| `POST` | `/api/memory` | `{ "type": "Project", "projectId": "...", "label": "...", "description": "..." }` → short memory |
| `PUT` | `/api/memory/{level}/{id}` | sama dengan POST |
| `DELETE` | `/api/memory/{level}/{id}` | — |
| `POST` | `/api/memory/{id}/promote` | — (short → long) |
| `POST` | `/api/memory/consolidate` | — (jumlah yang dipindah) |

### Thread & dashboard

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/threads` | `take` (default 30, maks 200), `projectId` |
| `GET` | `/api/threads/{id}` | — (ringkasan, `planJson`, respons akhir, metrik termasuk `reviewerModel`/`reviewerTokens`/`verificationMs`, tugas, `verificationJson`: `outcome` `passed`/`failed`/`unverified`, `command`, `reviewerModel`, `rounds[]` berisi `build` (`status`, `headline`, `errors[]`, …), `review` (`approved`, `summary`, `issues[]`, `error`), `fixTasks[]`, `unfixable[]`) |
| `POST` | `/api/threads` | `{ "projectId": "...", "prompt": "..." }` → id thread, berjalan di belakang |
| `POST` | `/api/threads/{id}/cancel` | — |
| `GET` | `/api/dashboard/summary` | — |
| `GET` | `/api/dashboard/logs` | `take` (default 200), `minLevel` (`Debug`/`Info`/`Success`/`Warning`/`Error`), `threadId` |
| `GET` | `/api/health` | — |

### Sistem

Dipakai `scripts/aithread.ps1` untuk instance background.

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/system` | — (`processId`, `startedAt`, `environment`, `contentRoot`) |
| `POST` | `/api/system/shutdown` | — ; wajib header `X-AiThread-Shutdown` dan hanya dari localhost, selain itu `403`. Mematikan AiThread dengan rapi: thread berjalan dibatalkan dan dicatat dulu. |

### Telegram

Kartu Telegram di halaman Setting — lihat [Prompt dari Telegram](11-telegram.md).

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/telegram` | — (`enabled`, `hasBotToken`, `botTokenMasked`, `state` = `Disabled`/`Starting`/`Running`/`Error`, `botUsername`, `error`, `lastPollAt`, `chats[]`) |
| `PUT` | `/api/telegram` | `{ "enabled": true, "botToken": "123456789:AAE…", "clearBotToken": false }` — `botToken` kosong = pertahankan token tersimpan. Token baru diverifikasi ke Telegram (`getMe`); ditolak → `400`. |
| `POST` | `/api/telegram/chats/{id}/allow` | — ; mengizinkan chat, bot mengirim konfirmasi ke chat itu |
| `DELETE` | `/api/telegram/chats/{id}` | — ; menghapus chat. Chat yang sudah diizinkan dicabut aksesnya dan diberi tahu. |

`chats[]`: `id`, `chatId`, `displayName`, `username`, `isAllowed`, `activeProjectId`, `activeProjectName`, `createdAt`,
`lastMessageAt`; chat yang menunggu izin tampil lebih dulu. Token tidak pernah dikembalikan (hanya `botTokenMasked`).

## SSE `/api/stream`

```
GET /api/stream?types=log,router,thread     # filter prefix tipe event
GET /api/stream?threadId={id}               # semua event satu thread
```

Setiap event punya nama sesuai tipenya dan data `{ "data": …, "threadId": "…", "timestamp": "…" }`.
Event `ping` dikirim tiap 15 detik saat sepi.

| Event | `data` |
|---|---|
| `log` | entri log: `level`, `category`, `message`, `threadId`, `projectId`, `createdAt` |
| `router.status` | status 9router (dikirim tiap interval monitor; juga saat koneksi dibuka tanpa `threadId`) |
| `thread.created` | `id`, `projectId`, `status` |
| `thread.updated` | `id`, `status`, `planSummary`, `totalMs`, `error` |
| `thread.output` | aktivitas planner: `{ "kind": "planner.tool", "tool": "read_file", "detail": "…" }` |
| `task.updated` | objek tugas lengkap (status, node, durasi, diff, error, …) |
| `task.output` | potongan output model executor: `taskId`, `taskKey`, `text` |
| `memory.changed` | `id`, `action` (`created`, `updated`, `deleted`, `promoted`, `consolidated`) |
| `project.changed` | `id`, `action` |
| `node.changed` | `id`, `action` |
| `telegram.changed` | `action` (`status`, `created`, `allowed`, `deleted`, `project`); `id` chat untuk perubahan chat, `status` untuk perubahan status bot |
| `ping` | — |

Contoh di browser:

```js
const source = new EventSource('/api/stream?types=task,thread');
source.addEventListener('task.updated', e => console.log(JSON.parse(e.data).data));
```
