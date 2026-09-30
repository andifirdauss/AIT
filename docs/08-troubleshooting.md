# 8. Troubleshooting

Langkah pertama untuk hampir semua masalah: buka **Dashboard → Log langsung** (pilih *Semua (debug)*) atau
halaman detail thread → *Aktivitas*.

## 9router

**Kartu 9router *Offline*: "No connection could be made…" / "Timeout (5 detik)"**
9router tidak berjalan atau URL salah. Jalankan `9router`, pastikan dashboard terbuka di URL yang sama dengan
Setting → Base URL, lalu *Test koneksi*.

**"9router HTTP 401 untuk model '…': Missing API key" padahal Setting terlihat terhubung**
Belum ada API key **tersimpan**. *Test koneksi* hanya menguji key yang diketik di form tanpa menyimpannya, dan
status *Online* hanya membuktikan 9router hidup (health check dan daftar model tidak butuh key). Isi API key di
Setting → klik **Simpan** → pastikan baris status menampilkan `API key OK`. Kartu 9router di Dashboard menampilkan
*Online · cek API key* selama key belum beres.

**"API key ditolak oleh 9router" / "Invalid API key"**
Key salah atau sudah dihapus di 9router. Salin ulang dari dashboard 9router, isi di Setting, klik **Simpan**.

**Key 9router "hilang" setelah pindah komputer/akun Windows**
Key dienkripsi dengan Data Protection milik akun Windows tersebut. Isi ulang key di Setting.

**"9router HTTP 4xx/5xx untuk model '…'"**
Model tidak tersedia atau provider-nya bermasalah. Klik *Test* pada node terkait; pilih model/combo lain bila perlu.
Error sementara (429, 5xx, koneksi) sudah dicoba ulang otomatis 2 kali sebelum dilaporkan. Untuk 429, header
`Retry-After` dari 9router dihormati (maks. 30 detik; bila lebih lama tidak ditunggu), tanpa header jedanya ±5 dan ±10
detik.

**Planner kena 429 / rate limit**
Biasanya karena akun provider (mis. langganan Claude di 9router) kehabisan kuota per menit — tiap langkah planner
mengirim ulang seluruh percakapan, jadi token input cepat menumpuk. Lewat 9router (`/v1/chat/completions`) tidak ada
prompt caching: satu thread planner `cc/claude-opus-…` dengan 10–20 langkah bisa memakai 0,2–1 juta token input penuh.
Kolom token planner di detail thread menunjukkan pemakaian sebenarnya. Solusi:
- Tambahkan node Planner **kedua** (model/provider lain, prioritas lebih besar) dan biarkan aktif. AiThread otomatis
  beralih ke node itu dan melanjutkan investigasi; baris `⚠️ Planner … beralih ke …` muncul di progres.
- Atau jadikan model planner sebuah **combo** 9router (mis. Claude → model lain) supaya fallback terjadi di 9router.
- Kurangi slot node planner bila beberapa thread berjalan bersamaan, atau turunkan Setting → *Langkah planner*.

**"Model '…' tidak merespons dalam N detik"**
Naikkan Setting → *Timeout (detik)*, atau pakai model yang lebih cepat.

## Claude Code tidak melewati AiThread

Jawaban muncul tanpa baris `**AiThread** · …` dan tidak ada thread baru di Dashboard:

1. Buka **sesi Claude Code baru** — env hanya dibaca saat sesi dimulai.
2. Menu CLI → kartu Claude Code: switch harus menyala dengan lencana *Aktif* (bukan *Mengarah ke alamat lain*).
3. Untuk cakupan per project, pastikan Claude Code dijalankan di folder yang berisi `.claude/settings.local.json`
   (lihat path di kotak status). Jika berbeda, salin blok `env` ke folder tempat Anda menjalankan `claude`.
4. Pastikan tidak ada environment variable Windows `ANTHROPIC_BASE_URL` yang menimpa.
5. Setting → *Orkestrasi aktif* harus menyala. Jika mati, request tetap lewat AiThread tetapi diteruskan tanpa orkestrasi
   (terlihat sebagai log debug `Passthrough (orchestration disabled)`).
6. Cari di log baris `Sesi xxxxxxxx (…) bukan bagian project (…)`. Artinya sesi **sengaja diabaikan** karena
   working directory-nya bukan bagian sebuah project — lihat [Project](#project).

Log debug `Passthrough (auxiliary request without tools)` untuk request judul sesi/compact adalah hal normal.

## Error yang tampil di Claude Code

| Pesan | Penyebab | Solusi |
|---|---|---|
| `authentication_error: Invalid AiThread proxy key` | `ANTHROPIC_AUTH_TOKEN` berbeda dengan Proxy API key. | Terapkan ulang integrasi setelah mengganti key, lalu buka sesi baru. |
| `AiThread: passthrough model belum diatur…` (503) | Tidak ada model passthrough dan tidak ada node Wrapper/Executor aktif. | Isi *Model passthrough* atau aktifkan node. |
| `❌ AiThread gagal: Belum ada node Planner aktif…` | Pipeline belum lengkap. | Tambahkan/aktifkan node di menu Node. |
| `❌ AiThread gagal: Planner tidak menghasilkan rencana yang valid dalam N langkah.` | Planner kehabisan langkah atau terus mengirim rencana yang ditolak validator. | Lihat alasan penolakan di *Aktivitas* thread. Naikkan *Langkah planner*, pakai model planner yang lebih kuat, atau perjelas prompt. |
| `❌ AiThread gagal memulai thread: …` | Masalah sebelum thread dibuat, biasanya database atau project dihapus saat sesi berjalan. | Periksa kartu Database di Dashboard; daftarkan ulang project bila perlu. |
| Claude Code timeout | Thread lebih lama dari `API_TIMEOUT_MS`. | Naikkan `API_TIMEOUT_MS` di settings Claude Code, atau pecah prompt. |

## Project

**Prompt tidak diorkestrasi: log `Sesi … bukan bagian project (…)`**
Sesi dianggap di luar project, sehingga prompt diteruskan langsung dan tidak ada memory yang dibuat. Alasan di dalam
kurung menunjukkan penyebabnya:

- *tidak ada marker project*: tidak ada `.git` atau file build (`.sln`, `pom.xml`, `package.json`, `go.mod`, dst.) di
  working directory maupun folder induknya (hingga 6 tingkat). Ini wajar untuk folder kosong atau folder catatan;
- *folder ini berisi project a, b, …*: working directory hanya berisi beberapa project, tidak ada yang dipilih karena
  ambigu. Buka sesi di salah satunya;
- *folder ini terlalu luas*: working directory adalah drive, folder Home, atau folder sistem;
- *working directory tidak diketahui*: klien tidak mengirimnya; set header `X-AiThread-Cwd`.

Solusi: jalankan Claude Code di folder project, tambahkan `.git` (`git init`) atau file build ke folder itu, atau
daftarkan foldernya lewat *Daftarkan manual* di menu Project (folder tanpa marker pun bisa). Bisa juga menerapkan
integrasi *per project* (mengirim header `X-AiThread-Project`). Sesi yang diabaikan dicek ulang setelah sekitar 5 detik.

**Registrasi manual: "Folder … tidak ditemukan" atau "Folder ini terlalu luas untuk jadi project"**
Path salah, atau folder yang dituju adalah drive/Home/folder sistem. Isi path folder project-nya, atau gunakan
*Telusuri…* → *Scan* untuk mencarinya.

**Scan: "berhenti lebih awal (batas waktu/jumlah)"**
Scan dibatasi 300 hasil, 50.000 folder, dan 20 detik. Masuk ke folder yang lebih spesifik atau kurangi kedalaman.

**Folder tidak terlihat di penelusur**
Folder tersembunyi/sistem, folder yang diawali `.` atau `$`, folder build (`bin`, `obj`, `node_modules`, …), dan
junction/symlink sengaja disembunyikan. Ketik path lengkapnya di kolom path lalu Enter.

**Project salah terdeteksi**
Root dipilih dari root repository (`.git`) terdekat, lalu file penghimpun paling luar, lalu manifest terdekat (lihat
[Cek sesi](07-cara-kerja.md#2-cek-sesi-project-atau-bukan)); project yang sudah terdaftar dan memuat folder itu
diutamakan. Bila root yang Anda mau berbeda (mis. satu modul di monorepo sebagai project sendiri), daftarkan folder
itu lewat *Daftarkan manual*: folder dengan marker sendiri didaftarkan apa adanya, dan sesi di dalamnya memakainya.
Integrasi per project (header `X-AiThread-Project`) juga mengunci foldernya.

**Badge *folder hilang* di menu Project**
Folder project sudah dipindah/dihapus. Daftarkan path baru; hapus entri lama bila tidak diperlukan
(ingat: memory project ikut terhapus).

**Project lama muncul ganda atau menyatu setelah pembaruan**
Identitas project berpindah dari file `.sln` ke folder root. Migrasi otomatis menyatukan dua solution yang berada di
folder yang sama menjadi satu project (yang terakhir dipakai), lengkap dengan thread dan memory-nya.

## Tugas executor

| Pesan di tugas | Arti | Solusi |
|---|---|---|
| `the SEARCH text was not found…` | Executor tidak menyalin teks file dengan persis, bahkan setelah retry. | Naikkan *Retry executor*, pakai model executor yang lebih patuh, atau naikkan `ExecutorFullFileMaxLines` bila file besar. |
| `the SEARCH text occurs N times…` / `match several places` | Potongan SEARCH terlalu pendek sehingga ambigu. | Biasanya selesai di retry; bila tidak, sama seperti di atas. |
| `Output was cut off (max tokens)…` | Output melebihi *Max tokens* node. | Naikkan *Max tokens* executor (terutama untuk tugas `create`). |
| `No valid SEARCH/REPLACE block in the output.` | Model menjawab di luar format. | Ganti model executor; tambahkan instruksi di *Instruksi tambahan* node. |
| `Dependency tidak berhasil: T3` | Tugas dilewati karena tugas yang ditunggu gagal. | Perbaiki tugas yang gagal, lalu ulangi bagian ini dengan prompt baru. |

Tugas yang gagal tidak menulis apa pun ke file (edit bersifat all-or-nothing).

## Redis & database

**Kartu Redis *Tidak terhubung***
Short memory memakai Garnet di dalam AiThread. Cari baris `Embedded Garnet` di log AiThread: bila Garnet gagal
start, penyebabnya tercatat di situ. Bila `AiThread:EmbeddedRedis:Enabled` = `false`, pastikan Redis eksternal di
`ConnectionStrings:Redis` hidup. Selama short memory tidak tersedia, memory baru langsung masuk long memory; tidak
ada data hilang.

**Log `Port 6379 is already in use … short memory uses that server`**
Program lain (biasanya container Docker `aithread-redis` lama) memakai port short memory, sehingga AiThread
memakainya alih-alih Garnet. Pindahkan dulu short memory yang ingin disimpan (menu Memory → **→ DB**), lalu
`docker rm -f aithread-redis` dan restart AiThread. Lihat [Instalasi](01-instalasi.md#1-short-memory-garnet).

**Kartu Database *Tidak terhubung*** / log `Database migration failed`
Untuk LocalDB: `sqllocaldb start MSSQLLocalDB`. Untuk server lain: periksa `ConnectionStrings:Default`.
Restart AiThread setelah diperbaiki agar migrasi dijalankan.

## Telegram

Token ditolak, bot tidak membalas, *Token ini sedang dipakai program lain…*, *Chat ini belum diizinkan*, dan gejala
bot Telegram lainnya: lihat [Prompt dari Telegram → Troubleshooting](11-telegram.md#troubleshooting).

## Aplikasi

**AiThread berhenti saat browser, terminal, atau Visual Studio ditutup**
Instance dari Visual Studio (debugger berhenti saat browser yang dibukanya ditutup) dan `dotnet run` hanya hidup
selama program itu terbuka. Jalankan di background: `.\scripts\aithread.ps1 start` — lihat
[Menjalankan di background](01-instalasi.md#5-menjalankan-di-background).

**Port 5225 sudah dipakai**
Jalankan dengan `--urls http://localhost:<port lain>` lalu terapkan ulang integrasi Claude Code.

**`aithread.ps1 start`: "Port 5225 sudah dipakai proses lain (PID …)"**
Biasanya instance dari Visual Studio / `dotnet run` masih hidup. Hentikan program itu, lalu `start` lagi. Bila yang
memakai port adalah AiThread, `.\scripts\aithread.ps1 stop` juga bisa mematikannya.

**`aithread.ps1 start`: "AiThread berhenti saat start (exit N)"**
Potongan log langsung ditampilkan. Penyebab umum: database tidak bisa dihubungi (lihat [Redis & database](#redis--database))
atau `appsettings.json` tidak valid. Log lengkap: `.\scripts\aithread.ps1 logs`.

**Perubahan kode tidak terlihat di instance background**
Instance background memakai hasil publish, bukan folder `src/`. Jalankan `.\scripts\aithread.ps1 restart`.

**Log background berisi `Failed to determine the https port for redirect`**
Normal: instance background hanya mendengarkan http. Peringatan ini tidak berpengaruh.

**Thread *Cancelled*: "AiThread berhenti saat thread ini berjalan."**
AiThread dimatikan (stop, restart, logoff, crash) saat thread itu berjalan. Hasil tugas yang sudah selesai tetap
tertulis di file — tinjau dengan git, lalu kirim ulang prompt bila perlu.

**Base URL https gagal dari Claude Code**
Claude Code tidak mempercayai sertifikat development. Pakai alamat `http://localhost:5225`.

**Halaman tidak update live**
Browser memutus koneksi SSE (mis. terlalu banyak tab AiThread terbuka). Tutup tab lain atau refresh halaman.
