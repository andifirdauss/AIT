# PLAN: Database project — backup tanpa batas (lanjutan)

Branch: `feature/database-query` (dari `main` setelah rilis 1.14.1). **Belum di-commit** saat catatan ini dibuat.

## Status sekarang (sudah jadi, test lulus 1092/1092)
- Rancangan awal: [docs/rancangan/database-query.md](docs/rancangan/database-query.md).
- Koneksi per mesin + project: entity `ProjectDatabase`, `DatabaseBackup`, migrasi `AddProjectDatabases`.
  - Service: `ProjectDatabaseService`.
  - API: `/api/projects/{id}/databases` (CRUD, test koneksi, daftar backup, restore).
  - UI: kartu Database di Project → Detail (`pages/project-databases.js`).
- Mesin: `src/AIThread.Service/Databases/`:
  - `SqlStatement` mengklasifikasi statement;
  - `DatabaseQueryService` menangani query, describe, snapshot, dry run + test restore, execute, restore, dan latihan
    SQLite;
  - `DatabaseBackupStore` menyimpan file backup (gzip + Data Protection) beserta verifikasi checksum;
  - `SensitiveColumns` + `SecretGuard.Protect` menyamarkan kolom sensitif sebagai placeholder `pii-<kolom>`.
- Tool planner `list_databases` / `describe_table` / `query_database` / `change_database`; Tester mendapat tiga tool
  baca.
- Izin: `ApprovalKind.DatabaseWrite`. Prod tanpa "selalu" dan punya `ApprovalDecision.Rehearse` (pilihan 6), di kartu
  CLI, halaman thread, dan TUI.
- Docs ID/EN: `05-menu.md#database`, `12-keamanan.md`, `09-referensi-api.md`, plus `CLAUDE.md`.
- Test:
  - `DatabaseQueryTests` (SQLite);
  - `DatabaseOrchestrationTests` (alur penuh: Dev, Prod + latihan, tanpa PK, PII, batas backup);
  - `DatabaseSqlServerLiveTests` (opt-in `AIT_LIVE_SQLSERVER=1`, LocalDB, lulus).
- Bug yang sudah diperbaiki:
  - parameter `DateTime` → `DateTime2` (SQL Server) / `TimeStamp` (Oracle);
  - urutan inisialisasi statis di `PlannerTools` (field di file partial lain).

## Masalah yang harus diperbaiki
Batas `BackupMaxRows` (50.000) salah secara desain. Guard-nya adalah **"tidak ada eksekusi tanpa backup lengkap"**,
dan aturan itu tidak boleh dibatasi ukuran tabel. Batas itu ada karena implementasi sekarang menampung semua baris di
memori (`TableSnapshot.Rows`), membaca tabel tiga kali, dan melakukan diff di memori.

## Desain baru (disetujui user)
1. **Backup di-stream ke disk, tanpa batas baris.**
   - `SELECT … ORDER BY <PK>` → tulis per baris ke file gzip + Data Protection (format per baris / chunk, bukan satu
     JSON besar).
   - Checksum dihitung sambil jalan: hash per baris, digabung berurutan menurut PK, jadi tidak perlu sorting di memori.
   - Hapus `BackupMaxRows` dari `ProjectDatabaseOptions`, docs, dan test `A_table_above_the_backup_limit…` (ganti
     dengan test tabel besar atau disk penuh).
2. **Satu-satunya batas = batas fisik, tetap sebagai guard.**
   - Cek ruang disk sebelum backup (perkiraan dari `COUNT(*)` × lebar rata-rata).
   - Backup yang tidak selesai utuh (disk penuh, koneksi putus, verifikasi gagal) → file dihapus dan tidak ada
     eksekusi. Tidak ada backup sebagian yang dianggap sah.
3. **Backup sesempit mungkin, tetapi selalu lengkap untuk yang bisa berubah.**
   - UPDATE/DELETE: ambil `WHERE` dari statement (`SqlStatement`, harus ketat), lalu backup
     `SELECT * FROM t WHERE <sama>`. Bila `WHERE` tidak bisa dipastikan (JOIN/alias rumit, subquery berkorelasi),
     backup seluruh tabel (stream).
   - INSERT/MERGE: catat kunci baris baru saat dry test (diff PK yang di-stream). Restore = hapus kunci itu + kembalikan
     baris yang diubah MERGE.
   - Dry test wajib memastikan tidak ada baris di luar backup yang berubah. Bila ada, alur berhenti.
4. **Dry test, test restore, dan restore di-stream.**
   - Data saat ini (dibaca berurutan menurut PK, ke file sementara bila engine tidak bisa membuka reader + command
     bersamaan; SQL Server tanpa MARS) di-merge-join dengan file backup.
   - Perintah restore dikirim per batch (mis. 500 baris per statement, atau multi-row VALUES), memori O(1).
   - Contoh perubahan untuk kartu tetap maks 5 baris.
5. **Waktu dan lock tidak ditolak, tetapi ditampilkan.** Kartu izin menampilkan jumlah baris yang di-backup, ukuran
   file, durasi backup, dan durasi dry test (perkiraan lama transaksi). Developer memutuskan: lanjut, latihan lokal,
   atau di luar jam sibuk. Prod tetap selalu ditanya.
6. **Latihan SQLite** memakai stream yang sama: salinan tabel ditulis bertahap, tanpa batas baris, dengan cek ruang
   disk.

## File utama yang akan disentuh
- `Databases/TableSnapshot.cs` → ganti dengan writer/reader stream (mis. `SnapshotWriter`, `SnapshotReader`, checksum
  berjalan).
- `Databases/DatabaseQueryService/*`: `SnapshotAsync`, `ReadSnapshotAsync`, `ApplyRestoreAsync`, `Diff`,
  `DryRunAsync`, `RestoreAsync`, `RehearseAsync`, `LoadCopyAsync`.
- `Databases/DatabaseBackupStore/*`: format file baru, verifikasi streaming. Backup lama boleh tetap dibaca, atau
  ditandai tidak didukung.
- `Databases/SqlStatement.cs`: ekstraksi `WHERE` untuk UPDATE/DELETE (plus test).
- `Orchestration/PlannerToolRunner/PlannerToolRunner.ChangeDatabaseAsync.cs`: info ukuran dan durasi di kartu.
- `Common/Options/AIThreadOptions.cs` (hapus `BackupMaxRows`), docs `05-menu.md` (tabel opsi + teks batas), EN, dan
  `rancangan/database-query.md`.

## Verifikasi
- `dotnet test tests/AIThread.Tests -c Release` (Release dipakai bila AIT jalan di debugger VS, karena Debug terkunci).
- Test baru:
  - tabel besar (mis. 200.000 baris SQLite), dengan pemakaian memori tetap kecil dan restore terverifikasi;
  - `WHERE` sempit di tabel besar, yang hanya mem-backup baris terdampak;
  - statement yang mengubah baris di luar backup, yang harus berhenti;
  - disk penuh / backup gagal di tengah, tanpa eksekusi.
- Live: `AIT_LIVE_SQLSERVER=1` (LocalDB). PostgreSQL, MySQL, dan Oracle belum pernah diuji ke server sungguhan.
- UI kartu Database belum dicoba di browser.

## Sesudahnya
- Commit di `feature/database-query`, merge ke `main`, lalu rilis (versi, banner `_Layout.cshtml`, README npm,
  publish-npm; lihat pola rilis 1.14.0/1.14.1).
