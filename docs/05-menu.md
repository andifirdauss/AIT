# 5. Panduan menu

Semua halaman memperbarui diri secara live lewat Server-Sent Events — tidak perlu refresh. Pill di kiri bawah
sidebar menunjukkan status 9router; tombol *Ganti tema* beralih antara mode gelap dan terang.

## Dashboard

**Kartu status**

| Kartu | Isi |
|---|---|
| 9router | Online/Offline, latency, jumlah model & combo, status API key. Tombol *Cek ulang 9router* memaksa pengecekan. |
| Short memory · Redis | Terhubung atau tidak, jumlah short memory aktif. |
| Database | Terhubung atau tidak, jumlah long memory dan thread. |
| Pipeline node | Siap / belum lengkap, beserta catatannya. |

**Statistik**: thread hari ini (dan total), thread yang sedang berjalan, success rate 100 thread terakhir,
rata-rata speedup paralel, token hari ini (planner + executor + wrapper), jumlah project dan memory.

**Jalankan prompt**: menjalankan thread untuk project terdaftar tanpa Claude Code.

**Thread terbaru**: 25 thread terakhir — waktu & sumber, project, prompt, status, tugas berhasil/total, durasi,
speedup, token. Klik baris untuk detail.

**Log langsung**: log terbaru di atas. Filter level (*Info+* default, *Semua (debug)*, *Warning+*, *Error*) dan
tombol *Jeda*. Log debug berisi panggilan tool planner dan request passthrough.

## Detail thread

| Bagian | Isi |
|---|---|
| Header | Status, project, sumber, sesi Claude Code, waktu, working directory. Tombol *Batalkan thread* saat berjalan. |
| Permintaan | Prompt Anda (tanpa blok `<system-reminder>` Claude Code). |
| Rencana planner | Ringkasan dan tabel **kontrak**. |
| Tugas executor | **Timeline paralel** (batang yang overlap berjalan bersamaan) dan tabel tugas. Klik tugas untuk instruksi, ketergantungan, ringkasan, error, diff berwarna, dan output model live. |
| Hasil wrapper | Balasan yang diterima Claude Code. |
| Waktu & token | Model planner/wrapper, jumlah langkah planner, waktu planning / eksekusi nyata / eksekusi jika sekuensial / wrapping / total, speedup, token per peran. |
| Aktivitas | Log thread ini, termasuk setiap panggilan tool planner dan penolakan validator. |

## Project

Daftar codebase yang dikenal AiThread. Satu project = satu file `.sln`/`.slnx`.

- **Registrasi otomatis**: prompt pertama dari sesi Claude Code di folder solution mendaftarkan project-nya. Sesi di
  luar project `.sln`/`.slnx` diabaikan (lihat [Penggunaan](04-penggunaan.md#sesi-di-luar-project-slnslnx)).
- **Telusuri…**: membuka penelusur solution tanpa perlu menyalin path:
  - pintasan lokasi: drive, *Home*, `source\repos`, dan folder induk project yang baru dipakai;
  - daftar folder — klik untuk masuk, *↑ Naik* untuk kembali, atau ketik path lalu Enter. Folder yang langsung berisi
    solution bertanda <kbd>sln</kbd>;
  - *Solution di folder ini*: `.sln`/`.slnx` di folder aktif, dengan status *Terdaftar* / *Baru*;
  - *Scan*: cari di semua sub-folder sampai kedalaman 2–8 (folder build, VCS, tersembunyi, dan junction dilewati;
    berhenti di 300 hasil, 50.000 folder, atau 20 detik). *Pilih semua yang belum terdaftar* mencentang semua hasil baru;
  - centang satu atau beberapa solution → **Daftarkan terpilih**.
  Folder terakhir yang dibuka diingat browser untuk kunjungan berikutnya.
- **Daftarkan manual**: tempel path file solution atau foldernya → *Daftarkan*.
- Registrasi diperlukan sebelum menerapkan integrasi Claude Code per project.
- Kolom: nama & root, path solution, jumlah thread, sesi Claude Code, memory, terakhir dipakai. Badge
  *file hilang* muncul bila `.sln` sudah tidak ada.
- **Hapus**: riwayat thread tetap ada (tanpa project), tetapi **semua memory project ini ikut terhapus**.

**Detail project**: daftar project di dalam solution, sesi Claude Code (id sesi, jumlah thread, terakhir aktif),
thread project ini, dan tautan ke memory-nya.

## Memory

Lihat [Memory](06-memory.md) untuk konsepnya.

- **Statistik**: jumlah short (Redis) & long (DB), project & knowledge, status Redis.
- **Filter**: level, tipe, project, pencarian label/deskripsi. Datang dari halaman project? Filter project sudah terisi.
- **Tabel**: level (short menampilkan perkiraan sisa jam sebelum pindah ke DB), tipe, project, label sebagai chip +
  cuplikan deskripsi, waktu dibuat & tautan thread sumber, jumlah dipakai (long memory).
- **Aksi**: *Buka* (lihat/ubah), *→ DB* (pindahkan short ke long sekarang), *Hapus*.
- **Tambah memory**: entri manual masuk sebagai short memory. Knowledge memory tidak terikat project.
- **Konsolidasi sekarang**: memindahkan semua short memory yang sudah melewati masa simpan.

## Node

Tiga kolom: Planner, Executor, Wrapper. Setiap kartu menampilkan model, temperature, max token, max paralel,
prioritas, instruksi tambahan, dengan tombol *Test*, *Edit*, *Hapus*. *Muat ulang model 9router* menyegarkan daftar
model (cache 60 detik). Detail field ada di [Konfigurasi](02-konfigurasi.md#menu-node).

## Setting

- **Koneksi 9router** — URL, API key, *Test koneksi* (tidak menyimpan) dan *Simpan*; baris status menunjukkan apakah key tersimpan dan diterima.
- **Proxy & orkestrasi** — saklar orkestrasi & progres, model passthrough, proxy API key, batas planner/executor/
  paralel/timeout.
- **Integrasi Claude Code** — cakupan per project/global, URL proxy, status file, pratinjau, *Terapkan* /
  *Kembalikan*.

Detail di [Konfigurasi](02-konfigurasi.md) dan [Integrasi Claude Code](03-integrasi-claude-code.md).

## Docs

Dokumentasi ini, ditampilkan di aplikasi. Kolom kiri berisi daftar dokumen (dengan pencarian judul) dan daftar
isi halaman yang sedang dibuka; tombol di bawah halaman berpindah ke dokumen sebelum/berikutnya. Tautan antar-dokumen
dan anchor (mis. `#menu-node`) bekerja seperti di GitLab. Isi diambil dari folder `docs/` — perubahan file langsung
terlihat setelah refresh.
