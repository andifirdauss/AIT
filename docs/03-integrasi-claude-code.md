# 3. Integrasi Claude Code

Claude Code membaca variabel lingkungan dari blok `env` di `settings.json`. AiThread memakai tiga (plus satu
opsional) variabel:

| Variabel | Nilai | Fungsi |
|---|---|---|
| `ANTHROPIC_BASE_URL` | `http://localhost:5225` | Semua request Claude Code dikirim ke AiThread, bukan ke Anthropic. |
| `ANTHROPIC_AUTH_TOKEN` | Proxy API key, atau `aithread-local` bila kosong | Dikirim sebagai `Authorization: Bearer`. Kredensial Anthropic Anda tidak ikut terkirim ke proxy. |
| `API_TIMEOUT_MS` | `1800000` (30 menit) | Satu thread bisa memakan beberapa menit; default Claude Code (10 menit) bisa terlalu pendek. |
| `ANTHROPIC_CUSTOM_HEADERS` | `X-AiThread-Project: <folder root project>` | Hanya untuk cakupan per project. Mengikat sesi ke project yang tepat. |

## Cara A — dari UI (disarankan)

### Per project

Hanya sesi Claude Code yang dibuka di project tersebut yang melewati AiThread.

1. **Project** → *Telusuri…* → cari folder project (navigasi folder atau *Scan* sub-folder; folder project bertanda
   stack atau <kbd>git</kbd>) → centang → *Daftarkan terpilih*. Bisa juga menempel path folder di kolom lalu *Daftarkan*.
   Project juga terdaftar otomatis pada prompt pertama sesi Claude Code yang dibuka di folder project.
2. Paling cepat: di tabel **Project**, nyalakan switch kolom **AiThread** pada project tersebut (matikan untuk
   mengembalikan). Switch memakai URL proxy default (alamat http aplikasi).
3. Atau lewat menu **CLI** → kartu *Claude Code* → nyalakan switch project di daftar *Per project*. URL proxy yang
   ditulis diatur di kolom *URL proxy AiThread* di bagian atas halaman; *Yang akan ditulis* menampilkan pratinjaunya.
4. Buka sesi Claude Code baru di project itu agar berlaku.

File yang ditulis: `<folder project>/.claude/settings.local.json`, dengan folder project = folder root project
(tertera di kotak status). `settings.local.json` adalah setting lokal Claude Code dan biasanya di-*gitignore*.

> Claude Code membaca `.claude/settings.local.json` dari folder tempat Anda menjalankannya. Jika Anda biasa membuka
> Claude Code di sub-folder project atau di folder lain, gunakan [Cara B](#cara-b--manual) dan letakkan file di
> folder tersebut.

### Global

Semua sesi Claude Code di komputer ini melewati AiThread. Di menu **CLI**, nyalakan switch *Global* pada kartu Claude
Code (ada konfirmasi). File yang ditulis:
`%USERPROFILE%\.claude\settings.json`.

Hanya sesi yang dibuka di dalam sebuah project yang diorkestrasi, yaitu folder yang berisi `.git` atau file build
(`.sln`, `pom.xml`, `package.json`, `go.mod`, dst.; lihat [Cek sesi](07-cara-kerja.md#2-cek-sesi-project-atau-bukan)).
Sesi di folder lain (folder kosong, folder catatan, dll.) **diabaikan**: prompt-nya diteruskan langsung ke 9router
dengan model passthrough, sehingga Claude Code tetap bekerja seperti biasa di sana, tanpa memory.

### Status yang ditampilkan

| Status | Arti |
|---|---|
| ✓ Claude Code sudah diarahkan ke AiThread | `ANTHROPIC_BASE_URL` di file sama dengan URL proxy. |
| `ANTHROPIC_BASE_URL saat ini: … (bukan AiThread)` | File mengarah ke tempat lain (mis. langsung ke 9router). *Terapkan* akan menimpanya. |
| Belum diarahkan | Belum ada `ANTHROPIC_BASE_URL`. |

Sebelum perubahan pertama, file asli dibackup **sekali** sebagai `settings.json.aithread-backup` /
`settings.local.json.aithread-backup`. Kunci lain di file (permission, model, hooks, dll.) tidak diubah, dan
`ANTHROPIC_CUSTOM_HEADERS` milik Anda sendiri tidak ditimpa.

## Cara B — manual

Tambahkan ke `.claude/settings.local.json` di folder tempat Anda menjalankan `claude`
(atau ke `~/.claude/settings.json` untuk global):

```json
{
  "env": {
    "ANTHROPIC_BASE_URL": "http://localhost:5225",
    "ANTHROPIC_AUTH_TOKEN": "aithread-local",
    "API_TIMEOUT_MS": "1800000",
    "ANTHROPIC_CUSTOM_HEADERS": "X-AiThread-Project: D:\\Source\\MyApp"
  }
}
```

- Ganti `aithread-local` dengan Proxy API key bila Anda mengisinya di Setting.
- `ANTHROPIC_CUSTOM_HEADERS` opsional. Tanpa header ini, AiThread menentukan project dari working directory sesi
  (folder itu atau folder induknya). Alternatif lain: `X-AiThread-Cwd: <folder>` untuk menetapkan working directory.
  Header lama `X-AiThread-Solution` (path `.sln`) tetap diterima; foldernya dipakai sebagai petunjuk project.

## Verifikasi

1. **Tutup lalu buka sesi Claude Code baru** — env hanya dibaca saat sesi dimulai.
2. Ketik prompt sederhana, mis. "jelaskan struktur project ini".
3. Jawaban harus diawali baris seperti:

   ```
   **AiThread** · `MyApp` · thread `5e469b54`
   🧠 Planner `…` menganalisis project & menyusun rencana…
   ```

4. Di Dashboard AiThread muncul thread baru dan log `Thread baru dari Claude Code · MyApp`.

Jika jawaban datang tanpa baris `**AiThread**`, lihat [Troubleshooting](08-troubleshooting.md#claude-code-tidak-melewati-aithread).

## Mengembalikan

- UI: menu **CLI** → matikan switch yang sama (atau switch **AiThread** di tabel Project). Kunci AiThread
  (`ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, `API_TIMEOUT_MS`, dan header `X-AiThread-Project`, atau
  `X-AiThread-Solution` dari versi lama) dihapus;
  isi lain dibiarkan.
- Manual: hapus kunci-kunci tersebut, atau pulihkan dari file `*.aithread-backup`.

Buka sesi Claude Code baru setelahnya.

## Context Keeper untuk /clear

Sesi Claude Code yang panjang mahal karena **setiap request membaca ulang seluruh konteks**. `/clear` membuat konteks
kecil lagi, tetapi alurnya hilang. Hook ini menjembatani keduanya, dan berlaku untuk **semua** sesi di cakupannya —
termasuk sesi yang tidak diarahkan ke proxy AiThread:

1. Anda menjalankan `/clear`. Hook `SessionEnd` (reason `clear`) mengirim lokasi transcript sesi lama ke AiThread,
   yang langsung membalas lalu bekerja di latar.
2. AiThread meringkas transcript: prompt Anda, teks balasan, satu baris per tool call, error tool, dan ringkasan
   `/compact` (hasil tool, thinking, dan sub-agent dilewati; maks 60.000 karakter). Context Keeper (node Context
   Keeper, atau Wrapper) menulis ringkasan sesi dari situ, berikut ringkasan yang dipakai sesi lama itu sendiri.
3. Hook `SessionStart` (source `clear`) sesi baru di folder yang sama menunggu ringkasan (maks ±40 detik) dan
   menyisipkannya sebagai `additionalContext`. Claude melanjutkan dari ringkasan itu dengan konteks yang kecil.

`/clear` berikutnya membangun ringkasan dari ringkasan sebelumnya plus sesi terbaru, jadi alur tetap tersambung.

**Memasang**: **CLI → kartu Claude Code → Context Keeper untuk /clear** → pilih cakupan (Global disarankan agar
berlaku di semua project) → **Pasang hook**. Yang ditulis ke file settings (hook lain milik Anda tidak disentuh):

```json
{
  "hooks": {
    "SessionEnd": [
      { "matcher": "clear", "hooks": [ { "type": "http", "url": "http://localhost:5225/api/hooks/claude/session-end", "timeout": 5 } ] }
    ],
    "SessionStart": [
      { "matcher": "clear", "hooks": [ { "type": "http", "url": "http://localhost:5225/api/hooks/claude/session-start", "timeout": 60 } ] }
    ]
  }
}
```

Hook berlaku untuk sesi Claude Code yang dibuka setelah dipasang. **Lepas hook** menghapus entri AiThread saja.

Catatan:

- Setelah `/clear`, Claude Code menunggu ringkasan (biasanya beberapa detik, tergantung model Context Keeper). Bila
  AiThread mati atau ringkasan belum selesai dalam 40 detik, sesi baru tetap dimulai tanpa ringkasan.
- Sesi lama dan baru dicocokkan lewat working directory. Bila dua sesi di folder yang sama di-`/clear` pada saat
  bersamaan, ringkasan bisa tertukar.
- Isi transcript (versi ringkas) dikirim ke model Context Keeper lewat 9router. AiThread hanya mau membaca transcript
  di dalam `~/.claude/projects`.
- Ringkasan otomatis `/compact` bawaan Claude Code tidak diganti; hook ini hanya bekerja pada `/clear`.

## Klien lain: Codex, Gemini CLI, oh-my-pi, dll.

AiThread tidak terikat ke Claude Code. Ia menerima empat format request, dan 9router menerjemahkannya ke provider mana
pun (Claude, GPT, Gemini, …) — jadi model yang dipakai tidak harus sama dengan "bahasa" klien:

| Format | Endpoint AiThread | Klien contoh |
|---|---|---|
| Anthropic Messages | `POST /v1/messages` | Claude Code, oh-my-pi (`anthropic-messages`) |
| OpenAI Chat Completions | `POST /v1/chat/completions` | Cursor, Continue, opencode, oh-my-pi (`openai-completions`) |
| OpenAI Responses | `POST /v1/responses` | Codex CLI |
| Gemini native | `POST /v1beta/models/{model}:generateContent` dan `:streamGenerateContent` | Gemini CLI |

Aturan orkestrasi sama untuk semuanya: request diorkestrasi bila membawa `tools`, pesan terakhirnya dari user (bukan
hasil tool), dan sesinya berasal dari sebuah project (folder dengan `.git` atau file build); balasannya dikirim dalam format klien itu sendiri
(stream maupun non-stream). Selain itu request diteruskan ke endpoint 9router yang sama dengan model passthrough.

**Codex CLI** — `~/.codex/config.toml` (Codex hanya memakai `wire_api = "responses"`):

```toml
model_provider = "aithread"

[model_providers.aithread]
name = "AiThread"
base_url = "http://localhost:5225/v1"
env_key = "AITHREAD_API_KEY"        # isi dengan Proxy API key (atau nilai bebas bila kosong)
wire_api = "responses"
```

**Gemini CLI** — variabel lingkungan (HTTP diizinkan karena `localhost`):

```powershell
$env:GOOGLE_GEMINI_BASE_URL = "http://localhost:5225"
$env:GEMINI_API_KEY = "aithread-local"   # Proxy API key bila diisi
```

Nama model yang dikirim klien diabaikan pada passthrough dan tampil apa adanya pada balasan orkestrasi; model yang
sebenarnya dipakai ditentukan Setting *Passthrough model* dan Node.

**oh-my-pi (paling mudah: menu CLI)** — di menu **CLI**, nyalakan switch pada kartu *oh-my-pi (omp)*, global atau
per project. AiThread menulis satu blok di file `.env` yang dibaca omp (`<project>/.env` atau `~/.omp/agent/.env`),
tanpa menyentuh baris lain di file itu:

```dotenv
# >>> AiThread (dikelola otomatis, lepas lewat menu CLI) >>>
ANTHROPIC_BASE_URL=http://localhost:5225
ANTHROPIC_API_KEY=aithread-local
ANTHROPIC_CUSTOM_HEADERS=X-AiThread-Project: D:\Source\MyApp
# <<< AiThread <<<
```

Diuji dengan omp 18.4.4: omp memuat `.env` dari folder tempat ia dijalankan, mengirim request ke URL itu, dan sudah
menyertakan id sesi serta working directory-nya sendiri, jadi sesi langsung dikenali dan diorkestrasi. Yang perlu
diingat:

- Ini hanya berlaku untuk provider **anthropic** bawaan omp. Untuk provider lain, pakai `models.yml` di bawah.
- Jalankan omp dari folder yang berisi `.env` (biasanya root project); omp tidak mencari `.env` ke folder induk.
- Bila `.env` Anda sudah memuat `ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY`, atau `ANTHROPIC_CUSTOM_HEADERS` di luar
  blok AiThread, AiThread menolak menulis (agar tidak bertabrakan) dan menyebut kuncinya.
- Variabel yang sudah diekspor di shell selalu menang atas `.env`, dan login OAuth Anthropic di omp dipakai lebih dulu
  daripada `ANTHROPIC_API_KEY`.
- `.env` berisi kunci proxy lokal; jangan di-commit (tambahkan ke `.gitignore`).

**oh-my-pi lewat `models.yml`** — `~/.omp/agent/models.yml`; pilih `api: anthropic-messages` (baseUrl
`http://localhost:5225`) atau `api: openai-completions` (baseUrl `http://localhost:5225/v1`):

```yaml
providers:
  aithread:
    baseUrl: http://localhost:5225
    api: anthropic-messages
    apiKey: aithread-local          # Proxy API key bila diisi di Setting
    headers:
      X-AiThread-Cwd: D:\Source\MyApp   # opsional, lihat di bawah
    models:
      - id: <id model dari GET /v1/models>
        name: AiThread
```

**Membantu AiThread mengenali sesi.** Working directory dicari dari header `X-AiThread-Cwd`, lalu dari teks request:
`Primary working directory:` (Claude Code), `<cwd>…</cwd>` (Codex), `I'm currently working in the directory:`
(Gemini CLI), atau `Current working directory:` / `Working directory:` / `Current directory:` / `cwd:`. Bila klien Anda
tidak mengirim salah satunya, set header `X-AiThread-Cwd` (atau `X-AiThread-Project`). Id sesi dibaca dari header
`X-AiThread-Session`, `x-claude-code-session-id`, `session_id` (Codex) atau `x-session-id`, dari `prompt_cache_key`
(OpenAI), atau dari `metadata.user_id`; tanpa id sesi, keputusan project diingat per working directory.

**Batasan**
- Endpoint Gemini hanya `generateContent` dan `streamGenerateContent` (`countTokens` dll. tidak diproxy), dan stream
  Gemini selalu berformat SSE (`alt=sse`, yang dipakai Gemini CLI).
- Codex harus memakai HTTP biasa (bukan WebSocket) ke provider ini; itu default untuk provider kustom.
- Label sumber thread di dashboard/Telegram untuk semua klien ini masih "Claude Code".

## Catatan

- Selama diarahkan ke AiThread, sesi Claude Code itu **tidak** memakai akun Anthropic Anda; semua model berasal dari
  9router.
- Usage yang dikembalikan untuk jawaban orkestrasi adalah estimasi ukuran konteks, dilaporkan sebagai
  `cache_read_input_tokens` (`input_tokens` = 0). Claude Code tetap menghitung sisa konteks dengan benar, sementara
  pelacak biaya yang membaca transcript (burn rate, ccusage, dll.) tidak menghitungnya sebagai input Opus penuh.
- Environment variable sistem dengan nama yang sama bisa bertabrakan dengan setting ini. Jika hasil tidak sesuai,
  periksa `ANTHROPIC_BASE_URL` di environment Windows Anda.
- Mematikan *Orkestrasi aktif* di Setting tidak perlu mengubah file ini: Claude Code tetap lewat AiThread, tetapi
  diteruskan langsung ke 9router sebagai agen biasa.
