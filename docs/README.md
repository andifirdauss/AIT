# Dokumentasi AIT

AIT adalah proxy lokal di antara agen coding (**Claude Code**, **Codex CLI**, **Gemini CLI**, oh-my-pi, …) dan
**9router**. Setiap prompt yang Anda ketik direncanakan oleh satu model yang kuat (planner), dikerjakan paralel oleh
banyak model murah (executor) dengan konteks sekecil mungkin, lalu dirangkum (wrapper) sebelum dikirim balik ke agen
Anda dalam format yang ia pahami (Anthropic, OpenAI, atau Gemini). Panduan utama memakai Claude Code; klien lain ada di
[Klien lain](03-integrasi-claude-code.md#klien-lain-codex-gemini-cli-oh-my-pi-dll).

```
Anda ─► Claude Code ─► AIT ─► Planner ─► Executor × N (paralel) ─► Wrapper ─► Claude Code
                           │                                                │
                           └──────────── 9router (semua model) ◄────────────┘
```

Dokumentasi ini juga bisa dibaca di aplikasi lewat menu **Docs** (http://localhost:5225/Docs).

## Mulai cepat

1. Jalankan 9router — [Instalasi](01-instalasi.md). Redis/Docker tidak diperlukan.
2. Jalankan AIThread: `dotnet run --project src/AIThread.UI --launch-profile http` lalu buka http://localhost:5225.
   Agar tetap hidup walau browser/terminal ditutup, jalankan di background: `.\scripts\aithread.ps1 start` —
   [Menjalankan di background](01-instalasi.md#5-menjalankan-di-background). Tanpa .NET SDK: `AIThread.exe` standalone
   dengan menu dan ikon tray — [AIThread.exe standalone](01-instalasi.md#6-aithreadexe-standalone-tanpa-net).
3. Menu **Setup** (terbuka sendiri saat AIT pertama kali dipakai) mengerjakan semua langkah awal di satu halaman —
   [Panduan menu → Setup](05-menu.md#setup):
   - **9router**: isi URL & API key → *Test & simpan*.
   - **Model**: pilih model kuat dan model murah → *Buat node & uji* (Planner, Executor, Wrapper, Tools Node, Context Keeper
     dibuat sekaligus).
   - **Project** *(opsional)* dan **CLI**: nyalakan switch Claude Code (atau Codex, Gemini CLI, opencode, Qwen Code, oh-my-pi) —
     [Integrasi Claude Code](03-integrasi-claude-code.md).

   Dalam tim, cukup **Import profil** dari ketua tim lalu isi API key/password sendiri —
   [Profil setup tim](02-konfigurasi.md#profil-setup-tim).
4. Buka sesi Claude Code **baru** di folder project dan beri perintah seperti biasa — [Penggunaan](04-penggunaan.md).
5. *(Opsional)* **Setting → Telegram** untuk mengirim prompt dari HP saat laptop ditinggal —
   [Prompt dari Telegram](11-telegram.md).

## Daftar isi

| # | Dokumen | Isi |
|---|---|---|
| 1 | [Instalasi](01-instalasi.md) | Prasyarat, short memory (Garnet), 9router, database, menjalankan aplikasi (juga di background), `appsettings.json` |
| 2 | [Konfigurasi](02-konfigurasi.md) | Menu Setting (9router, proxy, orkestrasi), menu Node (memilih model per peran), profil setup tim (export/import) |
| 3 | [Integrasi Claude Code](03-integrasi-claude-code.md) | Mengarahkan Claude Code (dan Codex, Gemini CLI, opencode, Qwen Code, oh-my-pi) ke AIT lewat menu CLI, verifikasi, mengembalikan |
| 4 | [Penggunaan sehari-hari](04-penggunaan.md) | Alur kerja, membaca progres, menulis prompt yang efektif, meninjau & membatalkan |
| 5 | [Panduan menu](05-menu.md) | Dashboard, Setup, detail thread, Project, Memory, Node, CLI, Setting |
| 6 | [Memory](06-memory.md) | Short vs long memory, project vs knowledge memory, label (modul · layer · jenis + keyword), peta kode, daftar & gabung modul, migrasi label, siklus hidup, pengelolaan |
| 7 | [Cara kerja](07-cara-kerja.md) | Detail pipeline: klasifikasi request, planner, kontrak, validator, executor, verifikasi (build & reviewer), testing (Auto Testing: browser Playwright & API), wrapper, katalog unit test (baseline, regresi, triase Tester, pemulihan otomatis) |
| 8 | [Troubleshooting](08-troubleshooting.md) | Pesan error umum dan solusinya |
| 9 | [Referensi API](09-referensi-api.md) | Endpoint proxy Anthropic, REST `/api`, event SSE |
| 10 | [Pengembangan](10-pengembangan.md) | Struktur solusi, build, test, migrasi, titik ekstensi |
| 11 | [Prompt dari Telegram](11-telegram.md) | Bot Telegram pribadi untuk prompt jarak jauh: setup, perintah, progres live, keamanan, troubleshooting |
| 12 | [Keamanan & privasi](12-keamanan.md) | Penyamaran secret sebelum ke model, pemulihan saat menulis file, file kredensial yang diblokir, aturan per project, data yang disimpan & dikirim, batasan |

## Istilah

| Istilah | Arti |
|---|---|
| **Thread** | Satu permintaan yang diorkestrasi: rencana → tugas → ringkasan. Punya halaman detail di Dashboard. |
| **Planner** | Node paling cerdas. Membaca kode (read-only), menyusun kontrak, dan memecah pekerjaan menjadi tugas. |
| **Executor** | Node murah. Mengerjakan satu tugas = satu file. Banyak executor berjalan paralel. |
| **Wrapper** | Node yang mengumpulkan semua hasil executor dan menulis balasan untuk Anda. |
| **Kontrak** | Daftar nama yang dipakai lintas file (id elemen, properti DTO, parameter API, kolom DB) — wajib dipakai persis oleh semua executor. |
| **Tugas** | Instruksi untuk satu file (`edit`, `create`, `delete`, `move`, atau `extract`), bisa bergantung pada tugas lain (`dependsOn`). `delete`, `move`, dan `extract` (memindahkan member C# ke file lain, mis. memecah class jadi `partial`) dijalankan AIT tanpa model. |
| **Node** | Konfigurasi satu pekerja AI: peran, model 9router, temperature, max token, batas paralel. |
| **Combo** | Model gabungan di 9router yang otomatis fallback antar provider. Bisa dipilih seperti model biasa. |
| **Passthrough** | Request Claude Code yang tidak diorkestrasi (judul sesi, compact, cek kuota, sesi di luar project) dan diteruskan langsung ke 9router. |
| **Project** | Folder root yang berisi `.git` atau file build (`.sln`, `pom.xml`, `package.json`, `go.mod`, …). Identitas unik sebuah codebase; memory terikat padanya. |
| **Sesi diabaikan** | Sesi yang working directory-nya bukan bagian sebuah project (tidak ada marker). Semua prompt-nya dilewatkan apa adanya — tidak ada planner/executor, project, maupun memory. |
| **Short memory** | Memory baru, disimpan di Garnet (server protokol Redis di dalam AIT) selama ±24 jam. |
| **Long memory** | Memory yang sudah dipindah dari short memory ke database. |
| **Project memory / Knowledge memory** | Pengetahuan khusus satu project / pola umum yang berguna lintas project. |
| **Label memory** | `Modul · Layer · Jenis` (mis. `User · Logic · Rule`; beberapa modul: `Order+Invoice · Logic · Flow`) + maksimal 16 keyword, PascalCase (singkatan kapital pada nama modul dipertahankan: `MKPP`, `KlaimDF`). Modul menyaring, keyword memilih. |
| **Peta kode** | Modul dan layer tiap file project, diputuskan sekali (aturan, model, atau manual) dan dipakai ulang untuk label memory. |
