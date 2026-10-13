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
- **sesi berasal dari sebuah project**, yaitu folder dengan `.git` atau file build (lihat bagian 2).

Selain itu (judul sesi, `/compact`, cek kuota, sesi di luar project, dll.) request diteruskan apa adanya ke
endpoint 9router yang sama dengan **model passthrough** — AIT tidak membuat thread dan tidak memanggil planner. `POST /v1/messages/count_tokens` juga diteruskan; bila gagal, jumlah token
diperkirakan (±4 karakter per token).

Jika *Proxy API key* diisi, request tanpa key yang cocok (`Authorization: Bearer` atau `x-api-key`) ditolak `401`.

## 2. Cek sesi: project atau bukan

Sebelum planner dipanggil, AIT memeriksa **sesi** asal prompt:

1. Id sesi diambil dari header `X-AiThread-Session` / `x-claude-code-session-id` / `x-session-id`, atau dari
   `metadata.user_id`; working directory dari header `X-AiThread-Cwd`, atau dari baris `Primary working directory:` /
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

   Header `X-AiThread-Project` (folder root) menjadi titik awal pencarian menggantikan working directory (bila tidak
   menghasilkan project, working directory dipakai); header lama `X-AiThread-Solution` (path `.sln`, foldernya yang
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

1. info project: nama, root, stack, folder tempat sesi dibuka (bila di bawah root), daftar file solution/build yang
   ditemukan, dan perintah build yang bisa dijalankan AIT (lihat [Verifikasi](#5-verifikasi-build-dan-reviewer));
2. struktur folder 2 tingkat (maks 200 entri);
3. konteks yang disisipkan Claude Code (isi `CLAUDE.md`, reminder, file yang dilampirkan), maks 30.000 karakter;
4. **konteks sesi** dari Context Keeper bila chat ini sudah punya thread AIT sebelumnya (lihat
   [Konteks sesi](#konteks-sesi-context-keeper)), maks 12.000 karakter;
5. percakapan sebelumnya: tanpa konteks sesi, 12 pesan terakhir; dengan konteks sesi, hanya pesan setelah thread yang
   ringkasannya dipakai. Tiap pesan maks 1.500 karakter, total maks 12.000 karakter;
6. indeks memory (id + label, maks 25);
7. permintaan terbaru developer (tanpa `<system-reminder>`), maks 40.000 karakter.

Bila sebuah bagian melebihi batas, yang dibuang adalah bagian tengahnya; awal dan akhir teks tetap ada.

Lalu planner bekerja dalam putaran tool (maks *Langkah planner*). Semua tool **read-only**, kecuali `run_build` yang
menjalankan build project (tidak mengubah kode; hanya ditawarkan bila *Build & verifikasi otomatis* menyala dan project
punya perintah build). Path relatif dihitung dari
root project; tool baca (`list_directory`, `find_files`, `search_text`, `read_file`) juga menerima **path absolut di luar
root**, supaya planner bisa mengecek folder atau project lain ketika Anda memintanya (mis. "cek ke
`D:/project/otherproject`"). Di luar root hasilnya ditampilkan dengan path absolut:

| Tool | Batas |
|---|---|
| `list_directory(path, depth)` | depth 1–4, maks 300 entri |
| `find_files(pattern, path?)` | glob, mis. `**/*User*.cs`; maks 200 hasil; `path` = folder pencarian (default root; bila di luar root, pola relatif terhadap folder itu) |
| `search_text(query, glob?, regex?, path?)` | tidak peka huruf besar/kecil; file ≤ 1 MB; maks 80 hasil; `path` seperti `find_files` |
| `read_file(path, startLine?, endLine?)` | bernomor baris; maks 600 baris per panggilan; file ≤ 2 MB |
| `search_memory(query)` | maks 25 label |
| `read_memory(ids)` | deskripsi lengkap |
| `read_thread(id)` | catatan lengkap thread sebelumnya di project ini (permintaan, rencana, tugas, balasan) |
| `run_build(target?)` | build project, hasilnya status + setiap error `file(baris,kolom): kode: pesan` (maks 40). `target` = solution/project/manifest lain di dalam root. Dipakai bila Anda minta build atau memperbaiki error build |
| `ask_user(question, options?)` | menghentikan thread dengan satu pertanyaan ke developer (lihat [Pertanyaan dari planner](#pertanyaan-dari-planner)); hanya ditawarkan bila *Planner boleh bertanya* menyala dan prompt berasal dari sesi yang bisa menjawab |
| `submit_plan(plan)` | mengakhiri planning |

Folder `.git .vs .idea .nuget bin obj node_modules packages TestResults artifacts` dan `wwwroot/lib/` disembunyikan
dari semua tool. Beberapa tool dalam satu putaran dijalankan paralel. Daftar tool tidak berubah selama satu thread
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
2. Judul, aksi, dan path file target; instruksi; kontrak; ringkasan rencana (sebagai konteks saja).
3. Potongan referensi dari `context` (maks 250 baris per potongan, total maks 40.000 karakter).
4. Isi file target:
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
| .NET | `dotnet build <file> -v:q -p:GenerateFullPaths=true -p:CopyRetryCount=1 -p:NuGetAudit=false` | `.sln`/`.slnx` paling dangkal, kalau tidak ada `.csproj`/`.fsproj`/`.vbproj` paling dangkal |
| Node.js | `tsc --noEmit` (TypeScript lokal di `node_modules`), kalau tidak ada `npm run build` | `package.json` paling dangkal yang punya salah satunya |
| Go | `go build ./...` | `go.mod` |
| Rust | `cargo check --message-format short` | `Cargo.toml` |
| Java (Maven) | `mvnw`/`mvn -q -B compile` | `pom.xml` |

Stack lain (Python, PHP, Gradle, …) tidak punya perintah build; verifikasinya hanya lewat Reviewer bila ada.

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
   setiap error atau baris yang dikutip reviewer. Lalu putaran berikutnya dimulai.

   Pengecualian: error member ganda C# (`CS0111`, `CS0102`) antara file yang **diisi** thread ini (tugas `create`
   atau tujuan `extract`) dan file yang sudah ada sebelumnya berarti pemindahan member belum tuntas. Untuk itu dibuat
   tugas `extract` (tanpa model) dari file lama ke file baru, sehingga salinan di file lama yang dihapus — bukan
   salinan di file baru. Tugas perbaikan lain pada kedua file menunggu tugas ini, dan temuan reviewer untuk kedua
   file itu ditunda ke putaran berikutnya (reviewer memeriksa ulang setelah pemindahan).

Batas dan aturan:

- Maksimal *Putaran perbaikan* putaran (default 2). Nilai `0` = hanya verifikasi dan laporkan.
- Error build hanya diperbaiki otomatis di **file yang diubah thread ini**. Error di file lain (mungkin sudah ada
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

## 6. Wrapper

Wrapper menerima permintaan Anda, ringkasan rencana, kontrak, hasil setiap tugas termasuk tugas perbaikan (ringkasan,
error, maks 60 baris diff per tugas), dan ringkasan verifikasi per putaran, lalu menulis bagian jawaban yang perlu
penjelasan, dalam bahasa permintaan Anda: 1–3 kalimat tentang apa yang dikerjakan, saran untuk tugas gagal atau
masalah verifikasi yang tersisa, dan langkah lanjutan bila perlu. Fakta lainnya ditulis AIT sendiri dari hasil, bukan
oleh model: baris status (tugas, build, reviewer) dan daftar *Perlu perhatian* sebelum teks Wrapper, tabel *File
berubah* dan footer (durasi, token, memory, wiki, link thread) sesudahnya — lihat
[Membaca output di Claude Code](04-penggunaan.md#membaca-output-di-claude-code). Teks Wrapper di-stream ke Claude Code.

Jika tidak ada wrapper aktif atau wrapper gagal, Claude Code menampilkan ringkasan rencana sebagai pengganti teksnya;
yang disimpan sebagai jawaban thread (Dashboard, Telegram) adalah ringkasan otomatis (✅/❌/⏭️ per tugas, plus hasil
verifikasi).

Setelah itu ekstraksi memory dijalankan dan ditunggu paling lama 30 detik agar footer bisa menyebut jumlah memory
baru; lewat dari itu footer menulis *memory baru masih diproses* dan ekstraksi tetap selesai di latar belakang.

## 7. Setelah thread

- Status akhir: *Selesai* bila ada tugas yang berhasil (atau jawaban langsung), *Gagal* bila tidak ada. Thread yang
  berhenti pada pertanyaan planner berstatus *Menunggu jawaban* sampai dijawab, dibatalkan, atau kedaluwarsa.
- Waktu (termasuk verifikasi; waktu menunggu jawaban tidak dihitung), token per peran (planner beserta porsi cache-nya,
  executor termasuk tugas perbaikan, reviewer, wrapper), dan setiap tugas tersimpan di database dan tampil di Dashboard.
- Ekstraksi memory ditunggu sebelum footer jawaban (maks 30 detik, lalu lanjut di belakang) — lihat [Memory](06-memory.md).
- Thread yang *Selesai* menunggu dirangkum ke wiki bisnis project oleh penulis wiki (berkala, default tiap 6 jam) —
  lihat [Business memory](06-memory.md#business-memory-wiki-bisnis). Gambar yang dilampirkan di prompt disimpan
  sampai saat itu, agar screenshot bisa masuk ke halaman modulnya.
- Untuk thread dari Claude Code (juga yang gagal), Context Keeper memperbarui konteks sesi di belakang.

## Konteks sesi (Context Keeper)

Planner tidak membawa ingatan antar prompt: setiap prompt memulai planner baru. Supaya chat yang panjang tetap
nyambung tanpa mengirim seluruh percakapan, AIT menyimpan **ringkasan sesi** per thread:

1. Baris pertama setiap balasan orkestrasi adalah penanda ``**AIT** · `NamaProject` · thread `3f2a1b9c` `` (selalu
   dikirim, juga saat progres dimatikan). Penanda ini ikut tersimpan di chat Claude Code. Penanda lama
   ``**AiThread** · …`` dari balasan sebelum rename tetap dikenali.
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
3. **Gudang** ada di `%LOCALAPPDATA%\AiThread\skills\<id sumber>` (ubah lewat `AiThread:SkillsDirectory`). Isi `SKILL.md`
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
- Isi skill masuk ke system prompt model node. Tarik skill hanya dari sumber yang Anda percaya, karena skill bisa
  mengarahkan perilaku model.
- AIT menulis ke folder mana pun yang dikirim sebagai working directory. Biarkan aplikasi hanya mendengarkan
  `localhost`, dan isi *Proxy API key* bila mesin dapat diakses orang lain.
- API key 9router disimpan terenkripsi dengan ASP.NET Core Data Protection.
