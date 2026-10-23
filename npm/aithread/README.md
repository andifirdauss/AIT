# aithread

**AIT** adalah proxy lokal untuk coding CLI (Claude Code, Codex CLI, Gemini CLI, opencode, Qwen Code, oh-my-pi) yang
memecah satu permintaan menjadi tugas-tugas kecil. **Planner** (model paling cerdas) membaca kode dan menyusun rencana.
Banyak **executor** (model murah) mengerjakannya secara paralel. Setelah itu AIT membuild project dan **reviewer**
(opsional) memeriksa hasilnya sampai lolos, lalu **wrapper** merangkum hasilnya untuk Anda. Semua model diakses lewat
[9router](https://github.com/decolua/9router).

Paket ini berisi `AIThread.exe` standalone, jadi .NET runtime tidak perlu dipasang. Hanya untuk **Windows x64**.

## Yang baru di 1.7.8

- **Secret tidak lagi sampai ke model**: password, API key, token, JWT, private key, serta password di connection string
  dan URL diganti placeholder `{{AIT_SECRET:<label>:<id>}}` sebelum dikirim ke model, termasuk request passthrough CLI
  lain. Prompt, riwayat chat, log aktivitas, memory, wiki bisnis, dan file export memory juga disamarkan sebelum
  disimpan. Nilai aslinya hanya ada di memori AIT dan dipasang kembali saat executor menulis file, Tester memanggil
  aplikasi, atau Claude Code menjalankan Edit/Write/Bash (lewat hook).
- **File kredensial diblokir**: kunci, sertifikat, kunci SSH, file token, dan folder kredensial di profil Windows tidak
  dibaca atau diubah agen AIT. Aturan tambahan per project (`deny`, `allow`, `keys`) ada di bagian `"secrets"` pada
  `.aithread/project.json`.
- Setting → *Keamanan & privasi* untuk menyalakan/mematikan penyamaran beserta hitungannya, dan popup penjelasan saat AIT
  pertama kali dibuka.
- Perbaikan: error 9router `tool_use.input: Input should be an object` saat tool call menulis file yang berisi
  connection string (argumen tool call kini disamarkan per nilai, jadi JSON-nya tetap utuh).

## Yang baru di 1.7.6

- **Background job dan function dicek dengan unit test dry-run**: perubahan di scheduler, job Hangfire/Quartz, hosted
  service, atau kode yang efeknya hanya di sistem eksternal tidak lagi berakhir "testing dilewati, cek sendiri". Planner
  menulis unit test yang memanggil fungsinya langsung dengan database, API eksternal, dan jam dipalsukan, jadi tidak
  ada data nyata yang terkirim atau tertulis. Berlaku juga bila Anda hanya minta testing tanpa perubahan kode (kode
  yang diuji tidak diubah, hanya file test yang ditambah).
- **Project tanpa test project** dibuatkan test project-nya sekalian (xUnit untuk .NET, Vitest/runner yang ada untuk
  Node.js, pytest, PHPUnit, JUnit), tanpa bertanya dulu.
- **Test project untuk project .NET Framework lama** (format lama, `packages.config`) dibuild dengan MSBuild Visual
  Studio lalu dijalankan `vstest.console`, bukan `dotnet test` yang tidak bisa membuild project seperti itu. Restore
  memakai kredensial feed NuGet yang sama dengan build solusi.
- **Test baru tidak lagi "dilewati" karena database/jaringan**: test yang ditulis untuk perubahan itu harus dry-run,
  jadi bila menyentuh database ia diperbaiki. Restore/build yang memang tidak bisa jalan (feed NuGet 401, SDK tidak
  ada) tetap dilewati dengan alasannya.
- Auto Testing: `dotnet run` memakai launch profile yang port-nya cocok, warning build disaring dari pesan gagal start,
  dan output aplikasi tampil di blok tersendiri.

## Yang baru di 1.7.5

- **Auto Testing menjalankan aplikasinya sendiri**: bila aplikasi belum jalan di alamat yang dites, AIT menjalankannya
  untuk testing itu, menunggu sampai menjawab (maksimal 3 menit), lalu menghentikannya lagi beserta proses turunannya.
  Yang dikenali: project ASP.NET Core (`dotnet run --urls`), Laravel (`php artisan serve`), dan script
  `dev`/`start`/`serve` di `package.json`. Untuk stack lain, tulis perintahnya di `.aithread/project.json`, mis.
  `{ "run": "docker compose up web" }`. Aplikasi yang sudah Anda jalankan sendiri tetap dipakai dan tidak dihentikan.

## Yang baru di 1.7.4

- **Node Tester lebih stabil dalam membuat dan menjalankan unit test**: katalog unit test per project (scan, jalankan,
  karantina, setting) di Project > Detail. Baseline berjalan paralel dengan planner; regresi dinilai node Tester lalu
  diperbaiki otomatis (maksimal 3 putaran), dan test yang hilang dipulihkan sebelum planner.
- Katalog unit test ikut export/import memory (file `aithread-memory` v3).

## Yang baru di 1.7.3

- **Auto Testing dengan node Tester**: setelah build dan review lolos, node Tester mencoba perubahan di aplikasi yang
  sedang berjalan (request HTTP dan browser, hanya ke localhost). Hasil per skenario dan output API tampil di akhir
  jawaban dan di detail thread, dengan tombol *Ulangi testing*. Bila butuh login, balas `login <user> <password>` di
  chat; password tidak disimpan ke disk dan tidak dilihat model.
- **Planner bisa membaca riwayat git**: `git_status`, `git_diff`, `git_log`, `git_show`, dan `git_blame` (hanya baca),
  jadi permintaan seperti "review perubahan saya" tidak perlu menempel diff.
- **Planner mengingat analisa sebelumnya**: file dan baris yang dibaca, hasil pencarian, dan temuan sub agent disimpan
  per thread. Pertanyaan lanjutan di chat yang sama dijawab dari analisa itu tanpa mencari ulang dari awal.
- Logo baru.

## Yang baru di 1.7.2

- **Sisa kuota kembali tampil** di footer jawaban saat *Require login* 9router aktif (bawaan 9router). Untuk 9router di
  komputer yang sama, AIT kini membaca API dashboard dengan token CLI lokal (`x-9r-cli-token`), cara yang juga dipakai
  CLI 9router sendiri, jadi setting 9router tidak perlu diubah. Token tidak pernah dikirim ke 9router di komputer
  lain; untuk itu matikan *Require login* di sana.

## Yang baru di 1.7.1

- **Sidebar ditata ulang**: menu dikelompokkan menjadi *Ruang kerja* (Dashboard, Project, Memory), *Konfigurasi*
  (Setup, Node, CLI, Setting), dan *Bantuan* (Docs, Panduan memulai), dengan ikon garis yang seragam.
- **Menu CLI lebih mudah dibaca**: setiap kartu memakai logo dan warna tool-nya (Claude Code, oh-my-pi, Codex, Gemini
  CLI, opencode, Qwen Code).
- **Log konsol lebih ringkas**: satu baris per entri, warning tanpa stack trace, dan warning yang sama dalam 5 menit
  hanya dicetak sekali. Error tetap dengan stack trace lengkap.
- Kartu *Jalankan prompt* di Dashboard disembunyikan; thread tanpa Claude Code tetap bisa dijalankan lewat
  `POST /api/threads`.

## Yang baru di 1.7.0

- **Struktur kode project**: menu Project → detail kini punya jendela *Struktur kode* yang menampilkan isi project
  tanpa compile. Tampilan **Folder** (folder → file → class → method/property) atau **Class** (namespace → class →
  member; class `partial` C# digabung jadi satu). Klik deklarasi untuk menyalin `path:baris` yang bisa langsung
  ditempel ke prompt, dan pencarian menemukan class, member, dan file.
- **Banyak bahasa, tetap cepat**: C# dibaca dengan Roslyn; PHP, JavaScript/TypeScript, Python, dan Java/Kotlin dibaca
  per baris dengan pola. Library pihak ketiga (`thirdpartylibs.xml`, file minify, folder build) dilewati. Scan ulang
  hanya membaca file yang berubah, dan berjalan otomatis di belakang setiap kali thread mengubah file.
- **Panggilan function**: AIT mencatat apa saja yang dipanggil sebuah function, mis. `p_InitForm` →
  `p_initiateData` → `POST /Transaction/KlaimPending/InitiateData` → `KlaimPendingController.InitiateData`, sehingga
  alur fitur bisa diikuti dari layar sampai database. Yang dibaca hanya kode yang disentuh planner (`read_file`) atau
  diubah thread, oleh node **Tools Node** di belakang. Function yang isinya tidak berubah tidak dibaca ulang. Lokasi
  deklarasi dicari AIT sendiri dari Struktur kode, bukan ditebak model.
- **Planner melihat kerangka dulu, baru membaca**: file yang disebut di prompt langsung di-outline di brief planner
  (class, method, rentang baris). Tool baru `code_outline` memberi kerangka file, folder, atau class mana pun, dan
  `function_calls` memberi panggilan serta pemanggil sebuah function. Hasilnya, planner hanya membaca rentang baris
  yang dibutuhkan, tidak salah memilih file, dan token planner lebih hemat.
- **Tabel Project → detail lebih ringkas**: thread, sesi, modul, peta kode, dan riwayat gabung modul tampil 10 baris
  per halaman, dan tabel modul kini selebar halaman.

Sebelumnya (1.6.3): **label memory berstruktur** `Modul · Layer · Jenis` + keyword, **peta kode** file → modul,
**modul ganda digabung otomatis**, label ulang memory lama, **Export/Import memory**, dan 9router yang ikut terpasang
serta dinyalakan otomatis. (1.6.0): role **Tools Node**, **Sisa kuota** provider di footer jawaban, build **.NET
Framework** lewat MSBuild, serta dukungan **Codex CLI, Gemini CLI, opencode, dan Qwen Code**.

## Instalasi

```powershell
npm i -g aithread
aithread            # menu interaktif
```

Untuk mencoba tanpa memasang: `npx aithread serve`. Untuk update ke versi terbaru: `npm i -g aithread@latest`.
Web UI juga menampilkan *Update tersedia* di sidebar bila ada versi baru.

### Prasyarat

- **SQL Server**. Defaultnya LocalDB `(localdb)\MSSQLLocalDB`, dan migrasi database diterapkan otomatis saat start.
- **9router**: ikut terpasang bersama paket ini dan dinyalakan AIT saat start (dashboard di http://localhost:20128).
  Hubungkan minimal satu provider di dashboard itu. Bila ingin versi terbaru, pasang juga `npm i -g 9router`; AIT
  memakai yang global lebih dulu.

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
  dari thread. Setiap memory berlabel `Modul · Layer · Jenis` + keyword, dengan modul dari peta kode project. Memory
  diikat ke *project key* (git remote) sehingga bisa dibagi antar developer.
- **Struktur kode & panggilan function**: peta class/method project dan rantai panggilannya, tanpa compile, di menu
  Project. Planner memakainya untuk melihat kerangka kode sebelum membaca file.
- **Node cadangan**: node lain dengan role yang sama mengambil alih bila sebuah model kena rate limit.
- **Skill**: tarik repository skill (mis. `anthropics/skills`) ke gudang lokal, lalu tempel ke node lewat Skill
  Orchestrator.
- **Telegram**: kirim prompt dari HP lewat bot pribadi tanpa URL publik.

## Konfigurasi

Edit `%LOCALAPPDATA%\AIThread\appsettings.json`. File ini dibuat sendiri dan cukup berisi kunci yang ingin diubah.
Isinya menimpa `appsettings.json` bawaan paket dan tidak hilang saat `npm update`. Contoh:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=AIThread;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

Data dan log ada di `%LOCALAPPDATA%\AIThread`. Dokumentasi lengkap (instalasi, konfigurasi, integrasi CLI, memory,
cara kerja, troubleshooting, referensi API) bisa dibaca di menu **Docs** di Web UI.

## Catatan keamanan

- Executor **menulis langsung ke file project**, jadi jalankan AIT di repo git agar perubahan mudah ditinjau atau
  dibatalkan. Setiap thread juga bisa di-rollback dari Dashboard.
- Biarkan AIT hanya mendengarkan `localhost`, dan isi *Proxy API key* di Setting bila mesin diakses orang lain.
- API key 9router dan token bot Telegram disimpan terenkripsi.
