# 1. Instalasi

## Prasyarat

| Komponen | Wajib | Cek | Catatan |
|---|---|---|---|
| .NET SDK 10 | Ya | `dotnet --list-sdks` | Solusi menargetkan `net10.0`. |
| SQL Server | Ya | `sqllocaldb info` | Default memakai LocalDB `(localdb)\MSSQLLocalDB` (terpasang bersama Visual Studio). |
| Redis / Docker | Tidak | — | Short memory memakai Garnet yang berjalan di dalam AiThread (lihat langkah 1). |
| Node.js + 9router | Ya | `9router --help` | Sumber semua model AI. |
| Git | Disarankan | `git --version` | Executor menulis langsung ke file project; git memudahkan meninjau/membatalkan. |

## 1. Short memory (Garnet)

Tidak ada yang perlu di-install. AiThread menjalankan [Garnet](https://microsoft.github.io/garnet/) (server
protokol Redis dari Microsoft) di dalam prosesnya sendiri, di port dari `ConnectionStrings:Redis` (default
`localhost:6379`) dan hanya bisa diakses dari komputer ini. Tambahan RAM ±30 MB.

- Data: `%LOCALAPPDATA%\AiThread\garnet\<port>`. Setiap penulisan dicatat ke *append-only file* sebelum
  dikonfirmasi, jadi short memory tidak hilang walau AiThread dimatikan paksa atau crash. Checkpoint tiap jam
  memangkas file itu.
- Instance lain (mis. untuk uji coba) cukup memakai port lain di `ConnectionStrings:Redis`; data-nya otomatis
  terpisah.
- Bila port itu sudah dipakai program lain (mis. container Docker `aithread-redis` lama), Garnet tidak dijalankan
  dan AiThread memakai server yang sudah ada di port itu (tercatat sebagai peringatan di log).
- Redis eksternal: set `AiThread:EmbeddedRedis:Enabled` = `false` dan arahkan `ConnectionStrings:Redis` ke server
  tersebut (format StackExchange.Redis, mis. `myhost:6379,password=rahasia`).

**Pindah dari Docker Redis.** Short memory yang masih ada di container lama tidak dipindahkan otomatis. Sebelum
mematikan container, buka menu **Memory** dan klik **→ DB** pada short memory yang ingin disimpan (atau biarkan
container hidup ±24 jam agar worker memindahkannya). Setelah itu: `docker rm -f aithread-redis` dan
`docker volume rm ai-multi-agent_aithread-redis` (cek nama volume dengan `docker volume ls`).

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

## 5. Menjalankan di background

`dotnet run` dan Visual Studio hanya menjalankan AiThread selama terminal/debugger-nya terbuka. Di Visual Studio,
menutup browser yang dibuka debugger juga menghentikan aplikasi. Untuk pemakaian sehari-hari (Claude Code, Telegram),
jalankan AiThread di background agar tetap hidup walau browser, terminal, dan Visual Studio ditutup:

```powershell
.\scripts\aithread.ps1 start          # publish Release lalu jalankan tanpa jendela di http://localhost:5225
.\scripts\aithread.ps1 status         # PID, sejak kapan, lokasi log, status autostart
.\scripts\aithread.ps1 logs -Follow   # ikuti log (Ctrl+C untuk keluar)
.\scripts\aithread.ps1 restart        # setelah kode berubah: stop, publish ulang, start
.\scripts\aithread.ps1 stop           # matikan dengan rapi
.\scripts\aithread.ps1 autostart-on   # jalankan otomatis saat login Windows (autostart-off untuk menghapus)
```

Tambahkan `-Open` pada `start`/`restart` untuk langsung membuka dashboard. Bila PowerShell menolak menjalankan
script (*running scripts is disabled on this system*), pakai `scripts\aithread.cmd` dengan argumen yang sama.

Port 5225 hanya bisa dipakai satu instance: hentikan dulu instance dari Visual Studio / `dotnet run`. `start` menolak
bila port sudah dipakai dan menyebutkan prosesnya.

| Hal | Keterangan |
|---|---|
| Lokasi aplikasi | `%LOCALAPPDATA%\AiThread\app` (hasil `dotnet publish -c Release`). Build memakai folder sendiri (`%LOCALAPPDATA%\AiThread\build`), jadi tidak bentrok dengan `bin/` yang sedang dikunci Visual Studio. Bila publish gagal, versi lama tetap dipakai. |
| Log | `%LOCALAPPDATA%\AiThread\logs\aithread-<waktu>.log` (dan `.err.log`); 10 run terakhir disimpan. Log aktivitas thread tetap ada di Dashboard. |
| Konfigurasi | `appsettings.json` ikut ter-publish: ubah `src/AiThread.UI/appsettings.json` lalu `restart`. Environment `Production`. Setting dari UI (database) tetap sama dengan saat development. |
| Port lain | `-Port 6000` — pakai `-Port` yang sama untuk semua perintah, lalu terapkan ulang integrasi Claude Code. |
| Autostart | Task Scheduler `AiThread`, 30 detik setelah login, berjalan sebagai akun Anda sendiri (tanpa admin). Karena itu LocalDB, API key 9router yang terenkripsi, dan login Claude Code CLI tetap berlaku. Memakai hasil publish terakhir tanpa build ulang. |
| Stop | Lewat `POST /api/system/shutdown`: thread yang sedang berjalan dibatalkan dan dicatat (maks. ±15 detik), lalu proses keluar. Bila tidak berhenti dalam 45 detik, proses dihentikan paksa. |
| Mati mendadak | Logoff, crash, atau dihentikan paksa: saat start berikutnya, thread yang tertinggal ditandai *Cancelled* dengan pesan *AiThread berhenti saat thread ini berjalan.* |

9router tetap dijalankan terpisah dan harus hidup; AiThread menyambung ulang otomatis bila 9router baru menyala
setelah AiThread. Short memory (Garnet) ikut berjalan di dalam proses AiThread.

## 6. AiThread.exe standalone (tanpa .NET)

Untuk dipakai tanpa .NET SDK atau `dotnet run`, buat `AiThread.exe` self-contained (Windows x64, ±60 MB, satu berkas):

```powershell
.\scripts\publish-exe.ps1 -Zip     # hasil: dist\AiThread\ dan dist\AiThread-win-x64.zip
```

Folder `dist\AiThread` berisi `AiThread.exe`, `appsettings.json` (boleh diubah, mis. connection string),
`wwwroot\` dan `docs\`; salin seluruh folder ke komputer lain. SQL Server (LocalDB) dan 9router tetap diperlukan.
Jalankan `AiThread.exe` tanpa argumen untuk **menu interaktif**; server berjalan di jendela itu, lognya ke file:

| Menu | Fungsi |
|---|---|
| 1 Web UI | Buka dashboard di browser. |
| 2 Sembunyikan ke Tray | Pindahkan ke background dengan ikon di system tray; jendela terminal boleh ditutup. Thread yang sedang berjalan dibatalkan dan dicatat, karena server dijalankan ulang sebagai proses tanpa jendela (±10 detik). |
| 3 Log langsung | Ikuti log server; tombol apa pun kembali ke menu. |
| 4 Restart server | Terapkan `appsettings.json` yang baru diubah. |
| 5 Ganti port | Disimpan di `%LOCALAPPDATA%\AiThread\cli.json`. Terapkan ulang integrasi Claude Code karena alamatnya berubah. |
| 6 Autostart | Jalankan tray otomatis saat login Windows (kunci `Run` milik pengguna, tanpa admin). |
| 0 Keluar | Matikan server dengan rapi. |

Bila sudah ada instance yang berjalan di port itu (mis. di tray), menu mengendalikan instance tersebut: buka Web UI,
lihat log terakhir, atau matikan. Klik kanan ikon tray untuk *Buka Web UI*, *Lihat log*, *Buka folder log*,
*Jalankan saat login Windows*, dan *Keluar*; klik ganda membuka Web UI.

Perintah tanpa menu (`AiThread.exe help` menampilkan semuanya):

```powershell
AiThread.exe serve            # di jendela ini, log tampil di konsol (Ctrl+C untuk berhenti)
AiThread.exe tray --open      # background dengan ikon tray, lalu buka dashboard
AiThread.exe status           # PID, sejak kapan, lokasi log, status autostart
AiThread.exe logs -f          # ikuti log terbaru
AiThread.exe stop             # matikan dengan rapi
AiThread.exe autostart on     # atau off
AiThread.exe serve --port 6000
```

Port default 5225 (atau yang terakhir disimpan dari menu); `--port` berlaku untuk satu perintah. Log ada di
`%LOCALAPPDATA%\AiThread\logs` (folder yang sama dengan `scripts\aithread.ps1`). Dengan `--urls`/`--environment`
atau tanpa terminal (mis. `dotnet run`, Visual Studio), exe berjalan sebagai host biasa tanpa menu.

Autostart exe (kunci `Run`) dan `scripts\aithread.ps1 autostart-on` (Task Scheduler) tidak perlu diaktifkan bersamaan;
instance kedua mendeteksi yang pertama dan hanya membuka dashboard.

## 7. Verifikasi

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
| `Telegram:ApiBaseUrl` | `https://api.telegram.org` | Endpoint Bot API untuk [prompt dari Telegram](11-telegram.md) (token bot diatur di menu Setting). |
| `Telegram:PollTimeoutSeconds` | `50` | Lama satu long poll `getUpdates` menunggu pesan baru (1–50). |
| `EmbeddedRedis:Enabled` | `true` | Jalankan Garnet di dalam AiThread untuk short memory. `false` = pakai Redis eksternal di `ConnectionStrings:Redis`. |
| `EmbeddedRedis:DataDirectory` | *(kosong)* | Lokasi data Garnet. Kosong = `%LOCALAPPDATA%\AiThread\garnet\<port>`. |
| `EmbeddedRedis:MemorySize` | `16m` | Batas RAM log Garnet. Record tertua di luar batas ini dibuang, jadi jangan diperkecil (satu short memory ±1 KB). |
| `EmbeddedRedis:CheckpointIntervalMinutes` | `60` | Interval checkpoint yang memangkas *append-only file*. |

Restart aplikasi setelah mengubah `appsettings.json`.

Lanjut ke [Konfigurasi](02-konfigurasi.md).
