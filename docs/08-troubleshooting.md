# 8. Troubleshooting

Langkah pertama untuk hampir semua masalah: buka **Dashboard → Log langsung** (pilih *Semua (debug)*) atau
halaman detail thread → *Aktivitas*.

## 9router

**Kartu 9router *Offline*: "No connection could be made…" / "Timeout (5 detik)"**
9router tidak berjalan atau URL salah. Klik **Jalankan 9router** di Dashboard (atau jalankan `9router`), pastikan
dashboard terbuka di URL yang sama dengan Setting → Base URL, lalu *Test koneksi*.

**AIT tidak menyalakan 9router saat start**
Lihat log kategori *9router* di Dashboard. Penyebab umum: Base URL bukan alamat lokal; *Node.js tidak ditemukan*
(pasang Node.js 18+); *9router belum terpasang* (klik **Jalankan 9router**, atau set `AIThread:NineRouter:AutoInstall`);
*Port … sudah dipakai* oleh program lain (ganti port 9router dan Base URL, atau hentikan program itu); atau 9router
berhenti saat dinyalakan, yang detailnya ada di `%LOCALAPPDATA%\AIThread\logs\9router.log`.

**"9router HTTP 401 untuk model '…': Missing API key" padahal Setting terlihat terhubung**
Belum ada API key **tersimpan**. *Test koneksi* hanya menguji key yang diketik di form tanpa menyimpannya, dan
status *Online* hanya membuktikan 9router hidup (health check dan daftar model tidak butuh key). Isi API key di
Setting → klik **Simpan** → pastikan baris status menampilkan `API key OK`. Kartu 9router di Dashboard menampilkan
*Online · cek API key* selama key belum beres.

**"API key ditolak oleh 9router" / "Invalid API key"**
Key salah atau sudah dihapus di 9router. Salin ulang dari dashboard 9router, isi di Setting, klik **Simpan**.

**Key 9router "hilang" setelah pindah komputer/akun Windows**
Key dienkripsi dengan Data Protection milik akun Windows tersebut. Isi ulang key di Setting.

**"Akun provider untuk model '…' di 9router perlu login ulang (OAuth access token has expired …)"**
9router hidup dan API key AIT diterima, tetapi akun provider di balik model itu (mis. langganan Claude) sudah logout:
token OAuth kedaluwarsa atau key provider dicabut. 9router meneruskannya sebagai HTTP 503 berisi `authentication_error`
/ `[401]`. AIT tidak mencoba ulang (menunggu tidak membantu) dan langsung beralih ke
[node cadangan](02-konfigurasi.md#node-cadangan) bila ada. Buka dashboard 9router → Providers, sambungkan ulang akun
tersebut, lalu **Ulangi yang gagal** di detail thread.

**"9router HTTP 4xx/5xx untuk model '…'"**
Model tidak tersedia atau provider-nya bermasalah. Klik *Test* pada node terkait; pilih model/combo lain bila perlu.
Error sementara (429, 5xx, koneksi) sudah dicoba ulang otomatis 2 kali sebelum dilaporkan. Untuk 429, header
`Retry-After` dari 9router dihormati (maks. 30 detik; bila lebih lama tidak ditunggu), tanpa header jedanya ±5 dan ±10
detik. Bila tugas executor tetap gagal (mis. `upstream_503` *Model sedang gangguan*), tunggu sebentar lalu balas
`ulangi` di chat (atau **Ulangi yang gagal** di detail thread): hanya tugas yang gagal dijalankan lagi, tanpa planner.
Supaya tidak perlu diulang manual, pasang [node cadangan](02-konfigurasi.md#node-cadangan) untuk executor.

**Planner kena 429 / rate limit**
Biasanya karena akun provider (mis. langganan Claude di 9router) kehabisan kuota per menit — tiap langkah planner
mengirim ulang seluruh percakapan, jadi token input cepat menumpuk. Sebagian besar dilayani dari prompt cache (lihat
[Biaya token planner](02-konfigurasi.md#biaya-token-planner)), tetapi batas kuota per menit provider bisa tetap
menghitung semua token input: satu thread planner `cc/claude-opus-…` dengan 10–20 langkah bisa memakai 0,2–1 juta
token. Kolom token planner di detail thread menunjukkan pemakaian sebenarnya beserta porsi cache-nya. Solusi:
- Tambahkan node Planner **kedua** (model/provider lain; boleh nonaktif) dan pilih sebagai *Node cadangan* di form
  planner utama. AIT beralih ke node itu dan melanjutkan investigasi; baris `⚠️ Planner … beralih ke …` muncul di
  progres. Cara yang sama berlaku untuk executor, reviewer, dan wrapper (lihat
  [Node cadangan](02-konfigurasi.md#node-cadangan)).
- Thread yang sudah terlanjur gagal tidak perlu diulang dari awal: balas `ulangi` di chat, atau buka detail thread →
  **Ulangi yang gagal**.
- Atau jadikan model planner sebuah **combo** 9router (mis. Claude → model lain) supaya fallback terjadi di 9router.
- Kurangi slot node planner bila beberapa thread berjalan bersamaan, atau turunkan Setting → *Langkah planner*.

**Footer tidak menampilkan *Sisa kuota***
- 9router di komputer lain dengan *Require login* aktif (bawaan 9router): API `/api/usage` menolak AIT, dan log AIT
  menulis *Kuota provider tidak bisa dibaca* sekali. Matikan *Require login* di 9router tersebut. 9router di komputer
  yang sama tidak perlu diubah karena AIT masuk dengan token CLI lokal. Bila log menyebut *menolak token CLI lokal*,
  restart 9router (atau matikan *Require login*).
- Model node berasal dari akun API key biasa (mis. provider *compatible*) atau provider yang kuotanya tidak bisa dibaca
  9router — bagian ini memang hanya untuk langganan/OAuth yang ditampilkan 9router di halaman *Usage*-nya.
- 9router tidak menjawab dalam 10 detik; footer dikirim tanpa kuota. Thread berikutnya mencoba lagi.

**"Model '…' tidak merespons dalam N detik"**
Naikkan Setting → *Timeout (detik)*, atau pakai model yang lebih cepat.

## CLI tidak melewati AIT

Jawaban muncul tanpa baris `**AIT** · …` dan tidak ada thread baru di Dashboard:

1. Buka **sesi CLI baru** — konfigurasi hanya dibaca saat sesi dimulai.
2. Menu CLI → kartu CLI Anda: switch harus menyala dengan lencana *Aktif* (bukan *Mengarah ke alamat lain*).
3. Pastikan sesi memakai provider/model AIT: Codex dijalankan dengan `codex --profile aithread`; di oh-my-pi,
   opencode, dan Qwen Code model AIT dipilih lewat `/model`/`/models`; Gemini CLI memakai *Use Gemini API Key* di
   `/auth`. Lihat [Integrasi CLI](03-integrasi-cli.md).
4. Untuk cakupan per project, pastikan CLI dijalankan di folder project itu. Claude Code membaca
   `.claude/settings.local.json` dari folder tempat ia dijalankan (lihat path di kotak status); jika berbeda, salin
   blok `env` ke folder tempat Anda menjalankan `claude`. Gemini CLI hanya membaca satu `.env` — `.env` milik project
   bisa menutupi switch global.
5. Pastikan tidak ada environment variable Windows yang menimpa (mis. `ANTHROPIC_BASE_URL` untuk Claude Code,
   `GOOGLE_GEMINI_BASE_URL`/`GEMINI_API_KEY` untuk Gemini CLI).
6. Setting → *Orkestrasi aktif* harus menyala. Jika mati, request tetap lewat AIT tetapi diteruskan tanpa orkestrasi
   (terlihat sebagai log debug `Passthrough (orchestration disabled)`).
7. Cari di log baris `Sesi xxxxxxxx (…) bukan bagian project (…)`. Artinya sesi **sengaja diabaikan** karena
   working directory-nya bukan bagian sebuah project — lihat [Project](#project).

Log debug `Passthrough (auxiliary request without tools)` untuk request judul sesi/compact adalah hal normal.

## Error yang tampil di CLI

| Pesan | Penyebab | Solusi |
|---|---|---|
| `authentication_error: Invalid AIT proxy key` | Key yang dikirim CLI (mis. `ANTHROPIC_AUTH_TOKEN` di Claude Code) berbeda dengan Proxy API key. | Terapkan ulang integrasi setelah mengganti key, lalu buka sesi baru. |
| `AIT: passthrough model belum diatur…` (503) | Tidak ada model passthrough dan tidak ada node Wrapper/Executor aktif. | Isi *Model passthrough* atau aktifkan node. |
| `❌ AIT gagal: Belum ada node Planner aktif…` | Pipeline belum lengkap. | Tambahkan/aktifkan node di menu Node. |
| `❌ AIT gagal: Planner tidak menghasilkan rencana yang valid dalam N langkah.` | Planner kehabisan langkah atau terus mengirim rencana yang ditolak validator. | Lihat alasan penolakan di *Aktivitas* thread. Naikkan *Langkah planner*, pakai model planner yang lebih kuat, atau perjelas prompt. |
| `❌ AIT gagal memulai thread: …` | Masalah sebelum thread dibuat, biasanya database atau project dihapus saat sesi berjalan. | Periksa kartu Database di Dashboard; daftarkan ulang project bila perlu. |
| CLI timeout | Thread lebih lama dari batas waktu CLI. | Naikkan batasnya (Claude Code: `API_TIMEOUT_MS`; Codex: `stream_idle_timeout_ms`; Qwen Code: `QWEN_STREAM_MAX_LIFETIME_MS`), atau pecah prompt. |

## Project

**Prompt tidak diorkestrasi: log `Sesi … bukan bagian project (…)`**
Sesi dianggap di luar project, sehingga prompt diteruskan langsung dan tidak ada memory yang dibuat. Alasan di dalam
kurung menunjukkan penyebabnya:

- *tidak ada marker project*: tidak ada `.git` atau file build (`.sln`, `pom.xml`, `package.json`, `go.mod`, dst.) di
  working directory maupun folder induknya (hingga 6 tingkat). Ini wajar untuk folder kosong atau folder catatan;
- *folder ini berisi project a, b, …*: working directory hanya berisi beberapa project, tidak ada yang dipilih karena
  ambigu. Buka sesi di salah satunya;
- *folder ini terlalu luas*: working directory adalah drive, folder Home, atau folder sistem;
- *working directory tidak diketahui*: klien tidak mengirimnya; set header `X-AIThread-Cwd`.

Solusi: jalankan CLI di folder project, tambahkan `.git` (`git init`) atau file build ke folder itu, atau
daftarkan foldernya lewat *Daftarkan manual* di menu Project (folder tanpa marker pun bisa). Bisa juga menerapkan
integrasi *per project* (mengirim header `X-AIThread-Project`). Sesi yang diabaikan dicek ulang setelah sekitar 5 detik.

**Registrasi manual: "Folder … tidak ditemukan" atau "Folder ini terlalu luas untuk jadi project"**
Path salah, atau folder yang dituju adalah drive/Home/folder sistem. Isi path folder project-nya, atau gunakan
*Telusuri…* → *Scan* untuk mencarinya.

**Scan: "berhenti lebih awal (batas waktu/jumlah)"**
Scan dibatasi 300 hasil, 50.000 folder, dan 20 detik. Masuk ke folder yang lebih spesifik atau kurangi kedalaman.

**Folder tidak terlihat di penelusur**
Folder tersembunyi/sistem, folder yang diawali `.` atau `$`, folder build (`bin`, `obj`, `node_modules`, …), dan
junction/symlink sengaja disembunyikan. Ketik path lengkapnya di kolom path lalu Enter.

**Project salah terdeteksi**
Root dipilih dari root repository (`.git`) terdekat, lalu file penghimpun paling luar, lalu manifest terdekat (lihat
[Cek sesi](07-cara-kerja.md#2-cek-sesi-project-atau-bukan)); project yang sudah terdaftar dan memuat folder itu
diutamakan. Bila root yang Anda mau berbeda (mis. satu modul di monorepo sebagai project sendiri), daftarkan folder
itu lewat *Daftarkan manual*: folder dengan marker sendiri didaftarkan apa adanya, dan sesi di dalamnya memakainya.
Integrasi per project (header `X-AIThread-Project`) juga mengunci foldernya.

**Badge *folder hilang* di menu Project**
Folder project sudah dipindah/dihapus. Daftarkan path baru; hapus entri lama bila tidak diperlukan
(ingat: memory project ikut terhapus).

**Project lama muncul ganda atau menyatu setelah pembaruan**
Identitas project berpindah dari file `.sln` ke folder root. Migrasi otomatis menyatukan dua solution yang berada di
folder yang sama menjadi satu project (yang terakhir dipakai), lengkap dengan thread dan memory-nya.

## Tugas executor

| Pesan di tugas | Arti | Solusi |
|---|---|---|
| `the SEARCH text was not found…` | Executor tidak menyalin teks file dengan persis, bahkan setelah retry. | Naikkan *Retry executor*, pakai model executor yang lebih patuh, atau naikkan `ExecutorFullFileMaxLines` bila file besar. |
| `the SEARCH text occurs N times…` / `match several places` | Potongan SEARCH terlalu pendek sehingga ambigu. | Biasanya selesai di retry; bila tidak, sama seperti di atas. |
| `Output was cut off (max tokens)…` | Output melebihi *Max tokens* node. | Naikkan *Max tokens* executor (terutama untuk tugas `create`). |
| `No valid SEARCH/REPLACE block in the output.` | Model menjawab di luar format. | Ganti model executor; tambahkan instruksi di *Instruksi tambahan* node. |
| `Model '…' mengembalikan jawaban kosong lewat 9router` | Provider di balik model itu menjawab tanpa teks dan tanpa tool call (output masuk ke *reasoning*, akun bermasalah, atau max tokens habis untuk reasoning). Tidak dicoba ulang otomatis karena biasanya berulang; *Test* di menu Node juga gagal dengan pesan yang sama. | Pilih model lain atau pasang [node cadangan](02-konfigurasi.md#node-cadangan) (langsung dipakai), cek provider-nya di 9router, lalu `ulangi`. |
| `Dependency tidak berhasil: T3` | Tugas dilewati karena tugas yang ditunggu gagal. | Perbaiki tugas yang gagal, lalu ulangi bagian ini dengan prompt baru. |

Tugas yang gagal tidak menulis apa pun ke file (edit bersifat all-or-nothing).

## Build & verifikasi

| Gejala | Arti | Solusi |
|---|---|---|
| `build tidak bisa dijalankan — 'dotnet' could not be started…` | Toolchain project (`dotnet`, `node`/`npm`, `go`, `cargo`, `mvn`) tidak ada di PATH proses AIT. | Pasang toolchain-nya atau tambahkan ke PATH, lalu restart AIT. Exe standalone tidak butuh .NET SDK untuk berjalan, tetapi membuild project .NET tetap membutuhkannya. |
| `kompilasi berhasil, tetapi file output terkunci…` | `MSB3021`/`MSB3027`: aplikasi yang sedang berjalan (mis. project yang Anda debug) mengunci file di `bin/`. Kode sudah dikompilasi, jadi verifikasi dianggap lolos. | Tidak perlu tindakan. Hentikan aplikasinya bila ingin output terbaru tersalin. |
| `build melewati batas waktu…` | Build lebih lama dari *Timeout build*; build dihentikan dan tidak diulang di putaran berikutnya. | Naikkan *Timeout build*. Build pertama (restore paket) biasanya paling lama. |
| Build lambat di setiap putaran | Restore menunggu feed NuGet yang tidak terjangkau. | AIT sudah mematikan audit kerentanan (`NuGetAudit=false`); periksa `nuget.config` bila restore masih menunggu feed. |
| `… — in a file this thread did not change` | Error di file yang tidak diubah thread, mungkin sudah ada sebelumnya; tidak diperbaiki otomatis. | Minta secara eksplisit: "build lalu perbaiki semua error". Planner akan memanggil `run_build` dan merencanakan perbaikannya. |
| `The build failed without a recognisable error message` | Output build tidak memuat format error yang dikenali (sering pada script `npm run build` khusus). | Lihat output di kartu *Verifikasi* detail thread. |
| Verifikasi *Masih ada masalah* setelah semua putaran | Executor tidak berhasil memperbaiki dalam *Putaran perbaikan*. | Lanjutkan dengan prompt berikutnya (planner melihat thread sebelumnya), naikkan putaran perbaikan, atau pakai model executor yang lebih kuat. |
| Reviewer terlalu cerewet atau memicu perubahan yang tidak diminta | Model reviewer melaporkan hal di luar permintaan. | Tambahkan batasan di *Instruksi tambahan* node Reviewer, ganti modelnya, atau nonaktifkan node itu (verifikasi tetap memakai build). |
| Verifikasi tidak muncul | Tidak ada tugas yang berhasil, *Build & verifikasi otomatis* mati dan tidak ada node Reviewer, atau stack project tidak punya perintah build. | Lihat [Verifikasi](07-cara-kerja.md#5-verifikasi-build-dan-reviewer). |

## Unit test

| Gejala | Arti | Solusi |
|---|---|---|
| Unit test tidak pernah jalan / kartu Unit test kosong | Project belum punya project key, mode katalog *Mati*, atau tidak ada suite yang dikenali (lihat [Runner yang dikenali](07-cara-kerja.md#runner-yang-dikenali)). Store long memory yang tidak menjawab dalam 10 detik juga membuat unit test dilewati thread itu (tercatat di log aplikasi). | Cek kartu Unit test di Project > Detail; tekan **Scan test**. Pastikan project punya key dan koneksi store long memory sehat. |
| Suite bertanda ⚠️ `… tidak ada di node_modules (jalankan npm install dulu)` / `vendor/bin/phpunit tidak ada` / `Assembly test belum di-build` / `vstest.console.exe … tidak ditemukan` | Runner-nya belum terpasang di mesin ini; AIT tidak menginstal paket. | Jalankan `npm install` / `composer install` / build solution sekali, atau pasang Visual Studio (Test Platform) untuk project .NET Framework lama. |
| Semua test di satu suite *Error: Test tidak bisa dijalankan (exit code …)* | Runner gagal sebelum menjalankan test, biasanya project test tidak bisa di-build atau konfigurasinya rusak. Pesan dan akhir output ada di detail test. | Perbaiki build project test (`dotnet test` membuild sendiri), lalu jalankan lagi. |
| Test *dilewati — Butuh database / koneksi jaringan / konfigurasi …* | Test gagal karena mesin ini tidak punya database, service, atau file config yang dibutuhkannya; tidak dihitung regresi. Selama 24 jam berikutnya test itu langsung dilewati di thread tanpa menunggu timeout. | Normal untuk test integrasi di laptop tanpa DB. Bila kebutuhannya sudah tersedia, tekan **Jalankan** di Project > Detail (selalu mencoba lagi). Test **baru** dari thread yang sama tidak pernah dilewati karena ini: ia diperbaiki agar tidak menyentuh database/jaringan (lihat [Test yang dilewati](07-cara-kerja.md#test-yang-dilewati)). |
| Test *tidak dilaporkan runner* | Runner jalan, tetapi tidak menyebut test itu: method/test diganti nama, dipindah, atau filter tidak cocok. | **Scan test** untuk mengikuti perubahan nama/file; test lama yang memang hilang tampil di hasil scan (`missing`) dan bisa ditandai obsolete lewat thread. |
| `run test melewati batas waktu` | Run lebih lama dari *Batas waktu run test* (default = *Timeout build*). | Naikkan batasnya di Setting unit test project, atau pakai mode *Relevan saja*. |
| Thread terasa lebih lama sejak ada katalog | Baseline berjalan paralel dengan planner, tetapi eksekusi menunggunya; run setelah build menambah waktu per putaran. Mode *Semua test aktif* menjalankan seluruh suite. | Pakai *Relevan saja*. Hasil untuk kode yang sama dipakai ulang (14 hari) tanpa menjalankan lagi. |
| Regresi tidak diperbaiki: *the file to fix cannot be told without the tester's triage* | Tanpa node Tester, AIT hanya bisa menebak file yang diperbaiki (file target test yang diubah, atau satu-satunya file non-test yang diubah). | Tambahkan node Tester (model kuat, preset `AIT Tester`), atau perbaiki lewat prompt berikutnya (*"perbaiki UT-0012"*). |
| `Masih ada regresi unit test setelah 3 putaran perbaikan` | Perbaikan otomatis dibatasi 3 putaran. | Lihat pesan gagal di bagian *🧪 Unit test*; lanjutkan dengan prompt berikutnya atau perbaiki manual. Bila perilakunya memang sengaja berubah, katakan di prompt agar planner memutuskan `update`. |
| File test baru muncul di `git status` padahal tidak diminta | Test katalog yang belum ada di repository Anda (dibuat developer lain dan belum di-pull, atau terhapus) dipulihkan otomatis sebelum planner. Laporan menyebut commit/branch asalnya. | Commit file itu. Bila Anda kemudian pull branch yang berisi file yang sama, git menolak menimpa file untracked: hapus salinan hasil pemulihan (isinya sama) lalu pull lagi. Bila test memang sengaja dihapus, tandai obsolete lalu konfirmasi hapus. |
| Test *Ditulis ulang (perlu konfirmasi)* | Tidak ada snapshot untuk test yang hilang (mis. *Simpan snapshot test* mati), jadi executor menulisnya lagi dari skenario katalog. | Tinjau kode test-nya sebelum di-commit; hasilnya bisa berbeda dari aslinya. |
| Test berpindah ke status *Karantina* | Test gagal lalu lolos saat diulang, atau Tester menilainya tidak stabil (bergantung waktu, urutan, acak). | Perbaiki test-nya, lalu **Aktifkan** di Project > Detail. |

## Auto Testing

| Gejala | Arti | Solusi |
|---|---|---|
| Fase `🧪 Testing` tidak muncul | *Auto Testing* mati di node Planner yang dipakai, tidak ada node Tester aktif (banner Node memberi catatan), tidak ada tugas yang berhasil, atau thread dibuat sebelum Auto Testing dinyalakan. | Nyalakan *Auto Testing* di Edit node Planner dan tambahkan node ber-role Tester. |
| `Testing · dilewati` | Planner menilai perubahannya tidak bisa dicoba dari luar (alasannya tertulis), atau build terakhir masih gagal. | Perbaiki build dulu; atau sebut di prompt bagian mana yang harus dites. |
| Tester tidak berganti user / tidak ada `switch_user` | Project belum punya akun uji, atau belum punya project key. | Isi akun per peran di Project > Detail → *Akun uji*. |
| `Password dev project ditolak aplikasi` | Aplikasi tidak menerima `Test@1234` (atau password dev yang tersimpan). | Balas `password dev <nilai>` di chat (disimpan untuk project), atau isi di kartu *Akun uji*. Untuk password asli, balas `login <user> <password>` (hanya sesi itu). |
| Rencana ditolak: `actor '…' is neither a test account nor the role of one` | Planner menulis aktor yang tidak ada di daftar akun uji. | Planner memperbaikinya sendiri; bila perannya memang belum ada, tambahkan akun dengan peran itu. |
| `File skenario belum bisa dipakai: Sheet Langkah baris N: …` | Ada baris tanpa langkah/hasil, atau aktor yang tidak ada di sheet Akun. | Perbaiki baris yang disebut, simpan, lalu jalankan/unggah lagi. |
| `… tidak bisa dibaca …; tutup dulu di Excel` | File skenario sedang terkunci Excel. | Simpan dan tutup file di Excel, lalu balas `jalankan skenario <nama>` lagi. |
| `Skenario … belum dijalankan. Skenario dijalankan di aplikasi yang sedang berjalan …` | Konfirmasi efek samping pertama di project ini. | Pastikan email/notifikasi dev tidak ke orang asli, lalu balas `jalankan skenario <nama> setuju`. |
| `Skenario belum lengkap: Peran X belum punya akun uji` | Sheet Akun belum diisi untuk peran itu. | Isi username di sheet Akun (sel kuning), simpan, jalankan lagi. |
| Langkah *tidak bisa dicek — Langkah sebelumnya tidak lolos* | Skenario berhenti di langkah pertama yang gagal. | Lihat langkah yang gagal; perbaiki aplikasi atau isi file, lalu jalankan lagi. |
| *Login as* salah/tidak dipakai | Hasil deteksi belum benar (peran beda nama, URL salah) atau statusnya *tidak ada*. | Buka kartu *Akun uji* → **Ubah**, isi cara dan peran yang benar, centang *sudah dicek*. **Deteksi** mengulang scan kode. |
| `Aplikasi tidak bisa dihubungi di http://localhost:… AIT belum tahu cara menjalankan project ini` | Aplikasinya tidak berjalan dan stack-nya tidak dikenali (lihat [Menjalankan aplikasi](07-cara-kerja.md#menjalankan-aplikasi)), atau ada beberapa project web .NET tanpa port yang cocok. | Tulis perintahnya di `.aithread/project.json`, mis. `{ "run": "dotnet run --project src/Web" }`, atau jalankan aplikasinya sendiri, lalu **🧪 Ulangi testing**. |
| `Aplikasi belum jalan di … dan AIT gagal menjalankannya: … berhenti dengan exit code …` | Aplikasi gagal start: port sudah dipakai, konfigurasi dev tidak ada (mis. section di user secrets atau `appsettings.Development.json` kosong → `NullReferenceException` di `Program.cs`), database/Redis dev tidak bisa dihubungi, atau build gagal. Penyebabnya ada di blok **Output aplikasi**. | Coba perintah yang sama di terminal; bila di sana juga gagal, lengkapi konfigurasi dev di mesin ini. Bila di sana jalan, tulis perintah yang Anda pakai di `.aithread/project.json` (`"run"`). Lalu **🧪 Ulangi testing**. |
| `… AIT gagal menjalankannya: … belum menjawab setelah 3 menit` | Aplikasi jalan di port lain dari yang ditemukan planner, atau start-nya sangat lama. | Tulis URL-nya di prompt atau di *Instruksi tambahan* node Planner (mis. "aplikasi berjalan di http://localhost:5080"), atau jalankan aplikasinya sendiri. |
| `… node_modules belum ada` | Script `dev`/`start` ada, tetapi paketnya belum dipasang; AIT tidak menjalankan `npm install`. | Jalankan `npm install` sekali, lalu **🧪 Ulangi testing**. |
| `Browser tester tidak bisa dibuka` | Edge dan Chrome tidak ditemukan dan Chromium gagal diunduh (offline), atau folder `.playwright` di samping `AIThread.exe` hilang. | Pasang Edge/Chrome, atau sambungkan internet sekali agar Chromium terunduh; pasang ulang AIT bila `.playwright` hilang. |
| `… Node.js untuk Playwright tidak ditemukan dan gagal diunduh` | Tidak ada `node` 20+ di `PATH` dan Node.js tidak bisa diunduh dari nodejs.org (offline/proxy). | Pasang Node.js 20+ (atau arahkan env `PLAYWRIGHT_NODEJS_PATH` ke `node.exe`), restart AIT, lalu **🧪 Ulangi testing**. |
| Browser tidak terlihat | AIT berjalan sebagai service / di sesi Windows lain. | Jalankan AIT dari tray atau menu `AIThread.exe` di sesi Anda. |
| Skenario *tidak bisa dicek — Butuh login* | Tester tidak punya akun. | Balas di chat `login <username> <password>`; untuk thread dari Dashboard/Telegram isi di modal **🧪 Ulangi testing**. |
| Balasan `login …` dijalankan sebagai prompt biasa | Tidak ada pertanyaan login yang menunggu di sesi & project itu (lewat 2 jam, AIT di-restart, atau chat lain), atau formatnya bukan tiga kata `login <username> <password>`. | Pakai **🧪 Ulangi testing** di detail thread, atau minta testing lagi lewat prompt baru. Password berspasi: pakai format berlabel `username: …` / `password: …`. |
| Skenario *tidak bisa dicek* karena captcha | Tester sudah mengisi captcha dengan nilai sembarang (cukup di lingkungan dev yang mem-bypass captcha asal terisi), tetapi aplikasi tetap memeriksanya. | Nyalakan bypass captcha di konfigurasi development (mis. flag di `appsettings.Development.json`/`.env`, atau test key reCAPTCHA), lalu **🧪 Ulangi testing**. |
| Login ditolak padahal akunnya benar | Form login memakai 2FA/OTP atau SSO di host lain (bukan localhost). | Pakai akun uji tanpa 2FA di lingkungan pengembangan. Halaman SSO di host lain tidak bisa dibuka tester. |
| Skenario gagal padahal diff benar | Aplikasi belum memuat perubahan (berjalan tanpa hot reload, atau view/JS di-cache). | Restart aplikasinya atau pakai `dotnet watch`/dev server, lalu **🧪 Ulangi testing**. |
| Tester berputar di halaman yang sama | Model tester terlalu lemah membaca snapshot. | Pakai model tester yang lebih teliti, atau beri id elemen yang jelas di kontrak (planner menyalinnya ke langkah skenario). |

## Redis & database

**Kartu Redis *Tidak terhubung***
Short memory memakai Garnet di dalam AIT. Cari baris `Embedded Garnet` di log AIThread: bila Garnet gagal
start, penyebabnya tercatat di situ. Bila `AIThread:EmbeddedRedis:Enabled` = `false`, pastikan Redis eksternal di
`ConnectionStrings:Redis` hidup. Selama short memory tidak tersedia, memory baru langsung masuk long memory; tidak
ada data hilang.

**Log `Port 6379 is already in use … short memory uses that server`**
Program lain (biasanya container Docker `aithread-redis` lama) memakai port short memory, sehingga AIT
memakainya alih-alih Garnet. Pindahkan dulu short memory yang ingin disimpan (menu Memory → **→ DB**), lalu
`docker rm -f aithread-redis` dan restart AIT. Lihat [Instalasi](01-instalasi.md#1-short-memory-garnet).

**Penyimpanan memory eksternal *Tidak terhubung*** (menu Setting → Penyimpanan memory)
Klik **Test koneksi** pada kartunya: pesan error dari server (host tidak ditemukan, login gagal, database tidak ada)
ditampilkan apa adanya. Selama short memory eksternal mati, memory baru masuk long memory dan store dicek ulang tiap
15 detik. Bila long memory eksternal mati, thread tetap berjalan tanpa long memory (log aktivitas mencatat *Long
memory … tidak dapat dihubungi*), dan konsolidasi menunggu sampai server kembali; atau kembalikan provider ke *Bawaan*. Log `The saved short/long memory store is invalid` saat start berarti setting
tersimpan tidak valid, dan AIT memakai store bawaan.

**Simpan penyimpanan memory ditolak dengan `permission denied` / `CREATE DATABASE`**
AIT membuat database dan tabel memory sendiri. Buat database-nya secara manual lalu beri user hak membuat tabel
di database itu (Oracle: tabel dibuat di schema milik user yang login).

**Kartu Database *Tidak terhubung*** / log `Database migration failed`
Untuk LocalDB: `sqllocaldb start MSSQLLocalDB`. Untuk server lain: periksa `ConnectionStrings:Default`.
Restart AIT setelah diperbaiki agar migrasi dijalankan.

## Telegram

Token ditolak, bot tidak membalas, *Token ini sedang dipakai program lain…*, *Chat ini belum diizinkan*, dan gejala
bot Telegram lainnya: lihat [Prompt dari Telegram → Troubleshooting](11-telegram.md#troubleshooting).

## Skill

**Sumber skill *Gagal*: `HTTP 404 — link tidak ditemukan atau repository privat`**
Repository privat tidak bisa diunduh tanpa kredensial. Untuk host git, `git clone` dijalankan tanpa prompt kredensial,
jadi hanya repository yang bisa di-clone tanpa login (atau dengan credential helper yang sudah tersimpan) yang berhasil.

**`HTTP 403`/`429 — dibatasi oleh server`**
API GitHub membatasi unduhan tanpa login (60 per jam per IP). AIT otomatis mencoba `git clone` sebagai gantinya.
Bila git tidak terpasang, tunggu lalu klik *Sync*.

**Sumber lama di status *Dianalisis AI…***
Bukan error bila baris progresnya (mis. `864 skill · batch 7/22`) masih bergerak. Skill diberi tag per 40 skill,
4 batch sekaligus, jadi repository berisi ratusan `SKILL.md` butuh beberapa menit, lebih lama dengan model besar.
Model Skill Orchestrator yang lebih cepat (mis. model executor) mempercepatnya. Batch yang gagal disimpan tanpa tag.

**`git tidak ditemukan di PATH`**
Pasang Git, atau pakai link GitHub/GitLab (diunduh sebagai ZIP tanpa git).

**`Tidak ada skill yang ditemukan di link ini`**
Repository tidak punya `SKILL.md` dan model juga tidak menemukan file Markdown yang berisi instruksi untuk AI. Coba
link ke folder skill-nya langsung (`/tree/main/<folder>`) atau ke file `.md`-nya.

**Skill tersimpan tanpa tag / *Cari skill* gagal: `Pilih model Skill Orchestrator…`**
Pilih model di Setting → Skill, atau tambahkan node Planner aktif. Skill tanpa tag tetap bisa dipakai; klik *Sync*
setelah model tersedia agar tag dibuat ulang.

**Node terasa lambat atau mahal setelah diberi skill**
Isi skill ikut terkirim di setiap panggilan model node. Kurangi jumlah skill, atau turunkan *Batas karakter skill per
node*.

## Aplikasi

**AIT berhenti saat browser, terminal, atau Visual Studio ditutup**
Instance dari Visual Studio (debugger berhenti saat browser yang dibukanya ditutup) dan `dotnet run` hanya hidup
selama program itu terbuka. Jalankan di background: `.\scripts\aithread.ps1 start` — lihat
[Menjalankan di background](01-instalasi.md#5-menjalankan-di-background).

**Port 5225 sudah dipakai**
Jalankan dengan `--urls http://localhost:<port lain>` lalu terapkan ulang integrasi CLI di menu CLI.

**`aithread.ps1 start`: "Port 5225 sudah dipakai proses lain (PID …)"**
Biasanya instance dari Visual Studio / `dotnet run` masih hidup. Hentikan program itu, lalu `start` lagi. Bila yang
memakai port adalah AIT, `.\scripts\aithread.ps1 stop` juga bisa mematikannya.

**`aithread.ps1 start`: "AIT berhenti saat start (exit N)"**
Potongan log langsung ditampilkan. Penyebab umum: database tidak bisa dihubungi (lihat [Redis & database](#redis--database))
atau `appsettings.json` tidak valid. Log lengkap: `.\scripts\aithread.ps1 logs`.

**Perubahan kode tidak terlihat di instance background**
Instance background memakai hasil publish, bukan folder `src/`. Jalankan `.\scripts\aithread.ps1 restart`.

**Log background berisi `Failed to determine the https port for redirect`**
Normal: instance background hanya mendengarkan http. Peringatan ini tidak berpengaruh.

**Thread *Cancelled*: "AIT berhenti saat thread ini berjalan."**
AIT dimatikan (stop, restart, logoff, crash) saat thread itu berjalan. Hasil tugas yang sudah selesai tetap
tertulis di file — tinjau dengan git, lalu kirim ulang prompt bila perlu.

**Thread *Cancelled* setelah planner bertanya**
Pesannya menjelaskan sebabnya: *AIT di-restart sebelum pertanyaan planner dijawab*, *tidak dijawab dalam 2 jam*,
*dibatalkan saat menunggu jawaban*, atau *prompt berikutnya dijalankan sebagai thread baru* (jawaban dikirim dari
project lain). Pertanyaan hanya disimpan di memori proses, jadi kirim ulang jawaban Anda bersama permintaannya —
planner tetap bisa membaca pertanyaan tadi dari riwayat chat.

**Jawaban untuk planner malah menjadi thread baru**
Jawaban dikenali dari id sesi klien. Pastikan Anda menjawab di sesi (chat) yang sama, belum `/clear` atau membuka sesi
baru, dan klien Anda mengirim id sesi (Claude Code dan Telegram selalu; klien tanpa id sesi tidak pernah ditanya).

**Base URL https gagal dari CLI**
CLI berbasis Node.js (mis. Claude Code) tidak mempercayai sertifikat development. Pakai alamat `http://localhost:5225`.

**Halaman tidak update live**
Browser memutus koneksi SSE (mis. terlalu banyak tab AIT terbuka). Tutup tab lain atau refresh halaman.
