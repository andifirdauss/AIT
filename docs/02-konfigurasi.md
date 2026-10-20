# 2. Konfigurasi

Konfigurasi runtime disimpan di database dan diubah dari UI — tidak perlu restart.

## Menu Setting

### Koneksi 9router

| Field | Keterangan |
|---|---|
| Base URL | Alamat 9router, default `http://localhost:20128`. Akhiran `/v1` (seperti yang ditampilkan dashboard 9router) dibuang otomatis. |
| API key | Key dari dashboard 9router. Disimpan **terenkripsi**; setelah disimpan hanya ditampilkan tersamar (mis. `sk-abc••••1234`). Kosongkan field untuk mempertahankan key lama. |
| Hapus API key tersimpan | Centang lalu *Simpan setting* untuk menghapus key. |

Tombol **Test koneksi** memakai nilai di form **tanpa menyimpannya**. Setelah hasilnya benar, klik **Simpan setting**
— hanya key yang tersimpan yang dipakai AIT. Saat halaman dibuka, baris hasil menampilkan
status konfigurasi yang tersimpan.

- `✓ Online · 12 ms · 48 model · 3 combo · API key OK` — siap dipakai.
- `… · key ini belum disimpan — klik Simpan` — key yang Anda ketik valid, tetapi belum tersimpan.
- `API key wajib, belum disimpan` — 9router mewajibkan key (*require API key* aktif) dan belum ada key tersimpan.
  Semua panggilan model akan gagal `401 Missing API key`.
- `API key ditolak` — key salah atau sudah dihapus di 9router.
- `tanpa API key (tidak diwajibkan)` — tidak ada key dan 9router tidak memintanya.
- `✕ Offline — …` — 9router tidak berjalan atau URL salah.

"Online" saja belum cukup: health check dan daftar model 9router tidak butuh key, jadi status key dicek terpisah
(dengan request kosong yang ditolak 9router sebelum model dipanggil — tanpa biaya token). Masalah key juga tampil
sebagai *Online · cek API key* (kuning) di Dashboard dan sebagai peringatan di log.

### Proxy & orkestrasi

| Field | Default | Rentang | Keterangan |
|---|---|---|---|
| Orkestrasi aktif | aktif | — | Jika dimatikan, semua request Claude Code diteruskan ke 9router dengan *model passthrough* — Claude Code bekerja sebagai agen biasa. |
| Tampilkan progres di Claude Code | aktif | — | Menampilkan langkah planner, rencana, dan status tiap tugas sebelum ringkasan akhir. Jika mati, hanya ringkasan yang tampil. |
| Planner boleh bertanya | aktif | — | Planner boleh menghentikan thread dengan satu pertanyaan bernomor bila permintaan benar-benar ambigu, perubahannya berisiko dan tidak diminta, atau ada fakta yang tidak bisa ditemukan di kode/memory. Bukan konfirmasi tiap prompt. Lihat [Pertanyaan dari planner](07-cara-kerja.md#pertanyaan-dari-planner). |
| Model passthrough | otomatis | — | Model untuk request pendukung (judul sesi, `/compact`, cek kuota). *Otomatis* = model node Wrapper aktif, lalu Executor. |
| Proxy API key | kosong | — | Jika diisi, Claude Code wajib mengirimnya sebagai `ANTHROPIC_AUTH_TOKEN`. Tombol *Generate* membuat key acak. |
| Langkah planner | 24 | 3–100 | Maksimal putaran tool planner. Di langkah terakhir planner dipaksa mengirim rencana. |
| Retry executor | 1 | 0–5 | Berapa kali executor boleh mencoba ulang bila editnya gagal diterapkan. |
| Maks paralel | 8 | 1–64 | Batas tugas yang berjalan bersamaan **dalam satu thread**. |
| Timeout (detik) | 600 | 30–3600 | Batas waktu satu panggilan model ke 9router. |

### Build & verifikasi

| Field | Default | Rentang | Keterangan |
|---|---|---|---|
| Build & verifikasi otomatis | aktif | — | AIT boleh menjalankan perintah build project (`dotnet build --no-incremental`, MSBuild Visual Studio untuk project .NET Framework format lama, `tsc`/`npm run build`, `go build`, `cargo check`, `mvn compile`): sebagai tool `run_build` planner dan untuk memeriksa hasil executor. Error build diserahkan ke executor sebagai tugas perbaikan. Lihat [Verifikasi](07-cara-kerja.md#5-verifikasi-build-dan-reviewer). |
| Timeout build (detik) | 300 | 30–3600 | Batas satu kali build; build yang melewatinya dihentikan beserta proses anaknya. |
| Putaran perbaikan | 2 | 0–5 | Maksimal putaran build/review → tugas perbaikan. `0` = hanya verifikasi dan laporkan. |

Tab *Koneksi 9router*, *Proxy & orkestrasi*, dan *Build & verifikasi* adalah satu form: **Simpan setting** di tab
mana pun menyimpan ketiganya. Setelah mengganti proxy API key, terapkan ulang integrasi Claude Code agar
`ANTHROPIC_AUTH_TOKEN` ikut berubah.

### Penyimpanan memory

Tab `/Setting#memory`: satu kartu masing-masing untuk short memory, long memory, dan business memory (wiki bisnis),
disimpan lewat tombol sendiri **Simpan penyimpanan memory**. Perubahan langsung berlaku tanpa restart. Untuk tim,
arahkan **Long** dan **Business memory** semua developer ke database yang sama: memory dibagi per
[project key](06-memory.md#berbagi-memory-antar-developer-project-key), bukan per folder root.

| Field | Default | Keterangan |
|---|---|---|
| Provider | Bawaan | Short: Garnet bawaan, Redis, SQL Server, PostgreSQL, MySQL, Oracle, SQLite. Long & business: database utama, SQL Server, PostgreSQL, MySQL, Oracle, SQLite. |
| Host / Port | kosong | Server database/Redis. Port kosong = port default provider. |
| Database | kosong | Nama database; service name untuk Oracle; indeks untuk Redis; path file untuk SQLite. |
| Username / Password | kosong | Password disimpan **terenkripsi**; kosongkan untuk mempertahankan password lama, atau centang *Hapus password tersimpan*. |
| Opsi tambahan | kosong | Keyword connection string tambahan (mis. `SSL Mode=Require`). |
| Salin memory yang ada | aktif | Menyalin memory dari store lama ke store baru saat provider/server diganti. Store lama tidak diubah. |

Store yang tidak bisa dihubungi ditolak saat disimpan. Rincian per provider di [Memory](06-memory.md#penyimpanan-memory).

### Business memory

Kartu di bawah penyimpanan memory (`/Setting#memory`), disimpan lewat **Simpan business memory**. Lihat
[Business memory](06-memory.md#business-memory-wiki-bisnis).

| Field | Default | Keterangan |
|---|---|---|
| Perbarui wiki bisnis otomatis | aktif | Worker merangkum thread yang selesai menjadi halaman wiki bisnis per modul. Tombol *Perbarui sekarang* di menu Memory → Bisnis tetap bisa dipakai saat nonaktif. |
| Interval (jam) | 6 | 1–168. Jeda antar rangkuman otomatis. |
| Model penulis wiki | otomatis | Model 9router penulis wiki; kosong = model node Planner aktif. Pilih model yang teliti dan bisa membaca gambar agar screenshot dari prompt ikut masuk halaman. |
| Nama developer | `git config user.name` | Dicatat pada halaman dan riwayat revisi, supaya tim tahu siapa (AIT siapa) yang mengubah. Kosong = `user.name` dari git config global (dibaca langsung dari `~/.gitconfig`); bila git juga tidak punya, AIT menanyakannya sekali lewat popup saat UI pertama dibuka. |

### Telegram

Tab terakhir halaman Setting (`/Setting#telegram`), untuk mengirim prompt dari HP — panduan lengkap di
[Prompt dari Telegram](11-telegram.md).

| Field | Default | Keterangan |
|---|---|---|
| Aktifkan bot Telegram | mati | Menyalakan long polling ke Telegram. Butuh token tersimpan. |
| Bot token | kosong | Token dari `@BotFather`. Diverifikasi ke Telegram (`getMe`) saat disimpan, lalu disimpan **terenkripsi** dan hanya ditampilkan tersamar. Kosongkan field untuk mempertahankan token lama. |
| Hapus token tersimpan | — | Centang lalu *Simpan Telegram* untuk menghapus token (bot otomatis berhenti). |
| Chat | — | Chat pribadi yang pernah mengirim pesan ke bot. **Izinkan** chat Anda sendiri; **Hapus** chat asing; **Cabut** menghentikan akses chat yang sudah diizinkan. |

Tombol **Simpan Telegram** hanya menyimpan bagian ini (terpisah dari *Simpan setting*) dan langsung berlaku tanpa
restart. Progres di Telegram selalu tampil, tidak dipengaruhi saklar *Tampilkan progres di Claude Code*.

### Skill

Tab `/Setting#skills` berisi Skill Orchestrator dan gudang skill offline. Cara kerjanya dijelaskan di
[Skill pada node](07-cara-kerja.md#skill-pada-node).

| Field | Default | Keterangan |
|---|---|---|
| Model Skill Orchestrator | otomatis | Model 9router yang memeriksa isi link saat skill ditarik dan yang mencarikan skill di modal Node. *Otomatis* = model node Planner aktif dengan prioritas terkecil. |
| Batas karakter skill per node | 40000 | 4000–200000. Total isi skill yang ditempel ke system prompt satu node, dibagi rata antar skill. |
| Sumber skill | — | Tempel link lalu klik **Tarik skill**. Formatnya: repository GitHub/GitLab (boleh `/tree/<branch>/<folder>`), `owner/repo`, URL `.git`, file `.md`, atau halaman web. Status: *Antre* → *Mengunduh…* → *Dianalisis AI…* → *Siap*/*Gagal*. **Sync** menarik ulang; **Hapus** membuang sumber, skill-nya, dan foldernya, serta melepas skill itu dari node. |
| Gudang skill | — | Daftar skill dengan pencarian (nama, deskripsi, tag, sumber), jumlah node pemakai, dan tombol **Lihat** untuk isi serta file pendukungnya. |

**Simpan Skill** hanya menyimpan model dan batas karakter. Sumber diproses satu per satu di latar belakang, dan status
berubah langsung tanpa refresh. Folder gudang: `%LOCALAPPDATA%\AiThread\skills`, bisa diubah lewat
`AiThread:SkillsDirectory` di `appsettings.json`.

## Menu Node

Pipeline butuh minimal **1 Planner, 1 Executor, 1 Wrapper** yang aktif. Banner di atas halaman menunjukkan
kesiapan pipeline beserta catatannya. Node **Reviewer** (lihat
[Verifikasi](07-cara-kerja.md#5-verifikasi-build-dan-reviewer)) dan **Context Keeper** (lihat
[Konteks sesi](07-cara-kerja.md#konteks-sesi-context-keeper)) opsional, begitu juga **Tools Node** (lihat
[Tools node planner](#tools-node-planner)).

### Field node

| Field | Default (UI) | Keterangan |
|---|---|---|
| Nama | — | Label bebas, tampil di progres dan detail thread. |
| Role | Executor | Planner / Tools Node / Executor / Reviewer / Wrapper / Context Keeper. |
| Model / combo | — | Dipilih dari `/v1/models` 9router, dikelompokkan: *Combo* dulu, lalu per provider. Jika 9router offline, muncul input teks untuk mengetik id model manual. |
| Temperature | Planner 0.2 · Tools Node 0.1 · Executor 0.1 · Reviewer 0.1 · Wrapper 0.3 · Context Keeper 0.2 | 0–2. Kosongkan untuk nilai default yang sama. |
| Max tokens | Planner 16384 · Tools Node 4096 · Executor 8192 · Reviewer 4096 · Wrapper 4096 · Context Keeper 4096 | 256–200000. Batas output per panggilan. |
| Max paralel | Planner 2 · Tools Node 4 · Executor 6 · Reviewer 2 · Wrapper 4 · Context Keeper 2 | 1–64. Berapa request ke model node ini yang boleh berjalan bersamaan, dihitung **di semua thread** (berguna untuk rate limit provider). Bukan jumlah proses/instance. |
| Prioritas | 100 | Angka kecil diutamakan. |
| Node cadangan | tidak ada | Node lain dengan **role yang sama** yang mengambil alih bila node ini tetap gagal setelah 9router mencoba ulang. Lihat [Node cadangan](#node-cadangan). |
| Instruksi tambahan | kosong | Ditambahkan di akhir prompt bawaan peran, mis. konvensi coding tim. |
| Skill | kosong | Skill dari gudang (maks 10). Cari sendiri lewat dropdown *Cari sendiri di gudang* (cocok dengan nama, deskripsi, tag, atau isi skill; klik untuk memasang/melepas), atau tulis kebutuhan di *Butuh kemampuan apa?* lalu klik **Cari skill**: Skill Orchestrator menyarankan skill yang cocok beserta alasannya, Anda centang, lalu **Terapkan** dan **Simpan**. Isi skill ditempel ke system prompt node setelah instruksi tambahan; field *Instruksi tambahan* tidak berubah. |
| Delegasi tool | semua di planner | Khusus Planner: per tool baca, pilih *Planner langsung* atau sebuah Tools Node. Lihat [Tools node planner](#tools-node-planner). |
| Aktif | aktif | Node nonaktif tidak dipakai. |

Tombol **Test** mengirim prompt singkat ("Reply with exactly one word: pong") dan menampilkan latency, token, dan
balasan — cara cepat memastikan model dan key benar.

### Aturan pemilihan node

- **Planner**: node aktif dengan prioritas terkecil yang dipakai. Bila ia kena rate limit, yang mengambil alih adalah
  [node cadangan](#node-cadangan) yang dipilih untuknya, bukan otomatis planner aktif berikutnya.
- **Wrapper**: hanya satu yang dipakai — node aktif dengan prioritas terkecil.
- **Executor**: semua node aktif dipakai. Tiap tugas diberikan ke executor dengan slot kosong terbanyak, lalu
  prioritas terkecil, lalu bergiliran.
- **Wrapper** juga dipakai untuk ekstraksi memory, dan menjadi model passthrough bila *Model passthrough* diset
  otomatis.
- **Context Keeper**: node aktif dengan prioritas terkecil meringkas percakapan Claude Code setelah tiap thread.
  Tanpa node ini, Wrapper yang meringkas.
- **Tools Node**: tidak dipilih lewat prioritas; dipakai hanya oleh planner yang mendelegasikan tool kepadanya.
- **Reviewer**: node aktif dengan prioritas terkecil memeriksa hasil executor (diff, kontrak, hasil build) di setiap
  putaran verifikasi. Tanpa node ini, verifikasi hanya memakai build. Model yang teliti lebih penting daripada model
  yang cepat: setiap temuannya memicu tugas perbaikan.

### Node cadangan

Setiap node bisa diberi satu **node cadangan** di form node (opsional). Alurnya:

1. Panggilan ke node gagal dengan error yang berarti "model sedang tidak tersedia": rate limit **429**, timeout
   **408**, atau error server **5xx**.
2. 9router lebih dulu mencoba ulang dengan jeda (seperti sebelumnya).
3. Bila tetap gagal, panggilan yang **sama** dikirim ke node cadangan. Untuk planner dan executor, percakapan
   dilanjutkan dari titik terakhir, tidak diulang dari awal. Bila node cadangan juga gagal dan punya cadangan sendiri,
   rantainya diikuti terus. Setiap node paling banyak dicoba sekali per panggilan.
4. Peralihan tercatat di aktivitas dan progres, mis. *Executor T2 `model-a` tidak tersedia (rate limit 429), beralih
   ke `model-b`…*. Kolom executor di tabel tugas menampilkan node yang benar-benar mengerjakan.

Aturannya:

- Node cadangan harus ber-role sama, tidak boleh node itu sendiri, dan rantainya tidak boleh berputar (A → B → A
  ditolak saat menyimpan).
- Node cadangan **boleh nonaktif**. Node nonaktif tidak menerima pekerjaan biasa, sehingga cocok sebagai cadangan murni
  yang hanya dipakai saat dibutuhkan.
- Error lain (mis. 400 request tidak valid, 401 key salah) tidak memicu peralihan, karena node lain kemungkinan besar
  akan gagal dengan cara yang sama.
- Berlaku untuk semua peran: Planner, Executor, Reviewer, Wrapper, Context Keeper, serta ekstraksi memory (memakai
  node Wrapper).
- Menghapus node, atau mengubah role-nya, otomatis melepasnya dari node lain yang memakainya sebagai cadangan.
- Sebelum fitur ini ada, planner aktif otomatis menjadi cadangan satu sama lain. Saat update, migrasi mengisi node
  cadangan planner yang sudah ada sesuai urutan prioritas, jadi perilakunya tetap sama dan sekarang bisa diubah.

### Tools node planner

Output tool planner (isi file dari `read_file`, hasil `search_text`/`find_references`, dst.) masuk ke percakapan
planner dan **dikirim ulang ke model planner di setiap langkah berikutnya**. Satu `read_file` 600 baris di langkah 3
ikut terbayar lagi di langkah 4, 5, … sampai `submit_plan`. Tools node memindahkan pekerjaan itu ke model murah:

1. Buat node ber-role **Tools Node** dengan model murah yang **mendukung tool calling**.
2. Edit node Planner → bagian **Delegasi tool ke tools node**: untuk tiap tool, pilih *Planner langsung* atau tools node.
3. Tool yang didelegasikan **dihapus** dari daftar tool planner. Sebagai gantinya planner mendapat tool
   `investigate(question)` (plus `agent` bila ada lebih dari satu tools node). Tools node menjalankan tool-nya sendiri dalam
   loop (maks 12 langkah), lalu mengembalikan **temuan ringkas** — `path:baris` dan kutipan pendek bila perlu, maks
   12.000 karakter. Output mentah tidak pernah masuk ke konteks planner.

| Tool | Saran |
|---|---|
| `read_file`, `search_text`, `find_references` | Delegasikan — output terbesar, paling boros di planner. |
| `list_directory`, `find_files`, `read_thread`, `read_memory` | Opsional; delegasikan bila sering panjang di project Anda. |
| `find_symbol`, `search_memory` | Biarkan di planner — hasilnya pendek, panggilan `investigate` justru lebih mahal. |
| `run_build` | Biarkan di planner kecuali Anda ingin daftar error dirangkum tools node. |

`ask_user` dan `submit_plan` tidak bisa didelegasikan. `code_outline` dan `function_calls` juga tidak: hasilnya
pendek, jadi selalu ada di planner supaya ia melihat kerangka kode lebih dulu dan bertanya ke tools node dengan file,
member, dan rentang baris yang tepat. Tools node yang memegang `read_file` otomatis ikut mendapat kedua tool ini.

Aturannya:

- Satu tool hanya ke satu tools node per planner; satu planner boleh memakai beberapa tools node (mis. satu untuk baca
  kode, satu untuk memory). Beberapa `investigate` dalam satu langkah berjalan paralel, dibatasi *Max paralel* tools node.
- Tools node **nonaktif** = tool-nya kembali dijalankan planner sendiri (banner node memberi catatan). Cara cepat
  membandingkan hasil dengan dan tanpa tools node.
- Tools node memakai [node cadangan](#node-cadangan) ber-role Tools Node bila kena rate limit. Bila tetap gagal, planner
  menerima error dan melanjutkan dengan yang sudah ia ketahui.
- Menghapus tools node atau mengubah role-nya melepas semua delegasi ke node itu; mengubah role planner menghapus
  delegasinya.
- Trade-off: planner hanya melihat apa yang dilaporkan tools node. Pakai model tools node yang teliti menyalin nama dan
  nomor baris; model yang terlalu lemah membuat planner bertanya ulang (lebih banyak langkah).
- Token tools node dicatat terpisah (*Token tools node* di detail thread) dan ikut dalam total token thread.

### Cara kerja *Max paralel*

Setiap panggilan model ke sebuah node memakai satu slot; bila semua slot terpakai, panggilan berikutnya menunggu.
Slot dibagi di antara semua thread yang sedang berjalan (beberapa project sekaligus).

| Peran | Yang memakai slot |
|---|---|
| Executor | Satu tugas selama dikerjakan, termasuk retry. Executor dengan 6 slot mengerjakan maksimal 6 tugas serentak. |
| Planner | Satu slot per panggilan model; slot dilepas saat planner menjalankan tool baca-kode. Dengan 2 slot, maksimal 2 thread menunggu jawaban planner bersamaan. |
| Wrapper | Satu slot untuk ringkasan akhir dan satu untuk ekstraksi memory tiap thread. |
| Context Keeper | Satu slot per pembaruan konteks sesi (sekali per thread Claude Code, di belakang). |
| Tools Node | Satu slot per panggilan model dalam sebuah `investigate`; dilepas saat tools node menjalankan tool. |

Selain itu, Setting → *Maks paralel* membatasi tugas serentak **per thread**. Jumlah tugas yang benar-benar berjalan
bersamaan = nilai terkecil dari: tugas yang siap, *Maks paralel* per thread, dan total slot kosong semua executor.
Panggilan passthrough (request pendukung Claude Code) dan tombol *Test* tidak memakai slot.

### Biaya token planner

Planner bekerja lewat 9router (`/v1/chat/completions`, format OpenAI), dan setiap langkah mengirim ulang seluruh
percakapan. Bagian yang sama dengan langkah sebelumnya dilayani dari **prompt cache** provider, yang jauh lebih murah
daripada input baru:

- Model Claude (termasuk `cc/…`): 9router memasang `cache_control` sendiri di tools, system prompt, dan pesan
  assistant terakhir.
- OpenAI, DeepSeek, Gemini, dan sejenisnya: caching prefix otomatis di sisi provider.

AIT menjaga awal percakapan tetap sama agar cache terus cocok: daftar tool planner tidak berubah selama satu
thread, brief disusun dari bagian paling stabil (project, struktur) ke permintaan di akhir, dan pesan executor
diawali kontrak + ringkasan rencana yang sama untuk semua tugas. Tiap request juga membawa `prompt_cache_key`
`ait:<thread>:planner|tools|executor` agar provider (OpenAI/Codex) mengarahkan request yang awalnya sama ke cache yang
sama; model yang provider-nya menolak field itu otomatis dipanggil tanpa kunci (sekali tercatat di log). Detail thread
menampilkan porsinya, mis. *Token planner 120.000 (85% cache)*. Persentase 0 berarti provider tidak melaporkan cache
atau tidak memakainya.

Untuk menghemat lebih jauh:

- Turunkan Setting → *Langkah planner*.
- Hindari model langganan `cc/…` (Claude via 9router) untuk planner; pakai model/combo yang kuotanya longgar.
- Tambahkan node Planner kedua dan pilih sebagai [node cadangan](#node-cadangan) planner utama.
- Delegasikan tool yang outputnya besar ke [tools node](#tools-node-planner) bermodel murah.

### Memilih model

| Peran | Kebutuhan | Saran |
|---|---|---|
| Planner | Paling cerdas, konteks besar, **wajib mendukung tool calling** (function calling). | Model kelas teratas yang Anda punya di 9router. Kualitas rencana & kontrak menentukan akurasi semua executor. |
| Executor | Murah, cepat, patuh format. | Model murah/gratis. Tambahkan beberapa node dari provider berbeda agar beban dan rate limit tersebar; combo memberi fallback otomatis. |
| Wrapper | Murah, bisa merangkum. | Model murah yang sama dengan executor cukup. |
| Tools Node | Murah, **wajib mendukung tool calling**, teliti menyalin path, nomor baris, dan identifier. | Model murah yang kuat di kode; konteksnya cukup untuk beberapa file sekaligus. |
| Context Keeper | Murah, teliti menyalin nama file & identifier, konteks cukup untuk ±15 catatan thread. | Model murah yang rapi merangkum; tidak perlu tool calling. Jangan pakai model langganan Claude agar tidak memakai kuota. |

Tips:

- *Max tokens* executor harus cukup untuk seluruh output edit. Tugas `create` menulis file utuh — naikkan bila
  sering muncul "Output was cut off (max tokens)".
- Jika planner sering gagal membuat rencana valid, naikkan *Langkah planner* atau ganti ke model yang lebih kuat.
- Model planner yang tidak mendukung tool calling tetap dicoba: AIT membaca rencana JSON dari teks, dan bila
  tetap berupa teks biasa setelah dua kali diingatkan, teks itu dipakai sebagai jawaban langsung.

Contoh susunan (id model hanya ilustrasi — pilih dari dropdown sesuai provider Anda):

| Nama | Role | Model | Max paralel | Prioritas |
|---|---|---|---|---|
| Planner utama | Planner | model terkuat, mis. dari provider Claude Code | 2 | 1 |
| Executor A | Executor | combo murah | 6 | 10 |
| Executor B | Executor | model murah dari provider lain | 4 | 10 |
| Wrapper | Wrapper | model murah | 4 | 1 |

Lanjut ke [Integrasi Claude Code](03-integrasi-claude-code.md).
