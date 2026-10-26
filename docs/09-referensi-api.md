# 9. Referensi API

Semua endpoint berada di host AIT (default `http://localhost:5225`). Enum dikirim sebagai string
(`"Planner"`, `"Short"`, `"Completed"`, …) dan properti JSON memakai camelCase.

Pesan di respons (`message`, error validasi, status) mengikuti [bahasa tampilan](02-konfigurasi.md#bahasa-tampilan)
mesin; nama field dan nilai enum tidak berubah.

## Proxy CLI coding (Claude Code, Codex, Gemini CLI, …)

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
Header opsional: `X-AIThread-Project` (folder root project; `X-AIThread-Solution` dengan path `.sln`/`.slnx` dari versi
lama masih diterima), `X-AIThread-Cwd` (working directory), `X-AIThread-Session` (id sesi).

Respons orkestrasi berupa satu blok teks. Versi stream memakai event standar format masing-masing; untuk Anthropic: `message_start`,
`content_block_start`, `ping` (tiap 10 detik selama bekerja), `content_block_delta` (`text_delta`),
`content_block_stop`, `message_delta` (`stop_reason: end_turn`), `message_stop`. Error Anthropic mengikuti format
`{ "type": "error", "error": { "type": "...", "message": "..." } }`.

## Hook Claude Code

Dipanggil oleh HTTP hook yang dipasang dari Pengaturan (lihat
[Context Keeper untuk /clear](03-integrasi-cli.md#context-keeper-untuk-clear)). Body = input hook Claude Code
(JSON); respons **tidak** dibungkus `ApiResponse`. Tidak dialihkan ke HTTPS.

| Method | Path | Keterangan |
|---|---|---|
| `POST` | `/api/hooks/claude/session-end` | Hanya `reason: "clear"`: mencatat sesi, lalu meringkas transcript-nya di latar. Membalas `200` kosong seketika. `transcript_path` harus berada di `~/.claude/projects` (atau `$CLAUDE_CONFIG_DIR/projects`). |
| `POST` | `/api/hooks/claude/session-start` | Hanya `source: "clear"`: menunggu ringkasan sesi yang di-`/clear` di `cwd` yang sama (maks 40 detik), lalu membalas `{ "hookSpecificOutput": { "hookEventName": "SessionStart", "additionalContext": "..." } }`. Tanpa ringkasan: `200` kosong. |
| `POST` | `/api/hooks/claude/pre-tool-use` | Pemulih secret ([Keamanan & privasi](12-keamanan.md#pemulihan-nilai-asli)). Placeholder `{{AIT_SECRET:…}}` di `tool_input` diganti nilai asli dan dibalas `permissionDecision: "ask"` (atau `"allow"` bila `permission_mode` sudah menerima tanpa bertanya) dengan `updatedInput`. Placeholder yang tidak dikenali dibalas `"deny"`. Tanpa placeholder: `200` kosong. Hanya menjawab request dari loopback. |

## REST `/api`

Semua respons dibungkus:

```json
{ "success": true, "data": { }, "message": "Opsional" }
```

Error validasi → `400`, error 9router → `502`, tidak ditemukan → `404`, dengan `success: false` dan `message`.

### Pengaturan & 9router

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/settings` | — (juga `uiLanguage` = `id`/`en`) |
| `PUT` | `/api/settings` | `nineRouterBaseUrl`, `nineRouterApiKey`, `clearNineRouterApiKey`, `proxyApiKey`, `passthroughModel`, `orchestrationEnabled`, `streamProgressToClient`, `plannerMaxSteps`, `executorMaxRetries`, `maxParallelTasks`, `requestTimeoutSeconds`, `buildEnabled` (default `true`), `buildTimeoutSeconds` (30–3600, default 300), `maxFixRounds` (0–5, default 2) |
| `PUT` | `/api/settings/language` | `{ "language": "id" \| "en" }` — bahasa tampilan mesin ini ([Bahasa tampilan](02-konfigurasi.md#bahasa-tampilan)) |
| `POST` | `/api/settings/test-router` | `{ "baseUrl"?: "...", "apiKey"?: "..." }` — kosong = pakai nilai tersimpan |
| `GET` | `/api/settings/claude?scope=User\|Project&projectId=` | status file settings Claude Code |
| `POST` | `/api/settings/claude/apply` | `{ "scope": "Project", "projectId": "...", "proxyBaseUrl": "http://localhost:5225", "timeoutMs": null }` |
| `POST` | `/api/settings/claude/revert` | body sama dengan apply |
| `GET` | `/api/settings/claude/hooks?scope=User\|Project&projectId=&baseUrl=` | status hook `/clear` Context Keeper di file settings Claude Code |
| `POST` | `/api/settings/claude/hooks/apply` | pasang hook `/clear`; body sama dengan apply (`proxyBaseUrl` = alamat AIT) |
| `POST` | `/api/settings/claude/hooks/revert` | lepas hook `/clear` AIT; hook lain tetap |
| `GET` | `/api/security` | status keamanan: `maskingEnabled`, `passthroughMasking`, `noticeAcknowledgedAt`, `masked`, `knownSecrets`, `restored`, `blockedReads` |
| `PUT` | `/api/security` | `{ "maskingEnabled": true, "passthroughMasking": true }` |
| `POST` | `/api/security/acknowledge` | popup Keamanan & privasi sudah dibaca (sekali per instalasi) |
| `GET` | `/api/security/secrets` | secret yang terdeteksi sejak AIT berjalan + daftar bukan secret: `items[]` (`id`, `label`, `preview`, `length`, `count`, `firstSeen`, `lastSeen`, `allowed`, `allowedAt`), `total` |
| `POST` | `/api/security/secrets/{id}/allow` | tandai bukan secret (id placeholder 10 karakter); 404 bila AIT tidak lagi mengenal id itu |
| `DELETE` | `/api/security/secrets/{id}/allow` | samarkan lagi |
| `GET` | `/api/security/secrets/{id}/value` | nilai asli (`id`, `value`); hanya dari komputer ini (loopback), 404 setelah restart |
| `GET` | `/api/router/status?refresh=true` | status 9router (tanpa `refresh` memakai hasil monitor terakhir) |
| `GET` | `/api/router/models?refresh=true` | model & combo 9router (cache 60 detik) |

### Penyiapan

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/setup/status?refresh=true` | langkah setup dari kondisi saat ini: `steps[]` (`id` = `system`/`router`/`nodes`/`project`/`cli`/`memory`, `title`, `required`, `done`, `detail`), `requiredCount`, `requiredDone`, `isComplete`. `refresh` = cek 9router sekarang (tanpa itu memakai hasil monitor terakhir) |
| `POST` | `/api/setup/router` | `{ "baseUrl": "...", "apiKey"?: "..." }` → `status` (bentuk `/api/router/status`), `saved`, `message`. Diuji dulu; disimpan hanya bila 9router online dan key diterima. `apiKey` kosong = pakai key tersimpan |
| `POST` | `/api/setup/preset` | `{ "strongModel": "...", "cheapModel"?: "...", "toolsNode": true, "reviewer": false, "tester": false, "contextKeeper": true }` → `nodes[]` (`node`, `created`). Membuat/memperbarui node `AIT …` (lihat [Penyiapan](05-menu.md#penyiapan)); `cheapModel` kosong = model kuat |
| `GET` | `/api/setup/profile` | file profil setup tim `aithread-setup` v1 (unduhan JSON, enum berupa nama, field kosong tidak ditulis) |
| `POST` | `/api/setup/profile/import` | `{ "profile": { … }, "sections"?: ["Router", "Orchestration", "Nodes", "Memory", "Business", "Skills"], "nodeMode": "Merge"\|"Replace", "secrets"?: { "nineRouterApiKey", "shortPassword", "longPassword", "businessPassword" }, "copyExistingMemory": true, "dryRun": true }` → `dryRun`, `sections` (yang ada di file), `changes[]` (`section`, `text`), `secrets[]` (`id`, `label`, `stored`), `warnings[]`, `errors[]`. `sections` kosong = semua; `dryRun` default `true` |

### CLI

Backend menu **CLI**. `{id}` = `claude-code`, `oh-my-pi-provider`, `codex`, `gemini-cli`, `opencode`, atau `qwen-code`;
id lain → `404`. `scope` = `Global` atau `Project` (hanya `claude-code` dan `gemini-cli` yang punya `Project`; untuk
yang lain `projectFile` di daftar CLI bernilai `null`, `/projects` mengembalikan daftar kosong, dan scope `Project` → `400`).

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/cli` | daftar CLI (`id`, `name`, `description`, `globalPath`, `projectFile`, `note`) dan `proxyBaseUrl` default |
| `GET` | `/api/cli/{id}/status?scope=&projectId=&proxyBaseUrl=` | status file: `path`, `fileExists`, `enabled`, `currentBaseUrl`, `preview` |
| `GET` | `/api/cli/{id}/projects?proxyBaseUrl=` | status `enabled`/`currentBaseUrl`/`path`/`error` tiap project terdaftar |
| `PUT` | `/api/cli/{id}/toggle` | `{ "scope": "Project", "projectId": "...", "enabled": true, "proxyBaseUrl"?: "..." }` — `enabled: false` menghapus yang ditulis AIT |

### Node

| Method | Path | Body |
|---|---|---|
| `GET` | `/api/nodes` | — |
| `GET` | `/api/nodes/pipeline` | — (`isReady`, jumlah per peran, `issues`) |
| `GET` | `/api/nodes/{id}` | — |
| `POST` | `/api/nodes` | `name`, `role` (`Planner`, `ToolsNode`, `Executor`, `Reviewer`, `Tester`, `Wrapper`, `ContextKeeper`), `model`, `temperature`, `maxTokens`, `maxConcurrency`, `priority`, `systemPrompt`, `isActive`, `skillIds` (opsional, urutan = urutan di prompt, maks 10; tidak dikirim/`null` = skill node tidak diubah), `fallbackNodeId` (opsional; node ber-role sama, `null` = tanpa cadangan; 400 bila role beda, diri sendiri, atau rantai berputar), `toolDelegations` (opsional, khusus Planner: `[{ tool, toolsNodeId }]`, `tool` salah satu `list_directory`, `find_files`, `search_text`, `find_symbol`, `find_references`, `read_file`, `search_memory`, `read_memory`, `read_thread`, `run_build`; `toolsNodeId` node ber-role `ToolsNode`; tidak dikirim/`null` = delegasi tidak diubah; 400 bila tool tidak dikenal/ganda atau target bukan Tools Node), `autoTesting` (opsional, khusus Planner: `true`/`false`; tidak dikirim/`null` = tidak diubah; role lain selalu `false`). Respons node memuat `toolDelegations: [{ tool, toolsNodeId, toolsNodeName, toolsNodeActive }]` dan `autoTesting` |
| `PUT` | `/api/nodes/{id}` | sama dengan POST |
| `DELETE` | `/api/nodes/{id}` | — |
| `POST` | `/api/nodes/{id}/test` | — (`success`, `reply`, `latencyMs`, `tokens`, `error`) |

### Project

| Method | Path | Body |
|---|---|---|
| `GET` | `/api/projects` | — (tiap project: `name`, `rootPath`, `stacks[]`, `exists`, jumlah thread/sesi/memory, `key`, `keySource` = `File`/`Manual`/`GitRemote`/`Local`, `manualKey`) |
| `GET` | `/api/projects/{id}` | — (`project`, `modules[]` = file solution/build di dalam project, `sessions[]`) |
| `POST` | `/api/projects` | `{ "path": "D:\\Source\\MyApp" }` (folder; path di dalam project mendaftarkan project itu, path file memakai foldernya). `400` untuk drive, Home, atau folder sistem |
| `POST` | `/api/projects/batch` | `{ "paths": ["D:\\Source\\A", "D:\\Source\\B"] }` → `registered`, `alreadyRegistered`, `errors` |
| `GET` | `/api/projects/browse?path=` | tanpa `path` = daftar drive. Hasil: `path`, `parent`, `folders[]` (`name`, `path`, `isProject`, `stacks[]`), `projects[]` (root project di folder itu: `name`, `path`, `stacks[]`, `isRegistered`, `projectId`), `quickLocations[]` |
| `GET` | `/api/projects/scan?path=&depth=4` | `depth` 1–8. Hasil: `root`, `projects[]` (bentuk sama dengan `browse`), `foldersScanned`, `truncated`, `elapsedMs` |
| `PUT` | `/api/projects/{id}/key` | `{ "key": "eep" }` — key manual (kosong/`null` = hapus); `.aithread/project.json` tetap menang. Memory di bawah key lokal lama ikut pindah (juga daftar modul & peta kodenya) |
| `POST` | `/api/projects/{id}/git/refresh` | — → jumlah thread yang statusnya berubah. Menanyakan git lagi status perubahan semua thread project yang diikuti (30 hari terakhir); tiap perubahan dikirim lewat SSE `thread.git` |
| `GET` | `/api/projects/{id}/git` | — → `isRepository`, `hasRemote`, `fetchMinutes` (diatur; `null` = bawaan), `effectiveFetchMinutes`, `lastFetchAt`, `lastFetchError`, `followedThreads` |
| `PUT` | `/api/projects/{id}/git` | `{ "fetchMinutes": 15 \| 0 \| null }` — interval `git fetch` otomatis (0–1440 menit, 0 = mati, `null` = bawaan 15) → bentuk sama dengan `GET` |
| `POST` | `/api/projects/{id}/git/fetch` | — → `git fetch --all --prune` sekarang, lalu thread yang diikuti dicek ulang; bentuk sama dengan `GET` (gagal = `lastFetchError`) |
| `GET` | `/api/projects/{id}/environments` | — → `id`, `name`, `ref`, `order`, `isProduction`, `refFound` + `refCommit` (ref di clone ini), `changes` (perubahan yang sudah masuk), `lastSeenAt` |
| `PUT` | `/api/projects/{id}/environments` | `{ "environments": [{ "name": "dev", "ref": "origin/develop", "isProduction": false }, …] }` — seluruh daftar (urutan = urutan tampil, maks 20; nama huruf/angka/`_`/`.`/`-`, bukan `work`). Dipakai bersama per project key; ganti ref/hapus = catatan environment itu dilupakan → bentuk sama dengan `GET` |
| `POST` | `/api/projects/{id}/environments/refresh` | — → cari perubahan yang sudah masuk tiap environment sekarang; bentuk sama dengan `GET` |
| `GET` | `/api/projects/{id}/git/ingest` | — → `enabled`, `ref`, `effectiveRef` (ref yang dibaca di clone ini; `null` = tidak ada), `since`, `maxPerRun`, `done`, `skipped`, `failed`, `lastIngestedAt`, `recent[]` (`id`, `commit`, `status` = `Pending`/`Done`/`Skipped`/`Failed`, `title`, `author`, `committedAt`, `claimedBy`, `memoryId`, `note`, `createdAt`). `404` bila project tidak ada atau belum punya key |
| `PUT` | `/api/projects/{id}/git/ingest` | `{ "enabled": true, "ref": "origin/develop" \| null, "since": null, "maxPerRun": 10 }` — dipakai bersama per project key; `since` `null` = tetap (menyalakan ulang = mulai sekarang) → bentuk sama dengan `GET` |
| `POST` | `/api/projects/{id}/git/ingest/run` | — → satu putaran sekarang: `ingested`, `skipped`, `failed`, `problem` (alasan bila tidak berjalan) |
| `GET` | `/api/projects/{id}/codemap?search=&module=` | peta kode: `path`, `module`, `layer` (`UI`/`Entry`/`Logic`/`Data`/`Infra`/`Test`), `area`, `source` (`Heuristic`/`AI`/`Manual`), `updatedAt` (maks 500 baris) |
| `PUT` | `/api/projects/{id}/codemap` | `{ "path": "src/Bl/UserBL.cs" \| "UserBL", "module": "Account", "layer": "Logic" \| null }` — koreksi manual, tidak pernah ditimpa; nama class/file dicari ke file project (400 bila tidak ada atau lebih dari satu) |
| `POST` | `/api/projects/{id}/codemap/scan` | memetakan file sumber yang belum terpetakan (maks 3.000) → `files`, `mapped`, `modules` |
| `GET` | `/api/projects/{id}/structure?rescan=false` | Struktur kode: `folders`, `files`, `parsedFiles`, `thirdPartyFiles`, `types`, `members`, `languages[]` (`language`, `files`, `types`), `scanMs`, `reparsed`, `scannedAt`, `truncated`, `functionsWithCalls`. Memindai bila hasil terakhir > 1 menit atau `rescan=true` |
| `GET` | `/api/projects/{id}/structure/folder?path=` | isi satu folder (`path` kosong = root): `folders[]` (`name`, `path`, `files`, `types`, `thirdParty`), `files[]` (`name`, `path`, `language`, `types`, `members`, `thirdParty`, `parsed`), `hiddenFiles` (lebih dari 1.000 file) |
| `GET` | `/api/projects/{id}/structure/file?path=` | `types[]` (`name`, `kind`, `namespace`, `container`, `line`, `isPartial`, `members[]`) dan `functions[]`; member: `name`, `kind` (`method`/`constructor`/`property`/`event`/`function`), `line`, `endLine`, `signature`, `key` (method/function berbadan; untuk `/structure/calls`), `calls` (jumlah panggilan yang sudah dibaca Tools Node; `null` = belum pernah dibaca) |
| `GET` | `/api/projects/{id}/structure/calls?key=` | panggilan satu function: `path`, `symbol`, `startLine`, `endLine`, `status` (`Done`/`Failed`), `error`, `model`, `scannedAt`, `changed` (isi function sudah berbeda sejak dibaca), `calls[]` (`kind` = `Function`/`Method`/`Http`/`View`/`Sql`/`Other`, `target`, `line`, `resolvedPath`, `resolvedSymbol`, `resolvedLine`). `404` bila belum pernah dibaca |
| `GET` | `/api/projects/{id}/structure/groups` | tampilan Class: `key`, `name`, `isFolder` (file tanpa namespace dikelompokkan per folder; `key` diawali `/`), `types` |
| `GET` | `/api/projects/{id}/structure/types?group=` | `name`, `container`, `kind`, `files`, `members`, `isPartial` |
| `GET` | `/api/projects/{id}/structure/class?group=&name=&container=` | class yang digabung dari semua deklarasinya: `files[]` (`path`, `line`), `members[]` (+ `path` bila class tersebar di beberapa file) |
| `GET` | `/api/projects/{id}/structure/search?q=` | min. 2 karakter; maks 100: `kind` (`type`/`member`/`file`), `name`, `detail`, `path`, `line` |
| `DELETE` | `/api/projects/{id}?includeSharedMemory=false` | thread dipertahankan; memory di mesin ini dihapus, memory di store bersama hanya bila `includeSharedMemory=true`; bila clone lain dengan key sama masih terdaftar, memory pindah ke clone itu. Katalog unit test mengikuti memory |

### Unit test

Katalog [unit test](07-cara-kerja.md#unit-test) project, dipakai bersama lewat project key (`400` bila project belum
punya key). Respons **tidak pernah** memuat snapshot isi file test; hanya `hasSnapshot`.

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/projects/{id}/unit-tests` | — → `projectKey`, `settings` (`mode` = `Relevant`/`All`/`Off`, `authoring` = `OnRequest`/`AutoLogic`/`Off`, `keepSnapshots`, `timeoutSeconds`, `updatedBy`, `updatedAt`), `suites[]` (`framework`, `stack`, `manifest`, `perTestResults`, `problem`), `testerAvailable`, `tests[]`: `id`, `number`, `displayNumber` (`UT-0012`), `title`, `purpose`, `scenario`, `module`, `layers[]`, `keywords[]`, `targetFiles[]`, `framework`, `testProject`, `testFile`, `className`, `methodName`, `selector`, `requires[]`, `state` (`Active`/`Quarantined`/`Obsolete`/`Removed`), `origin` (`Ait`/`Scanned`/`Regenerated`), `createdBy`, `createdThreadId`, `createdAt`, `obsoleteReason`, `obsoleteThreadId`, `obsoleteBy`, `obsoleteAt`, `quarantineReason`, `lastResult` (`Unknown`/`Passed`/`Failed`/`Error`/`Skipped`/`NotFound`), `lastRunAt`, `lastRunBy`, `lastDurationMs`, `lastMessage`, `lastSkipReason`, `hasSnapshot` |
| `PUT` | `/api/projects/{id}/unit-tests/settings` | `{ "mode": "Relevant", "authoring": "OnRequest", "keepSnapshots": true, "timeoutSeconds": null }` — `timeoutSeconds` 10–3600 atau `null` (ikut *Timeout build*). `keepSnapshots: false` menghapus snapshot yang ada |
| `POST` | `/api/projects/{id}/unit-tests/scan` | — → `found`, `added`, `updated`, `moved`, `missing` (test aktif yang tidak ditemukan lagi di kode), `labeled`, `warnings[]`. Melabeli test dengan model murah |
| `POST` | `/api/projects/{id}/unit-tests/run` | `{ "ids": ["..."], "all": false }` (`all` = semua test aktif; `ids` boleh berisi test karantina/obsolete) → `ran`, `passed`, `failed`, `skipped`, `durationMs`, `tests[]` (`id`, `displayNumber`, `title`, `result`, `durationMs`, `message`, `skipReason`), `warnings[]`. Hasil dan snapshot test yang lolos dicatat ke katalog |
| `GET` | `/api/projects/{id}/unit-tests/{testId}/history` | riwayat run (maks 20): `phase` (`baseline`/`after`/`manual`/`flaky`), `result`, `durationMs`, `message`, `skipReason`, `machine`, `runBy`, `threadId`, `runAt` |
| `POST` | `/api/projects/{id}/unit-tests/{testId}/reactivate` | — → test. Test obsolete atau karantina kembali aktif; `400` untuk status lain |
| `GET` | `/api/projects/{id}/test-accounts` | — → `projectKey`, `canSave`, `problem` (alasan bila tanpa project key), `accounts[]` (`id`, `username`, `role`, `attributes` objek string, `notes`, `createdBy`, `updatedBy`, `createdAt`, `updatedAt`), `settings` (`devPassword` yang dipakai, `devPasswordIsDefault`, `devPasswordSource` = `default`/`developer`/`chat`, `impersonation`, `sideEffectsAcknowledgedBy`, `sideEffectsAcknowledgedAt`, `updatedBy`, `updatedAt`), `attributeKeys[]` |
| `POST` | `/api/projects/{id}/test-accounts` | `{ "username": "atasan01", "role": "Atasan", "attributes": { "Unit": "Sales" }, "notes": null }` → akun. Username unik per project (tidak peka huruf besar/kecil), tanpa spasi; maks 30 atribut, 200 akun |
| `PUT` | `/api/projects/{id}/test-accounts/{accountId}` | sama dengan `POST` → akun |
| `DELETE` | `/api/projects/{id}/test-accounts/{accountId}` | — → `true` |
| `PUT` | `/api/projects/{id}/testing-settings` | `{ "devPassword": "123" }` — kosong/`null` = kembali ke bawaan `Test@1234` → `settings` |
| `PUT` | `/api/projects/{id}/testing-settings/impersonation` | `{ "status": "available", "how": "GET /Account/LoginAs?username={{target}}", "back": null, "roles": ["Administrator"], "confirmed": true }` — `status` `available`/`none`/`unknown`; `available` wajib `how` dan `roles` → `settings` |
| `POST` | `/api/projects/{id}/testing-settings/impersonation/detect` | — → `settings`. Scan kode + model murah; hasilnya belum dikonfirmasi (`impersonation.source` = `scan`) |
| `GET` | `/api/projects/{id}/test-scenarios` | — → skenario[]: `id`, `slug`, `title`, `description`, `version`, `level`, `baseUrl`, `steps[]` (`order`, `role`, `action`, `target`, `instruction`, `values` objek field → nilai, `expect`), `roles[]` (`role`, `username`), `fields[]`, `sourceThreadId`, `sourceFollowUp`, `missing[]`, `lastOutcome`, `lastRunAt`, `lastRunBy`, `createdBy`, `updatedBy`, `createdAt`, `updatedAt` |
| `POST` | `/api/threads/{id}/follow-ups/{number}/scenario` | — → skenario. Membuat skenario dari saran test lanjutan (sama lagi bila sudah pernah dibuat) |
| `GET` | `/api/projects/{id}/test-scenarios/{scenarioId}/excel` | — → file `.xlsx` (`<slug>.xlsx`) untuk diisi; nama sheet dalam bahasa tampilan |
| `POST` | `/api/projects/{id}/test-scenarios/import` | multipart `file` (.xlsx, maks 5 MB; sheet dalam bahasa mana pun) → `scenario`, `accountsAdded[]`, `warnings[]`. 400 dengan daftar baris yang salah |
| `POST` | `/api/projects/{id}/test-scenarios/{scenarioId}/run` | `{ "acknowledge": true }` → id thread asal (kemajuan lewat SSE di thread itu). 400 bila peran belum punya akun, tanpa thread asal, atau efek samping belum dikonfirmasi (`acknowledge` false) |
| `DELETE` | `/api/projects/{id}/test-scenarios/{scenarioId}` | — → `true` |
| `POST` | `/api/projects/{id}/unit-tests/{testId}/confirm-removal` | `{ "removeCode": true }` → `test`, `threadId`. Hanya test obsolete: status `Removed`, snapshot dihapus, dan bila `removeCode` serta file test-nya ada, thread Dasbor dimulai untuk menghapus kode test itu (`threadId`) |

### Memory

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/memory` | `level`, `type`, `projectId`, `search`, `module`, `layer`, `kind`, `labeled` (`true`/`false`), `needsReview`, `environment` (dengan `projectId`: hanya memory yang perubahannya sudah masuk environment itu), `page`, `pageSize` (maks 200). Tiap memory memuat `gitAuthor`, `gitBranch`, `gitCommit` (asal-usul) dan `gitState` (status perubahannya di clone author, dari `ThreadChanges`), `fromCommit` (dirangkum dari commit di luar AIT) |
| `GET` | `/api/memory/stats` | — (+ `unlabeledCount`, `reviewCount`) |
| `GET` | `/api/memory/{level}/{id}` | — |
| `POST` | `/api/memory` | `{ "type": "Project", "projectId": "...", "module": "User", "extraModules": ["Invoice"], "kind": "Rule", "keywords": ["Description", "Deskripsi"], "layers": ["Logic"], "area": null, "description": "..." }` → short memory. `module`, `kind`, minimal satu keyword (maks 16), dan `description` wajib; `extraModules` opsional (maks 3); knowledge hanya `Pitfall`/`Pattern`/`Architecture`. Modul dicocokkan ke daftar modul (nama/alias/jamak) atau ditambahkan; semua dinormalisasi PascalCase (singkatan kapital pada nama modul dipertahankan: `MKPP`, `KlaimDF`). Respons memory memuat `extraModules[]`; filter `module` di `GET /api/memory` ikut mencocokkan modul lain |
| `PUT` | `/api/memory/{level}/{id}` | sama dengan POST; memory yang belum berlabel jadi berlabel (label lama disimpan di `legacyLabel`) |
| `DELETE` | `/api/memory/{level}/{id}` | — |
| `POST` | `/api/memory/{id}/promote` | — (short → long) |
| `POST` | `/api/memory/consolidate` | — (jumlah yang dipindah) |
| `GET` | `/api/memory/export` | `level`, `type`, `projectId`, `search`, `includeUnitTests` (default `true`) → file JSON `aithread-memory`; katalog unit test di `unitTests[]` (dengan snapshot), tidak ikut bila `search` diisi atau `type=Knowledge` (lihat [Memory](06-memory.md#export--import)) |
| `POST` | `/api/memory/import` | body = file export (v1–v4); query `mode` (`Skip` default / `Overwrite`; tidak berlaku untuk unit test), `dryRun` (`true` = hanya ringkasan). Hasil juga memuat `unitTestsAdded`, `unitTestsUpdated`, `unitTestsRenumbered`, `unitTestsSkipped` |
| `GET` | `/api/memory/modules?projectId=&knowledge=false` | daftar modul: `name`, `aliases[]`, `description`, `memories`, `files` (`knowledge=true` = modul knowledge memory) |
| `PUT` | `/api/memory/modules/{name}?projectId=&knowledge=` | `{ "name": "KlaimDF", "description": "...", "aliases": ["Pengguna"] }`; semua field opsional. `name` mengganti nama modul (boleh hanya huruf besar/kecil): memory, peta kode, dan wiki ikut; nama yang dipakai modul lain ditolak |
| `POST` | `/api/memory/modules/merge?knowledge=` | `{ "projectId": "...", "from": "Pengguna", "into": "User" }` → riwayat merge |
| `POST` | `/api/memory/modules/check?projectId=&knowledge=` | cek modul ganda sekarang → jumlah merge + saran |
| `GET` | `/api/memory/merges?projectId=&knowledge=` | riwayat: `from`, `into`, `status` (`Applied`/`Undone`/`Suggested`/`Rejected`), `reason`, `confidence`, `decidedBy`, jumlah `memories`/`files`/`pages` |
| `POST` | `/api/memory/merges/{id}/undo` \| `accept` \| `reject` | batalkan merge yang diterapkan; terima atau tolak saran |
| `GET` | `/api/memory/review?projectId=` | `memories[]` (perlu review), `merges[]` (saran), `duplicates[]` (`first`, `second`, `similarity`) |
| `GET` | `/api/memory/relabel` | status migrasi label: `running`, `phase`, `pending`, `labeled`, `needsReview`, `lastRunAt`, `lastMessage`, `backupPath`, `detail` (yang sedang dikerjakan fase itu, mis. `KN2018_KCO (1/5) · 12/40 file disebut memory`) |
| `POST` | `/api/memory/relabel` | jalankan migrasi label sekarang (di belakang) |
| `GET` | `/api/memory/changelog` | status migrasi memory lama ke ringkasan modul: `running`, `pendingFacts`, `pendingModules`, `shortFacts` (masih di short memory), `summaries`, `done`/`total`/`module` (putaran yang berjalan), `lastRunAt`, `lastMessage`, `backupPath`, `aiAvailable`, `totalFacts`/`migratedFacts` (progres keseluruhan, bertahan setelah restart) |
| `GET` | `/api/settings/memory-storage` | — (`shortMemory`, `longMemory`, `businessMemory` beserta status masing-masing; password tidak pernah dikembalikan) |
| `PUT` | `/api/settings/memory-storage` | `{ "shortMemory": {…}, "longMemory": {…}, "businessMemory": {…}, "copyExistingData": true }` — `businessMemory` boleh dihilangkan (store tidak berubah). Hasil: `settings`, `copiedShort`, `copiedLong`, `copiedBusiness`, `warnings[]` |
| `POST` | `/api/settings/memory-storage/test` | `{ "level": "Short"\|"Long"\|"Business", "store": {…} }` |

Item memory membawa `projectKey`; `projectId`/`projectName` menunjuk project di mesin ini (clone lain dari project yang
sama bila memory ditulis developer lain). Label berstruktur: `label` (tampilan `Modul · Layer · Jenis`, atau label lama
bila belum berlabel), `module`, `layers[]`, `kind`, `area`, `keywords[]`, `labeled`, `legacyLabel`, `reviewReason`.

### Business memory (wiki bisnis)

Lihat [Business memory](06-memory.md#business-memory-wiki-bisnis). `projectId` selalu id project di mesin ini; halaman
disimpan per project key.

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/business/projects/{projectId}` | — (`projectKey`, `keyIsShared`, `pages[]` tanpa isi, `pendingThreads`, `lastDigestAt`, `digestRunning`, `storeDescription`) |
| `GET` | `/api/business/pages/{id}` | — (`page`, `content` Markdown, `html` siap tampil) |
| `POST` | `/api/business/pages` | `{ "projectId": "...", "module": "TO", "title": "Transfer Order", "summary": "...", "labels": "to, transfer order", "content": "# …", "autoUpdate": true, "changeNote": "…" }` |
| `PUT` | `/api/business/pages/{id}` | body sama + `version` (versi yang sedang diedit). Halaman sudah diubah orang lain → `409` |
| `DELETE` | `/api/business/pages/{id}` | — ; beserta revisi dan gambar yang tidak dipakai halaman lain |
| `GET` | `/api/business/pages/{id}/revisions` | — (`version`, `title`, `changeNote`, `author`, `editKind` = `Ai`/`Manual`/`Restore`, `sourceThreadIds[]`) |
| `GET` | `/api/business/pages/{id}/revisions/{version}` | — (+ `content`) |
| `POST` | `/api/business/pages/{id}/revisions/{version}/restore` | — ; menjadi versi baru |
| `POST` | `/api/business/preview` | `{ "content": "# …" }` → HTML (sama dengan halaman tersimpan) |
| `POST` | `/api/business/assets` | `{ "projectId": "...", "pageId": null, "data": "data:image/png;base64,…", "caption": "Form TO" }` — PNG/JPEG/GIF/WebP maks 5 MB (dicek dari isi file); gambar sama disimpan sekali per project. Hasil: `id`, `url` |
| `GET` | `/api/business/assets/{id}` | file gambar (cache permanen) |
| `POST` | `/api/business/digest?projectId=` | rangkum thread yang menunggu sekarang → `threads`, `pagesCreated`, `pagesUpdated`, `images`, `warnings[]`. `400` bila rangkuman lain sedang berjalan |
| `GET` | `/api/business/settings` | — (`enabled`, `intervalHours`, `model`, `developerName`, `effectiveDeveloperName`) |
| `PUT` | `/api/business/settings` | `{ "enabled": true, "intervalHours": 6, "model": null, "developerName": "Andi" }` |
| `GET` | `/api/business/projects/{projectId}/environments/{environment}` | — → `environment`, `pages[]` (`pageId`, `module`, `title`, `slug`, `summary`, `version` = revisi yang berlaku, `workingVersion` = versi wiki kerja sekarang, `updatedAt`), `missing` (halaman kerja yang belum ada di environment) |
| `GET` | `/api/business/pages/{id}/environments/{environment}` | — → halaman seperti di atas + `content`, `html`. `404` bila halaman belum ada di environment itu |
| `GET` | `/api/business/projects/{projectId}/compare?left=&right=` | `left`/`right` = nama environment atau `work` (wiki kerja) → `pages[]` (`pageId`, `module`, `title`, `status` = `same`/`changed`/`left`/`right`, `leftVersion`, `rightVersion`, `diff[]` (`kind` = `same`/`add`/`del`/`skip`, `text`; `skip` = jumlah baris sama yang dilipat)), `changes[]` (`threadId`, `title`, `author`, `branch`, `side` = `left`/`right`, `commit`) |

### Thread & dashboard

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/threads` | `take` (default 30, maks 200), `projectId` |
| `GET` | `/api/threads/{id}` | — (ringkasan, `planJson`, respons akhir, metrik termasuk `reviewerModel`/`reviewerTokens`/`verificationMs`, tugas, `verificationJson`: `outcome` `passed`/`failed`/`unverified`, `command`, `reviewerModel`, `rounds[]` berisi `build` (`status`, `headline`, `errors[]`, …), `review` (`approved`, `summary`, `issues[]`, `error`), `unitTests` (ringkasan putaran unit test, bentuk sama dengan `unitTestJson.rounds[]`), `fixTasks[]`, `unfixable[]`; `unitTestMs` dan `unitTestJson`: `outcome` `passed`/`failed`/`skipped`, `mode`, `testOnly`, `suites[]`, `problems[]`, `restored[]` (`number`, `file`, `how` = `moved`/`snapshot`/`method`/`task`/`regenerate`/`missing`, `note`), `notes[]`, `baselineRan`, `baselineCached`, `baselineMs`, `rounds[]` (`round`, `ran`, `passed`, `failed`, `skipped`, `durationMs`, `commands[]`, `regressions[]`, `preexisting[]`, `triage`, `testerModel`, `fixTargets[]`), `tests[]` (`id`, `number`, `title`, `file`, `decision`, `baseline`, `baselineCached`, `after`, `verdict` = `ok`/`fixed`/`regression`/`preexisting`/`new-failing`/`failing`/`flaky`/`skipped`/`notfound`/`missing`/`obsolete`, `message`, `skipReason`, `triage`, `triageReason`, `durationMs`), `testerModel`, `summary`; `testerModel`/`testerTokens` (termasuk triase unit test)/`testingMs`, dan `testingJson`: `outcome` `passed`/`failed`/`blocked`/`skipped`, `level`, `baseUrl`, `browser`, `testerModel`, `summary`, `error`, `steps`, `scenarios[]` (`title`, `status`, `details`), `requests[]` (`method`, `url`, `requestBody`, `status`, `reason`, `contentType`, `location`, `responseBody`, `durationMs`, `error`; password login tersamar sebagai `{{password}}`), `browserErrors[]`, `loginUser`, `loginNeeded`/`loginUrl` (tester berhenti meminta login), `askedLogin` (pertanyaan dikirim ke chat)) |
| `POST` | `/api/threads` | `{ "projectId": "...", "prompt": "..." }` → id thread, berjalan di belakang |
| `POST` | `/api/threads/{id}/cancel` | — |
| `POST` | `/api/threads/{id}/retry` | — → id thread. Menjalankan ulang tugas rencana yang belum berhasil di belakang (tanpa planner). 400 bila thread sedang berjalan, sudah di-rollback, atau tidak ada yang perlu diulang. |
| `POST` | `/api/threads/{id}/test` | `{ "username"?: "...", "password"?: "..." }` (opsional; login untuk testing ini, tidak disimpan) → id thread. Menjalankan ulang Auto Testing dari rencana testing tersimpan di belakang (kode tidak diubah). 400 bila hanya satu dari username/password diisi, thread sedang berjalan, belum *Selesai*, sudah di-rollback, tidak punya rencana testing UI/API, atau tidak ada node Tester aktif. |
| `POST` | `/api/threads/{id}/follow-ups/{number}/run` | — → id thread. Menjalankan satu test lanjutan yang disarankan Tester (bagian `followUps` di `testingJson`) di belakang: skenario saran itu, akun uji project, nilai yang diingat (`values`). Hasilnya di `followUps[].lastRun`; saran baru dari run itu ditambahkan. 400 dengan alasan yang sama seperti `/test`, atau bila nomor saran tidak ada. |
| `GET` | `/api/threads/{id}/rollback` | — → pratinjau `{ canRollback, reason, files[]: { filePath, action, problem } }`; `action` = `restore` / `delete` / `unchanged` / `conflict`. Tidak mengubah apa pun. |
| `POST` | `/api/threads/{id}/rollback` | — → `{ restored, deleted, files[] }`. Semua atau tidak sama sekali: 400 tanpa menyentuh file bila ada `conflict`. |
| `POST` | `/api/threads/{id}/git/refresh` | — → bagian `git` detail thread (lihat di bawah) setelah git ditanya lagi |
| `GET` | `/api/dashboard/summary` | — |
| `GET` | `/api/dashboard/logs` | `take` (default 200), `minLevel` (`Debug`/`Info`/`Success`/`Warning`/`Error`), `threadId` |
| `GET` | `/api/health` | — |

Detail thread (`GET /api/threads/{id}`) juga memuat `git` (`null` di luar repository git): `branch`, `baseCommit`,
`author` (asal-usul saat thread mulai), `state`, `commits[]`, `checkedAt`, `files[]` (`filePath`, `state`, `commit`, `edited`). Ringkasan
thread (`GET /api/threads`) memuat `gitState`. Lihat [Status git perubahan thread](07-cara-kerja.md#status-git-perubahan-thread).

### Sistem

Dipakai `scripts/aithread.ps1` untuk instance background.

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/system` | — (`processId`, `startedAt`, `environment`, `contentRoot`) |
| `POST` | `/api/system/shutdown` | — ; wajib header `X-AIThread-Shutdown` dan hanya dari localhost, selain itu `403`. Mematikan AIT dengan rapi: thread berjalan dibatalkan dan dicatat dulu. |

### Telegram

Kartu Telegram di halaman Pengaturan — lihat [Prompt dari Telegram](11-telegram.md).

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/telegram` | — (`enabled`, `hasBotToken`, `botTokenMasked`, `state` = `Disabled`/`Starting`/`Running`/`Error`, `botUsername`, `error`, `lastPollAt`, `chats[]`) |
| `PUT` | `/api/telegram` | `{ "enabled": true, "botToken": "123456789:AAE…", "clearBotToken": false }` — `botToken` kosong = pertahankan token tersimpan. Token baru diverifikasi ke Telegram (`getMe`); ditolak → `400`. |
| `POST` | `/api/telegram/chats/{id}/allow` | — ; mengizinkan chat, bot mengirim konfirmasi ke chat itu |
| `DELETE` | `/api/telegram/chats/{id}` | — ; menghapus chat. Chat yang sudah diizinkan dicabut aksesnya dan diberi tahu. |

`chats[]`: `id`, `chatId`, `displayName`, `username`, `isAllowed`, `activeProjectId`, `activeProjectName`, `createdAt`,
`lastMessageAt`; chat yang menunggu izin tampil lebih dulu. Token tidak pernah dikembalikan (hanya `botTokenMasked`).

### Skill

Tab Skill di halaman Pengaturan dan saran skill di modal Node — lihat [Skill pada node](07-cara-kerja.md#skill-pada-node).

| Method | Path | Body / query |
|---|---|---|
| `GET` | `/api/skills/settings` | — (`model`, `maxPromptChars`, `storeDirectory`) |
| `PUT` | `/api/skills/settings` | `{ "model": "cc/claude-opus-4", "maxPromptChars": 40000 }` — `model` `null` = model node Planner aktif |
| `GET` | `/api/skills/sources` | — (`id`, `url`, `name`, `kind` = `GitHub`/`GitLab`/`Git`/`RawFile`/`Web`, `ref`, `subPath`, `status` = `Queued`/`Fetching`/`Analyzing`/`Ready`/`Failed`, `error`, `skillCount`, `lastPulledAt`) |
| `POST` | `/api/skills/sources` | `{ "url": "https://github.com/anthropics/skills" }` — link tidak valid atau sudah ada → `400`; penarikan berjalan di latar belakang |
| `POST` | `/api/skills/sources/{id}/sync` | — ; menarik ulang (`400` bila masih diproses) |
| `DELETE` | `/api/skills/sources/{id}` | — ; menghapus sumber, skill-nya (dan lampirannya di node), serta foldernya |
| `GET` | `/api/skills?query=` | daftar skill (`id`, `sourceId`, `sourceName`, `name`, `description`, `tags`, `relativeDir`, `nodeCount`, `snippet`); `query` mencari di nama, deskripsi, tag, sumber, dan isi skill — hasil yang cocok di nama/deskripsi diurutkan duluan, yang hanya cocok di isi membawa `snippet` (potongan teks di sekitar kata kunci) |
| `GET` | `/api/skills/{id}` | detail + `content`, `folder`, `entryFile`, `files[]`, `nodeNames[]` |
| `POST` | `/api/skills/suggest` | `{ "prompt": "aku butuh kamu bisa menjadi engineer yang handal", "role": "Executor" }` → maks 6 saran (`id`, `name`, `description`, `sourceName`, `reason`, `score` 0–1, `recommended`). Gudang kosong → `400`; 9router gagal → `502`. |

`GET /api/nodes` dan `GET /api/nodes/{id}` menyertakan `skills[]` (`id`, `name`, `description`, `sourceName`) sesuai urutan di prompt, serta `fallbackNodeId` dan `fallbackNodeName`.

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
| `thread.git` | `threadId`, `projectId`, `state` (`Uncommitted`/`Staged`/`Committed`/`Pushed`/`ModifiedOutside`/`Stashed`/`OnOtherBranch`/`Discarded`/`Lost`), `commits[]` — status git perubahan thread berubah |
| `thread.output` | aktivitas planner: `{ "kind": "planner.tool", "tool": "read_file", "detail": "…" }` |
| `task.updated` | objek tugas lengkap (status, node, durasi, diff, error, …) |
| `task.output` | potongan output model executor: `taskId`, `taskKey`, `text` |
| `memory.changed` | `id`, `action` (`created`, `updated`, `deleted`, `promoted`, `consolidated`, `imported`, `relabel`, `summary`, `changelog`, `merged`, `unmerged`, `suggested`, `rejected`) |
| `project.changed` | `id`, `action` |
| `node.changed` | `id`, `action` |
| `telegram.changed` | `action` (`status`, `created`, `allowed`, `deleted`, `project`); `id` chat untuk perubahan chat, `status` untuk perubahan status bot |
| `skill.changed` | `sourceId`, `status` (status sumber), `action` (`added`, `deleted`) bila ada, `error` bila gagal |
| `business.changed` | `action` (`created`, `updated`, `deleted`, `running`, `finished`, `settings`), `id` halaman dan/atau `projectKey` |
| `ping` | — |

Contoh di browser:

```js
const source = new EventSource('/api/stream?types=task,thread');
source.addEventListener('task.updated', e => console.log(JSON.parse(e.data).data));
```
