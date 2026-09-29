# 3. Integrasi Claude Code

Claude Code membaca variabel lingkungan dari blok `env` di `settings.json`. AiThread memakai tiga (plus satu
opsional) variabel:

| Variabel | Nilai | Fungsi |
|---|---|---|
| `ANTHROPIC_BASE_URL` | `http://localhost:5225` | Semua request Claude Code dikirim ke AiThread, bukan ke Anthropic. |
| `ANTHROPIC_AUTH_TOKEN` | Proxy API key, atau `aithread-local` bila kosong | Dikirim sebagai `Authorization: Bearer`. Kredensial Anthropic Anda tidak ikut terkirim ke proxy. |
| `API_TIMEOUT_MS` | `1800000` (30 menit) | Satu thread bisa memakan beberapa menit; default Claude Code (10 menit) bisa terlalu pendek. |
| `ANTHROPIC_CUSTOM_HEADERS` | `X-AiThread-Solution: <path .sln>` | Hanya untuk cakupan per project. Mengikat sesi ke solution yang tepat. |

## Cara A — dari UI (disarankan)

### Per project

Hanya sesi Claude Code yang dibuka di project tersebut yang melewati AiThread.

1. **Project** → *Telusuri…* → cari `.sln`/`.slnx` (navigasi folder atau *Scan* sub-folder) → centang →
   *Daftarkan terpilih*. Bisa juga menempel path di kolom lalu *Daftarkan*.
   Project juga terdaftar otomatis pada prompt pertama sesi Claude Code yang dibuka di folder solution.
2. Paling cepat: di tabel **Project**, nyalakan switch kolom **AiThread** pada project tersebut (matikan untuk
   mengembalikan). Switch memakai URL proxy default (alamat http aplikasi).
3. Atau lewat **Setting → Integrasi Claude Code** → pilih *Per project* → pilih project, periksa *URL proxy AiThread*
   dan pratinjau blok yang akan ditulis, lalu klik **Terapkan ke Claude Code**.
4. Buka sesi Claude Code baru di project itu agar berlaku.

File yang ditulis: `<folder project>/.claude/settings.local.json`, dengan folder project = folder yang berisi
`.sln`/`.slnx` (tertera di kotak status). `settings.local.json` adalah setting lokal Claude Code dan biasanya
di-*gitignore*.

> Claude Code membaca `.claude/settings.local.json` dari folder tempat Anda menjalankannya. Jika Anda biasa membuka
> Claude Code di folder lain (mis. root repo, sedangkan `.sln` ada di `src/`), gunakan [Cara B](#cara-b--manual)
> dan letakkan file di folder tersebut.

### Global

Semua sesi Claude Code di komputer ini melewati AiThread. Pilih *Global* lalu **Terapkan**. File yang ditulis:
`%USERPROFILE%\.claude\settings.json`.

Hanya sesi yang dibuka di dalam project `.sln`/`.slnx` yang diorkestrasi. Sesi di folder lain (project non-.NET,
folder catatan, dll.) **diabaikan**: prompt-nya diteruskan langsung ke 9router dengan model passthrough, sehingga
Claude Code tetap bekerja seperti biasa di sana.

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
    "ANTHROPIC_CUSTOM_HEADERS": "X-AiThread-Solution: D:\\Source\\MyApp\\MyApp.sln"
  }
}
```

- Ganti `aithread-local` dengan Proxy API key bila Anda mengisinya di Setting.
- `ANTHROPIC_CUSTOM_HEADERS` opsional. Tanpa header ini, AiThread mencari `.sln`/`.slnx` dari working directory
  sesi. Alternatif lain: `X-AiThread-Cwd: <folder>` untuk menetapkan working directory.

## Verifikasi

1. **Tutup lalu buka sesi Claude Code baru** — env hanya dibaca saat sesi dimulai.
2. Ketik prompt sederhana, mis. "jelaskan struktur solution ini".
3. Jawaban harus diawali baris seperti:

   ```
   **AiThread** · `MyApp.sln` · thread `5e469b54`
   🧠 Planner `…` menganalisis project & menyusun rencana…
   ```

4. Di Dashboard AiThread muncul thread baru dan log `Thread baru dari Claude Code · MyApp`.

Jika jawaban datang tanpa baris `**AiThread**`, lihat [Troubleshooting](08-troubleshooting.md#claude-code-tidak-melewati-aithread).

## Mengembalikan

- UI: **Setting → Integrasi Claude Code** → pilih cakupan yang sama → **Kembalikan**. Kunci AiThread
  (`ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, `API_TIMEOUT_MS`, dan header `X-AiThread-Solution`) dihapus;
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

**Memasang**: **Setting → Integrasi Claude Code → Context Keeper untuk /clear** → pilih cakupan (Global disarankan agar
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

## Catatan

- Selama diarahkan ke AiThread, sesi Claude Code itu **tidak** memakai akun Anthropic Anda; semua model berasal dari
  9router. Pengecualian: node Planner backend *Claude Code CLI* menjalankan `claude -p` dengan login CLI di mesin
  AiThread, sehingga memakai kuota akun itu. Transcript-nya tersimpan sebagai sesi terpisah di folder project yang sama.
- Usage yang dikembalikan untuk jawaban orkestrasi adalah estimasi ukuran konteks, dilaporkan sebagai
  `cache_read_input_tokens` (`input_tokens` = 0). Claude Code tetap menghitung sisa konteks dengan benar, sementara
  pelacak biaya yang membaca transcript (burn rate, ccusage, dll.) tidak menghitungnya sebagai input Opus penuh.
- Environment variable sistem dengan nama yang sama bisa bertabrakan dengan setting ini. Jika hasil tidak sesuai,
  periksa `ANTHROPIC_BASE_URL` di environment Windows Anda.
- Mematikan *Orkestrasi aktif* di Setting tidak perlu mengubah file ini: Claude Code tetap lewat AiThread, tetapi
  diteruskan langsung ke 9router sebagai agen biasa.
