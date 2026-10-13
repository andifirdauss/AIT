# 6. Memory

Memory membuat planner makin paham konteks bisnis dan teknis project dari waktu ke waktu, tanpa Anda mengulang
penjelasan.

## Isi satu memory

| Bagian | Fungsi |
|---|---|
| **Label** | 3–8 keyword dipisah koma, mis. `user, pengguna, description, deskripsi, form, validation`. Planner memindai label **tanpa** membaca deskripsi, lalu hanya membuka yang relevan. |
| **Deskripsi** | Penjelasan mandiri 2–6 kalimat. Untuk project memory berisi path file dan identifier. |

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
  (`%LOCALAPPDATA%\AiThread\garnet\<port>`) sebelum dikonfirmasi.
- **Short memory tidak tersedia** (mis. Garnet gagal start, atau server short memory eksternal mati): memory baru
  langsung disimpan sebagai long memory (tercatat di log sebagai peringatan). Store database eksternal dicek ulang
  tiap 15 detik dan otomatis dipakai lagi begitu kembali hidup.
- Short memory dari project yang sudah dihapus berubah menjadi knowledge memory saat dipindah.

## Berbagi memory antar developer (project key)

Folder root project berbeda di tiap mesin (`D:\a\eep` di laptop Andi, `C:\src\eep` di laptop Budi), jadi memory
tidak diikat ke folder, melainkan ke **project key** yang sama untuk semua clone sebuah repository:

1. file `.aithread/project.json` di root repository, mis. `{ "key": "eep" }` — di-commit tim, selalu menang;
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
| SQLite | ✓ | ✓ | ✓ | database = path file (kosong = `%LOCALAPPDATA%\AiThread\memory.db`) |

- **Opsi tambahan** ditambahkan ke connection string dan menimpa field di atas, mis. `SSL Mode=Require` (PostgreSQL),
  `Encrypt=True` (SQL Server), atau `ssl=true` (Redis, dipisah koma). Keyword yang tidak dikenal ditolak saat disimpan.
- **Test koneksi** memakai isi form tanpa menyimpan. Password kosong = memakai password tersimpan.
- Saat **disimpan**, AIT menghubungi store baru dan membuat database (bila belum ada dan user punya hak) serta
  tabel `ShortMemories`/`LongMemories`, atau `BusinessPages`/`BusinessPageRevisions`/`BusinessAssets` untuk business
  memory. Store yang tidak bisa dihubungi ditolak, jadi setting lama tetap berlaku. Database eksternal tidak memakai
  migrasi EF; tabel dibuat sendiri pada pemakaian pertama, dan tabel dari versi lama otomatis mendapat kolom baru
  (`ProjectKey`, `ProjectName`) — user perlu hak `ALTER TABLE` sekali itu.
- **Salin memory yang ada** (default aktif) menyalin memory dari store lama ke store baru berdasarkan id, jadi aman
  diulang dan tidak menggandakan. Store lama tidak dihapus atau diubah. Tanpa salinan, memory lama tidak terlihat
  sampai provider dikembalikan.
- Password disimpan terenkripsi (ASP.NET Core Data Protection) dan tidak pernah dikirim balik ke browser.
- Short, long, dan business memory boleh memakai database yang sama; masing-masing punya tabel sendiri.
- Long memory di database eksternal tidak punya foreign key ke `Projects`: memory diikat ke project key dan menyimpan
  nama project-nya sendiri. Lihat [Berbagi memory antar developer](#berbagi-memory-antar-developer-project-key) untuk
  apa yang terjadi saat project dihapus.
- Menyalin business memory ke store yang sudah berisi wiki developer lain melewati halaman yang judulnya sudah ada di
  sana (dilaporkan sebagai peringatan), agar tidak ada halaman ganda.

## Kapan memory dibuat

Setelah thread selesai (minimal satu tugas berhasil, atau planner menjawab langsung), model **Wrapper** membaca
permintaan, rencana, kontrak, hasil tugas, dan ringkasan, lalu menulis **maksimal 5** memory baru. Label yang sudah
ada ikut dikirim agar tidak terjadi duplikasi. Karena hanya sesi di dalam sebuah project (folder dengan `.git` atau file build) yang diorkestrasi,
setiap thread selalu punya project; bila project dihapus saat thread berjalan, hanya knowledge memory yang disimpan.
Proses ini berjalan di belakang dan tidak menunda balasan ke Claude Code.

## Cara planner memakai memory

1. Brief planner berisi **indeks memory** (hanya id + label): memory yang label-nya cocok dengan keyword prompt,
   ditambah short memory terbaru — maksimal 25 baris. Halaman wiki bisnis project ikut masuk indeks (tipe
   `business`, label = `Modul — Judul (business wiki): label halaman`) dengan jatah sendiri (maks 8), supaya tidak
   tergeser oleh banyaknya memory lain.
2. Planner memanggil `read_memory` untuk membaca deskripsi yang relevan, dan `search_memory` untuk topik lain. Untuk
   halaman bisnis, `read_memory` mengembalikan isi halaman (gambar diganti keterangannya, maks 15.000 karakter);
   planner diminta membaca halaman modul sebelum mengubah perilakunya dan mengikuti aturannya.
3. Setiap kali long memory dibaca, kolom *Dipakai* bertambah — penanda memory yang bernilai.

Pencocokan memakai keyword (huruf kecil, minimal 3 karakter, kata umum seperti "yang", "tambah", "the" diabaikan).
Karena itu label sebaiknya memuat istilah bahasa Inggris **dan** istilah yang Anda pakai sehari-hari.

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
  tim. Masuk sebagai short memory, lalu otomatis pindah ke long memory.
- **Buka**: ubah tipe, project, label, deskripsi. Perbaiki memory yang kurang tepat — planner mempercayainya.
- **→ DB**: pindahkan short memory ke long memory sekarang.
- **Hapus**: hapus memory yang salah atau usang.
- **Konsolidasi sekarang**: jalankan pemindahan short → long tanpa menunggu worker.
- Tab **Bisnis (wiki)**: halaman wiki bisnis per project — lihat [Business memory](#business-memory-wiki-bisnis).

Tips menulis memory manual:

- Label spesifik: `invoice, faktur, tax, pajak, rounding` lebih berguna daripada `bisnis`.
- Deskripsi menyebut lokasi: "Perhitungan pajak ada di `InvoiceService.CalculateTax`; pembulatan ke atas per baris."
- Satu topik per memory.

## Konfigurasi

| Kunci `appsettings.json` | Default |
|---|---|
| `AiThread:ShortMemoryHours` | 24 |
| `AiThread:MemoryConsolidationIntervalMinutes` | 10 |
| `ConnectionStrings:Redis` | `localhost:6379` (port Garnet; lihat [Instalasi](01-instalasi.md#1-short-memory-garnet)) |
| `AiThread:EmbeddedRedis:Enabled` | `true` |

Dua kunci terakhir hanya dipakai saat short memory memakai provider *Bawaan*.

Business memory diatur di menu Setting (aktif, interval, model, nama developer) — lihat
[Konfigurasi](02-konfigurasi.md#business-memory).

Perkiraan "±N jam lagi ke long memory" di tabel Memory selalu dihitung dari 24 jam.
