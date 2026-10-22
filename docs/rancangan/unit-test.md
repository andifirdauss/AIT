# Rancangan: Unit test di AIT

> Status: **sudah diimplementasikan** (2026-10-21). Perilaku yang berlaku ada di dokumen pengguna:
> [07-cara-kerja.md → Unit test](../07-cara-kerja.md#unit-test), [05-menu.md → Unit test](../05-menu.md#unit-test),
> [06-memory.md → Export & import](../06-memory.md#export--import), [09-referensi-api.md](../09-referensi-api.md#unit-test),
> dan [08-troubleshooting.md](../08-troubleshooting.md#unit-test). Dokumen ini menyimpan rancangan awal sebagai catatan
> keputusan dan tidak tampil di menu Docs.
>
> Bagian yang berbeda di implementasi:
> - Tabel katalog punya model sendiri (`UnitTestDb`: `IUnitTestDb`, `UnitTestDbContext`, `UnitTestModel`), tetap di
>   database long memory lewat `LongMemoryStore.CreateUnitTestsAsync` dan dibuat `MemorySchema` di DB eksternal.
> - Bagian brief planner bernama `# Unit tests`; triase Tester menjawab JSON (prompt `AgentPrompts.TesterTriage`),
>   bukan tool `report_triage`.
> - Daftar test dibaca statis dari kode (Roslyn / pola), tidak lewat `--list-tests` runner. Komponennya
>   `UnitTestScanner` + `TestDiscovery` dan `UnitTestSelection` (static), bukan `UnitTestDiscovery`/`UnitTestSelector`.
> - Runner `npm test` tanpa hasil per test: semua test suite itu lolos atau gagal bersama (bukan *Skipped*).
> - Export v3 membawa katalog + snapshot, tanpa riwayat `UnitTestRuns`.
> - *Ulangi yang gagal* (retry thread) tidak menjalankan unit test.

## Latar belakang

Kondisi AIT saat ini terkait test:

- Executor sudah bisa menulis file test seperti file lain, dan build verifikasi ikut meng-compile project test yang ada
  di solution. Tetapi tidak ada yang **menjalankan** test. Prompt planner menyebut *"running tests … is up to the
  developer"*.
- Node Tester hanya menguji aplikasi yang sedang berjalan lewat browser atau HTTP ke localhost, dan hanya setelah ada
  perubahan yang berhasil. Permintaan "tolong tes fitur X" tanpa perubahan kode berakhir di jalur *Planner menjawab
  langsung* (0 tugas), sehingga Tester tidak pernah dipanggil.
- Bagian *Auto Testing* di prompt planner berisi *"Do not plan tasks for tests"*. Kalimat ini terlalu umum, sehingga
  planner bisa menolak membuat unit test walaupun developer memintanya.

Tujuannya: setiap project punya **katalog unit test bersama** (lintas developer, lewat `ProjectKey`). Developer
berikutnya yang bekerja di project yang asing menjalankan test peninggalan developer sebelumnya, sehingga perubahannya
tidak merusak perilaku yang sudah dijaga.

## Keputusan

| # | Keputusan |
|---|---|
| 1 | Katalog berisi **semua** test project, baik yang dibuat lewat AIT maupun yang sudah ada di repo (*Scan test*). Berlaku untuk semua stack yang didukung, tidak hanya .NET. |
| 2 | Yang dijalankan hanya **test yang relevan** dengan file yang diubah, bukan seluruh suite. Seluruh suite hanya jalan lewat mode `Semua` atau tombol *Jalankan semua*. |
| 3 | Test `Obsolete` **tidak dijalankan**, wajib punya **alasan**, dan perlu **konfirmasi developer** sebelum dihapus. |
| 4 | Menjalankan test dikerjakan **AIT tanpa model**. Planner memutuskan status test per perubahan. Tester (model mahal) melakukan **triase kegagalan** dan testing UI/API. |
| 5 | Node Tester **wajib bersyarat**: wajib bila katalog unit test atau Auto Testing aktif. Tanpa Tester, AIT memakai aturan mekanis (pass → fail = perbaiki kode). |
| 6 | Kasus test dirancang **planner** dari request, kode test ditulis **executor** dari contract (paralel dengan implementasi), lalu diperiksa **reviewer**. |
| 7 | Test di katalog yang filenya tidak ada di repo developer **dipulihkan otomatis**, karena test adalah bagian standar project dan wajib ada. |
| 8 | Isi file test disimpan sebagai **snapshot** di katalog untuk pemulihan. Snapshot **tidak tampil di UI** dan tidak dikirim lewat API. |
| 9 | Planner mendapat tool `run_tests`, dan tahap verifikasi menjalankan test setelah build lulus. Bisa dimatikan lewat setting. |
| 10 | Kalimat *"Do not plan tasks for tests"* diganti menjadi aturan: tambah test hanya bila developer memintanya atau setting `Otomatis` berlaku. |
| 11 | Perbaikan regresi unit test dibatasi **3 putaran**. Setelah itu sisa regresi dilaporkan, tidak diperbaiki lagi. |
| 12 | Test yang tidak bisa jalan karena lingkungan (DB, jaringan, config tidak tersedia) hasilnya **`Skipped` dengan alasan**, bukan `Failed`, dan tidak dianggap regresi. |
| 13 | Katalog unit test, **termasuk snapshot**, ikut **export/import memory** (`aithread-memory`), supaya tim tanpa DB eksternal bersama tetap bisa berbagi katalog. |

## Dukungan stack

Satu parser inti untuk **JUnit XML** (dipakai hampir semua runner), ditambah TRX untuk .NET dan JSON untuk Go.
Perintahnya tetap per stack dan tidak pernah berasal dari model, sama seperti `BuildCommand`.

| Stack | Jalankan | Daftar test | Format hasil |
|---|---|---|---|
| .NET | `dotnet test --logger trx` (+ `--filter` per test) | `dotnet test --list-tests` | TRX |
| .NET Framework | `vstest.console.exe /Logger:trx` | `vstest.console.exe /ListTests` | TRX |
| Java Maven | `mvn -q -B test` (+ `-Dtest=`) | statis (`@Test`) | JUnit XML (surefire) |
| Java Gradle | `gradle test` (+ `--tests`) | statis (`@Test`) | JUnit XML |
| Node (jest / vitest / mocha) | runner dideteksi dari `package.json` | `jest --listTests`, `vitest list` | JUnit / JSON |
| Python | `pytest --junitxml` (+ node id) | `pytest --collect-only -q` | JUnit XML |
| PHP | `phpunit --log-junit` (+ `--filter`) | `phpunit --list-tests` | JUnit XML |
| Go | `go test -json` (+ `-run`) | `go test -list` | JSON |
| Rust | `cargo test` (+ nama) | `cargo test -- --list` | teks |

- Node dengan script `npm test` yang bukan jest, vitest, atau mocha: hanya **exit code** yang terbaca, tanpa hasil
  per test. Test seperti itu ditandai *hasil per test tidak tersedia*.
- Deteksi statis untuk *Scan test* memakai `CSharpOutline` dan `PatternOutline` yang sudah ada (`[Fact]`, `[Test]`,
  `[TestMethod]`, `@Test`, `def test_`, `it(` / `test(`, `func Test`).

## Katalog (DB bersama)

Tabel katalog ikut **store long memory / katalog** (`IMemoryCatalogDb`, `MemoryCatalogModel`) supaya ikut pindah ke DB
eksternal dan terbaca developer lain. Scope-nya `ProjectKey`. Tabel baru dibuat oleh `MemorySchema` di DB eksternal,
sama seperti `ProjectModules` / `CodeMapEntries`. Mapping-nya harus netral provider (SQL Server dan PostgreSQL, tanggal
UTC).

### `UnitTestCases`

| Kolom | Isi |
|---|---|
| `Id`, `ProjectKey` | |
| `Number` | `UT-0001`. Urut per project dan tidak pernah dipakai ulang, termasuk setelah test dihapus |
| `Title` | Judul dalam Bahasa Indonesia yang bisa dibaca orang bisnis |
| `Purpose` | Aturan atau perilaku yang dijaga, 1-2 kalimat |
| `Scenario` | Kasus terstruktur: kondisi awal, input, hasil yang diharapkan. Dipakai untuk tulis ulang dan triase |
| `Module`, `Layers`, `Keywords` | Label yang sama dengan memory (`MemoryLabels`) |
| `TargetFiles`, `TargetSymbols` | Kode yang diuji. Kunci seleksi test relevan dan analisa validitas |
| `Framework`, `TestProject`, `TestFile`, `Selector` | Mis. xUnit, `tests/X.Tests/X.Tests.csproj`, path file, FQN untuk filter |
| `Requires` | `none` / `database` / `network` / `config`. Test dilewati, bukan dianggap gagal, di mesin tanpa kebutuhan itu |
| `State` | `Active` / `Quarantined` / `Obsolete` / `Removed` |
| `Origin` | `Ait` (dibuat lewat thread) / `Scanned` (sudah ada di repo) / `Regenerated` (ditulis ulang dari `Scenario`) |
| `CreatedBy`, `CreatedThreadId`, `CreatedAt` | Developer dan thread asal |
| `ObsoleteReason`, `ObsoleteThreadId`, `ObsoleteBy`, `ObsoleteAt` | Wajib terisi saat `Obsolete` |
| `LastResult` | `Passed` / `Failed` / `Error` / `Skipped` / `NotFound` / `Unknown` |
| `LastRunAt`, `LastRunBy`, `LastDurationMs`, `LastMessage` | Potongan pesan gagal |
| `LastSkipReason` | Wajib terisi bila `LastResult = Skipped`, mis. *"Butuh database; koneksi `NewESS` tidak tersedia di mesin ini"* |
| `LastInputHash` | Hash isi file test + file target saat `LastResult` dicatat. Untuk cache baseline |
| `SourceSnapshot`, `SourceHash`, `SourceCommit`, `SourceBranch` | **Internal**: isi file test untuk pemulihan |
| `MethodSnapshot` | **Internal**: kode method test saja, untuk menyisipkan ke file yang sudah ada |
| `SupportFiles` | **Internal**: snapshot helper, fixture, file project test, dan config runner yang dibutuhkan |

### `UnitTestRuns`

Riwayat per test: `UnitTestCaseId`, `ThreadId`, `Phase` (`baseline` / `after` / `manual` / `scan`), `Result`,
`DurationMs`, `Message`, `SkipReason`, `Machine`, `RunAt`. Disimpan N terakhir per test. Dipakai untuk deteksi flaky dan bukti di
Thread Detail.

### Snapshot tidak tampil

- DTO katalog **tidak memuat** `SourceSnapshot`, `MethodSnapshot`, dan `SupportFiles`. Snapshot tidak tampil di
  Project > Detail maupun di `/api/*`. Satu-satunya jalan keluar snapshot adalah file export memory (lihat
  [Export/import](#exportimport-memory)).
- Snapshot hanya dibaca oleh `UnitTestRestorer`.
- Setting per project **Simpan snapshot test** (default menyala) untuk project yang kodenya tidak boleh ada di DB
  bersama. Tanpa snapshot, pemulihan memakai lapis 1 dan 3.
- Snapshot diperbarui setiap kali test **lulus** di fase `after` atau `manual`, dari isi file saat itu.

### Export/import memory

Untuk tim yang setiap developernya memakai DB lokal, katalog dibagikan lewat file export memory yang sudah ada
(`MemoryTransferService`).

- Format naik ke **`aithread-memory` v3** dengan bagian baru `unitTests` (katalog + snapshot) dan `unitTestRuns`
  (opsional, hanya N terakhir). Import tetap menerima v2.
- Project dicocokkan lewat `ProjectKey`, sama seperti memory.
- Import **idempoten per id + isi**:
  - Test baru → ditambahkan.
  - Test yang sama → diperbarui hanya bila datanya lebih baru (`LastRunAt` / `SourceHash` berbeda dan lebih baru).
  - Bentrok nomor (`UT-0012` di file ≠ `UT-0012` lokal untuk test lain) → test dari file mendapat nomor berikutnya, dan
    pemetaannya dicatat di hasil import.
- `State` `Removed` ikut terbawa, supaya test yang sudah dihapus tim tidak muncul lagi lewat pemulihan otomatis.
- Pilihan export: *Sertakan unit test* (default menyala). Snapshot hanya ikut bila setting **Simpan snapshot test**
  project itu menyala.
- Hasil dry run import menampilkan jumlah test baru, diperbarui, dan bentrok nomor, tanpa menampilkan isi snapshot.

## Setting per project

| Setting | Pilihan | Default |
|---|---|---|
| Katalog unit test | `Off` / `Relevan saja` / `Semua` | `Relevan saja` bila project punya test |
| Buat unit test | `Hanya saat diminta` / `Otomatis untuk perubahan Logic` / `Off` | `Hanya saat diminta` |
| Simpan snapshot test | menyala / mati | menyala |
| Batas waktu run test | detik | sama dengan batas build |

## Alur di pipeline

```text
0. Pemulihan otomatis test yang hilang (AIT)
1. Planner  ──┬── baseline test relevan (AIT, paralel, file belum berubah)
              │
2. Eksekusi (implementasi + file test, paralel)
3. Build
4. Unit test (AIT) ── ada pass → fail? ── Tester triase ── fix task ── ulangi 3-4
5. Testing UI/API (Tester, bila Auto Testing)
6. Wrapper + catat hasil ke katalog
```

### 0. Pemulihan otomatis

Sebelum baseline, AIT membandingkan katalog dengan repo developer. Test `Active` yang file atau method-nya tidak
ditemukan dipulihkan **otomatis** lewat tiga lapis, berhenti di lapis pertama yang berhasil:

1. **Cek git (tanpa model).** Kalau `SourceCommit` / `SourceBranch` belum ada di branch developer, AIT mencatat
   *"UT-0012 ada di commit `abc123` branch `feature/x`, belum ada di branch Anda"* di laporan. Pemulihan tetap lanjut ke
   lapis 2, supaya pengamannya ada sekarang juga, dan saran pull atau merge tetap ditampilkan.
2. **Snapshot (tanpa model, persis).**
   - File tidak ada: tulis `SourceSnapshot` apa adanya, beserta `SupportFiles` yang juga hilang.
   - File ada, tetapi method-nya hilang: sisipkan `MethodSnapshot`. Untuk C#, AIT menyisipkan lewat Roslyn seperti
     `extract`. Untuk stack lain, dibuat task executor kecil dengan kode method lengkap sebagai sumber yang disalin.
   - Project test tidak ada di solution: `dotnet sln add` (perintah tetap).
   - File yang ada **tidak pernah ditimpa**.
3. **Tulis ulang dari `Scenario` (pakai model, cadangan terakhir).** Executor menulis test baru dari `Scenario`, dan
   `Origin` menjadi `Regenerated`. Test ini tidak dianggap regresi kalau gagal di baseline, dan perlu konfirmasi
   developer sebelum dipercaya penuh.

Catatan:
- Pemulihan dicatat di activity log thread dan di bagian *Unit test* laporan akhir, tetapi **tidak ikut rollback
  thread**, karena bukan perubahan dari request. File hasil pulihan muncul sebagai file baru di `git status`
  developer, untuk di-commit.
- Test hasil pulihan yang gagal di baseline dilaporkan sebagai *sudah gagal sebelum perubahan* (kode developer sudah
  berbeda dari saat test dibuat), tidak sebagai regresi.

### 1. Baseline

- **Test relevan** = test `Active` yang `TargetFiles` / `TargetSymbols` / `Module`-nya beririsan dengan file yang
  disentuh. Saat baseline jalan, rencana belum ada, jadi perkiraan awalnya diambil dari file di prompt, code map
  `PromptCodeMapAsync`, dan modul hasil label. Setelah rencana jadi, test relevan tambahan dijalankan sebelum eksekusi
  dimulai.
- **Cache**: bila `LastInputHash` sama dengan hash isi file saat ini, `LastResult` dipakai tanpa menjalankan test.
- Baseline jalan **paralel dengan planner** karena file belum berubah. Eksekusi menunggu baseline selesai.
- Mode `Semua`: seluruh suite `Active`, tetap memakai cache.

### Planner

- Brief planner mendapat bagian **Registered tests**: test relevan saja, berisi nomor, judul, tujuan, target, dan
  status terakhir.
- `submit_plan` mendapat bagian `tests`:

| Keputusan | Arti | Akibat |
|---|---|---|
| `keep` | Perilaku tidak berubah | Harus tetap lulus. Kalau gagal, yang diperbaiki **kode** |
| `update` | Perilaku **sengaja** diubah oleh request | Task edit file test di rencana yang sama + alasan yang mengutip request |
| `obsolete` | Fitur dihapus | `State = Obsolete` + `ObsoleteReason`. Tidak dijalankan, menunggu konfirmasi |
| `new` | Test baru | Judul, tujuan, `Scenario`, target, file tujuan + task create/edit file test |

- Bila `update` atau `obsolete` tidak jelas dari request, planner memakai `ask_user`. Ini sesuai aturan (b) yang sudah
  ada: menghapus fitur atau melampaui maksud request.
- Prompt tanpa perubahan (0 tugas) tidak dianalisa.
- Aturan membuat test:
  - Ikuti framework dan gaya project test yang ada. Lampirkan satu file test yang ada sebagai referensi task.
  - Project tanpa project test: **jangan dibuat diam-diam**, tanya dulu lewat `ask_user` kecuali developer memintanya.
  - Kasus dirancang dari request dan aturan bisnis, bukan dari implementasi.
- Tool **`run_tests(filter?)`**: baca saja, untuk request yang memang soal test (*"perbaiki test yang gagal"*,
  *"kenapa UT-0012 gagal"*). Tidak bisa didelegasikan ke Tools Node.

### 2-3. Eksekusi dan build

- Task file test jalan paralel dengan task implementasi. Executor hanya melihat contract, bukan kode implementasinya.
- Reviewer ikut memeriksa file test: assert kosong, test yang selalu lulus, mock yang menguji dirinya sendiri. Aturan
  *"do not report missing tests"* tetap berlaku untuk test yang tidak diminta.

### 4. Unit test setelah perubahan

- Jalankan ulang test relevan, test `update`, dan test `new`.
- Test yang gagal **diulang sekali**. Kalau lalu lulus, ditandai flaky dan diusulkan `Quarantined`.
- Penilaian:
  - `keep` / `update`: pass di baseline → fail sekarang = **regresi**.
  - `new`: gagal = implementasinya atau test-nya yang salah.
  - Gagal di baseline dan masih gagal = *sudah gagal sebelumnya*, hanya dilaporkan.
- **Triase oleh Tester** (hanya bila ada kegagalan, jadi semua lulus = 0 token Tester). Tester melihat request,
  rencana, diff, keputusan `tests`, kode test yang gagal, dan pesan gagalnya, lalu memutuskan per test:
  - `regression` → fix task ke kode.
  - `test_wrong` → fix task ke file test. Hanya boleh untuk test `new` atau `update` di thread ini, bukan test `keep`
    milik developer lain.
  - `environment` → tidak dianggap salah perubahan (DB, config, atau jaringan tidak tersedia).
  - `flaky` → usul `Quarantined`.
  - `plan_wrong` → perilaku memang sengaja berubah tetapi planner menandai `keep`. Test dijadikan `update` dengan
    alasan, dan developer diberi tahu di laporan.
- **Tanpa node Tester**: semua pass → fail menjadi fix task ke kode, tanpa triase.
- Fix task memakai siklus verifikasi yang sudah ada (`V<n>-<m>`, `FixTaskFactory`). Perbaikan regresi unit test
  dibatasi **3 putaran** (build + unit test diulang tiap putaran). Regresi yang tersisa setelah putaran ke-3 dilaporkan
  di laporan akhir beserta triase terakhirnya.

### Test yang dilewati (Skipped)

Hasil `Skipped` **selalu disertai alasan** (`SkipReason`) dan tidak pernah dihitung sebagai regresi. Sumbernya:

| Sumber | Contoh alasan |
|---|---|
| `Requires` tidak terpenuhi, dicek sebelum menjalankan (connection string kosong, host tidak bisa dihubungi) | *"Butuh database; connection string `DefaultConnection` kosong di mesin ini"* |
| Gagal karena lingkungan, dikenali dari pesan error (koneksi DB ditolak, timeout jaringan, file config tidak ada) atau dari triase Tester `environment` | *"Koneksi ke SQL Server `10.0.0.5` ditolak"* |
| Di-skip oleh test-nya sendiri (`[Fact(Skip = "...")]`, `@Disabled`, `pytest.skip`, `it.skip`) | Alasan dari atribut atau anotasinya, atau *"Di-skip di kode test tanpa alasan"* |
| Hasil per test tidak tersedia (runner Node yang tidak dikenali) | *"Runner `npm test` tidak mengeluarkan hasil per test"* |
| `Quarantined` (flaky) | *"Dikarantina: tidak stabil (lulus 2 dari 4 run terakhir)"* |

Kalau gagal pertama kali karena lingkungan, `Requires` test itu diisi otomatis (mis. `database`). Run berikutnya di
mesin yang sama langsung `Skipped` dengan alasan tanpa harus menunggu koneksi timeout.

### 6. Laporan dan katalog

- `FinalReport` mendapat baris *Unit test*: lulus / regresi / sudah gagal sebelumnya / dipulihkan / dilewati.
- Katalog diperbarui: `LastResult`, riwayat `UnitTestRuns`, snapshot (yang lulus), test `new`, `Selector` yang baru
  ditemukan, dan `Obsolete` beserta alasan.

## Mode "test saja"

Request yang hanya meminta pengujian tanpa perubahan kode (*"tolong tes fitur Homecoming pakai akun X"*):

- Planner mengirim `submit_plan` dengan `tasks: []` ditambah `test` (UI/API) dan/atau filter unit test.
- `ExecuteAsync`: cabang 0 tugas tidak langsung selesai bila ada `test` atau unit test. Tahap unit test dan/atau Tester
  dijalankan tanpa eksekusi, build, atau review.
- Brief Tester mendukung "tanpa diff": yang dites adalah fitur yang sudah ada.
- Kredensial dari dokumen yang ditunjuk developer boleh masuk ke `notes` sebagai sumber yang sah. Aturan *"never invent
  credentials"* tetap berlaku.

## Node Tester wajib bersyarat

- Setup dan menu Node menampilkan peringatan bila katalog unit test atau Auto Testing aktif tetapi belum ada node
  Tester aktif.
- Preset Setup `AIT Tester` memakai **model kuat**, sama kelasnya dengan planner.
- Project tanpa test dan tanpa Auto Testing tetap berjalan tanpa Tester.

## UI dan API

- **Project > Detail → Unit test**: tabel nomor, judul, modul, status, hasil terakhir (kapan, oleh siapa), dan asal
  test. Filter per status dan modul. Tombol *Scan test*, *Jalankan semua*, dan *Jalankan* per test. Untuk `Obsolete`:
  alasan + tombol *Kembalikan* / *Konfirmasi hapus*. Konfirmasi hapus membuat thread kecil yang menghapus kode test
  (tercatat dan bisa di-rollback). Snapshot tidak pernah ditampilkan.
- **Thread Detail**: bagian *Unit test* berisi hasil baseline vs setelah perubahan, triase Tester, dan test yang
  dipulihkan.
- **API** (`/api/projects/{id}/unit-tests`): daftar, scan, jalankan semua atau per test, kembalikan / konfirmasi
  obsolete, ubah setting. Response tidak pernah berisi snapshot.

## Komponen (mengikuti konvensi Service)

| Komponen | Lokasi | Isi |
|---|---|---|
| `UnitTestCase`, `UnitTestRun`, enum | `AIThread.Common/Entities`, `Enums` | Entity katalog |
| Mapping + skema | `MemoryCatalogModel`, `IMemoryCatalogDb`, `MemorySchema` | Ikut store katalog |
| `TestCommand` | `Verification/` | Perintah tetap per stack (pola `BuildCommand`) |
| `TestRunner` + `ITestRunner` | `Verification/TestRunner/` | Proses + timeout (pola `BuildRunner`) |
| `TestResultParser` | `Verification/` | TRX, JUnit XML, JSON Go/jest/vitest |
| `UnitTestCatalog` + interface | `UnitTests/UnitTestCatalog/` | CRUD, penomoran, pembaruan hasil dan snapshot |
| `UnitTestDiscovery` + interface | `UnitTests/UnitTestDiscovery/` | *Scan test*: daftar dari runner + outline statis, label lewat `MemoryAi` |
| `UnitTestSelector` + interface | `UnitTests/UnitTestSelector/` | Test relevan dari code map + cache hash |
| `UnitTestRestorer` + interface | `UnitTests/UnitTestRestorer/` | Pemulihan otomatis tiga lapis |
| `UnitTestStage` + interface | `Orchestration/UnitTestStage/` | Baseline, run setelah perubahan, penilaian, triase |
| Triase | `TesterAgent` + tool `report_triage` | Prompt `AgentPrompts.TesterTriage` |
| Planner | `PlannerToolRunner.RunTestsAsync`, brief *Registered tests*, `submit_plan.tests` | |

## Urutan implementasi

1. Perbaiki kalimat *"Do not plan tasks for tests"* di `AgentPrompts.PlannerTesting`.
2. `TestCommand` + `TestRunner` + `TestResultParser` untuk semua stack di tabel, dengan unit test parser.
3. Tool planner `run_tests`.
4. Tahap unit test di verifikasi (tanpa katalog dulu: run test project yang tersentuh, pass → fail = fix task) +
   setting.
5. Katalog: entity, skema DB eksternal, `UnitTestCatalog`, *Scan test*, UI Project > Detail, API.
6. Planner: brief *Registered tests* + `submit_plan.tests` + registrasi `new` / `obsolete`.
7. Baseline paralel + cache + seleksi relevan.
8. Triase Tester + aturan Tester wajib bersyarat di Setup/Node.
9. Snapshot + pemulihan otomatis tiga lapis.
10. Export/import memory v3 dengan bagian `unitTests`.
11. Mode "test saja".
12. Pindahkan isi rancangan ini ke dokumen pengguna di `docs/`.
