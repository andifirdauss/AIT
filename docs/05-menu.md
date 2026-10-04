# 5. Panduan menu

Semua halaman memperbarui diri secara live lewat Server-Sent Events — tidak perlu refresh. Pill di kiri bawah
sidebar menunjukkan status 9router; tombol *Ganti tema* beralih antara mode gelap dan terang.

## Dashboard

**Kartu status**

| Kartu | Isi |
|---|---|
| 9router | Online/Offline, latency, jumlah model & combo, status API key. Tombol *Cek ulang 9router* memaksa pengecekan. |
| Short memory | Terhubung atau tidak, jumlah short memory aktif, dan store yang dipakai (menu Setting → Penyimpanan memory). |
| Database | Terhubung atau tidak, jumlah long memory dan thread. |
| Pipeline node | Siap / belum lengkap, beserta catatannya. |

**Statistik**: thread hari ini (dan total), thread yang sedang berjalan, success rate 100 thread terakhir,
rata-rata speedup paralel, token hari ini (planner + executor + wrapper), jumlah project dan memory.

**Jalankan prompt**: menjalankan thread untuk project terdaftar tanpa Claude Code.

**Thread terbaru**: 25 thread terakhir — waktu & sumber (*Claude Code*, *Dashboard*, *Telegram*), project, prompt,
status, tugas berhasil/total, durasi, speedup, token. Klik baris untuk detail.

**Log langsung**: log terbaru di atas. Filter level (*Info+* default, *Semua (debug)*, *Warning+*, *Error*) dan
tombol *Jeda*. Log debug berisi panggilan tool planner dan request passthrough.

## Detail thread

| Bagian | Isi |
|---|---|
| Header | Status, project, sumber, sesi Claude Code, waktu, working directory. Tombol *Batalkan thread* saat berjalan; setelah selesai, *🔁 Ulangi yang gagal* dan *↩ Batalkan perubahan file* (lihat [Penggunaan](04-penggunaan.md#mengulang-tugas-yang-gagal)). |
| Permintaan | Prompt Anda (tanpa blok `<system-reminder>` Claude Code). |
| Rencana planner | Ringkasan dan tabel **kontrak**. |
| Tugas executor | **Timeline paralel** (batang yang overlap berjalan bersamaan) dan tabel tugas. Klik tugas untuk instruksi, ketergantungan, ringkasan, error, diff berwarna, dan output model live. |
| Verifikasi | Muncul bila build/reviewer berjalan. Hasil akhir (*Lolos* / *Masih ada masalah* / *Tidak dapat dipastikan*), perintah build, model reviewer, lalu per putaran: hasil build dan errornya, temuan reviewer, masalah yang tidak bisa diperbaiki otomatis, dan id tugas perbaikan (`V1-1`, …) yang tampil juga di tabel tugas. |
| Hasil wrapper | Balasan yang diterima Claude Code. |
| Konteks sesi dari Context Keeper | Ringkasan percakapan sampai thread ini (khusus thread dari Claude Code) — titik awal planner di prompt berikutnya pada chat yang sama. Muncul beberapa detik setelah thread selesai. |
| Waktu & token | Model planner/reviewer/wrapper, jumlah langkah planner, waktu planning / eksekusi nyata / eksekusi jika sekuensial / verifikasi / wrapping / total, speedup, token per peran. |
| Aktivitas | Log thread ini, termasuk setiap panggilan tool planner dan penolakan validator. |

## Project

Daftar codebase yang dikenal AiThread. Satu project = satu folder root, yaitu folder yang berisi `.git` atau file build
(`.sln`, `pom.xml`, `package.json`, `go.mod`, dst.), apa pun bahasanya.

- **Registrasi otomatis**: prompt pertama dari sesi di dalam sebuah project mendaftarkan project-nya. Sesi di folder
  tanpa marker diabaikan (lihat [Penggunaan](04-penggunaan.md#sesi-di-luar-project)).
- **Telusuri…**: membuka penelusur folder tanpa perlu menyalin path:
  - pintasan lokasi: drive, *Home*, `source\repos`, dan folder induk project yang baru dipakai;
  - daftar folder — klik untuk masuk, *↑ Naik* untuk kembali, atau ketik path lalu Enter. Folder project bertanda
    stack-nya (mis. <kbd>.NET</kbd>, <kbd>Java (Maven)</kbd>, <kbd>Node.js</kbd>) atau <kbd>git</kbd>;
  - *Project di folder ini*: folder aktif bila ia sebuah root project, dengan status *Terdaftar* / *Baru*;
  - *Scan*: cari di semua sub-folder sampai kedalaman 2–8 (folder build, VCS, tersembunyi, dan junction dilewati;
    sebuah project tidak dipindai lebih dalam; berhenti di 300 hasil, 50.000 folder, atau 20 detik). *Pilih semua yang
    belum terdaftar* mencentang semua hasil baru;
  - centang satu atau beberapa project → **Daftarkan terpilih**.
  Folder terakhir yang dibuka diingat browser untuk kunjungan berikutnya.
- **Daftarkan manual**: tempel path folder → *Daftarkan*. Folder dengan marker sendiri didaftarkan apa adanya; path di
  dalam sebuah project mendaftarkan project itu; folder tanpa marker sama sekali tetap bisa didaftarkan (sesi di
  dalamnya lalu diorkestrasi dan punya memory). Drive, folder Home, dan folder sistem ditolak. Path file juga diterima
  (foldernya yang dipakai).
- Registrasi diperlukan sebelum menerapkan integrasi CLI per project.
- Kolom: nama & root, **AiThread**, stack, jumlah thread, sesi Claude Code, memory, terakhir dipakai. Badge
  *folder hilang* muncul bila folder project sudah tidak ada.
- **Switch AiThread**: menyala = `<project>/.claude/settings.local.json` mengarahkan Claude Code ke proxy ini. Menyalakan
  menulis blok `env` (sama dengan switch Claude Code per project di menu [CLI](#cli)); mematikan menghapus kunci AiThread
  saja, kunci lain di file tetap. Catatan *URL lain* berarti file mengarah ke alamat proxy lain (mis. port lama) —
  nyalakan ulang untuk memperbarui. Buka sesi Claude Code baru setelah mengubahnya.
- **Hapus**: riwayat thread tetap ada (tanpa project), tetapi **semua memory project ini ikut terhapus**.

**Detail project**: stack, daftar file solution/build yang ditemukan di dalam project, sesi Claude Code (id sesi, jumlah thread, terakhir aktif),
thread project ini, dan tautan ke memory-nya.

## Memory

Lihat [Memory](06-memory.md) untuk konsepnya.

- **Statistik**: jumlah short & long memory, project & knowledge, status store short memory.
- **Filter**: level, tipe, project, pencarian label/deskripsi. Datang dari halaman project? Filter project sudah terisi.
- **Tabel**: level (short menampilkan perkiraan sisa jam sebelum pindah ke DB), tipe, project, label sebagai chip +
  cuplikan deskripsi, waktu dibuat & tautan thread sumber, jumlah dipakai (long memory).
- **Aksi**: *Buka* (lihat/ubah), *→ DB* (pindahkan short ke long sekarang), *Hapus*.
- **Tambah memory**: entri manual masuk sebagai short memory. Knowledge memory tidak terikat project.
- **Konsolidasi sekarang**: memindahkan semua short memory yang sudah melewati masa simpan.

## Node

Lima kolom: Planner, Executor, Reviewer (opsional), Wrapper, Context Keeper (opsional). Setiap kartu menampilkan model, temperature, max token, max paralel,
prioritas, penanda *📝 instruksi* bila ada instruksi tambahan (isinya di tooltip), penanda *🧩 n* bila node memakai n skill
(namanya di tooltip), penanda *↪ nama* bila node punya [node cadangan](02-konfigurasi.md#node-cadangan), dengan tombol *Test*, *Edit*, *Hapus*. Di modal node, bagian *Skill* menampilkan skill terpasang
sebagai chip. Kolom *Cari sendiri di gudang* membuka dropdown skill yang cocok dengan nama, deskripsi, tag, sumber,
atau isi skill (potongan isi yang cocok ditampilkan); klik item untuk memasang/melepas. Kolom *Butuh kemampuan apa?* + **Cari skill** meminta Skill Orchestrator mencarikan skill yang cocok
dari gudang (lihat [Skill pada node](07-cara-kerja.md#skill-pada-node)).
Panel *Panduan* di atas kolom menjelaskan arti angka-angka tersebut; panel bisa dilipat dan keadaannya diingat browser. *Muat ulang model 9router* menyegarkan daftar
model (cache 60 detik). Detail field ada di [Konfigurasi](02-konfigurasi.md#menu-node).

## CLI

Mengarahkan CLI coding ke proxy AiThread dengan satu switch, tanpa menyunting file konfigurasi CLI itu sendiri. Saat ini
ada dua kartu: **Claude Code** dan **oh-my-pi (omp) —
provider AiThread** (`models.yml`, hanya global: omp mendapat provider `aithread` sendiri di samping Anthropic asli);
CLI lain bisa ditambahkan dengan cara yang sama.

- **URL proxy AiThread** di bagian atas: alamat yang ditulis ke konfigurasi CLI (default = alamat http aplikasi).
  Mengubahnya menyegarkan status semua switch.
- Tiap CLI punya satu kartu berisi:
  - **Global** — semua sesi CLI itu di komputer ini (`~/.claude/settings.json` untuk Claude Code,
    `~/.omp/agent/models.yml` untuk omp). Menyalakan meminta konfirmasi.
  - **Per project** — satu switch per project terdaftar (`<project>/.claude/settings.local.json`;
    tidak tersedia untuk omp). Lencana *Aktif*, *Nonaktif*, atau *Mengarah ke alamat lain* (file menunjuk URL lain, mis. port
    lama — matikan lalu nyalakan lagi).
  - *Yang akan ditulis*: pratinjau isi yang akan dituliskan.
- Mematikan switch hanya menghapus yang ditulis AiThread; isi lain di file tetap. Backup file asli disimpan sekali
  sebagai `*.aithread-backup`.
- Kartu Claude Code juga memuat **Context Keeper untuk /clear** (*Pasang hook* / *Lepas hook*; lihat
  [Integrasi Claude Code](03-integrasi-claude-code.md#context-keeper-untuk-clear)).
- Bila *Proxy API key* di Setting diubah, matikan lalu nyalakan lagi agar key baru ikut tertulis.

Detail tiap CLI: [Integrasi Claude Code](03-integrasi-claude-code.md) dan
[Klien lain](03-integrasi-claude-code.md#klien-lain-codex-gemini-cli-oh-my-pi-dll).

## Setting

Halaman dibagi menjadi tab. Tab yang terbuka tercatat di URL (mis. `/Setting#telegram`), jadi tetap sama setelah
refresh dan bisa ditautkan langsung.

- **Koneksi 9router** (`#router`) — URL, API key, *Test koneksi* (tidak menyimpan); baris status menunjukkan apakah key tersimpan dan diterima.
- **Proxy & orkestrasi** (`#proxy`) — saklar orkestrasi, progres & *Planner boleh bertanya*, model passthrough, proxy API key (beserta tautan ke
  menu [CLI](#cli), tempat integrasi Claude Code/omp dan hook Context Keeper diatur), batas planner/executor/paralel/timeout.
- **Build & verifikasi** (`#build`) — *Build & verifikasi otomatis*, timeout build, dan jumlah putaran perbaikan.
- **Penyimpanan memory** (`#memory`) — provider short & long memory (bawaan, Redis, SQL Server, PostgreSQL, MySQL, Oracle, SQLite)
  beserta host/port/database/username/password, *Test koneksi* per kartu, status live, dan opsi menyalin memory lama.
  Disimpan terpisah lewat **Simpan penyimpanan memory**.
- **Telegram** (`#telegram`) — token bot, aktif/nonaktif, status koneksi live, dan tabel chat (*Izinkan* / *Hapus* / *Cabut*).
- **Skill** (`#skills`) — model Skill Orchestrator, batas karakter skill per node, sumber skill (tempel link →
  **Tarik skill**, status live, *Sync* / *Hapus*), dan gudang skill dengan pencarian serta pratinjau isi. Disimpan
  terpisah lewat **Simpan Skill**.

Tiga tab pertama adalah satu form: **Simpan setting** di tab mana pun menyimpan isi ketiganya sekaligus, dan field
yang tidak valid membuka tabnya sendiri. Tab Telegram dan Skill disimpan terpisah lewat tombolnya masing-masing.

Detail di [Konfigurasi](02-konfigurasi.md) dan [Prompt dari Telegram](11-telegram.md).

## Docs

Dokumentasi ini, ditampilkan di aplikasi. Kolom kiri berisi daftar dokumen (dengan pencarian judul) dan daftar
isi halaman yang sedang dibuka; tombol di bawah halaman berpindah ke dokumen sebelum/berikutnya. Tautan antar-dokumen
dan anchor (mis. `#menu-node`) bekerja seperti di GitLab. Isi diambil dari folder `docs/` — perubahan file langsung
terlihat setelah refresh.
