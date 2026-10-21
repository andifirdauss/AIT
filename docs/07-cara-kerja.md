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
8. permintaan terbaru developer (tanpa `<system-reminder>`), maks 40.000 karakter.

Bila sebuah bagian melebihi batas, yang dibuang adalah bagian tengahnya; awal dan akhir teks tetap ada.

Lalu planner bekerja dalam putaran tool (maks *Langkah planner*). Semua tool **read-only**, kecuali `run_build` yang
menjalankan build project (tidak mengubah kode; hanya ditawarkan bila *Build & verifikasi otomatis* menyala dan project
punya perintah build). Path relatif dihitung dari
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
- Permintaan tanpa perubahan file → `tasks` kosong dan jawaban di `answer`; tidak ada executor yang berjalan.

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
- `dependsOn` menunjuk tugas yang tidak ada, dirinya sendiri, atau membentuk siklus.

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
- **node Reviewer** aktif (yang prioritasnya terkecil dipakai).

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
3. **Tugas perbaikan**. Masalah dikelompokkan per file menjadi tugas `V<putaran>-<n>` (mis. `V1-1`) yang dikerjakan
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

- Maksimal *Putaran perbaikan* putaran (default 2). Nilai `0` = hanya verifikasi dan laporkan.
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
  library, job tanpa endpoint, script database saja) — alasannya di `notes`.
- `baseUrl`: dicari planner sendiri dari `Properties/launchSettings.json`, `appsettings*.json` (Kestrel/`urls`), script
  `package.json` dan konfigurasi dev server (vite, next, angular), `.env` (`PORT`), atau port `docker-compose`. Harus
  alamat mesin ini: `localhost`, `*.localhost`, `127.0.0.1`, atau `[::1]`.
- `scenarios`: 1–6 skenario (jalur utama dulu, lalu kasus tepi yang ditangani perubahan), masing-masing dengan `steps`
  yang memakai nama dari kontrak dan `expect` yang bisa dilihat (teks, nilai, status code, field respons).
- Validator mengembalikan rencana ke planner bila `test` tidak ada, `level` tidak dikenal, `baseUrl` kosong atau bukan
  localhost, tidak ada skenario, atau skenario tanpa `expect`. Tanpa node Tester aktif planner tidak diminta bagian ini,
  dan bagian `test` yang tetap dikirim diabaikan. Rencana tanpa tugas tidak dites.

### Jalannya testing

1. **Dilewati** bila `level` = `none` (progres: *Testing · dilewati* + alasan planner), atau bila build terakhir masih
   gagal (aplikasi belum bisa memuat perubahan). Tidak berjalan sama sekali bila tidak ada tugas yang berhasil.
2. **Cek aplikasi**: sebelum memanggil model, AIT mengirim `GET` ke `baseUrl` (5 detik). Respons apa pun (juga 404 atau
   redirect ke login) berarti aplikasinya berjalan. Tanpa respons, semua skenario *tidak bisa dicek* tanpa token
   terpakai, dengan pesan untuk menjalankan aplikasinya dulu. **AIT tidak menjalankan aplikasi Anda**: jalankan sendiri
   (mis. `dotnet watch`, `npm run dev`); hot reload membuat perubahan executor langsung termuat.
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

### Browser tester

- Dipakai **Microsoft Edge** (bawaan Windows), lalu **Google Chrome**, lalu **Chromium** milik Playwright (diunduh sekali
  ±150 MB ke `%LOCALAPPDATA%\ms-playwright` bila keduanya tidak ada). Driver Playwright ikut terpasang bersama AIT
  (folder `.playwright` di samping `AIThread.exe`).
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
error, maks 60 baris diff per tugas), ringkasan verifikasi per putaran, dan hasil testing, lalu menulis bagian jawaban
yang perlu penjelasan, dalam bahasa permintaan Anda: 1–3 kalimat tentang apa yang dikerjakan, saran untuk tugas gagal atau
masalah verifikasi yang tersisa, dan langkah lanjutan bila perlu. Fakta lainnya ditulis AIT sendiri dari hasil, bukan
oleh model: baris status (tugas, build, reviewer, testing) dan daftar *Perlu perhatian* sebelum teks Wrapper, tabel
*File berubah*, bagian *Testing*, dan footer (durasi, token, memory, wiki, sisa kuota provider, link thread) sesudahnya — lihat
[Membaca output di Claude Code](04-penggunaan.md#membaca-output-di-claude-code). Teks Wrapper di-stream ke Claude Code.

Jika tidak ada wrapper aktif atau wrapper gagal, Claude Code menampilkan ringkasan rencana sebagai pengganti teksnya;
yang disimpan sebagai jawaban thread (Dashboard, Telegram) adalah ringkasan otomatis (✅/❌/⏭️ per tugas, plus hasil
verifikasi).

Setelah itu ekstraksi memory dijalankan dan ditunggu paling lama 30 detik agar footer bisa menyebut jumlah memory
baru; lewat dari itu footer menulis *memory baru masih diproses* dan ekstraksi tetap selesai di latar belakang.

## 8. Setelah thread

- Status akhir: *Selesai* bila ada tugas yang berhasil (atau jawaban langsung), *Gagal* bila tidak ada. Thread yang
  berhenti pada pertanyaan planner berstatus *Menunggu jawaban* sampai dijawab, dibatalkan, atau kedaluwarsa.
- Waktu (termasuk verifikasi dan testing; waktu menunggu jawaban tidak dihitung), token per peran (planner beserta porsi
  cache-nya, executor termasuk tugas perbaikan, reviewer, tester, wrapper), dan setiap tugas tersimpan di database dan tampil di Dashboard.
- Ekstraksi memory ditunggu sebelum footer jawaban (maks 30 detik, lalu lanjut di belakang) — lihat [Memory](06-memory.md).
- Thread yang *Selesai* menunggu dirangkum ke wiki bisnis project oleh penulis wiki (berkala, default tiap 6 jam) —
  lihat [Business memory](06-memory.md#business-memory-wiki-bisnis). Gambar yang dilampirkan di prompt disimpan
  sampai saat itu, agar screenshot bisa masuk ke halaman modulnya.
- Untuk thread dari Claude Code (juga yang gagal), Context Keeper memperbarui konteks sesi di belakang.
- Bila ada file yang diubah (juga thread yang dibatalkan/gagal di tengah), Struktur kode project dipindai ulang dan
  function yang diubah dibaca panggilannya (lihat [Panggilan function](#panggilan-function)), keduanya di belakang.

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
  membaca file apa pun yang bisa dibaca akun yang menjalankan AIT, dan isinya dikirim ke model planner. Permintaan
  "cek" ke folder lain hanya menghasilkan jawaban; mengubah file di luar root tidak didukung.
- Satu-satunya perintah yang dijalankan AIT adalah **perintah build** project (tabel di
  [Verifikasi](#5-verifikasi-build-dan-reviewer)), dengan program dan argumen tetap; model hanya memilih manifest di
  dalam root. Build menjalankan apa pun yang didefinisikan project itu sendiri (target MSBuild, script `build` di
  `package.json`, plugin Maven), jadi matikan *Build & verifikasi otomatis* untuk project yang tidak Anda percaya.
  Tidak ada perintah shell lain (test, install paket, migrasi). Pengecualiannya `git clone --depth 1` saat menarik
  sumber skill dari host git non-GitHub/GitLab (atau bila unduhan ZIP gagal). Clone ini memakai URL yang Anda tempel,
  tanpa prompt kredensial, dan folder `.git`-nya dibuang.
- **Auto Testing** menjalankan browser lewat Playwright (Edge/Chrome/Chromium dengan proses driver `node.exe` yang ikut
  terpasang bersama AIT) dan request HTTP — keduanya hanya ke alamat mesin ini (`localhost`, `127.0.0.1`, `[::1]`);
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
