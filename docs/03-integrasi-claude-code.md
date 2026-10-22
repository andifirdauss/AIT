# 3. Integrasi Claude Code

Claude Code membaca variabel lingkungan dari blok `env` di `settings.json`. AIT memakai tiga (plus satu
opsional) variabel:

| Variabel | Nilai | Fungsi |
|---|---|---|
| `ANTHROPIC_BASE_URL` | `http://localhost:5225` | Semua request Claude Code dikirim ke AIT, bukan ke Anthropic. |
| `ANTHROPIC_AUTH_TOKEN` | Proxy API key, atau `aithread-local` bila kosong | Dikirim sebagai `Authorization: Bearer`. Kredensial Anthropic Anda tidak ikut terkirim ke proxy. |
| `API_TIMEOUT_MS` | `1800000` (30 menit) | Satu thread bisa memakan beberapa menit; default Claude Code (10 menit) bisa terlalu pendek. |
| `ANTHROPIC_CUSTOM_HEADERS` | `X-AIThread-Project: <folder root project>` | Hanya untuk cakupan per project. Mengikat sesi ke project yang tepat. |

## Cara A — dari UI (disarankan)

### Per project

Hanya sesi Claude Code yang dibuka di project tersebut yang melewati AIT.

1. **Project** → *Telusuri…* → cari folder project (navigasi folder atau *Scan* sub-folder; folder project bertanda
   stack atau <kbd>git</kbd>) → centang → *Daftarkan terpilih*. Bisa juga menempel path folder di kolom lalu *Daftarkan*.
   Project juga terdaftar otomatis pada prompt pertama sesi Claude Code yang dibuka di folder project.
2. Paling cepat: di tabel **Project**, nyalakan switch kolom **AIT** pada project tersebut (matikan untuk
   mengembalikan). Switch memakai URL proxy default (alamat http aplikasi).
3. Atau lewat menu **CLI** → kartu *Claude Code* → nyalakan switch project di daftar *Per project*. URL proxy yang
   ditulis diatur di kolom *URL proxy AIT* di bagian atas halaman; *Yang akan ditulis* menampilkan pratinjaunya.
4. Buka sesi Claude Code baru di project itu agar berlaku.

File yang ditulis: `<folder project>/.claude/settings.local.json`, dengan folder project = folder root project
(tertera di kotak status). `settings.local.json` adalah setting lokal Claude Code dan biasanya di-*gitignore*.

> Claude Code membaca `.claude/settings.local.json` dari folder tempat Anda menjalankannya. Jika Anda biasa membuka
> Claude Code di sub-folder project atau di folder lain, gunakan [Cara B](#cara-b--manual) dan letakkan file di
> folder tersebut.

### Global

Semua sesi Claude Code di komputer ini melewati AIT. Di menu **CLI**, nyalakan switch *Global* pada kartu Claude
Code (ada konfirmasi); switch yang sama ada di Setup → langkah *Arahkan CLI coding ke AIT*. File yang ditulis:
`%USERPROFILE%\.claude\settings.json`.

Hanya sesi yang dibuka di dalam sebuah project yang diorkestrasi, yaitu folder yang berisi `.git` atau file build
(`.sln`, `pom.xml`, `package.json`, `go.mod`, dst.; lihat [Cek sesi](07-cara-kerja.md#2-cek-sesi-project-atau-bukan)).
Sesi di folder lain (folder kosong, folder catatan, dll.) **diabaikan**: prompt-nya diteruskan langsung ke 9router
dengan model passthrough, sehingga Claude Code tetap bekerja seperti biasa di sana, tanpa memory.

### Status yang ditampilkan

| Status | Arti |
|---|---|
| ✓ Claude Code sudah diarahkan ke AIT | `ANTHROPIC_BASE_URL` di file sama dengan URL proxy. |
| `ANTHROPIC_BASE_URL saat ini: … (bukan AIT)` | File mengarah ke tempat lain (mis. langsung ke 9router). *Terapkan* akan menimpanya. |
| Belum diarahkan | Belum ada `ANTHROPIC_BASE_URL`. |

Sebelum perubahan pertama, file asli dibackup **sekali** sebagai `settings.json.aithread-backup` /
`settings.local.json.aithread-backup`. Kunci lain di file (model, hooks, dll.) tidak diubah, dan
`ANTHROPIC_CUSTOM_HEADERS` milik Anda sendiri tidak ditimpa.

AIT juga menambahkan aturan `permissions.deny` untuk file kredensial (`Read(//**/*.pem)`, `Read(//**/.npmrc)`,
`Read(~/.ssh/**)`, `Read(~/.aws/**)`, dll.), sehingga Claude Code sendiri tidak membacanya. Aturan permission milik Anda
tidak disentuh. Daftar lengkapnya ada di [Keamanan & privasi](12-keamanan.md#file-kredensial-yang-diblokir).

## Cara B — manual

Tambahkan ke `.claude/settings.local.json` di folder tempat Anda menjalankan `claude`
(atau ke `~/.claude/settings.json` untuk global):

```json
{
  "env": {
    "ANTHROPIC_BASE_URL": "http://localhost:5225",
    "ANTHROPIC_AUTH_TOKEN": "aithread-local",
    "API_TIMEOUT_MS": "1800000",
    "ANTHROPIC_CUSTOM_HEADERS": "X-AIThread-Project: D:\\Source\\MyApp"
  }
}
```

- Ganti `aithread-local` dengan Proxy API key bila Anda mengisinya di Setting.
- `ANTHROPIC_CUSTOM_HEADERS` opsional. Tanpa header ini, AIT menentukan project dari working directory sesi
  (folder itu atau folder induknya). Alternatif lain: `X-AIThread-Cwd: <folder>` untuk menetapkan working directory.
  Header lama `X-AIThread-Solution` (path `.sln`) tetap diterima; foldernya dipakai sebagai petunjuk project.

## Verifikasi

1. **Tutup lalu buka sesi Claude Code baru** — env hanya dibaca saat sesi dimulai.
2. Ketik prompt sederhana, mis. "jelaskan struktur project ini".
3. Jawaban harus diawali baris seperti:

   ```
   **AIT** · `MyApp` · thread `5e469b54`
   🧠 Planner `…` menganalisis project & menyusun rencana…
   ```

4. Di Dashboard AIT muncul thread baru dan log `Thread baru dari Claude Code · MyApp`.

Jika jawaban datang tanpa baris `**AIT**`, lihat [Troubleshooting](08-troubleshooting.md#claude-code-tidak-melewati-aithread).

## Mengembalikan

- UI: menu **CLI** → matikan switch yang sama (atau switch **AIT** di tabel Project). Kunci AIT
  (`ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, `API_TIMEOUT_MS`, dan header `X-AIThread-Project`, atau
  `X-AIThread-Solution` dari versi lama) dihapus, begitu juga aturan `permissions.deny` yang ditambahkan AIT;
  isi lain dibiarkan.
- Manual: hapus kunci-kunci tersebut, atau pulihkan dari file `*.aithread-backup`.

Buka sesi Claude Code baru setelahnya.

## Context Keeper untuk /clear

Sesi Claude Code yang panjang mahal karena **setiap request membaca ulang seluruh konteks**. `/clear` membuat konteks
kecil lagi, tetapi alurnya hilang. Hook ini menjembatani keduanya, dan berlaku untuk **semua** sesi di cakupannya —
termasuk sesi yang tidak diarahkan ke proxy AIThread:

1. Anda menjalankan `/clear`. Hook `SessionEnd` (reason `clear`) mengirim lokasi transcript sesi lama ke AIT,
   yang langsung membalas lalu bekerja di latar.
2. AIT meringkas transcript: prompt Anda, teks balasan, satu baris per tool call, error tool, dan ringkasan
   `/compact` (hasil tool, thinking, dan sub-agent dilewati; maks 60.000 karakter). Context Keeper (node Context
   Keeper, atau Wrapper) menulis ringkasan sesi dari situ, berikut ringkasan yang dipakai sesi lama itu sendiri.
3. Hook `SessionStart` (source `clear`) sesi baru di folder yang sama menunggu ringkasan (maks ±40 detik) dan
   menyisipkannya sebagai `additionalContext`. Claude melanjutkan dari ringkasan itu dengan konteks yang kecil.

`/clear` berikutnya membangun ringkasan dari ringkasan sebelumnya plus sesi terbaru, jadi alur tetap tersambung.

**Memasang**: **CLI → kartu Claude Code → Hook: Context Keeper /clear & pemulih secret** → pilih cakupan (Global
disarankan agar berlaku di semua project) → **Pasang hook**. Yang ditulis ke file settings (hook lain milik Anda tidak
disentuh):

```json
{
  "hooks": {
    "SessionEnd": [
      { "matcher": "clear", "hooks": [ { "type": "http", "url": "http://localhost:5225/api/hooks/claude/session-end", "timeout": 5 } ] }
    ],
    "SessionStart": [
      { "matcher": "clear", "hooks": [ { "type": "http", "url": "http://localhost:5225/api/hooks/claude/session-start", "timeout": 60 } ] }
    ],
    "PreToolUse": [
      { "matcher": "Edit|MultiEdit|Write|NotebookEdit|Bash", "hooks": [ { "type": "http", "url": "http://localhost:5225/api/hooks/claude/pre-tool-use", "timeout": 10 } ] }
    ]
  }
}
```

Hook `PreToolUse` adalah **pemulih secret**. Model hanya melihat secret sebagai `{{AIT_SECRET:…}}`
([Keamanan & privasi](12-keamanan.md)). Sebelum Claude Code menjalankan Edit/Write/Bash yang berisi placeholder, AIT
menggantinya dengan nilai asli, dan Claude Code tetap meminta konfirmasi Anda. Placeholder yang tidak dikenali AIT
menghentikan tool tersebut, supaya tidak tertulis ke file apa adanya. Hook yang dipasang oleh versi lama (tanpa
`PreToolUse`) ditandai *versi lama*; klik **Pasang hook** lagi untuk memperbaruinya.

Hook berlaku untuk sesi Claude Code yang dibuka setelah dipasang. **Lepas hook** menghapus entri AIT saja.

Catatan:

- Setelah `/clear`, Claude Code menunggu ringkasan (biasanya beberapa detik, tergantung model Context Keeper). Bila
  AIT mati atau ringkasan belum selesai dalam 40 detik, sesi baru tetap dimulai tanpa ringkasan.
- Sesi lama dan baru dicocokkan lewat working directory. Bila dua sesi di folder yang sama di-`/clear` pada saat
  bersamaan, ringkasan bisa tertukar.
- Isi transcript (versi ringkas) dikirim ke model Context Keeper lewat 9router. AIT hanya mau membaca transcript
  di dalam `~/.claude/projects`.
- Ringkasan otomatis `/compact` bawaan Claude Code tidak diganti; hook ini hanya bekerja pada `/clear`.

## Klien lain: Codex, Gemini CLI, oh-my-pi, dll.

AIT tidak terikat ke Claude Code. Ia menerima empat format request, dan 9router menerjemahkannya ke provider mana
pun (Claude, GPT, Gemini, …) — jadi model yang dipakai tidak harus sama dengan "bahasa" klien:

| Format | Endpoint AIT | Klien contoh |
|---|---|---|
| Anthropic Messages | `POST /v1/messages` | Claude Code, oh-my-pi (`anthropic-messages`), opencode (`@ai-sdk/anthropic`) |
| OpenAI Chat Completions | `POST /v1/chat/completions` | Qwen Code, Cursor, Continue, oh-my-pi (`openai-completions`) |
| OpenAI Responses | `POST /v1/responses` | Codex CLI |
| Gemini native | `POST /v1beta/models/{model}:generateContent` dan `:streamGenerateContent` | Gemini CLI |

Aturan orkestrasi sama untuk semuanya: request diorkestrasi bila membawa `tools`, pesan terakhirnya dari user (bukan
hasil tool), dan sesinya berasal dari sebuah project (folder dengan `.git` atau file build); balasannya dikirim dalam format klien itu sendiri
(stream maupun non-stream). Selain itu request diteruskan ke endpoint 9router yang sama dengan model passthrough.

Nama model yang dikirim klien diabaikan pada passthrough dan tampil apa adanya pada balasan orkestrasi; model yang
sebenarnya dipakai ditentukan Setting *Passthrough model* dan Node.

### Lewat menu CLI

Menu **CLI** punya kartu untuk Claude Code, oh-my-pi, Codex CLI, Gemini CLI, opencode, dan Qwen Code. Semua kartu
bekerja sama: AIT hanya menulis/menghapus entri miliknya, backup file asli disimpan sekali sebagai `*.aithread-backup`,
dan file yang hanya berisi entri AIT dihapus saat switch dimatikan. Bila *Proxy API key* diisi, nilainya ikut ditulis
(kosong = `aithread-local`).

| CLI | File | Scope | Cara memakai |
|---|---|---|---|
| Codex CLI | `~/.codex/config.toml` (atau `$CODEX_HOME`) | global | `codex --profile aithread` (`-p aithread`); `codex` biasa tetap memakai provider aslinya |
| Gemini CLI | `~/.gemini/.env`, atau `<project>/.gemini/.env` | global + per project | pilih *Use Gemini API Key* di `/auth` |
| opencode | `~/.config/opencode/opencode.json` (atau `opencode.jsonc` yang sudah ada) | global | pilih `aithread/aithread` lewat `/models` |
| Qwen Code | `~/.qwen/settings.json` | global | pilih model *AIT* lewat `/model` |

**Codex CLI** — Codex tidak mengizinkan provider di `.codex/config.toml` project, jadi hanya global. Blok ini
ditambahkan di akhir file (Codex hanya memakai `wire_api = "responses"`; `stream_idle_timeout_ms` dipanjangkan
karena thread orkestrasi bisa diam beberapa menit):

```toml
# >>> AIThread (dikelola otomatis, lepas lewat menu CLI) >>>
[model_providers.aithread]
name = "AIT"
base_url = "http://localhost:5225/v1"
wire_api = "responses"
experimental_bearer_token = "aithread-local"   # Proxy API key bila diisi
stream_idle_timeout_ms = 1800000

[profiles.aithread]
model_provider = "aithread"
# <<< AIThread <<<
```

AIT menolak menulis bila sudah ada `[model_providers.aithread]` / `[profiles.aithread]` buatan Anda, atau bila
`model_providers`/`profiles` ditulis sebagai inline table (`model_providers = { ... }`).

**Gemini CLI** — AIT menulis dua variabel di `.gemini/.env` (HTTP diizinkan karena `localhost`):

```dotenv
# >>> AIThread (dikelola otomatis, lepas lewat menu CLI) >>>
GOOGLE_GEMINI_BASE_URL="http://localhost:5225"
GEMINI_API_KEY="aithread-local"
# <<< AIThread <<<
```

- Base URL hanya berlaku dengan autentikasi *Gemini API Key*; login Google tidak lewat AIT.
- Gemini CLI hanya membaca **satu** `.env`: yang pertama ditemukan dari folder kerja ke atas (`.gemini/.env` lalu
  `.env`), baru `~/.gemini/.env`. Project yang punya `.env` sendiri di root menutupi switch global — pakai switch
  per project untuk project itu. Variabel lingkungan sistem dengan nama sama juga menang atas `.env`.
- AIT menolak menulis bila file sudah berisi `GEMINI_API_KEY` atau `GOOGLE_GEMINI_BASE_URL` buatan Anda; jadikan
  komentar dulu.
- `.gemini/.env` per project berisi Proxy API key — pastikan tidak ikut di-commit.

**opencode** — provider `aithread` (format Anthropic lewat `@ai-sdk/anthropic`) ditambahkan ke `provider` di
`opencode.json` global, dengan `timeout: false` agar thread panjang tidak diputus. Hanya global, karena
`opencode.json` project biasanya di-commit. AIT menolak menulis bila file berisi komentar (tidak bisa ditulis ulang
tanpa menghapusnya) atau sudah punya provider `aithread` buatan Anda.

**Qwen Code** — model `aithread` (OpenAI-compatible, `baseUrl` = `<proxy>/v1`) ditambahkan ke
`modelProviders.openai` beserta `env.AITHREAD_API_KEY`, dengan `generationConfig.timeout` 30 menit. Hanya global,
karena Qwen Code mengganti (bukan menggabung) `modelProviders` dari settings project. Aturan komentar dan id yang
sudah dipakai sama dengan opencode. Batas umur stream bawaan Qwen Code (15 menit, `QWEN_STREAM_MAX_LIFETIME_MS`)
tetap berlaku.

**oh-my-pi (paling mudah: menu CLI)** — nyalakan switch *Global* pada kartu *oh-my-pi (omp) — provider
AIT*. AIT menambah satu provider di `~/.omp/agent/models.yml` (hanya global; omp tidak punya `models.yml`
per project), tanpa menyentuh provider lain:

```yaml
# >>> AIThread (dikelola otomatis, lepas lewat menu CLI) >>>
providers:
  AIT:
    baseUrl: "http://localhost:5225"
    api: anthropic-messages
    apiKey: "aithread-local"        # Proxy API key bila diisi di Setting
    models:
      - id: AIThread
        name: AIThread
        contextWindow: 1000000
        maxTokens: 32000
# <<< AIThread <<<
```

- Pilih model `AIT/AIThread` lewat `/model` di omp (atau `omp --model AIT/AIThread`); status bar omp menampilkan
  `AIThread`. Provider Anthropic asli tetap tersedia, jadi Anda bisa berpindah per sesi.
- Blok lama dari versi sebelumnya (provider `aithread/aithread`) tampil belum aktif di menu CLI; nyalakan switch
  *Global* lagi untuk menggantinya dengan `AIT/AIThread`.
- Bila `models.yml` sudah punya `providers:`, entri `AIT` disisipkan di bawahnya dengan indentasi yang sama.
  AIT menolak menulis bila `providers` berbentuk satu baris (`providers: { ... }`), bila sudah ada provider
  `AIT` buatan Anda, atau bila masih ada `models.json` lama yang belum dimigrasi omp.
- Project tetap dikenali dari working directory yang dikirim omp di setiap request.
- Diuji dengan omp 18.4.4: omp mengirim `POST <baseUrl>/v1/messages?beta=true` dengan `Authorization: Bearer <apiKey>`.
  Nilai `apiKey` yang sama dengan nama environment variable diganti isi variabel itu.

### Sesi dan batasan

**Membantu AIT mengenali sesi.** Working directory dicari dari header `X-AIThread-Cwd`, lalu dari teks request:
`Primary working directory:` (Claude Code), `<cwd>…</cwd>` (Codex), `I'm currently working in the directory:`
(Gemini CLI), atau `Current working directory:` / `Working directory:` / `Current directory:` / `cwd:`. Bila klien Anda
tidak mengirim salah satunya, set header `X-AIThread-Cwd` (atau `X-AIThread-Project`). Id sesi dibaca dari header
`X-AIThread-Session`, `x-claude-code-session-id`, `session_id` (Codex) atau `x-session-id`, dari `prompt_cache_key`
(OpenAI), atau dari `metadata.user_id`; tanpa id sesi, keputusan project diingat per working directory.

**Batasan**
- Endpoint Gemini hanya `generateContent` dan `streamGenerateContent` (`countTokens` dll. tidak diproxy), dan stream
  Gemini selalu berformat SSE (`alt=sse`, yang dipakai Gemini CLI).
- Codex harus memakai HTTP biasa (bukan WebSocket) ke provider ini; itu default untuk provider kustom.
- Label sumber thread di dashboard/Telegram untuk semua klien ini masih "Claude Code".

## Catatan

- Selama diarahkan ke AIT, sesi Claude Code itu **tidak** memakai akun Anthropic Anda; semua model berasal dari
  9router.
- Usage yang dikembalikan untuk jawaban orkestrasi adalah estimasi ukuran konteks, dilaporkan sebagai
  `cache_read_input_tokens` (`input_tokens` = 0). Claude Code tetap menghitung sisa konteks dengan benar, sementara
  pelacak biaya yang membaca transcript (burn rate, ccusage, dll.) tidak menghitungnya sebagai input Opus penuh.
- Environment variable sistem dengan nama yang sama bisa bertabrakan dengan setting ini. Jika hasil tidak sesuai,
  periksa `ANTHROPIC_BASE_URL` di environment Windows Anda.
- Mematikan *Orkestrasi aktif* di Setting tidak perlu mengubah file ini: Claude Code tetap lewat AIT, tetapi
  diteruskan langsung ke 9router sebagai agen biasa.
