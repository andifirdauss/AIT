# AiThread

Proxy untuk Claude Code yang memecah satu permintaan menjadi tugas-tugas sangat kecil:
**planner** (model paling cerdas) merencanakan, banyak **executor** (model murah) mengerjakan secara paralel,
lalu **wrapper** merangkum hasilnya untuk user. Semua model diakses lewat [9router](https://github.com/decolua/9router).

> **Panduan lengkap pemakaian ada di [docs/](docs/README.md)** — instalasi, konfigurasi, integrasi Claude Code,
> penggunaan sehari-hari, memory, cara kerja, troubleshooting, dan referensi API.

```
Claude Code ──(ANTHROPIC_BASE_URL)──► AiThread /v1/messages
                                          │
                  ┌───────────────────────┴───────────────────────┐
                  │ Planner (tool loop, read-only)                 │
                  │  list_directory · find_files · search_text     │
                  │  read_file · search_memory · read_memory       │
                  │  └─► submit_plan: summary + KONTRAK + tugas    │
                  └───────────────────────┬───────────────────────┘
                     ┌──────────┬─────────┼─────────┬──────────┐
                     ▼          ▼         ▼         ▼          ▼     (paralel, DAG dependsOn)
                 Executor   Executor  Executor  Executor   Executor  1 tugas = 1 file
                 (view)     (js)      (api)     (service)  (sql)     SEARCH/REPLACE, retry bila gagal
                     └──────────┴─────────┼─────────┴──────────┘
                                          ▼
                               Wrapper → balasan ke Claude Code (streaming)
                                          ▼
                          Memory extractor → short memory (Garnet)
```

## Kenapa akurat walau konteks executor kecil

Masalah klasik multi-agent: executor UI membuat `id="description"` sementara executor JS membaca `#txtDesc`,
atau executor JS membuat file baru karena tidak tahu file yang sudah ada. AiThread mencegahnya dengan:

1. **Planner wajib membaca kode dulu** (tool read-only) sebelum `submit_plan`.
2. **Kontrak**: semua nama yang dipakai lintas file (id elemen, properti DTO, parameter API, kolom DB, pesan validasi)
   ditetapkan sekali oleh planner dan dikirim ke setiap executor. Karena itu tugas bisa independen dan berjalan paralel.
3. **Validator rencana**: file `edit` harus ada, file `create` belum ada, path tidak boleh keluar root atau masuk
   `bin/ obj/ wwwroot/lib/`, `dependsOn` tanpa siklus. Rencana yang salah dikembalikan ke planner untuk diperbaiki.
4. **Executor hanya menerima**: satu file target (utuh, atau potongan di sekitar `focus` untuk file besar), instruksi,
   kontrak, dan potongan referensi dari file lain yang dipilih planner.
5. **Edit diterapkan terhadap isi file terkini** di bawah lock per file (tugas paralel di file yang sama tetap aman),
   all-or-nothing, dan kegagalan SEARCH dikembalikan ke executor untuk dicoba ulang.

Waktu eksekusi = rantai tugas terlama, bukan jumlah semua tugas. Halaman detail thread menampilkan timeline paralel
serta perbandingan "waktu nyata" vs "jika sekuensial".

## Prasyarat

- .NET SDK 10
- SQL Server — default ke LocalDB `(localdb)\MSSQLLocalDB` (ubah `ConnectionStrings:Default` bila perlu)
- 9router — `npm install -g 9router` lalu `9router` (dashboard di http://localhost:20128)

Redis/Docker tidak diperlukan: short memory memakai [Garnet](https://microsoft.github.io/garnet/) (server protokol
Redis dari Microsoft) yang berjalan di dalam proses AiThread — lihat
[docs/01-instalasi.md](docs/01-instalasi.md#1-short-memory-garnet).

## Menjalankan

```powershell
dotnet run --project src/AiThread.UI --launch-profile http   # http://localhost:5225
```

Migrasi database diterapkan otomatis saat start.

Agar AiThread tetap hidup walau browser, terminal, atau Visual Studio ditutup, jalankan di background
(publish Release ke `%LOCALAPPDATA%\AiThread\app`, tanpa jendela):

```powershell
.\scripts\aithread.ps1 start          # juga: stop · restart · status · logs -Follow · autostart-on · autostart-off
```

Detail: [docs/01-instalasi.md](docs/01-instalasi.md#5-menjalankan-di-background).

Tanpa .NET SDK: `.\scripts\publish-exe.ps1 -Zip` membuat `dist\AiThread\AiThread.exe` standalone. Dijalankan
tanpa argumen, exe menampilkan menu (Web UI, Sembunyikan ke Tray, log, restart, port, autostart); `AiThread.exe help`
menampilkan perintahnya — [docs/01-instalasi.md](docs/01-instalasi.md#6-aithreadexe-standalone-tanpa-net).

1. **Setting** → isi URL 9router (mis. `http://localhost:20128`) dan API key dari dashboard 9router → *Test koneksi* → *Simpan*.
2. **Node** → tambahkan minimal satu Planner, satu Executor, dan satu Wrapper. Model/combo diambil dari `/v1/models` 9router.
   - Planner: model terkuat yang mendukung *tool calling*.
   - Executor: model murah & cepat; tambah beberapa node untuk menyebar beban. `Max paralel` dibatasi per node lintas semua thread.
   - Wrapper: model murah; juga dipakai untuk ekstraksi memory dan sebagai model passthrough default.
3. **Setting → Integrasi Claude Code** → pilih *Per project* (disarankan) atau *Global* → *Terapkan*.
   Ini menulis blok `env` ke `.claude/settings.local.json` (atau `~/.claude/settings.json`):

   ```json
   {
     "env": {
       "ANTHROPIC_BASE_URL": "http://localhost:5225",
       "ANTHROPIC_AUTH_TOKEN": "<proxy key atau aithread-local>",
       "API_TIMEOUT_MS": "1800000",
       "ANTHROPIC_CUSTOM_HEADERS": "X-AiThread-Solution: D:\\Source\\MyApp\\MyApp.sln"
     }
   }
   ```

   Backup file asli disimpan sekali sebagai `*.aithread-backup`; tombol *Kembalikan* menghapus kunci-kunci di atas.
4. Buka sesi Claude Code baru di project tersebut dan beri perintah seperti biasa. Progres (tool planner, rencana,
   status tiap tugas) tampil di Claude Code; detail lengkap ada di Dashboard → thread.

Gunakan URL **http** untuk proxy: Claude Code (Node) tidak mempercayai sertifikat development ASP.NET Core.

## Menu

| Menu | Fungsi |
|---|---|
| Dashboard | Status 9router / Redis / database / pipeline, statistik (speedup paralel, token), jalankan prompt uji, daftar thread, log langsung (SSE) |
| Project | Project yang terbaca (unik per `.sln`/`.slnx`), sesi Claude Code per project, penelusur & scan `.sln`/`.slnx`, registrasi manual |
| Memory | Short memory (Garnet, ≤ 24 jam) dan long memory (database); tipe *project* dan *knowledge*; edit, promote, konsolidasi |
| Node | Planner / executor / wrapper beserta model 9router, temperature, max token, paralelisme, prioritas, instruksi tambahan |
| Setting | Koneksi & key 9router, proxy key, model passthrough, batas orkestrasi, integrasi `settings.json` Claude Code, bot Telegram |

## Prompt dari Telegram

Laptop ditinggal di kantor/rumah? Kirim prompt dari HP lewat bot Telegram pribadi: **Setting → Telegram** → tempel
token dari `@BotFather` → *Simpan Telegram*, kirim `/start` ke bot, lalu **Izinkan** chat Anda. Pesan teks biasa
menjalankan thread di project aktif; progres tampil live di chat dan ringkasan wrapper dikirim saat selesai
(`/projects`, `/status`, `/cancel`, `/diff`, `/new`). AiThread memakai long polling, jadi tidak perlu URL publik atau
port forwarding. Detail: [docs/11-telegram.md](docs/11-telegram.md).

## Cara request Claude Code diperlakukan

`POST /v1/messages` diorkestrasi bila request punya `tools`, system prompt Claude Code, pesan terakhir adalah
teks user (bukan `tool_result`), **dan sesinya berasal dari project `.sln`/`.slnx`**. Request pendukung lain (judul
sesi, compact, cek kuota) dan semua prompt dari sesi di luar project diteruskan apa adanya ke `/v1/messages` 9router
memakai *model passthrough* (default: model node Wrapper). Mematikan *Orkestrasi aktif* membuat AiThread menjadi
proxy biasa.

Pengecekan dilakukan per sesi Claude Code (keputusan diingat 30 menit). Solution diambil dari header
`X-AiThread-Solution` bila ada; jika tidak, `.sln/.slnx` dicari dari working directory sesi (header `X-AiThread-Cwd`
atau baris `Primary working directory:`): di folder itu, lalu sub-folder hingga 2 tingkat (hanya jika tepat satu),
lalu folder induk. Ditemukan → project didaftarkan otomatis pada prompt pertama. Tidak ditemukan → sesi diabaikan.

## Memory

- Setelah thread selesai, wrapper model menyarikan maksimal 5 memory: **label** (keyword, dipindai planner tanpa
  membaca isi) dan **deskripsi**.
- **Short memory** disimpan di Garnet (protokol Redis, di dalam AiThread, tahan restart/crash) tanpa TTL; worker konsolidasi (tiap 10 menit) memindahkan yang berumur
  > 24 jam ke **long memory** di database dengan id yang sama (idempoten).
- Planner menerima indeks label (relevan dengan prompt + short memory terbaru) dan bisa memanggil `search_memory` /
  `read_memory`.
- *Knowledge memory* tidak terikat project dan terlihat oleh semua project.

## API & SSE

- Proxy Anthropic: `POST /v1/messages`, `POST /v1/messages/count_tokens`, `GET /v1/models`
- SSE: `GET /api/stream?types=log,router,thread` (prefix tipe event) atau `GET /api/stream?threadId={id}`.
  Event: `log`, `router.status`, `thread.created|updated|output`, `task.updated|output`, `memory.changed`, `project.changed`, `node.changed`, `telegram.changed`, `ping`.
- REST: `/api/settings`, `/api/router`, `/api/nodes`, `/api/projects`, `/api/memory`, `/api/threads`, `/api/dashboard`, `/api/telegram`.

Konfigurasi infrastruktur ada di `src/AiThread.UI/appsettings.json` (connection string, `AiThread:ShortMemoryHours`,
interval konsolidasi & monitor, retensi log, batas baris file utuh untuk executor, `AiThread:EmbeddedRedis`).

## Catatan penting

- Executor **menulis langsung ke file project**. Jalankan di repo git agar perubahan mudah ditinjau atau dibatalkan.
- AiThread tidak menjalankan build/test/migrasi; langkah lanjutan disebutkan di ringkasan wrapper.
- Proxy dapat menulis file di folder mana pun yang dikirim sebagai working directory. Biarkan hanya mendengarkan
  `localhost`, dan isi *Proxy API key* bila mesin diakses orang lain.
- API key 9router dan token bot Telegram disimpan terenkripsi (ASP.NET Core Data Protection).
- Bot Telegram hanya melayani chat pribadi yang Anda izinkan di menu Setting — izinkan hanya akun Anda sendiri.

## Pengembangan

```powershell
dotnet build AiThread.slnx
dotnet test tests/AiThread.Tests
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Nama> --project src/AiThread.Data --startup-project src/AiThread.Data
```
