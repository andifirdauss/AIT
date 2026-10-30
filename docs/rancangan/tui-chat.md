# Rancangan: Chat TUI di terminal (`aithread chat`)

> Status: **F1–F4 diimplementasikan** (2026-10-29), kode di `src/AIThread.UI/Hosting/Chat/`. Dokumen ini tidak tampil di menu Docs. Perilaku yang berlaku ada di dokumen pengguna ([03-integrasi-cli.md](../03-integrasi-cli.md#chat-terminal-aithread-chat), [09-referensi-api.md](../09-referensi-api.md)); yang tersisa hanya bagian *Pertanyaan terbuka*.
>
> Penyesuaian saat implementasi:
> - `approval.changed` kini juga dikirim per thread saat izinnya dijawab (`threadId`, `projectId`, `nextThreadId`), supaya penonton lain tahu thread mana yang lanjut. Planner yang dijeda dilanjutkan di thread yang sama (`ResumeAsync`), jadi chat mengikuti id itu lagi dan menunggu statusnya bergeser dari `AwaitingInput`; thread baru hanya bila planner tidak bisa dilanjutkan. Chat juga memeriksa `GET …/approval` tiap 2 detik; bila halaman web menjawab lebih dulu, chat lanjut mengikuti thread yang sama.
> - Izin commit/push diminta *setelah* thread berstatus Completed (setelah memory ditulis), jadi chat menunggu sampai 60 detik bila rencana punya `publish` (`Esc` melewati).
> - `Ctrl+V` sering dipakai terminal (Windows Terminal) untuk paste teks, jadi `Alt+V` adalah tombol utama lampiran clipboard. Baris baru juga bisa dengan `Ctrl+J` atau `\` di akhir baris, karena `Alt+Enter` di Windows Terminal = layar penuh.
> - `Esc` saat thread berjalan melepas chat dari thread (thread lanjut), sehingga `/cancel` punya arti. `Ctrl+C` pertama hanya meminta tekan sekali lagi.
> - `Cli.SpawnTray` kini lewat shell (tanpa mewarisi handle) agar `aithread chat -p … | tee` selesai walau AIT baru dijalankan.

## Latar belakang

AIT memiliki web dashboard dan CLI proxy; developer menginginkan chat terminal seperti oh-my-pi (omp) yang berbicara langsung ke pipeline AIT.

| Ada | Belum ada |
|---|---|
| `POST /api/threads` (`ThreadsController.Start`, `StartThreadRequest(ProjectId, Prompt)`) | Terminal client |
| SSE `GET /api/stream?threadId=&types=` (comma-separated type prefixes) | Image attachments on `StartThreadRequest` (controller tidak melewatkan gambar) |
| `GET/POST /api/threads/{id}/approval` (`ApproveRequest(Decision)`) | Mode non-interaktif |
| `GET /api/threads/{id}` (`ThreadDetailDto`) | |
| `ServerClient.GetInfoAsync` (`GET /api/system`) | |
| `ThreadRequest.Images` (`ImageAttachment(MediaType, Data)` raw base64) dipakai oleh jalur proxy | |

## Keputusan

| # | Keputusan |
|---|---|
| 1 | Approval mengikuti aturan yang sudah ada: thread dari chat memakai `ThreadSource.Dashboard`, jadi `CanApprove` = true; thread hanya berhenti minta izin bila planner minta (mis. file di luar folder project, commit/push). Tidak ada aturan approval baru. |
| 2 | `aithread` tanpa argumen tetap membuka menu (`ConsoleMenu`); chat lewat `aithread chat`. |
| 3 | Rendering memakai paket NuGet `Spectre.Console` di `src/AIThread.UI/AIThread.UI.csproj`. |
| 4 | Mode non-interaktif `aithread chat -p "..."` didukung (untuk skrip/CI). |
| 5 | Lampiran gambar bergaya omp: penanda `[Image #N]` di teks prompt, dari clipboard (`Ctrl+V`) atau `@path`. |

## Konsep

### 1. Perintah
```
aithread chat [--project <nama|path>] [--port <n>] [-p "<prompt>"] [-y]
```
`--project` berdasarkan nama terdaftar atau path folder; default = project terdaftar yang root-nya berisi direktori saat ini; jika tidak ada, picker interaktif (Spectre `SelectionPrompt`); di mode `-p` tidak ada project ditemukan = exit code 2. `--port` seperti perintah lain (`CliSettings` port). `-y` hanya dengan `-p`: jawab approval dengan `Once`.

### 2. Server lokal
Client memanggil `ServerClient.GetInfoAsync`; ketika null, ia menjalankan server di latar belakang dengan cara yang sama seperti `aithread tray` (`SpawnTray`) dan menunggu dengan `WaitUntilAsync` (timeout 30 d); kegagalan mencetak error dan keluar dengan code 1. Chat tidak pernah meng-host server di prosesnya sendiri.

### 3. Susunan file

| File | Tanggung jawab |
|---|---|
| `src/AIThread.UI/Hosting/Cli.cs` (ubah) | tambah `chat` ke `Commands`, `case "chat"` di `RunAsync`, help text; opsi `-p/--prompt` dan `--project`. |
| `ChatCommand.cs` | entry point `RunAsync(int port, ChatOptions options)`: pastikan server, resolve project, pilih mode interaktif atau `-p`. |
| `ChatApiClient.cs` | HTTP: start thread, get thread, get/post approval, cancel, SSE reader untuk `/api/stream` (parsing baris `event:`/`data:` menjadi `AppEvent`). |
| `ChatSession.cs` | loop interaktif: baca prompt, kirim, ikuti thread sampai selesai, kembali ke prompt. |
| `PromptEditor.cs` | line editor: multi-line (`Shift+Enter`/`Alt+Enter` baris baru, `Enter` kirim), `Ctrl+V`, `@path`, history (↑/↓). |
| `ImageAttachments.cs` | muat dari file/clipboard, validasi, penomoran `[Image #N]`. |
| `ChatRenderer.cs` | rendering Spectre dari event dan hasil akhir. |
| `ProjectResolver.cs` | `--project`/direktori saat ini/picker. |

### 4. Alur satu prompt
1) user mengetik prompt (+ lampiran);
2) `POST /api/threads` dengan `{ projectId, prompt, images }` mengembalikan thread id;
3) buka `GET /api/stream?threadId={id}&types=thread.,task.,approval.,log` (`log` = log aktivitas thread untuk baris tahap verifikasi/testing);
4) render event;
5) ketika status thread final, `GET /api/threads/{id}` dan cetak ringkasan/jawaban, file yang berubah, hasil test, dan link web `http://localhost:{port}/Thread/Detail/{id}`;
6) kembali ke prompt.

Catatan: setiap prompt adalah thread terpisah (Dashboard tidak punya `SessionId`); kesinambungan percakapan antar prompt di luar cakupan F1–F3 (lihat Pertanyaan terbuka).

### 5. Rendering event

| Event | Tampilan |
|---|---|
| `thread.created`/`thread.updated` | baris status dengan fase (Spectre `Status` spinner) |
| `thread.output` dengan `kind = planner.tool` | baris redup `tool · detail` |
| `task.updated` (`ThreadTaskDto`: TaskKey, Title, FilePath, Status, NodeName, DurationMs) | tabel task live |
| `task.output` (`taskId`, `taskKey`, `text`, `reset`) | disembunyikan secara default, ditampilkan dengan `/verbose` |
| `thread.git` | satu baris branch/commit |
| `approval.requested`/`approval.changed` | kartu approval (bagian 6) |
| `ping` | diabaikan |

Output tanpa TTY (diredirect) memakai teks polos tanpa ANSI.

### 6. Approval
Pada `approval.requested` untuk thread, `GET /api/threads/{id}/approval` (`PendingApprovalDto`: Items, Reason, AllowAlways, Text, AfterThread, AllowPattern, AllowAll) dan tampilkan `Text` beserta item dalam panel. 

Tabel kunci:
| Kunci | Aksi |
|---|---|
| `y` | `Once` |
| `a` | `Always` (hanya saat AllowAlways) |
| `p` | `AlwaysPattern` (hanya saat AllowPattern) |
| `s` | `AlwaysAll` (hanya saat AllowAll) |
| `n` | `Deny` |

Jawaban = `POST /api/threads/{id}/approval` dengan `{ "decision": "<value>" }`. Halaman web mungkin juga menjawab; pada `approval.changed` kartu ditutup.

### 7. Perintah dalam chat

| Perintah | Deskripsi |
|---|---|
| `/help` | bantuan |
| `/project` | ganti project |
| `/cancel` | batalkan thread berjalan via endpoint `Cancel` yang ada |
| `/retry` | endpoint `Retry` yang ada untuk thread terakhir |
| `/open` | buka halaman thread di browser |
| `/verbose` | toggle output task |
| `/clear` | hapus lampiran |
| `/exit` | keluar |

`Ctrl+C`: tekan pertama saat thread berjalan = tanya untuk membatalkannya; di prompt = keluar.

### 8. Lampiran gambar (gaya omp)
- `Ctrl+V` membaca gambar dari clipboard Windows (OS lain: tidak didukung di F2, pakai `@path`);
- mengetik `@path/gambar.png` diikuti spasi/Enter mengubahnya menjadi lampiran (relatif terhadap direktori saat ini, path kutipan diizinkan);
- setiap lampiran menyisipkan `[Image #N]` di kursor, dan daftar di bawah input menampilkan `#N nama-file ukuran`;
- teks seperti `bagian ini [Image #1] lalu [Image #2]` dikirim apa adanya.
- Menghapus penanda menghapus lampirannya; saat kirim, lampiran dinomori ulang sesuai urutan kemunculan sehingga `[Image #N]` cocok dengan Image N planner (urutan `Images`).

Tabel validasi:
| Aturan | Batas |
|---|---|
| Jumlah gambar | maks 6 gambar per prompt |
| Ukuran | maks 6 MB per gambar (byte asli) |
| Format | png, jpg/jpeg, gif, webp, dicek via file signature (magic bytes), MediaType `image/png`, `image/jpeg`, `image/gif`, `image/webp` |

Pelanggaran menampilkan pesan di TUI dan lampiran tidak ditambahkan.

### 9. Perubahan API
```csharp
public record ImageAttachmentDto(string MediaType, string Data); // Data: base64 tanpa prefix "data:"
public record StartThreadRequest(Guid ProjectId, string Prompt, IReadOnlyList<ImageAttachmentDto>? Images = null);
```
- keduanya di `src/AIThread.Common/DTOs/ThreadDtos.cs`;
- `ThreadsController.Start` memvalidasi (batas sama dengan bagian 8, base64 tidak valid = error validasi, pesan via `Lang.T`) dan memetakan ke `ThreadRequest.Images` (`ImageAttachment`);
- `[RequestSizeLimit(50_000_000)]` pada `Start` karena 6 × 6 MB base64 melebihi default Kestrel 30 MB;
- web dashboard tetap tidak mengirim gambar (field opsional, backward compatible).

### 10. Mode non-interaktif (`-p`)
- prompt dari `-p`;
- token `@path` di dalamnya menjadi lampiran;
- tanpa line editor, tanpa spinner;
- baris progress dicetak polos ke stdout, error ke stderr;
- ringkasan/jawaban akhir dicetak di akhir.

Tabel exit code:
| Code | Arti |
|---|---|
| 0 | thread selesai sukses |
| 1 | thread gagal/dibatalkan atau server tidak bisa dijalankan |
| 2 | argumen salah atau project tidak ditemukan |
| 3 | thread menunggu approval dan `-y` tidak dipakai (thread dibiarkan menunggu, link halaman thread dicetak) |

## Fase

| Fase | Isi |
|---|---|
| F1 | `aithread chat` interaktif: Cli, ChatCommand, ChatApiClient (+SSE), ChatSession, PromptEditor (tanpa lampiran), ChatRenderer, ProjectResolver, approval. |
| F2 | Lampiran: perubahan API (bagian 9), ImageAttachments, `Ctrl+V`, `@path`. |
| F3 | Mode `-p` dan exit code. |
| F4 | Dokumen pengguna: 03-integrasi-cli.md, 09-referensi-api.md (dokumen EN juga di docs/en). |

Unit test (xunit, tests/AIThread.Tests) per fase: SSE parser, project resolution, image validation (count/size/signature), marker renumbering, controller validation of `Images`.

## Skenario

1. prompt biasa di folder project terdaftar → thread jalan, tabel task tampil, ringkasan + link.
2. plan mengubah file di luar project → kartu approval, `y` → thread lanjut.
3. dua gambar via Ctrl+V dan @path → teks berisi `[Image #1]` dan `[Image #2]`, API menerima 2 Images berurutan.
4. gambar ke-7 atau file 8 MB atau .bmp → ditolak dengan pesan, tidak ditambahkan.
5. `aithread chat -p "perbaiki typo" --project AIT` di CI → output polos, exit 0.
6. `-p` dan butuh approval tanpa `-y` → exit 3 + link.

## Pertanyaan terbuka

- Kesinambungan percakapan antar prompt (memberi `SessionId` per sesi chat) mengubah `canAsk` di `ThreadOrchestrator.ExecuteAsync` (planner bisa bertanya), jadi diputuskan terpisah setelah F3.
