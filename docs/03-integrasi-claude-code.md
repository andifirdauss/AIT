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

## Catatan

- Selama diarahkan ke AiThread, Claude Code **tidak** memakai akun Anthropic Anda; semua model berasal dari 9router.
- Environment variable sistem dengan nama yang sama bisa bertabrakan dengan setting ini. Jika hasil tidak sesuai,
  periksa `ANTHROPIC_BASE_URL` di environment Windows Anda.
- Mematikan *Orkestrasi aktif* di Setting tidak perlu mengubah file ini: Claude Code tetap lewat AiThread, tetapi
  diteruskan langsung ke 9router sebagai agen biasa.
