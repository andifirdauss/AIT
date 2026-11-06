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
2. **Backup = titik restore (wajib).** Isi tabel target dibaca lewat koneksi yang sama, lalu disimpan terkompresi dan
   terenkripsi di `%LOCALAPPDATA%\AIThread\db-backups`.
   - Isinya kolom, primary key, baris, dan checksum per baris.
   - Bila tabel melebihi batas (bawaan 50.000 baris), AIT menolak dan menyarankan backup manual oleh DBA.
   - **Selama backup belum berhasil, tidak ada eksekusi apa pun.**
3. **Cek file backup.** File dibuka lagi, didekripsi, lalu jumlah baris dan checksum-nya dicocokkan.
4. **Dry test + test restore, dalam satu transaksi yang selalu di-rollback:**
   - statement dijalankan, dan jumlah baris yang terpengaruh dicatat;
   - restore dari backup dijalankan per primary key: baris yang berubah dikembalikan, yang terhapus disisipkan
     lagi, yang baru dihapus;
   - isi tabel dicocokkan dengan backup lewat checksum;
   - lalu **ROLLBACK**, sehingga database tidak berubah sama sekali.

   Bila dry test atau test restore gagal, alur berhenti dan tidak ada eksekusi.
5. **Izin developer** (kartu izin). Kartu menampilkan environment, statement, jumlah baris terpengaruh, id backup, dan
   hasil test restore.
   - **Dev/Staging:** 1 sekali / 2 selalu untuk koneksi ini / 3 tolak.
   - **Prod:** 1 latihan lokal dulu / 2 eksekusi sekarang / 3 tolak. Tidak ada "selalu".
6. **Latihan lokal (prod, disarankan):**
   - tabel target (dan tabel yang disebut di statement) ditarik dari database target ke SQLite lokal
     `%LOCALAPPDATA%\AIThread\db-rehearsal\<thread>.db`;
   - versi SQLite dari statement (`sqlite_sql`, ditulis planner) dijalankan di sana;
   - hasilnya ditampilkan: jumlah baris terpengaruh dan contoh baris sebelum/sesudah;
   - lalu kartu izin muncul lagi: eksekusi sekarang / tolak.

   File latihan langsung dihapus setelah latihan, jadi data salinan tidak tertinggal di disk. Kartu menandai bahwa
   latihan di SQLite hanya **mendekati** perilaku database target, karena dialek dan tipenya berbeda.
7. **Eksekusi:**
   - statement dijalankan dalam transaksi;
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

## Di luar cakupan tahap ini
- Backup native per engine (`BACKUP DATABASE`, `pg_dump`, …).
- DDL.
- Latihan dengan engine yang sama di lokal.
- Berbagi koneksi lewat profil setup tim (koneksi berisi password, jadi tetap per mesin).
