# 6. Memory

Memory membuat planner makin paham konteks bisnis dan teknis project dari waktu ke waktu, tanpa Anda mengulang
penjelasan.

## Isi satu memory

| Bagian | Fungsi |
|---|---|
| **Label** | `[Area ·] Modul · Layer · Jenis`, mis. `User · Logic · Rule`; memory yang membahas beberapa modul: `Order+Invoice · Logic · Flow`. Lapis pertama pencarian: planner menyaring berdasarkan modul (dan bila perlu layer/jenis). |
| **Keyword** | Maks 16 kata, mis. `Description, Deskripsi, UserValidator`. Lapis kedua: di dalam hasil lapis pertama, mana yang memuat istilah yang dicari. |
| **Deskripsi** | Penjelasan mandiri berupa baris-baris berlabel sesuai jenis (lihat [Format deskripsi](#format-deskripsi)), maks ±700 karakter. Untuk project memory berisi path file dan identifier dalam backtick. Hanya dibaca setelah lolos dua lapis. |

Semua bagian label dan keyword ditulis **PascalCase**, dinormalisasi oleh AIT (bukan dipercayakan ke model):
`user dto` → `UserDTO`, `api client` → `APIClient`, `users` → `Users`. Singkatan ditulis kapital semua: daftar bawaan
(API, DTO, UI, SQL, DB, ID, URL, JSON, …) ditambah kata 2–3 huruf yang memang ditulis kapital (`TO`, `PO`, `SKU`); daftar bisa
ditambah lewat `AIThread:MemoryAcronyms`. Identifier dari kode tetap seperti aslinya (`UserDto`, `UserBL`), hanya huruf
pertamanya dibesarkan.

**Nama modul** sering berupa singkatan, jadi bagian yang ditulis kapital selalu dipertahankan, berapa pun panjangnya:
`MKPP` tetap `MKPP` (singkatan penuh), `KlaimDF` / `Klaim DF` → `KlaimDF` (PascalCase + singkatan). Yang ditulis huruf
kecil semua hanya diberi huruf besar di depan (`klaimdf` → `Klaimdf`), karena AIT tidak bisa menebak singkatannya. Nama
yang kurang tepat diperbaiki lewat [ganti nama modul](#daftar-modul--gabung-modul).

### Format deskripsi

Deskripsi ditulis per baris. Tiap baris diawali label tetap sesuai jenis, dalam bahasa developer. Bagian yang kosong boleh dilewati.

| Jenis | Urutan label |
|---|---|
| Pitfall | Masalah · Penyebab · Solusi · Lokasi |
| Rule | Aturan · Ditegakkan di · Pengecualian |
| Flow | Pemicu · Langkah (bernomor) · Hasil · Lokasi |
| Convention | Konvensi · Contoh · Alasan |
| Architecture | Komponen · Hubungan · Alasan |
| Pattern | Masalah · Solusi · Kapan dipakai |

- Identifier, path, key konfigurasi, nilai, dan pesan ditulis dalam backtick.
- Sebut nama, bukan "ini" atau "bug tadi".
- Tulis faktanya, bukan cerita thread.
- Knowledge memory tanpa path atau identifier project.

Contoh (Rule):

```text
Aturan: `TokenBadge` selalu lebar 24 kolom agar `[` dan `]` sejajar.
Ditegakkan di: `src/AIThread.Service/Orchestration/ClientProgress.cs`, `WithBadge`.
Pengecualian: badge tidak muat di sisa baris, pindah utuh ke baris baru rata kanan.
```

Memory lama tetap dengan format bebas. Format ini berlaku untuk memory yang diekstrak setelahnya.

### Modul

Fitur atau entitas bisnis (`User`, `Invoice`, `TransferOrder`), sama untuk semua layer fitur itu: `UserController`,
`UserBL`, `UserRepository`, dan `Views/User/Index.cshtml` semuanya modul `User`. Untuk knowledge memory, modulnya adalah
teknologi atau domain (`EFCore`, `Redis`, `Auth`). Modul diambil dari [daftar modul](#daftar-modul--gabung-modul) project
yang sama dengan modul wiki bisnis, dan untuk project memory diturunkan dari [peta kode](#peta-kode) file yang dibahas.

Satu memory punya satu **modul utama** dan boleh punya sampai 3 **modul lain** bila fakta yang sama memang membahas
beberapa modul (mis. alur posting `Order` yang membuat `Invoice`). Filter modul di menu Memory, pencarian planner, peta
memory, dan jumlah memory per modul ikut menghitung modul lain itu. Modul utama tersimpan di kolom `Module` (dibaca juga
oleh AIT versi lama), modul lain di kolom `ExtraModules`.

### Layer

Peran file di codebase-nya sendiri, bukan posisinya di FE atau BE. Satu memory boleh punya beberapa layer (memory alur).

| Layer | Peran | Contoh |
|---|---|---|
| `UI` | Tampilan & interaksi | `.cshtml`, component React/Vue, CSS, `pages/*.js` |
| `Entry` | Pintu masuk request/perintah | controller, route, handler, CLI command, consumer |
| `Logic` | Aturan & proses bisnis, klien keluar | service, BL, validator, store, API client |
| `Data` | Penyimpanan & bentuk data | repository, DbContext, migration, query, entity, DTO |
| `Infra` | Wiring, konfigurasi, build | `Program.cs`, DI, `appsettings`, Dockerfile, CI |
| `Test` | Pengujian | `*Tests.cs`, `*.spec.ts` |

### Jenis

Macam fakta yang disimpan. Ditentukan dengan pertanyaan **berurutan** — jawaban "ya" pertama yang dipakai, supaya kasus
ambigu selalu jatuh ke jenis yang sama:

1. Kesalahan atau jebakan yang harus dihindari (beserta solusinya)? → `Pitfall`
2. Syarat bisnis yang dipaksakan sistem (boleh, wajib, dihitung, divalidasi)? → `Rule`
3. Urutan langkah lintas komponen atau waktu? → `Flow`
4. Cara developer menulis kode di project ini? → `Convention`
5. Susunan/hubungan komponen atau alasan desain? → `Architecture`
6. Selain itu → `Pattern`

Knowledge memory hanya boleh `Pitfall`, `Pattern`, atau `Architecture`. Satu memory satu jenis; fakta dengan dua jenis
dipecah jadi dua memory.

### Area

Aplikasi di repository yang berisi beberapa aplikasi (mis. `apps/web` React dan `apps/api` .NET): folder manifest
terdekat, tanpa awalan yang sama di semua area. Hanya diisi bila manifest-nya berbeda stack atau ada beberapa aplikasi di
`apps/`, `packages/`, `services/`, `projects/`; solusi .NET berisi beberapa project tetap satu aplikasi tanpa area.

## Tipe

| Tipe | Isi | Cakupan |
|---|---|---|
| **Project** | Aturan bisnis, validasi, alur data, arsitektur, konvensi, lokasi kode. | Project asalnya — semua clone-nya, di mesin developer mana pun (lihat [project key](#berbagi-memory-antar-developer-project-key)). |
| **Knowledge** | Pola teknis umum yang berguna di project lain, mis. "cache data master di Redis, refresh tiap 5 menit". | Semua project. |
| **Business** | Wiki bisnis per modul: requirement, aturan bisnis, status & alur, validasi, screenshot. Ditulis untuk pembaca non-teknis. | Project asalnya (per project key). Lihat [Business memory](#business-memory-wiki-bisnis). |

## Dua level

| Level | Penyimpanan | Umur |
|---|---|---|
| **Short** | Default: Garnet di dalam AIT (protokol Redis, tanpa TTL). Bisa diganti — lihat [Penyimpanan memory](#penyimpanan-memory). | Sampai berumur `ShortMemoryHours` (default 24 jam). |
| **Long** | Default: tabel `LongMemories` di database utama. Bisa diganti — lihat [Penyimpanan memory](#penyimpanan-memory). | Permanen sampai dihapus. |

```
thread selesai ─► ekstraksi (model wrapper) ─► short memory (Garnet)
                                                    │  worker konsolidasi, tiap 10 menit:
                                                    │  umur > 24 jam ─► long memory (DB, id sama)
                                                    ▼
                                             long memory (database)
```

- Memory dipindah oleh worker, bukan oleh kedaluwarsa (TTL) — jadi tidak ada yang hilang walau aplikasi mati saat
  sebuah memory berumur 24 jam. Id tetap sama sehingga pemindahan aman diulang.
- Short memory bertahan saat AIT di-restart atau crash: Garnet mencatat setiap penulisan ke disk
  (`%LOCALAPPDATA%\AIThread\garnet\<port>`) sebelum dikonfirmasi.
- **Short memory tidak tersedia** (mis. Garnet gagal start, atau server short memory eksternal mati): memory baru
  langsung disimpan sebagai long memory (tercatat di log sebagai peringatan). Store database eksternal dicek ulang
  tiap 15 detik dan otomatis dipakai lagi begitu kembali hidup.
- **Long memory tidak tersedia** (mis. server PostgreSQL eksternal mati): thread tetap berjalan, planner hanya tidak
  melihat long memory. Log aktivitas mencatat `Long memory (…) tidak dapat dihubungi` paling sering sekali per 10
  menit. Konsolidasi menunda pemindahan: short memory yang sudah jatuh tempo tetap di short memory sampai database
  kembali, dan statistik menu Memory menampilkan store long memory sebagai *tidak terhubung*.
- Short memory dari project yang sudah dihapus berubah menjadi knowledge memory saat dipindah.

## Berbagi memory antar developer (project key)

Folder root project berbeda di tiap mesin (`D:\a\eep` di laptop Andi, `C:\src\eep` di laptop Budi), jadi memory
tidak diikat ke folder, melainkan ke **project key** yang sama untuk semua clone sebuah repository:

1. file `.aithread/project.json` di root repository, mis. `{ "key": "eep" }` — di-commit tim, selalu menang (file yang
   sama bisa memuat `"run"`, perintah menjalankan aplikasi untuk testing; lihat
   [Menjalankan aplikasi](07-cara-kerja.md#menjalankan-aplikasi));
2. **key manual** di menu Project → detail project (untuk repository tanpa remote);
3. **git remote** `origin` (atau remote pertama), dinormalisasi: `https://user:token@gitlab.com/Org/EEP.git`,
   `git@gitlab.com:org/eep.git`, dan `ssh://git@gitlab.com:22/org/eep` semuanya menjadi `gitlab.com/org/eep`
   (kredensial tidak pernah ikut). Worktree dan submodule (`.git` berupa file) mengikuti repository utamanya;
4. tanpa semua itu: key **lokal** (`local:…`, dari path folder) — memory hanya ada di mesin ini. Menu Project
   menandainya *key lokal — belum dibagi*.

AIT membaca file itu langsung (tidak menjalankan `git`) dan menghitung ulang key setiap kali project terlihat,
juga sekali saat start. Supaya long memory dari semua developer terkumpul di satu tempat, arahkan **Long memory**
(dan **Business memory**) semua developer ke database yang sama di Setting → Penyimpanan memory. Short memory boleh
tetap lokal: setelah 24 jam ia pindah ke long memory bersama.

- Memory baru menyimpan key dan nama project-nya, sehingga tetap bernama walau project itu belum terdaftar di mesin
  Anda. Di menu Memory, memory dari developer lain ditampilkan atas nama clone project yang sama di mesin Anda.
- Memory dari versi lama (tanpa key) otomatis diberi key project-nya saat AIT start. Di store bersama, setiap
  developer mengisi baris miliknya sendiri, jadi lama-lama semuanya terkunci ke key.
- Saat key project berubah dari **lokal** menjadi key bersama (remote, file, atau key manual ditambahkan), memory-nya
  ikut pindah. Memory di bawah key bersama tidak pernah dipindah, karena clone developer lain mungkin masih memakainya.
- **Menghapus project**: memory yang hanya ada di mesin ini ikut terhapus; memory di store bersama tetap disimpan untuk
  developer lain, kecuali Anda mengiyakan *hapus juga untuk SEMUA developer*. Bila clone lain dengan key yang sama
  masih terdaftar di mesin ini, memory pindah ke clone itu.
- Daftar modul, peta kode, dan [katalog unit test](07-cara-kerja.md#unit-test) project mengikuti aturan yang sama:
  ikut pindah saat key berubah dari lokal ke key bersama, dan ikut terhapus bersama memory-nya.

## Penyimpanan memory

Menu **Setting → Penyimpanan memory** (`/Setting#memory`) menentukan di mana tiap level disimpan. Perubahan langsung
berlaku tanpa restart.

| Provider | Short | Long | Business | Field yang dipakai |
|---|---|---|---|---|
| Bawaan | Garnet di dalam AIT (`ConnectionStrings:Redis`) | Database utama (`ConnectionStrings:Default`) | Database utama | — |
| Redis | ✓ | — | — | host, port (6379), database = indeks (opsional), username (ACL Redis 6+), password |
| SQL Server | ✓ | ✓ | ✓ | host, port (1433), database, username/password — username kosong = Windows authentication |
| PostgreSQL | ✓ | ✓ | ✓ | host, port (5432), database, username, password |
| MySQL | ✓ | ✓ | ✓ | host, port (3306), database, username, password |
| Oracle | ✓ | ✓ | ✓ | host, port (1521), database = service name (mis. `FREEPDB1`), username, password |
| SQLite | ✓ | ✓ | ✓ | database = path file (kosong = `%LOCALAPPDATA%\AIThread\memory.db`) |

- **Opsi tambahan** ditambahkan ke connection string dan menimpa field di atas, mis. `SSL Mode=Require` (PostgreSQL),
  `Encrypt=True` (SQL Server), atau `ssl=true` (Redis, dipisah koma). Keyword yang tidak dikenal ditolak saat disimpan.
- **Test koneksi** memakai isi form tanpa menyimpan. Password kosong = memakai password tersimpan.
- Saat **disimpan**, AIT menghubungi store baru dan membuat database (bila belum ada dan user punya hak) serta
  tabel `ShortMemories`/`LongMemories`, atau `BusinessPages`/`BusinessPageRevisions`/`BusinessAssets` untuk business
  memory. Daftar modul, peta kode, dan riwayat gabung modul (`ProjectModules`, `CodeMapEntries`, `ModuleMergeLogs`)
  ikut tinggal di database long memory, jadi store bersama juga berbagi daftar modulnya. Begitu juga
  [katalog unit test](07-cara-kerja.md#unit-test) (`UnitTestCases`, `UnitTestRuns`, `UnitTestSettings`): developer yang
  memakai store bersama menjalankan test yang sama. Store yang tidak bisa dihubungi
  ditolak, jadi setting lama tetap berlaku. Database eksternal tidak memakai migrasi EF; tabel dibuat sendiri pada
  pemakaian pertama, dan tabel dari versi lama otomatis mendapat kolom baru (`ProjectKey`, `ProjectName`, kolom label
  `Module`, `Layers`, `Kind`, `Area`, `Keywords`, `LabelVersion`, `LegacyLabel`, `ReviewReason`, `RelabelAttempts`) —
  user perlu hak `ALTER TABLE` sekali itu.
- **Salin memory yang ada** (default aktif) menyalin memory dari store lama ke store baru berdasarkan id, jadi aman
  diulang dan tidak menggandakan. Katalog unit test ikut disalin bersama long memory. Store lama tidak dihapus atau diubah. Tanpa salinan, memory lama tidak terlihat
  sampai provider dikembalikan.
- Password disimpan terenkripsi (ASP.NET Core Data Protection) dan tidak pernah dikirim balik ke browser.
- Short, long, dan business memory boleh memakai database yang sama; masing-masing punya tabel sendiri.
- Long memory di database eksternal tidak punya foreign key ke `Projects`: memory diikat ke project key dan menyimpan
  nama project-nya sendiri. Lihat [Berbagi memory antar developer](#berbagi-memory-antar-developer-project-key) untuk
  apa yang terjadi saat project dihapus.
- Menyalin business memory ke store yang sudah berisi wiki developer lain melewati halaman yang judulnya sudah ada di
  sana (dilaporkan sebagai peringatan), agar tidak ada halaman ganda.

## Kapan memory dibuat

Setelah thread selesai (minimal satu tugas berhasil, atau planner menjawab langsung), file yang direncanakan, disentuh
tugas, dan disebut di prompt dipetakan dulu ke modul dan layer ([peta kode](#peta-kode); bila perlu menunggu model peta
kode). Lalu model **Wrapper** membaca permintaan, rencana, kontrak, hasil tugas, ringkasan, peta kode file itu, daftar
modul, dan memory yang sudah ada **di modul yang sama**, lalu menulis **maksimal 5** memory baru lengkap dengan label
(modul dari daftar, layer dari peta kode, jenis lewat pertanyaan berurutan, keyword). AIT memvalidasi dan
menormalisasi hasilnya; memory yang labelnya tidak lengkap dilewati. Karena hanya sesi di dalam sebuah project (folder dengan `.git` atau file build) yang diorkestrasi,
setiap thread selalu punya project; bila project dihapus saat thread berjalan, hanya knowledge memory yang disimpan.
Proses ini berjalan di belakang dan tidak menunda balasan ke Claude Code.

## Cara planner memakai memory

```
Lapis 1 — Modul (· Layer · Jenis)  → daftar memory modul itu
Lapis 2 — Keyword                  → di dalam daftar itu, yang memuat istilah yang dicari
read_memory                        → baru deskripsinya dibaca
```

1. Sebelum planner jalan, file dan class yang disebut prompt dipetakan ke modul (peta kode yang tersimpan + aturan;
   yang ragu dipetakan model di belakang tanpa menahan planner).
2. Brief planner berisi:
   - **Peta kode** file yang disebut prompt (`path: Modul · Layer`);
   - **Peta memory**: tiap modul dengan jumlah memory per layer dan jenis, mis. `User (12): Logic 5 · Entry 3 | Rule 4 · Flow 3`;
   - **Indeks memory** (id + label + keyword, maks 25 baris): memory modul dari peta kode atau yang disebut prompt (nama
     atau alias modul), diurutkan berdasarkan keyword; ditambah short memory terbaru. Halaman wiki bisnis punya jatah
     sendiri (maks 8), memory lama yang belum berlabel juga (tanda `unlabeled`).
3. `search_memory` menerima `module`, `layer`, `kind`, dan `keywords`. Tanpa modul, modul yang disebut di keyword dipakai;
   bila lapis 2 kosong, seluruh isi lapis 1 dikembalikan supaya planner tidak buta.
4. `read_memory` membaca deskripsi (atau isi halaman wiki, gambar diganti keterangannya, maks 15.000 karakter); planner
   diminta membaca halaman modul sebelum mengubah perilakunya dan mengikuti aturannya.
5. Setiap kali long memory dibaca, kolom *Dipakai* bertambah — penanda memory yang bernilai.

Pencocokan keyword **per token**, bukan substring: `UserValidator` cocok dengan `user`, `validator`, dan
`uservalidator`; `form` tidak cocok dengan `information`. Bentuk jamak `-s`/`-es` dianggap sama. Keyword yang dipakai banyak
memory project (mis. `Service`) bernilai kecil, keyword yang jarang (identifier) bernilai besar. Karena itu keyword
sebaiknya memuat istilah bahasa Inggris **dan** istilah yang Anda pakai sehari-hari, plus nama class yang relevan.

## Peta kode

Modul dan layer setiap file, diputuskan sekali lalu disimpan (per project key + path relatif root, jadi berlaku di mesin
semua developer) supaya file yang sama selalu mendapat jawaban yang sama.

```
file disebut prompt / disentuh tugas
  ① peta tersimpan? ──ya──► pakai
  ② aturan: layer dari akhiran/folder/ekstensi (Controller→Entry, BL/Service→Logic, Repository→Data, .cshtml→UI, …),
     modul dari nama tanpa kata peran (UserController→User) yang cocok dengan daftar modul ──yakin──► simpan (aturan)
  ③ ragu: model (Tools node → Wrapper → Planner), satu panggilan per kelompok nama yang sama, wajib memilih modul
     yang ada; modul baru hanya bila tidak ada yang cocok ──► simpan (AI)
```

- Sebelum planner hanya ① dan ② yang ditunggu; ③ berjalan paralel, dan ekstraksi memory menunggu hasilnya.
- File plumbing tanpa fitur (startup, konfigurasi, layout, helper) masuk modul `Core`.
- Tanpa node model aktif, modul diambil dari nama file.
- **Koreksi manual** dari menu Project → detail → *Peta kode* (*Atur file* / *Ubah*), atau lewat prompt: planner
  punya tool `set_module` ("anggap `UserBL` modul Account"). Koreksi manual tidak pernah ditimpa aturan atau model.
- **Scan** memetakan semua file sumber yang belum terpetakan (maks 3.000 file per scan; `bin`, `obj`, `node_modules`,
  `wwwroot/lib`, dsb. dilewati). Setiap file pasti mendapat entri (dari model atau nama file), jadi scan yang terputus
  (mis. AIT di-restart) dilanjutkan dari file yang belum terpetakan. Dijalankan otomatis saat migrasi label.
- Panggilan model berisi 40 grup file (per stem nama). Batch pertama jalan sendiri untuk mengisi daftar modul, sisanya
  3 panggilan sekaligus (tetap dibatasi *Max concurrency* node); hasilnya disimpan bergantian agar modul baru dari satu
  batch dipakai batch berikutnya, bukan dibuat ganda.

## Daftar modul & gabung modul

Satu daftar modul per project key (dan satu untuk knowledge memory), dipakai bersama label memory dan modul wiki bisnis.
Nama dicocokkan tanpa melihat huruf besar/kecil dan pemisah, lewat **alias** (nama lain/terjemahan, mis. `Pengguna`
untuk `User`), lalu bentuk jamak bahasa Inggris. Modul yang sudah digabung tetap tersimpan sebagai pengalih, jadi nama
lamanya masih diarahkan ke modul tujuan.

**Modul ganda** (terpisah tidak sengaja) dicari saat migrasi, setiap kali modul baru dibuat, dan lewat tombol *Cek
modul ganda*:

| Kondisi | Aksi |
|---|---|
| Nama sama dengan penulisan berbeda (huruf besar/kecil, pemisah, jamak) | Digabung otomatis ke modul yang lebih banyak dipakai |
| Model menilai sama dengan keyakinan ≥ 85% (terjemahan, nama lebih panjang, typo) | Digabung otomatis, tercatat, bisa dibatalkan |
| Model ragu | Saran di **Memory → Perlu review** (*Gabungkan* / *Bukan duplikat*) |

Menggabungkan modul memindahkan memory (short & long, label ikut ditulis ulang), peta kode, dan halaman wiki bisnis;
nama dan alias modul asal menjadi alias modul tujuan. Halaman wiki yang judulnya sudah ada di modul tujuan tidak
dipindah (dilaporkan di log). **Batalkan** di riwayat gabung modul (Project → detail) mengembalikan tepat baris yang
dipindah. Saran yang ditolak tidak diusulkan lagi.

**Ganti nama modul** (form memory: tombol *Ubah nama* di samping dropdown modul; Project → detail: field *Nama* di
*Atur*) juga bisa hanya mengganti huruf besar/kecil, mis. `Klaimdf` → `KlaimDF`. Memory (modul utama maupun modul
lain, label ikut ditulis ulang), peta kode, halaman wiki bisnis, pengalih modul yang sudah digabung, dan riwayat gabung
ikut berganti nama. Nama lama yang berbeda (bukan hanya huruf besar/kecil) disimpan sebagai alias. Nama yang sudah
dipakai modul lain (termasuk aliasnya atau bentuk jamaknya) ditolak — pakai *Gabungkan* untuk itu.

## Migrasi label (memory lama)

Memory dari versi sebelum label berstruktur (dan dari AIT versi lama developer lain di store bersama) dimigrasi
otomatis oleh worker saat AIT start lalu tiap `MemoryRelabelIntervalMinutes` (default 10 menit):

1. **Backup** semua short & long memory ke `%LOCALAPPDATA%\AIThread\backup\memory-before-labels-v1-<waktu>.json`
   (plus modul & label halaman wiki), sekali per versi label. Bila backup gagal, migrasi tidak jalan.
2. **Wiki bisnis**: modul halaman diselaraskan dengan daftar modul (mengisi daftar awal) dan label halaman dinormalisasi
   PascalCase. Isi halaman tidak diubah, tidak membuat revisi.
3. **Per project** terdaftar yang punya memory menunggu (yang paling banyak lebih dulu):
   - **Peta kode**: hanya file yang disebut deskripsi memory itu (path dan nama class, maks 10 per memory, maks 3 file
     per nama), dicari dengan satu kali jelajah folder project. Hanya itu yang dibaca saat relabel;
   - **Relabel** memory project itu, 20 memory per panggilan model: path/identifier di deskripsi dicocokkan ke peta
     kode dulu, model menentukan modul (dari daftar), layer, jenis, dan keyword. Label lama disimpan sebagai *label
     lama*. Setiap baris diklaim dulu (update bersyarat), jadi beberapa mesin yang berbagi store tidak memproses baris
     yang sama.
4. **Relabel sisa**: knowledge memory, memory project yang terdaftar di mesin lain, dan memory tanpa project key.
5. Memory yang modulnya tidak bisa ditentukan, atau gagal 3 kali, masuk **Memory → Perlu review**.
6. **Peta kode lengkap**: scan penuh semua project terdaftar (yang punya memory lebih dulu), setelah semua label
   selesai. Sekali per proses AIT; scan yang terputus dilanjutkan.
7. **Cek modul ganda** dan daftar **kandidat memory duplikat** (modul & jenis sama, keyword sangat mirip; hanya saran).

Banner *Migrasi label memory* menampilkan fase dan kemajuannya, mis.
`Peta kode: KN2018_KCO (1/5) · 12/40 file disebut memory`, `Relabel: KN2018_KCO (1/5)`, atau
`Peta kode lengkap: KN2018_KCO (1/12) · 363/1.700 file`.

Selama migrasi, memory lama tetap terlihat oleh planner sebagai `unlabeled` (dicocokkan dengan label lamanya). Kolom
`Label` lama tetap diisi gabungan `Modul · Layer · Jenis | keyword` supaya AIT versi lama yang berbagi store masih bisa
membacanya. Migrasi butuh node Tools node, Wrapper, atau Planner yang aktif; tanpa itu worker menunggu.

## Business memory (wiki bisnis)

Memory project dan knowledge ditulis untuk planner (label + deskripsi singkat). **Business memory** ditulis untuk
manusia: satu halaman per modul bisnis (mis. modul **TO — Transfer Order** di project EEP) yang menjelaskan apa yang
dilakukan sistem dan aturan yang diikutinya, sederhana dan ramah dibaca analis, tester, user bisnis, dan anggota tim
baru. Isinya Markdown dengan struktur tetap:

```
# TO — Transfer Order
> Pemindahan stok antar gudang.
## Tujuan
## Istilah            (tabel Istilah | Arti)
## Alur & status      (tabel Status | Arti | Pelaku | Berikutnya)
## Aturan bisnis      (bernomor: BR-TO-01, BR-TO-02, …)
## Validasi           (tabel Field | Aturan | Pesan)
## Tampilan           (screenshot dengan keterangan)
## Lokasi di kode     (opsional, path file saja)
```

### Cara wiki dibangun

```
thread selesai (tiap developer) ──► menunggu dirangkum (lokal)
                                       │  tiap N jam (default 6) atau tombol "Perbarui sekarang":
                                       ▼
                 1. model memilih halaman: thread mana menyentuh modul apa (perubahan teknis murni dilewati)
                 2. per halaman: model menulis ulang halaman dari isi lama + thread baru (+ screenshot)
                                       ▼
                 halaman + revisi + gambar ──► store business memory (bisa dibagi semua developer)
```

- Bahan tiap thread: permintaan developer, ringkasan rencana, kontrak nama, daftar perubahan file, dan balasan akhir.
  Maks 15 thread / ±60.000 karakter per putaran, maks 5 putaran per jalan.
- **Screenshot**: gambar yang Anda lampirkan di prompt (Claude Code, Codex, …) disimpan bersama thread, lalu diberikan
  ke penulis wiki. Gambar yang dipakai di halaman disalin ke store business memory; sisanya dibuang setelah thread
  dirangkum (atau setelah masa retensi log, default 14 hari). Model yang tidak bisa membaca gambar tetap menulis dari
  teksnya saja.
- Penulis wiki diminta mempertahankan isi lama (termasuk tulisan manual), hanya menulis fakta yang didukung thread,
  menandai yang meragukan dengan *(perlu konfirmasi)*, dan memakai bahasa halaman (default Bahasa Indonesia).
- Thread ditandai *sudah dirangkum* hanya bila semua halaman yang ia sentuh berhasil disimpan; yang gagal dicoba lagi
  di jalan berikutnya.
- **Banyak developer**: setiap AIT merangkum thread miliknya sendiri ke store yang sama. Setiap halaman punya
  nomor versi; penyimpanan atas versi yang sudah berubah ditolak (penulis wiki membaca ulang lalu menulis ulang sekali,
  editor di menu Memory menampilkan pesan *sudah diubah …*). Setiap simpan membuat revisi baru yang bisa dipulihkan.
- Halaman yang **dikunci** (*Diperbarui AI* dimatikan) tidak pernah diubah penulis wiki — cocok untuk halaman yang
  sudah dikurasi manual.

### Mengedit manual

Menu **Memory → Bisnis (wiki)**: pilih project, buka halaman, lalu *Edit*, atau buat *Halaman baru*. Tempel screenshot
langsung ke editor (Ctrl+V) atau pakai *Sisipkan gambar*; gambar (PNG/JPEG/GIF/WebP, maks 5 MB) disimpan di store dan
ditulis sebagai `![keterangan](asset:<id>)`. *Pratinjau* menampilkan halaman persis seperti setelah disimpan. HTML mentah
tidak dirender, dan tautan hanya boleh ke `http(s)`, `mailto`, atau dalam aplikasi — wiki bisa ditulis siapa saja yang
berbagi store, jadi isinya tidak dipercaya mentah-mentah.

## Mengelola dari menu Memory

- **Tambah memory**: tulis pengetahuan yang ingin selalu diingat planner, mis. aturan bisnis penting atau konvensi
  tim. Isi modul (saran dari daftar modul), jenis, layer, keyword (maks 16), dan deskripsi. Masuk sebagai short memory,
  lalu otomatis pindah ke long memory.
- **Buka**: ubah tipe, project, label, deskripsi. Perbaiki memory yang kurang tepat — planner mempercayainya.
  Menyimpan memory yang belum berlabel memberinya label (label lamanya tetap tercatat).
- **Filter** modul, layer, jenis, dan *berlabel / belum berlabel* di atas tabel; banner *Migrasi label memory* tampil
  selama masih ada memory yang menunggu label (tombol *Jalankan sekarang*).
- Tab **Perlu review**: memory tanpa label (*Beri label*), saran gabung modul, dan kandidat memory duplikat.
- Menu **Project → detail**: daftar modul (deskripsi, alias, gabung ke modul lain, *Cek modul ganda*), peta kode
  (cari, *Atur file*, *Scan*), dan riwayat gabung modul (*Batalkan*).
- **→ DB**: pindahkan short memory ke long memory sekarang.
- **Hapus**: hapus memory yang salah atau usang.
- **Konsolidasi sekarang**: jalankan pemindahan short → long tanpa menunggu worker.
- **Export / Import**: simpan memory ke file atau masukkan dari file — lihat [Export & import](#export--import).
- Tab **Bisnis (wiki)**: halaman wiki bisnis per project — lihat [Business memory](#business-memory-wiki-bisnis).

### Export & import

Untuk backup, pindah mesin, atau mengirim memory satu project ke rekan tanpa store bersama. Untuk pindah store di
mesin yang sama, pakai *Salin memory yang ada* di [Penyimpanan memory](#penyimpanan-memory).

- **Export** mengunduh short dan long memory **sesuai filter** tabel (level, tipe, project, pencarian) sebagai file JSON
  (`"format": "aithread-memory"`). Bila short memory ikut dipilih tetapi store-nya tidak terhubung, export ditolak
  (pilih level *Long* untuk long memory saja), supaya tidak ada yang hilang diam-diam. Business memory tidak termasuk.
- **Katalog unit test** project yang diekspor (semua project bila tidak difilter) ikut di file, supaya tim tanpa store
  bersama tetap berbagi katalog: judul, skenario, status, alasan obsolete, hasil terakhir, dan **snapshot** isi file test
  (kecuali project yang mematikan *Simpan snapshot test*). Tidak ikut bila filter memakai pencarian teks atau tipe
  *Knowledge*. Riwayat run tidak ikut.
- **Akun uji**, **skenario test** (langkah, peran, kolom form; tanpa hasil run), dan setting testing project (password
  dev, hasil deteksi *login as*) ikut dengan aturan yang sama (bagian `testing`). Password dev ikut karena isinya hanya password bypass di dev yang terlihat seluruh tim.
- Project ditulis dengan **project key**-nya (memory lama tanpa key memakai key project-nya di mesin ini), jadi di
  mesin lain memory masuk ke clone project yang sama walau folder root-nya berbeda.
- **Import** memeriksa file dulu (*dry run*) dan menampilkan ringkasan: yang ditambahkan, ditimpa, sudah ada, tidak
  valid, serta project di file dan apakah sudah terdaftar di mesin ini. Data baru ditulis setelah tombol *Import*.
- Format file v2 membawa label berstruktur (modul, layer, jenis, area, keyword, label lama); modulnya masuk daftar modul
  key-nya di mesin tujuan. Item tanpa label (file v1) masuk sebagai *belum berlabel* dan dimigrasi worker. Format v3
  menambah bagian `unitTests`, v4 (saat ini) bagian `testing`; file v1–v3 tetap diterima.
- **Akun uji** dari file dicocokkan per id, lalu per project key + username (tidak peka huruf besar/kecil), dan
  setting testing per project key. Yang sudah ada diperbarui hanya bila salinan di file lebih baru. Ringkasan import
  menampilkan jumlah akun uji baru, diperbarui, dan dilewati.
- **Unit test** dari file dicocokkan per id, lalu per project key + nama test. Yang sudah ada diperbarui hanya bila
  salinan di file lebih baru (diubah atau dijalankan lebih akhir), terlepas dari pilihan *lewati/timpa*. Test baru
  mempertahankan nomornya, kecuali nomor itu sudah dipakai test lain di project yang sama — lalu mendapat nomor
  berikutnya (dilaporkan). Status *Dihapus* ikut terbawa, jadi test yang sudah dihapus tim tidak dipulihkan lagi.
  Ringkasan dry run menampilkan jumlah unit test baru, diperbarui, bernomor baru, dan dilewati (tanpa isi snapshot).
- Id memory dipertahankan: memory yang id-nya sudah ada **dilewati** (default) atau **ditimpa** (tipe, project, label,
  deskripsi dari file). Memory dengan isi sama (project, tipe, label, deskripsi — huruf besar/kecil dan spasi
  diabaikan) tapi id lain juga dilewati. Import aman diulang.
- Short memory tetap short dengan waktu dibuat aslinya, lalu dipindah worker ke long memory sesuai umurnya. Bila store
  short memory tidak terhubung, short memory dari file langsung disimpan sebagai long memory.
- Project yang belum terdaftar: memory tetap disimpan di bawah key-nya dan muncul begitu project itu ditambahkan.
  Project memory tanpa key dan tanpa project terdaftar menjadi knowledge memory. Key **lokal** (`local:…`) dari mesin
  lain tidak akan cocok dengan project mana pun — beri project key bersama di mesin asal sebelum mengekspor.
- File berisi teks biasa (deskripsi bisa memuat path, potongan kode, atau info internal; snapshot unit test berisi kode
  test project). Setting koneksi store dan password tidak pernah ikut. Maksimal 100.000 memory per import.

Tips menulis memory manual:

- Pilih modul yang sudah ada (lihat saran di kolom Modul); buat modul baru hanya bila memang fitur lain.
- Keyword spesifik: `Tax, Pajak, Rounding, InvoiceService` lebih berguna daripada `Bisnis`.
- Deskripsi menyebut lokasi: "Perhitungan pajak ada di `InvoiceService.CalculateTax`; pembulatan ke atas per baris."
- Satu fakta, satu jenis per memory.

## Konfigurasi

| Kunci `appsettings.json` | Default |
|---|---|
| `AIThread:ShortMemoryHours` | 24 |
| `AIThread:MemoryConsolidationIntervalMinutes` | 10 |
| `AIThread:MemoryRelabelIntervalMinutes` | 10 (interval migrasi label & cek modul ganda) |
| `AIThread:MemoryAcronyms` | `[]` (singkatan tambahan yang ditulis kapital di label, mis. `["SKU", "GL"]`) |
| `ConnectionStrings:Redis` | `localhost:6379` (port Garnet; lihat [Instalasi](01-instalasi.md#1-short-memory-garnet)) |
| `AIThread:EmbeddedRedis:Enabled` | `true` |

Dua kunci terakhir hanya dipakai saat short memory memakai provider *Bawaan*.

Business memory diatur di menu Setting (aktif, interval, model, nama developer) — lihat
[Konfigurasi](02-konfigurasi.md#business-memory).

Perkiraan "±N jam lagi ke long memory" di tabel Memory selalu dihitung dari 24 jam.
