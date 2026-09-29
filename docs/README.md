# Dokumentasi AiThread

AiThread adalah proxy lokal di antara **Claude Code** dan **9router**. Setiap prompt yang Anda ketik di Claude Code
direncanakan oleh satu model yang kuat (planner), dikerjakan paralel oleh banyak model murah (executor) dengan
konteks sekecil mungkin, lalu dirangkum (wrapper) sebelum dikirim balik ke Claude Code.

```
Anda ─► Claude Code ─► AiThread ─► Planner ─► Executor × N (paralel) ─► Wrapper ─► Claude Code
                           │                                                │
                           └──────────── 9router (semua model) ◄────────────┘
```

Dokumentasi ini juga bisa dibaca di aplikasi lewat menu **Docs** (http://localhost:5225/Docs).

## Mulai cepat

1. Jalankan Redis dan 9router — [Instalasi](01-instalasi.md).
2. Jalankan AiThread: `dotnet run --project src/AiThread.UI --launch-profile http` lalu buka http://localhost:5225.
   Agar tetap hidup walau browser/terminal ditutup, jalankan di background: `.\scripts\aithread.ps1 start` —
   [Menjalankan di background](01-instalasi.md#5-menjalankan-di-background).
3. **Setting** → isi URL & API key 9router → *Test koneksi* → *Simpan* — [Konfigurasi](02-konfigurasi.md).
4. **Node** → tambahkan 1 Planner, minimal 1 Executor, 1 Wrapper.
5. **Project** → *Telusuri…* untuk mencari dan mendaftarkan `.sln`/`.slnx` project Anda, lalu
   **Setting → Integrasi Claude Code** → *Terapkan* — [Integrasi Claude Code](03-integrasi-claude-code.md).
6. Buka sesi Claude Code **baru** di folder project dan beri perintah seperti biasa — [Penggunaan](04-penggunaan.md).
7. *(Opsional)* **Setting → Telegram** untuk mengirim prompt dari HP saat laptop ditinggal —
   [Prompt dari Telegram](11-telegram.md).

## Daftar isi

| # | Dokumen | Isi |
|---|---|---|
| 1 | [Instalasi](01-instalasi.md) | Prasyarat, Redis, 9router, database, menjalankan aplikasi (juga di background), `appsettings.json` |
| 2 | [Konfigurasi](02-konfigurasi.md) | Menu Setting (9router, proxy, orkestrasi) dan menu Node (memilih model per peran) |
| 3 | [Integrasi Claude Code](03-integrasi-claude-code.md) | Mengarahkan Claude Code ke AiThread lewat `settings.json`, verifikasi, mengembalikan |
| 4 | [Penggunaan sehari-hari](04-penggunaan.md) | Alur kerja, membaca progres, menulis prompt yang efektif, meninjau & membatalkan |
| 5 | [Panduan menu](05-menu.md) | Dashboard, detail thread, Project, Memory, Node, Setting |
| 6 | [Memory](06-memory.md) | Short vs long memory, project vs knowledge memory, siklus hidup, pengelolaan |
| 7 | [Cara kerja](07-cara-kerja.md) | Detail pipeline: klasifikasi request, planner, kontrak, validator, executor, wrapper |
| 8 | [Troubleshooting](08-troubleshooting.md) | Pesan error umum dan solusinya |
| 9 | [Referensi API](09-referensi-api.md) | Endpoint proxy Anthropic, REST `/api`, event SSE |
| 10 | [Pengembangan](10-pengembangan.md) | Struktur solusi, build, test, migrasi, titik ekstensi |
| 11 | [Prompt dari Telegram](11-telegram.md) | Bot Telegram pribadi untuk prompt jarak jauh: setup, perintah, progres live, keamanan, troubleshooting |

## Istilah

| Istilah | Arti |
|---|---|
| **Thread** | Satu permintaan yang diorkestrasi: rencana → tugas → ringkasan. Punya halaman detail di Dashboard. |
| **Planner** | Node paling cerdas. Membaca kode (read-only), menyusun kontrak, dan memecah pekerjaan menjadi tugas. |
| **Executor** | Node murah. Mengerjakan satu tugas = satu file. Banyak executor berjalan paralel. |
| **Wrapper** | Node yang mengumpulkan semua hasil executor dan menulis balasan untuk Anda. |
| **Kontrak** | Daftar nama yang dipakai lintas file (id elemen, properti DTO, parameter API, kolom DB) — wajib dipakai persis oleh semua executor. |
| **Tugas** | Instruksi untuk satu file (`edit` atau `create`), bisa bergantung pada tugas lain (`dependsOn`). |
| **Node** | Konfigurasi satu pekerja AI: peran, model 9router, temperature, max token, batas paralel. |
| **Combo** | Model gabungan di 9router yang otomatis fallback antar provider. Bisa dipilih seperti model biasa. |
| **Passthrough** | Request Claude Code yang tidak diorkestrasi (judul sesi, compact, cek kuota, sesi di luar project) dan diteruskan langsung ke 9router. |
| **Sesi diabaikan** | Sesi Claude Code yang working directory-nya bukan bagian project `.sln`/`.slnx`. Semua prompt-nya dilewatkan apa adanya — tidak ada planner/executor. |
| **Short memory** | Memory baru, disimpan di Redis selama ±24 jam. |
| **Long memory** | Memory yang sudah dipindah dari Redis ke database. |
| **Project memory / Knowledge memory** | Pengetahuan khusus satu project / pola umum yang berguna lintas project. |
