# 1. Instalasi

## Prasyarat

| Komponen | Wajib | Cek | Catatan |
|---|---|---|---|
| .NET SDK 10 | Ya | `dotnet --list-sdks` | Solusi menargetkan `net10.0`. |
| SQL Server | Ya | `sqllocaldb info` | Default memakai LocalDB `(localdb)\MSSQLLocalDB` (terpasang bersama Visual Studio). |
| Redis / Docker | Tidak | — | Short memory memakai Garnet yang berjalan di dalam AIT (lihat langkah 1). |
| Node.js + 9router | Ya | `node --version` | Sumber semua model AI. Node.js 18+ wajib; 9router ikut terpasang lewat npm `aithread` dan dinyalakan AIT (lihat langkah 2). |
| Git | Disarankan | `git --version` | Executor menulis langsung ke file project; git memudahkan meninjau/membatalkan. |

## 1. Short memory (Garnet)

Tidak ada yang perlu di-install. AIT menjalankan [Garnet](https://microsoft.github.io/garnet/) (server
protokol Redis dari Microsoft) di dalam prosesnya sendiri, di port dari `ConnectionStrings:Redis` (default
`localhost:6379`) dan hanya bisa diakses dari komputer ini. Tambahan RAM ±30 MB.

- Data: `%LOCALAPPDATA%\AIThread\garnet\<port>`. Setiap penulisan dicatat ke *append-only file* sebelum
  dikonfirmasi, jadi short memory tidak hilang walau AIT dimatikan paksa atau crash. Checkpoint tiap jam
  memangkas file itu.
- Instance lain (mis. untuk uji coba) cukup memakai port lain di `ConnectionStrings:Redis`; data-nya otomatis
  terpisah.
- Bila port itu sudah dipakai program lain (mis. container Docker `aithread-redis` lama), Garnet tidak dijalankan
  dan AIT memakai server yang sudah ada di port itu (tercatat sebagai peringatan di log).
- Redis eksternal: set `AIThread:EmbeddedRedis:Enabled` = `false` dan arahkan `ConnectionStrings:Redis` ke server
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

Langkah di atas opsional bila AIT dipasang lewat npm (`npm i -g aithread`): paket itu bergantung pada `9router`, dan
saat start AIT menyalakan 9router sendiri bila belum berjalan.

| Perilaku | Penjelasan |
|---|---|
| Kapan dinyalakan | Sekali saat AIT start, bila 9router *Offline* dan Base URL di Setting alamat lokal (`localhost`, `127.0.0.1`, `[::1]`). Port diambil dari URL itu. Bila 9router mati belakangan, AIT hanya melaporkannya (mungkin sengaja ditutup); klik **Jalankan 9router** di Dashboard untuk menyalakan ulang. |
| 9router mana | Urutan: `NineRouter:CliPath` → 9router global npm (`npm i -g 9router`, biasanya versi terbaru) → salinan di dalam paket `aithread`. Jadi menghapus 9router global tidak mematikan fitur ini. |
| Cara jalan | `node cli.js --tray --no-browser --skip-update -p <port>`, sama dengan *Hide to Tray* milik 9router: tanpa jendela, ikon tray 9router muncul. Output ke `%LOCALAPPDATA%\AIThread\logs\9router.log`. Tetap hidup walau AIT berhenti, kecuali `NineRouter:StopOnExit` = `true`. |
| Port terpakai | Saat start, 9router mematikan proses lain di port-nya. Karena itu AIT tidak menyalakan 9router bila port itu sudah dipakai program lain yang tidak menjawab sebagai 9router. |
| Belum terpasang | Tidak ada 9router sama sekali (mis. exe standalone tanpa npm): tombol **Jalankan 9router** di Dashboard menjalankan `npm install -g 9router` lalu menyalakannya. Saat start, pemasangan otomatis hanya bila `NineRouter:AutoInstall` = `true` (sekali per proses). Butuh Node.js + npm dan internet. |

1. Buka dashboard http://localhost:20128.
2. Hubungkan minimal satu provider (menu *Providers*). Idealnya satu provider kuat untuk planner dan satu yang
   murah/gratis untuk executor.
3. (Opsional) Buat *combo* agar model otomatis fallback ke provider lain saat limit habis.
4. Salin **API key** dari dashboard 9router bila opsi *require API key* aktif (default aktif).

AIT memakai endpoint 9router berikut: `GET /api/health`, `GET /v1/models`, `POST /v1/chat/completions`,
`POST /v1/messages`, `POST /v1/messages/count_tokens`. Untuk *Sisa kuota* di footer jawaban AIT juga membaca API
dashboard 9router (`GET /api/combos`, `/api/providers/client`, `/api/usage/{id}`); API ini hanya terbuka bila
*Require login* di Settings 9router dimatikan (bawaan 9router: aktif). Tanpa itu footer tampil tanpa kuota.

## 3. Database

Tidak perlu membuat database manual — migrasi EF Core diterapkan otomatis saat aplikasi start.

Untuk memakai SQL Server selain LocalDB, ubah `ConnectionStrings:Default` di
`src/AIThread.UI/appsettings.json`, contoh:

```json
"Default": "Server=localhost;Database=AIT;User Id=aithread;Password=...;TrustServerCertificate=True"
```

## 4. Menjalankan AIT

```powershell
dotnet run --project src/AIThread.UI --launch-profile http
```

Buka http://localhost:5225. Dari Visual Studio, profil `https` juga membuka `http://localhost:5225` — **pakai alamat
http** untuk Claude Code, karena Claude Code (Node.js) tidak mempercayai sertifikat development ASP.NET Core.

Mengganti port:

```powershell
dotnet run --project src/AIThread.UI --no-launch-profile --urls http://localhost:6000
```

Setelah port berubah, matikan lalu nyalakan lagi switch CLI yang aktif di menu **CLI** agar URL baru tertulis
(lihat [Integrasi Claude Code](03-integrasi-claude-code.md)).

## 5. Menjalankan di background

`dotnet run` dan Visual Studio hanya menjalankan AIT selama terminal/debugger-nya terbuka. Di Visual Studio,
menutup browser yang dibuka debugger juga menghentikan aplikasi. Untuk pemakaian sehari-hari (Claude Code, Telegram),
jalankan AIT di background agar tetap hidup walau browser, terminal, dan Visual Studio ditutup:

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
| Lokasi aplikasi | `%LOCALAPPDATA%\AIThread\app` (hasil `dotnet publish -c Release`). Build memakai folder sendiri (`%LOCALAPPDATA%\AIThread\build`), jadi tidak bentrok dengan `bin/` yang sedang dikunci Visual Studio. Bila publish gagal, versi lama tetap dipakai. |
| Log | `%LOCALAPPDATA%\AIThread\logs\aithread-<waktu>.log` (dan `.err.log`); 10 run terakhir disimpan. Log aktivitas thread tetap ada di Dashboard. Satu baris per entri (`jam level kategori: pesan`); warning hanya menampilkan pesan error (`→ Tipe: pesan`) tanpa stack trace, dan warning yang sama persis dalam 5 menit dicetak sekali lalu diberi catatan *Nx serupa disembunyikan*. Error tetap dengan stack trace lengkap. |
| Konfigurasi | `appsettings.json` ikut ter-publish: ubah `src/AIThread.UI/appsettings.json` lalu `restart`. Environment `Production`. Setting dari UI (database) tetap sama dengan saat development. |
| Port lain | `-Port 6000` — pakai `-Port` yang sama untuk semua perintah, lalu terapkan ulang integrasi Claude Code. |
| Autostart | Task Scheduler `AIThread`, 30 detik setelah login, berjalan sebagai akun Anda sendiri (tanpa admin). Karena itu LocalDB dan API key 9router yang terenkripsi tetap berlaku. Memakai hasil publish terakhir tanpa build ulang. |
| Stop | Lewat `POST /api/system/shutdown`: thread yang sedang berjalan dibatalkan dan dicatat (maks. ±15 detik), lalu proses keluar. Bila tidak berhenti dalam 45 detik, proses dihentikan paksa. |
| Mati mendadak | Logoff, crash, atau dihentikan paksa: saat start berikutnya, thread yang tertinggal ditandai *Cancelled* dengan pesan *AIT berhenti saat thread ini berjalan.* |

9router berjalan sebagai proses terpisah. AIT menyalakannya saat start bila belum hidup (lihat langkah 2), dan
menyambung ulang otomatis bila 9router baru menyala setelah AIT. Short memory (Garnet) ikut berjalan di dalam proses AIT.

## 6. AIThread.exe standalone (tanpa .NET)

Untuk dipakai tanpa .NET SDK atau `dotnet run`, buat `AIThread.exe` self-contained (Windows x64, ±60 MB, satu berkas):

```powershell
.\scripts\publish-exe.ps1 -Zip     # hasil: dist\AIThread\ dan dist\AIThread-win-x64.zip
```

Folder `dist\AIThread` berisi `AIThread.exe`, `appsettings.json` (boleh diubah, mis. connection string),
`wwwroot\` dan `docs\`; salin seluruh folder ke komputer lain. SQL Server (LocalDB) dan Node.js tetap diperlukan;
9router dipasang dengan `npm i -g 9router` atau tombol **Jalankan 9router** di Dashboard.
Jalankan `AIThread.exe` tanpa argumen untuk **menu interaktif**; server berjalan di jendela itu, lognya ke file:

| Menu | Fungsi |
|---|---|
| 1 Web UI | Buka dashboard di browser. |
| 2 Sembunyikan ke Tray | Pindahkan ke background dengan ikon di system tray; jendela terminal boleh ditutup. Thread yang sedang berjalan dibatalkan dan dicatat, karena server dijalankan ulang sebagai proses tanpa jendela (±10 detik). |
| 3 Log langsung | Ikuti log server; tombol apa pun kembali ke menu. |
| 4 Restart server | Terapkan `appsettings.json` yang baru diubah. |
| 5 Ganti port | Disimpan di `%LOCALAPPDATA%\AIThread\cli.json`. Terapkan ulang integrasi Claude Code karena alamatnya berubah. |
| 6 Autostart | Jalankan tray otomatis saat login Windows (kunci `Run` milik pengguna, tanpa admin). |
| 0 Keluar | Matikan server dengan rapi. |

Bila sudah ada instance yang berjalan di port itu (mis. di tray), menu mengendalikan instance tersebut: buka Web UI,
lihat log terakhir, atau matikan. Klik kanan ikon tray untuk *Buka Web UI*, *Lihat log*, *Buka folder log*,
*Jalankan saat login Windows*, dan *Keluar*; klik ganda membuka Web UI.

Perintah tanpa menu (`AIThread.exe help` menampilkan semuanya):

```powershell
AIThread.exe serve            # di jendela ini, log tampil di konsol (Ctrl+C untuk berhenti)
AIThread.exe tray --open      # background dengan ikon tray, lalu buka dashboard
AIThread.exe status           # PID, sejak kapan, lokasi log, status autostart
AIThread.exe logs -f          # ikuti log terbaru
AIThread.exe stop             # matikan dengan rapi
AIThread.exe autostart on     # atau off
AIThread.exe setup            # buka halaman Setup di browser
AIThread.exe setup export tim.json   # profil setup tim, tanpa API key dan password
AIThread.exe setup import tim.json   # pratinjau, tanya API key/password, lalu terapkan (--yes = tanpa konfirmasi)
AIThread.exe serve --port 6000
```

Perintah `setup` memakai instance yang sedang berjalan (jalankan `tray` dulu). Isi dan aturan profil ada di
[Profil setup tim](02-konfigurasi.md#profil-setup-tim).

Port default 5225 (atau yang terakhir disimpan dari menu); `--port` berlaku untuk satu perintah. Log ada di
`%LOCALAPPDATA%\AIThread\logs` (folder yang sama dengan `scripts\aithread.ps1`). Dengan `--urls`/`--environment`
atau tanpa terminal (mis. `dotnet run`, Visual Studio), exe berjalan sebagai host biasa tanpa menu.

Autostart exe (kunci `Run`) dan `scripts\aithread.ps1 autostart-on` (Task Scheduler) tidak perlu diaktifkan bersamaan;
instance kedua mendeteksi yang pertama dan hanya membuka dashboard.

### Lewat npm

`.\scripts\publish-npm.ps1` membuat dua paket di `dist\npm`: `aithread-win-x64` (berisi `AIThread.exe`, ±60 MB) dan
`aithread` (launcher kecil, `optionalDependencies` ke paket platform, `dependencies` ke `9router`). Pasang dengan
`npm i -g dist\npm\aithread-win-x64-<versi>.tgz dist\npm\aithread-<versi>.tgz`, lalu jalankan `aithread` (perintah dan
opsinya sama dengan exe). `-Publish` menjalankan `npm publish` untuk kedua paket; versi diambil dari `<Version>` di
`AIThread.UI.csproj`. Hanya Windows x64.

Karena folder `node_modules` ditimpa saat update, ubah konfigurasi di `%LOCALAPPDATA%\AIThread\appsettings.json` (opsional,
isi hanya kunci yang diganti); file ini menimpa `appsettings.json` di samping exe, juga untuk exe hasil `publish-exe.ps1`.

## 7. Verifikasi

Buka menu **Setup**: tiap langkah menampilkan statusnya dari kondisi AIT saat itu, dan langkah yang belum selesai bisa
langsung dikerjakan di sana (lihat [Panduan menu → Setup](05-menu.md#setup)). Di **Dashboard**, empat kartu status teratas harus:

| Kartu | Kondisi normal |
|---|---|
| 9router | *Online* · latency · jumlah model & combo · *API key OK* |
| Short memory · Redis | *Terhubung* |
| Database | *Terhubung* |
| Pipeline node | *Siap* (setelah node dikonfigurasi di langkah berikutnya) |

## Konfigurasi `appsettings.json`

Bagian `AIThread` mengatur hal-hal infrastruktur (pengaturan runtime lain ada di menu Setting):

| Kunci | Default | Fungsi |
|---|---|---|
| `ShortMemoryHours` | `24` | Umur short memory sebelum dipindah ke database. |
| `MemoryConsolidationIntervalMinutes` | `10` | Interval worker yang memindahkan short → long memory. |
| `MemoryRelabelIntervalMinutes` | `10` | Interval worker yang memberi label berstruktur ke memory lama dan mengecek modul ganda (lihat [Memory](06-memory.md#migrasi-label-memory-lama)). |
| `MemoryAcronyms` | `[]` | Singkatan tambahan yang ditulis kapital di label memory, mis. `["SKU", "GL"]` (API, DTO, UI, dll. sudah bawaan). |
| `RouterMonitorIntervalSeconds` | `15` | Interval cek kesehatan 9router untuk dashboard. |
| `ActivityLogRetentionDays` | `14` | Log dashboard yang lebih tua dihapus otomatis. |
| `ExecutorFullFileMaxLines` | `400` | File sampai sepanjang ini dikirim utuh ke executor; file lebih besar dikirim sebagai potongan. |
| `Telegram:ApiBaseUrl` | `https://api.telegram.org` | Endpoint Bot API untuk [prompt dari Telegram](11-telegram.md) (token bot diatur di menu Setting). |
| `Telegram:PollTimeoutSeconds` | `50` | Lama satu long poll `getUpdates` menunggu pesan baru (1–50). |
| `EmbeddedRedis:Enabled` | `true` | Jalankan Garnet di dalam AIT untuk short memory. `false` = pakai Redis eksternal di `ConnectionStrings:Redis`. |
| `EmbeddedRedis:DataDirectory` | *(kosong)* | Lokasi data Garnet. Kosong = `%LOCALAPPDATA%\AIThread\garnet\<port>`. |
| `EmbeddedRedis:MemorySize` | `16m` | Batas RAM log Garnet. Record tertua di luar batas ini dibuang, jadi jangan diperkecil (satu short memory ±1 KB). |
| `EmbeddedRedis:CheckpointIntervalMinutes` | `60` | Interval checkpoint yang memangkas *append-only file*. |
| `NineRouter:AutoStart` | `true` | Nyalakan 9router lokal saat AIT start bila belum berjalan (lihat [langkah 2](#2-9router)). |
| `NineRouter:AutoInstall` | `false` | Saat start, jalankan `npm install -g 9router` bila 9router tidak ditemukan. Tombol *Jalankan 9router* selalu boleh memasang. |
| `NineRouter:StopOnExit` | `false` | Hentikan 9router yang dinyalakan AIT ketika AIT berhenti. 9router yang Anda jalankan sendiri tidak pernah dihentikan. |
| `NineRouter:StartTimeoutSeconds` | `90` | Batas tunggu 9router menjawab setelah dinyalakan (start pertama ikut menyiapkan runtime SQLite-nya). |
| `NineRouter:InstallTimeoutSeconds` | `300` | Batas waktu `npm install -g 9router`. |
| `NineRouter:NodePath` / `NineRouter:CliPath` | *(kosong)* | Path `node.exe` / `cli.js` 9router tertentu. Kosong = cari di PATH, folder global npm, dan paket `aithread`. |

Restart aplikasi setelah mengubah `appsettings.json`.

Lanjut ke [Konfigurasi](02-konfigurasi.md).
