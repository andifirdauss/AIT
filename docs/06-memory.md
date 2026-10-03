# 6. Memory

Memory membuat planner makin paham konteks bisnis dan teknis project dari waktu ke waktu, tanpa Anda mengulang
penjelasan.

## Isi satu memory

| Bagian | Fungsi |
|---|---|
| **Label** | 3–8 keyword dipisah koma, mis. `user, pengguna, description, deskripsi, form, validation`. Planner memindai label **tanpa** membaca deskripsi, lalu hanya membuka yang relevan. |
| **Deskripsi** | Penjelasan mandiri 2–6 kalimat. Untuk project memory berisi path file dan identifier. |

## Dua tipe

| Tipe | Isi | Cakupan |
|---|---|---|
| **Project** | Aturan bisnis, validasi, alur data, arsitektur, konvensi, lokasi kode. | Hanya project asalnya. |
| **Knowledge** | Pola teknis umum yang berguna di project lain, mis. "cache data master di Redis, refresh tiap 5 menit". | Semua project. |

## Dua level

| Level | Penyimpanan | Umur |
|---|---|---|
| **Short** | Default: Garnet di dalam AiThread (protokol Redis, tanpa TTL). Bisa diganti — lihat [Penyimpanan memory](#penyimpanan-memory). | Sampai berumur `ShortMemoryHours` (default 24 jam). |
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
- Short memory bertahan saat AiThread di-restart atau crash: Garnet mencatat setiap penulisan ke disk
  (`%LOCALAPPDATA%\AiThread\garnet\<port>`) sebelum dikonfirmasi.
- **Short memory tidak tersedia** (mis. Garnet gagal start, atau server short memory eksternal mati): memory baru
  langsung disimpan sebagai long memory (tercatat di log sebagai peringatan). Store database eksternal dicek ulang
  tiap 15 detik dan otomatis dipakai lagi begitu kembali hidup.
- Short memory dari project yang sudah dihapus berubah menjadi knowledge memory saat dipindah.

## Penyimpanan memory

Menu **Setting → Penyimpanan memory** (`/Setting#memory`) menentukan di mana tiap level disimpan. Perubahan langsung
berlaku tanpa restart.

| Provider | Short | Long | Field yang dipakai |
|---|---|---|---|
| Bawaan | Garnet di dalam AiThread (`ConnectionStrings:Redis`) | Database utama (`ConnectionStrings:Default`) | — |
| Redis | ✓ | — | host, port (6379), database = indeks (opsional), username (ACL Redis 6+), password |
| SQL Server | ✓ | ✓ | host, port (1433), database, username/password — username kosong = Windows authentication |
| PostgreSQL | ✓ | ✓ | host, port (5432), database, username, password |
| MySQL | ✓ | ✓ | host, port (3306), database, username, password |
| Oracle | ✓ | ✓ | host, port (1521), database = service name (mis. `FREEPDB1`), username, password |
| SQLite | ✓ | ✓ | database = path file (kosong = `%LOCALAPPDATA%\AiThread\memory.db`) |

- **Opsi tambahan** ditambahkan ke connection string dan menimpa field di atas, mis. `SSL Mode=Require` (PostgreSQL),
  `Encrypt=True` (SQL Server), atau `ssl=true` (Redis, dipisah koma). Keyword yang tidak dikenal ditolak saat disimpan.
- **Test koneksi** memakai isi form tanpa menyimpan. Password kosong = memakai password tersimpan.
- Saat **disimpan**, AiThread menghubungi store baru dan membuat database (bila belum ada dan user punya hak) serta
  tabel `ShortMemories`/`LongMemories`. Store yang tidak bisa dihubungi ditolak, jadi setting lama tetap berlaku.
  Database eksternal tidak memakai migrasi EF; tabel dibuat sendiri pada pemakaian pertama.
- **Salin memory yang ada** (default aktif) menyalin memory dari store lama ke store baru berdasarkan id, jadi aman
  diulang dan tidak menggandakan. Store lama tidak dihapus atau diubah. Tanpa salinan, memory lama tidak terlihat
  sampai provider dikembalikan.
- Password disimpan terenkripsi (ASP.NET Core Data Protection) dan tidak pernah dikirim balik ke browser.
- Short dan long memory boleh memakai database yang sama; masing-masing punya tabel sendiri.
- Long memory di database eksternal tidak punya foreign key ke `Projects`: nama project diambil dari database utama,
  dan memory sebuah project dihapus oleh AiThread saat project dihapus.

## Kapan memory dibuat

Setelah thread selesai (minimal satu tugas berhasil, atau planner menjawab langsung), model **Wrapper** membaca
permintaan, rencana, kontrak, hasil tugas, dan ringkasan, lalu menulis **maksimal 5** memory baru. Label yang sudah
ada ikut dikirim agar tidak terjadi duplikasi. Karena hanya sesi di dalam sebuah project (folder dengan `.git` atau file build) yang diorkestrasi,
setiap thread selalu punya project; bila project dihapus saat thread berjalan, hanya knowledge memory yang disimpan.
Proses ini berjalan di belakang dan tidak menunda balasan ke Claude Code.

## Cara planner memakai memory

1. Brief planner berisi **indeks memory** (hanya id + label): memory yang label-nya cocok dengan keyword prompt,
   ditambah short memory terbaru — maksimal 25 baris.
2. Planner memanggil `read_memory` untuk membaca deskripsi yang relevan, dan `search_memory` untuk topik lain.
3. Setiap kali long memory dibaca, kolom *Dipakai* bertambah — penanda memory yang bernilai.

Pencocokan memakai keyword (huruf kecil, minimal 3 karakter, kata umum seperti "yang", "tambah", "the" diabaikan).
Karena itu label sebaiknya memuat istilah bahasa Inggris **dan** istilah yang Anda pakai sehari-hari.

## Mengelola dari menu Memory

- **Tambah memory**: tulis pengetahuan yang ingin selalu diingat planner, mis. aturan bisnis penting atau konvensi
  tim. Masuk sebagai short memory, lalu otomatis pindah ke long memory.
- **Buka**: ubah tipe, project, label, deskripsi. Perbaiki memory yang kurang tepat — planner mempercayainya.
- **→ DB**: pindahkan short memory ke long memory sekarang.
- **Hapus**: hapus memory yang salah atau usang.
- **Konsolidasi sekarang**: jalankan pemindahan short → long tanpa menunggu worker.

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

Perkiraan "±N jam lagi ke long memory" di tabel Memory selalu dihitung dari 24 jam.
