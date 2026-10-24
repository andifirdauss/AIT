# 5. Panduan menu

Semua halaman memperbarui diri secara live lewat Server-Sent Events — tidak perlu refresh. Menu sidebar dikelompokkan
menjadi *Ruang kerja* (Dashboard, Project, Memory — dipakai sehari-hari), *Konfigurasi* (Setup, Node, CLI, Setting) dan
*Bantuan* (Docs, Panduan memulai). Pill di kiri bawah sidebar menunjukkan status 9router; tombol *Ganti tema* beralih
antara mode gelap dan terang.

**Tombol jendela.** Semua jendela, dialog, panel log, dan blok kode memakai tiga tombol gaya macOS di kiri title
bar (simbolnya muncul saat kursor di atasnya):

| Tombol | Dialog | Panel log | Blok kode (output, diff, build, pratinjau config) |
|---|---|---|---|
| 🔴 merah | tutup | kosongkan tampilan log (log baru tetap masuk) | nonaktif (abu-abu) |
| 🟡 kuning | kecilkan ke bawah layar; halaman di belakang bisa dipakai, klik judulnya untuk membuka lagi | lipat / buka | lipat / buka |
| 🟢 hijau | layar penuh | layar penuh | layar penuh |

Double-click title bar = layar penuh; Esc keluar dari layar penuh. Status lipat panel log diingat browser. Banner dan
notifikasi hanya punya tombol merah.

**Setup belum selesai.** Selama masih ada langkah wajib [Setup](#setup) yang belum selesai, link *Setup* di sidebar
menampilkan angka (mis. `2/4`) dan di atas tiap halaman muncul banner *Setup AIT belum selesai* berisi langkah yang
tersisa. Tombol merah banner menyembunyikannya sampai ada langkah lain yang belum selesai. Saat AIT pertama kali dibuka
(belum ada pipeline node), Dashboard langsung membuka halaman Setup, sekali per browser.

**Panduan memulai.** Popup bergambar berisi 9 langkah: mengenal AIT, memasang AIT, menyiapkan 9router, koneksi 9router,
model per peran, Project, CLI, mulai bekerja, dan selesai. Geser, klik titik, atau pakai tombol panah ← → untuk berpindah
langkah; tombol *Kerjakan di Setup* membuka langkah yang sama di halaman Setup. Buka lewat menu *Panduan memulai* di
grup *Bantuan* sidebar (melanjutkan dari langkah terakhir yang dilihat) atau *Panduan bergambar* di halaman Setup.

## Dashboard

**Kartu status**

| Kartu | Isi |
|---|---|
| 9router | Online/Offline, latency, jumlah model & combo, status API key. Tombol *Cek ulang 9router* memaksa pengecekan. |
| Short memory | Terhubung atau tidak, jumlah short memory aktif, dan store yang dipakai (menu Setting → Penyimpanan memory). |
| Database | Terhubung atau tidak, jumlah long memory dan thread. |
| Pipeline node | Siap / belum lengkap, beserta catatannya. |

**Statistik**: thread hari ini (dan total), thread yang sedang berjalan, success rate 100 thread terakhir,
rata-rata speedup paralel, token hari ini (planner + executor + wrapper), jumlah project dan memory.

**Thread terbaru**: 25 thread terakhir — waktu & sumber (*CLI*, *Dashboard*, *Telegram*), project, prompt,
status, tugas berhasil/total, durasi, speedup, token. Klik baris untuk detail.

**Log langsung**: log terbaru di atas. Filter level (*Info+* default, *Semua (debug)*, *Warning+*, *Error*) dan
tombol *Jeda*. Log debug berisi panggilan tool planner dan request passthrough.

## Setup

Semua langkah awal di satu halaman (`/Setup`), jadi tidak perlu berpindah antara Setting, Node, Project, dan CLI. Status
tiap langkah dihitung dari kondisi AIT saat itu (bukan disimpan), sehingga halaman ini juga dipakai untuk memeriksa ulang:
*Cek ulang* memeriksa semuanya, dan halaman memeriksa lagi sendiri saat Anda kembali dari tab lain (mis. dashboard 9router).
Langkah wajib pertama yang belum selesai terbuka otomatis; begitu selesai, langkah itu dilipat dan langkah berikutnya dibuka.

| # | Langkah | Wajib | Selesai bila | Yang bisa dikerjakan di tempat |
|---|---|---|---|---|
| 1 | Database & short memory | Ya | database utama dan short memory terhubung | — (disiapkan otomatis saat start; tautan ke Instalasi/Troubleshooting) |
| 2 | Hubungkan 9router | Ya | 9router online, API key diterima (atau tidak diwajibkan), dan minimal satu model tersedia | *Jalankan 9router*, tautan ke dashboard 9router, Base URL + API key → **Test & simpan** (hanya disimpan bila koneksi dan key-nya benar) |
| 3 | Pilih model untuk tiap peran | Ya | pipeline siap (Planner, Executor, Wrapper aktif) | pilih **model kuat** dan **model murah** → **Buat node & uji** |
| 4 | Daftarkan project | Tidak | minimal satu project terdaftar | path manual, *Telusuri…* (penelusur yang sama dengan menu Project) |
| 5 | Arahkan CLI coding ke AIT | Ya | minimal satu CLI diarahkan ke AIT, global atau per project | switch **global** tiap CLI; per project lewat menu CLI |
| 6 | Memory bersama tim | Tidak | Long atau Business memory memakai database eksternal | *Import profil tim*, tautan ke Setting → Penyimpanan memory |

**Model (preset pipeline).** Dua pilihan model menggantikan pengisian node satu per satu. Pilihan awal ditebak dari nama model
(mis. `opus`/`fable` sebagai model kuat, `haiku`/`flash` dari provider yang sama sebagai model murah) dan bisa diganti.

| Node | Model | Default | Keterangan |
|---|---|---|---|
| `AIT Planner` | kuat | selalu | temperature 0.2, 16.384 token, 2 paralel |
| `AIT Executor` | murah | selalu | 0.1, 8.192 token, 6 paralel |
| `AIT Wrapper` | murah | selalu | 0.3, 4.096 token, 4 paralel |
| `AIT Tools Node` | murah | dicentang | planner mendelegasikan `read_file`, `search_text`, `find_references` ke node ini |
| `AIT Context Keeper` | murah | dicentang | meringkas percakapan CLI |
| `AIT Reviewer` | kuat | tidak dicentang | memeriksa hasil executor |
| `AIT Tester` | kuat | tidak dicentang | menilai unit test yang gagal setelah perubahan ([triase](07-cara-kerja.md#setelah-build)) dan menjalankan [Auto Testing](02-konfigurasi.md#auto-testing-node-tester); temperature 0.1, 4.096 token, 1 paralel. Wajib bila project memakai katalog unit test atau Auto Testing |

Semua node preset berprioritas 10, jadi dipakai lebih dulu daripada node buatan sendiri (default 100). Menjalankan preset lagi
memperbarui node `AIT …` yang sama (model, aktif); peran yang tidak dicentang menonaktifkan node preset-nya (tidak dihapus),
dan Tools Node yang dinonaktifkan melepas delegasi tool planner ke node itu. Node lain di menu Node tidak diubah. Setelah
disimpan, tiap node aktif diuji dengan satu panggilan kecil (*pong*) dan hasilnya tampil per baris.

**Profil setup tim.** Kartu di atas langkah-langkah: *Export profil* mengunduh file `ait-setup-<waktu>.json`; *Import profil…*
memilih file, menampilkan pratinjau perubahan per bagian, meminta secret yang dibutuhkan, lalu *Terapkan profil*. Lihat
[Profil setup tim](02-konfigurasi.md#profil-setup-tim).

## Detail thread

| Bagian | Isi |
|---|---|
| Header | Status, project, sumber, sesi CLI, waktu, working directory. Tombol *Batalkan thread* saat berjalan (juga saat testing); setelah selesai, *🔁 Ulangi yang gagal* dan *↩ Batalkan perubahan file* (lihat [Penggunaan](04-penggunaan.md#mengulang-tugas-yang-gagal)), serta *🧪 Ulangi testing* untuk thread yang punya rencana testing UI/API, dengan username & password opsional (lihat [Testing](07-cara-kerja.md#6-testing-auto-testing)). |
| Permintaan | Prompt Anda (tanpa konteks yang disisipkan CLI, mis. blok `<system-reminder>` Claude Code). |
| Rencana planner | Ringkasan dan tabel **kontrak**. |
| Tugas executor | **Timeline paralel** (batang yang overlap berjalan bersamaan) dan tabel tugas. Klik tugas untuk instruksi, ketergantungan, ringkasan, error, diff berwarna, dan output model live. |
| Verifikasi | Muncul bila build/reviewer/unit test berjalan. Hasil akhir (*Lolos* / *Masih ada masalah* / *Tidak dapat dipastikan*), perintah build, model reviewer, lalu per putaran: hasil build dan errornya, temuan reviewer, ringkasan unit test, masalah yang tidak bisa diperbaiki otomatis, dan id tugas perbaikan (`V1-1`, …) yang tampil juga di tabel tugas. |
| Unit test (katalog project) | Muncul bila unit test katalog dijalankan atau dipulihkan. Hasil (*Lolos* / *Gagal* / *Dilewati*), mode (atau *Hanya pengujian*), baseline (berapa dijalankan, berapa dari hasil sebelumnya, durasi), runner, lalu tabel per test: nomor, judul dan file, keputusan planner, hasil sebelum dan sesudah perubahan (*(cache)* = hasil sebelumnya untuk kode yang sama), verdict, pesan gagal atau alasan dilewati, dan verdict Tester. Di bawahnya test yang dipulihkan otomatis dan catatan. Hasil per putaran ada di kartu Verifikasi. |
| Testing | Muncul bila Auto Testing berjalan. Hasil (*Lolos* / *Gagal* / *Tidak bisa dicek* / *Dilewati*), level (UI/API), URL aplikasi, browser, model tester, username yang dipakai login, jumlah langkah, alasan bila tidak bisa berjalan, *🔐 Tester butuh login* bila tester berhenti meminta login, ringkasan tester, verdict per skenario, error browser, lalu satu jendela per `http_request` dengan URL, body request, dan body respons (jendela ke-4 dst. dilipat). |
| Hasil wrapper | Balasan yang diterima CLI. |
| Konteks sesi dari Context Keeper | Ringkasan percakapan sampai thread ini (khusus thread dari CLI) — titik awal planner di prompt berikutnya pada chat yang sama. Muncul beberapa detik setelah thread selesai. |
| Waktu & token | Model planner/reviewer/tester/wrapper, jumlah langkah planner, waktu planning / eksekusi nyata / eksekusi jika sekuensial / verifikasi / unit test / testing / wrapping / total, speedup, token per peran (token tester termasuk triase unit test). |
| Aktivitas | Log thread ini, termasuk setiap panggilan tool planner dan penolakan validator. |

## Project

Daftar codebase yang dikenal AIT. Satu project = satu folder root, yaitu folder yang berisi `.git` atau file build
(`.sln`, `pom.xml`, `package.json`, `go.mod`, dst.), apa pun bahasanya.

- **Registrasi otomatis**: prompt pertama dari sesi di dalam sebuah project mendaftarkan project-nya. Sesi di folder
  tanpa marker diabaikan (lihat [Penggunaan](04-penggunaan.md#sesi-di-luar-project)).
- **Telusuri…**: membuka penelusur folder tanpa perlu menyalin path:
  - pintasan lokasi: drive, *Home*, `source\repos`, dan folder induk project yang baru dipakai;
  - daftar folder — klik untuk masuk, *↑ Naik* untuk kembali, atau ketik path lalu Enter. Folder project bertanda
    stack-nya (mis. <kbd>.NET</kbd>, <kbd>Java (Maven)</kbd>, <kbd>Node.js</kbd>) atau <kbd>git</kbd>;
  - *Project di folder ini*: folder aktif bila ia sebuah root project, dengan status *Terdaftar* / *Baru*;
  - *Scan*: cari di semua sub-folder sampai kedalaman 2–8 (folder build, VCS, tersembunyi, dan junction dilewati;
    sebuah project tidak dipindai lebih dalam; berhenti di 300 hasil, 50.000 folder, atau 20 detik). *Pilih semua yang
    belum terdaftar* mencentang semua hasil baru;
  - centang satu atau beberapa project → **Daftarkan terpilih**.
  Folder terakhir yang dibuka diingat browser untuk kunjungan berikutnya.
- **Daftarkan manual**: tempel path folder → *Daftarkan*. Folder dengan marker sendiri didaftarkan apa adanya; path di
  dalam sebuah project mendaftarkan project itu; folder tanpa marker sama sekali tetap bisa didaftarkan (sesi di
  dalamnya lalu diorkestrasi dan punya memory). Drive, folder Home, dan folder sistem ditolak. Path file juga diterima
  (foldernya yang dipakai).
- Registrasi diperlukan sebelum menerapkan integrasi CLI per project.
- Kolom: nama & root (beserta **project key** 🔑), **AIT**, stack, jumlah thread, sesi CLI, memory,
  terakhir dipakai. Badge *folder hilang* muncul bila folder project sudah tidak ada; badge *key lokal — belum dibagi*
  berarti memory project ini belum bisa dibagi dengan developer lain (lihat [Project key](06-memory.md#berbagi-memory-antar-developer-project-key)).
- **Switch AIT**: menyala = `<project>/.claude/settings.local.json` mengarahkan Claude Code ke proxy ini. Menyalakan
  menulis blok `env` (sama dengan switch Claude Code per project di menu [CLI](#cli)); mematikan menghapus kunci AIT
  saja, kunci lain di file tetap. Catatan *URL lain* berarti file mengarah ke alamat proxy lain (mis. port lama) —
  nyalakan ulang untuk memperbarui. Buka sesi Claude Code baru setelah mengubahnya.
- **Hapus**: riwayat thread tetap ada (tanpa project). Memory yang hanya ada di mesin ini ikut terhapus. Memory di
  store bersama (database eksternal) tetap disimpan untuk developer lain, kecuali Anda mengiyakan konfirmasi kedua
  *hapus juga untuk SEMUA developer*. Bila clone lain dari repository yang sama masih terdaftar, memory-nya pindah ke
  clone itu.

**Detail project**: stack, kartu **Project key** (key efektif, sumbernya, dan isian *Key manual*), daftar file
solution/build yang ditemukan di dalam project, sesi CLI (id sesi, jumlah thread, terakhir aktif), thread
project ini, dan tautan ke memory-nya. Tabel sesi, thread, modul, peta kode, dan riwayat gabung modul tampil
10 baris per halaman (*‹ Sebelumnya* / *Berikutnya ›*; navigasi hanya muncul bila isinya lebih dari 10). Halaman yang
sedang dibuka tetap saat data diperbarui otomatis; mengganti pencarian atau filter peta kode kembali ke halaman 1.
Di bawah thread:

- **Peta kode**: modul, layer, area, dan sumber (aturan/AI/manual) tiap file, dengan pencarian dan filter modul.
  *Ubah* / *Atur file* (path atau nama class) menyimpan koreksi manual; *Scan* memetakan file yang belum terpetakan.
- **Riwayat gabung modul**: merge otomatis dan manual beserta jumlah memory/file/halaman yang dipindah; *Batalkan*
  mengembalikannya.

Di bawah kedua kolom, selebar halaman:

- **Modul**: daftar modul project (deskripsi, alias, jumlah memory dan file; klik jumlah file untuk memfilter peta kode). *Atur* mengganti nama (termasuk huruf
  besar/kecil), mengubah deskripsi/alias, atau menggabungkan modul ke modul lain; *Cek modul ganda* mencari modul yang terpisah tidak sengaja. Lihat
  [Daftar modul & gabung modul](06-memory.md#daftar-modul--gabung-modul).

### Unit test

Kartu **Unit test** (di bawah Modul) menampilkan [katalog unit test](07-cara-kerja.md#unit-test) project ini. Katalog
dan setting-nya dipakai bersama semua developer project (lewat project key) dan disimpan bersama long memory.

- Baris info: mode, aturan test baru, runner yang dikenali beserta masalahnya (mis. *jalankan npm install dulu*),
  dan peringatan bila belum ada node Tester aktif.
- Tabel 15 baris per halaman: nomor `UT-…`, judul dan file test (alasan obsolete/karantina di bawahnya), modul,
  status (*Aktif* / *Obsolete* / *Karantina* / *Dihapus*), hasil terakhir (kapan, oleh siapa, durasi, potongan pesan
  gagal atau alasan dilewati), dan asal (*Dibuat AIT* / *Scan kode* / *Ditulis ulang*). Cari nomor/judul/file, filter
  status (termasuk *Gagal terakhir*) dan modul.
- Klik judul untuk detail: tujuan, skenario, target, kebutuhan, selector, thread asal, dan riwayat run (sebelum/setelah
  perubahan, manual, tidak stabil; mesin dan developer). Isi file test (snapshot) tidak pernah ditampilkan.
- **Scan test**: mencari test di kode, mendaftarkan yang baru, mengikuti yang pindah file, dan memberi judul, tujuan,
  dan skenario dengan model murah. **Jalankan semua** / **Jalankan** per test: menjalankan sekarang (termasuk test
  karantina yang dipilih) dan mencatat hasilnya ke katalog.
- Test *Obsolete*: **Kembalikan** (aktif lagi) atau **Konfirmasi hapus** — status menjadi *Dihapus*, snapshot ikut
  dihapus, test tidak pernah dipulihkan lagi, dan sebuah thread Dashboard menghapus kode test-nya (bisa di-rollback
  dari halaman thread itu). Test *Karantina*: **Aktifkan**.
- **Setting**: *Test yang dijalankan tiap thread* (*Relevan saja* — default, *Semua test aktif*, *Mati*), *Buat unit
  test baru* (*Hanya saat diminta* — default, *Otomatis untuk perubahan logika bisnis*, *Tidak pernah*), *Simpan
  snapshot test* (default menyala; mematikannya menghapus snapshot yang ada), dan *Batas waktu run test* (10–3600
  detik; kosong = ikut *Timeout build*).
- Kartu diperbarui sendiri saat thread project ini selesai.

### Akun uji

Kartu **Akun uji** (di bawah Unit test) berisi akun yang dipakai node Tester untuk [berganti user](07-cara-kerja.md#akun-uji-berganti-user)
saat testing, misalnya pengaju, atasan, approver per level, dan admin. Akun dibagi ke semua developer project lewat
project key; tanpa project key kartu menjelaskan bahwa akun belum bisa disimpan. Semua developer project boleh mengubahnya.

- Tabel: username, peran, atribut (`NIK: 123`, `Atasan: atasan01`, …), catatan, dan siapa yang terakhir mengubah.
  **Tambah akun** / **Ubah** membuka form dengan atribut dinamis; akun baru diawali kunci atribut yang sudah dipakai akun
  lain di project, supaya kolomnya konsisten. Pakai akun uji, bukan akun karyawan sungguhan.
- **Password dev**: kosong = bawaan `Test@1234`. Nilai yang diisi (di sini atau lewat balasan chat `password dev <nilai>`)
  terlihat oleh semua developer project, jadi hanya untuk password bypass di dev.
- **Login as / impersonate**: hasil deteksi (tersedia/tidak ada/belum diketahui, cara, cara kembali, peran, dasar
  temuan file:baris, sumber, dikonfirmasi atau belum). **Deteksi** menjalankan scan kode + model murah lagi; **Ubah**
  mengisi atau mengoreksi sendiri dan bisa menandai *sudah dicek*.

### Skenario test

Kartu **Skenario test** (di bawah Akun uji) berisi [skenario multi-langkah](07-cara-kerja.md#skenario-multi-langkah-excel)
project: judul, nama file (slug), versi, kolom form, jumlah langkah, peran beserta akunnya (yang belum diisi ditandai),
hasil terakhir, dan tautan ke thread asalnya.

- **Excel** mengunduh file skenario untuk diisi; **Unggah Excel** membaca file yang sudah diisi (baris yang salah
  ditampilkan per nomor baris; username baru menjadi akun uji project).
- **Jalankan** (aktif bila semua peran punya akun) meminta konfirmasi efek samping, lalu membuka halaman thread asalnya
  tempat kemajuannya tampil. **Hapus** menghapus skenario untuk semua developer project.

### Struktur kode

Paling bawah, jendela **Struktur kode** menampilkan isi project tanpa compile dan tanpa dependensi tambahan. Data
yang sama dipakai planner: tool `code_outline` memberi kerangka file/folder/class sebelum planner membaca file, dan file
yang disebut prompt langsung di-outline di brief planner (lihat [Cara kerja](07-cara-kerja.md#3-planner)):

- **Folder**: folder → file → class/type → member (method, constructor, property, event) dan fungsi di luar class.
  **Class**: namespace (atau folder, untuk file tanpa namespace seperti PHP lama dan JavaScript biasa) → class →
  member. Class `partial` C# yang tersebar di banyak file tampil sebagai satu class; daftar file-nya terlipat di
  *Dideklarasikan di N file*.
- Klik baris deklarasi untuk menyalin `path:baris` (bisa langsung ditempel ke prompt). Pencarian mencari nama class,
  member, dan file (100 hasil teratas). Pilihan Folder/Class diingat browser.
- **Panggilan function** (`N panggilan`): apa saja yang dipanggil sebuah method/function, mis. `p_InitForm` →
  `p_InitiateDetail`, `p_initiateData`; `p_initiateData` → `POST /Transaction/KlaimPending/InitiateData` →
  `KlaimPendingController.InitiateData`. Klik function untuk membuka daftarnya; tiap panggilan menunjuk ke file dan
  baris deklarasinya (klik untuk menyalin), atau tampil redup *belum ditemukan di project*. Lihat
  [Panggilan function](07-cara-kerja.md#panggilan-function) untuk kapan dan bagaimana dibaca.
- C# dibaca dengan Roslyn (syntax saja, tepat). PHP, JavaScript/TypeScript, Python, dan Java/Kotlin dibaca per baris
  dengan pola, dan isi class ditentukan dari indentasi. Cocok untuk kode yang rapi; closure dan class anonim tidak
  dihitung. File lain tampil sebagai file saja.
- Tidak dibaca (ditandai *pihak ketiga*): lokasi yang terdaftar di `thirdpartylibs.xml` (Moodle dan plugin-nya),
  `*.min.js`/`*.min.css`/`*.bundle.js`, folder `amd/build` dan `yui/build`, file di atas 2 MB, dan JavaScript hasil
  minify. Folder yang diabaikan di seluruh AIT (`bin`, `obj`, `node_modules`, `vendor` untuk PHP, dll.) tidak tampil.
- Project dipindai saat jendela dibuka, lalu lagi bila hasil terakhir lebih dari 1 menit atau saat *Scan ulang*.
  Selain itu, setiap thread yang selesai mengubah file (juga thread yang dibatalkan/gagal di tengah, dan rollback)
  memindai ulang project-nya di latar belakang tanpa menahan thread. Antreannya satu per project: permintaan baru
  membatalkan pemindaian sebelumnya (yang masih menunggu atau sedang berjalan) dan mengambil alih, dengan jeda 1 detik
  agar thread dari beberapa CLI yang selesai hampir bersamaan berakhir dalam satu pemindaian.
  File yang ukuran dan waktu ubahnya sama memakai hasil sebelumnya, jadi pindai ulang biasanya kurang dari 1 detik
  (scan pertama ±1 detik untuk ±1.100 file C#, ±2–4 detik untuk ±14.000 file PHP/JS). Hasil disimpan di memori server
  untuk 4 project yang terakhir dibuka; maks 100.000 file per project.

## Memory

Lihat [Memory](06-memory.md) untuk konsepnya. Halaman dibagi tiga tab: **Short & long memory**, **Perlu review**
(`/Memory#review`), dan **Bisnis (wiki)** (`/Memory#bisnis`). Selama masih ada memory lama yang menunggu label, banner
*Migrasi label memory* menampilkan progres (menunggu, sudah berlabel, perlu review, fase beserta project dan jumlah
file yang sedang dipetakan, lokasi backup) dengan tombol
*Jalankan sekarang*.

Tab *Short & long memory*:

- **Statistik**: jumlah short & long memory, project & knowledge, belum berlabel (dan yang perlu review), status store
  short memory.
- **Filter**: level, tipe, project, pencarian label/deskripsi, modul (project terpilih + knowledge), layer, jenis,
  berlabel/belum. Datang dari halaman project? Filter project sudah terisi.
- **Tabel**: level (short menampilkan perkiraan sisa jam sebelum pindah ke DB), tipe, project, label
  sebagai satu tag bersegmen `Modul | Modul lain | Layer | Jenis` diikuti `#keyword` (memory lama: tanda *belum
  berlabel* + label lamanya) dan cuplikan
  deskripsi, waktu dibuat & tautan thread sumber, jumlah dipakai (long memory).
- **Aksi**: *Buka* (lihat/ubah), *→ DB* (pindahkan short ke long sekarang), *Hapus*.
- **Tambah memory / Buka**: tipe, project, modul (dropdown daftar modul; *＋ Modul baru…* menambah nama baru yang
  dibuat saat disimpan; *Ubah nama* mengganti nama modul terpilih di semua memory, peta kode, dan wiki), modul lain
  (opsional, maks 3, dipilih dari dropdown yang sama dan tampil sebagai chip), jenis, area (opsional), layer (centang),
  keyword (dipisah koma, maks 16, dihitung), deskripsi. Knowledge memory tidak terikat project dan hanya boleh jenis
  Pitfall/Pattern/Architecture. Entri baru masuk sebagai short memory.
- **Konsolidasi sekarang**: memindahkan semua short memory yang sudah melewati masa simpan.

Tab *Perlu review*:

- **Memory tanpa label**: memory yang tidak bisa dilabeli otomatis (modul tidak jelas, atau gagal 3 kali) —
  *Beri label* membuka form.
- **Saran gabung modul**: modul yang kemungkinan sama tapi keyakinannya rendah — *Gabungkan* atau *Bukan duplikat*
  (tidak diusulkan lagi). Pilih project untuk melihatnya.
- **Kandidat memory duplikat**: pasangan memory dengan modul & jenis sama dan keyword sangat mirip, berdampingan
  dengan tombol *Buka* dan *Hapus*. Tidak ada yang dihapus otomatis.

Tab *Bisnis (wiki)* — lihat [Business memory](06-memory.md#business-memory-wiki-bisnis):

- **Project**: pilih project; baris info menampilkan project key, jumlah thread yang menunggu dirangkum, waktu
  rangkuman terakhir, dan store yang dipakai.
- **Modul**: daftar halaman dikelompokkan per modul, dengan pencarian. 🔒 = halaman dikurasi manual (tidak diubah AI).
- **Halaman**: isi wiki (tabel, aturan bernomor, screenshot), versi, siapa yang terakhir mengubah (AI/manual).
  *Diperbarui AI* mengunci/membuka halaman dari penulis wiki AI; *Riwayat* menampilkan semua versi dan bisa
  memulihkan salah satunya; *Edit* dan *Hapus*.
- **Halaman baru / Edit**: modul, judul, ringkasan, label, isi Markdown dengan *Pratinjau*. Tempel screenshot
  (Ctrl+V) atau *Sisipkan gambar* — gambar diunggah ke store dan disisipkan di posisi kursor. Bila halaman sudah diubah
  orang lain sejak dibuka, penyimpanan ditolak (editor tetap terbuka) supaya perubahan siapa pun tidak tertimpa.
- **Perbarui sekarang**: merangkum thread project ini yang belum masuk wiki tanpa menunggu jadwal.

## Node

Tujuh kolom: Planner, Tools Node (opsional), Executor, Reviewer (opsional), Tester (opsional), Wrapper, Context Keeper (opsional). Setiap kartu menampilkan model, temperature, max token, max paralel,
prioritas, penanda *📝 instruksi* bila ada instruksi tambahan (isinya di tooltip), penanda *🧩 n* bila node memakai n skill
(namanya di tooltip), penanda *🛰 n tool* di planner yang mendelegasikan tool ke [tools node](02-konfigurasi.md#tools-node-planner)
(di kartu tools node: *🛰 n planner* yang memakainya; detail di tooltip), penanda *🧪 auto test* di planner yang menyalakan
[Auto Testing](02-konfigurasi.md#auto-testing-node-tester) (*🧪 tanpa tester* bila belum ada node Tester aktif), penanda *↪ nama* bila node punya [node cadangan](02-konfigurasi.md#node-cadangan), dengan tombol *Test*, *Edit*, *Hapus*.
Di modal node Planner, bagian *Delegasi tool ke tools node* berisi satu pilihan per tool baca (*Planner langsung* atau tools node), dan switch *Auto Testing* (dengan nama tester yang akan dipakai). Di modal node, bagian *Skill* menampilkan skill terpasang
sebagai chip. Kolom *Cari sendiri di gudang* membuka dropdown skill yang cocok dengan nama, deskripsi, tag, sumber,
atau isi skill (potongan isi yang cocok ditampilkan); klik item untuk memasang/melepas. Kolom *Butuh kemampuan apa?* + **Cari skill** meminta Skill Orchestrator mencarikan skill yang cocok
dari gudang (lihat [Skill pada node](07-cara-kerja.md#skill-pada-node)).
Bila ada project dengan katalog unit test tetapi belum ada node Tester aktif, status pipeline menampilkan peringatan
*Ada katalog unit test, tetapi belum ada node Tester aktif* (unit test yang gagal lalu diperbaiki di kode tanpa triase).
Panel *Panduan* di atas kolom menjelaskan arti angka-angka tersebut; panel bisa dilipat dan keadaannya diingat browser. *Muat ulang model 9router* menyegarkan daftar
model (cache 60 detik). Detail field ada di [Konfigurasi](02-konfigurasi.md#menu-node).

## CLI

Mengarahkan CLI coding ke proxy AIT dengan satu switch, tanpa menyunting file konfigurasi CLI itu sendiri. Kartu yang
tersedia: **Claude Code**, **oh-my-pi (omp) — provider AIT** (`models.yml`), **Codex CLI — profile AIT**
(`config.toml`, dipakai lewat `codex --profile aithread`), **Gemini CLI** (`.gemini/.env`), **opencode — provider
AIT** (`opencode.json`), dan **Qwen Code — model AIT** (`settings.json`). Kartu "provider/profile/model AIT" hanya
global dan tidak mengalihkan sesi lain: CLI itu mendapat pilihan `aithread` di samping provider aslinya.

- **URL proxy AIT** di bagian atas: alamat yang ditulis ke konfigurasi CLI (default = alamat http aplikasi).
  Mengubahnya menyegarkan status semua switch.
- Tiap CLI punya satu kartu berisi:
  - **Global** — file konfigurasi CLI itu di level pengguna (`~/.claude/settings.json` untuk Claude Code,
    `~/.omp/agent/models.yml` untuk omp, `~/.codex/config.toml`, `~/.gemini/.env`, `~/.config/opencode/opencode.json`,
    `~/.qwen/settings.json`). Menyalakan Claude Code atau Gemini CLI global meminta konfirmasi karena semua sesinya
    ikut lewat AIT.
  - **Per project** — satu switch per project terdaftar (`<project>/.claude/settings.local.json` untuk Claude Code,
    `<project>/.gemini/.env` untuk Gemini CLI; CLI lain hanya global). Lencana *Aktif*, *Nonaktif*, atau *Mengarah ke alamat lain* (file menunjuk URL lain, mis. port
    lama — matikan lalu nyalakan lagi).
  - *Yang akan ditulis*: pratinjau isi yang akan dituliskan.
- Mematikan switch hanya menghapus yang ditulis AIT; isi lain di file tetap. Backup file asli disimpan sekali
  sebagai `*.aithread-backup`. AIT menolak menulis (dengan pesan) bila file sudah berisi entri bernama sama buatan
  Anda, atau JSON opencode/Qwen Code berisi komentar.
- Kartu Claude Code juga memuat **Context Keeper untuk /clear** (*Pasang hook* / *Lepas hook*; lihat
  [Integrasi CLI](03-integrasi-cli.md#context-keeper-untuk-clear)).
- Bila *Proxy API key* di Setting diubah, matikan lalu nyalakan lagi agar key baru ikut tertulis.

Detail tiap CLI: [Integrasi CLI](03-integrasi-cli.md).

## Setting

Halaman dibagi menjadi tab. Tab yang terbuka tercatat di URL (mis. `/Setting#telegram`), jadi tetap sama setelah
refresh dan bisa ditautkan langsung.

- **Koneksi 9router** (`#router`) — URL, API key, *Test koneksi* (tidak menyimpan); baris status menunjukkan apakah key tersimpan dan diterima.
- **Proxy & orkestrasi** (`#proxy`) — saklar orkestrasi, progres & *Planner boleh bertanya*, model passthrough, proxy API key (beserta tautan ke
  menu [CLI](#cli), tempat integrasi CLI (Claude Code, omp, Codex, Gemini CLI, opencode, Qwen Code) dan hook Context Keeper diatur), batas planner/executor/paralel/timeout.
- **Build & verifikasi** (`#build`) — *Build & verifikasi otomatis*, timeout build, dan jumlah putaran perbaikan.
- **Penyimpanan memory** (`#memory`) — provider short, long, dan business memory (bawaan, Redis untuk short saja,
  SQL Server, PostgreSQL, MySQL, Oracle, SQLite) beserta host/port/database/username/password, *Test koneksi* per kartu,
  status live, dan opsi menyalin memory lama. Disimpan terpisah lewat **Simpan penyimpanan memory**. Di bawahnya,
  kartu **Business memory**: perbarui otomatis, interval (jam), model penulis wiki, dan nama developer — disimpan lewat
  **Simpan business memory**.
- **Telegram** (`#telegram`) — token bot, aktif/nonaktif, status koneksi live, dan tabel chat (*Izinkan* / *Hapus* / *Cabut*).
- **Skill** (`#skills`) — model Skill Orchestrator, batas karakter skill per node, sumber skill (tempel link →
  **Tarik skill**, status live, *Sync* / *Hapus*), dan gudang skill dengan pencarian serta pratinjau isi. Disimpan
  terpisah lewat **Simpan Skill**.
- **Keamanan & privasi** (`#keamanan`) — saklar penyamaran secret dan penyamaran request passthrough, hitungan sejak AIT
  berjalan (disamarkan, bisa dipasang kembali, dipasang kembali, akses file kredensial ditolak), contoh aturan
  `.aithread/project.json`, dan tombol untuk membuka penjelasan lengkap. Disimpan lewat **Simpan setting keamanan**
  ([Keamanan & privasi](12-keamanan.md)).

Tiga tab pertama adalah satu form: **Simpan setting** di tab mana pun menyimpan isi ketiganya sekaligus, dan field
yang tidak valid membuka tabnya sendiri. Tab Telegram, Skill, dan Keamanan & privasi disimpan terpisah lewat tombolnya
masing-masing.

## Keamanan & privasi (popup)

Saat AIT pertama kali dibuka, popup **Keamanan & privasi** menjelaskan apa yang dilakukan AIT terhadap secret dan data
Anda: penyamaran sebelum ke model, pemulihan saat menulis file, file kredensial yang diblokir, apa yang disimpan dan
dikirim, serta batasannya. Popup ini hanya muncul sekali per instalasi (dicatat di database, bukan di browser), dan bisa
dibuka lagi lewat tombol **Keamanan & privasi** di grup Bantuan sidebar.

Detail di [Konfigurasi](02-konfigurasi.md) dan [Prompt dari Telegram](11-telegram.md).

## Docs

Dokumentasi ini, ditampilkan di aplikasi. Kolom kiri berisi daftar dokumen (dengan pencarian judul) dan daftar
isi halaman yang sedang dibuka; tombol di bawah halaman berpindah ke dokumen sebelum/berikutnya. Tautan antar-dokumen
dan anchor (mis. `#menu-node`) bekerja seperti di GitLab. Isi diambil dari folder `docs/` — perubahan file langsung
terlihat setelah refresh.
