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
   - batas proses global `MaxProcesses`, timeout `RequestTimeoutSeconds`, stall 180 detik.
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

## Di luar cakupan tahap 1
- Adapter Codex dan Gemini CLI.
- Tool call asli pada passthrough lewat CLI (sesi di luar project yang bisa bertindak).
- Prompt cache antar panggilan (bisa memakai `--resume` per thread+peran bila biaya jadi masalah).
