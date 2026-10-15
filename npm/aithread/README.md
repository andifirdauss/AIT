# aithread

**AIT** adalah proxy lokal untuk coding CLI (Claude Code, Codex CLI, Gemini CLI, opencode, Qwen Code, oh-my-pi) yang
memecah satu permintaan menjadi tugas-tugas kecil. **Planner** (model paling cerdas) membaca kode dan menyusun rencana.
Banyak **executor** (model murah) mengerjakannya secara paralel. Setelah itu AIT membuild project dan **reviewer**
(opsional) memeriksa hasilnya sampai lolos, lalu **wrapper** merangkum hasilnya untuk Anda. Semua model diakses lewat
[9router](https://github.com/decolua/9router).

Paket ini berisi `AiThread.exe` standalone, jadi .NET runtime tidak perlu dipasang. Hanya untuk **Windows x64**.

## Yang baru di 1.6.0

- **Tools Node**: role node baru. Tool baca planner (`read_file`, `search_text`, `find_references`, …) bisa
  didelegasikan ke model murah. Planner hanya menerima temuan ringkas, sehingga output mentah tidak ikut terbayar
  ulang di setiap langkah dan token planner jauh lebih hemat.
- **Sisa kuota** di footer jawaban: bar sisa kuota per akun provider (mis. jendela 5 jam / 7 hari Claude, kuota
  per model Antigravity) dibaca dari 9router, lengkap dengan waktu reset.
- **Build .NET Framework**: project format lama dibuild memakai MSBuild Visual Studio. Project .NET modern memakai
  `dotnet build --no-incremental`, dan build tetap jalan walau ada file output yang terkunci aplikasi yang sedang berjalan.
- **Dukungan CLI lain**: menu *CLI* bisa mengarahkan Codex CLI, Gemini CLI, opencode, dan Qwen Code ke AIT dengan
  satu switch, di samping Claude Code dan oh-my-pi. Provider asli tetap bisa dipakai.

## Instalasi

```powershell
npm i -g aithread
aithread            # menu interaktif
```

Untuk mencoba tanpa memasang: `npx aithread serve`. Untuk update ke versi terbaru: `npm i -g aithread@latest`.
Web UI juga menampilkan *Update tersedia* di sidebar bila ada versi baru.

### Prasyarat

- **SQL Server**. Defaultnya LocalDB `(localdb)\MSSQLLocalDB`, dan migrasi database diterapkan otomatis saat start.
- **9router**: `npm i -g 9router` lalu `9router` (dashboard di http://localhost:20128).

Redis/Docker tidak perlu dipasang, karena short memory memakai Garnet yang berjalan di dalam proses AIT.

## Mulai cepat

1. Jalankan `aithread tray --open`. Web UI terbuka di http://localhost:5225. Saat pertama dibuka, *Panduan memulai*
   menuntun langkah-langkah berikut.
2. **Setting**: isi URL dan API key 9router, klik *Test koneksi*, lalu *Simpan*.
3. **Node**: tambahkan minimal satu **Planner** (model terkuat dengan tool calling), satu **Executor** (model murah &
   cepat), dan satu **Wrapper** (model murah). Node **Reviewer**, **Tools Node**, dan **Context Keeper** opsional.
4. **CLI**: pilih kartu CLI Anda (Claude Code, Codex CLI, …), lalu nyalakan switch *Per project* atau *Global*.
   Backup config asli disimpan otomatis, dan tombol *Kembalikan* memulihkannya.
5. Buka sesi CLI baru di folder project lalu beri perintah seperti biasa. Progres tampil di CLI, dan detail plus rollback
   per thread ada di Dashboard.

## Perintah

| Perintah | Fungsi |
|---|---|
| `aithread` | Menu interaktif: Web UI, sembunyikan ke tray, log, restart, port, autostart |
| `aithread serve` | Jalankan server di jendela ini, log tampil di konsol (Ctrl+C untuk berhenti) |
| `aithread tray` | Jalankan di background dengan ikon tray, tanpa jendela |
| `aithread stop` | Matikan instance yang berjalan dengan rapi |
| `aithread status` | Status instance, port, log, dan autostart |
| `aithread logs [-f]` | Log terbaru; `-f` terus mengikuti log baru |
| `aithread open` | Buka Web UI di browser |
| `aithread autostart on\|off` | Jalankan tray otomatis saat login Windows |
| `aithread version` / `help` | Versi / bantuan |

Opsi: `--port <n>` (default 5225 atau port terakhir yang disimpan dari menu), dan `--open` untuk `serve`/`tray`
(membuka Web UI setelah siap).

## Fitur utama

- **Kontrak lintas file**: planner menetapkan sekali semua nama yang dipakai lintas file (id elemen, properti DTO,
  parameter API, kolom DB), sehingga executor bisa bekerja paralel tanpa saling bertabrakan.
- **Verifikasi otomatis**: AIT membuild project (`dotnet build`, MSBuild, `tsc`/`npm run build`, `go build`,
  `cargo check`, `mvn compile`) dan reviewer opsional memeriksa hasilnya. Error dijadikan tugas perbaikan hingga 2
  putaran.
- **Memory**: short memory (≤ 24 jam), long memory di database, dan **wiki bisnis** per modul yang dirangkum otomatis
  dari thread. Memory diikat ke *project key* (git remote) sehingga bisa dibagi antar developer.
- **Node cadangan**: node lain dengan role yang sama mengambil alih bila sebuah model kena rate limit.
- **Skill**: tarik repository skill (mis. `anthropics/skills`) ke gudang lokal, lalu tempel ke node lewat Skill
  Orchestrator.
- **Telegram**: kirim prompt dari HP lewat bot pribadi tanpa URL publik.

## Konfigurasi

Edit `%LOCALAPPDATA%\AiThread\appsettings.json`. File ini dibuat sendiri dan cukup berisi kunci yang ingin diubah.
Isinya menimpa `appsettings.json` bawaan paket dan tidak hilang saat `npm update`. Contoh:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=AiThread;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

Data dan log ada di `%LOCALAPPDATA%\AiThread`. Dokumentasi lengkap (instalasi, konfigurasi, integrasi CLI, memory,
cara kerja, troubleshooting, referensi API) bisa dibaca di menu **Docs** di Web UI.

## Catatan keamanan

- Executor **menulis langsung ke file project**, jadi jalankan AIT di repo git agar perubahan mudah ditinjau atau
  dibatalkan. Setiap thread juga bisa di-rollback dari Dashboard.
- Biarkan AIT hanya mendengarkan `localhost`, dan isi *Proxy API key* di Setting bila mesin diakses orang lain.
- API key 9router dan token bot Telegram disimpan terenkripsi.
