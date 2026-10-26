# aithread

**AIT** adalah proxy lokal untuk coding CLI (Claude Code, Codex CLI, Gemini CLI, opencode, Qwen Code, oh-my-pi) yang
memecah satu permintaan menjadi tugas-tugas kecil. **Planner** (model paling cerdas) membaca kode dan menyusun rencana.
Banyak **executor** (model murah) mengerjakannya secara paralel. Setelah itu AIT membuild project dan **reviewer**
(opsional) memeriksa hasilnya sampai lolos, lalu **wrapper** merangkum hasilnya untuk Anda. Semua model diakses lewat
[9router](https://github.com/decolua/9router).

Paket ini berisi `AIThread.exe` standalone, jadi .NET runtime tidak perlu dipasang. Hanya untuk **Windows x64**.

## Yang baru di 1.8.0

- **AIT kini dua bahasa (Indonesia / English)**: pilih di kotak bahasa di bawah sidebar. Halaman web, menu
  `AIThread.exe` dan ikon tray, log aktivitas, pesan validasi & respons API, pesan Telegram, file skenario Excel, dan
  menu Dokumentasi (`docs/en/`) ikut berganti. Nama menu di sisi Indonesia kini Dasbor, Proyek, Memori, Penyiapan,
  Pengaturan, dan Dokumentasi.
- **Output AIT di CLI mengikuti bahasa prompt**, apa pun bahasa tampilannya: prompt berbahasa Indonesia dijawab dalam
  Bahasa Indonesia dan sebaliknya (kode, log, dan tag yang ditempel tidak dihitung). Perintah chat dikenali dalam dua
  bahasa: `ulangi` / `retry`, `jalankan test lanjutan 2` / `run follow-up test 2`, `password dev <nilai>` /
  `dev password <nilai>`, dan seterusnya.
- **Project memory bergaya MEMORY.md**: satu catatan perubahan per thread (fitur, file yang berubah, validasi & flow,
  catatan) dilipat menjadi **ringkasan modul** yang menggambarkan kondisi modul saat ini. Planner membaca utuh ringkasan
  dan catatan terbaru modul yang dibahas, dan bisa mencari catatan lama lewat `search_memory`.
- **Migrasi memory lama otomatis**: fakta lama dibackup sekali, dilipat ke ringkasan modul, lalu dihapus. Progresnya
  tampil sebagai banner di menu Memori dan tetap berlanjut setelah AIT di-restart. Ringkasan yang terlanjur berbahasa
  lain ditulis ulang.
- Deskripsi memory bisa dipratinjau sebagai Markdown; tab wiki bisnis kini bernama **Business memory**.

## Yang baru di 1.7.10

- **Tester tidak lagi menebak URL API**: path endpoint dibaca dari atribut route di kode ASP.NET (Core MVC/Web API dan
  Web API 2) saat testing dimulai: `[controller]` = nama class tanpa `Controller` (`OutletUnionApiController` →
  `OutletUnionApi`), `[action]`, `[area]`, `{version:apiVersion}`, route base class. Tester mendapat daftar endpoint
  yang relevan, dan target skenario yang salah eja dikoreksi otomatis.
- **Setelah 404** Tester diberi route terdekat dari kode (atau tanda bahwa aplikasi belum memuat perubahan), dan halaman
  Swagger UI diberi daftar dokumen OpenAPI-nya (mis. `/swagger/OutboundService/swagger.json`), jadi tidak berakhir
  *blocked* karena menebak path.
- **Fitur 1.7.7 kini ada bersama perbaikan 1.7.8 dan 1.7.9**: akun uji per project, Tester berganti user
  (`switch_user`), deteksi *login as*, test lanjutan yang disarankan, dan skenario multi-langkah dengan Excel ikut di
  versi yang sama dengan `ulangi` dari chat, penanganan jawaban kosong, dan penyamaran secret. Versi 1.7.8 dan 1.7.9 di
  npm belum memuat fitur 1.7.7.

## Yang baru di 1.7.9

- **Ulangi tugas yang gagal dari chat**: bila provider gangguan (mis. `upstream_503`), balas `ulangi` (juga `coba lagi`,
  `retry`). AIT menjalankan lagi tugas yang belum berhasil dari thread terakhir di chat itu dengan rencana yang sama,
  tanpa memanggil planner, dan jawaban yang masih punya tugas gagal menuliskan petunjuknya. Pesan yang diketik saat
  giliran sebelumnya masih berjalan kini dibaca sebagai teks aslinya.
- **Jawaban kosong dari model = error provider**: model yang menjawab tanpa teks dan tanpa tool call dilaporkan dengan
  jelas dan langsung beralih ke node cadangan, bukan berakhir sebagai `No valid SEARCH/REPLACE block`. *Test* di menu
  Node juga menandainya gagal.
- **Planner lebih cepat**: tools node mendapat peta folder project, pertanyaan investigasi dikirim paralel per area
  dengan temuan yang lebih ringkas, dan progres menampilkan waktu tiap giliran model serta pembagian waktu planner
  (model, tool, antre).
- Dokumentasi integrasi kini berlaku untuk semua CLI (Claude Code, Codex CLI, Gemini CLI, opencode, Qwen Code, oh-my-pi).

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

## Yang baru di 1.7.7

- **Akun uji per project**: Project > Detail → *Akun uji* berisi akun yang dipakai Tester (username, peran, atribut bebas
  seperti NIK, unit, atasan), dibagi ke tim lewat project key. Tidak ada password per akun: Tester login dengan
  *password dev* project (bawaan `Test@1234`); bila ditolak, ia bertanya dan jawaban `password dev <nilai>` disimpan.
- **Tester berganti user** (`switch_user`): tiap akun punya profil browser dan cookie sendiri, jadi alur multi-user seperti
  submit lalu approve per level bisa dites dalam satu run. Planner menulis skenario dengan aktor per langkah.
- **Login as / impersonate dideteksi per project**: scan kode + model murah mencari fitur admin "login sebagai user
  lain", cara memakainya, dan peran yang boleh. Bisa dikoreksi dan dikonfirmasi di kartu *Akun uji*.
- **Test lanjutan yang disarankan**: setelah testing, Tester menyarankan langkah berikutnya (approve level 1, edit
  sebelum approve, reject). Jalankan dengan `jalankan test lanjutan <nomor>` atau tombol di halaman thread. Nilai seperti
  nomor dokumen diingat antar langkah (`{{requestNo}}`).
- **Skenario multi-langkah dengan Excel**: `buat skenario test lanjutan <nomor>` menulis file Excel di
  `.aithread/testing/` (akun per peran, langkah, kolom form) untuk diisi. `jalankan skenario <nama>` membacanya lalu
  menjalankan langkah demi langkah, dan berhenti di langkah yang gagal. Konfirmasi efek samping diminta sekali per
  project.
- Logo baru.

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
