# Rancangan: Tester multi akun, test lanjutan, dan skenario Excel

> Status: **fase 1 sudah diimplementasikan** (2026-10-23). Perilaku yang berlaku ada di dokumen pengguna:
> [07-cara-kerja.md → Akun uji](../07-cara-kerja.md#akun-uji-berganti-user), [05-menu.md → Akun uji](../05-menu.md#akun-uji),
> [09-referensi-api.md](../09-referensi-api.md), [06-memory.md → Export & import](../06-memory.md#export--import).
> Dokumen ini catatan keputusan dan tidak tampil di menu Docs.
>
> Bagian fase 1 yang berbeda di implementasi:
> - Service dipecah dua: `ITestAccountStore` (per project key, dipakai Tester dan planner) dan `ITestAccountService`
>   (per project id, untuk API/UI).
> - Balasan chat untuk password dev yang ditolak: `password dev <nilai>` (juga `password dev: <nilai>`), ditangani
>   `ThreadOrchestrator.TestWithDevPasswordAsync`.
> - Profil browser akun: `tester-browser\accounts\<10 hex hash project key>-<username>\<browser>`.
> - Planner memuat akun sendiri (`PlannerAgent.WithAccountsAsync`) hanya saat Auto Testing aktif; tanpa akun uji, `actor`
>   apa saja diterima (diteruskan ke tester).
> - Deteksi *login as* otomatis hanya bila project punya **lebih dari satu** akun dan belum pernah dideteksi. Hasil
>   `none` dari scan juga dihitung "sudah dideteksi"; ulangi lewat tombol **Deteksi**.
> - Pengamatan tester tidak menimpa hasil yang dikonfirmasi, diisi developer, atau yang sudah `available`.
> - Export memakai flag `MemoryQuery.IncludeTestAccounts` (default menyala), terpisah dari unit test.
>
> **Fase 2 sudah diimplementasikan** (2026-10-23): [07-cara-kerja.md → Test lanjutan](../07-cara-kerja.md#test-lanjutan-dan-nilai-antar-langkah).
> Yang berbeda:
> - Test lanjutan tidak membuat thread baru: ia testing ulang thread yang sama dengan skenario saran itu. Hasilnya
>   disimpan di saran (`TestingReport.FollowUps[].LastRun`), laporan utama thread tetap, dan saran baru dari run itu
>   ditambahkan dengan nomor berikutnya (maks 20 per thread, judul sama tidak digandakan).
> - Nilai yang diingat (`TestingReport.Values`) juga dibawa ke *Ulangi testing*; saran lama tetap dipertahankan bila
>   testing ulang tidak menyarankan apa pun.
> - Thread yang hanya menguji (tanpa tugas) kini bisa diulang testing-nya (sebelumnya ditolak *tidak ada perubahan*).
> - Balasan chat yang dikenali: `jalankan test lanjutan <n>`, `test lanjutan #n`, `lanjut test n`, `run follow-up n`
>   (seluruh pesan; kalimat lain tetap prompt biasa).
>
> **Fase 3 sudah diimplementasikan** (2026-10-23): [07-cara-kerja.md → Skenario multi-langkah](../07-cara-kerja.md#skenario-multi-langkah-excel).
> Yang berbeda:
> - Skenario disimpan di tabel `TestingScenarios` di context testing yang sama dengan akun uji (bukan context sendiri).
>   Store eksternal yang sudah punya `TestAccounts` tetapi belum `TestingScenarios` tidak mendapat tabel itu otomatis;
>   ini hanya mungkin bila build fase 1/2 (tidak pernah dirilis) sempat dipakai dengan DB eksternal.
> - Sheet *Langkah* memakai **peran** sebagai aktor (dropdown dari sheet *Akun*); peran dipetakan ke username lewat
>   sheet *Akun*. Saran yang aktornya username diubah ke peran akun itu.
> - Akun baru di sheet *Akun* langsung ditambahkan saat file dibaca (membaca/mengunggah file dianggap konfirmasi);
>   akun yang sudah ada tidak diubah (beda peran hanya diperingatkan).
> - Versi skenario hanya naik bila isi file berubah, jadi `jalankan skenario` boleh selalu membaca file project.
> - Skenario dijalankan di thread asalnya (rencana, perubahan, nilai yang diingat); hasilnya juga disimpan di saran asal.
> - Balasan chat: `buat skenario test lanjutan <n>` / `buatkan skenario excel untuk test lanjutan #n`, dan
>   `jalankan skenario <slug>` (+ `setuju`/`ya`/`ok` untuk konfirmasi efek samping).
> - Saran tester mendapat `fields` (kolom form sheet) dan skenarionya `action`.

## Latar belakang

Aplikasi bisnis jarang selesai di "submit berhasil". Setelah submit biasanya ada **approval berjenjang**: atasan
level 1, level 2, HR, dan seterusnya, ditambah tindakan lain seperti edit sebelum approve, reject, atau revisi. Bagian
inilah yang paling sering rusak dan paling jarang dites, karena butuh **beberapa user** dan data yang saling
bersambung.

Kondisi Tester (Auto Testing) saat ini:

- Hanya **satu akun per sesi**: `TestLogin` dari balasan chat `login <user> <password>`, disimpan di memori
  (`TestLoginStore`) dan tidak pernah ke disk.
- Browser memakai **satu profil persisten** per channel (`%LOCALAPPDATA%\AIThread\tester-browser\msedge`). Berganti user
  berarti logout manual di tengah testing, dan sesi login user sebelumnya hilang.
- Laporan berhenti di hasil skenario. Tester tidak memberi tahu apa yang **seharusnya dites setelah itu**.
- Tester tidak tahu **hierarki approval**. Siapa atasan dari pengaju ada di data aplikasi, bukan di layar.

## Keputusan

| # | Keputusan |
|---|---|
| 1 | Akun uji dan skenario disimpan di **database AIT**, per `ProjectKey`, ikut store long memory seperti katalog unit test. Dengan begitu terbagi ke seluruh tim. |
| 2 | Excel **dibuat dinamis** per skenario dari struktur yang disusun model. File Excel ditulis **kode AIT**, bukan model. Excel hanya alat isian; datanya disimpan di database saat diimpor. |
| 3 | Test lanjutan **tidak jalan otomatis**: dijalankan setelah developer klik atau membalas di chat, karena bisa membuat data dan memicu notifikasi. |
| 4 | Akun uji **tanpa password per akun**. Project punya satu **Password dev**: bawaannya "ketik apa saja" (AIT mengisi `Test@1234`), Tester memberi tahu nilai yang dipakai, dan nilai yang ternyata dibutuhkan disimpan per project. Password asli tetap lewat jalur chat yang sekarang (memori saja). |
| 5 | **Semua developer** project boleh menambah, mengubah, dan menghapus akun uji, sama seperti katalog unit test. |
| 6 | Fitur ***login as* / impersonate** berbeda di tiap project: ada atau tidak, dan peran mana yang boleh memakainya (Administrator saja atau peran lain juga). AIT **mendeteksinya per project** (scan kode oleh model + pengamatan Tester), menyimpannya di setting testing project, dan developer bisa mengoreksinya. `switch_user` memakainya bila tersedia, dan kembali ke login biasa bila tidak. |
| 7 | **SSO** (Microsoft/Google) di luar cakupan: skenario yang butuh SSO ditandai *blocked* dengan alasan yang jelas. |

## Fase

| Fase | Isi | Bergantung pada |
|---|---|---|
| **1** | Akun uji per project + Password dev + tool Tester `switch_user` (profil browser dan cookie terpisah per akun) | — |
| **2** | Tester menyarankan **test lanjutan** di laporan; developer menjalankannya dengan satu klik / balasan chat; nilai antar langkah disambung | Fase 1 |
| **3** | **Skenario multi-langkah** + Excel dinamis (`.aithread/testing/*.xlsx` dan unduh/unggah di Web UI), peringatan efek samping, penanda data uji | Fase 1, 2 |

Fase 1 dirinci di bawah. Fase 2 dan 3 cukup gambaran, untuk dirinci setelah fase 1 dipakai.

---

## Fase 1: akun uji dan berganti user

### Data

Ikut store long memory (`LongMemoryStore`, dibuat `MemorySchema` di DB eksternal), seperti `UnitTestCases`. Ada
migrasi untuk DB lokal.

**`TestAccounts`**

| Kolom | Isi |
|---|---|
| `Id` | GUID |
| `ProjectKey` | Key project (sama untuk semua clone) |
| `Username` | Yang diketik di form login. Unik per `ProjectKey` (tidak peka huruf besar/kecil) |
| `Role` | Peran bebas: *Pengaju*, *Atasan*, *HR*, *Admin*… Dipakai planner/Tester untuk memilih akun |
| `Attributes` | JSON objek bebas `{ "NIK": "12345", "Unit": "Sales", "Level approval": "1", "Atasan": "atasan01" }`. Kolomnya dinamis per project |
| `Notes` | Catatan bebas (mis. *"dipakai untuk flow cuti saja"*) |
| `CreatedBy`, `UpdatedBy`, `UpdatedAt` | Nama developer (setting *Nama developer*) |

**`TestingSettings`** (satu baris per `ProjectKey`)

| Kolom | Isi |
|---|---|
| `DevPassword` | `null` = bawaan "ketik apa saja": AIT mengisi `Test@1234` (cukup panjang, ada huruf besar, angka, dan simbol, supaya lolos validasi sisi browser yang umum). Bila diisi: nilai itu yang dipakai |
| `DevPasswordSource` | `default` / `developer` (diisi di UI) / `chat` (jawaban saat Tester bertanya) |
| `Impersonation` | JSON hasil deteksi *login as* (lihat [Login as / impersonate](#login-as--impersonate)); `null` = belum dideteksi |
| `SideEffectsAcknowledgedBy`, `…At` | Dipakai fase 3: siapa yang sudah membaca peringatan efek samping |

Password dev **bukan rahasia**: isinya hanya password bypass di lingkungan dev, dan terlihat oleh semua developer
project. UI menyatakannya terang-terangan: *"Jangan isi password asli. Untuk akun dengan password asli, balas
`login <user> <password>` di chat (tidak disimpan)."*

Akun uji sebaiknya **akun uji**, bukan akun karyawan asli. Username dan NIK tetap data pribadi, jadi ada peringatan
yang sama di UI.

### Di brief planner dan Tester

Bagian baru `# Test accounts` (hanya bila project punya akun):

```
# Test accounts (project's shared list; log in with switch_user, password: {{password}})
- pengaju01 · Pengaju · NIK 12345, Unit Sales, Atasan atasan01
- atasan01 · Atasan · NIK 22222, Unit Sales, Level approval 1, Atasan hr01
- hr01 · HR · Level approval 2
Password: the project's development password ("Test@1234" by default; the app is expected to accept any password in development).
```

- **Planner** memakai daftar ini untuk menulis skenario dengan aktor. `TestScenario` mendapat field opsional
  `actor` (username, atau peran bila username belum pasti). Validator menolak aktor yang tidak ada di daftar akun
  maupun daftar peran.
- **Tester** memakai daftar ini untuk memilih akun per skenario, mis. "approve oleh atasan pengaju01 → atasan01".

### Tool Tester baru: `switch_user`

```
switch_user(account)   // username dari "# Test accounts"
```

- **Browser**: menutup jendela yang sedang terbuka, lalu membuka profil milik akun itu
  (`tester-browser\<channel>\accounts\<username>`). Sesi login tiap akun bertahan antar run: setelah login sekali,
  run berikutnya tidak perlu login lagi. Tetap satu jendela pada satu waktu (gate `TestBrowserLauncher` yang ada).
- **HTTP** (`http_request`): tiap akun punya cookie jar sendiri di `TestRun`. Berganti akun tidak membuang sesi akun lain
  dalam run yang sama.
- **Placeholder**: setelah `switch_user`, `{{username}}` diisi username akun aktif dan `{{password}}` diisi Password dev
  project. Mekanisme `Fill`/`Mask` yang sekarang tetap dipakai, jadi model tetap tidak melihat nilainya.
- Jawaban tool: akun aktif, apakah profil itu sudah punya sesi (cookie) untuk `baseUrl`, dan snapshot halaman bila
  browser dibuka.
- Progres: `👤 switch_user  atasan01 (Atasan)`. Laporan mencatat aktor per skenario.

### Login as / impersonate

Banyak aplikasi internal punya fitur admin *login sebagai user lain*. Fitur itu jauh lebih cepat dan andal daripada
logout–login, tetapi **berbeda di tiap project**: ada atau tidak, cara memakainya (tombol di daftar user, URL, endpoint),
dan peran yang boleh memakainya (Administrator saja, atau juga peran lain seperti HR/Superuser).

**Yang disimpan** (`TestingSettings.Impersonation`):

| Field | Isi |
|---|---|
| `Status` | `available` / `none` / `unknown` |
| `How` | Cara memakai, dalam kalimat untuk Tester: menu/URL/endpoint dan langkahnya, mis. *"Admin › Users › cari username › tombol Login as"* atau *"GET /Account/LoginAs?username={{target}}"* |
| `Back` | Cara kembali ke akun admin bila ada (mis. tombol *Kembali ke admin*), atau kosong |
| `Roles` | Peran akun uji yang boleh memakainya, mis. `["Administrator"]` atau `["Administrator", "HR"]` |
| `Evidence` | File/baris kode atau halaman yang jadi dasar deteksi |
| `Source`, `Confirmed` | `scan` / `tester` / `developer`; `Confirmed` = sudah dicek developer |

**Deteksi (sensing) per project**, dijalankan sekali saat pertama kali sebuah test butuh lebih dari satu akun, atau
lewat tombol *Deteksi login as* di kartu *Akun uji*:

1. **Scan kode tanpa model**: cari pola seperti `impersonat`, `LoginAs`, `login-as`, `SwitchUser`, `RunAs`,
   `SignInAs`, *login sebagai*, beserta pembatas peran di sekitarnya (`[Authorize(Roles = …)]`, `IsInRole("…")`,
   `User.HasClaim`, policy).
2. **Model murah** (node Tools, seperti `MemoryAi`) membaca potongan hasil scan dan menjawab JSON: tersedia atau tidak,
   `How`, `Back`, `Roles`, `Evidence`. Tanpa temuan dari scan, hasilnya `none` tanpa memanggil model.
3. **Pengamatan Tester**: bila saat testing Tester melihat tombol atau menu *Login as* yang belum tercatat, ia
   melaporkannya di `report_test`, dan AIT menambahkannya dengan `Source = tester`.
4. Hasil deteksi tampil di kartu *Akun uji* dengan dasar temuannya. Developer bisa **mengoreksi** atau
   **mengonfirmasi**. Hasil yang belum dikonfirmasi tetap dipakai, tetapi laporan testing menyebutnya
   (*"memakai login as hasil deteksi, belum dikonfirmasi"*).

**Dipakai oleh `switch_user`**:

- Bila `Status = available` dan ada akun uji dengan salah satu `Roles`: Tester login sekali sebagai akun itu (mis.
  `admin01`), lalu memakai `How` untuk masuk sebagai akun tujuan, dan `Back` untuk kembali. Brief Tester memuat `How`,
  `Back`, dan akun admin yang dipakai.
- Bila tidak tersedia, tidak ada akun dengan peran itu, atau cara itu gagal: login biasa dengan Password dev. Bila
  cara *login as* gagal, laporan menyarankan developer mengecek hasil deteksi.
- Mencocokkan peran antara `Roles` dan `Role` akun uji tidak peka huruf besar/kecil. Nama peran yang berbeda
  (*Admin* vs *Administrator*) dikoreksi developer di kartu *Akun uji*.

### Bila login ditolak

1. Tester memakai Password dev (bawaan `Test@1234`).
2. Bila aplikasi menolaknya, Tester memanggil `ask_login` dengan alasan *"password dev ditolak"*. Developer membalas di
   chat, mis. `password dev 123`, atau mengisi di modal *Ulangi testing*.
3. Jawaban itu **disimpan sebagai Password dev project** (`DevPasswordSource = chat`), testing diulang, dan laporan
   menyebutnya: *"Password dev project diperbarui menjadi `123` oleh Andi"*.
4. Balasan `login <user> <password>` yang sekarang tetap berlaku: hanya untuk sesi itu, hanya di memori, tidak
   disimpan. Itu jalur untuk password asli.

### Laporan

- Baris kepala testing menyebut akun yang dipakai: `· akun pengaju01, atasan01`.
- Sekali per run, bila Password dev bawaan dipakai: *"Login memakai password dev bawaan `Test@1234`. Bila aplikasi Anda
  memakai password lain di dev, isi di Project > Detail → Akun uji."*
- Skenario mencatat aktornya: `✅ **Approve level 1** (atasan01) — status menjadi Menunggu HR`.

### UI dan API

**Project > Detail → kartu *Akun uji***

- Tabel: username, peran, atribut (chip `key: value`), catatan; tambah/ubah/hapus. Atribut diedit sebagai daftar
  pasangan key–value; kunci yang sudah dipakai akun lain di project itu muncul sebagai saran, supaya kolomnya konsisten.
- Field *Password dev* dengan placeholder *"kosong = ketik apa saja (AIT memakai Test@1234)"* dan peringatan di atas.
- Kartu memakai kontrol jendela macOS (`.win`) seperti kartu lain.

**API**

| Method | Path | Isi |
|---|---|---|
| `GET` | `/api/projects/{id}/test-accounts` | Daftar akun + setting testing project |
| `POST` | `/api/projects/{id}/test-accounts` | Tambah akun |
| `PUT` | `/api/projects/{id}/test-accounts/{accountId}` | Ubah akun |
| `DELETE` | `/api/projects/{id}/test-accounts/{accountId}` | Hapus akun |
| `PUT` | `/api/projects/{id}/testing-settings` | Password dev (kosong = bawaan) |

Project tanpa project key tidak bisa menyimpan akun (sama seperti katalog unit test). UI menampilkan alasannya.

### Export/import memory

Akun uji dan setting testing ikut file `aithread-memory` (v4), supaya tim tanpa DB eksternal bersama tetap bisa
berbagi. Aturannya sama dengan katalog unit test: per `ProjectKey`, idempoten, yang lebih baru menang. Username
bentrok dengan isi berbeda → versi yang lebih baru dipakai.

### Komponen (mengikuti konvensi Service)

| Lapisan | Komponen |
|---|---|
| Common | `Entities/TestAccount`, `Entities/TestingSettings`, `DTOs/TestAccountDtos` |
| Data | `ITestingDb` + `TestingDbContext` lewat `LongMemoryStore.CreateTestingAsync`, tabel di `MemorySchema`, migrasi EF untuk DB lokal |
| Service | `ITestAccountService` / `TestAccountService` (CRUD, setting, `ForBriefAsync`); `Testing/TestAccountText` (static: bagian brief) |
| Login as (1b) | `Testing/ImpersonationScanner` (static: pola di kode); `IImpersonationDetector` / `ImpersonationDetector` (scan → model murah → simpan); field `impersonation` di `report_test` |
| Orchestration | `TesterTools.SwitchUser`; `TesterAgent.SwitchUserAsync`; `TestRun` menyimpan akun aktif + cookie jar per akun; `ITestBrowserLauncher.LaunchAsync(profile)`; `TestScenario.Actor` + validasi di `PlanValidator`; bagian `# Test accounts` di brief planner dan Tester; `ask_login` untuk password dev → simpan ke `TestingSettings` |
| API/UI | `TestAccountsController`; kartu *Akun uji* di `pages/project.js` |

### Test

- Store: CRUD, unik per `ProjectKey`, atribut JSON dinamis, export/import.
- Validator: aktor skenario harus akun atau peran yang dikenal.
- Tester (browser palsu + `LocalApp`): `switch_user` membuka profil per akun, cookie HTTP terpisah per akun, placeholder
  terisi dan ter-mask, password dev bawaan → ditolak → `ask_login` → disimpan → ulang.
- Laporan: aktor per skenario, catatan password dev bawaan.

---

## Fase 2: test lanjutan yang disarankan (gambaran)

- `report_test` mendapat bagian opsional `follow_ups`. Tiap saran berisi judul, alasan, langkah dengan aktor
  (username atau peran), data yang dibutuhkan, dan hasil yang diharapkan. Contoh: *"Approve level 1 oleh atasan
  pengaju"*, *"Edit oleh pengaju sebelum di-approve → approval harus mulai lagi"*, *"Reject oleh HR → kembali ke
  pengaju"*.
- Laporan menampilkan *🧭 Test lanjutan yang disarankan* bernomor. Developer menjalankannya dengan membalas
  `jalankan test lanjutan 1` di chat, atau dengan tombol di halaman thread. Yang terjadi adalah thread uji tanpa
  perubahan kode, dengan saran itu sebagai bagian `test`.
- **Nilai disambung antar langkah**: tool Tester `remember(name, value)` (mis. nomor dokumen `REQ-0123` dari halaman
  sukses submit) dan placeholder `{{nama}}` di langkah berikutnya. Nilai ikut disimpan di thread, sehingga test lanjutan
  dari thread itu memakai dokumen yang sama.
- Bila saran butuh akun yang belum ada (mis. *"butuh akun HR"*), laporan menyebutnya dan menautkan ke kartu *Akun uji*.

## Fase 3: skenario multi-langkah dan Excel dinamis (gambaran)

- Saran yang butuh data dari developer (siapa approver tiap level, nilai form) menjadi **skenario**. Model menyusun
  struktur JSON-nya; **AIT menulis Excel-nya** (library dipilih saat implementasi, mis. ClosedXML/MIT):
  - **Panduan**: penjelasan skenario dan cara mengisi; sel kuning wajib diisi.
  - **Akun**: akun per peran yang dibutuhkan, terisi dari akun uji project bila ada; kolom atribut dinamis.
  - **Langkah**: urutan, aktor (dropdown dari sheet Akun), aksi (dropdown: submit/approve/reject/edit/revisi/…), kolom
    field khusus form itu, hasil yang diharapkan.
  - Sheet tersembunyi: ID skenario + versi, untuk impor balik.
- Dua jalur: dari CLI, AIT menulis `.aithread/testing/<skenario>.xlsx` dan menyebut path-nya; developer mengisi lalu
  membalas *"lanjut test <skenario>"*. Dari Web UI: tombol *Unduh Excel* / *Unggah*.
- Impor = validasi per baris (akun tidak ada, aksi tidak dikenal, sel wajib kosong) lalu simpan ke tabel skenario di
  database (keputusan 1). Skenario bisa dijalankan ulang oleh developer mana pun tanpa mengisi lagi; impor ulang menaikkan
  versinya. Akun baru di sheet *Akun* ikut masuk ke akun uji project setelah dikonfirmasi.
- **Peringatan efek samping** sebelum skenario pertama dijalankan di project: submit/approve sungguhan bisa mengirim
  email atau notifikasi ke orang asli bila DB dev berisi salinan data production. Dikonfirmasi sekali per project
  (`SideEffectsAcknowledgedBy`).
- **Penanda data uji**: field teks bebas diisi dengan awalan `[AIT TEST]`, supaya data uji mudah dicari dan dibersihkan.
- Satu langkah = satu sesi Tester dengan batas langkahnya sendiri, supaya alur panjang (3 approver × beberapa tindakan)
  tidak menabrak batas 40 langkah dan bisa dilanjutkan dari langkah yang gagal.

## Urutan implementasi fase 1

1. **1a: akun uji + Password dev** (data, service, API, kartu UI, export/import) dan `switch_user` dengan login biasa.
2. **1b: login as**: scan + model murah + pengamatan Tester, kolom `Impersonation`, koreksi/konfirmasi di UI, dan
   `switch_user` memakainya bila tersedia.

Pertanyaan terbuka dari rancangan awal sudah dijawab dan masuk ke tabel keputusan (4–7).
