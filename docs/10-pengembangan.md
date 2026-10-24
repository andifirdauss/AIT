# 10. Pengembangan

Konvensi kode dan penggunaan MCP ada di `CLAUDE.md` di root repo.

## Struktur solusi

| Project | Isi |
|---|---|
| `src/AIThread.Common` | Entity, enum, DTO, `AIThreadOptions`. |
| `src/AIThread.Data` | `AIThreadDbContext` (SQL Server via `IDbContextFactory`), migrasi EF, `RedisShortMemoryStore`, `EmbeddedRedisServer` (Garnet di dalam proses), `Memory/` (store memory yang bisa diganti dari menu Setting: `SwitchableShortMemoryStore`, `LongMemoryStore`, `SqlShortMemoryStore`, `MemoryStoreConnection` untuk SQL Server/PostgreSQL/MySQL/Oracle/SQLite/Redis), `InProcessEventBus`. |
| `src/AIThread.Service` | Service utama (`NineRouterService`, `NodeService`, `ProjectService`, `MemoryService`, `SettingService`, dll.), `Orchestration/`, `Workspace/`, `Proxy/`, `Background/`. |
| `src/AIThread.API` | Controller REST `/api/*`, controller proxy `AnthropicProxyController` / `OpenAiProxyController` / `GeminiProxyController` (`/v1/*`, `/v1beta/*`), `StreamController` (SSE), infrastruktur. |
| `src/AIThread.UI` | Host ASP.NET Core MVC. View hanya kerangka; data dimuat oleh `wwwroot/js/pages/*.js` lewat `/api` + SSE. |
| `tests/AIThread.Tests` | Unit test xUnit, plus test end-to-end orkestrasi (`ThreadOrchestratorTests`): `OrchestrationHarness` memakai service layer asli di atas SQLite sementara dan folder project sementara, hanya model yang dipalsukan oleh `ScriptedRouter` (balasan per agen, executor per file). Test unit test katalog (`UnitTestOrchestrationTests`) memalsukan runner test lewat `ITestRunner` di `configureServices`. |

Alur dependensi: `UI → API → Service → Data → Common`.

## Konvensi service

Setiap class di `AIThread.Service` yang di-register di DI (`DependencyInjection.AddServiceLayer`) memakai pola
**Interface + Service + partial class per method**:

- **Interface** `I<Nama>` di `Interfaces/` (namespace `AIThread.Service.Interfaces`) memuat semua member publik
  instance. Registrasi: `services.AddSingleton<I<Nama>, <Nama>>()`. Konsumen (parameter constructor, field,
  `GetRequiredService<>`, test lewat `harness.Service<I<Nama>>()`) selalu memakai interface, bukan class konkret.
- **Service** `partial` di folder bernama class itu, di bawah folder asalnya. Namespace tetap mengikuti folder asal,
  mis. `Orchestration/PlannerAgent/` tetap `AIThread.Service.Orchestration`.
  - `<Nama>.cs`: deklarasi utama, yaitu primary constructor, base list, field, konstanta, property, nested type, dan
    type pendukung (record/DTO) yang memang milik class itu.
  - `<Nama>.<Method>.cs`: satu file per method, publik maupun private. Overload digabung dalam satu file, termasuk
    `[GeneratedRegex]`. Header cukup `public sealed partial class <Nama>` tanpa base list.
- Method baru berarti file baru `<Nama>.<Method>.cs`. Member publik baru juga ditambahkan ke `I<Nama>`.
- Tidak memakai pola ini: static helper (mis. `FinalReport`, `CSharpOutline`), model/record, dan hosted worker di
  `Background/`.

```text
Interfaces/IThreadService.cs
Implementations/ThreadService/
  ThreadService.cs                 primary constructor, field, SummaryProjection
  ThreadService.GetRecentAsync.cs
  ThreadService.GetDetailAsync.cs
  ThreadService.Cancel.cs
  ThreadService.ToTaskDto.cs
```

## Perintah

```powershell
dotnet build AIThread.slnx
dotnet test tests/AIThread.Tests
dotnet run --project src/AIThread.UI --launch-profile http
```

Migrasi EF (tool lokal di `dotnet-tools.json`):

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add <NamaMigrasi> --context AIThreadDbContext --project src/AIThread.Data --startup-project src/AIThread.Data
dotnet tool run dotnet-ef migrations has-pending-model-changes --context AIThreadDbContext --project src/AIThread.Data --startup-project src/AIThread.Data
```

`--context` wajib karena `AIThread.Data` juga berisi `ShortMemoryDbContext` dan `LongMemoryDbContext` (store memory
eksternal). Keduanya tidak punya migrasi: tabelnya dibuat oleh `MemorySchema` saat pertama dipakai, jadi perubahan pada
`LongMemory` harus tetap kompatibel dengan tabel yang sudah ada di database eksternal.

Instance background (`scripts/aithread.ps1`, lihat [Instalasi](01-instalasi.md#5-menjalankan-di-background))
memakai hasil publish di `%LOCALAPPDATA%\AIThread\app`, jadi build/debug dari repo tetap bisa jalan — asalkan
memakai port lain (`--urls`) atau instance background dihentikan dulu. `Background/ThreadLifecycleWorker` menjaga
status thread saat proses berhenti: shutdown rapi membatalkan thread yang berjalan, dan saat start thread yang
tertinggal dari proses yang mati mendadak ditandai *Cancelled*.

`AIThread.exe` standalone: `scripts/publish-exe.ps1` memakai profil `src/AIThread.UI/Properties/PublishProfiles/standalone.pubxml`
(win-x64, self-contained, single-file; `AssemblyName` jadi `AIThread` hanya di profil itu, build biasa tetap
`AIThread.UI`) dan membangun ke `dist/.build`, jadi tidak bentrok dengan `bin/` yang sedang dikunci. Menu, tray
(Win32 murni, tanpa WinForms agar RAM kecil), autostart, dan perintah CLI ada di `src/AIThread.UI/Hosting/`;
`Program.cs` hanya memanggil `Cli.RunAsync`. Tanpa argumen dan tanpa terminal/`--urls`/env `ASPNETCORE_*`, host berjalan
seperti biasa (`dotnet run`, Visual Studio, script background tidak berubah). Menguji menu membutuhkan konsol asli:
jalankan exe dengan `menu --port <port uji>` agar tidak menyentuh instance port 5225.

Migrasi diterapkan otomatis saat aplikasi start (`InitializeDataLayerAsync`). `DesignTimeDbContextFactory` memakai
connection string LocalDB default.

## Peta kode orkestrasi

| File | Tanggung jawab |
|---|---|
| `Proxy/ProtocolAdapter.cs` | Mengubah request Anthropic / OpenAI chat / OpenAI Responses / Gemini ke bentuk kanonik (gaya Anthropic) dan menentukan path 9router per format. |
| `Proxy/ClaudeRequestInspector.cs` | Memilah request kanonik: kandidat orkestrasi vs passthrough; ambil working directory, sesi, prompt, riwayat. |
| `Implementations/SetupService/`, `SetupProfileService/`, `API/Controllers/SetupAssistantController.cs` | Menu Setup: status langkah dari kondisi saat ini (`GetStatusAsync`, defensif seperti dashboard), simpan 9router setelah diuji, preset node `AIT …` (dicocokkan lewat nama + peran). Profil tim: export tanpa secret, import = rencana per bagian (teks dry run + aksi) lalu dijalankan berurutan; node dibuat dulu, tautan (cadangan, delegasi, skill) disambung lewat nama. UI: `pages/setup.js`, penelusur project bersama `js/project-browser.js` + `_ProjectBrowser.cshtml`; CLI: `Hosting/SetupCommand.cs`. |
| `Implementations/ProjectService/` | `ResolveSessionAsync`: gerbang per sesi (project atau diabaikan, cache 30 menit, auto-register); penelusur folder, scan, registrasi. Identitas project = `RootPath`. |
| `Workspace/ProjectLocator.cs` | Tabel marker (`.git`, manifest per ekosistem), penentuan root dari working directory (repo terdekat → penghimpun paling luar → manifest terdekat), folder yang diblokir (drive/Home/sistem), scan rekursif, daftar manifest & stack, daftar folder build per stack. |
| `Orchestration/ThreadOrchestrator/` | Alur utama thread: project → pemulihan unit test → planner (+ baseline paralel) → eksekusi → verifikasi → testing → wrapper → memory, status & error; thread yang hanya menguji (`TestOnlyAsync`); *Ulangi testing* (`RetestInBackgroundAsync`). |
| `Orchestration/PlannerAgent/`, `PlannerTools.cs` | Planner (loop tool 9router), failover antar node planner, definisi tool (JSON schema), brief (urutan stabil → spesifik untuk prompt cache), jeda & lanjut pada `ask_user`, tool `investigate` untuk tool yang didelegasikan. |
| `Orchestration/PlannerToolRunner/` | Eksekusi tool baca, `run_build`, dan `run_tests` (dipakai planner dan tools node; `run_tests` hanya planner), teks progres per tool (`Describe`). |
| `Workspace/GitTools.cs` | Tool git read-only planner (`git_status`/`git_diff`/`git_log`/`git_show`/`git_blame`): proses `git` dengan argumen tetap, revisi divalidasi, path setelah `--`, tanpa lock opsional/pager/diff eksternal, output dibatasi. `IsRepository` menentukan apakah tool git ditawarkan (`PlannerTools.Offered`). |
| `Orchestration/ToolsNodeAgent/` | Tools node: loop tool sendiri untuk satu pertanyaan `investigate`, diakhiri `report_findings`; delegasi disimpan di `PlannerToolDelegation` (`NodeService` memvalidasi). |
| `Orchestration/PlannerQuestions.cs`, `PendingQuestionStore/` | Pertanyaan planner: format teks bernomor, jawaban → hasil tool, `PendingQuestionStore` (per sesi, di memori, 2 jam), penutupan thread yang tidak dijawab. |
| `Orchestration/ThreadPlan.cs`, `PlanValidator.cs` | Model rencana dan aturan validasinya. |
| `Orchestration/ExecutionEngine/` | Penjadwalan DAG paralel, pemilihan executor, skip dependensi gagal. |
| `Orchestration/ExecutorAgent/` | Menyusun input executor, retry, menerapkan edit di bawah lock. |
| `Orchestration/TesterAgent/`, `TesterTools.cs`, `Testing/` | Auto Testing: node Tester menjalankan bagian `test` rencana (`TestPlan`, divalidasi `PlanValidator`) dalam loop tool sendiri — `http_request` dan tool `browser_*` — lalu `report_test`; `TestingReport` disimpan di `ThreadRun.TestingJson`. `Testing/TestBrowserLauncher` membuka Edge/Chrome/Chromium non-headless lewat Microsoft.Playwright (profil persisten, satu browser sekaligus), `PlaywrightTestBrowser` = aksi + snapshot ber-`ref`, `TestUrls` = hanya localhost. Login: `ask_login` → pertanyaan di akhir jawaban → balasan `login <user> <password>` (`TestLogin.Parse`) ditangkap `ThreadOrchestrator.RunAsync` → `TestWithLoginAsync` (testing ulang di-stream); `TestLoginStore` (memori: pertanyaan per sesi, login per sesi+project 8 jam, `Redact` riwayat chat); tester hanya melihat `{{password}}` (`TesterAgent.Fill`/`Mask`). |
| `Verification/TestCommandResolver.cs`, `TestResultParser.cs`, `TestEnvironment.cs`, `TestRunner/`, `RootGates.cs` | Unit test tanpa model: suite per stack dari manifest + perintah tetap (`dotnet test` TRX, `vstest.console`, jest/vitest/mocha JSON, pytest/PHPUnit/Maven/Gradle JUnit XML, `go test -json`, `cargo test`, `npm test` exit code saja), parser hasil per test, pengenal kegagalan karena lingkungan (→ *dilewati* + alasan), proses + timeout (`CI=true`), dan satu build/test per root (`RootGates`, dipakai juga `BuildRunner`). |
| `UnitTests/` | Katalog unit test per `ProjectKey`: `UnitTestCatalog` (CRUD, penomoran `UT-0001`, hasil, riwayat 20 run, `RevertThreadAsync` untuk rollback), `UnitTestScanner` + `TestDiscovery` (Roslyn untuk C#, pola untuk stack lain; label lewat `MemoryAi`), `UnitTestExecutor` (kelompokkan per suite, ulang kegagalan sekali → tidak stabil, `UnitTestMatcher` mencocokkan hasil runner ke test), `UnitTestRestorer` (pindah file → catatan git → snapshot / sisip method Roslyn → tugas executor `R<n>` dari snapshot atau skenario), `UnitTestSnapshot` (snapshot internal, tidak pernah ke DTO), `UnitTestSelection` (test relevan). Tabel di `Data/Memory/UnitTestDb.cs` (`IUnitTestDb`, ikut store long memory lewat `LongMemoryStore.CreateUnitTestsAsync`). |
| `Orchestration/UnitTestStage/` | Unit test di thread: `OpenAsync` (setting, suite, scan otomatis katalog kosong, batas 10 detik), `RestoreAsync`, baseline paralel planner + cache hash (`StartBaseline`/`CompleteBaselineAsync`), `Brief` (`# Unit tests` di brief planner), `CheckAsync` per putaran verifikasi (klasifikasi, triase `TesterAgent.TriageAsync`, aturan mekanis, `ReviewIssue` → `FixTaskFactory`), `RunOnlyAsync`/`RunForPlannerAsync`, `FinishAsync` (hasil, test baru, obsolete, karantina, `UnitTestReport` di `ThreadRun.UnitTestJson`). Batas 3 putaran perbaikan di `VerificationStage`. |
| `Implementations/UnitTestService/`, `API/Controllers/UnitTestsController.cs` | API katalog untuk Project > Detail (`pages/project-unittests.js`): setting, scan, run manual, riwayat, aktifkan lagi, konfirmasi hapus (thread Dashboard menghapus kode test). Export/import katalog ada di `MemoryTransferService` (format v3). |
| `Orchestration/WrapperAgent/`, `MemoryExtractor/` | Ringkasan akhir dan ekstraksi memory. |
| `Orchestration/ContextKeeper/`, `ContextPackBuilder.cs`, `ThreadAnchor.cs` | Konteks sesi chat CLI: penanda thread di balasan, pencarian ringkasan dari history, pembaruan ringkasan setelah thread, tool `read_thread`. |
| `Orchestration/SessionHandoff/`, `TranscriptDigest.cs`, `Implementations/ClaudeHookConfig.cs`, `API/Controllers/ClaudeHooksController.cs` | Context Keeper untuk `/clear`: hook `SessionEnd`/`SessionStart`, ringkasan transcript, penulisan blok `hooks` di settings Claude Code. |
| `Orchestration/AgentPrompts.cs` | **Semua prompt peran** (planner + bagian *Auto Testing* dan *Unit tests*, executor, reviewer, tester + triase unit test, label unit test, wrapper, memory, context keeper). |
| `Orchestration/ThreadRecorder.cs`, `ClientProgress.cs` | Persistensi + event SSE; progres untuk CLI sebagai *vertical stepper* di blok ```` ````text ```` (wrap 90 kolom). |
| `Orchestration/FinalReport.cs` | Bagian jawaban akhir yang ditulis AIT dari hasil: baris status + *Perlu perhatian*, tabel *File berubah* (dari diff), bagian *Unit test*, bagian *Testing* + *Output API*, footer (durasi, token, memory, wiki, *Sisa kuota*, link thread). |
| `Implementations/RouterQuotas.cs` | Bagian murni kuota provider: model node/combo → provider 9router (peta alias `cc`→`claude`, `ag`→`antigravity`, …), parsing `/api/combos`, `/api/providers/client`, `/api/usage/{id}`, dan pemilihan batas yang ditampilkan. HTTP-nya di `NineRouterService.GetQuotasAsync` (cache 60 detik, timeout 10 detik). |
| `Implementations/RouterCliToken.cs` | Token CLI 9router (`x-9r-cli-token` = 16 hex pertama sha256(`machine-id` + `9r-cli-auth` + `auth/cli-secret`) dari folder data 9router), dikirim `GetDashboardJsonAsync` hanya ke 9router di localhost agar kuota terbaca walau *Require login* aktif. |
| `Workspace/*` | Sandbox path, tool baca repo, parser & applier SEARCH/REPLACE, I/O teks (BOM/line ending), diff, pencari project. |
| `Security/` | Keamanan secret: `SecretRules` (pola secret + placeholder `{{AIT_SECRET:<label>:<id>}}`, murni), `SecretPaths` (file & folder kredensial yang ditolak `WorkspaceScope`), `SecretPolicy` (`"secrets"` di `.aithread/project.json`: `deny`/`allow`/`keys`), `SecretGuard` (vault di memori, id HMAC dengan kunci Data Protection, `Redact`/`RedactFile`/`RedactMessages`/`RedactJson` + `Restore`/`RestoreJson`/`Learn`, tally per thread). Dipasang di `NineRouterService.ChatAsync` (semua panggilan model), `WorkspaceTools`, `PlannerToolRunner`, `ExecutorAgent` (pulihkan blok sebelum diterapkan), `TesterAgent`, `ThreadOrchestrator.Redacted`, `ActivityLogService`, `MemoryExtractor`, `BusinessPageStore`, export memory, passthrough `AgentProxyHandler`, dan hook `PreToolUse` (`ClaudeHooksController`). `ClaudePermissionRules` menulis `permissions.deny` Claude Code. Setting + popup: `SecurityService`, `SecurityController`, `Views/Shared/_SecurityNotice.cshtml`. |
| `API/Infrastructure/AgentProxyHandler.cs` | Logika proxy bersama semua format: auth, klasifikasi, orkestrasi, passthrough ke 9router. |
| `API/Infrastructure/AnthropicSseWriter.cs`, `ClientStreamWriters.cs` | Menulis balasan orkestrasi (stream, non-stream, error) dalam format Anthropic, OpenAI chat, OpenAI Responses, atau Gemini. |
| `Background/TelegramPollingWorker.cs`, `Telegram/TelegramBotRuntime/` | Long polling `getUpdates` bot Telegram, mulai ulang saat setting Telegram disimpan, backoff saat offline, status untuk kartu Setting. |
| `Telegram/TelegramApi/` | Klien Bot API minimal: JSON dengan `Content-Length`, token dibaca dari runtime setting tiap panggilan, kirim ulang sebagai teks biasa bila HTML ditolak. HttpClient-nya tanpa logger karena token ada di URL. |
| `Telegram/TelegramBot/` | Izin chat, perintah (`/projects`, `/status`, `/cancel`, `/diff`, `/new`), memulai thread (`ThreadSource.Telegram`, sesi `telegram:<chat>`, riwayat percakapan untuk planner). |
| `Telegram/TelegramThreadReporter/` | Pesan status live per thread (event bus sebagai pemicu, database sebagai sumber data) dan pesan hasil akhir. |
| `Telegram/TelegramFormatter.cs`, `TelegramMessages.cs` | Markdown wrapper → HTML Telegram (tanpa `<code>` di dalam `<b>`/`<i>`, dipecah per 4096 karakter) dan semua teks bot. |

## Dokumentasi di aplikasi

Menu **Docs** (`/Docs`, `/Docs/{nama-file-tanpa-.md}`) merender `docs/*.md` dengan Markdig lewat `DocsService`.

- Lokasi folder: `AIThread:DocsPath` bila diisi; selain itu `docs/` repo (dua tingkat di atas `src/AIThread.UI`, dipakai
  saat development sehingga perubahan langsung terlihat), lalu salinan `docs/` di folder output/publish.
- `README.md` adalah halaman indeks; urutan navigasi mengikuti nama file, jadi pertahankan awalan angka (`11-...md`).
- Judul diambil dari heading `# ` pertama. Tautan `nama-file.md#anchor` otomatis menjadi `/Docs/nama-file#anchor`;
  anchor mengikuti aturan GitHub/GitLab. Tautan ke file di luar `docs/` tidak bisa dibuka dari web — tulis sebagai teks.

## Titik ekstensi umum

**Menyetel perilaku model** — ubah `AgentPrompts.cs`. Prompt ke model ditulis dalam bahasa Inggris; teks untuk
user dalam bahasa Indonesia. Untuk penyesuaian per tim tanpa mengubah kode, gunakan *Instruksi tambahan* di node.

**Menambah tool planner**
1. Tambahkan definisi di `PlannerTools.Definition()` (nama konstanta + JSON schema) dan namanya di
   `PlannerTools.Delegable` (urutan tool baca planner; semua tool baca bisa didelegasikan ke tools node), serta di
   daftar `delegableTools` di `wwwroot/js/pages/nodes.js`.
2. Tangani di `PlannerToolRunner.RunAsync` (dan `Describe` untuk teks progres).
3. Tool harus read-only dan memakai `WorkspaceScope` untuk setiap path.

**Menambah aturan validasi rencana** — `PlanValidator.Validate`; tulis pesan error dalam bahasa Inggris yang
menjelaskan cara memperbaikinya (pesan dikirim balik ke planner). Tambahkan test di `PlanValidatorTests`.

**Mengubah format edit executor** — `EditBlockParser`, `EditApplier`, prompt `AgentPrompts.Executor`, dan test
terkait harus diubah bersamaan.

**Event SSE baru** — tambahkan konstanta di `AppEventTypes`, publikasikan lewat `IEventBus.Publish`, lalu tangani
di `AIThread.stream(...)` pada halaman terkait.

**Menambah perintah bot Telegram**
1. Tambahkan ke `TelegramMessages.Commands` (menu `/` di Telegram diperbarui saat bot mulai polling).
2. Tangani di `TelegramBot.HandleCommandAsync`; tulis teks balasan di `TelegramMessages` sebagai HTML Telegram dan
   escape input dengan `TelegramFormatter.Escape`.
3. Jangan menaruh `<code>`/`<pre>` di dalam `<b>`/`<i>`/`<s>`/`<a>` — Telegram menolak pesan seperti itu.

## Pengujian end-to-end tanpa biaya model

Pipeline bisa diuji dengan server tiruan 9router yang mengimplementasikan `GET /api/health`, `GET /v1/models`, dan
`POST /v1/chat/completions` (stream SSE OpenAI, termasuk `tool_calls`), dengan jawaban berbeda berdasarkan isi system
prompt (`You are the PLANNER`, `You are an EXECUTOR`, `You are the WRAPPER`, `maintain the memory`). Arahkan
Setting → Base URL ke server tiruan tersebut, lalu kirim request CLI (mis. format Anthropic ke `/v1/messages`) atau
`POST /api/threads` (`{ "projectId", "prompt" }`). Gunakan database terpisah (ubah `ConnectionStrings:Default`) agar data uji
tidak bercampur.

Bot Telegram diuji dengan cara yang sama: arahkan `AIThread:Telegram:ApiBaseUrl` ke server tiruan Bot API yang
menjawab `getMe`, `deleteWebhook`, `setMyCommands`, `getUpdates` (long poll dengan `offset`), `sendMessage`,
`editMessageText`, dan `answerCallbackQuery`, lalu sisipkan update pesan/tombol ke antrean `getUpdates`-nya.
Kecilkan `PollTimeoutSeconds` (mis. `2`) agar uji cepat. Server tiruan sebaiknya memvalidasi HTML seperti Telegram
(tag yang didukung, entity, `code` tidak di dalam emphasis, batas 4096 karakter).
