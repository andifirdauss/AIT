# Rancangan: CLI resmi sebagai mesin model ("CLI to CLI")

## Masalah
Sebagian developer tidak bisa login provider di 9router, misalnya karena akun perusahaan diloginkan atasan langsung
di CLI resmi. Tanpa 9router mereka tidak bisa memakai AIT, sehingga pekerjaan mereka tidak terekam (thread, journal
file, memory, status git, wiki bisnis).

## Keputusan
- **CLI = mesin model, bukan agen.** Orkestrasi tetap di AIT. CLI hanya menjawab satu giliran model, dengan tool
  bawaannya dimatikan. AIT tetap yang menulis file lewat SEARCH/REPLACE executor, sehingga `FileJournal`, rollback,
  dan memory tidak perlu diubah.
- **Penanda di model id, tanpa migrasi:** `cli:<tool>:<model>`, dibaca oleh `CliModelId`.
- **Satu titik sambung:** `NineRouterService.ChatAsync`, setelah `SecretGuard.RedactMessages` dan di dalam loop retry.
  Semua agent (planner, executor, reviewer, tester, wrapper, context keeper, memory, skill, business writer, uji node)
  ikut tanpa perubahan.
- **Tahap 1 hanya Claude Code** (`ClaudeCliAdapter`). Antarmuka `ICliModelService` memilih adapter per `tool`, agar
  Codex (`codex exec --json`) dan Gemini (`gemini -p --output-format stream-json`) bisa menyusul.
- **Tahap 2: Codex CLI, Gemini CLI, dan Antigravity CLI** (`CodexCliAdapter`, `GeminiCliAdapter`,
  `AntigravityCliAdapter`). Lihat [Tahap 2](#tahap-2-codex-cli-dan-gemini-cli).

## Alur satu panggilan
1. `CliTranscript.Render` mengubah pesan bentuk OpenAI dan `tools` menjadi `CliPrompt`:
   - system prompt + protokol tool tertulis;
   - giliran user. Satu giliran user dikirim apa adanya; percakapan ditulis sebagai `<conversation>` dengan
     `<tool_call>`/`<tool_result>`.
   - gambar giliran terakhir.
2. `ClaudeCliAdapter.RunAsync` menjalankan proses:
   - folder kerja kosong baru per panggilan, dan system prompt ditulis ke file;
   - `claude -p --input-format stream-json --output-format stream-json --verbose --include-partial-messages
     --system-prompt-file … --tools "" --strict-mcp-config --disable-slash-commands --setting-sources local
     --no-session-persistence --max-turns 1 [--model …]`;
   - satu baris user stream-json dikirim lewat stdin;
   - environment dibersihkan (`CLAUDECODE`, `ANTHROPIC_BASE_URL` loopback, autoupdater mati);
   - batas proses global `MaxProcesses` lewat `CliSlots` (pekerjaan latar belakang di `CliSlots.Background()` maks
     `BackgroundProcesses`), timeout `RequestTimeoutSeconds` (habis saat CLI masih menulis = `IsTimeLimit`, tidak
     di-retry ke model yang sama), stall 180 detik (di-retry).
3. `ClaudeStreamReader` membaca `text_delta` (diteruskan ke `onDelta`), `stop_reason`, serta baris `result`
   (`is_error`, `api_error_status`, `usage`).
4. Bila request membawa tools, `CliToolCallParser` mengubah blok `<tool_call name="…">{json}</tool_call>` di luar
   code fence menjadi `ToolCall`. JSON rusak dibiarkan sebagai teks, sehingga nudge planner yang sudah ada yang
   menanganinya.
5. Kegagalan dipetakan ke `NineRouterException` (`ClaudeCliAdapter.Failure`):
   - belum login → `IsProviderLoginRequired`;
   - 429 → rate limit;
   - overloaded/5xx → transient;
   - CLI tidak ada → tidak di-retry.

   Dengan begitu retry dan fallback node bekerja seperti untuk 9router.

## Batas etika
- AIT tidak membaca, menyalin, atau meneruskan kredensial CLI; binary resmi dijalankan sebagai user OS yang sama.
- Kebijakan managed perusahaan tetap berlaku, dan pemakaian tercatat di akun itu.

## Passthrough proxy lewat CLI
Bila CLI coding diarahkan ke AIT dan model passthrough adalah `cli:`:
- `AgentProxyHandler.CliPassthroughAsync` mengubah body klien menjadi pesan chat teks lewat `PassthroughTranscript`.
  Ini berlaku untuk keempat protokol; tool call/hasil tool menjadi teks, dan gambar ikut.
- Pesan itu dijawab lewat `router.ChatAsync`, sehingga disamarkan dan diteruskan ke CLI.
- Jawaban ditulis dengan `ClientResponses.Message` / `CreateWriter` yang sama dengan balasan orkestrasi.
- Tool klien tidak ditawarkan, dan model diberi catatan `TextOnlyNote`. Tool call asli dalam format klien perlu
  emulasi penuh per protokol, dan itu belum dikerjakan.
- `count_tokens` dengan model `cli:` langsung memakai estimasi.

Loop tidak terjadi walaupun `~/.claude/settings.json` mengarah ke AIT. Ini sudah diuji: dengan settings palsu yang
mengarah ke listener lokal, `claude -p` default mengirim 22 request ke sana, sedangkan dengan
`--setting-sources local` mengirim 0.

## Tahap 2: Codex CLI dan Gemini CLI

Status: diimplementasikan (2026-11-07). Perilaku yang berlaku ada di
[03-integrasi-cli.md](../03-integrasi-cli.md#cli-resmi-sebagai-mesin-model-tanpa-9router).

### Struktur
- `ICliEngine` (`Tool`, `Name`, `ListModelsAsync`, `RunAsync`, `DetectAsync`) adalah antarmuka bersama.
  `IClaudeCliAdapter`, `ICodexCliAdapter`, `IGeminiCliAdapter`, dan `IAntigravityCliAdapter` menurunkannya, dan
  semuanya di-register juga sebagai `ICliEngine`. `CliModelService(IEnumerable<ICliEngine>)` memilih mesin lewat
  `CliModelId.Tool`, `DetectAsync` memeriksa semua mesin secara paralel, dan `GetModelsAsync` mendaftar model milik
  mesin yang terpasang dengan owner `"<Name> (CLI)"`.
- Kode proses bersama ada di `CliProcess` (static helper). Isinya: `Resolve` (path konfigurasi → PATH → folder
  tambahan → shim npm `<command>.cmd` → entry `bin` paket), `ProbeAsync` (`--version`), `RunAsync` (stdin lalu
  ditutup, stdout per baris, timeout total + stall, kill satu pohon proses), `NewWorkFolder`, dan `WriteImage`.
- `CliFailure.Classify` → `Map` memetakan error dengan urutan login → rate limit → sibuk → model → lainnya. Setiap
  adapter memberi nama CLI dan cara login pemilik akun. Error `error_max_turns` khusus Claude tetap ada di
  `ClaudeCliAdapter.Failure`.
- Opsi `CliEngineOptions` (`Path`, `MaxProcesses`) diturunkan oleh `ClaudeCliOptions`, `CodexCliOptions`
  (+ `IgnoreUserConfig`), dan `GeminiCliOptions`.

### Codex CLI
- `codex exec [--image <file>]… --json --ephemeral --skip-git-repo-check --sandbox read-only --color never
  -c model_instructions_file='<file>' -c features.shell_tool=false -c web_search="disabled" -c project_doc_max_bytes=0
  [--ignore-user-config] [--model <m>] -`. Prompt dikirim lewat stdin (`-`). Gambar ditulis sebagai file di folder
  kerja.
- `CodexStreamReader` membaca JSONL `item.completed`: `agent_message` terakhir menjadi jawaban, `command_execution`
  dihitung lalu dicatat di log. Selain itu `turn.completed.usage`, `turn.failed.error.message`, dan event `error`.
  Status HTTP diambil dari teks error (`CliFailure.StatusIn`).
- Codex tidak mengirim delta teks, jadi `onDelta` dipanggil sekali dengan jawaban utuh.
- Pencegah loop: `OPENAI_BASE_URL` loopback (+ `OPENAI_API_KEY`) dibuang. Bila `IgnoreUserConfig = false` tetapi
  `profile`/`model_provider` tingkat atas di `config.toml` adalah `aithread`, `--ignore-user-config` tetap dipakai
  (`RoutesToAit`, hanya dua kunci itu yang dibaca).
- Model di pemilih hanya `cli:codex`, yaitu model bawaan Codex, karena nama model Codex berganti setiap rilis. Nama
  lain bisa diketik.

### Gemini CLI
- `gemini --output-format stream-json --extensions none --allowed-mcp-server-names none [--model <m>]`. Prompt
  dikirim lewat stdin: stdin yang di-pipe membuat CLI berjalan headless, dan tidak ada batas panjang command line.
  System prompt diberikan lewat `GEMINI_SYSTEM_MD=<file>`.
- `<work>/.gemini/settings.json` (workspace settings, di atas settings user dan di bawah system/managed) berisi:
  `tools.core: []` (allowlist kosong = semua tool ditolak), `tools.exclude` berisi nama tool bawaan (cadangan),
  `mcpServers: {}`, dan `context.fileName` dengan nama yang tidak pernah ada (tanpa `GEMINI.md`).
- `GeminiStreamReader` membaca `message` assistant (`delta: true` diteruskan ke `onDelta`), `tool_use` (dihitung
  lalu dicatat di log), `error`, dan `result` (`status`, `error.type/message`, `stats.input_tokens/output_tokens/cached`).
  Exit code 41 atau `error.type` yang berisi `Auth` dianggap belum login.
- Pencegah loop: Gemini CLI memuat `.env` pertama dari folder kerja ke atas (`<dir>/.gemini/.env`, `<dir>/.env`, lalu
  `~/.gemini/.env`, `~/.env`), dan tidak menimpa variabel yang sudah ada. Bila base URL di environment atau di `.env`
  itu adalah loopback, `GOOGLE_GEMINI_BASE_URL` diisi `https://generativelanguage.googleapis.com` (`FindEnvFile` +
  `EnvValue`, hanya kunci base URL yang dibaca). `GEMINI_API_KEY` dari environment ikut dibuang. Key AIT di `.env`
  akan ditolak Google sebagai error login, sehingga tidak terjadi loop.
- Gambar tidak bisa dikirim: `@file` butuh tool baca, yang dimatikan. Model diberi catatan berisi jumlah gambar yang
  tidak ikut. Giliran user yang diawali `/` diberi label agar tidak dijalankan sebagai slash command.

### Antigravity CLI (`agy`)
Antigravity CLI adalah CLI headless Google yang lebih baru (`agy`, dipasang di `%LOCALAPPDATA%\agy\bin`). Ia
menyediakan model Gemini, Claude, dan GPT-OSS lewat akun Google, dan dibuat sebagai mesin terpisah (`cli:agy:<model>`)
di samping Gemini CLI. Semua di bawah ini diuji langsung dengan `agy` 1.5.2.
- `agy --input-format stream-json --output-format stream-json --disable-slash-commands --agent ait --settings <file>
  [--model <m>]`. Satu baris stdin `{"event":"user","message":{"content":"…"}}`; hanya blok teks yang diterima (blok
  lain menghentikan sesi), jadi gambar diganti catatan (`CliProcess.WithImageNote`). `--settings` berupa file, karena
  JSON sebagai argumen akan rusak oleh quoting Windows.
- **Tanpa tool + system prompt AIT:** agy tidak punya opsi system prompt, tetapi agen workspace
  `<work>/.agents/agents/ait/agent.md` (frontmatter `tools: []`, `mainAgent: true`, `subagent: false`,
  `commandExecutionPolicy: off`) memakai isi Markdown-nya sebagai system prompt, menggantikan milik agy. Diukur: input
  turun dari sekitar 6.950 menjadi sekitar 2.050 token, dan model yang diminta menjalankan `whoami` atau web search
  tidak memanggil tool apa pun. Daftar `tools` di event `init` tetap daftar lengkap (generik).
- **Cadangan izin:** `--settings` menolak `command(*)`, `read_file(*)`, `write_file(*)`, `read_url(*)`,
  `execute_url(*)`, dan `mcp(*)`. Diuji tanpa agen kustom: `run_command`, `write_file`, dan `read_file` ditolak
  ("Matches user-configured deny rule"), tetapi `search_web` tidak tercakup aksi izin mana pun. Karena itu agen tanpa
  tool menjadi pengaman utama.
- **Function call native:** model Gemini sesekali tetap mengirim function call walaupun agennya tanpa tool. agy lalu
  mengakhiri giliran dengan `status: ERROR` ("improperly formatted function call"), sekitar 1 dari 3 panggilan
  ber-tool. Ada tiga penanganan:
  - kalimat pertama prompt agen melarang function call native;
  - pesan user diberi pengingat `<tool_call>` bila system prompt memuat protokol tool;
  - `RunAsync` mengulang langsung sampai 3 kali bila belum ada teks yang di-stream (`IsMalformedFunctionCall`).

  Hasilnya, round trip protokol tool lolos 8 dari 8.
- `AntigravityStreamReader`: `step_update` dengan `step_type: agent_response` → `text_delta` (stream); `step_type:
  tool` dihitung per `step_index` lalu dicatat di log; `result` → `status` (`SUCCESS`/`ERROR`), `response`, `error`,
  `finished_reason`, `usage.input_tokens/output_tokens/cache_read_tokens`, serta `conversation_id` (dari `init`
  atau `result`).
- **Riwayat agy:** setiap run disimpan sebagai percakapan (`~/.gemini/antigravity-cli/conversations/<id>.db` sekitar
  400 KB, ditambah `brain/<id>/`). Tidak ada opsi ephemeral. Bila `AntigravityCli:DeleteConversations` (bawaan
  `true`), `ForgetConversation` menghapus keduanya setelah proses selesai. Hanya id GUID dari run itu sendiri yang
  diterima, dan `conversation_summaries.db` milik agy tidak disentuh.
- **Model:** `agy models` (`<id>\t<label>`, tanpa memakai request model) → `ParseModels`, di-cache 10 menit. Bila
  gagal, hanya `cli:agy` (model bawaan) yang ditawarkan. Karena itu `ICliEngine.Models` diganti menjadi
  `ListModelsAsync`.
- Pencegah loop memakai `GeminiCliAdapter.CleanEnvironment` (base URL Gemini loopback → endpoint resmi).

### Pengujian
- `CliEngineTests`: reader, argumen, `RoutesToAit`, environment/`.env`, pemetaan error, dan pemilihan mesin dengan
  mesin palsu.
- `CliEngineLiveTests` (opt-in `AIT_LIVE_CODEX=1` / `AIT_LIVE_GEMINI=1` / `AIT_LIVE_AGY=1`, model lewat
  `AIT_LIVE_CODEX_MODEL` / `AIT_LIVE_GEMINI_MODEL` / `AIT_LIVE_AGY_MODEL`): jawaban polos, round trip protokol tool,
  deteksi, ditambah untuk agy daftar model dan satu thread penuh (planner → executor → wrapper mengubah file,
  `ThreadFileSnapshots` tercatat). Semua live test agy lolos dengan `agy` 1.5.2 (`gemini-3.8-flash-low`). Flag
  Codex/Gemini diambil dari dokumentasi resmi (context7). Kedua CLI itu belum terpasang di mesin pengembangan, jadi
  live test-nya perlu dijalankan di mesin yang punya CLI-nya sebelum rilis.

## Di luar cakupan
- Tool call asli pada passthrough lewat CLI (sesi di luar project yang bisa bertindak).
- Prompt cache antar panggilan (bisa memakai `--resume` per thread+peran bila biaya jadi masalah).
- Gambar lewat Gemini CLI.
