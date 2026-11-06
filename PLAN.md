# PLAN: Database project — backup tanpa batas (lanjutan)

Branch: `feature/database-query`. Fitur dasar sudah di commit `8709e34 [FEAT] Akses Query`. Backup tanpa batas
(di bawah) **sudah dikerjakan tetapi belum di-commit**.

## Selesai
- Fitur dasar (commit `8709e34`): koneksi per mesin + project, tool planner, izin Prod + latihan SQLite, docs, test.
  Rancangan: [docs/rancangan/database-query.md](docs/rancangan/database-query.md).
- **Backup tanpa batas baris.** `BackupMaxRows` dihapus. Satu-satunya batas adalah disk (`BackupFreeSpaceReserveMb`,
  bawaan 512 MB; `DiskSpace`), dicek sebelum mulai, per frame, dan dari perkiraan `COUNT(*)` × ukuran per baris.
  - `SnapshotFile` (`SnapshotWriter`/`SnapshotReader`): format 2. Frame gzip + Data Protection terikat id file, nomor
    urut, trailer jumlah baris + `RowChecksum` (jumlah SHA-256 per baris, tidak bergantung urutan, tanpa sorting).
  - Backup gagal di tengah → file dihapus dan tidak ada eksekusi. Format lama ditandai tidak didukung.
- **Backup sesempit mungkin** (`BackupScope` dari `SqlStatement`):
  - `Rows`: `WHERE` persis dari `UPDATE`/`DELETE` biasa;
  - `Insert`: kunci baris baru dicatat saat eksekusi (`<backup>.added`), karena identity saat dry test berbeda;
  - `Table`: selain itu, termasuk `WHERE` nondeterministik (`TOP`/`LIMIT`/`RAND`/`NEWID`/…).
- **Dry test, test restore, eksekusi, dan restore di-stream** lewat `DiffScratch`: SQLite sementara di `db-work`, kunci
  di-hash, baris dienkripsi AES-GCM dengan kunci di memori. Delete restore per batch (≤ 500 kunci). Contoh perubahan
  tetap maks 5.
  - Dry test `Rows`/`Insert` membandingkan checksum **seluruh tabel** sebelum/sesudah, sehingga trigger, cascade, atau
    PK yang berubah dan menyentuh baris di luar backup menghentikan alur.
  - Eksekusi mengecek ulang bahwa baris yang dicakup masih sama dengan backup. Untuk `Insert`, kunci baru harus sama
    dengan baris terpengaruh.
- **Kartu izin** menampilkan cakupan, jumlah baris dan ukuran backup, lama backup, lama dry test, dan `Hold`
  (perkiraan lama lock).
- **Latihan SQLite** di-stream tanpa batas baris, per 10.000 baris, dengan cek disk.
- Tambahan dari review sendiri: `SqlStatement` kini menolak `INSERT ALL/FIRST` (Oracle multi-tabel) dan
  `OUTPUT … INTO` (menulis tabel kedua); modifier MySQL (`LOW_PRIORITY`, `IGNORE`, …) dilewati saat membaca target.
- Docs ID/EN (`05-menu.md`, `12-keamanan.md`), `rancangan/database-query.md`, dan `CLAUDE.md` diperbarui.

## Verifikasi
- `dotnet test tests/AIThread.Tests -c Release`: 1113/1115 lulus. Dua yang gagal tidak terkait database dan gagal
  karena lingkungan mesin: `ChatWindowsTests.Prompt_mode_turns_image_paths_into_numbered_markers` (path temp berisi
  spasi) dan `EmbeddedRedisServerTests.A_taken_port_is_left_to_its_owner` (port sedang dipakai).
- Test baru:
  - `DatabaseLargeTableTests`: 200.000 baris, backup + dry test + eksekusi + restore. Puncak memori managed hidup
    2,0 MB, backup 3,7 MB, dry test 8,2 s, hold 0,4 s. `WHERE` sempit di tabel besar hanya mem-backup 10 baris.
  - `DatabaseQueryTests`: cakupan per statement, backup `WHERE`, INSERT (kunci dicatat, restore hanya menghapus baris
    itu), data berubah sejak backup, trigger ke baris di luar backup, file backup rusak, disk penuh.
  - `DatabaseOrchestrationTests`: disk penuh (tanpa file/record tersisa), alur INSERT end to end.

- Live `AIT_LIVE_SQLSERVER=1` (LocalDB 17.0) lulus. Kasus yang diuji: backup `WHERE` + `IDENTITY_INSERT`,
  `UPDATE … FROM` sebagai seluruh tabel, dan INSERT ke tabel IDENTITY (kunci dicatat saat eksekusi, restore menghapus
  baris itu).

## Belum
- PostgreSQL, MySQL, dan Oracle belum pernah diuji ke server sungguhan.
- UI kartu Database belum dicoba di browser.
- Batasan yang diketahui ada di bagian "Batasan yang diketahui" pada rancangan:
  - trigger/cascade ke tabel lain tidak terdeteksi;
  - jeda kecil READ COMMITTED antara cek dan statement;
  - dry test `Rows`/`Insert` membaca seluruh tabel dua kali.

## Sesudahnya
- Commit di `feature/database-query`, merge ke `main`, lalu rilis (versi, banner `_Layout.cshtml`, README npm,
  publish-npm; lihat pola rilis 1.14.0/1.14.1).
