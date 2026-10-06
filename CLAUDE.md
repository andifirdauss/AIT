# CLAUDE.md

Panduan untuk Claude Code di repo **ai-thread** (solusi .NET: `AiThread.UI`, `AiThread.API`, `AiThread.Service`, `AiThread.Data`, `AiThread.Common` di `src/`).

## Prioritas MCP

MCP server dikonfigurasi di [.mcp.json](.mcp.json). **Selalu utamakan MCP ini** dibanding tool bawaan atau pengetahuan internal.

### context7 — dokumentasi library
- **Wajib** dipakai sebelum menulis/mengubah kode yang memakai library atau framework (ASP.NET Core, EF Core, Bootstrap, jQuery, NuGet package, dll.).
- Alur: `resolve-library-id` → `get-library-docs` (atau `query-docs`) dengan topik spesifik.
- Jangan mengandalkan ingatan untuk API, signature, atau konfigurasi — verifikasi via context7, terutama untuk versi terbaru.
- Jika context7 tidak menemukan library, baru gunakan WebSearch/WebFetch.

### filesystem — operasi file
- Utamakan tool MCP filesystem (`read_text_file`, `read_multiple_files`, `write_file`, `edit_file`, `list_directory`, `directory_tree`, `search_files`, `get_file_info`) untuk membaca, menulis, dan menjelajah file proyek.
- Gunakan `read_multiple_files` untuk membaca beberapa file sekaligus.
- Akses dibatasi ke `D:/External/Presistence/ai-thread`.
- Fallback ke Read/Edit/Write/Glob/Grep bawaan hanya jika MCP filesystem gagal atau tidak tersedia.

## Konvensi
- Balas dalam Bahasa Indonesia; identifier kode dalam Bahasa Inggris.
- Abaikan folder `bin/`, `obj/`, dan `wwwroot/lib/` (library pihak ketiga) saat menjelajah kode.
- Teks UI dan pesan log untuk user dalam Bahasa Indonesia; prompt ke model AI dan pesan error tool planner dalam Bahasa Inggris.

## Publish npm
- Token npm ada di environment variable user `NPM_TOKEN` (Windows). **Jangan pernah menulis nilainya ke file repo, commit, atau chat.**
- Sebelum `scripts/publish-npm.ps1 -Publish`, naikkan `<Version>` di `src/AiThread.UI/AiThread.UI.csproj` (npm menolak versi yang sudah ada). Skrip menyamakan versi `package.json`.
- Publish memakai `.npmrc` sementara yang dibuat dari `NPM_TOKEN` lalu dihapus; jangan menyimpan `.npmrc` berisi token di folder repo.
- Versi di registry dicek dengan `npm view aithread version`.

## Arsitektur singkat
Lihat [README.md](README.md) dan [docs/](docs/README.md) untuk alur lengkap. Perbarui dokumen di `docs/` bila perilaku, default, atau endpoint berubah.
- `AiThread.Common`: entity (`ThreadRun`, `ThreadTask`, `AiNode`, `Project` (+ `ProjectKey` lintas mesin), `LongMemory`, `BusinessPage`/`BusinessPageRevision`/`BusinessAsset`, `ThreadAttachment`, `SystemSetting`, `ActivityLog`), enum, DTO.
- `AiThread.Data`: `AiThreadDbContext` (SQL Server, dipakai via `IDbContextFactory`), `RedisShortMemoryStore`, `EmbeddedRedisServer` (Garnet di dalam proses, pengganti Redis/Docker), `InProcessEventBus` (sumber SSE), store memory yang bisa dipindah ke DB eksternal (`SwitchableShortMemoryStore`, `LongMemoryStore`, `BusinessMemoryStore`; `MemorySchema` membuat tabel & menambah kolom baru di DB eksternal), migrasi EF.
- `AiThread.Service`: `NineRouterService`, `NodeService`, `ProjectService` (+ `ProjectMemoryKeys`: backfill/rekey/hapus memory per project key), `MemoryService` (scope per `ProjectKey`, ikut mengindeks halaman wiki bisnis untuk planner), `BusinessMemoryService`, `SettingService`; `Business/` (business memory: `BusinessDigestCollector` thread yang belum dirangkum, `BusinessMemoryWriter` route → tulis halaman, `BusinessDigestRunner` + `Background/BusinessMemoryWorker`, `BusinessPageStore` simpan dengan versi + revisi, `BusinessMarkdown` render aman); `Workspace/ProjectKeyResolver` (key dari `.aithread/project.json` → key manual → git remote → `local:`); `Orchestration/` (planner → `ExecutionEngine` paralel → `VerificationStage` (build + `ReviewerAgent`, fix task `V<n>-<m>` via `FixTaskFactory`) → wrapper → `MemoryExtractor`); `Verification/` (`BuildCommandResolver` perintah build tetap per stack, `BuildRunner` proses + timeout, `BuildOutputParser`); `Workspace/` (sandbox path, parser & applier SEARCH/REPLACE); `Implementations/Cli/` (`ICliIntegration` per CLI untuk menu CLI: `ClaudeCodeIntegration`, `OhMyPiProviderIntegration` menambah provider `aithread` di `~/.omp/agent/models.yml` lewat `YamlProviderBlock`); `Proxy/` (`ProtocolAdapter` mengubah request Anthropic/OpenAI/Gemini ke satu bentuk, `ClaudeRequestInspector` memilah orkestrasi vs passthrough); `Skills/` (gudang skill offline: `SkillSourceLocator` link→cara tarik, `SkillFetcher` ZIP GitHub/GitLab atau `git clone`, `SkillDiscovery` scan `SKILL.md`, `SkillCatalogAgent` analisis AI, `SkillImporter` + `Background/SkillImportWorker`, `SkillOrchestrator` saran skill untuk node; isi skill ditempel ke system prompt node oleh `Orchestration/NodePromptBuilder` → `AgentPrompts.ForNode`); worker di `Background/`.
- `AiThread.API`: proxy multi-format lewat `AgentProxyHandler` — `AnthropicProxyController` (`/v1/messages`), `OpenAiProxyController` (`/v1/chat/completions`, `/v1/responses` untuk Codex), `GeminiProxyController` (`/v1beta/models/*`); `ClientStreamWriters` menulis balasan sesuai format klien; `StreamController` (SSE), REST `/api/*`.
- `AiThread.UI`: host MVC; view hanya kerangka, data dimuat oleh `wwwroot/js/pages/*.js` lewat `/api` + SSE.
  `Hosting/`: entry point `AiThread.exe` (`Cli`: menu interaktif, `tray`, `serve`, `stop`, dll.), ikon tray Win32, autostart. Exe standalone: `scripts/publish-exe.ps1`.
- Test: `tests/AiThread.Tests` (`dotnet test`). Migrasi: `dotnet tool run dotnet-ef migrations add <Nama> --context AiThreadDbContext --project src/AiThread.Data --startup-project src/AiThread.Data`.
