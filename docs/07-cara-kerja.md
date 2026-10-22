# 7. Cara kerja

Dokumen ini menjelaskan apa yang terjadi di balik satu prompt, agar Anda bisa menilai hasil dan menyetel
konfigurasi dengan tepat.

## 1. Klasifikasi request

Agen coding mengirim banyak request, tidak hanya prompt Anda: Claude Code ke `POST /v1/messages`, dan klien lain ke
`/v1/chat/completions`, `/v1/responses` (Codex), atau `/v1beta/models/…` (Gemini CLI) — lihat
[Klien lain](03-integrasi-claude-code.md#klien-lain-codex-gemini-cli-oh-my-pi-dll). Semuanya lebih dulu diubah ke satu
bentuk internal (`ProtocolAdapter`), jadi aturan di bawah sama untuk semua format. AIT **mengorkestrasi**
sebuah request hanya bila semua syarat terpenuhi:

- *Orkestrasi aktif* menyala di Setting;
- request membawa daftar `tools` (ciri request agen utama);
- pesan terakhir berasal dari user, **bukan** `tool_result`;
- teks prompt tidak kosong setelah blok `<system-reminder>` dibuang;
- prompt bukan permintaan sampingan Claude Code: `<title-request>` (menamai sesi) dan `<recap>` (rangkuman saat
  developer kembali) dikirim Claude Code lengkap dengan `tools`, tetapi bukan tugas untuk planner;
- **sesi berasal dari sebuah project**, yaitu folder dengan `.git` atau file build (lihat bagian 2).

Teks yang dilampirkan sebagai blok `document` bersumber teks (tempelan atau lampiran file teks) ikut menjadi bagian
prompt, dibungkus `<document title="…">`; dokumen biner (mis. PDF) tidak dibaca.

Selain itu (judul sesi, rangkuman sesi, `/compact`, cek kuota, sesi di luar project, dll.) request diteruskan apa adanya ke
endpoint 9router yang sama dengan **model passthrough** — AIT tidak membuat thread dan tidak memanggil planner. `POST /v1/messages/count_tokens` juga diteruskan; bila gagal, jumlah token
diperkirakan (±4 karakter per token).

Jika *Proxy API key* diisi, request tanpa key yang cocok (`Authorization: Bearer` atau `x-api-key`) ditolak `401`.

## 2. Cek sesi: project atau bukan

Sebelum planner dipanggil, AIT memeriksa **sesi** asal prompt:

1. Id sesi diambil dari header `X-AIThread-Session` / `x-claude-code-session-id` / `x-session-id`, atau dari
   `metadata.user_id`; working directory dari header `X-AIThread-Cwd`, atau dari baris `Primary working directory:` /
   `Working directory:` / `Current working directory:` / `cwd:` (atau tag `<cwd>`) di system prompt.
2. **Project = folder root** yang berisi `.git` atau file build, tanpa memandang bahasanya: `.sln`/`.slnx`/`.csproj`,
   `pom.xml`, `build.gradle(.kts)`, `package.json`, `pyproject.toml`/`requirements.txt`, `go.mod`, `Cargo.toml`,
   `composer.json`, `Gemfile`, `pubspec.yaml`, `CMakeLists.txt`, dan sejenisnya. Yang dilihat hanya folder working
   directory dan **folder induknya** (hingga 6 tingkat), tidak pernah sub-foldernya. Urutan pemilihan:
   1. project yang sudah terdaftar dan memuat folder itu (yang rootnya paling dalam), agar project yang didaftarkan
      manual atau oleh versi lama tetap dikenali;
   2. root repository terdekat (folder `.git`);
   3. tanpa `.git`: file penghimpun paling luar (`.sln`/`.slnx`, `settings.gradle`, `pom.xml` berisi `<modules>`,
      `package.json` berisi `workspaces`, `pnpm-workspace.yaml`, `go.work`, dst.);
   4. lainnya: manifest terdekat.

   Header `X-AIThread-Project` (folder root) menjadi titik awal pencarian menggantikan working directory (bila tidak
   menghasilkan project, working directory dipakai); header lama `X-AIThread-Solution` (path `.sln`, foldernya yang
   dipakai) tetap diterima. Drive, folder Home, dan folder sistem tidak pernah menjadi project, walau ada `package.json` atau
   `.git` nyasar di sana.
3. **Project ditemukan** → didaftarkan otomatis bila belum ada (prompt pertama sesi), lalu prompt diorkestrasi.
   **Root workspace** = folder root project, sehingga sesi yang dibuka di `repo/services/api` tetap bekerja pada
   `repo`; planner diberi tahu bahwa sesi dibuka di `services/api`.
4. **Tidak ada marker** (atau working directory tidak ada/tidak diketahui) → sesi **diabaikan**: setiap prompt-nya
   diteruskan langsung ke 9router dengan model passthrough, sehingga Claude Code tetap bekerja sebagai agen biasa. Tidak
   ada thread, project, maupun memory yang dibuat, misalnya untuk folder kosong tempat Anda hanya meminta dibuatkan
   sebuah file teks. Folder induk yang hanya berisi beberapa project (mis. `a`, `b`, `c`) juga diabaikan karena
   ambigu, dan log menyebut nama project di dalamnya agar Anda membuka sesi di salah satunya.

Stack ditentukan dari manifest yang ditemukan (kedalaman 3): `.NET`, `Java (Maven)`, `JVM (Gradle)`, `Node.js`,
`Python`, `Go`, `Rust`, `PHP`, `Ruby`, dan lainnya; satu project boleh campuran. Stack dipakai untuk badge di menu
Project, brief planner, dan daftar folder build yang disembunyikan (mis. `target/` untuk Maven, `dist/` untuk Node.js).

Keputusan ini **diingat per sesi** selama 30 menit — prompt berikutnya di sesi yang sama tidak memindai disk lagi.
Sesi yang terhubung ke project diperpanjang setiap kali dipakai; sesi yang diabaikan dicek ulang setelah sekitar 5
detik (berguna bila Anda baru menambahkan `.git` atau file build). Log dashboard mencatat satu baris per keputusan:
`Sesi xxxxxxxx terhubung ke project …` atau `Sesi xxxxxxxx (…) bukan bagian project (alasannya) — prompt diabaikan orkestrasi…`.

Bila pengecekan gagal (mis. database mati), prompt juga diteruskan tanpa orkestrasi dan kesalahannya dicatat di log.

## 3. Planner

Planner menerima **brief**, disusun dari bagian paling stabil ke yang paling spesifik (agar prompt cache provider
bisa dipakai ulang antar langkah dan antar thread, dan permintaan berada setelah semua bahan yang dirujuknya):

1. info project: nama, root, stack, daftar file solution/build yang ditemukan, dan perintah build yang bisa dijalankan
   AIT (lihat [Verifikasi](#5-verifikasi-build-dan-reviewer));
2. struktur folder 2 tingkat (maks 200 entri), lalu folder tempat sesi dibuka (bila di bawah root). Folder sesi
   sengaja di bawah struktur: ia berbeda per sesi, sehingga bila di atas, struktur tidak ikut ter-cache antar sesi;
3. konteks yang disisipkan Claude Code (isi `CLAUDE.md`, reminder, file yang dilampirkan), maks 30.000 karakter;
4. **konteks sesi** dari Context Keeper bila chat ini sudah punya thread AIT sebelumnya (lihat
   [Konteks sesi](#konteks-sesi-context-keeper)), maks 12.000 karakter;
5. percakapan sebelumnya: tanpa konteks sesi, 12 pesan terakhir; dengan konteks sesi, hanya pesan setelah thread yang
   ringkasannya dipakai. Tiap pesan maks 1.500 karakter, total maks 12.000 karakter. Lalu **catatan investigasi**
   thread AIT terakhir di chat ini (lihat [Konteks sesi](#konteks-sesi-context-keeper)), maks 8.000 karakter;
6. **peta kode** file yang disebut permintaan (modul · layer), **peta memory** per modul, dan **indeks memory**
   (id + label, maks 25) hasil pencarian label: memory modul file tersebut lebih dulu, lalu diurutkan per keyword.
   Bila permintaan tidak menyebut file (lanjutan seperti "berlaku buat yang lainnya juga"), labelnya diambil dari
   sesi: file yang diubah lalu file yang dibaca planner thread AIT terakhir di chat ini (atau yang disebut
   prompt-nya), bila tidak ada dari
   percakapan terakhir lalu dari konteks sesi; prompt thread terakhir itu ikut dipakai sebagai keyword;
7. **outline** file-file tersebut (maks 6 file, 12.000 karakter): class, member, dan function beserta rentang
   `baris-awal-baris-akhir` dan signature-nya, dibaca langsung dari disk (sama dengan `code_outline`). Planner jadi
   tahu kerangka file sebelum membacanya, dan cukup membaca rentang baris yang dibutuhkan. File yang modulnya belum
   terpetakan tetap ikut;
8. **perubahan di sesi ini**: file yang diubah thread-thread AIT sebelumnya di chat ini (10 thread terakhir yang
   benar-benar mengubah file; thread yang hanya menjawab/membaca dan thread yang di-rollback tidak ikut), dikelompokkan
   per modul (file tanpa modul di peta kode dikelompokkan per thread), terbaru dulu, beserta unit test yang
   meng-cover-nya — lihat [Merujuk perubahan sebelumnya](#merujuk-perubahan-sebelumnya);
9. **unit test** dari [katalog unit test](#unit-test) project (bila aktif): suite, aturan membuat test baru, dan test
   yang terkait file/modul permintaan beserta hasil terakhirnya (maks 30);
10. permintaan terbaru developer (tanpa `<system-reminder>`), maks 40.000 karakter.

Bila sebuah bagian melebihi batas, yang dibuang adalah bagian tengahnya; awal dan akhir teks tetap ada.

Lalu planner bekerja dalam putaran tool (maks *Langkah planner*). Semua tool **read-only**, kecuali `run_build` yang
menjalankan build project (tidak mengubah kode; hanya ditawarkan bila *Build & verifikasi otomatis* menyala dan project
punya perintah build) dan `run_tests` yang menjalankan unit test katalog (juga tidak mengubah kode). Path relatif dihitung dari
root project; tool baca (`list_directory`, `find_files`, `search_text`, `find_symbol`, `find_references`, `code_outline` untuk file, `read_file`) juga menerima **path absolut di luar
root**, supaya planner bisa mengecek folder atau project lain ketika Anda memintanya (mis. "cek ke
`D:/project/otherproject`"). Di luar root hasilnya ditampilkan dengan path absolut:

| Tool | Batas |
|---|---|
| `list_directory(path, depth)` | depth 1–4, maks 300 entri |
| `find_files(pattern, path?)` | glob, mis. `**/*User*.cs`; maks 200 hasil; `path` = folder pencarian (default root; bila di luar root, pola relatif terhadap folder itu) |
| `search_text(query, glob?, regex?, path?)` | tidak peka huruf besar/kecil; file ≤ 1 MB; maks 80 hasil; `path` seperti `find_files` |
| `find_symbol(name, kind?, path?)` | lokasi deklarasi (class, method, property, field, enum, function, …) sebagai `file:baris: jenis signature (in Tipe)`; `name` boleh `Tipe.Member`; C# lewat Roslyn (syntax, tanpa build), JS/TS/Python/Java lewat pola regex (akurasi lebih rendah); maks 60 hasil |
| `find_references(name, path?)` | semua pemakaian identifier, komentar diabaikan (C#: string literal juga), baris deklarasi ditandai `[declaration]`; cocok berdasarkan nama saja; maks 120 hasil |
| `code_outline(path?, name?)` | kerangka kode tanpa isi, dari [Struktur kode](05-menu.md#project): **file** → setiap class/member/function dengan `baris-awal-baris-akhir` dan signature, dibaca dari disk saat itu; function yang panggilannya sudah tercatat diberi `[calls: N]` (`, changed since` bila isinya berubah sejak dibaca). **Folder** (`.` = seluruh project) → satu baris per file: class (jumlah member) dan function. **`name` saja** → class itu di seluruh project, termasuk semua file class `partial` (lebih dari 6 file → daftar lokasinya saja). C#, PHP, JS/TS, Python, Java, Kotlin; file ≤ 2 MB; maks 24.000 karakter, folder maks 300 file |
| `function_calls(path, name)` | panggilan satu function/method (`name` atau `Tipe.Member`) yang sudah dicatat [Panggilan function](#panggilan-function): baris, jenis, target, dan lokasi deklarasinya, plus **pemanggilnya** yang tercatat (maks 40). Belum tercatat atau isinya berubah → dimasukkan antrean Tools Node. Hanya file di dalam project; maks 5 overload |
| `read_file(path, startLine?, endLine?)` | bernomor baris; maks 600 baris per panggilan; file ≤ 2 MB |
| `search_memory(query)` | maks 25 label |
| `read_memory(ids)` | deskripsi lengkap |
| `read_thread(id)` | catatan lengkap thread sebelumnya di project ini (permintaan, rencana, tugas, balasan) |
| `git_status(path?)` | branch saat ini (ahead/behind upstream) dan setiap file staged, modified, dan untracked; maks 300 entri |
| `git_diff(path?, base?, staged?, stat?)` | unified diff. Default: semua yang belum di-commit (staged + belum) terhadap `HEAD`; `staged` = hanya index; `base` = commit/branch/tag atau range (mis. `main...HEAD`: perubahan branch ini sejak berpisah dari `main`); `stat` = daftar file + jumlah baris saja. File untracked tidak ada di diff (lihat `git_status`) |
| `git_log(path?, revision?, limit?, query?, change?, files?)` | commit terbaru dulu: hash, tanggal, author, subjek; default 20, maks 100. `path` = riwayat file/folder (rename diikuti), `query` = teks di pesan commit, `change` = commit yang menambah/menghapus teks itu di kode (`git log -S`), `files` = daftar file per commit |
| `git_show(commit, path?, stat?)` | satu commit: author, tanggal, pesan lengkap, daftar file, dan diff; `stat` = tanpa diff |
| `git_blame(path, startLine?, endLine?)` | commit, author, dan tanggal terakhir per baris (baris yang belum di-commit: `Not Committed Yet`); maks 200 baris per panggilan, file ≤ 2 MB |
| `run_build(target?)` | build project, hasilnya status + setiap error `file(baris,kolom): kode: pesan` (maks 40). `target` = solution/project/manifest lain di dalam root. Dipakai bila Anda minta build atau memperbaiki error build |
| `run_tests(tests?, file?, all?)` | hanya bila project punya [katalog unit test](#unit-test) aktif: menjalankan test katalog (nomor `UT-0012`, satu file test, atau semua) dan mengembalikan hasil per test (lolos, gagal + pesan, dilewati + alasan). Dipakai bila permintaan memang soal test |
| `investigate(question, agent?)` | hanya ada bila node planner [mendelegasikan tool ke tools node](02-konfigurasi.md#tools-node-planner); tools node menjalankan tool yang didelegasikan (yang hilang dari daftar ini) dalam loop sendiri, maks 12 langkah, lalu mengembalikan temuan ringkas maks 12.000 karakter. Di progres: `investigate  #1 "…"`, lalu baris `#1 › read_file …` per tool tools node, dan `#1 › complete  n langkah · token · model` (sejajar dengan kolom tool; bila gagal `#1 › failed  …`). Di progres CLI, path panjang diringkas menjadi `folder-awal/…/folder/file` (maks 48 karakter) dan query pencarian dipotong di 60 karakter agar satu tool tetap satu baris; log aktivitas menyimpan versi lengkap |
| `ask_user(question, options?)` | menghentikan thread dengan satu pertanyaan ke developer (lihat [Pertanyaan dari planner](#pertanyaan-dari-planner)); hanya ditawarkan bila *Planner boleh bertanya* menyala dan prompt berasal dari sesi yang bisa menjawab |
| `submit_plan(plan)` | mengakhiri planning |

Folder `.git .vs .idea .nuget bin obj node_modules packages TestResults artifacts` dan `wwwroot/lib/` disembunyikan
dari semua tool. Isi `.git` tidak pernah dibaca langsung; tool `git_*` menjalankan `git` itu sendiri.

**Tool git** hanya ditawarkan bila project berada di dalam repository git (ada `.git` di root atau folder induknya),
dan membutuhkan `git` di `PATH`. Kelimanya hanya membaca: argumennya tetap (tanpa shell), nama commit/branch yang
diawali `-` atau berisi spasi ditolak, path selalu ditaruh setelah `--`, dan git dijalankan tanpa lock opsional
(`GIT_OPTIONAL_LOCKS=0`, jadi `git_status` tidak menulis ulang index), pager, warna, diff eksternal, textconv, atau
cek signature, dengan batas waktu 30 detik. Planner memakainya bila permintaan menyinggung perubahan yang belum
di-commit, commit, atau branch ("review perubahan saya", "lanjutkan yang tadi"). Bila project ada di sub-folder
repository, hasilnya dibatasi ke folder project dan path-nya relatif terhadap root project. Commit, checkout, merge,
dan push tetap urusan Anda. Output git maks 24.000 karakter; bila terpotong, planner diberi saran mempersempit
(`path`, `stat`).

Beberapa tool dalam satu putaran dijalankan paralel. Daftar tool tidak berubah selama satu thread
(mengubahnya membatalkan prompt cache). Di langkah terakhir planner diminta memanggil `submit_plan`; hanya bila ia
mengabaikannya, satu langkah tambahan dijalankan dengan `submit_plan` sebagai satu-satunya tool.

### Pertanyaan dari planner

Seperti agen coding pada umumnya, planner bisa bertanya, tetapi **jarang**. Ia hanya bertanya bila:

- permintaan punya dua tafsiran wajar atau lebih yang menghasilkan perubahan berbeda nyata;
- rencananya akan menghapus data, mengubah/menghapus skema, membuang fitur, atau jauh melampaui permintaan, padahal
  Anda tidak memintanya dengan jelas; atau
- ia butuh fakta yang tidak ada di kode, memory, maupun percakapan (aturan bisnis, nilai, sistem eksternal).

Permintaan yang jelas tidak pernah dikonfirmasi ulang; hal yang bisa ditebak dari kode (nama, gaya) diputuskan sendiri
dan asumsinya disebut di ringkasan. Planner menyelidiki kode dulu agar pertanyaannya spesifik.

1. Planner memanggil `ask_user` dengan pertanyaan dan 2–4 opsi (rekomendasinya di urutan pertama). Thread berhenti
   dengan status **Menunggu jawaban**, dan balasannya adalah pertanyaan bernomor:

   ```
   ❓ Pertanyaan dari planner

   Kolom Deskripsi wajib diisi atau opsional?

   1. Opsional — NULL di database, tanpa validasi
   2. Wajib — NOT NULL, validasi di service dan form

   Balas dengan nomor pilihan atau tulis jawaban Anda sendiri.
   ```

2. **Pesan berikutnya di sesi yang sama adalah jawabannya**: angka (`2`) atau kalimat bebas. Thread yang sama
   dilanjutkan; planner meneruskan percakapannya dari titik ia bertanya (tidak membaca ulang kode), lalu rencana,
   eksekusi, dan wrapper berjalan seperti biasa. Wrapper dan reviewer melihat permintaan awal beserta klarifikasinya.
3. Jawaban yang ternyata permintaan baru juga diterima apa adanya; planner menyesuaikan rencananya.

Batasan:

- Maks 2 pertanyaan per thread, satu per waktu, dan tidak di langkah terakhir planner.
- Pertanyaan disimpan di memori proses: bila AIT di-restart, atau tidak dijawab dalam **2 jam**, thread ditutup
  sebagai *Dibatalkan* dan pesan berikutnya dijalankan sebagai thread baru (riwayat chat yang berisi pertanyaan tetap
  terbaca planner). Tombol *Batalkan* di detail thread juga menutup thread yang menunggu jawaban.
- Sesi dikenali dari id sesi klien (Claude Code, Codex, Telegram, …); klien yang tidak mengirim id sesi, dan prompt
  dari Dashboard, tidak pernah ditanya. Prompt di project lain dalam sesi yang sama menutup pertanyaan yang tertunda.
- Matikan lewat Setting → *Planner boleh bertanya* bila Anda ingin planner selalu memutuskan sendiri.

### Merujuk perubahan sebelumnya

Permintaan seperti *"test perubahan ini"*, *"review yang tadi"*, atau *"batalkan yang itu"* merujuk ke perubahan
sebelumnya tanpa menyebut yang mana. Planner mengartikannya sebagai file di bagian *perubahan di sesi ini* — **bukan
seluruh `git diff`**, karena sesi Claude Code lain (tab lain) bisa sedang mengubah folder yang sama. Sesi ditentukan dari
anchor `**AIT** · thread …` di riwayat chat, jadi dua tab = dua sesi yang tidak saling tercampur.

- Satu modul, atau permintaan menyebut modul/file/fitur → langsung dipakai.
- Lebih dari satu modul → planner [bertanya](#pertanyaan-dari-planner) dulu: satu pilihan per modul (maks 3 terbaru,
  dengan thread dan jumlah file-nya) plus *semua*. Anda juga boleh menjawab modul lain atau beberapa nomor sekaligus.
- Tanpa kemungkinan bertanya (Dashboard, Telegram, atau *Planner boleh bertanya* mati) → modul terbaru dipakai dan
  asumsi itu disebut di ringkasan.

### Bentuk rencana

```json
{
  "summary": "Menambah field Deskripsi pada form User …",
  "answer": null,
  "contract": [
    { "name": "UI input id", "value": "txtDescription" },
    { "name": "DTO property", "value": "Description" },
    { "name": "DB column", "value": "Description NVARCHAR(500) NULL", "note": "table Users" }
  ],
  "tasks": [
    {
      "id": "T1",
      "title": "Add description textarea to the user form",
      "file": "src/Web/Views/User/Index.cshtml",
      "action": "edit",
      "instruction": "After the txtName form group add …",
      "focus": [{ "startLine": 40, "endLine": 60 }],
      "context": [],
      "dependsOn": []
    },
    {
      "id": "T2",
      "title": "Send description in saveUser",
      "file": "src/Web/wwwroot/js/user.js",
      "action": "edit",
      "instruction": "In saveUser add description: $('#txtDescription').val() …",
      "context": [{ "file": "src/Web/Views/User/Index.cshtml", "startLine": 1, "endLine": 12, "note": "form markup" }]
    }
  ]
}
```

- **Kontrak** adalah kunci akurasi: semua nama lintas file ditetapkan sekali, sehingga tugas bisa berjalan paralel
  tanpa saling melihat.
- `focus` menandai area file target yang besar; `context` melampirkan potongan file lain (read-only) untuk executor.
- Permintaan tanpa perubahan file → `tasks` kosong dan jawaban di `answer`; tidak ada executor yang berjalan. Bila
  developer hanya meminta pengujian, `tasks` kosong disertai `tests.run` dan/atau `test` (lihat
  [Thread yang hanya menguji](#thread-yang-hanya-menguji)).
- Dengan katalog unit test aktif, field `tests` berisi keputusan per test terdaftar dan test baru (lihat
  [Keputusan planner](#keputusan-planner)).

### Validasi rencana

Rencana ditolak dan dikembalikan ke planner (memakan satu langkah) bila:

- lebih dari 40 tugas, atau `summary` kosong padahal ada tugas;
- id tugas ganda, `instruction` kosong (kecuali untuk `delete`/`move`/`extract`), `action` bukan
  `edit`/`create`/`delete`/`move`/`extract`;
- path (termasuk tujuan `to`) di luar root (perubahan hanya boleh di dalam root; membaca di luar root diperbolehkan),
  menunjuk folder, atau berada di folder yang disembunyikan;
- `edit` untuk file yang tidak ada, `create` untuk file yang sudah ada, atau dua tugas membuat/memindahkan file ke path
  yang sama;
- `delete`/`move` untuk file yang tidak ada; `move` tanpa `to`, ke file yang sudah ada, atau hanya mengubah huruf
  besar/kecil;
- `extract` tanpa `members`, tanpa `to`, bukan file `.cs`, atau ke file yang juga dibuat tugas `create` (beberapa
  `extract` boleh mengisi file baru yang sama);
- file yang dihapus atau dipindahkan juga disentuh tugas lain. File hasil `move` atau file baru hasil `extract` boleh
  di-`edit` asal tugas itu bergantung (`dependsOn`) pada tugas `move`/`extract`-nya;
- `dependsOn` menunjuk tugas yang tidak ada, dirinya sendiri, atau membentuk siklus;
- bagian `tests` tidak valid (lihat [Keputusan planner](#keputusan-planner)).

Path dinormalisasi (`\` → `/`, relatif terhadap root). Penolakan tercatat di log aktivitas thread.

## 4. Eksekusi paralel

- Tugas tanpa ketergantungan langsung berjalan; tugas lain mulai begitu semua `dependsOn`-nya **berhasil**. Jika
  salah satu gagal, tugas penunggu **dilewati**.
- Dua batas berlaku bersamaan: *Maks paralel* (per thread) dan *Max paralel* tiap node (lintas semua thread).
- Executor dipilih: slot kosong terbanyak → prioritas terkecil → bergiliran. Bila executor itu tetap gagal (429/408/5xx)
  setelah retry 9router, tugas dilanjutkan oleh [node cadangannya](02-konfigurasi.md#node-cadangan) (bila dipilih).
- Sebelum menulis file untuk pertama kali, isi asli file dicatat agar thread bisa dibatalkan
  ([rollback](04-penggunaan.md#membatalkan-perubahan-file-rollback)).
  Setelah setiap penulisan, hash isi yang ditulis juga dicatat.
- Waktu eksekusi = rantai tugas terpanjang. Dashboard membandingkannya dengan total waktu kerja executor (speedup).

### Hapus, pindah file, dan pindah member C#

Selain `edit` dan `create`, sebuah tugas bisa berupa:

- `delete` — menghapus satu file;
- `move` — memindahkan/me-rename satu file ke path `to`. Isinya tidak berubah (byte demi byte);
- `extract` — memindahkan member C# (`members`: method, property, field, event, nested type) dari file `.cs` ke file
  `.cs` lain (`to`). Lihat [Pindah member C#](#pindah-member-c-extract).

Ketiga aksi ini dijalankan **langsung oleh AIT tanpa model** (tanpa token, tanpa node executor; di detail thread
kolom node bertuliskan *tanpa model*), di bawah lock file yang sama dengan executor. Folder yang menjadi kosong setelah
file diambil darinya ikut dihapus (tidak pernah root project). Bila isi file yang dipindahkan juga perlu berubah
(namespace, import, nama class), planner menambah tugas `edit` di path baru yang bergantung pada tugas `move`; referensi
ke file itu di file lain diubah lewat tugas `edit` masing-masing. Seperti penulisan executor, isi asli file dicatat
dulu sehingga hapus dan pindah ikut dikembalikan saat
[rollback](04-penggunaan.md#membatalkan-perubahan-file-rollback).

#### Pindah member C# (`extract`)

Untuk memecah class besar menjadi beberapa file `partial` (atau memindahkan method ke file lain), planner memakai
`extract`, bukan `create` + `edit`. Executor tidak bisa diandalkan untuk menyalin atau menghapus ribuan baris: ia hanya
melihat potongan file besar dan outputnya dibatasi *max tokens*, sehingga pemindahan berhenti di tengah dan build gagal
karena member ganda.

AIT mengerjakan `extract` dengan parser C# (Roslyn), dalam hitungan milidetik:

- Member disalin **apa adanya** (isi, komentar dokumentasi, indentasi). Semua overload dengan nama yang sama ikut
  pindah; field dengan beberapa variabel dalam satu deklarasi harus dipindah bersama.
- Class sumber (dan class pembungkusnya bila nested) diberi `partial` bila belum.
- File tujuan baru mendapat `using`, namespace (gaya blok atau *file-scoped* mengikuti sumber), dan header class yang
  sama tanpa atribut, base class/interface, dan constraint (cukup di bagian utama).
- File tujuan yang sudah ada harus berisi bagian `partial` dari class yang sama; member ditambahkan di akhir class.
  Member yang sudah ada di tujuan dengan signature sama hanya dihapus dari sumber (tidak digandakan).
- `#region`/`#if` di sekitar member tetap di file sumber agar tetap seimbang. Constructor dan operator tidak bisa
  dipindah.
- Beberapa `extract` dari file sumber yang sama berjalan paralel; lock file membuat masing-masing bekerja pada isi
  terbaru.

### Yang dilihat executor

1. Prompt peran executor (+ instruksi tambahan node, + isi skill node; lihat [Skill pada node](#skill-pada-node)).
2. Bagian rencana yang sama untuk semua tugas: kontrak dan ringkasan rencana (sebagai konteks saja). Bagian ini
   sengaja paling depan agar executor-executor satu rencana berbagi awal prompt yang sama di prompt cache provider.
3. Judul, aksi, dan path file target; instruksi.
4. Potongan referensi dari `context` (maks 250 baris per potongan, total maks 40.000 karakter).
5. Isi file target:
   - file ≤ `ExecutorFullFileMaxLines` (400) baris → utuh;
   - file lebih besar → potongan di sekitar `focus` ±40 baris dengan penanda `... (lines X-Y not shown) ...`;
     tanpa `focus` → 400 baris pertama;
   - file baru → keterangan bahwa file belum ada.

### Format jawaban executor

```
<<<<<<< SEARCH
        <input type="text" id="txtName" class="form-control" />
    </div>
=======
        <input type="text" id="txtName" class="form-control" />
    </div>
    <div class="mb-3">
        <label for="txtDescription">Deskripsi</label>
        <textarea id="txtDescription" class="form-control" rows="3"></textarea>
    </div>
>>>>>>> REPLACE
SUMMARY: Added the txtDescription textarea after the name field.
```

Blok dengan SEARCH kosong berarti membuat file baru (isi lengkap di bagian REPLACE).

### Menerapkan edit

1. File target **dibaca ulang di dalam lock per file** — tugas lain di file yang sama mungkin sudah mengubahnya.
2. Setiap blok dicocokkan bertahap: teks persis → baris per baris mengabaikan spasi di akhir baris → mengabaikan
   indentasi (indentasi pengganti disesuaikan). Kecocokan lebih dari satu tempat **ditolak**, tidak ditebak.
3. **All-or-nothing**: file hanya ditulis bila semua blok berhasil.
4. BOM dan gaya line ending (CRLF/LF) file asli dipertahankan; file baru memakai line ending sistem.
5. Bila gagal (tidak ada blok valid, SEARCH tidak ketemu, output terpotong), executor menerima pesan error beserta
   isi file terkini lalu mencoba ulang hingga *Retry executor* kali.

Diff yang ditampilkan di detail thread dibangun dari blok-blok tersebut.

## 5. Verifikasi: build dan reviewer

Setelah eksekusi, bila minimal satu tugas berhasil, AIT memeriksa hasilnya (status thread *Verifikasi*). Tahap ini
berjalan bila ada salah satu dari:

- **perintah build**: *Build & verifikasi otomatis* menyala di Setting dan project punya perintah build;
- **node Reviewer** aktif (yang prioritasnya terkecil dipakai);
- **unit test**: project punya [katalog unit test](#unit-test) aktif dan suite yang dikenali.

Perintah build dipilih dari manifest project, **bukan oleh model** (model hanya boleh memilih manifest lain di dalam
root lewat `run_build`):

| Stack | Perintah | Target default |
|---|---|---|
| .NET | `dotnet build <file> --no-incremental -v:q -p:GenerateFullPaths=true -p:CopyRetryCount=1 -p:NuGetAudit=false` | `.sln`/`.slnx` paling dangkal, kalau tidak ada `.csproj`/`.fsproj`/`.vbproj` paling dangkal |
| .NET Framework (csproj format lama/non-SDK, mis. ASP.NET MVC 5) | `MSBuild.exe <file> -restore -t:Rebuild -m -nr:false -p:RestorePackagesConfig=true …` dari Visual Studio / Build Tools (dicari lewat `vswhere`, lalu folder standar `Program Files\Microsoft Visual Studio`) | sama dengan .NET; solution dianggap format lama bila salah satu project-nya format lama atau berisi web site project |
| Node.js | `tsc --noEmit` (TypeScript lokal di `node_modules`), kalau tidak ada `npm run build` | `package.json` paling dangkal yang punya salah satunya |
| Go | `go build ./...` | `go.mod` |
| Rust | `cargo check --message-format short` | `Cargo.toml` |
| Java (Maven) | `mvnw`/`mvn -q -B compile` | `pom.xml` |

Stack lain (Python, PHP, Gradle, …) tidak punya perintah build; verifikasinya hanya lewat Reviewer bila ada.

Build .NET selalu **rebuild penuh** (clean + build), supaya output lama tidak menutupi error. Pengecualiannya: bila
ada `.dll`/`.exe` di folder `bin` project yang sedang dipakai program yang berjalan (app-nya sendiri, IIS tanpa shadow
copy, compiler server di `bin\roslyn`), build dijalankan **incremental tanpa clean** (`dotnet build` / `msbuild
-t:Build`). Kalau tetap clean, semua file di sekitar file yang terkunci (mis. `*.runtimeconfig.json`, `*.deps.json`)
ikut terhapus dan tidak disalin ulang karena copy-nya gagal, sehingga app tidak bisa dijalankan lagi tanpa build
ulang. Copy ke `bin` yang gagal karena file terkunci (`MSB3021`/`MSB3027`) tetap dihitung lolos: kodenya sudah
terkompilasi. Project format lama
tidak bisa di-build `dotnet build` karena meng-import target milik Visual Studio (mis.
`Microsoft.WebApplication.targets`). Kalau MSBuild Visual Studio tidak ada, project itu tidak di-build sama sekali
(tidak diganti build stack lain seperti `npm run build` di sebelahnya). Build yang gagal **hanya** karena toolchain
kurang (`MSB4019` target import tidak ada, `MSB4236` SDK tidak ada, `NETSDK1045` target framework tidak didukung)
dilaporkan sebagai "build tidak bisa dijalankan" (🟡): bukan masalah kode, jadi tidak dibuatkan tugas perbaikan.

Satu putaran verifikasi:

1. **Build** (bila ada perintahnya). Satu build per project pada satu waktu (dua build berebut `obj/`). Build
   dihentikan beserta proses anaknya bila melewati *Timeout build* atau thread dibatalkan. Error dibaca dari output
   (format MSBuild/Roslyn, tsc, Maven, dan `file:baris:kolom` untuk Go/Rust); bila tidak ada error yang dikenali,
   120 baris terakhir output yang dipakai.
2. **Reviewer** (bila ada node aktif). Ia menerima permintaan, rencana, kontrak, instruksi dan diff setiap tugas (maks
   150 baris per tugas), dan hasil build, lalu menjawab JSON: disetujui, atau daftar masalah per file — kebutuhan yang
   terlewat, nama yang tidak cocok antar file/kontrak, salah ketik, `using`/import yang kurang, dan penyebab error
   build. Gaya, refactor, dan hal di luar permintaan tidak dilaporkan. Reviewer yang gagal dihubungi atau menjawab
   bukan JSON hanya membuat review dilewati.
3. **Unit test** (bila build lolos atau tidak ada perintah build): test relevan dijalankan dan dibandingkan dengan
   hasil sebelum perubahan; kegagalan didiagnosis node Tester (bila ada) — lihat [Setelah build](#setelah-build).
4. **Tugas perbaikan**. Masalah dikelompokkan per file menjadi tugas `V<putaran>-<n>` (mis. `V1-1`) yang dikerjakan
   executor seperti tugas biasa, dengan instruksi berisi error build (baris, kolom, kode, pesan), temuan reviewer, dan
   instruksi tugas awal file itu sebagai konteks. *Focus* diarahkan ke awal file (tempat `using`/import) dan ke baris
   setiap error atau baris yang dikutip reviewer. Nama yang dikutip pesan error (mis. `OracleEntities` pada
   `CS7036 … 'OracleEntities.OracleEntities(string)'`) dicari di bagian lain file yang sama: maksimal 6 baris yang
   memakainya ikut masuk *focus* dan instruksi sebagai pola yang harus diikuti. Tanpa ini, executor file besar hanya
   melihat ±40 baris di sekitar error dan tidak tahu cara yang dipakai method lain. Lalu putaran berikutnya dimulai.

   Nama yang tidak ditemukan (`CS0103`, `CS0117`, `CS1061`) diperbaiki di **file yang seharusnya mendeklarasikannya**,
   bukan di file pemanggil, bila tepat satu tugas lain (file C#) menyebut nama itu di instruksinya dan file tersebut
   belum memuat nama itu sama sekali. Instruksinya menyebut lokasi pemanggil, dan baris pemanggil (±5 baris) ikut
   sebagai excerpt referensi supaya signature-nya cocok. Tanpa itu executor file pemanggil hanya bisa menjawab
   "pemanggilannya sudah sesuai kontrak" dan putaran perbaikan terbuang.

   Pengecualian: error member ganda C# (`CS0111`, `CS0102`) antara file yang **diisi** thread ini (tugas `create`
   atau tujuan `extract`) dan file yang sudah ada sebelumnya berarti pemindahan member belum tuntas. Untuk itu dibuat
   tugas `extract` (tanpa model) dari file lama ke file baru, sehingga salinan di file lama yang dihapus — bukan
   salinan di file baru. Tugas perbaikan lain pada kedua file menunggu tugas ini, dan temuan reviewer untuk kedua
   file itu ditunda ke putaran berikutnya (reviewer memeriksa ulang setelah pemindahan).

Batas dan aturan:

- Maksimal *Putaran perbaikan* putaran (default 2). Nilai `0` = hanya verifikasi dan laporkan. Regresi unit test punya
  batas sendiri: 3 putaran perbaikan (tetap mati bila nilai ini `0`).
- Error build hanya diperbaiki otomatis di **file yang diubah thread ini** (atau, untuk nama yang tidak ditemukan, di
  file yang menurut rencana harus mendeklarasikannya). Error di file lain (mungkin sudah ada
  sebelum thread), di folder hasil generate, di luar root, atau tanpa file (mis. paket NuGet tidak ditemukan)
  dilaporkan apa adanya. Reviewer tetap boleh menunjuk file lain bila jelas rusak akibat perubahan.
- Error `MSB3021`/`MSB3026`/`MSB3027` (file output terkunci oleh aplikasi yang sedang berjalan) dianggap **lolos**:
  kodenya berhasil dikompilasi.
- Build yang timeout atau tidak bisa dijalankan (toolchain tidak terpasang) tidak diulang di putaran berikutnya.
- Verifikasi berhenti bila tidak ada tugas perbaikan yang berhasil, atau masalah yang tersisa tidak bisa diperbaiki
  otomatis.

Hasilnya: **Lolos**, **Masih ada masalah**, atau **Tidak dapat dipastikan** (build tidak bisa jalan dan tidak ada
review). Thread tetap *Selesai*; masalah yang tersisa dicatat di kolom error thread dan disebutkan wrapper. Detail
setiap putaran tampil di kartu *Verifikasi* pada detail thread.

## 6. Testing (Auto Testing)

Bila node Planner yang dipakai menyalakan **Auto Testing** (modal node Planner) dan ada node **Tester** aktif, AIT
mencoba perubahan di aplikasi yang **sedang berjalan** setelah verifikasi (status thread *Testing*). Yang dipakai node
Tester aktif dengan prioritas terkecil.

### Rencana testing dari planner

Saat Auto Testing menyala, prompt planner mendapat bagian *Auto Testing* dan `submit_plan` mewajibkan field `test`
untuk rencana yang mengubah file. Di situ planner menilai **seperti apa perubahan itu terlihat dari luar**:

```json
"test": {
  "level": "ui",
  "baseUrl": "http://localhost:5000",
  "notes": "Menu Master → User.",
  "scenarios": [
    {
      "title": "Simpan user dengan deskripsi",
      "target": "/User",
      "steps": ["Klik Tambah", "Isi #txtDescription dengan 'AIT test'", "Klik Simpan"],
      "expect": "Baris baru di tabel menampilkan deskripsi 'AIT test'"
    }
  ]
}
```

- `level`: `ui` bila perubahan tampil di halaman (tester mengendalikan browser dan boleh memanggil endpoint juga),
  `api` bila hanya bisa dicapai lewat endpoint HTTP, `none` bila tidak bisa dicoba dari luar (refactor, script build,
  library, script database saja) — alasannya di `notes`. Untuk background job dan sistem eksternal lihat di bawah.

#### Background job dan sistem eksternal

Kode yang hanya jalan di background (job Hangfire/Quartz, hosted service, consumer antrian, sync terjadwal) atau yang
efeknya hanya terlihat di sistem eksternal **tidak dites dengan memicu job-nya**. Bila project punya katalog unit test
(atau bisa dibuatkan test project, lihat [Keputusan planner](#keputusan-planner)), planner menambah **unit test
dry-run**:

- test memanggil method yang dijalankan job itu **langsung**, dengan API eksternal, database, antrian, dan jam
  dipalsukan (mock/fake): tidak ada yang keluar dari proses, tidak ada data yang benar-benar tertulis;
- input-nya persis kasus yang diubah (record dari API eksternal, field kosong, respons error), lalu hasilnya di-assert;
- bila method itu tidak bisa dipanggil tanpa framework job-nya (static, membuat `HttpClient`/`DbContext` sendiri),
  planner menambah refactor kecil agar fake bisa dimasukkan;
- `level` = `none` (atau tetap `ui`/`api` untuk bagian yang memang tampil di aplikasi) dan `notes` menyebut test-nya.
  Hasilnya ada di bagian *🧪 Unit test*, bukan langkah manual untuk developer.

Test itu masuk katalog, jadi ikut menjaga regresi di thread berikutnya. Tanpa katalog unit test (mode *Mati* atau
*Buat unit test baru* = *Tidak pernah*) testing tetap dilewati dengan alasan dari planner.
- `baseUrl`: dicari planner sendiri dari `Properties/launchSettings.json`, `appsettings*.json` (Kestrel/`urls`), script
  `package.json` dan konfigurasi dev server (vite, next, angular), `.env` (`PORT`), atau port `docker-compose`. Harus
  alamat mesin ini: `localhost`, `*.localhost`, `127.0.0.1`, atau `[::1]`.
- `scenarios`: 1–6 skenario (jalur utama dulu, lalu kasus tepi yang ditangani perubahan), masing-masing dengan `steps`
  yang memakai nama dari kontrak dan `expect` yang bisa dilihat (teks, nilai, status code, field respons).
- Validator mengembalikan rencana ke planner bila `test` tidak ada, `level` tidak dikenal, `baseUrl` kosong atau bukan
  localhost, tidak ada skenario, atau skenario tanpa `expect`. Tanpa node Tester aktif planner tidak diminta bagian ini,
  dan bagian `test` yang tetap dikirim diabaikan. Rencana tanpa tugas hanya dites bila developer memang hanya meminta
  pengujian (lihat [Thread yang hanya menguji](#thread-yang-hanya-menguji)).

### Jalannya testing

1. **Dilewati** bila `level` = `none` (progres: *Testing · dilewati* + alasan planner), atau bila build terakhir masih
   gagal (aplikasi belum bisa memuat perubahan). Tidak berjalan sama sekali bila tidak ada tugas yang berhasil (kecuali
   thread yang hanya menguji).
2. **Cek aplikasi**: sebelum memanggil model, AIT mengirim `GET` ke `baseUrl` (5 detik). Respons apa pun (juga 404 atau
   redirect ke login) berarti aplikasinya berjalan. Tanpa respons, **AIT menjalankan aplikasinya** (lihat
   [Menjalankan aplikasi](#menjalankan-aplikasi)) dan menunggu sampai `baseUrl` menjawab. Bila tetap tidak menjawab,
   semua skenario *tidak bisa dicek* tanpa token terpakai, dengan alasannya (perintah tidak dikenali, exit code dan
   output terakhir, atau batas waktu).
3. **Tester** bekerja dalam loop tool-nya sendiri (maks 40 langkah). Ia melihat permintaan, ringkasan rencana, kontrak,
   diff tiap tugas yang berhasil (maks 80 baris per tugas), dan rencana testing — bukan kode lain. Tool-nya:

| Tool | Level | Keterangan |
|---|---|---|
| `http_request(method, url, headers?, body?)` | ui, api | `url` = path relatif `baseUrl` atau URL localhost absolut. Body objek/array dikirim sebagai JSON. Cookie dari respons sebelumnya ikut terkirim (login sekali, lalu panggil endpoint yang dilindungi). Aplikasi dengan Windows Authentication (IIS/Negotiate) otomatis memakai akun Windows yang menjalankan AIT. Redirect tidak diikuti (302 menampilkan `Location`). Timeout 30 detik; body dibaca maks 512 KB, JSON dirapikan. |
| `browser_open(url)` | ui | Membuka halaman di **browser yang tampil di layar** (non-headless). Hanya localhost. |
| `browser_snapshot()` | ui | Halaman sebagai *accessibility snapshot* Playwright: elemen beserta `ref` (mis. `[ref=e12]`), teks, dan nilai field. |
| `browser_click`, `browser_fill`, `browser_select` | ui | Elemen dipilih lewat `ref` dari snapshot terakhir, atau selector CSS/Playwright (`#txtDescription`, `text=Simpan`); `element` = nama elemen yang tampil di progres. `browser_fill` bisa menekan Enter sesudahnya. |
| `browser_press(key)`, `browser_wait(text?, gone?, seconds?)` | ui | Menekan tombol (`Enter`, `Escape`, `Control+A`, …); menunggu teks muncul/hilang atau beberapa detik (maks 15). |
| `report_test(summary, scenarios)` | — | Mengakhiri testing dengan `passed` / `failed` / `blocked` per skenario beserta penjelasannya. |
| `ask_login(reason, loginUrl?)` | — | Hanya untuk thread dari chat (Claude Code/Codex/…, ada id sesi). Menghentikan testing untuk meminta login ke developer — lihat [Login](#login). |

   Setiap aksi browser dijawab dengan URL, judul, kejadian sejak aksi sebelumnya — error console, error JavaScript yang
   tidak tertangkap, request halaman (`xhr`/`fetch`/dokumen) yang 4xx/5xx atau gagal, dialog (`alert`/`confirm`
   otomatis diterima) — lalu snapshot (maks 12.000 karakter). Aksi dijalankan pelan (250 ms per langkah) agar bisa
   diikuti mata, dengan timeout 10 detik; elemen yang tidak ditemukan dilaporkan ke tester bersama snapshot terbaru. Tab
   baru atau popup otomatis diikuti. Beberapa tool dalam satu giliran dijalankan berurutan.
4. **Progres** di CLI: fase `🧪 Testing · UI · http://localhost:5000 · model`, satu baris per tool (mis.
   `browser_fill  Deskripsi ← "AIT test"`), dan untuk `http_request` status, durasi, content type, serta 8 baris pertama
   body respons di bawahnya.

### Menjalankan aplikasi

Bila `baseUrl` tidak menjawab, AIT menjalankan aplikasinya sendiri untuk testing itu (`Testing/AppLaunchResolver` +
`AppLauncher`). Perintahnya tetap per stack dan tidak pernah dari model:

| Urutan | Kondisi | Perintah |
|---|---|---|
| 1 | `.aithread/project.json` punya `"run"`, mis. `{ "key": "eep", "run": "npm run dev:all" }` | perintah itu, lewat `cmd /c` (Windows) atau `sh -c`, di root project |
| 2 | Project ASP.NET Core (`Microsoft.NET.Sdk.Web`): yang punya launch profile `Project` di `Properties/launchSettings.json` dengan port `baseUrl`, atau satu-satunya | `dotnet run --project <csproj> --launch-profile <profile> --urls <origin baseUrl>` di folder project. Profile yang hanya berisi alamat itu didahulukan (`http` untuk `http://localhost:5211`, bukan `https` yang juga memuatnya); environment variable profile itu menang atas bawaan AIT. Tanpa profile yang cocok: `--no-launch-profile` |
| 3 | Laravel (`artisan` di root) | `php artisan serve --host=<host> --port=<port>` |
| 4 | `package.json` (yang paling dangkal) dengan script `dev`, `start`, atau `serve` | `npm run <script>`; `node_modules` harus sudah ada |

- Environment tambahan: `PORT`, `ASPNETCORE_URLS` (origin `baseUrl`), `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT` =
  `Development`, `BROWSER=none`. Dev server yang tidak membaca `PORT` (mis. vite) tetap memakai port dari konfigurasinya;
  planner mengambil `baseUrl` dari konfigurasi yang sama.
- AIT menunggu sampai `baseUrl` menjawab (dicek tiap detik, maks 3 menit, karena `dotnet run` membuild dulu). Proses
  yang berhenti sebelum menjawab dilaporkan dengan exit code (kode crash Windows juga dalam hex, mis. `0xC0000005`);
  yang tidak menjawab dalam 3 menit dihentikan. Satu kalimat penyebabnya tampil di laporan, lalu 15 baris terakhir
  output aplikasi di blok **Output aplikasi** (jawaban akhir dan detail thread). Warning build (`warning NU1903: …`,
  `warning CS…`, `npm warn …`) tidak ikut, agar error start-nya (mis. stack trace) tidak tertutup. Bila aplikasi lain sempat menjawab di URL itu (dijalankan developer atau thread lain), aplikasi itu yang
  dipakai.
- Progres: `🚀 Aplikasi belum jalan, AIT menjalankannya: …` lalu `🟢 Aplikasi menjawab di …`. Laporan testing (jawaban
  akhir dan detail thread) menyebut *dijalankan AIT* beserta perintahnya.
- Setelah testing (juga bila dibatalkan) proses itu dihentikan beserta semua proses turunannya. Aplikasi yang sudah
  berjalan sebelum testing tidak pernah dihentikan.
- Tidak didukung otomatis: project .NET Framework lama (IIS Express), beberapa project web .NET tanpa port yang cocok,
  dan stack lain (Python, Go, Java, Docker, …). Pakai `"run"` di `.aithread/project.json` atau jalankan sendiri. Untuk
  perubahan yang langsung termuat, `dotnet watch`/dev server yang Anda jalankan sendiri tetap paling cepat.

### Browser tester

- Dipakai **Microsoft Edge** (bawaan Windows), lalu **Google Chrome**, lalu **Chromium** milik Playwright (diunduh sekali
  ±150 MB ke `%LOCALAPPDATA%\ms-playwright` bila keduanya tidak ada). Driver Playwright ikut terpasang bersama AIT
  (folder `.playwright` di samping `AIThread.exe`), tetapi tanpa `node.exe`-nya agar paket tidak membengkak ±90 MB.
  Node.js untuk driver dicari saat browser tester pertama kali dibuka: env `PLAYWRIGHT_NODEJS_PATH` → `node.exe`
  bawaan driver (build dev) → `node` di `PATH` (versi 20+) → unduhan sebelumnya → unduh Node.js resmi sekali (±36 MB,
  dicek SHA-256 dari nodejs.org) ke `%LOCALAPPDATA%\AIThread\node\v<versi>\node.exe`.
- Profilnya terpisah dari browser harian Anda, di `%LOCALAPPDATA%\AIThread\tester-browser\<browser>`. Cookie persisten
  ikut tersimpan antar testing, tetapi jangan mengandalkannya untuk login: session cookie hilang saat jendela ditutup.
  Pakai [Login](#login).
- Hanya satu browser tester terbuka pada satu waktu; testing UI thread lain menunggu giliran. Jendela ditutup 2 detik
  setelah testing selesai. Testing API tidak membuka browser.
- Browser hanya terlihat bila AIT berjalan di sesi Windows Anda (tray/menu `AIThread.exe`), bukan sebagai service.

### Login

Bila aplikasi meminta login dan tester belum punya akun, **tester bertanya ke Anda di chat**:

1. Tester memanggil `ask_login` (mis. *`/User` mengalihkan ke halaman login*). Testing berhenti di situ; skenario yang belum
   dicek menjadi *tidak bisa dicek — Butuh login*. Thread tetap **selesai** seperti biasa (wrapper, file berubah, footer),
   dan bagian *Testing* di akhir jawaban ditutup dengan:

   ```
   🔐 Tester butuh login — /User mengalihkan ke halaman login (`/Account/Login`)

   Balas pesan ini dengan `login <username> <password>` (akun uji) untuk menjalankan testing lagi dengan akun itu. …
   ```

2. Anda membalas di chat yang sama, mis. `login admin rahasia123`. Format lain yang diterima: balasan pendek berlabel
   seperti `username: admin` + `password: rahasia123` (urutan bebas). Pesan lain dianggap prompt biasa.
3. AIT menjalankan **testing thread tadi lagi** dengan akun itu — tanpa planner dan tanpa mengubah kode — dan
   menampilkan progres serta bagian *Testing* barunya sebagai balasan. Kalau login ditolak, tester boleh bertanya lagi.

Penanganan password:

- Tester (model) **tidak pernah melihat password**. Ia hanya diberi username dan placeholder `{{password}}`; AIT
  menggantinya dengan nilai asli saat `browser_fill` atau `http_request` dijalankan, lalu menyamarkannya lagi di semua
  yang kembali ke model, di progres, log, dan laporan testing.
- Password **tidak ditulis ke disk** (database, log, laporan). Ia disimpan di memori proses AIT untuk sesi chat itu dan
  project itu selama 8 jam, sehingga thread berikutnya di sesi yang sama langsung login tanpa ditanya ulang. Hilang bila
  AIT di-restart.
- Riwayat chat berisi balasan `login …` ikut terkirim bersama setiap prompt berikutnya; AIT menyamarkan password itu
  sebelum riwayat sampai ke planner atau model lain.
- Balasan Anda tetap tercatat di riwayat chat klien (mis. transcript Claude Code). Pakai **akun uji**, bukan akun pribadi.
- Pertanyaan menunggu 2 jam. Thread dari Dashboard atau Telegram tidak bisa ditanya; isi username & password di modal
  **🧪 Ulangi testing** detail thread (juga tidak disimpan).
- **Captcha**: di lingkungan pengembangan captcha biasanya tidak diperiksa asal terisi, jadi tester mengisi field captcha
  dengan nilai apa saja (mis. `1234`) dan lanjut; widget centang (reCAPTCHA, hCaptcha, Turnstile) diklik sekali. Bila
  aplikasi tetap menolak, skenario di baliknya *tidak bisa dicek* dengan keterangan captcha tidak di-bypass di lingkungan
  ini — tester tidak meminta apa pun ke chat untuk itu.
- Alternatif lama tetap berlaku: kredensial yang ditulis di *Instruksi tambahan* node Tester dipakai tester (tetapi
  tersimpan apa adanya, terkirim ke model, dan ikut export profil setup — hindari untuk password).

### Akun uji (berganti user)

Alur bisnis jarang selesai di satu user: pengaju submit, atasan level 1 meng-approve, HR level 2, dan seterusnya. Untuk
itu tiap project punya **akun uji** (Project > Detail → *Akun uji*), dibagi ke semua developer project lewat project
key dan ikut store long memory seperti katalog unit test.

- Tiap akun: **username**, **peran** (*Pengaju*, *Atasan*, *HR*, *Administrator*, …), **atribut bebas** (NIK, Unit,
  Level approval, Atasan = username atasannya, …), dan catatan. Username unik per project, tidak peka huruf besar/kecil.
- **Tidak ada password per akun.** Project punya satu **Password dev**: kosong = bawaan `Test@1234` (cukup panjang, ada
  huruf besar, angka, dan simbol, supaya lolos validasi browser yang umum). Nilai ini bukan rahasia dan terlihat oleh
  semua developer project; untuk password asli tetap pakai balasan `login <user> <password>` (memori saja).
- Brief planner dan tester mendapat bagian `# Test accounts` (username · peran · atribut, tanpa password). Planner
  memberi skenario field **`actor`** (username atau peran dari daftar itu); validator menolak aktor yang tidak dikenal.
  Alur yang melewati beberapa user ditulis sebagai beberapa skenario berurutan, masing-masing dengan aktornya.
- Tool tester **`switch_user(account)`** menjadikan satu akun aktif:
  - **browser**: jendela yang terbuka ditutup, dan tool browser berikutnya membuka **profil browser milik akun itu**
    (`%LOCALAPPDATA%\AIThread\tester-browser\accounts\<project>-<username>\<browser>`), terpisah per project karena cookie
    `localhost` tidak dipisah per port. Login akun itu bertahan untuk run berikutnya;
  - **HTTP**: `http_request` memakai cookie jar akun itu (sesi akun lain tetap utuh dalam run yang sama);
  - **placeholder**: `{{username}}` = username akun aktif, `{{password}}` = Password dev project. Model tidak pernah
    melihat nilainya, dan nilainya disamarkan lagi di semua output.
- Progres `👤 switch_user`, laporan menyebut `akun pengaju01, atasan01` dan aktor per skenario
  (`✅ **Approve** (atasan01) — …`). Bila password dev bawaan yang dipakai, laporan memberi catatan dan menautkan ke
  kartu *Akun uji*.
- **Password dev ditolak**: tester memanggil `ask_login` dengan `kind: "dev_password"`. Jawaban akhir meminta balasan
  `password dev <nilai>`. Nilai itu disimpan sebagai Password dev project (sumber *chat*), lalu testing thread tadi
  diulang dengan akun uji. Balasan `login <user> <password>` tetap bisa dipakai sebagai gantinya (hanya untuk sesi itu).

#### Login as / impersonate

Banyak aplikasi internal punya fitur admin *login sebagai user lain*. Ada atau tidaknya, cara memakainya, dan peran yang
boleh memakainya berbeda per project, jadi AIT **mendeteksinya per project**:

1. **Scan kode tanpa model** (`ImpersonationScanner`): pola seperti `Impersonate`, `LoginAs`, `SwitchUser`, `RunAs`,
   `SignInAs`, *login sebagai*, beserta beberapa baris di sekitarnya (tempat `[Authorize(Roles = …)]`/`IsInRole`
   biasanya ada). Folder build/pihak ketiga dan file yang dilindungi kebijakan rahasia dilewati.
2. **Model murah** (node Tools → Wrapper → Planner, seperti `MemoryAi`) membaca temuan itu dan menjawab: tersedia atau
   tidak, *cara* (menu/URL/endpoint, dengan `{{target}}` untuk user tujuan), *cara kembali*, dan *peran* yang boleh.
   Tanpa temuan tidak ada model yang dipanggil (hasil: *tidak ada*).
3. Berjalan **sekali** saat test pertama di project yang punya lebih dari satu akun uji (progres
   `🔎 Mencari fitur login as…`), atau lewat tombol **Deteksi** di kartu *Akun uji*.
4. **Pengamatan tester**: bila tester melihat fitur *login as* yang belum tercatat, ia melaporkannya di `report_test`
   (`impersonation`), dan AIT mencatatnya (sumber *tester*), kecuali developer sudah mengisi atau mengonfirmasinya.
5. Hasil tampil di kartu *Akun uji* beserta dasarnya (file:baris), dan bisa **diubah/dikonfirmasi** developer. Hasil yang
   belum dikonfirmasi tetap dipakai, dan brief tester menyebutnya.

Saat `switch_user` ke akun yang bukan pemegang peran itu, tester diberi jalan pintas: login di profil akun itu sebagai
akun uji ber-peran admin (password dev), lalu pakai *cara* login as untuk masuk sebagai akun tujuan; bila gagal, logout
dan login biasa. Peran dicocokkan tanpa peka huruf besar/kecil. SSO (Microsoft/Google) di luar cakupan: skenario yang
membutuhkannya ditandai *tidak bisa dicek* dengan alasan.

### Hasil

*Lolos* (tidak ada skenario gagal dan minimal satu lolos), *Gagal* (ada skenario gagal), *Tidak bisa dicek* (tidak ada
skenario yang bisa diperiksa: aplikasi mati, browser tidak terbuka, butuh login), atau *Dilewati*. Skenario yang gagal
membuat baris status *🟡 Selesai sebagian* dan dicatat di kolom error thread (`Testing: n skenario gagal.`), tetapi
**tidak** memicu tugas perbaikan; status thread tetap *Selesai*. Wrapper menerima hasilnya dan menyarankan langkah
berikutnya. Jawaban akhir memuat bagian **🧪 Testing** (verdict per skenario, error browser, dan **Output API**) — lihat
[Membaca output](04-penggunaan.md#membaca-output-di-claude-code). Detail lengkap, termasuk body request dan respons
setiap `http_request` (maks 30 request), ada di kartu *Testing* detail thread.

- **Data**: tester diminta memakai data yang ada, menandai data buatannya (mis. awalan `AIT test`), menghapusnya bila
  aplikasi menyediakan caranya, dan tidak mengubah data lain. Arahkan aplikasi yang dites ke database pengembangan.
- **Ulangi testing**: detail thread → **🧪 Ulangi testing** (thread *Selesai* yang punya rencana testing UI/API)
  menjalankan testing lagi dari rencana tersimpan — kode, verifikasi, dan jawaban akhir tidak berubah. Berguna setelah
  aplikasi dijalankan atau di-restart. Modalnya punya field username & password opsional (lihat [Login](#login)).
  Hasilnya menggantikan kartu Testing.

## 7. Wrapper

Wrapper menerima permintaan Anda, ringkasan rencana, kontrak, hasil setiap tugas termasuk tugas perbaikan (ringkasan,
error, maks 60 baris diff per tugas), ringkasan verifikasi per putaran (termasuk unit test), dan hasil testing, lalu menulis bagian jawaban
yang perlu penjelasan, dalam bahasa permintaan Anda: 1–3 kalimat tentang apa yang dikerjakan, saran untuk tugas gagal atau
masalah verifikasi yang tersisa, dan langkah lanjutan bila perlu. Fakta lainnya ditulis AIT sendiri dari hasil, bukan
oleh model: baris status (tugas, build, reviewer, unit test, testing) dan daftar *Perlu perhatian* sebelum teks Wrapper, tabel
*File berubah*, bagian *Unit test* dan *Testing*, dan footer (durasi, token, memory, wiki, sisa kuota provider, link thread) sesudahnya — lihat
[Membaca output di Claude Code](04-penggunaan.md#membaca-output-di-claude-code). Teks Wrapper di-stream ke Claude Code.

Jika tidak ada wrapper aktif atau wrapper gagal, Claude Code menampilkan ringkasan rencana sebagai pengganti teksnya;
yang disimpan sebagai jawaban thread (Dashboard, Telegram) adalah ringkasan otomatis (✅/❌/⏭️ per tugas, plus hasil
verifikasi).

Setelah itu ekstraksi memory dijalankan dan ditunggu paling lama 30 detik agar footer bisa menyebut jumlah memory
baru; lewat dari itu footer menulis *memory baru masih diproses* dan ekstraksi tetap selesai di latar belakang.

## 8. Setelah thread

- Status akhir: *Selesai* bila ada tugas yang berhasil (atau jawaban langsung), *Gagal* bila tidak ada. Thread yang
  berhenti pada pertanyaan planner berstatus *Menunggu jawaban* sampai dijawab, dibatalkan, atau kedaluwarsa.
- Waktu (termasuk verifikasi, unit test, dan testing; waktu menunggu jawaban tidak dihitung), token per peran (planner beserta porsi
  cache-nya, executor termasuk tugas perbaikan dan pemulihan test, reviewer, tester termasuk triase unit test, wrapper), dan setiap tugas tersimpan di database dan tampil di Dashboard.
- Hasil unit test, test baru, dan keputusan obsolete dicatat ke katalog project — lihat
  [Saat thread selesai](#saat-thread-selesai).
- Ekstraksi memory ditunggu sebelum footer jawaban (maks 30 detik, lalu lanjut di belakang) — lihat [Memory](06-memory.md).
- Thread yang *Selesai* menunggu dirangkum ke wiki bisnis project oleh penulis wiki (berkala, default tiap 6 jam) —
  lihat [Business memory](06-memory.md#business-memory-wiki-bisnis). Gambar yang dilampirkan di prompt disimpan
  sampai saat itu, agar screenshot bisa masuk ke halaman modulnya.
- Untuk thread dari Claude Code (juga yang gagal), Context Keeper memperbarui konteks sesi di belakang.
- Bila ada file yang diubah (juga thread yang dibatalkan/gagal di tengah), Struktur kode project dipindai ulang dan
  function yang diubah dibaca panggilannya (lihat [Panggilan function](#panggilan-function)), keduanya di belakang.

## Unit test

Setiap project punya **katalog unit test** yang dipakai bersama semua developer project itu (scope `ProjectKey`,
disimpan bersama long memory, jadi ikut pindah ke DB eksternal). Tujuannya: developer yang bekerja di project yang
asing ikut menjalankan test peninggalan developer sebelumnya, sehingga perubahannya tidak merusak perilaku yang sudah
dijaga. Katalog dan setting-nya ada di [Project > Detail → Unit test](05-menu.md#unit-test).

Test dijalankan **oleh AIT sendiri, tanpa model**. Model hanya dipakai untuk memberi judul dan tujuan test hasil
*Scan test* (node murah, seperti label memory), memutuskan nasib test per perubahan (planner), mendiagnosis test yang
gagal (node Tester), dan menulis atau memperbaiki kode test (executor).

### Runner yang dikenali

Perintahnya tetap per stack dan tidak pernah berasal dari model, sama seperti perintah build. Nama test hanya masuk
sebagai filter yang di-escape AIT.

| Stack | Suite dikenali dari | Perintah | Hasil |
|---|---|---|---|
| .NET | project (`.csproj`/`.fsproj`/`.vbproj`, juga yang tercantum di `.sln`/`.slnx`) yang mereferensikan xUnit, NUnit, MSTest, atau `Microsoft.NET.Test.Sdk` | `dotnet test <project> --logger trx` (+ `--filter`) | per test (TRX) |
| .NET Framework (csproj format lama) | sama | `vstest.console.exe` dari Visual Studio atas assembly test di `bin` (harus sudah di-build) | per test (TRX) |
| .NET, test project SDK-style yang mereferensikan project format lama (`ProjectReference`, sampai 4 tingkat) | sama | dibuild dulu oleh **MSBuild Visual Studio** (`-restore -p:RestorePackagesConfig=true`, `SolutionDir` = solution terdekat di atasnya, output ke folder milik AIT), lalu `vstest.console.exe` atas assembly hasilnya | per test (TRX) |
| Node.js | `package.json` dengan jest, vitest, atau mocha (dependency atau script `test`) | runner dari `node_modules` dijalankan lewat `node` dengan reporter JSON (+ filter judul) | per test |
| Node.js lain | script `test` lain (selain bawaan `npm init`) | `npm test` | hanya exit code: semua test suite itu lolos atau gagal bersama |
| Python | folder `pyproject.toml`/`setup.py`/`requirements.txt`/`Pipfile` yang berisi `test_*.py`/`*_test.py` | `python -m pytest --junitxml` (virtualenv `.venv`/`venv` dipakai bila ada) | per test (JUnit XML) |
| PHP | `composer.json` + `phpunit.xml(.dist)` | `vendor/bin/phpunit --log-junit` (+ `--filter`) | per test (JUnit XML) |
| Java (Maven) | `pom.xml` dengan folder `src/test` | `mvnw`/`mvn -B -q test` (+ `-Dtest=`) | per test (surefire) |
| JVM (Gradle) | `build.gradle(.kts)` dengan folder `src/test` | `gradlew`/`gradle test` (+ `--tests`) | per test (JUnit XML) |
| Go | `go.mod` dengan file `*_test.go` | `go test -json -count=1 ./...` (+ `-run`) | per test |
| Rust | `Cargo.toml` | `cargo test --no-fail-fast` | per test |

- Suite yang tidak bisa jalan di mesin ini ditandai dengan alasannya (mis. *jest tidak ada di node_modules (jalankan
  npm install dulu)*, *vendor/bin/phpunit tidak ada*, *assembly test belum di-build*). AIT tidak menginstal paket.
- **Kenapa test project campuran tidak memakai `dotnet test`:** `dotnet` tidak bisa me-restore `packages.config` dan tidak
  punya target Visual Studio (`Microsoft.WebApplication.targets`, dll.), jadi gagal membuild project .NET Framework lama.
  MSBuild Visual Studio yang dipakai build solusi (verifikasi) juga dipakai di sini, termasuk restore dan kredensial
  feed NuGet yang sama, sehingga test project baru tidak butuh login terpisah. Langkah build memakai sebagian dari
  batas waktu run test; kegagalannya (restore, compile) tampil sebagai *Test tidak bisa dijalankan* dengan akhir
  output MSBuild. Tanpa Visual Studio/Build Tools suite itu ditandai tidak bisa jalan dengan alasannya.
- Satu project menjalankan build atau test satu per satu (keduanya berebut `obj/`/`bin`), dengan batas waktu *Batas
  waktu run test* (default sama dengan *Timeout build*). Variabel `CI=true` diset agar runner tidak menunggu input.
- Bila lebih dari 120 test dipilih, filter dibuang dan seluruh suite dijalankan (command line terlalu panjang di
  Windows); hasilnya tetap dicocokkan per test.

### Isi katalog

Satu baris per test: nomor `UT-0001` (urut per project; nomor test yang dihapus tidak dipakai ulang), judul dan tujuan dalam bahasa
developer, skenario (*given / when / then* dengan nilai konkret), modul, file yang diuji (target), framework, file dan
nama test, kebutuhan (`database`, `network`, `config`), status, asal, pembuat dan thread asal, hasil terakhir (kapan,
oleh siapa, durasi, pesan gagal, alasan dilewati), dan riwayat 20 run terakhir per test.

| Status | Arti |
|---|---|
| Aktif | Dijalankan bila relevan. |
| Karantina | Tidak stabil (lolos saat diulang, atau menurut Tester); tidak dijalankan thread sampai diaktifkan lagi. Masih bisa dijalankan manual. |
| Obsolete | Fitur yang diuji dihapus oleh sebuah thread. Wajib punya alasan, tidak dijalankan, dan menunggu konfirmasi hapus dari developer. |
| Dihapus | Sudah dikonfirmasi hapus. Tetap tercatat (nomornya tidak dipakai ulang) dan tidak pernah dipulihkan. |

Asal: *AIT* (dibuat lewat thread), *Scan* (sudah ada di repo), *Ditulis ulang* (dipulihkan dari skenario — lihat
[Pemulihan](#pemulihan-test-yang-hilang)).

Katalog juga menyimpan **snapshot** isi file test (beserta method test-nya dan file pendukung yang dibutuhkan:
`.csproj` test, fixture, config runner) untuk pemulihan. Snapshot diambil saat test didaftarkan dan diperbarui setiap
kali test lolos. Snapshot **tidak pernah tampil** di UI atau dikirim lewat `/api`; satu-satunya jalan keluarnya adalah
[export memory](06-memory.md#export--import). Matikan *Simpan snapshot test* untuk project yang kodenya tidak boleh
disimpan di luar repository (snapshot yang ada ikut dihapus).

### Mengisi katalog

- **Otomatis**: thread pertama sebuah project yang punya suite test tetapi katalognya masih kosong memindai kode dulu
  (tanpa model). Judulnya dibentuk dari nama method (`Save_WithoutName_IsRejected` → *Save without name is rejected*).
- **Scan test** (Project > Detail): memindai lagi, mendaftarkan test baru, mengikuti test yang pindah file, dan memberi
  judul, tujuan, skenario, modul, serta target dengan node murah (Tools → Wrapper → Planner, seperti label memory).
- Deteksi statis: C# lewat Roslyn (`[Fact]`, `[Theory]`, `[Test]`, `[TestCase]`, `[TestMethod]`, …), stack lain lewat
  pola (`it(`/`test(`, `def test_`, `@Test`/`@ParameterizedTest`, `func TestX`, `#[test]`, method `test*` PHPUnit). Target ditebak dari nama
  file/class test (`UserServiceTests` → `UserService.*`) dan peta kode.
- **Lewat thread**: test baru yang dirancang planner (lihat di bawah) masuk katalog saat thread selesai, lengkap dengan
  skenario dari planner.

### Alur di thread

```text
0. Pemulihan test yang hilang (AIT, sebelum planner)
1. Planner  ──┬── baseline test relevan (AIT, paralel, file belum berubah)
2. Eksekusi   │   (implementasi + file test, paralel)
3. Build      │
4. Unit test (AIT) ── gagal? ── triase Tester ── tugas perbaikan ── ulangi 3–4 (maks 3 putaran)
5. Testing UI/API (Auto Testing), 6. Wrapper + catat hasil ke katalog
```

Tahap ini berjalan bila project punya `ProjectKey`, mode katalog bukan *Mati*, dan ada suite test yang dikenali.
Katalog dibaca paling lama 10 detik; store yang tidak bisa dihubungi membuat unit test dilewati, bukan menahan thread.

**Test relevan** = test aktif yang file test-nya, file targetnya, atau modulnya beririsan dengan file yang disentuh.
Mode *Semua test aktif* mengambil semuanya. Sebelum rencana ada, perkiraannya diambil dari file yang disebut prompt
dan peta kodenya; setelah rencana jadi, test terkait file tugas dan test yang di-`update` ditambahkan, test yang
di-`obsolete` dikeluarkan.

**Baseline** (hasil sebelum perubahan) dijalankan **paralel dengan planner**, karena file belum berubah. Eksekusi
menunggu baseline selesai (progres `🧪 Unit test · baseline · n test terkait`). Hasil dipakai ulang tanpa menjalankan:

- bila isi file test dan file targetnya sama persis dengan saat hasil terakhir dicatat (hash) dan hasil itu belum lewat
  14 hari — kode yang sama memberi hasil yang sama, siapa pun yang menjalankannya;
- bila test itu dilewati karena lingkungan di mesin ini kurang dari 24 jam lalu (tanpa menunggu timeout koneksi lagi).

### Keputusan planner

Brief planner mendapat bagian `# Unit tests`: suite, aturan membuat test baru, dan test relevan (maks 30) berisi
nomor, judul, tujuan, file → target, dan hasil terakhir. `submit_plan` mendapat field `tests`:

```json
"tests": {
  "decisions": [
    { "test": "UT-0012", "decision": "update", "reason": "Request: \"nama boleh kosong untuk user tamu\"" },
    { "test": "UT-0015", "decision": "obsolete", "reason": "Fitur export PDF dihapus sesuai permintaan." }
  ],
  "new": [
    {
      "title": "Simpan user tamu tanpa nama diterima",
      "purpose": "User tamu boleh disimpan tanpa nama.",
      "scenario": "given user tamu dengan nama \"\", when Save, then tersimpan tanpa error",
      "file": "tests/Shop.Tests/UserTests.cs",
      "name": "Save_GuestWithoutName_IsAccepted",
      "targets": ["src/Shop/UserService.cs"],
      "requires": []
    }
  ]
}
```

| Keputusan | Arti | Akibat |
|---|---|---|
| `keep` (default) | Perilaku yang diuji tidak berubah | Harus tetap lolos; bila gagal, **kode** yang diperbaiki |
| `update` | Request **sengaja** mengubah perilaku yang diuji | Wajib ada tugas edit file test-nya di rencana yang sama dan alasan yang mengutip request |
| `obsolete` | Request menghapus fitur yang diuji | Wajib alasan. Test tidak dijalankan, berstatus *Obsolete* setelah thread selesai, dan menunggu konfirmasi hapus |
| `new` | Test baru | Judul, skenario, file, nama test persis seperti di kode, target; wajib ada tugas create/edit file test itu |

- Test baru dibuat **hanya bila developer memintanya**, kecuali setting *Buat unit test baru* = *Otomatis untuk
  perubahan logika bisnis* (layer Logic yang belum punya test) atau *Tidak pernah*. Dengan Auto Testing aktif, perubahan
  yang tidak bisa dicek Tester di aplikasi yang berjalan **selalu** dibuatkan test dry-run (kecuali *Tidak pernah*) —
  lihat [Background job dan sistem eksternal](#background-job-dan-sistem-eksternal).
- **Project tanpa test project**: bila test baru dibutuhkan, planner langsung merencanakan test project-nya juga, tanpa
  bertanya: untuk .NET project xUnit `<Nama>.Tests` di sebelah project yang diuji (`Microsoft.NET.Test.Sdk`, `xunit`,
  `xunit.runner.visualstudio`, Moq bila perlu fake, `ProjectReference` ke project yang diuji); untuk Node.js runner yang
  sudah dipakai package, atau Vitest; pytest untuk Python, PHPUnit untuk PHP, JUnit untuk Java. Solution `.slnx` ditambah
  baris `<Project Path=…>`; `.sln` klasik dibiarkan (`dotnet test` membuild test project sendiri). Brief planner menyebut
  *No test project yet*, dan `run_tests` tidak ditawarkan. Setelah executor selesai, AIT membaca ulang suite dari
  manifest di disk (maks 3 folder dalam), lalu test baru dijalankan dan didaftarkan seperti biasa. Rencana yang tidak
  menambah test tidak menjalankan unit test apa pun.
- Kasus dirancang planner dari request dan aturan bisnis, bukan dari implementasi. Executor file test hanya melihat
  kontrak dan instruksi (bukan kode implementasinya) dan mengikuti gaya file test lain di suite yang sama.
- Planner tidak boleh mengubah atau mempensiunkan test developer lain hanya agar perubahannya lolos; bila ragu ia
  bertanya lewat `ask_user`.
- Validator menolak rencana bila nomor test tidak ada di katalog, satu test punya dua keputusan, `update`/`obsolete`
  tanpa alasan, `update` tanpa tugas edit file test-nya, test baru tanpa nama/judul/skenario/file atau tanpa tugas yang
  menulis file-nya, lebih dari 15 test baru, test baru padahal dimatikan, atau test baru di rencana tanpa tugas.

Tool `run_tests(tests?, file?, all?)` menjalankan test katalog (nomor, satu file test, atau semua) dan mengembalikan
hasilnya ke planner: lolos, gagal beserta pesannya, atau dilewati beserta alasannya. Dipakai hanya untuk permintaan
yang memang soal test (*"perbaiki test yang gagal"*, *"kenapa UT-0012 gagal"*). Hasilnya sekaligus menjadi baseline,
dan dipakai ulang oleh `tests.run` di thread yang sama (test tidak dijalankan dua kali; progres: *n dari run_tests
planner*). Tool ini tidak bisa didelegasikan ke tools node.

**Menguji sebuah perubahan** (*"test perubahan ini"*, *"jalankan test untuk fitur X"*) hanya memakai test katalog yang
meng-cover perubahan itu (file test dan targetnya). Test fitur lain tidak dijalankan sebagai pengganti — lolosnya tidak
membuktikan apa pun tentang perubahan ini. Bila belum ada yang meng-cover, permintaan itu dianggap permintaan test:
planner merancang test `new` untuk perubahan tersebut (dependensi ke database, API, atau service lain di-mock), atau
bertanya dulu lewat `ask_user` bila cakupannya tidak jelas. Saat tidak ada test terkait, brief planner menyebut file
test apa saja yang ada di katalog (maks 10).

### Setelah build

Unit test berjalan di setiap putaran verifikasi **setelah build lolos** (project tanpa perintah build dianggap lolos):
test relevan, test yang di-`update`, test di file yang disentuh tugas perbaikan, dan test baru (dicari di file-nya
setelah executor selesai; yang tidak ditemukan dilaporkan *test baru tidak ditemukan*). Test yang gagal **diulang
sekali**; bila lalu lolos, ia ditandai *tidak stabil* dan tidak dihitung gagal.

| Hasil | Arti | Tindakan |
|---|---|---|
| ✅ lolos | | |
| ✅ diperbaiki | Gagal sebelum perubahan, sekarang lolos | dilaporkan |
| ❌ regresi | Lolos sebelum perubahan (baseline, atau hasil terakhir katalog), sekarang gagal; atau test `update` yang gagal | diperbaiki |
| ❌ test baru gagal | Test `new` gagal | diperbaiki |
| 🟠 sudah gagal sebelumnya | Gagal sebelum dan sesudah perubahan | hanya dilaporkan, bukan kesalahan perubahan ini |
| 〰️ tidak stabil | Gagal lalu lolos saat diulang, atau menurut Tester | dikarantina saat thread selesai |
| ⏭️ dilewati | Lihat [Test yang dilewati](#test-yang-dilewati) | dilaporkan dengan alasan |

**Triase Tester.** Bila ada regresi atau test baru yang gagal dan ada node **Tester** aktif, Tester (model kuat)
menerima request, ringkasan rencana, keputusan planner per test, diff setiap file yang berubah (maks 150 baris per
file), serta kode, tujuan, dan pesan gagal tiap test, lalu memutuskan per test:

| Verdict | Akibat |
|---|---|
| `regression` | Tugas perbaikan ke **kode** yang ditunjuk Tester |
| `test_wrong` | Tugas perbaikan ke **file test** — hanya untuk test `new` atau `update` thread ini. Untuk test lama milik developer lain, kodenya yang diperbaiki dan catatannya masuk laporan |
| `plan_wrong` | Perilaku memang sengaja diubah request tetapi planner menandai `keep`: test dijadikan `update`, diperbaiki mengikuti perilaku baru, dan developer diberi tahu di laporan |
| `environment` | Bukan kesalahan kode: test menjadi *dilewati* dengan alasan dari Tester |
| `flaky` | Tidak stabil: dikarantina saat thread selesai |

Semua lolos = 0 token Tester. **Tanpa node Tester** (atau bila Tester gagal menjawab) aturannya mekanis: yang
diperbaiki adalah file target test yang diubah thread, atau satu-satunya file non-test yang diubah; bila tidak bisa
ditentukan, kegagalan dilaporkan tanpa perbaikan. Progres: `🩺 Triase · n test gagal · model`.

**Perbaikan** memakai tugas `V<putaran>-<n>` yang sama dengan build/reviewer (judul *Fix failing unit tests*, bagian
*Unit tests that fail after the change* di instruksinya: test, tujuan, pesan gagal, dan arahan *jangan mengubah test*
untuk perbaikan kode). Putaran perbaikan unit test dibatasi **3** (terpisah dari *Putaran perbaikan* build/reviewer,
tetapi `0` di sana mematikan keduanya); regresi yang tersisa dilaporkan apa adanya. Setiap putaran membuild ulang lalu
menjalankan test lagi.

Progres per putaran di fase `🔍 Verifikasi`:

```text
│  ├─ ✅ Build berhasil (9.8s)
│  ├─ 🔴 Unit test · 14 test · 12 lolos, 1 regresi, 1 sudah gagal sebelum perubahan (8.2s)
│  │     UT-0012 Simpan user tanpa nama ditolak — Assert.False() Failure …
│  ├─ 🩺 Triase · 1 test gagal · cc/claude-opus
│  ├─ 🔧 Perbaikan 1 · 1 tugas
```

### Test yang dilewati

Hasil *dilewati* **selalu disertai alasan** dan tidak pernah dihitung regresi:

| Sumber | Contoh alasan |
|---|---|
| Gagal karena lingkungan, dikenali dari pesan error (koneksi database ditolak, host tidak dikenal, connection string kosong, file config tidak ada) | *Butuh database yang tidak tersedia di mesin ini: Login failed for user 'app'* |
| Triase Tester `environment` | *Lingkungan (menurut Tester): service pembayaran tidak berjalan* |
| Di-skip oleh test-nya sendiri (`[Fact(Skip = "…")]`, `@Disabled`, `pytest.skip`, `it.skip`) | alasan dari kodenya, atau *Di-skip oleh runner tanpa alasan.* |
| Runner tidak bisa dijalankan | *jest tidak ada di node_modules (jalankan npm install dulu).* |
| Dilewati karena lingkungan di mesin ini kurang dari 24 jam lalu | alasan sebelumnya + *(tidak dicoba lagi: run terakhir di mesin ini juga dilewati)* |

**Pengecualian: test baru dari thread yang sama.** Test yang ditulis untuk perubahan ini adalah test dry-run (semua
database, jaringan, dan config dipalsukan), jadi kegagalan karena database/jaringan/config **tidak pernah dilewati**:
test itu dianggap tidak terisolasi dan diperbaiki (fix task ke file test-nya, maks 3 putaran) dengan instruksi
*fake or inject every database, network call, config and clock it touches*. Verdict `environment` dari Tester untuk
test baru diperlakukan sama **bila pesannya memang error database/jaringan/config saat test berjalan**. Restore atau
build yang tidak bisa jalan di mesin ini (feed NuGet/npm yang butuh kredensial: `NU1301 401 Unauthorized`, SDK atau
targeting pack tidak ada) tetap *dilewati* dengan alasannya, karena tidak ada perubahan di kode test yang bisa
memperbaikinya. Tanpa pengecualian ini sebuah thread bisa selesai *lolos* tanpa satu pun test baru yang pernah jalan.

Kebutuhan yang terdeteksi (`database`, `network`, `config`) dicatat di test itu. Menjalankan test dari Project > Detail
selalu mencoba lagi. Test yang tidak dilaporkan runner sama sekali (nama atau file-nya berubah) tampil *tidak
dilaporkan runner*; jalankan *Scan test* untuk mengikuti perubahannya.

### Pemulihan test yang hilang

Test adalah bagian standar project, jadi test katalog (aktif atau karantina) yang file atau method-nya tidak ada di
repository developer **dipulihkan otomatis sebelum planner berjalan** (progres `🧷 Pemulihan unit test · n test`),
berhenti di lapis pertama yang berhasil:

1. **Pindah file**: test dengan nama yang sama ditemukan di file lain → katalog mengikuti file barunya.
2. **Catatan git** (tanpa model): commit tempat snapshot diambil dicek terhadap branch Anda. Belum ada → *UT-0012
   berasal dari commit `abc12345` (branch feature/x) yang belum ada di branch ini; pull/merge branch itu …*. Sudah ada
   tetapi file-nya terhapus setelahnya → saran menandai test itu obsolete bila memang sengaja dihapus. Pemulihan tetap
   lanjut supaya pengamannya ada sekarang juga.
3. **Snapshot** (tanpa model, persis): file yang hilang ditulis dari snapshot beserta file pendukung yang juga hilang
   (project test baru didaftarkan dengan `dotnet sln add`). Method yang hilang dari file yang masih ada disisipkan
   kembali lewat Roslyn untuk C#; stack lain lewat tugas executor kecil (`R1`, …) yang menyalin kode method dari
   snapshot.
4. **Tulis ulang dari skenario** (pakai model, cadangan terakhir): executor menulis test baru dari skenario katalog
   dengan file test lain sebagai contoh gaya. Asalnya menjadi *Ditulis ulang* dan perlu ditinjau developer.

File yang sudah ada **tidak pernah ditimpa**. Pemulihan **tidak ikut rollback** thread (bukan bagian dari request):
file hasil pulihan muncul sebagai file baru di `git status` untuk Anda commit. Test hasil pulihan yang gagal di
baseline dilaporkan *sudah gagal sebelum perubahan* (kode Anda mungkin sudah berbeda dari saat test dibuat), bukan
regresi.

### Thread yang hanya menguji

Permintaan yang hanya meminta pengujian tanpa perubahan kode (*"jalankan semua unit test"*, *"tolong tes fitur
Homecoming"*) tidak berakhir di jawaban langsung. Planner mengirim rencana tanpa tugas dengan `tests.run` (nomor test,
atau `["all"]`) dan/atau bagian `test` [Auto Testing](#6-testing-auto-testing); AIT menjalankan unit test dan/atau
Tester terhadap perilaku yang sudah ada, tanpa eksekusi, build, atau review, lalu Wrapper menjelaskan hasilnya. Baris
status: `🧪 **Pengujian selesai** · tanpa perubahan kode · unit test lolos (12)` (atau *ada yang gagal*). Test yang
gagal dicatat di kolom error thread.

| Yang diminta | Yang dijalankan |
|---|---|
| Form / halaman | Rencana tanpa tugas + `test.level = ui`: Tester mencoba flow-nya di browser (aplikasi dijalankan AIT bila belum jalan) |
| API | Rencana tanpa tugas + `test.level = api`: Tester mengirim request, respons tampil di *Output API* |
| Function / scheduler yang sudah punya test di katalog | Rencana tanpa tugas + `tests.run` |
| Function / scheduler **tanpa** test (atau yang tidak terlihat dari aplikasi: job Hangfire/Quartz, hosted service, consumer) | Planner menulis **test dry-run** baru: tugasnya hanya file test (dan test project bila belum ada), **kode yang diuji tidak diubah**. Test itu dijalankan setelah build lalu masuk katalog. File test-nya tetap ada di repository; rollback thread bila tidak ingin menyimpannya |

Planner tidak boleh menjawab permintaan seperti itu tanpa test atau menyuruh developer mengetes sendiri. Rencana
tanpa tugas yang berisi `tests.new` ditolak validator (*tests.new needs tasks that write the tests*), sehingga test
baru tidak pernah hilang diam-diam. Butuh project key dan katalog unit test yang tidak *Mati*.

### Saat thread selesai

Hasil dicatat ke katalog, juga bila semua tugasnya gagal (thread yang dibatalkan tidak mencatat hasilnya):

- hasil terakhir setiap test yang dijalankan (hasil setelah perubahan menang; yang hanya jalan di baseline menyimpan
  hasil itu tanpa hash), riwayat run (fase `baseline`, `after`, `manual`, `flaky`), dan snapshot test yang lolos;
- test baru masuk katalog dengan nomor berikutnya, modul dari peta kode, dan snapshot;
- keputusan `obsolete` (hanya bila ada tugas yang berhasil) dan karantina test yang tidak stabil.

Jawaban akhir memuat hasilnya di baris status (`unit test lolos (12)`, `unit test: 1 regresi`, …; regresi atau test baru
yang gagal membuat status *🟡 Selesai sebagian*) dan bagian **🧪 Unit test**: hanya test yang perlu perhatian (maks 10:
gagal, diperbaiki, sudah gagal sebelumnya, tidak stabil, dilewati beserta alasannya), test yang ditandai obsolete,
test yang dipulihkan, dan catatan (git, keputusan Tester). Test yang tetap lolos hanya dihitung. Detail lengkap ada di
kartu *Unit test* detail thread.

**Rollback** thread juga membatalkan perubahannya di katalog: test baru yang ditambahkan thread itu dihapus dari
katalog dan keputusan `obsolete`-nya dikembalikan ke *Aktif*.

Batasan saat ini: **🔁 Ulangi yang gagal** tidak menjalankan unit test (hanya build/reviewer); jalankan test dari
Project > Detail setelahnya.

## Panggilan function

AIT mencatat apa saja yang dipanggil tiap function, sedikit demi sedikit, hanya di kode yang benar-benar disentuh:

- **Pemicu**: function di baris yang dibaca planner atau tools node (`read_file`, file di dalam project saja), dan
  function yang diubah thread (blok baris yang berbeda dari isi sebelum thread), serta function yang ditanyakan
  planner lewat `function_calls` tetapi belum tercatat atau sudah berubah. Tidak ada scan seluruh project.
- **Pembaca**: node **Tools Node** aktif pertama (dengan node cadangannya). Tanpa Tools Node aktif, tidak ada yang
  dibaca. Satu panggilan model berisi beberapa function satu file (maks 20 function / ±16.000 karakter; function
  panjang dipotong di 8.000 karakter). Model menyebut panggilan, jenisnya (`function`, `method`, `http`, `view`,
  `sql`, `other`), dan barisnya; library/framework (jQuery DOM, LINQ, logging, …) diabaikan.
- **Pemeriksaan**: panggilan yang namanya tidak tertulis di baris yang disebut (±1 baris) dibuang; untuk `http`, bagian
  URL-nya harus ada di badan function. **Lokasi** deklarasi dicari AIT sendiri dari Struktur kode, bukan oleh model:
  file yang sama dulu, lalu class yang disebut (`clsKlaimDFBL.GetByDocNo` → file class itu, termasuk class partial),
  lalu nama yang unik di project. URL dicocokkan ke `{Controller}Controller.{Action}` (dua segmen terakhir, jadi area
  di depannya tidak masalah) atau ke file script dengan path itu. Yang ragu dibiarkan *belum ditemukan*.
- **Hemat**: function yang isinya sama dengan saat terakhir dibaca tidak dikirim lagi; yang gagal dicoba lagi paling
  cepat 1 jam kemudian. Antrean diproses satu file per waktu di belakang; planner dan executor tidak menunggu.
- Hasil disimpan di database lokal (tabel `CodeFunctionScans` dan `CodeFunctionCalls`), mengikuti file clone di mesin
  ini; tampil di Project → detail → *Struktur kode*.
- **Dipakai planner**: `function_calls` memberi panggilan sebuah function beserta pemanggilnya yang tercatat, dan
  `code_outline` menandai function yang sudah punya catatan dengan `[calls: N]`. Planner bisa mengikuti alur
  (layar → endpoint → action → logic) tanpa membaca setiap file di jalurnya, lalu memastikan dengan kode. Makin sering
  project dikerjakan lewat AIT, makin lengkap catatannya.

## Konteks sesi (Context Keeper)

Planner tidak membawa ingatan antar prompt: setiap prompt memulai planner baru. Supaya chat yang panjang tetap
nyambung tanpa mengirim seluruh percakapan, AIT menyimpan **ringkasan sesi** per thread:

1. Baris pertama setiap balasan orkestrasi adalah penanda ``**AIT** · `NamaProject` · thread `3f2a1b9c` `` (selalu
   dikirim, juga saat progres dimatikan). Penanda ini ikut tersimpan di chat Claude Code. Penanda lama
   ``**AIThread** · …`` dari balasan sebelum rename tetap dikenali.
2. Setelah thread selesai, Context Keeper menulis ringkasan sesi (tujuan, keputusan & batasan, log kerja per thread,
   fakta penting berupa penunjuk file, hal yang belum selesai) dan menyimpannya pada thread itu.
3. Di prompt berikutnya, proxy mencari penanda di history yang dikirim Claude Code, mengambil ringkasan dari thread
   terbaru yang punya ringkasan, dan memberikannya ke planner beserta pesan setelah thread itu saja.

Karena yang dipakai selalu penanda yang masih ada di chat, konteks otomatis mengikuti chat: setelah *rewind* atau
edit pesan, ringkasan kembali ke thread terakhir yang tersisa; `/clear` atau chat baru mulai dari kosong; resume sesi
lama memulihkan ringkasannya. Pencarian dibatasi ke project yang sama dan thread 60 hari terakhir.

- **Model**: node *Context Keeper* aktif dengan prioritas terkecil; tanpa node itu dipakai Wrapper. Tanpa keduanya,
  atau bila modelnya gagal, thread baru dicatat apa adanya di bagian *Not yet summarized* dan dirangkum pada
  pembaruan berikutnya.
- **Mencegah ringkasan melenceng**: ringkasan biasanya diperbarui dari ringkasan sebelumnya, tetapi setiap thread ke-8
  (dan setiap kali ringkasan sebelumnya tidak berasal dari thread tepat sebelumnya) disusun ulang dari catatan
  maksimal 15 thread terakhir di database.
- **Detail tidak hilang**: planner bisa memanggil `read_thread` untuk catatan lengkap thread mana pun, dan
  ringkasan berisi penunjuk file, bukan isi file, sehingga planner membaca versi terbaru.
- **Hasil analisa ikut disimpan** (`ThreadRun.InvestigationJson`): selama merencanakan, AIT mencatat file yang dibaca
  planner beserta rentang barisnya (digabung, maks 40 file), lookup (`find_symbol`, `find_references`,
  `search_text`, `find_files`, `function_calls` dengan awal hasilnya; `code_outline` tanpa hasil, maks 30), dan
  temuan sub agent `investigate` (maks 8). Catatan ini masuk ke brief prompt berikutnya di chat yang sama, file yang
  dibaca ikut di-outline, Context Keeper menjadikannya *Key facts*, dan `read_thread` menampilkannya. Pertanyaan
  lanjutan ("button A itu type apa?") jadi dijawab dari analisa sebelumnya; planner hanya membaca ulang rentang baris
  yang perlu dipastikan. Thread yang dilanjutkan setelah pertanyaan planner meneruskan catatan sebelum jeda.
- Ringkasan terbaru bisa dilihat di detail thread (panel *Konteks sesi dari Context Keeper*).

Untuk sesi Claude Code biasa (termasuk yang tidak lewat proxy), Context Keeper juga bisa menjembatani `/clear`
lewat hook resmi Claude Code — lihat [Context Keeper untuk /clear](03-integrasi-claude-code.md#context-keeper-untuk-clear).

## Skill pada node

Skill adalah instruksi siap pakai (folder dengan `SKILL.md`, gaya Claude Code / Agent Skills) yang membuat model
lebih mahir di satu jenis pekerjaan. AIT menyimpannya di **gudang skill** offline dan menempelkannya ke node.

1. **Menarik link** (Setting → Skill). Link dikenali lalu diunduh ke folder staging:
   - GitHub/GitLab sebagai arsip ZIP, tanpa perlu git. Link `/tree/<ref>/<folder>` hanya mengambil folder itu.
   - URL `.git` atau host git lain lewat `git clone --depth 1`, yang juga menjadi cadangan bila unduhan ZIP gagal.
   - File `.md` diambil apa adanya.
   - Halaman web: teks dan tautannya dibaca model.
2. **Skill Orchestrator memeriksa isinya**, memakai model yang dipilih di tab Skill (default: node Planner aktif).
   - Repository dengan `SKILL.md`: tiap skill diberi tag kata kunci, dan entri yang bukan skill dibuang.
   - Repository tanpa `SKILL.md`: model menunjuk file Markdown mana yang merupakan skill (mis. `agents/*.md`, aturan Cursor).
   - Halaman web: skill yang tertulis di halaman disimpan sebagai `SKILL.md`, dan hingga 5 repository yang ditautkan
     ditarik sebagai sumber turunan.
3. **Gudang** ada di `%LOCALAPPDATA%\AIThread\skills\<id sumber>` (ubah lewat `AIThread:SkillsDirectory`). Isi `SKILL.md`
   juga disimpan di database. *Sync* menarik ulang sumber; skill yang sama (folder + file) tetap terpasang di node,
   sedangkan skill yang hilang dari sumber dilepas dari node.
4. **Memilih skill untuk node** (modal Node → *Cari skill*). Kebutuhan yang Anda tulis dicocokkan oleh Skill
   Orchestrator dengan nama, deskripsi, dan tag semua skill (gudang besar disaring kata kunci dulu menjadi 150
   kandidat). Hasilnya maks 6 saran dengan alasan; saran dengan skor ≥ 0,6 sudah tercentang. Yang dipilih tampil sebagai
   chip dan tersimpan sebagai relasi node–skill (maks 10 per node), **bukan** teks di *Instruksi tambahan*.
5. **Saat runtime**, system prompt node berisi prompt peran, lalu *Additional instructions*, lalu bagian
   *Active skills*. Bagian itu memuat satu blok `<skill name="…" folder="…">` per skill, berisi isi lengkap
   `SKILL.md`, dan model diminta menerapkan workflow serta checklist skill tersebut. Total isi skill dibatasi
   *Batas karakter skill per node* (default 40.000, dibagi rata). Skill yang lebih panjang dipotong dengan penunjuk ke
   file lengkapnya. Planner juga diberi tahu folder skill, sehingga file pendukung (referensi, script, template) bisa
   ia baca dengan `read_file` dan ia lampirkan ke tugas sebagai `context` untuk executor. Ini berlaku untuk semua
   peran: Planner, Executor, Reviewer, Wrapper, dan Context Keeper.

## Batas keamanan

- Planner hanya bisa membaca; executor hanya bisa menulis file target tugasnya. Penulisan dibatasi ke root workspace
  dan tidak bisa menyentuh folder yang disembunyikan. Pembacaan planner boleh keluar root (path absolut), jadi ia dapat
  membaca file yang bisa dibaca akun yang menjalankan AIT, kecuali file kredensial (kunci, sertifikat, kunci SSH,
  `.npmrc`, `~/.aws`, user-secrets, data AIT), yang tidak pernah dibaca maupun ditulis. Permintaan "cek" ke folder lain
  hanya menghasilkan jawaban; mengubah file di luar root tidak didukung.
- **Secret disamarkan** sebelum teks apa pun dikirim ke model: isi file, prompt dan riwayat chat, output tool, respons
  aplikasi saat testing. Password, API key, token, JWT, private key, dan password di connection string diganti
  `{{AIT_SECRET:…}}`, lalu nilai aslinya dipasang kembali hanya saat executor menulis file (atau Tester memanggil
  aplikasi). Log, memory, wiki, dan export juga disimpan dalam bentuk tersamar. Detail dan batasannya ada di
  [Keamanan & privasi](12-keamanan.md).
- Satu-satunya perintah yang dijalankan AIT adalah **perintah build** project (tabel di
  [Verifikasi](#5-verifikasi-build-dan-reviewer)), dengan program dan argumen tetap; model hanya memilih manifest di
  dalam root. Build menjalankan apa pun yang didefinisikan project itu sendiri (target MSBuild, script `build` di
  `package.json`, plugin Maven), jadi matikan *Build & verifikasi otomatis* untuk project yang tidak Anda percaya.
  Tidak ada perintah shell lain (test, install paket, migrasi). Pengecualiannya `git clone --depth 1` saat menarik
  sumber skill dari host git non-GitHub/GitLab (atau bila unduhan ZIP gagal). Clone ini memakai URL yang Anda tempel,
  tanpa prompt kredensial, dan folder `.git`-nya dibuang.
- **Auto Testing** menjalankan browser lewat Playwright (Edge/Chrome/Chromium dengan proses driver Node.js: `node` di
  `PATH`, atau diunduh sekali dari nodejs.org bila tidak ada) dan request HTTP — keduanya hanya ke alamat mesin ini (`localhost`, `127.0.0.1`, `[::1]`);
  URL lain ditolak, apa pun isi rencana atau jawaban model. Link yang diikuti halaman itu sendiri tidak dibatasi. Tester
  bertindak seperti pengguna aplikasi Anda: ia bisa membuat atau mengubah data lewat aplikasi itu, jadi nyalakan Auto
  Testing hanya untuk aplikasi yang berjalan dengan data pengembangan. Sertifikat HTTPS tidak diperiksa (sertifikat
  pengembangan localhost), dan request HTTP tester memakai akun Windows yang menjalankan AIT bila aplikasi meminta
  Windows Authentication. Password login testing hanya ada di memori AIT dan tidak dikirim ke model (lihat
  [Login](#login)).
- Isi skill masuk ke system prompt model node. Tarik skill hanya dari sumber yang Anda percaya, karena skill bisa
  mengarahkan perilaku model.
- AIT menulis ke folder mana pun yang dikirim sebagai working directory. Biarkan aplikasi hanya mendengarkan
  `localhost`, dan isi *Proxy API key* bila mesin dapat diakses orang lain.
- API key 9router disimpan terenkripsi dengan ASP.NET Core Data Protection.
