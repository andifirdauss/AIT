# 4. Penggunaan sehari-hari

## Alur kerja

1. Pastikan 9router dan AiThread berjalan (pill *9router* di sidebar AiThread berwarna hijau).
2. Buka Claude Code di folder project yang sudah diarahkan ke AiThread.
3. Ketik permintaan seperti biasa.
4. Pantau progres di Claude Code, atau buka Dashboard → thread untuk detailnya.
5. Tinjau perubahan dengan `git diff`.
6. Jalankan sendiri langkah lanjutan yang disebut ringkasan (build, script SQL/migrasi, restart).
7. Lanjutkan dengan prompt berikutnya — percakapan sebelumnya dan memory ikut dibaca planner.

## Membaca progres di Claude Code

Contoh untuk prompt *"Tambahkan kolom deskripsi di form user: field di UI, logic di JS, validasi di service, dan DDL database."*:

```
**AiThread** · `SampleApp.sln` · thread `5e469b54`
🧠 Planner `cc/claude-opus` menganalisis project & menyusun rencana…
   · list_directory `.`
   · read_file `src/Web/Views/User/Index.cshtml`
   · read_file `src/Web/wwwroot/js/user.js`
   · read_file `src/Service/UserService.cs`
   · search_memory "user description validation"
📋 Rencana: Menambah field Deskripsi pada form User: input di view, pengiriman nilai di JS, validasi di service, dan script DDL kolom baru.
   • T1 `src/Web/Views/User/Index.cshtml` — Add description textarea to the user form
   • T2 `src/Web/wwwroot/js/user.js` — Send description in saveUser
   • T3 `database/AddUserDescription.sql` — DDL script for Users.Description
   • T4 `src/Service/UserService.cs` — Validate description length (setelah T3)
⚙️ Menjalankan 4 tugas paralel di 2 executor…
   ✅ T2 `src/Web/wwwroot/js/user.js` (1.7s, Combo Executor) — saveUser now sends description from #txtDescription.
   ✅ T3 `database/AddUserDescription.sql` (1.7s, GLM Executor) — Created an idempotent script adding Users.Description.
   ✅ T1 `src/Web/Views/User/Index.cshtml` (1.8s, GLM Executor) — Added the txtDescription textarea after the name field.
   ✅ T4 `src/Service/UserService.cs` (3.3s, Combo Executor) — Added a 500-character limit for Description.
⏱️ Eksekusi selesai dalam 5.0s (total kerja executor 8.4s → 1.7x lebih cepat dari sekuensial)
📦 Wrapper `glm/glm-cheap` merangkum hasil…

---

Field **Deskripsi** sudah ditambahkan end-to-end. …
```

| Baris | Arti |
|---|---|
| `**AiThread** · MyApp.sln · thread …` | Project yang terdeteksi dan id thread (cari di Dashboard). Baris ini hanya muncul untuk sesi di dalam project `.sln`/`.slnx`. |
| `🧠 Planner …` dan baris `·` | Planner sedang membaca kode. Setiap baris adalah satu panggilan tool (read-only). |
| `📋 Rencana` dan `•` | Rencana yang sudah lolos validasi, satu baris per tugas. `(setelah T3)` = menunggu tugas lain. |
| `✅` / `❌` / `⏭️` | Tugas berhasil / gagal (dengan alasan) / dilewati karena tugas yang ditunggunya gagal. |
| `⏱️ … lebih cepat dari sekuensial` | Waktu nyata tahap eksekusi dibanding total waktu kerja semua executor. |
| `---` lalu teks | Ringkasan wrapper — bagian yang perlu Anda baca. |

Progres bisa disembunyikan lewat Setting → *Tampilkan progres di Claude Code*; ringkasan tetap tampil.

## Menulis prompt yang efektif

AiThread paling efektif untuk perubahan yang menyentuh beberapa file/layer sekaligus.

- **Sebut layer yang diinginkan**: "field di UI, JS, parameter API, parameter & validasi service, DDL".
- **Tunjuk contoh yang sudah ada** agar planner meniru polanya: "sama seperti kolom `Email` di form yang sama".
- **Sebut batasan**: "jangan ubah API publik", "pakai migrasi EF, bukan script SQL", "maksimal 500 karakter".
- **Satu fitur per prompt.** Perubahan besar lebih akurat bila dipecah menjadi beberapa prompt berurutan.
- **Pertanyaan boleh langsung ditanyakan** ("di mana validasi user?"). Planner membaca kode lalu menjawab tanpa
  membuat tugas — tidak ada executor yang berjalan.

## Yang tidak dilakukan AiThread

- Tidak menjalankan build, test, migrasi, perintah shell, atau git. Langkah tersebut disebutkan di ringkasan.
- Tidak memakai tool bawaan Claude Code: saat orkestrasi aktif, Claude Code berfungsi sebagai tampilan percakapan.
  Untuk pekerjaan yang butuh menjalankan perintah, matikan *Orkestrasi aktif* sementara (Claude Code menjadi agen
  biasa lewat 9router) atau kembalikan integrasi.
- Tidak meminta izin per edit — executor menulis langsung ke file. Kerjakan di repo git.

## Meninjau hasil

- `git diff` / `git status` untuk melihat semua perubahan.
- Dashboard → klik thread → klik baris tugas untuk melihat instruksi, ringkasan, **diff**, error, dan output
  model (output live hanya terlihat bila halaman sudah terbuka saat tugas berjalan).
- Tugas gagal: baca alasan di baris `❌` atau di detail tugas. Ulangi dengan prompt yang lebih spesifik untuk bagian
  itu saja, mis. "lanjutkan validasi deskripsi di UserService yang tadi gagal".

Status thread:

| Status | Arti |
|---|---|
| Selesai | Planner menjawab langsung, atau minimal satu tugas berhasil (tugas lain yang gagal dicatat di kolom error). |
| Gagal | Tidak ada tugas berhasil, atau planner/9router gagal. Pesan penyebab tampil di Claude Code dan di detail thread. |
| Dibatalkan | Dihentikan oleh Anda. |

## Membatalkan

- Tekan **Esc** di Claude Code saat thread berjalan — request terputus dan thread dibatalkan.
- Atau buka detail thread → **Batalkan thread**.

Tugas yang belum selesai dihentikan dan tidak menulis apa pun. Tugas yang **sudah** selesai sebelum pembatalan tetap
tersimpan di file — periksa dengan `git status`.

## Menjalankan prompt dari Dashboard

Kartu **Jalankan prompt** di Dashboard menjalankan thread tanpa Claude Code: pilih project terdaftar, tulis
prompt, klik *Jalankan thread*. Anda langsung diarahkan ke halaman detail thread untuk memantau. Cocok untuk
menguji konfigurasi node.

## Sesi di luar project .sln/.slnx

AiThread hanya mengorkestrasi sesi Claude Code yang dibuka di dalam project `.sln`/`.slnx`. Pada prompt pertama
sebuah sesi, AiThread memeriksa working directory-nya:

- **ada solution** → project didaftarkan (bila belum) dan semua prompt sesi itu diorkestrasi;
- **tidak ada solution** → sesi **diabaikan**: prompt diteruskan langsung ke 9router, Claude Code bekerja sebagai agen
  biasa, dan tidak ada thread yang tercatat. Log dashboard menampilkan
  `Sesi xxxxxxxx (…) bukan bagian project .sln/.slnx — prompt diabaikan orkestrasi dan diteruskan langsung.`

Keputusan diingat per sesi selama 30 menit. Jika Anda baru menambahkan `.sln` ke folder itu, mulai sesi baru
(atau tunggu 30 menit) agar dicek ulang.

## Beberapa project sekaligus

Sesi Claude Code dari project berbeda boleh berjalan bersamaan. Tiap thread terikat ke solution-nya sendiri
(`.sln`/`.slnx`), memory project terpisah, dan batas *Max paralel* tiap node dibagi di antara semua thread.

## Percakapan lanjutan

Setiap giliran, AiThread mengirim ke planner hingga 12 pesan terakhir percakapan (dipotong bila panjang) dan indeks
memory project. Jadi "sekarang tambahkan juga di halaman detail" bisa dipahami tanpa mengulang konteks.
