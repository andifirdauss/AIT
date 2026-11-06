# Rancangan: database project untuk query dan perubahan data

## Masalah
Planner dan tester hanya bisa membaca kode, bukan data. Akibatnya:
- Saat menganalisis bug, planner tidak bisa melihat isi tabel.
- Saat testing, tester tidak bisa memastikan data benar-benar tersimpan setelah memanggil API.

Developer juga sering perlu memperbaiki data (UPDATE/DELETE) dengan aman: ada titik restore, diuji dulu, dan untuk
prod dilatih dulu di salinan lokal.

## Keputusan
- **Koneksi per project, per mesin.** Disimpan di database utama AIT (tabel `ProjectDatabases`), password dienkripsi
  Data Protection seperti API key 9router. Model hanya melihat nama koneksi, provider, dan environment; connection
  string tidak pernah dikirim ke model.
- **Provider:** sama dengan penyimpanan memory, yaitu SQL Server, PostgreSQL, MySQL, Oracle, dan SQLite. Pembentuk
  connection string dipakai bersama `MemoryStoreConnection`.
- **Environment per koneksi:** `Dev`, `Staging`, `Prod`.
- **Baca:** tool `query_database` hanya menerima satu statement baca (`SELECT` / `WITH … SELECT`):
  - dijalankan di transaksi baca-saja bila engine mendukung, lalu selalu di-rollback;
  - dibatasi jumlah baris (bawaan 50, maks 500) dan timeout 30 detik.
- **Kolom sensitif disamarkan.** Ini berlaku untuk daftar kolom per koneksi ditambah deteksi dari nama kolom (email,
  phone/telp, nik/ktp, npwp, password, hash, token, secret, alamat/address, tanggal lahir, nomor kartu/rekening).
  - Nilainya menjadi placeholder `SecretGuard` (`{{AIT_SECRET:pii-<kolom>:<id>}}`). Nilai yang sama selalu mendapat
    placeholder yang sama, jadi model tetap bisa mencocokkan baris.
  - Saat placeholder dipakai di query tulis, AIT memulihkannya ke nilai asli sebelum query dijalankan.
- **Tulis: DML saja** (`INSERT`, `UPDATE`, `DELETE`, `MERGE`), satu statement, satu tabel target yang punya primary
  key.
  - DDL (`CREATE`/`ALTER`/`DROP`/`TRUNCATE`), `EXEC`/`CALL`, `SELECT … INTO`, dan lebih dari satu statement ditolak.
  - Perubahan skema tetap lewat migrasi kode.

## Alur tulis (`change_database`)
1. **Klasifikasi.** Statement harus DML tunggal, dan tabel target diambil dari statement. Tabel tanpa primary key
   ditolak, karena restore per baris butuh kunci.
2. **Backup = titik restore (wajib), tanpa batas baris.** Guard-nya adalah "tidak ada eksekusi tanpa backup
   lengkap", bukan ukuran tabel. Cakupan backup (`BackupScope`, dari `SqlStatement`) sesempit mungkin, tetapi selalu
   lengkap untuk yang bisa berubah:
   - `Rows`: `UPDATE`/`DELETE` satu tabel dengan `WHERE` biasa. Backup = `SELECT … FROM <tabel + alias persis seperti
     di statement> WHERE (<kondisi persis>)`.
   - `Insert`: `INSERT` tanpa upsert (`ON DUPLICATE KEY UPDATE`, `ON CONFLICT … DO UPDATE`, `OR REPLACE`). Tidak ada
     baris yang di-backup; kunci baris baru dicatat saat eksekusi (`<backup>.added`), karena nilai identity saat dry
     test berbeda dengan saat eksekusi.
   - `Table`: selain itu (MERGE, upsert, `UPDATE … FROM`, `DELETE … USING`/JOIN, CTE, tanpa `WHERE`, `WHERE CURRENT
     OF`, atau `WHERE` yang tidak deterministik: `TOP`, `LIMIT`, `FETCH`, `ROWNUM`, `RAND`, `NEWID`, `UUID`, …).

   Format file 2 (`SnapshotFile`): `AITDBK2` + id file + frame. Setiap frame = [panjang][Data Protection([urutan][jenis]
   [isi])], dan purpose protector memuat id file. Jenis frame: header JSON, baris (gzip, maks 2000 baris/1 MB), dan
   trailer (jumlah baris + checksum). File tanpa trailer tidak lengkap dan tidak pernah dianggap backup. Checksum
   (`RowChecksum`) = jumlah SHA-256 per baris (kunci + isi) dalam empat lajur 64-bit, jadi tidak bergantung urutan dan
   tidak perlu sorting.
   - Baca untuk backup dilakukan di transaksi baca-saja yang di-rollback, karena `WHERE`-nya berasal dari model.
   - Batas satu-satunya adalah disk (`DiskSpace`): sebelum mulai dan setiap frame, sisa ruang dicek terhadap
     `BackupFreeSpaceReserveMb` plus perkiraan sisa (`COUNT(*)` × ukuran per baris sejauh ini).
   - **Selama backup belum lengkap, tidak ada eksekusi apa pun.** Backup yang gagal (disk penuh, koneksi putus,
     verifikasi gagal) dihapus.
3. **Cek file backup.** File dibaca ulang sampai trailer: urutan frame, jumlah baris, dan checksum dicocokkan.
4. **Dry test + test restore, dalam satu transaksi yang selalu di-rollback:**
   - baris yang dicakup backup dibaca lagi dan checksum-nya harus sama dengan backup; untuk `Rows`/`Insert`, checksum
     seluruh tabel juga dicatat;
   - statement dijalankan, dan jumlah baris yang terpengaruh dicatat. Untuk `Rows`, lebih banyak baris daripada isi
     backup berarti statement menyentuh baris di luar backup, jadi alur berhenti;
   - perbandingan memakai `DiffScratch`: file SQLite sementara di `db-work` dengan tabel `b` (backup) dan `a` (sekarang),
     kunci = hash, isi baris dienkripsi AES-GCM dengan kunci yang hanya ada di memori; SQLite mencari baris yang hanya
     ada di satu sisi atau yang isinya berbeda. Memori tetap kecil berapa pun ukuran tabelnya;
   - untuk `Rows`, baris "sekarang" = lookup per batch kunci backup (`IN (…)` maks 500 kunci, di bawah batas Oracle 1000
     dan SQL Server 2100 parameter); untuk `Table`, seluruh tabel; untuk `Insert`, kunci seluruh tabel sebelum/sesudah;
   - restore dari backup per primary key: baris baru dihapus per batch, baris yang berubah dikembalikan, baris yang
     terhapus disisipkan lagi;
   - baris yang dicakup dicocokkan dengan backup lewat checksum. Untuk `Rows`/`Insert`, seluruh tabel juga harus sama
     dengan checksum sebelum statement, sehingga trigger, cascade, atau primary key yang berubah dan menyentuh baris di
     luar backup ikut menghentikan alur. Untuk `Insert`, jumlah kunci baru juga harus sama dengan baris terpengaruh;
   - lalu **ROLLBACK**, sehingga database tidak berubah sama sekali.
   - Waktu dicatat: lama dry test, dan lama dari awal transaksi sampai statement selesai (`Hold`, perkiraan lama
     eksekusi sungguhan memegang lock). Keduanya ditampilkan di kartu, bukan ditolak.

   Bila dry test atau test restore gagal, alur berhenti dan tidak ada eksekusi.
5. **Izin developer** (kartu izin). Kartu menampilkan environment, statement, jumlah baris terpengaruh, id backup,
   cakupan, jumlah baris dan ukuran backup, lama backup, lama dry test, perkiraan lama lock, dan hasil test restore.
   - **Dev/Staging:** 1 sekali / 2 selalu untuk koneksi ini / 3 tolak.
   - **Prod:** 1 latihan lokal dulu / 2 eksekusi sekarang / 3 tolak. Tidak ada "selalu".
6. **Latihan lokal (prod, disarankan):**
   - tabel target (dan tabel yang disebut di statement) di-stream dari database target ke SQLite lokal
     `%LOCALAPPDATA%\AIThread\db-rehearsal\<thread>.db`, per 10.000 baris per transaksi dengan cek ruang disk, tanpa
     batas baris;
   - versi SQLite dari statement (`sqlite_sql`, ditulis planner) dijalankan di sana;
   - hasilnya ditampilkan: jumlah baris terpengaruh dan contoh baris sebelum/sesudah;
   - lalu kartu izin muncul lagi: eksekusi sekarang / tolak.

   File latihan langsung dihapus setelah latihan, jadi data salinan tidak tertinggal di disk. Kartu menandai bahwa
   latihan di SQLite hanya **mendekati** perilaku database target, karena dialek dan tipenya berbeda.
7. **Eksekusi:**
   - di transaksi yang sama, baris yang dicakup backup harus masih sama dengan checksum backup; bila tidak, tidak ada
     yang dijalankan;
   - statement dijalankan dalam transaksi;
   - untuk `Insert`, kunci baru (kunci sesudah dikurangi kunci sebelum) ditulis ke `<backup>.added` sebelum COMMIT; bila
     jumlahnya bukan jumlah baris terpengaruh (sesi lain ikut menyisipkan), rollback;
   - bila jumlah baris terpengaruh berbeda dari dry test, transaksi di-rollback dan alur berhenti;
   - bila sama, COMMIT;
   - bila ada error sebelum COMMIT, transaksi di-rollback;
   - backup tetap disimpan dan bisa dipulihkan dari Project → Detail → *Database* (dengan konfirmasi).

## Tool
| Tool | Siapa | Catatan |
|---|---|---|
| `list_databases` | planner, tools node | nama, provider, environment, kolom yang disamarkan |
| `describe_table` | planner, tools node, tester | kolom, tipe, primary key |
| `query_database` | planner, tools node, tester | baca saja |
| `change_database` | planner | alur di atas; tidak bisa didelegasikan |

## Batasan yang diketahui
- Trigger atau cascade yang mengubah **tabel lain** tidak terdeteksi (hanya tabel target yang dibandingkan).
- Antara cek "baris masih sama dengan backup" dan statement di eksekusi ada jeda kecil di isolasi READ COMMITTED.
- Dry test `Rows`/`Insert` membaca seluruh tabel dua kali (checksum sebelum/sesudah). Backup dan restore-nya tetap
  kecil, tetapi dry test di tabel sangat besar butuh waktu; lamanya ditampilkan di kartu.
- Backup format 1 (satu JSON, sebelum backup di-stream) ditandai tidak didukung dan tidak bisa dipulihkan otomatis.

## Di luar cakupan tahap ini
- Backup native per engine (`BACKUP DATABASE`, `pg_dump`, …).
- DDL.
- Latihan dengan engine yang sama di lokal.
- Berbagi koneksi lewat profil setup tim (koneksi berisi password, jadi tetap per mesin).
