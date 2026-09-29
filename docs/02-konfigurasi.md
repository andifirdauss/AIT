# 2. Konfigurasi

Konfigurasi runtime disimpan di database dan diubah dari UI — tidak perlu restart.

## Menu Setting

### Koneksi 9router

| Field | Keterangan |
|---|---|
| Base URL | Alamat 9router, default `http://localhost:20128`. Akhiran `/v1` (seperti yang ditampilkan dashboard 9router) dibuang otomatis. |
| API key | Key dari dashboard 9router. Disimpan **terenkripsi**; setelah disimpan hanya ditampilkan tersamar (mis. `sk-abc••••1234`). Kosongkan field untuk mempertahankan key lama. |
| Hapus API key tersimpan | Centang lalu *Simpan setting* untuk menghapus key. |

Tombol **Test koneksi** memakai nilai di form **tanpa menyimpannya**. Setelah hasilnya benar, klik **Simpan**
(di kartu yang sama) — hanya key yang tersimpan yang dipakai AiThread. Saat halaman dibuka, baris hasil menampilkan
status konfigurasi yang tersimpan.

- `✓ Online · 12 ms · 48 model · 3 combo · API key OK` — siap dipakai.
- `… · key ini belum disimpan — klik Simpan` — key yang Anda ketik valid, tetapi belum tersimpan.
- `API key wajib, belum disimpan` — 9router mewajibkan key (*require API key* aktif) dan belum ada key tersimpan.
  Semua panggilan model akan gagal `401 Missing API key`.
- `API key ditolak` — key salah atau sudah dihapus di 9router.
- `tanpa API key (tidak diwajibkan)` — tidak ada key dan 9router tidak memintanya.
- `✕ Offline — …` — 9router tidak berjalan atau URL salah.

"Online" saja belum cukup: health check dan daftar model 9router tidak butuh key, jadi status key dicek terpisah
(dengan request kosong yang ditolak 9router sebelum model dipanggil — tanpa biaya token). Masalah key juga tampil
sebagai *Online · cek API key* (kuning) di Dashboard dan sebagai peringatan di log.

### Proxy & orkestrasi

| Field | Default | Rentang | Keterangan |
|---|---|---|---|
| Orkestrasi aktif | aktif | — | Jika dimatikan, semua request Claude Code diteruskan ke 9router dengan *model passthrough* — Claude Code bekerja sebagai agen biasa. |
| Tampilkan progres di Claude Code | aktif | — | Menampilkan langkah planner, rencana, dan status tiap tugas sebelum ringkasan akhir. Jika mati, hanya ringkasan yang tampil. |
| Model passthrough | otomatis | — | Model untuk request pendukung (judul sesi, `/compact`, cek kuota). *Otomatis* = model node Wrapper aktif, lalu Executor. |
| Proxy API key | kosong | — | Jika diisi, Claude Code wajib mengirimnya sebagai `ANTHROPIC_AUTH_TOKEN`. Tombol *Generate* membuat key acak. |
| Langkah planner | 24 | 3–100 | Maksimal putaran tool planner. Di langkah terakhir planner dipaksa mengirim rencana. |
| Retry executor | 1 | 0–5 | Berapa kali executor boleh mencoba ulang bila editnya gagal diterapkan. |
| Maks paralel | 8 | 1–64 | Batas tugas yang berjalan bersamaan **dalam satu thread**. |
| Timeout (detik) | 600 | 30–3600 | Batas waktu satu panggilan model ke 9router. |

Klik **Simpan setting**. Setelah mengganti proxy API key, terapkan ulang integrasi Claude Code agar
`ANTHROPIC_AUTH_TOKEN` ikut berubah.

## Menu Node

Pipeline butuh minimal **1 Planner, 1 Executor, 1 Wrapper** yang aktif. Banner di atas halaman menunjukkan
kesiapan pipeline beserta catatannya.

### Field node

| Field | Default (UI) | Keterangan |
|---|---|---|
| Nama | — | Label bebas, tampil di progres dan detail thread. |
| Role | Executor | Planner / Executor / Wrapper. |
| Backend | 9router | Khusus Planner: *9router* atau *Claude Code CLI lokal* (lihat [Planner lewat Claude Code CLI](#planner-lewat-claude-code-cli)). Role lain selalu 9router. |
| Model / combo | — | Dipilih dari `/v1/models` 9router, dikelompokkan: *Combo* dulu, lalu per provider. Jika 9router offline, muncul input teks untuk mengetik id model manual. Untuk backend CLI: daftar berisi alias Claude Code (`opus`, `fable`, `sonnet`, `haiku` — selalu model terbaru keluarganya); nama model lengkap tetap bisa diketik. Mengganti backend mengosongkan/mengganti isian yang tidak cocok. |
| Temperature | Planner 0.2 · Executor 0.1 · Wrapper 0.3 | 0–2. Kosongkan untuk nilai default yang sama. |
| Max tokens | Planner 16384 · Executor 8192 · Wrapper 4096 | 256–200000. Batas output per panggilan. |
| Max paralel | Planner 2 · Executor 6 · Wrapper 4 | 1–64. Berapa request ke model node ini yang boleh berjalan bersamaan, dihitung **di semua thread** (berguna untuk rate limit provider). Bukan jumlah proses/instance. |
| Prioritas | 100 | Angka kecil diutamakan. |
| Instruksi tambahan | kosong | Ditambahkan di akhir prompt bawaan peran, mis. konvensi coding tim. |
| Aktif | aktif | Node nonaktif tidak dipakai. |

Tombol **Test** mengirim prompt singkat ("Reply with exactly one word: pong") dan menampilkan latency, token, dan
balasan — cara cepat memastikan model dan key benar.

### Aturan pemilihan node

- **Planner**: node aktif dengan prioritas terkecil yang dipakai. Node Planner aktif lainnya menjadi cadangan
  otomatis: bila planner utama kena rate limit (429), timeout, atau error 5xx setelah retry — atau Claude Code CLI
  gagal — planner berikutnya (urut prioritas) mengambil alih. Antar-node 9router investigasi dilanjutkan tanpa
  mengulang; saat berpindah dari/ke backend CLI, planner berikutnya mulai lagi dari brief.
- **Wrapper**: hanya satu yang dipakai — node aktif dengan prioritas terkecil. Node lain berfungsi sebagai cadangan
  yang tinggal diaktifkan.
- **Executor**: semua node aktif dipakai. Tiap tugas diberikan ke executor dengan slot kosong terbanyak, lalu
  prioritas terkecil, lalu bergiliran.
- **Wrapper** juga dipakai untuk ekstraksi memory, dan menjadi model passthrough bila *Model passthrough* diset
  otomatis.

### Cara kerja *Max paralel*

Setiap panggilan model ke sebuah node memakai satu slot; bila semua slot terpakai, panggilan berikutnya menunggu.
Slot dibagi di antara semua thread yang sedang berjalan (beberapa project sekaligus).

| Peran | Yang memakai slot |
|---|---|
| Executor | Satu tugas selama dikerjakan, termasuk retry. Executor dengan 6 slot mengerjakan maksimal 6 tugas serentak. |
| Planner | Satu slot per panggilan model; slot dilepas saat planner menjalankan tool baca-kode. Dengan 2 slot, maksimal 2 thread menunggu jawaban planner bersamaan. Backend CLI memegang satu slot selama seluruh proses `claude` berjalan. |
| Wrapper | Satu slot untuk ringkasan akhir dan satu untuk ekstraksi memory tiap thread. |

Selain itu, Setting → *Maks paralel* membatasi tugas serentak **per thread**. Jumlah tugas yang benar-benar berjalan
bersamaan = nilai terkecil dari: tugas yang siap, *Maks paralel* per thread, dan total slot kosong semua executor.
Panggilan passthrough (request pendukung Claude Code) dan tombol *Test* tidak memakai slot.

### Planner lewat Claude Code CLI

Backend *Claude Code CLI lokal* menjalankan `claude -p` di folder project memakai login Claude Code di mesin yang
menjalankan AiThread (langganan atau API key milik CLI), bukan lewat 9router. Keuntungannya: prompt caching dan tool
baca-kode bawaan CLI, serta kuota yang dipakai lewat klien resmi.

Cara kerja:

- CLI hanya boleh memakai tool baca `Read`, `Grep`, `Glob`. Tool tulis/perintah (`Edit`, `Write`, `Bash`, web, sub-agent)
  diblokir.
- Memory (`search_memory`, `read_memory`) dan `submit_plan` disediakan AiThread sebagai server MCP di
  `/mcp/planner/{token}`. Token acak berlaku selama satu proses planner. Rencana tetap divalidasi `PlanValidator`, lalu
  executor 9router dipicu seperti biasa.
- Settings Claude level *user* dan *local* tidak dibaca (`--setting-sources project`), karena di sanalah AiThread menulis
  `ANTHROPIC_BASE_URL`. Bila proses CLI tetap sampai ke proxy AiThread, request-nya ditolak (tidak diorkestrasi ulang)
  dan planner cadangan mengambil alih.
- Batas langkah CLI = 2 × *Langkah planner*; tidak ada langkah terakhir yang memaksa `submit_plan`.

Konfigurasi di `appsettings.json` bagian `AiThread`:

| Kunci | Default | Keterangan |
|---|---|---|
| `ClaudeCli:Command` | `claude` | Nama di PATH atau path lengkap (`claude.exe` installer native, `claude.cmd` dari npm). `~/.local/bin` ikut dicari. |
| `ClaudeCli:SettingSources` | `project` | Nilai `--setting-sources` untuk CLI. |
| `ClaudeCli:TimeoutMinutes` | 15 | Batas satu kali planning. |
| `ClaudeCli:ExtraArguments` | `[]` | Argumen tambahan, mis. `["--fallback-model", "sonnet"]`. |
| `LocalBaseUrl` | kosong | URL AiThread untuk klien MCP CLI. Kosong = alamat `http://` pertama Kestrel. |

Setelah menyimpan node, klik **Test** untuk memastikan CLI terpasang, sudah login, dan nama model diterima.
Disarankan tetap punya node Planner 9router aktif dengan prioritas lebih besar sebagai cadangan.

### Memilih model

| Peran | Kebutuhan | Saran |
|---|---|---|
| Planner | Paling cerdas, konteks besar, **wajib mendukung tool calling** (function calling). | Model kelas teratas yang Anda punya di 9router. Kualitas rencana & kontrak menentukan akurasi semua executor. |
| Executor | Murah, cepat, patuh format. | Model murah/gratis. Tambahkan beberapa node dari provider berbeda agar beban dan rate limit tersebar; combo memberi fallback otomatis. |
| Wrapper | Murah, bisa merangkum. | Model murah yang sama dengan executor cukup. |

Tips:

- *Max tokens* executor harus cukup untuk seluruh output edit. Tugas `create` menulis file utuh — naikkan bila
  sering muncul "Output was cut off (max tokens)".
- Jika planner sering gagal membuat rencana valid, naikkan *Langkah planner* atau ganti ke model yang lebih kuat.
- Model planner yang tidak mendukung tool calling tetap dicoba: AiThread membaca rencana JSON dari teks, dan bila
  tetap berupa teks biasa setelah dua kali diingatkan, teks itu dipakai sebagai jawaban langsung.

Contoh susunan (id model hanya ilustrasi — pilih dari dropdown sesuai provider Anda):

| Nama | Role | Model | Max paralel | Prioritas |
|---|---|---|---|---|
| Planner utama | Planner | model terkuat, mis. dari provider Claude Code | 2 | 1 |
| Executor A | Executor | combo murah | 6 | 10 |
| Executor B | Executor | model murah dari provider lain | 4 | 10 |
| Wrapper | Wrapper | model murah | 4 | 1 |

Lanjut ke [Integrasi Claude Code](03-integrasi-claude-code.md).
