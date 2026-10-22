# 4. Penggunaan sehari-hari

## Alur kerja

1. Pastikan 9router dan AIT berjalan (pill *9router* di sidebar AIT berwarna hijau).
2. Buka Claude Code di folder project yang sudah diarahkan ke AIT.
3. Ketik permintaan seperti biasa.
4. Pantau progres di Claude Code, atau buka Dashboard → thread untuk detailnya.
5. Tinjau perubahan dengan `git diff`.
6. Jalankan sendiri langkah lanjutan yang disebut ringkasan (build, script SQL/migrasi, restart).
7. Lanjutkan dengan prompt berikutnya — percakapan sebelumnya dan memory ikut dibaca planner.

## Membaca output di Claude Code

Contoh untuk prompt *"Tambahkan kolom deskripsi di form user: field di UI, logic di JS, validasi di service, dan DDL database."*:

`````
**AIT** · `SampleApp` · thread `5e469b54`
````text
🧠 Planner · cc/claude-opus
│  ├─ 🔖 Label · 1 file → User·UI · 3 memory relevan
│  ├─ list_directory  .
│  ├─ search_memory   "user description validation"
│  ├─ read_memory     2 item
│  ├─ read_file       src/Web/Views/User/Index.cshtml
│  ├─ read_file       src/Web/wwwroot/js/user.js
│  ├─ read_file       src/Service/UserService.cs
│  └─ selesai dalam 12.3s
│
📋 Rencana · 4 tugas
│  Menambah field Deskripsi pada form User: input di view, pengiriman nilai di JS, validasi
│  di service, dan script DDL kolom baru.
│  ├─ T1 src/Web/Views/User/Index.cshtml
│  │     Add description textarea to the user form
│  ├─ T2 src/Web/wwwroot/js/user.js
│  │     Send description in saveUser
│  ├─ T3 database/AddUserDescription.sql
│  │     DDL script for Users.Description
│  └─ T4 src/Service/UserService.cs ← T3
│        Validate description length
│
🚀 Eksekusi · 2 executor paralel
│  ├─ ✅ T2   1.7s  Combo Executor  saveUser now sends description from #txtDescription.
│  ├─ ✅ T3   1.7s  GLM Executor    Created an idempotent script adding Users.Description.
│  ├─ ✅ T1   1.8s  GLM Executor    Added the txtDescription textarea after the name field.
│  ├─ ✅ T4   3.3s  Combo Executor  Added a 500-character limit for Description.
│  └─ 4/4 berhasil · 5.0s (1.7x lebih cepat dari sekuensial)
│
🔍 Verifikasi · dotnet build SampleApp.sln + reviewer cc/claude-sonnet
│  ├─ 🔨 Build gagal: 1 error (14.2s)
│  │     src/Service/UserService.cs(48,13): CS0103: The name 'MaxDescriptionLength' does
│  │     not exist in the current context
│  ├─ 🔎 Reviewer: 1 masalah — Konstanta batas panjang deskripsi belum dideklarasikan.
│  │     src/Service/UserService.cs — MaxDescriptionLength is used but never declared.
│  ├─ 🔧 Perbaikan 1 · 1 tugas
│  │     ✅ V1-1   2.1s  GLM Executor    Declared MaxDescriptionLength = 500.
│  ├─ ✅ Build berhasil (9.8s)
│  ├─ ✅ Reviewer: Semua layer memakai txtDescription/Description sesuai kontrak.
│  └─ lolos setelah 1 putaran perbaikan
│
📦 Rangkuman · glm/glm-cheap
````

---

✅ **Selesai** · 4/4 tugas · build lolos · reviewer setuju

Field **Deskripsi** sudah ditambahkan end-to-end. Jalankan `database/AddUserDescription.sql` di database sebelum
mencoba form-nya.

**File berubah**

| File | Aksi | Baris | Tugas |
|---|---|---|---|
| `src/Web/Views/User/Index.cshtml` | ubah | +4 | T1 |
| `src/Web/wwwroot/js/user.js` | ubah | +1 −1 | T2 |
| `database/AddUserDescription.sql` | baru | +9 | T3 |
| `src/Service/UserService.cs` | ubah | +7 | T4, V1-1 |

⏱️  48.6s · 🪙  41.2k token · 🧠 2 memory dipakai, +2 memory baru · 📚 Wiki masuk antrian rangkuman berikutnya (tiap 6 jam)

📊 **Sisa kuota**
- **Antigravity · DS997**
  - 🟩🟩🟩🟩🟩🟩🟩🟩🟩⬜ 98% · Gemini 3.8 Flash (High) · reset 1j 56m
- **Claude Code · Andi**
  - 🟥⬜⬜⬜⬜⬜⬜⬜⬜⬜ 17% · session (5h) · reset 1j 20m
  - 🟩🟩🟩🟩🟩🟩⬜⬜⬜⬜ 69% · weekly (7d) · reset 4h 18j

🔗 Detail & rollback: http://localhost:5225/Thread/Detail/5e469b54-…
`````

Output terdiri dari tiga bagian:

1. **Baris anchor** `**AIT** · project · thread …`: nama project yang terdeteksi dan id thread (cari di Dashboard). Baris
   ini hanya muncul untuk sesi di dalam sebuah project (folder dengan `.git` atau file build), dan selalu tampil
   walaupun progres disembunyikan — AIT memakainya untuk mengenali thread sebelumnya di percakapan. Balasan lama yang
   masih bertuliskan `**AIThread**` tetap dikenali.
2. **Progres** dalam blok teks (font monospace), berbentuk *vertical stepper*: tiap fase diawali emoji, entri fase
   memakai `├─`, dan fase ditutup baris `└─` berisi hasilnya. Baris panjang dipotong di 90 kolom.
3. **Jawaban akhir** setelah `---`.

| Fase / baris | Arti |
|---|---|
| `🧠 Planner` | Planner membaca kode. Entri pertama `🔖 Label` adalah hasil pencarian label sebelum model dipanggil: file yang disebut prompt beserta modul·layer-nya (maks. 4 ditampilkan), lalu jumlah memory yang ditemukan pencarian dua lapis untuk brief planner (lihat [Planner](07-cara-kerja.md#3-planner)). `dari sesi (thread …)` berarti prompt tidak menyebut file, jadi labelnya diambil dari thread AIT sebelumnya di chat ini. Entri berikutnya masing-masing satu panggilan tool (read-only); nama tool rata kiri agar argumennya sejajar. `run_build` tampil dua baris: perintahnya, lalu hasilnya di bawahnya. Saat thread dilanjutkan setelah pertanyaan planner, baris label tidak muncul lagi. |
| `🧷 Pemulihan unit test` | Hanya bila test katalog project tidak ada di repository Anda: satu baris per test yang dipulihkan (dari snapshot, disisipkan kembali, atau ditulis ulang executor) dan catatan git. Lihat [Pemulihan](07-cara-kerja.md#pemulihan-test-yang-hilang). |
| `📋 Rencana` | Ringkasan rencana, lalu satu entri per tugas: id, path relatif penuh, dependensi (`← T3` = menunggu T3), dan judul tugas di baris bawahnya. |
| `🚀 Eksekusi` | Satu baris per tugas yang selesai, dalam kolom: status, id, durasi, executor, ringkasan. `✅` berhasil, `❌` gagal (dengan alasan), `⏩` dilewati karena tugas yang ditunggunya gagal, `🟡` node dialihkan ke cadangannya. Penutupnya menyebut waktu nyata dan, bila paralel memang menghemat waktu, berapa kali lebih cepat dari sekuensial. |
| `🧪 Unit test · baseline` | Hanya dengan [katalog unit test](07-cara-kerja.md#unit-test): hasil test terkait sebelum perubahan (dijalankan paralel dengan planner, atau diambil dari hasil sebelumnya untuk kode yang sama). |
| `🔍 Verifikasi` | AIT membuild project, menjalankan unit test terkait, dan/atau node Reviewer memeriksa hasil (lihat [Verifikasi](07-cara-kerja.md#5-verifikasi-build-dan-reviewer)). `🔨` build gagal (error maks 5 baris), `🔎` temuan reviewer, `✅`/`🔴 Unit test · 14 test · 12 lolos, 1 regresi` hasil unit test (test yang gagal di bawahnya), `🩺 Triase` node Tester mendiagnosis test yang gagal, `🔧 Perbaikan` = putaran tugas perbaikan `V1-1`, …, `🟡` peringatan. |
| `🧪 Testing` | Hanya dengan [Auto Testing](02-konfigurasi.md#auto-testing-node-tester): node Tester mencoba perubahan di aplikasi yang berjalan. Satu baris per tool (`browser_open`, `browser_fill  Deskripsi ← "AIT test"`, `http_request  POST /api/users`, …); di bawah `http_request` tampil status, durasi, dan awal body respons. `🌐` = browser tester dibuka (jendelanya muncul di layar), `🚀`/`🟢` = aplikasi belum jalan lalu dijalankan AIT dan sudah menjawab, `🟡` = aplikasi tidak bisa dihubungi, `🔐` = tester butuh login. Penutupnya: `lolos 2/2 skenario`, `1 dari 3 skenario gagal`, atau *dilewati* beserta alasannya. |
| `📦 Rangkuman` | Node Wrapper sedang menulis jawaban akhir. Tidak muncul bila tidak ada Wrapper aktif. |

Jawaban akhir disusun sebagian oleh AIT sendiri (dari hasil, bukan dari model) dan sebagian oleh Wrapper:

| Bagian | Dibuat oleh | Isi |
|---|---|---|
| Baris status | AIT | `✅ Selesai` / `🟡 Selesai sebagian` / `❌ Gagal`, jumlah tugas rencana yang berhasil (tugas perbaikan `V` tidak dihitung), hasil build dan reviewer terakhir, hasil unit test (`unit test lolos (12)`, `unit test: 1 regresi`, …), dan hasil testing (`testing lolos`, `testing: 1 gagal`, …). Skenario testing yang gagal, regresi unit test, atau test baru yang gagal membuat status *Selesai sebagian*. Thread yang hanya menguji memakai `🧪 Pengujian selesai · tanpa perubahan kode`. |
| **Perlu perhatian** | AIT | Hanya bila ada tugas gagal/dilewati, error build/temuan reviewer, atau unit test yang masih gagal. |
| Teks | Wrapper | 1–3 kalimat tentang apa yang dikerjakan, saran untuk masalah yang tersisa, dan langkah lanjutan bila perlu. Tanpa node Wrapper (atau bila Wrapper gagal), ringkasan rencana yang tampil. |
| **File berubah** | AIT | Satu baris per file dari diff: aksi (`baru`/`ubah`/`hapus`/`pindah`), baris `+`/`−`, dan tugas yang menyentuhnya. |
| **🧪 Unit test** | AIT | Hanya bila unit test katalog dijalankan atau dipulihkan: ringkasan, lalu test yang perlu perhatian (maks 10) — `❌` regresi/gagal beserta pesannya, `✅` gagal sebelum perubahan dan sekarang lolos, `🟠` sudah gagal sebelum perubahan, `〰️` tidak stabil, `⏭️` dilewati beserta alasannya — test yang ditandai obsolete (`🗃️`, menunggu konfirmasi hapus), test yang dipulihkan (`♻️`), dan catatan. Test yang tetap lolos hanya dihitung. |
| **🧪 Testing** | AIT | Hanya dengan Auto Testing: level, URL aplikasi, browser, ringkasan tester, `✅`/`❌`/`🚫` per skenario dengan penjelasannya, *Error di browser* (maks 5), lalu **Output API**: tiap request (maks 5) dengan status, durasi, dan body respons dalam blok kode (maks 30 baris / 2.000 karakter; selengkapnya di detail thread). Bila tester butuh login, ditutup dengan pertanyaan **🔐 Tester butuh login** — lihat [Login untuk testing](#login-untuk-testing). |
| Footer | AIT | Durasi total, total token (planner + executor + reviewer + wrapper + ekstraksi memory), memory yang dipakai planner dan memory baru, antrian wiki bisnis (bila *Business memory* aktif), *Sisa kuota* provider, dan link ke halaman thread untuk detail & rollback. |

Footer menunggu ekstraksi memory selesai, paling lama 30 detik. Bila lebih lama, footer menulis
*memory baru masih diproses* dan ekstraksi tetap berjalan di latar belakang. Yang disimpan sebagai jawaban thread
(Dashboard, Telegram, context keeper) hanya teks Wrapper atau ringkasan otomatis.

**Sisa kuota** dibaca dari 9router (`/api/usage/{koneksi}`) dan hanya mencakup akun provider yang modelnya dipakai
node aktif beserta node cadangannya (anggota combo ikut dihitung). Per akun 9router, tiap batas (mis. jendela 5 jam /
7 hari Claude) tampil sebagai bar 10 kotak (1 kotak = 10%, sisa berapa pun minimal 1 kotak) beserta persen sisa dan
waktu reset (`m` menit, `j` jam, `h` hari). Warna bar: 🟩 sisa > 50%, 🟨 > 35%, 🟥 > 0%, dan semua ⬜ bila habis —
terminal hanya bisa mewarnai emoji, dan tidak ada emoji kotak abu-abu. Untuk kuota
per model (mis. Antigravity) hanya model yang dipakai node yang tampil. Akun API key biasa dan provider yang kuotanya
tidak bisa dibaca 9router tidak tampil. Hasil di-cache 60 detik; bila 9router tidak menjawab dalam 10 detik atau
dashboard-nya mewajibkan login, bagian ini dilewati — lihat [Troubleshooting](08-troubleshooting.md#9router).

Progres bisa disembunyikan lewat Setting → *Tampilkan progres di Claude Code*; anchor dan jawaban akhir tetap tampil.

## Menulis prompt yang efektif

AIT paling efektif untuk perubahan yang menyentuh beberapa file/layer sekaligus.

- **Sebut layer yang diinginkan**: "field di UI, JS, parameter API, parameter & validasi service, DDL".
- **Tunjuk contoh yang sudah ada** agar planner meniru polanya: "sama seperti kolom `Email` di form yang sama".
- **Sebut batasan**: "jangan ubah API publik", "pakai migrasi EF, bukan script SQL", "maksimal 500 karakter".
- **Satu fitur per prompt.** Perubahan besar lebih akurat bila dipecah menjadi beberapa prompt berurutan.
- **Pertanyaan boleh langsung ditanyakan** ("di mana validasi user?"). Planner membaca kode lalu menjawab tanpa
  membuat tugas — tidak ada executor yang berjalan.
- **Perubahan yang belum di-commit bisa langsung dirujuk** ("review perubahan saya", "lanjutkan yang tadi saya
  kerjakan", "apa yang berubah sejak commit kemarin?"). Di repository git, planner membaca `git status`, `git diff`, dan
  riwayat commit sendiri lewat [tool git](07-cara-kerja.md#3-planner), jadi diff tidak perlu ditempel.

## Yang tidak dilakukan AIT

- Tidak menjalankan migrasi, git yang mengubah repository (commit, checkout, merge, push), instalasi paket (`npm
  install`, `composer install`, restore manual), atau perintah shell lain. Perintah selain git read-only (status,
  diff, log, show, blame) hanya **build** project (bila *Build & verifikasi otomatis* menyala) dan **unit test**
  [katalog project](07-cara-kerja.md#unit-test) dengan runner yang dikenali (`dotnet test`, jest/vitest/mocha,
  pytest, PHPUnit, Maven/Gradle, `go test`, `cargo test`). Anda bisa meminta "build lalu perbaiki errornya" atau
  "jalankan unit test modul order", dan setelah tiap perubahan AIT membuild ulang dan menjalankan test terkait
  sendiri. Langkah lain disebutkan di ringkasan.
- Menjalankan aplikasi Anda hanya untuk testing. Dengan [Auto Testing](02-konfigurasi.md#auto-testing-node-tester), AIT
  mencoba aplikasi di localhost (browser dan request HTTP); yang belum jalan dijalankan lalu dihentikan lagi setelah
  testing ([Menjalankan aplikasi](07-cara-kerja.md#menjalankan-aplikasi)). AIT tidak memperbaiki kode berdasarkan hasil
  testing.
- Tidak memakai tool bawaan Claude Code: saat orkestrasi aktif, Claude Code berfungsi sebagai tampilan percakapan.
  Untuk pekerjaan yang butuh menjalankan perintah selain build, matikan *Orkestrasi aktif* sementara (Claude Code
  menjadi agen biasa lewat 9router) atau kembalikan integrasi.
- Tidak meminta izin per edit — executor menulis langsung ke file. Kerjakan di repo git.

## Meninjau hasil

- `git diff` / `git status` untuk melihat semua perubahan.
- Dashboard → klik thread → klik baris tugas untuk melihat instruksi, ringkasan, **diff**, error, dan output
  model (output live hanya terlihat bila halaman sudah terbuka saat tugas berjalan).
- Tugas gagal: baca alasan di baris `❌` atau di detail tugas. Ulangi dengan prompt yang lebih spesifik untuk bagian
  itu saja, mis. "lanjutkan validasi deskripsi di UserService yang tadi gagal".

Status thread:

| Status | Arti |
|---|---|
| Selesai | Planner menjawab langsung, atau minimal satu tugas berhasil (tugas lain yang gagal, error build/temuan reviewer yang tersisa setelah verifikasi, dan skenario testing yang gagal, dicatat di kolom error). |
| Gagal | Tidak ada tugas berhasil, atau planner/9router gagal. Pesan penyebab tampil di Claude Code dan di detail thread. |
| Dibatalkan | Dihentikan oleh Anda. |
| Perubahan dibatalkan | Perubahan file thread ini sudah dikembalikan lewat **Batalkan perubahan file** (lihat di bawah). |

## Membatalkan

- Tekan **Esc** di Claude Code saat thread berjalan — request terputus dan thread dibatalkan.
- Atau buka detail thread → **Batalkan thread**.

Tugas yang belum selesai dihentikan dan tidak menulis apa pun. Tugas yang **sudah** selesai sebelum pembatalan tetap
tersimpan di file. Untuk mengembalikannya, pakai **Batalkan perubahan file** (di bawah).

## Mengulang tugas yang gagal

Detail thread → **🔁 Ulangi yang gagal** (muncul bila thread sudah selesai, gagal, atau dibatalkan dan masih ada tugas
rencana yang belum berhasil).

- Rencana yang tersimpan dipakai lagi; **planner tidak dipanggil**, jadi tidak ada biaya planner.
- Yang dijalankan ulang hanya tugas rencana yang gagal, dilewati, atau dibatalkan. Tugas yang sudah berhasil tidak
  diulang, dan dependensi ke tugas itu dianggap terpenuhi.
- Setelah itu verifikasi (build/reviewer) dan wrapper berjalan lagi atas seluruh hasil thread. Tugas perbaikan lama
  yang tidak berhasil ditandai *Dilewati* karena digantikan oleh verifikasi baru.
- Semuanya tercatat di thread yang sama; jumlahnya tampil di *Percobaan ulang* pada panel waktu & token.
- Berguna setelah rate limit, setelah menambah node Executor yang tadinya belum ada, atau setelah memperbaiki
  konfigurasi model.

## Login untuk testing

Bila aplikasi meminta login, akhir jawaban berisi **🔐 Tester butuh login**. Balas di chat yang sama:

```
login admin rahasia123
```

AIT langsung menjalankan testing thread itu lagi dengan akun tersebut dan membalas dengan hasil testing barunya. Login
diingat selama sesi chat (8 jam, di memori), jadi thread berikutnya tidak ditanya lagi. Password tidak ditulis ke disk
dan tidak pernah dikirim ke model (tester hanya melihat `{{password}}`), tetapi tetap ada di riwayat chat Anda — pakai
akun uji. Detail: [Login](07-cara-kerja.md#login).

## Mengulang testing

Detail thread → **🧪 Ulangi testing** (thread *Selesai* yang dibuat dengan Auto Testing dan punya rencana testing UI/API)
menjalankan lagi skenario rencana testing ke aplikasi yang sedang berjalan, tanpa planner dan tanpa mengubah kode.
Berguna bila aplikasinya belum jalan saat testing pertama atau perlu di-restart agar perubahan termuat. Modalnya punya
field **username & password** opsional untuk aplikasi yang butuh login (dipakai sekali, tidak disimpan). Kemajuannya
tampil di kartu *Testing* dan aktivitas thread (tidak di Claude Code).

## Test lanjutan

Bila Tester menyarankan **test lanjutan** (mis. approve level 1, edit sebelum approve), jawaban akhir menampilkannya
bernomor. Jalankan dengan membalas `jalankan test lanjutan <nomor>` di chat yang sama, atau tombol **Jalankan** di kartu
*Testing* halaman thread. Tester memakai akun uji project dan nilai yang ia ingat dari langkah sebelumnya (mis. nomor
dokumen), lalu bisa menyarankan langkah berikutnya lagi. Detail: [Test lanjutan](07-cara-kerja.md#test-lanjutan-dan-nilai-antar-langkah).

Untuk menentukan sendiri akun tiap peran dan nilai form, balas `buat skenario test lanjutan <nomor>`: AIT menulis file
Excel di `.aithread/testing/`. Isi sel kuningnya, simpan, lalu balas `jalankan skenario <nama>` (pertama kali per project:
`jalankan skenario <nama> setuju`). Detail: [Skenario multi-langkah](07-cara-kerja.md#skenario-multi-langkah-excel).

## Unit test project

Bila project punya test (xUnit/NUnit/MSTest, jest/vitest/mocha, pytest, PHPUnit, JUnit, Go, Rust), thread pertama
mendaftarkannya ke **katalog unit test** project, dan sejak itu setiap thread menjalankan test yang terkait dengan
perubahannya sebelum dan sesudah mengubah kode. Test yang lolos sebelumnya lalu gagal (regresi) diperbaiki di
**kode**, bukan dengan melemahkan test. Katalognya dipakai bersama semua developer project itu (lihat
[Unit test](07-cara-kerja.md#unit-test)).

Contoh prompt:

| Prompt | Yang terjadi |
|---|---|
| *"Tambahkan diskon member di OrderService, buatkan unit test-nya"* | Planner merancang kasus test (judul, skenario, nilai), executor menulis kode dan file test paralel, AIT menjalankan test baru setelah build, dan test baru masuk katalog dengan nomor `UT-…`. Tanpa kata "unit test", test baru hanya dibuat bila setting project *Otomatis untuk perubahan logika bisnis*. |
| *"Jalankan semua unit test"*, *"jalankan test modul order"* | Tanpa perubahan kode: AIT menjalankan test lalu Wrapper menjelaskan hasilnya. |
| *"Kenapa UT-0012 gagal? perbaiki"* | Planner menjalankan test itu lewat `run_tests`, membaca kodenya, lalu merencanakan perbaikan. |
| *"Hapus fitur export PDF"* | Test yang menguji fitur itu ditandai *obsolete* beserta alasannya dan tidak dijalankan lagi. Konfirmasi hapusnya di Project > Detail → Unit test. |

- Test katalog yang belum ada di repository Anda (dibuat developer lain dan belum Anda pull, atau terhapus) dipulihkan
  otomatis sebelum planner berjalan dan muncul sebagai file baru di `git status`. Commit file itu, atau bila memang
  sengaja dihapus, tandai test-nya obsolete.
- Test yang butuh database, jaringan, atau config yang tidak ada di mesin Anda tampil *dilewati* dengan alasannya,
  tidak dianggap gagal.
- Dengan node **Tester** aktif, test yang gagal didiagnosis dulu (kode yang salah, test baru yang salah, atau
  lingkungan) sebelum diperbaiki. Tanpa Tester, AIT langsung memperbaiki kode yang diuji test itu.

## Membatalkan perubahan file (rollback)

Detail thread → **↩ Batalkan perubahan file** mengembalikan semua file yang diubah thread ini ke keadaan sebelum
thread berjalan.

Cara kerjanya:

1. **Sebelum** executor menulis sebuah file untuk pertama kali dalam thread, isi aslinya disimpan di database apa
   adanya (byte demi byte, termasuk encoding dan akhir baris). File yang belum ada dicatat sebagai "belum ada".
   Penulisan berikutnya di thread yang sama (putaran perbaikan, percobaan ulang) tidak menimpa catatan ini.
2. **Setelah** setiap penulisan, sidik jari (hash SHA-256) isi yang ditulis AIT ikut dicatat. File yang dihapus
   atau dipindahkan thread (tugas `delete`/`move`) dicatat sebagai "dihapus thread". Tugas `extract` mencatat file
   sumber dan file tujuannya seperti penulisan biasa.
3. Tombol ini membuka pratinjau per file:
   - *Dikembalikan ke isi asli*: isi file sekarang masih persis hasil thread ini, atau file dihapus/dipindahkan thread
     ini dan path lamanya masih kosong.
   - *Dihapus*: file dibuat (atau dipindahkan ke path ini) oleh thread ini dan isinya belum diubah orang lain.
   - *Sudah sama dengan aslinya*: tidak ada yang perlu dilakukan.
   - *Sudah berubah lagi*: file diedit setelah thread ini (oleh Anda atau thread lain), atau dihapus.
4. **Semua atau tidak sama sekali**: bila ada satu saja file *Sudah berubah lagi*, pembatalan ditolak dan **tidak ada
   file yang disentuh**. Kembalikan file itu secara manual (mis. lewat git), atau batalkan dulu thread yang lebih baru,
   lalu coba lagi.
5. Setelah berhasil, status thread menjadi *Perubahan dibatalkan*. Thread itu tidak bisa diulang atau dibatalkan lagi.
   Di [katalog unit test](07-cara-kerja.md#unit-test), test baru yang ditambahkan thread itu dihapus dan test yang
   ditandai obsolete olehnya kembali aktif.

Batasan:

- Hanya bisa saat thread tidak sedang berjalan.
- Hanya mencakup file yang ditulis, dihapus, atau dipindahkan AIT, bukan perubahan lain (mis. output build). Test yang
  [dipulihkan otomatis](07-cara-kerja.md#pemulihan-test-yang-hilang) sebelum planner juga tidak ikut dibatalkan: itu
  bukan bagian dari permintaan.
- Folder yang menjadi kosong setelah file buatan thread dihapus ikut dihapus.
- Thread yang berjalan sebelum fitur ini ada tidak punya catatan, jadi tidak bisa dibatalkan.

## Menjalankan prompt tanpa Claude Code

Kartu *Jalankan prompt* di Dashboard sedang disembunyikan. Untuk menguji konfigurasi node tanpa Claude Code, kirim
`POST /api/threads` dengan body `{ "projectId": <id project>, "prompt": "…" }`; balasannya id thread yang bisa
dipantau di `/Thread/Detail/<id>`.

## Menjalankan prompt dari Telegram

Saat laptop ditinggal menyala, kirim prompt dari HP lewat bot Telegram pribadi. Pilih project dengan `/projects`,
lalu kirim pesan teks biasa: bot menampilkan status thread yang diperbarui live (rencana, tiap tugas, tombol
*Batalkan*) dan mengirim ringkasan wrapper saat selesai; `/diff` menampilkan perubahan file. Setup dan daftar perintah
ada di [Prompt dari Telegram](11-telegram.md).

## Sesi di luar project

AIT hanya mengorkestrasi sesi yang dibuka di dalam sebuah project: folder yang berisi `.git` atau file build
(`.sln`, `pom.xml`, `package.json`, `go.mod`, dst.), atau salah satu sub-foldernya. Bahasa project tidak dibatasi.
Pada prompt pertama sebuah sesi, AIT memeriksa working directory-nya:

- **ada marker project** → project didaftarkan (bila belum) dan semua prompt sesi itu diorkestrasi;
- **tidak ada marker** → sesi **diabaikan**: prompt diteruskan langsung ke 9router, Claude Code bekerja sebagai agen
  biasa, dan tidak ada thread, project, maupun memory yang tercatat. Contohnya folder kosong tempat Anda hanya meminta
  dibuatkan sebuah file teks. Log dashboard menampilkan
  `Sesi xxxxxxxx (…) bukan bagian project (alasannya) — prompt diabaikan orkestrasi dan diteruskan langsung.`

Folder induk yang hanya berisi beberapa project (mis. 4 repo berdampingan) juga diabaikan karena ambigu; log menyebut
nama project di dalamnya. Buka sesi Claude Code di dalam salah satunya. Ingin folder tanpa marker tetap diorkestrasi
dan punya memory? Daftarkan manual lewat menu Project (*Daftarkan manual*).

Keputusan untuk sesi yang diabaikan dicek ulang setelah sekitar 5 detik, jadi begitu Anda menambahkan `.git` atau file
build, prompt berikutnya langsung dikenali.

## Beberapa project sekaligus

Sesi dari project berbeda boleh berjalan bersamaan (mis. 4 sesi Claude Code di 4 project, atau campuran Java, Node,
dan .NET). Tiap thread terikat ke root project-nya sendiri, memory project terpisah, dan batas *Max paralel* tiap
node dibagi di antara semua thread.

## Mengecek folder atau project lain

Sebutkan path lengkapnya di prompt, mis. *"cek struktur `D:/project/otherproject` dan bandingkan dengan project ini"*.
Planner boleh **membaca** (melihat isi folder, mencari teks, membaca file) di luar root project, jadi ia bisa menjawab
dari sana. Mengubah file tetap hanya di dalam root project: bila Anda meminta perubahan di luar root, planner
menjelaskannya di jawaban dan tidak membuat tugas. Untuk mengerjakan project lain, buka sesi di project itu.

## Percakapan lanjutan

Setiap giliran, AIT mengirim ke planner hingga 12 pesan terakhir percakapan (dipotong bila panjang) dan indeks
memory project. Jadi "sekarang tambahkan juga di halaman detail" bisa dipahami tanpa mengulang konteks.

Planner juga melihat file yang sudah diubah thread-thread sebelumnya di chat ini, per modul. *"Test perubahan ini"* atau
*"review yang tadi"* mengacu ke perubahan itu, bukan ke perubahan dari tab Claude Code lain di folder yang sama. Bila
chat ini sudah mengubah beberapa modul, planner menanyakan modul mana (atau semua) — lihat
[Merujuk perubahan sebelumnya](07-cara-kerja.md#merujuk-perubahan-sebelumnya).

Sesekali planner balik bertanya, misalnya bila permintaan bisa ditafsirkan dua cara yang hasilnya berbeda jauh.
Balasan AIT lalu berupa pertanyaan bernomor (status thread *Menunggu jawaban*); ketik nomor pilihan atau jawaban
Anda sebagai prompt berikutnya, dan thread yang sama dilanjutkan. Lihat
[Pertanyaan dari planner](07-cara-kerja.md#pertanyaan-dari-planner).
