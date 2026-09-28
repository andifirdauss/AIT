# 1. Instalasi

## Prasyarat

| Komponen | Wajib | Cek | Catatan |
|---|---|---|---|
| .NET SDK 10 | Ya | `dotnet --list-sdks` | Solusi menargetkan `net10.0`. |
| SQL Server | Ya | `sqllocaldb info` | Default memakai LocalDB `(localdb)\MSSQLLocalDB` (terpasang bersama Visual Studio). |
| Redis | Disarankan | `docker ps` | Untuk short memory. Tanpa Redis aplikasi tetap jalan; memory langsung disimpan ke database. |
| Node.js + 9router | Ya | `9router --help` | Sumber semua model AI. |
| Git | Disarankan | `git --version` | Executor menulis langsung ke file project; git memudahkan meninjau/membatalkan. |

## 1. Redis

Dari root repo:

```powershell
docker compose up -d redis
```

`docker-compose.yml` menjalankan `redis:7-alpine` di port `6379` dengan persistensi *append-only*, sehingga
short memory tidak hilang saat container di-restart. Untuk Redis lain, ubah `ConnectionStrings:Redis`
(format StackExchange.Redis, mis. `myhost:6379,password=rahasia`).

## 2. 9router

```powershell
npm install -g 9router
9router --no-browser        # atau cukup: 9router
```

1. Buka dashboard http://localhost:20128.
2. Hubungkan minimal satu provider (menu *Providers*). Idealnya satu provider kuat untuk planner dan satu yang
   murah/gratis untuk executor.
3. (Opsional) Buat *combo* agar model otomatis fallback ke provider lain saat limit habis.
4. Salin **API key** dari dashboard 9router bila opsi *require API key* aktif (default aktif).

AiThread memakai endpoint 9router berikut: `GET /api/health`, `GET /v1/models`, `POST /v1/chat/completions`,
`POST /v1/messages`, `POST /v1/messages/count_tokens`.

## 3. Database

Tidak perlu membuat database manual — migrasi EF Core diterapkan otomatis saat aplikasi start.

Untuk memakai SQL Server selain LocalDB, ubah `ConnectionStrings:Default` di
`src/AiThread.UI/appsettings.json`, contoh:

```json
"Default": "Server=localhost;Database=AiThread;User Id=aithread;Password=...;TrustServerCertificate=True"
```

## 4. Menjalankan AiThread

```powershell
dotnet run --project src/AiThread.UI --launch-profile http
```

Buka http://localhost:5225. Dari Visual Studio, profil `https` juga membuka `http://localhost:5225` — **pakai alamat
http** untuk Claude Code, karena Claude Code (Node.js) tidak mempercayai sertifikat development ASP.NET Core.

Mengganti port:

```powershell
dotnet run --project src/AiThread.UI --no-launch-profile --urls http://localhost:6000
```

Setelah port berubah, terapkan ulang integrasi Claude Code (lihat [Integrasi Claude Code](03-integrasi-claude-code.md)).

## 5. Verifikasi

Buka **Dashboard**. Empat kartu status teratas harus:

| Kartu | Kondisi normal |
|---|---|
| 9router | *Online* · latency · jumlah model & combo · *API key OK* |
| Short memory · Redis | *Terhubung* |
| Database | *Terhubung* |
| Pipeline node | *Siap* (setelah node dikonfigurasi di langkah berikutnya) |

## Konfigurasi `appsettings.json`

Bagian `AiThread` mengatur hal-hal infrastruktur (pengaturan runtime lain ada di menu Setting):

| Kunci | Default | Fungsi |
|---|---|---|
| `ShortMemoryHours` | `24` | Umur short memory sebelum dipindah ke database. |
| `MemoryConsolidationIntervalMinutes` | `10` | Interval worker yang memindahkan short → long memory. |
| `RouterMonitorIntervalSeconds` | `15` | Interval cek kesehatan 9router untuk dashboard. |
| `ActivityLogRetentionDays` | `14` | Log dashboard yang lebih tua dihapus otomatis. |
| `ExecutorFullFileMaxLines` | `400` | File sampai sepanjang ini dikirim utuh ke executor; file lebih besar dikirim sebagai potongan. |

Restart aplikasi setelah mengubah `appsettings.json`.

Lanjut ke [Konfigurasi](02-konfigurasi.md).
