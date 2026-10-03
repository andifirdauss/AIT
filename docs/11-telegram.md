# 11. Prompt dari Telegram

Kirim prompt ke AiThread dari HP saat laptop ditinggal menyala di kantor atau di rumah. Anda chat dengan bot
Telegram pribadi; AiThread di laptop menjalankan planner → executor → wrapper seperti biasa, melaporkan progres ke
chat, lalu mengirim ringkasannya.

```
HP (Telegram) ──► server Telegram ◄──(long polling, koneksi keluar)── AiThread di laptop ─► Planner ─► Executor × N ─► Wrapper
              ◄── status live & ringkasan ─────────────────────────────┘
```

AiThread **menarik** pesan dari Telegram (long polling `getUpdates`). Yang dibutuhkan hanya koneksi keluar HTTPS ke
`api.telegram.org` — tidak perlu IP publik, port forwarding, atau tunnel seperti ngrok, jadi tetap bekerja di balik
NAT/firewall kantor.

## Prasyarat

- AiThread berjalan di laptop, dan laptop tetap menyala serta terhubung internet (lihat
  [Agar laptop tetap bisa dihubungi](#agar-laptop-tetap-bisa-dihubungi)).
- Node Planner, Executor, dan Wrapper sudah dikonfigurasi; minimal satu project terdaftar di menu **Project**.
- Akun Telegram.

## Setup (sekali, di depan laptop)

1. **Buat bot.** Di Telegram buka `@BotFather` → `/newbot` → isi nama dan username (harus berakhiran `bot`) →
   salin **token** (format `123456789:AAE…`). Perlakukan token seperti password.
2. **Setting → tab Telegram** (langsung: http://localhost:5225/Setting#telegram): tempel token, centang *Aktifkan bot Telegram*, klik **Simpan Telegram**. Token
   diverifikasi ke Telegram sebelum disimpan; badge berubah menjadi **Aktif · @nama_bot**.
3. Di HP, buka bot Anda (klik `@nama_bot` di kartu, atau cari di Telegram) lalu tekan **Start**. Bot membalas bahwa
   chat belum diizinkan, beserta ID chat Anda.
4. Kembali ke kartu Telegram: chat Anda muncul sebagai **Menunggu izin**. Cocokkan nama dan ID, lalu klik
   **Izinkan**. Bot mengirim pesan konfirmasi.
5. Di Telegram kirim `/projects` dan pilih project lewat tombol. Bila hanya ada satu project terdaftar, project itu
   dipilih otomatis pada prompt pertama.

Setelah itu laptop boleh ditinggal.

## Mengirim prompt

Kirim pesan teks biasa, seperti mengetik di Claude Code. Bot membalas pesan Anda dengan **pesan status** yang
diperbarui terus (pembaruan tidak memunculkan notifikasi):

```
🧵 MyApp · thread 1a2b3c4d
⚙️ Eksekusi · 1/3 tugas · 42s

📋 Menambah kolom deskripsi pada form user.

✅ T1 Add description input — Views/User/Index.cshtml
🔄 T2 Send description field — wwwroot/js/pages/user.js
⏳ T3 Validate description — Services/UserService.cs
```

- Selama planning, baris 🔍 menampilkan tool yang sedang dipakai planner (`read_file`, `search_text`, …).
- Tombol **⛔ Batalkan** di bawah pesan status menghentikan thread.
- Setelah selesai, bot mengirim **pesan baru** (dengan notifikasi) berisi status akhir, jumlah tugas berhasil, durasi,
  dan ringkasan wrapper. Heading, list, bold, blok kode, dan tabel Markdown dikonversi ke format Telegram; ringkasan
  panjang dipecah menjadi beberapa pesan.
- Kirim `/diff` untuk melihat perubahan file thread tersebut.

Pertanyaan yang tidak butuh perubahan file (mis. "jelaskan alur login di project ini") dijawab langsung oleh planner.
Satu chat menjalankan **satu thread pada satu waktu**; prompt berikutnya ditolak sampai thread selesai atau dibatalkan.

Thread dari Telegram tercatat seperti thread lain: sumber **Telegram** di Dashboard, sesi `telegram:<id chat>` di
halaman Project, dan memory diekstrak seperti biasa.

## Perintah

| Perintah | Fungsi |
|---|---|
| *(teks biasa)* | Menjalankan prompt di project aktif. Bila belum ada project aktif, bot menampilkan pilihan project lalu menjalankan prompt setelah Anda memilih. |
| `/projects` | Daftar project terdaftar dengan tombol untuk memilih. |
| `/project <nomor atau nama>` | Memilih project tanpa tombol. Nama boleh sebagian selama hanya cocok dengan satu project. |
| `/status` | Project aktif, thread chat ini yang sedang berjalan, dan 5 thread terakhir dari **semua** sumber (termasuk Claude Code). |
| `/cancel` | Membatalkan thread chat ini. `/cancel <id>` membatalkan thread lain yang sedang berjalan — id 8 karakter dari `/status`, misalnya thread Claude Code yang tertinggal di laptop. |
| `/diff` | Diff perubahan file thread terakhir chat ini. `/diff <id>` untuk thread lain. |
| `/new` | Memulai percakapan baru: prompt berikutnya dikirim tanpa riwayat. |
| `/help`, `/start` | Bantuan singkat dan project aktif. |

Daftar perintah juga muncul di tombol menu `/` Telegram.

## Percakapan lanjutan

Seperti di Claude Code, prompt berikutnya membawa riwayat percakapan untuk planner: hingga 6 pertukaran terakhir
(prompt + ringkasan) dari chat ini **di project yang sama**, dipotong bila panjang. Jadi "sekarang tambahkan juga di
halaman detail" bisa dipahami tanpa mengulang konteks. Riwayat mulai dari nol setelah `/new` atau ganti project.

## Keamanan

- Hanya **chat pribadi yang diizinkan** di menu Setting yang bisa menjalankan prompt. Pesan dari grup dan channel
  diabaikan.
- Izinkan hanya akun Telegram Anda sendiri: sama seperti Claude Code, prompt dapat mengubah file project di laptop.
- Chat asing yang mengirim pesan ke bot tercatat sebagai *Menunggu izin* (20 terbaru disimpan) dan log dashboard
  menampilkan peringatan. Klik **Hapus** bila tidak Anda kenal; **Cabut** menghentikan akses chat yang sudah diizinkan
  seketika.
- Thread hanya berjalan di project terdaftar, dan executor tetap dibatasi ke folder project.
- Token disimpan terenkripsi (ASP.NET Core Data Protection), tidak pernah dikirim balik ke browser (hanya tersamar),
  dan tidak ditulis ke log.
- Prompt yang terkirim saat AiThread tidak berjalan (laptop mati/sleep, bot nonaktif) **tidak dijalankan** ketika
  AiThread hidup kembali — bot membalas agar Anda mengirim ulang. Perintah seperti `/status` tetap dijawab.
- AiThread tidak perlu dibuka ke internet. Jangan mem-forward port 5225 hanya untuk fitur ini.

## Agar laptop tetap bisa dihubungi

- Matikan sleep saat laptop dicolok: Windows **Settings → System → Power & battery** → sleep saat *plugged in* =
  **Never**, atau jalankan `powercfg /change standby-timeout-ac 0`. Bila lid akan ditutup: **Control Panel → Power
  Options → Choose what closing the lid does** → *When plugged in* = **Do nothing**.
- Jalankan AiThread di background (`.\scripts\aithread.ps1 start`, opsional `autostart-on`) agar tidak ikut mati saat
  browser, terminal, atau Visual Studio ditutup — lihat [Menjalankan di background](01-instalasi.md#5-menjalankan-di-background).
- Pastikan AiThread dan 9router berjalan sebelum pergi, lalu uji sekali dengan `/status` dari HP.
- Bila koneksi internet putus, AiThread mencoba lagi otomatis (jeda naik hingga 60 detik) dan log dashboard mencatat
  saat koneksi ke Telegram pulih.

## Status di kartu Telegram

| Status | Arti |
|---|---|
| **Nonaktif** | Bot dimatikan atau token belum diisi. |
| **Menghubungkan…** | Token sedang diverifikasi dan long polling dimulai. |
| **Aktif** · `@bot` · *cek terakhir …* | Menunggu pesan. *Cek terakhir* diperbarui setiap siklus long polling (±50 detik saat tidak ada pesan). |
| **Bermasalah** | Penyebab tampil di bawahnya: token ditolak, Telegram tidak terjangkau, atau token dipakai program lain. |

Tabel chat menampilkan nama, username, ID, status izin, project aktif, dan waktu pesan terakhir; tabel diperbarui
langsung ketika chat baru masuk.

## Konfigurasi `appsettings.json`

Bagian `AiThread:Telegram` (token dan aktif/nonaktif diatur dari menu Setting, bukan di sini):

| Kunci | Default | Fungsi |
|---|---|---|
| `ApiBaseUrl` | `https://api.telegram.org` | Endpoint Bot API. Ganti bila memakai [Bot API server](https://github.com/tdlib/telegram-bot-api) sendiri atau proxy. |
| `PollTimeoutSeconds` | `50` | Lama satu panggilan `getUpdates` menunggu pesan baru (1–50). |

HTTP proxy sistem Windows ikut dipakai secara otomatis.

## Troubleshooting

| Gejala | Penyebab | Solusi |
|---|---|---|
| Simpan: *Token ditolak Telegram* | Token salah, terpotong, atau sudah di-revoke. | Salin ulang dari `@BotFather` → `/mybots` → bot Anda → *API Token*. |
| Simpan: *Token tidak dapat diverifikasi: … tidak dapat dihubungi* | Jaringan memblokir `api.telegram.org`. | Periksa internet/proxy kantor; atur `ApiBaseUrl` bila harus lewat proxy. |
| Bermasalah: *Token ini sedang dipakai program lain…* | Instance AiThread lain (atau program lain) menerima pesan dengan token yang sama. | Hentikan instance lain, atau buat bot terpisah untuk tiap instance. |
| Bot tidak membalas sama sekali | AiThread tidak berjalan, laptop sleep/mati, atau bot nonaktif. | Periksa laptop. Setelah hidup kembali, kirim ulang prompt. |
| Bot membalas *Chat ini belum diizinkan* | Chat belum diizinkan. | Setting → Telegram → **Izinkan**. |
| *Thread … masih berjalan* | Satu chat hanya menjalankan satu thread. | Tunggu sampai selesai, atau `/cancel`. |
| Pesan status berhenti diperbarui | Pesan status dihapus, atau jaringan sempat putus. | Hasil akhir tetap dikirim sebagai pesan baru; cek `/status`. |
| *AiThread berhenti saat thread … berjalan* | AiThread dimatikan atau di-restart dengan rapi saat thread berjalan. | Setelah AiThread hidup lagi, tinjau tugas yang sempat selesai dengan `/diff <id>`, lalu kirim ulang prompt bila perlu. |
| Hasil akhir sebuah prompt tidak pernah datang | AiThread mati mendadak (crash, listrik, laptop sleep) saat thread berjalan — tidak sempat memberi tahu. | Cek `/status`, lalu seperti baris di atas. |
| Ringkasan tampil tanpa format | Telegram menolak format pesan itu, sehingga AiThread mengirimnya ulang sebagai teks biasa. | Tidak perlu tindakan. |
| *Thread gagal: Belum ada node Planner aktif* | Pipeline belum lengkap. | Lengkapi node di menu Node (harus dari laptop). |
