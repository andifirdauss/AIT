# 10. Pengembangan

Konvensi kode dan penggunaan MCP ada di `CLAUDE.md` di root repo.

## Struktur solusi

| Project | Isi |
|---|---|
| `src/AiThread.Common` | Entity, enum, DTO, `AiThreadOptions`. |
| `src/AiThread.Data` | `AiThreadDbContext` (SQL Server via `IDbContextFactory`), migrasi EF, `RedisShortMemoryStore`, `EmbeddedRedisServer` (Garnet di dalam proses), `InProcessEventBus`. |
| `src/AiThread.Service` | Service utama (`NineRouterService`, `NodeService`, `ProjectService`, `MemoryService`, `SettingService`, dll.), `Orchestration/`, `Workspace/`, `Proxy/`, `Background/`. |
| `src/AiThread.API` | Controller REST `/api/*`, `AnthropicProxyController` (`/v1/*`), `StreamController` (SSE), infrastruktur. |
| `src/AiThread.UI` | Host ASP.NET Core MVC. View hanya kerangka; data dimuat oleh `wwwroot/js/pages/*.js` lewat `/api` + SSE. |
| `tests/AiThread.Tests` | Unit test xUnit. |

Alur dependensi: `UI → API → Service → Data → Common`.

## Perintah

```powershell
dotnet build AiThread.slnx
dotnet test tests/AiThread.Tests
dotnet run --project src/AiThread.UI --launch-profile http
```

Migrasi EF (tool lokal di `dotnet-tools.json`):

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add <NamaMigrasi> --project src/AiThread.Data --startup-project src/AiThread.Data
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/AiThread.Data --startup-project src/AiThread.Data
```

Instance background (`scripts/aithread.ps1`, lihat [Instalasi](01-instalasi.md#5-menjalankan-di-background))
memakai hasil publish di `%LOCALAPPDATA%\AiThread\app`, jadi build/debug dari repo tetap bisa jalan — asalkan
memakai port lain (`--urls`) atau instance background dihentikan dulu. `Background/ThreadLifecycleWorker` menjaga
status thread saat proses berhenti: shutdown rapi membatalkan thread yang berjalan, dan saat start thread yang
tertinggal dari proses yang mati mendadak ditandai *Cancelled*.

`AiThread.exe` standalone: `scripts/publish-exe.ps1` memakai profil `src/AiThread.UI/Properties/PublishProfiles/standalone.pubxml`
(win-x64, self-contained, single-file; `AssemblyName` jadi `AiThread` hanya di profil itu, build biasa tetap
`AiThread.UI`) dan membangun ke `dist/.build`, jadi tidak bentrok dengan `bin/` yang sedang dikunci. Menu, tray
(Win32 murni, tanpa WinForms agar RAM kecil), autostart, dan perintah CLI ada di `src/AiThread.UI/Hosting/`;
`Program.cs` hanya memanggil `Cli.RunAsync`. Tanpa argumen dan tanpa terminal/`--urls`/env `ASPNETCORE_*`, host berjalan
seperti biasa (`dotnet run`, Visual Studio, script background tidak berubah). Menguji menu membutuhkan konsol asli:
jalankan exe dengan `menu --port <port uji>` agar tidak menyentuh instance port 5225.

Migrasi diterapkan otomatis saat aplikasi start (`InitializeDataLayerAsync`). `DesignTimeDbContextFactory` memakai
connection string LocalDB default.

## Peta kode orkestrasi

| File | Tanggung jawab |
|---|---|
| `Proxy/ClaudeRequestInspector.cs` | Memilah request Claude Code: kandidat orkestrasi vs passthrough; ambil working directory, sesi, prompt, riwayat. |
| `Implementations/ProjectService.cs` | `ResolveSessionAsync`: gerbang per sesi (project `.sln`/`.slnx` atau diabaikan, cache 30 menit, auto-register); penelusur folder, scan, registrasi. |
| `Workspace/SolutionLocator.cs` | Mencari solution dari working directory, scan rekursif, daftar folder yang layak ditelusuri, membaca isi `.sln`/`.slnx`. |
| `Orchestration/ThreadOrchestrator.cs` | Alur utama thread: project → planner → eksekusi → wrapper → memory, status & error. |
| `Orchestration/PlannerAgent.cs`, `PlannerTools.cs` | Planner per backend (loop tool 9router / Claude Code CLI), failover antar node planner, definisi tool (JSON schema). |
| `Planner/ClaudeCliRunner.cs`, `ClaudeCliPlanner.cs` | Menjalankan `claude -p` (stream-json), argumen read-only, file `--mcp-config` sementara. |
| `Planner/PlannerSessionRegistry.cs` + `API/Controllers/McpPlannerController.cs` | Server MCP minimal (`/mcp/planner/{token}`) untuk memory & `submit_plan`; daftar session id CLI untuk menolak loop ke proxy. |
| `Orchestration/ThreadPlan.cs`, `PlanValidator.cs` | Model rencana dan aturan validasinya. |
| `Orchestration/ExecutionEngine.cs` | Penjadwalan DAG paralel, pemilihan executor, skip dependensi gagal. |
| `Orchestration/ExecutorAgent.cs` | Menyusun input executor, retry, menerapkan edit di bawah lock. |
| `Orchestration/WrapperAgent.cs`, `MemoryExtractor.cs` | Ringkasan akhir dan ekstraksi memory. |
| `Orchestration/ContextKeeper.cs`, `ContextPackBuilder.cs`, `ThreadAnchor.cs` | Konteks sesi Claude Code: penanda thread di balasan, pencarian ringkasan dari history, pembaruan ringkasan setelah thread, tool `read_thread`. |
| `Orchestration/SessionHandoff.cs`, `TranscriptDigest.cs`, `Implementations/ClaudeHookConfig.cs`, `API/Controllers/ClaudeHooksController.cs` | Context Keeper untuk `/clear`: hook `SessionEnd`/`SessionStart`, ringkasan transcript, penulisan blok `hooks` di settings Claude Code. |
| `Orchestration/AgentPrompts.cs` | **Semua prompt peran** (planner, executor, wrapper, memory, context keeper). |
| `Orchestration/ThreadRecorder.cs`, `ClientProgress.cs` | Persistensi + event SSE; teks progres untuk Claude Code. |
| `Workspace/*` | Sandbox path, tool baca repo, parser & applier SEARCH/REPLACE, I/O teks (BOM/line ending), diff, pencari solution. |
| `API/Infrastructure/AnthropicSseWriter.cs` | Menulis stream format Anthropic ke Claude Code. |
| `Background/TelegramPollingWorker.cs`, `Telegram/TelegramBotRuntime.cs` | Long polling `getUpdates` bot Telegram, mulai ulang saat setting Telegram disimpan, backoff saat offline, status untuk kartu Setting. |
| `Telegram/TelegramApi.cs` | Klien Bot API minimal: JSON dengan `Content-Length`, token dibaca dari runtime setting tiap panggilan, kirim ulang sebagai teks biasa bila HTML ditolak. HttpClient-nya tanpa logger karena token ada di URL. |
| `Telegram/TelegramBot.cs` | Izin chat, perintah (`/projects`, `/status`, `/cancel`, `/diff`, `/new`), memulai thread (`ThreadSource.Telegram`, sesi `telegram:<chat>`, riwayat percakapan untuk planner). |
| `Telegram/TelegramThreadReporter.cs` | Pesan status live per thread (event bus sebagai pemicu, database sebagai sumber data) dan pesan hasil akhir. |
| `Telegram/TelegramFormatter.cs`, `TelegramMessages.cs` | Markdown wrapper → HTML Telegram (tanpa `<code>` di dalam `<b>`/`<i>`, dipecah per 4096 karakter) dan semua teks bot. |

## Dokumentasi di aplikasi

Menu **Docs** (`/Docs`, `/Docs/{nama-file-tanpa-.md}`) merender `docs/*.md` dengan Markdig lewat `DocsService`.

- Lokasi folder: `AiThread:DocsPath` bila diisi; selain itu `docs/` repo (dua tingkat di atas `src/AiThread.UI`, dipakai
  saat development sehingga perubahan langsung terlihat), lalu salinan `docs/` di folder output/publish.
- `README.md` adalah halaman indeks; urutan navigasi mengikuti nama file, jadi pertahankan awalan angka (`11-...md`).
- Judul diambil dari heading `# ` pertama. Tautan `nama-file.md#anchor` otomatis menjadi `/Docs/nama-file#anchor`;
  anchor mengikuti aturan GitHub/GitLab. Tautan ke file di luar `docs/` tidak bisa dibuka dari web — tulis sebagai teks.

## Titik ekstensi umum

**Menyetel perilaku model** — ubah `AgentPrompts.cs`. Prompt ke model ditulis dalam bahasa Inggris; teks untuk
user dalam bahasa Indonesia. Untuk penyesuaian per tim tanpa mengubah kode, gunakan *Instruksi tambahan* di node.

**Menambah tool planner**
1. Tambahkan definisi di `PlannerTools.All()` (nama konstanta + JSON schema).
2. Tangani di `PlannerAgent.RunToolAsync` (dan `Describe` untuk teks progres).
3. Tool harus read-only dan memakai `WorkspaceScope` untuk setiap path.

**Menambah aturan validasi rencana** — `PlanValidator.Validate`; tulis pesan error dalam bahasa Inggris yang
menjelaskan cara memperbaikinya (pesan dikirim balik ke planner). Tambahkan test di `PlanValidatorTests`.

**Mengubah format edit executor** — `EditBlockParser`, `EditApplier`, prompt `AgentPrompts.Executor`, dan test
terkait harus diubah bersamaan.

**Event SSE baru** — tambahkan konstanta di `AppEventTypes`, publikasikan lewat `IEventBus.Publish`, lalu tangani
di `AiThread.stream(...)` pada halaman terkait.

**Menambah perintah bot Telegram**
1. Tambahkan ke `TelegramMessages.Commands` (menu `/` di Telegram diperbarui saat bot mulai polling).
2. Tangani di `TelegramBot.HandleCommandAsync`; tulis teks balasan di `TelegramMessages` sebagai HTML Telegram dan
   escape input dengan `TelegramFormatter.Escape`.
3. Jangan menaruh `<code>`/`<pre>` di dalam `<b>`/`<i>`/`<s>`/`<a>` — Telegram menolak pesan seperti itu.

## Pengujian end-to-end tanpa biaya model

Pipeline bisa diuji dengan server tiruan 9router yang mengimplementasikan `GET /api/health`, `GET /v1/models`, dan
`POST /v1/chat/completions` (stream SSE OpenAI, termasuk `tool_calls`), dengan jawaban berbeda berdasarkan isi system
prompt (`You are the PLANNER`, `You are an EXECUTOR`, `You are the WRAPPER`, `maintain the memory`). Arahkan
Setting → Base URL ke server tiruan tersebut, lalu kirim request berformat Claude Code ke `/v1/messages` atau
gunakan *Jalankan prompt* di Dashboard. Gunakan database terpisah (ubah `ConnectionStrings:Default`) agar data uji
tidak bercampur.

Bot Telegram diuji dengan cara yang sama: arahkan `AiThread:Telegram:ApiBaseUrl` ke server tiruan Bot API yang
menjawab `getMe`, `deleteWebhook`, `setMyCommands`, `getUpdates` (long poll dengan `offset`), `sendMessage`,
`editMessageText`, dan `answerCallbackQuery`, lalu sisipkan update pesan/tombol ke antrean `getUpdates`-nya.
Kecilkan `PollTimeoutSeconds` (mis. `2`) agar uji cepat. Server tiruan sebaiknya memvalidasi HTML seperti Telegram
(tag yang didukung, entity, `code` tidak di dalam emphasis, batas 4096 karakter).
