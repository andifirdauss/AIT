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

### Telegram

Kartu di bagian bawah halaman Setting, untuk mengirim prompt dari HP — panduan lengkap di
[Prompt dari Telegram](11-telegram.md).

| Field | Default | Keterangan |
|---|---|---|
| Aktifkan bot Telegram | mati | Menyalakan long polling ke Telegram. Butuh token tersimpan. |
| Bot token | kosong | Token dari `@BotFather`. Diverifikasi ke Telegram (`getMe`) saat disimpan, lalu disimpan **terenkripsi** dan hanya ditampilkan tersamar. Kosongkan field untuk mempertahankan token lama. |
| Hapus token tersimpan | — | Centang lalu *Simpan Telegram* untuk menghapus token (bot otomatis berhenti). |
| Chat | — | Chat pribadi yang pernah mengirim pesan ke bot. **Izinkan** chat Anda sendiri; **Hapus** chat asing; **Cabut** menghentikan akses chat yang sudah diizinkan. |

Tombol **Simpan Telegram** hanya menyimpan bagian ini (terpisah dari *Simpan setting*) dan langsung berlaku tanpa
restart. Progres di Telegram selalu tampil, tidak dipengaruhi saklar *Tampilkan progres di Claude Code*.

## Menu Node

Pipeline butuh minimal **1 Planner, 1 Executor, 1 Wrapper** yang aktif. Banner di atas halaman menunjukkan
kesiapan pipeline beserta catatannya. Node **Context Keeper** opsional (lihat
[Konteks sesi](07-cara-kerja.md#konteks-sesi-context-keeper)).

### Field node

| Field | Default (UI) | Keterangan |
|---|---|---|
| Nama | — | Label bebas, tampil di progres dan detail thread. |
| Role | Executor | Planner / Executor / Wrapper / Context Keeper. |
| Model / combo | — | Dipilih dari `/v1/models` 9router, dikelompokkan: *Combo* dulu, lalu per provider. Jika 9router offline, muncul input teks untuk mengetik id model manual. |
| Temperature | Planner 0.2 · Executor 0.1 · Wrapper 0.3 · Context Keeper 0.2 | 0–2. Kosongkan untuk nilai default yang sama. |
| Max tokens | Planner 16384 · Executor 8192 · Wrapper 4096 · Context Keeper 4096 | 256–200000. Batas output per panggilan. |
| Max paralel | Planner 2 · Executor 6 · Wrapper 4 · Context Keeper 2 | 1–64. Berapa request ke model node ini yang boleh berjalan bersamaan, dihitung **di semua thread** (berguna untuk rate limit provider). Bukan jumlah proses/instance. |
| Prioritas | 100 | Angka kecil diutamakan. |
| Instruksi tambahan | kosong | Ditambahkan di akhir prompt bawaan peran, mis. konvensi coding tim. |
| Aktif | aktif | Node nonaktif tidak dipakai. |

Tombol **Test** mengirim prompt singkat ("Reply with exactly one word: pong") dan menampilkan latency, token, dan
balasan — cara cepat memastikan model dan key benar.

### Aturan pemilihan node

- **Planner**: node aktif dengan prioritas terkecil yang dipakai. Node Planner aktif lainnya menjadi cadangan
  otomatis: bila planner utama kena rate limit (429), timeout, atau error 5xx setelah retry, planner berikutnya
  (urut prioritas) mengambil alih dan investigasi dilanjutkan tanpa mengulang.
- **Wrapper**: hanya satu yang dipakai — node aktif dengan prioritas terkecil. Node lain berfungsi sebagai cadangan
  yang tinggal diaktifkan.
- **Executor**: semua node aktif dipakai. Tiap tugas diberikan ke executor dengan slot kosong terbanyak, lalu
  prioritas terkecil, lalu bergiliran.
- **Wrapper** juga dipakai untuk ekstraksi memory, dan menjadi model passthrough bila *Model passthrough* diset
  otomatis.
- **Context Keeper**: node aktif dengan prioritas terkecil meringkas percakapan Claude Code setelah tiap thread.
  Tanpa node ini, Wrapper yang meringkas.

### Cara kerja *Max paralel*

Setiap panggilan model ke sebuah node memakai satu slot; bila semua slot terpakai, panggilan berikutnya menunggu.
Slot dibagi di antara semua thread yang sedang berjalan (beberapa project sekaligus).

| Peran | Yang memakai slot |
|---|---|
| Executor | Satu tugas selama dikerjakan, termasuk retry. Executor dengan 6 slot mengerjakan maksimal 6 tugas serentak. |
| Planner | Satu slot per panggilan model; slot dilepas saat planner menjalankan tool baca-kode. Dengan 2 slot, maksimal 2 thread menunggu jawaban planner bersamaan. |
| Wrapper | Satu slot untuk ringkasan akhir dan satu untuk ekstraksi memory tiap thread. |
| Context Keeper | Satu slot per pembaruan konteks sesi (sekali per thread Claude Code, di belakang). |

Selain itu, Setting → *Maks paralel* membatasi tugas serentak **per thread**. Jumlah tugas yang benar-benar berjalan
bersamaan = nilai terkecil dari: tugas yang siap, *Maks paralel* per thread, dan total slot kosong semua executor.
Panggilan passthrough (request pendukung Claude Code) dan tombol *Test* tidak memakai slot.

### Biaya token planner

Planner bekerja lewat 9router (`/v1/chat/completions`, format OpenAI) tanpa prompt caching, sehingga setiap langkah
mengirim ulang seluruh percakapan sebagai input penuh. Untuk menghemat:

- Turunkan Setting → *Langkah planner*.
- Hindari model langganan `cc/…` (Claude via 9router) untuk planner; pakai model/combo yang kuotanya longgar.
- Tambahkan node Planner kedua sebagai cadangan (lihat [Aturan pemilihan node](#aturan-pemilihan-node)).

### Memilih model

| Peran | Kebutuhan | Saran |
|---|---|---|
| Planner | Paling cerdas, konteks besar, **wajib mendukung tool calling** (function calling). | Model kelas teratas yang Anda punya di 9router. Kualitas rencana & kontrak menentukan akurasi semua executor. |
| Executor | Murah, cepat, patuh format. | Model murah/gratis. Tambahkan beberapa node dari provider berbeda agar beban dan rate limit tersebar; combo memberi fallback otomatis. |
| Wrapper | Murah, bisa merangkum. | Model murah yang sama dengan executor cukup. |
| Context Keeper | Murah, teliti menyalin nama file & identifier, konteks cukup untuk ±15 catatan thread. | Model murah yang rapi merangkum; tidak perlu tool calling. Jangan pakai model langganan Claude agar tidak memakai kuota. |

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
