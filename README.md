# AIT

Proxy lokal untuk CLI coding (Claude Code, Codex CLI, Gemini CLI, opencode, Qwen Code, oh-my-pi, …) yang memecah satu
permintaan menjadi tugas-tugas sangat kecil:
**planner** (model paling cerdas) merencanakan, banyak **executor** (model murah) mengerjakan secara paralel,
AIT membuild project, menjalankan **unit test** project yang terkait (katalog bersama per project; regresi diperbaiki
di kode) dan (opsional) **reviewer** memeriksa hasilnya sampai lolos, (opsional) **tester** mencoba perubahannya di
aplikasi yang berjalan — UI lewat browser Playwright yang tampil, API lewat request HTTP — lalu **wrapper** merangkum
hasilnya untuk user. Semua model diakses lewat [9router](https://github.com/decolua/9router).

> **Panduan lengkap pemakaian ada di [docs/](docs/README.md)** — instalasi, konfigurasi, integrasi CLI,
> penggunaan sehari-hari, memory, cara kerja, troubleshooting, dan referensi API.

```
CLI (Claude Code, Codex, Gemini CLI, …) ──(base URL)──► AIT /v1/messages · /v1/chat/completions
                                                           /v1/responses · /v1beta/models/*
                                          │
                  ┌───────────────────────┴───────────────────────┐
                  │ Planner (tool loop read-only + run_build/tests)│
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
                  Verifikasi: build (dotnet build, tsc, …) + unit test terkait
                     (baseline paralel planner) + Reviewer (opsional)
                     error/temuan/regresi ─► (triase Tester) ─► tugas perbaikan V1-1… ─► cek ulang
                                          ▼
                  Testing (Auto Testing, opsional): Tester → localhost
                     UI: browser Playwright non-headless · API: http_request + output
                                          ▼
                               Wrapper → balasan ke CLI (streaming, format CLI itu)
                                          ▼
                          Memory extractor → short memory (Garnet)
```

## Kenapa akurat walau konteks executor kecil

Masalah klasik multi-agent: executor UI membuat `id="description"` sementara executor JS membaca `#txtDesc`,
atau executor JS membuat file baru karena tidak tahu file yang sudah ada. AIT mencegahnya dengan:

1. **Planner wajib membaca kode dulu** (tool read-only, boleh juga membaca path di luar root untuk pengecekan) sebelum `submit_plan`.
2. **Kontrak**: semua nama yang dipakai lintas file (id elemen, properti DTO, parameter API, kolom DB, pesan validasi)
   ditetapkan sekali oleh planner dan dikirim ke setiap executor. Karena itu tugas bisa independen dan berjalan paralel.
3. **Validator rencana**: file `edit`/`delete`/`move` harus ada, file `create` dan tujuan `move` belum ada, file yang
   dihapus/dipindah tidak disentuh tugas lain, path perubahan tidak boleh keluar root atau masuk
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
- Node.js 18+ dan 9router — ikut terpasang lewat `npm i -g aithread` dan dinyalakan otomatis oleh AIT bila belum
  berjalan; tanpa npm `aithread`: `npm install -g 9router` (dashboard di http://localhost:20128)

Redis/Docker tidak diperlukan: short memory memakai [Garnet](https://microsoft.github.io/garnet/) (server protokol
Redis dari Microsoft) yang berjalan di dalam proses AIT — lihat
[docs/01-instalasi.md](docs/01-instalasi.md#1-short-memory-garnet).

## Menjalankan

```powershell
dotnet run --project src/AIThread.UI --launch-profile http   # http://localhost:5225
```

Migrasi database diterapkan otomatis saat start.

Agar AIT tetap hidup walau browser, terminal, atau Visual Studio ditutup, jalankan di background
(publish Release ke `%LOCALAPPDATA%\AIThread\app`, tanpa jendela):

```powershell
.\scripts\aithread.ps1 start          # juga: stop · restart · status · logs -Follow · autostart-on · autostart-off
```

Detail: [docs/01-instalasi.md](docs/01-instalasi.md#5-menjalankan-di-background).

Tanpa .NET SDK: `.\scripts\publish-exe.ps1 -Zip` membuat `dist\AIThread\AIThread.exe` standalone. Dijalankan
tanpa argumen, exe menampilkan menu (Web UI, Sembunyikan ke Tray, log, restart, port, autostart); `AIThread.exe help`
menampilkan perintahnya — [docs/01-instalasi.md](docs/01-instalasi.md#6-aithreadexe-standalone-tanpa-net).

1. **Setting** → isi URL 9router (mis. `http://localhost:20128`) dan API key dari dashboard 9router → *Test koneksi* → *Simpan*.
2. **Node** → tambahkan minimal satu Planner, satu Executor, dan satu Wrapper. Model/combo diambil dari `/v1/models` 9router.
   - Planner: model terkuat yang mendukung *tool calling*.
   - Executor: model murah & cepat; tambah beberapa node untuk menyebar beban. `Max paralel` dibatasi per node lintas semua thread.
   - Wrapper: model murah; juga dipakai untuk ekstraksi memory dan sebagai model passthrough default.
   - Reviewer (opsional): model yang teliti; memeriksa hasil executor terhadap rencana, kontrak, dan hasil build.
   - Tester (wajib bila project punya unit test atau memakai Auto Testing): model kuat dengan *tool calling*; menilai unit test yang gagal setelah perubahan (regresi, test salah, lingkungan, tidak stabil) — lihat [docs/07-cara-kerja.md](docs/07-cara-kerja.md#unit-test) — dan, dengan *Auto Testing* di Edit node Planner, mencoba perubahan di aplikasi yang sedang berjalan (UI lewat browser non-headless, API lewat HTTP) — lihat [docs/02-konfigurasi.md](docs/02-konfigurasi.md#auto-testing-node-tester).
   - Tools Node (opsional): model murah dengan *tool calling*; di Edit node Planner, tool yang boros (mis. `read_file`, `search_text`) bisa didelegasikan ke sini agar output mentahnya tidak membebani token planner — lihat [docs/02-konfigurasi.md](docs/02-konfigurasi.md#tools-node-planner).
3. Menu **CLI** → pilih kartu CLI Anda (Claude Code, Codex CLI, Gemini CLI, opencode, Qwen Code, oh-my-pi) → nyalakan
   switch *Per project* (bila tersedia) atau *Global*. Tiap kartu menulis entri AIT ke file konfigurasi CLI itu —
   lihat [Integrasi CLI](docs/03-integrasi-cli.md). Contoh untuk Claude Code, blok `env` di
   `.claude/settings.local.json` (atau `~/.claude/settings.json`):

   ```json
   {
     "env": {
       "ANTHROPIC_BASE_URL": "http://localhost:5225",
       "ANTHROPIC_AUTH_TOKEN": "<proxy key atau aithread-local>",
       "API_TIMEOUT_MS": "1800000",
       "ANTHROPIC_CUSTOM_HEADERS": "X-AIThread-Project: D:\\Source\\MyApp"
     }
   }
   ```

   Backup file asli disimpan sekali sebagai `*.aithread-backup`; mematikan switch menghapus entri AIT saja.
   Codex dipakai lewat `codex --profile aithread`; oh-my-pi, opencode, dan Qwen Code lewat pilihan model AIT.
4. Buka sesi CLI baru di project tersebut dan beri perintah seperti biasa. Progres (tool planner, rencana,
   status tiap tugas) tampil di CLI; detail lengkap ada di Dashboard → thread.

Gunakan URL **http** untuk proxy: CLI berbasis Node (mis. Claude Code) tidak mempercayai sertifikat development
ASP.NET Core.

## Menu

| Menu | Fungsi |
|---|---|
| Dashboard | Status 9router / Redis / database / pipeline, statistik (speedup paralel, token), jalankan prompt uji, daftar thread, log langsung (SSE) |
| Project | Project yang terbaca (unik per folder root: `.git` atau file build apa pun bahasanya), **project key** untuk berbagi memory antar developer, stack terdeteksi, sesi per project, penelusur & scan folder project, registrasi manual, **katalog unit test** (scan, jalankan, obsolete/karantina, setting per project) |
| Memory | Short memory (Garnet, ≤ 24 jam) dan long memory (database); tipe *project* dan *knowledge*; edit, promote, konsolidasi. Tab **Bisnis**: wiki bisnis per modul (business memory) dengan editor, screenshot, dan riwayat versi |
| Node | Planner / executor / reviewer / tester (triase unit test, Auto Testing) / wrapper / context keeper beserta model 9router, temperature, max token, paralelisme, prioritas, instruksi tambahan, skill (dicarikan Skill Orchestrator dari gudang skill) |
| Setting | Koneksi & key 9router, proxy key, model passthrough, batas orkestrasi, build & verifikasi otomatis, penyimpanan memory (short/long/bisnis) & penulis wiki bisnis, bot Telegram, Skill Orchestrator & gudang skill |

## Prompt dari Telegram

Laptop ditinggal di kantor/rumah? Kirim prompt dari HP lewat bot Telegram pribadi: **Setting → Telegram** → tempel
token dari `@BotFather` → *Simpan Telegram*, kirim `/start` ke bot, lalu **Izinkan** chat Anda. Pesan teks biasa
menjalankan thread di project aktif; progres tampil live di chat dan ringkasan wrapper dikirim saat selesai
(`/projects`, `/status`, `/cancel`, `/diff`, `/new`). AIT memakai long polling, jadi tidak perlu URL publik atau
port forwarding. Detail: [docs/11-telegram.md](docs/11-telegram.md).

## Skill untuk node

**Setting → Skill**: pilih model Skill Orchestrator, tempel link repository skill (mis.
`https://github.com/anthropics/skills`), lalu klik *Tarik skill*. Model memeriksa skill apa saja yang ada, lalu skill
ditarik ke gudang lokal (offline). Di modal **Node**, tulis kebutuhan ("aku butuh kamu bisa menjadi engineer yang
handal") lalu klik *Cari skill*. Skill yang cocok dicentang dan ditempel ke node, dan isinya ikut masuk ke system
prompt node setiap kali node dipanggil. Detail: [docs/07-cara-kerja.md](docs/07-cara-kerja.md#skill-pada-node).

## Cara request CLI diperlakukan

Request dari CLI mana pun (format Anthropic, OpenAI, atau Gemini) diorkestrasi bila punya `tools`, pesan terakhir
adalah teks user (bukan hasil tool), **dan sesinya berasal dari sebuah project (folder dengan `.git` atau file
build)**. Request pendukung lain (judul sesi, compact, cek kuota) dan semua prompt dari sesi di luar project
diteruskan apa adanya ke endpoint 9router yang sama memakai *model passthrough* (default: model node Wrapper).
Mematikan *Orkestrasi aktif* membuat AIT menjadi proxy biasa.

Pengecekan dilakukan per sesi CLI (keputusan diingat 30 menit). Project = folder root yang berisi `.git`
atau file build (`.sln`, `pom.xml`, `package.json`, `go.mod`, …), dicari dari working directory sesi (header
`X-AIThread-Cwd` atau baris working directory yang dikirim CLI, mis. `Primary working directory:`) naik ke folder induknya hingga 6 tingkat, tidak pernah ke
sub-folder; header `X-AIThread-Project` bisa menunjuk folder rootnya langsung. Ditemukan → project didaftarkan otomatis
pada prompt pertama. Tidak ada marker (mis. folder kosong) atau folder induk berisi beberapa project → sesi
diabaikan: tanpa orkestrasi, tanpa memory.

## Memory

- Project memory ditulis seperti `MEMORY.md`: setelah thread selesai, wrapper model menulis satu **catatan perubahan**
  (`Change`) — fitur yang berubah, **file yang berubah** (dari snapshot thread, bukan tebakan model), validasi dan flow
  yang ditambah/diubah, catatan. Setelah masuk long memory, entri dilipat ke **ringkasan modul** (`Summary`: kondisi modul
  saat ini). Label `Modul · Layer · Jenis` + keyword tetap ada untuk pencarian; modul dan layer diturunkan dari
  **peta kode**, modul dari daftar modul yang sama dengan wiki bisnis. Memory berformat lama (Rule/Flow/Pitfall/…)
  dimigrasi ke ringkasan modul (dengan backup dulu).
- **Short memory** disimpan di Garnet (protokol Redis, di dalam AIT, tahan restart/crash) tanpa TTL; worker konsolidasi (tiap 10 menit) memindahkan yang berumur
  > 24 jam ke **long memory** di database dengan id yang sama (idempoten).
- Planner menerima ringkasan + catatan perubahan terbaru modul yang dibahas, ditulis utuh di brief; ditambah peta memory
  (modul → jumlah per layer/jenis) dan indeks label (lapis 1: modul dari file/istilah di prompt; lapis 2: keyword), lalu memanggil `search_memory` (module/layer/kind/keywords) / `read_memory`, dan
  `set_module` untuk mengoreksi peta kode.
- *Knowledge memory* tidak terikat project dan terlihat oleh semua project.
- Memory project diikat ke **project key** (git remote, `.aithread/project.json`, atau key manual), bukan ke folder:
  arahkan long memory semua developer ke database yang sama (Setting → Penyimpanan memory) agar memory sebuah project
  terkumpul di satu tempat walau folder root tiap orang berbeda.
- **Business memory**: secara berkala (default tiap 6 jam, atau tombol *Perbarui sekarang*) thread yang selesai
  dirangkum AI menjadi wiki bisnis per modul — requirement, aturan bisnis, tabel status & validasi, dan screenshot
  dari prompt. Bisa diedit manual (dengan riwayat versi), disimpan di store sendiri yang bisa dibagi, dan dibaca
  planner sebelum mengubah perilaku sebuah modul. Detail: [docs/06-memory.md](docs/06-memory.md#business-memory-wiki-bisnis).

## API & SSE

- Proxy CLI: Anthropic `POST /v1/messages`, `POST /v1/messages/count_tokens`, `GET /v1/models`; OpenAI
  `POST /v1/chat/completions`, `POST /v1/responses`; Gemini `POST /v1beta/models/{model}:generateContent` /
  `:streamGenerateContent`
- SSE: `GET /api/stream?types=log,router,thread` (prefix tipe event) atau `GET /api/stream?threadId={id}`.
  Event: `log`, `router.status`, `thread.created|updated|output`, `task.updated|output`, `memory.changed`, `project.changed`, `node.changed`, `telegram.changed`, `skill.changed`, `business.changed`, `ping`.
- REST: `/api/settings`, `/api/router`, `/api/nodes`, `/api/projects`, `/api/memory`, `/api/business`, `/api/threads`, `/api/dashboard`, `/api/telegram`, `/api/skills`.

Konfigurasi infrastruktur ada di `src/AIThread.UI/appsettings.json` (connection string, `AIThread:ShortMemoryHours`,
interval konsolidasi & monitor, retensi log, batas baris file utuh untuk executor, `AIThread:EmbeddedRedis`).

## Catatan penting

- Executor **menulis langsung ke file project**. Jalankan di repo git agar perubahan mudah ditinjau atau dibatalkan.
- Satu-satunya perintah yang dijalankan AIT adalah build project (bisa dimatikan di Setting); test, migrasi, dan
  git tidak dijalankan — langkah lanjutan disebutkan di ringkasan wrapper.
- Proxy dapat menulis file di folder mana pun yang dikirim sebagai working directory. Biarkan hanya mendengarkan
  `localhost`, dan isi *Proxy API key* bila mesin diakses orang lain.
- API key 9router dan token bot Telegram disimpan terenkripsi (ASP.NET Core Data Protection).
- Bot Telegram hanya melayani chat pribadi yang Anda izinkan di menu Setting — izinkan hanya akun Anda sendiri.

## Pengembangan

```powershell
dotnet build AIThread.slnx
dotnet test tests/AIThread.Tests
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Nama> --context AIThreadDbContext --project src/AIThread.Data --startup-project src/AIThread.Data
```
