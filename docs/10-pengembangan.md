# 10. Pengembangan

Konvensi kode dan penggunaan MCP ada di `CLAUDE.md` di root repo.

## Struktur solusi

| Project | Isi |
|---|---|
| `src/AiThread.Common` | Entity, enum, DTO, `AiThreadOptions`. |
| `src/AiThread.Data` | `AiThreadDbContext` (SQL Server via `IDbContextFactory`), migrasi EF, `RedisShortMemoryStore`, `InProcessEventBus`. |
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

Migrasi diterapkan otomatis saat aplikasi start (`InitializeDataLayerAsync`). `DesignTimeDbContextFactory` memakai
connection string LocalDB default.

## Peta kode orkestrasi

| File | Tanggung jawab |
|---|---|
| `Proxy/ClaudeRequestInspector.cs` | Memilah request Claude Code: kandidat orkestrasi vs passthrough; ambil working directory, sesi, prompt, riwayat. |
| `Implementations/ProjectService.cs` | `ResolveSessionAsync`: gerbang per sesi (project `.sln`/`.slnx` atau diabaikan, cache 30 menit, auto-register); penelusur folder, scan, registrasi. |
| `Workspace/SolutionLocator.cs` | Mencari solution dari working directory, scan rekursif, daftar folder yang layak ditelusuri, membaca isi `.sln`/`.slnx`. |
| `Orchestration/ThreadOrchestrator.cs` | Alur utama thread: project → planner → eksekusi → wrapper → memory, status & error. |
| `Orchestration/PlannerAgent.cs`, `PlannerTools.cs` | Loop tool planner dan definisi tool (JSON schema). |
| `Orchestration/ThreadPlan.cs`, `PlanValidator.cs` | Model rencana dan aturan validasinya. |
| `Orchestration/ExecutionEngine.cs` | Penjadwalan DAG paralel, pemilihan executor, skip dependensi gagal. |
| `Orchestration/ExecutorAgent.cs` | Menyusun input executor, retry, menerapkan edit di bawah lock. |
| `Orchestration/WrapperAgent.cs`, `MemoryExtractor.cs` | Ringkasan akhir dan ekstraksi memory. |
| `Orchestration/AgentPrompts.cs` | **Semua prompt peran** (planner, executor, wrapper, memory). |
| `Orchestration/ThreadRecorder.cs`, `ClientProgress.cs` | Persistensi + event SSE; teks progres untuk Claude Code. |
| `Workspace/*` | Sandbox path, tool baca repo, parser & applier SEARCH/REPLACE, I/O teks (BOM/line ending), diff, pencari solution. |
| `API/Infrastructure/AnthropicSseWriter.cs` | Menulis stream format Anthropic ke Claude Code. |

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

## Pengujian end-to-end tanpa biaya model

Pipeline bisa diuji dengan server tiruan 9router yang mengimplementasikan `GET /api/health`, `GET /v1/models`, dan
`POST /v1/chat/completions` (stream SSE OpenAI, termasuk `tool_calls`), dengan jawaban berbeda berdasarkan isi system
prompt (`You are the PLANNER`, `You are an EXECUTOR`, `You are the WRAPPER`, `maintain the memory`). Arahkan
Setting → Base URL ke server tiruan tersebut, lalu kirim request berformat Claude Code ke `/v1/messages` atau
gunakan *Jalankan prompt* di Dashboard. Gunakan database terpisah (ubah `ConnectionStrings:Default`) agar data uji
tidak bercampur.
