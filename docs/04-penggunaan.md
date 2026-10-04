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
**AiThread** · `SampleApp` · thread `5e469b54`
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
🔍 Verifikasi: build `dotnet build SampleApp.sln` + reviewer `cc/claude-sonnet`…
🔨 Build `dotnet build SampleApp.sln`…
   ❌ build gagal: 1 error (14.2s)
      · src/Service/UserService.cs(48,13): CS0103: The name 'MaxDescriptionLength' does not exist in the current context
🔎 Reviewer `cc/claude-sonnet` memeriksa hasil…
   ⚠️ Reviewer menemukan 1 masalah: Konstanta batas panjang deskripsi belum dideklarasikan.
      · `src/Service/UserService.cs` — MaxDescriptionLength is used but never declared.
🛠️ Putaran perbaikan 1: 1 tugas…
   • V1-1 `src/Service/UserService.cs` — Fix build errors and review findings
   ✅ V1-1 `src/Service/UserService.cs` (2.1s, GLM Executor) — Declared MaxDescriptionLength = 500.
🔨 Build `dotnet build SampleApp.sln`…
   ✅ build berhasil (9.8s)
🔎 Reviewer `cc/claude-sonnet` memeriksa hasil…
   ✅ Reviewer: Semua layer memakai txtDescription/Description sesuai kontrak.
📦 Wrapper `glm/glm-cheap` merangkum hasil…

---

Field **Deskripsi** sudah ditambahkan end-to-end. …
```

| Baris | Arti |
|---|---|
| `**AiThread** · MyApp · thread …` | Nama project yang terdeteksi dan id thread (cari di Dashboard). Baris ini hanya muncul untuk sesi di dalam sebuah project (folder dengan `.git` atau file build). |
| `🧠 Planner …` dan baris `·` | Planner sedang membaca kode. Setiap baris adalah satu panggilan tool (read-only). |
| `📋 Rencana` dan `•` | Rencana yang sudah lolos validasi, satu baris per tugas. `(setelah T3)` = menunggu tugas lain. |
| `✅` / `❌` / `⏭️` | Tugas berhasil / gagal (dengan alasan) / dilewati karena tugas yang ditunggunya gagal. |
| `⏱️ … lebih cepat dari sekuensial` | Waktu nyata tahap eksekusi dibanding total waktu kerja semua executor. |
| `🔍 Verifikasi`, `🔨 Build`, `🔎 Reviewer` | AiThread membuild project dan/atau node Reviewer memeriksa hasil (lihat [Verifikasi](07-cara-kerja.md#5-verifikasi-build-dan-reviewer)). Error ditampilkan maks 5 baris. |
| `🛠️ Putaran perbaikan` dan `V1-1`, … | Error build dan temuan reviewer diserahkan ke executor sebagai tugas perbaikan, lalu dicek ulang. |
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

- Tidak menjalankan test, migrasi, git, instalasi paket, atau perintah shell lain. Satu-satunya perintah adalah
  **build** project (bila *Build & verifikasi otomatis* menyala): Anda bisa meminta "build lalu perbaiki errornya",
  dan setelah tiap perubahan AiThread membuild ulang sendiri. Langkah lain disebutkan di ringkasan.
- Tidak memakai tool bawaan Claude Code: saat orkestrasi aktif, Claude Code berfungsi sebagai tampilan percakapan.
  Untuk pekerjaan yang butuh menjalankan perintah selain build, matikan *Orkestrasi aktif* sementara (Claude Code
  menjadi agen biasa lewat 9router) atau kembalikan integrasi.
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
| Selesai | Planner menjawab langsung, atau minimal satu tugas berhasil (tugas lain yang gagal, dan error build/temuan reviewer yang tersisa setelah verifikasi, dicatat di kolom error). |
| Gagal | Tidak ada tugas berhasil, atau planner/9router gagal. Pesan penyebab tampil di Claude Code dan di detail thread. |
| Dibatalkan | Dihentikan oleh Anda. |
| Perubahan dibatalkan | Perubahan file thread ini sudah dikembalikan lewat **Batalkan perubahan file** (lihat di bawah). |

## Membatalkan

- Tekan **Esc** di Claude Code saat thread berjalan — request terputus dan thread dibatalkan.
- Atau buka detail thread → **Batalkan thread**.

Tugas yang belum selesai dihentikan dan tidak menulis apa pun. Tugas yang **sudah** selesai sebelum pembatalan tetap
tersimpan di file. Untuk mengembalikannya, pakai **Batalkan perubahan file** (di bawah).

## Mengulang tugas yang gagal

Detail thread → **🔁 Ulangi yang gagal** (muncul bila thread sudah selesai, gagal, atau dibatalkan dan masih ada tugas
rencana yang belum berhasil).

- Rencana yang tersimpan dipakai lagi; **planner tidak dipanggil**, jadi tidak ada biaya planner.
- Yang dijalankan ulang hanya tugas rencana yang gagal, dilewati, atau dibatalkan. Tugas yang sudah berhasil tidak
  diulang, dan dependensi ke tugas itu dianggap terpenuhi.
- Setelah itu verifikasi (build/reviewer) dan wrapper berjalan lagi atas seluruh hasil thread. Tugas perbaikan lama
  yang tidak berhasil ditandai *Dilewati* karena digantikan oleh verifikasi baru.
- Semuanya tercatat di thread yang sama; jumlahnya tampil di *Percobaan ulang* pada panel waktu & token.
- Berguna setelah rate limit, setelah menambah node Executor yang tadinya belum ada, atau setelah memperbaiki
  konfigurasi model.

## Membatalkan perubahan file (rollback)

Detail thread → **↩ Batalkan perubahan file** mengembalikan semua file yang diubah thread ini ke keadaan sebelum
thread berjalan.

Cara kerjanya:

1. **Sebelum** executor menulis sebuah file untuk pertama kali dalam thread, isi aslinya disimpan di database apa
   adanya (byte demi byte, termasuk encoding dan akhir baris). File yang belum ada dicatat sebagai "belum ada".
   Penulisan berikutnya di thread yang sama (putaran perbaikan, percobaan ulang) tidak menimpa catatan ini.
2. **Setelah** setiap penulisan, sidik jari (hash SHA-256) isi yang ditulis AiThread ikut dicatat.
3. Tombol ini membuka pratinjau per file:
   - *Dikembalikan ke isi asli*: isi file sekarang masih persis hasil thread ini.
   - *Dihapus*: file dibuat oleh thread ini dan isinya belum diubah orang lain.
   - *Sudah sama dengan aslinya*: tidak ada yang perlu dilakukan.
   - *Sudah berubah lagi*: file diedit setelah thread ini (oleh Anda atau thread lain), atau dihapus.
4. **Semua atau tidak sama sekali**: bila ada satu saja file *Sudah berubah lagi*, pembatalan ditolak dan **tidak ada
   file yang disentuh**. Kembalikan file itu secara manual (mis. lewat git), atau batalkan dulu thread yang lebih baru,
   lalu coba lagi.
5. Setelah berhasil, status thread menjadi *Perubahan dibatalkan*. Thread itu tidak bisa diulang atau dibatalkan lagi.

Batasan:

- Hanya bisa saat thread tidak sedang berjalan.
- Hanya mencakup file yang ditulis executor AiThread, bukan perubahan lain (mis. output build).
- Folder yang dibuat thread untuk file baru tidak ikut dihapus.
- Thread yang berjalan sebelum fitur ini ada tidak punya catatan, jadi tidak bisa dibatalkan.

## Menjalankan prompt dari Dashboard

Kartu **Jalankan prompt** di Dashboard menjalankan thread tanpa Claude Code: pilih project terdaftar, tulis
prompt, klik *Jalankan thread*. Anda langsung diarahkan ke halaman detail thread untuk memantau. Cocok untuk
menguji konfigurasi node.

## Menjalankan prompt dari Telegram

Saat laptop ditinggal menyala, kirim prompt dari HP lewat bot Telegram pribadi. Pilih project dengan `/projects`,
lalu kirim pesan teks biasa: bot menampilkan status thread yang diperbarui live (rencana, tiap tugas, tombol
*Batalkan*) dan mengirim ringkasan wrapper saat selesai; `/diff` menampilkan perubahan file. Setup dan daftar perintah
ada di [Prompt dari Telegram](11-telegram.md).

## Sesi di luar project

AiThread hanya mengorkestrasi sesi yang dibuka di dalam sebuah project: folder yang berisi `.git` atau file build
(`.sln`, `pom.xml`, `package.json`, `go.mod`, dst.), atau salah satu sub-foldernya. Bahasa project tidak dibatasi.
Pada prompt pertama sebuah sesi, AiThread memeriksa working directory-nya:

- **ada marker project** → project didaftarkan (bila belum) dan semua prompt sesi itu diorkestrasi;
- **tidak ada marker** → sesi **diabaikan**: prompt diteruskan langsung ke 9router, Claude Code bekerja sebagai agen
  biasa, dan tidak ada thread, project, maupun memory yang tercatat. Contohnya folder kosong tempat Anda hanya meminta
  dibuatkan sebuah file teks. Log dashboard menampilkan
  `Sesi xxxxxxxx (…) bukan bagian project (alasannya) — prompt diabaikan orkestrasi dan diteruskan langsung.`

Folder induk yang hanya berisi beberapa project (mis. 4 repo berdampingan) juga diabaikan karena ambigu; log menyebut
nama project di dalamnya. Buka sesi Claude Code di dalam salah satunya. Ingin folder tanpa marker tetap diorkestrasi
dan punya memory? Daftarkan manual lewat menu Project (*Daftarkan manual*).

Keputusan untuk sesi yang diabaikan dicek ulang setelah sekitar 5 detik, jadi begitu Anda menambahkan `.git` atau file
build, prompt berikutnya langsung dikenali.

## Beberapa project sekaligus

Sesi dari project berbeda boleh berjalan bersamaan (mis. 4 sesi Claude Code di 4 project, atau campuran Java, Node,
dan .NET). Tiap thread terikat ke root project-nya sendiri, memory project terpisah, dan batas *Max paralel* tiap
node dibagi di antara semua thread.

## Mengecek folder atau project lain

Sebutkan path lengkapnya di prompt, mis. *"cek struktur `D:/project/otherproject` dan bandingkan dengan project ini"*.
Planner boleh **membaca** (melihat isi folder, mencari teks, membaca file) di luar root project, jadi ia bisa menjawab
dari sana. Mengubah file tetap hanya di dalam root project: bila Anda meminta perubahan di luar root, planner
menjelaskannya di jawaban dan tidak membuat tugas. Untuk mengerjakan project lain, buka sesi di project itu.

## Percakapan lanjutan

Setiap giliran, AiThread mengirim ke planner hingga 12 pesan terakhir percakapan (dipotong bila panjang) dan indeks
memory project. Jadi "sekarang tambahkan juga di halaman detail" bisa dipahami tanpa mengulang konteks.

Sesekali planner balik bertanya, misalnya bila permintaan bisa ditafsirkan dua cara yang hasilnya berbeda jauh.
Balasan AiThread lalu berupa pertanyaan bernomor (status thread *Menunggu jawaban*); ketik nomor pilihan atau jawaban
Anda sebagai prompt berikutnya, dan thread yang sama dilanjutkan. Lihat
[Pertanyaan dari planner](07-cara-kerja.md#pertanyaan-dari-planner).
