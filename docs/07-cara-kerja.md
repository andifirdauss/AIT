# 7. Cara kerja

Dokumen ini menjelaskan apa yang terjadi di balik satu prompt, agar Anda bisa menilai hasil dan menyetel
konfigurasi dengan tepat.

## 1. Klasifikasi request

Claude Code mengirim banyak request ke `POST /v1/messages`, tidak hanya prompt Anda. AiThread **mengorkestrasi**
sebuah request hanya bila semua syarat terpenuhi:

- *Orkestrasi aktif* menyala di Setting;
- request membawa daftar `tools` (ciri request agen utama);
- system prompt berasal dari Claude Code / Claude Agent SDK, atau memuat working directory;
- pesan terakhir berasal dari user, **bukan** `tool_result`;
- teks prompt tidak kosong setelah blok `<system-reminder>` dibuang;
- **sesi berasal dari project `.sln`/`.slnx`** (lihat bagian 2).

Selain itu (judul sesi, `/compact`, cek kuota, sesi di luar project, dll.) request diteruskan apa adanya ke
`/v1/messages` 9router dengan **model passthrough** — AiThread tidak membuat thread dan tidak memanggil planner. `POST /v1/messages/count_tokens` juga diteruskan; bila gagal, jumlah token
diperkirakan (±4 karakter per token).

Jika *Proxy API key* diisi, request tanpa key yang cocok (`Authorization: Bearer` atau `x-api-key`) ditolak `401`.

## 2. Cek sesi: project `.sln`/`.slnx` atau bukan

Sebelum planner dipanggil, AiThread memeriksa **sesi Claude Code** asal prompt:

1. Id sesi diambil dari `metadata.user_id` (atau header `x-claude-code-session-id`); working directory dari header
   `X-AiThread-Cwd`, atau dari baris `Primary working directory:` / `Working directory:` di system prompt.
2. Solution diambil dari header `X-AiThread-Solution` bila ada. Jika tidak, `.sln`/`.slnx` dicari:
   di folder itu → di sub-folder hingga 2 tingkat (hanya bila **tepat satu** ditemukan) → di folder induk hingga
   6 tingkat. Bila satu folder berisi beberapa solution, yang bernama sama dengan foldernya diutamakan, lalu `.slnx`.
3. **Solution ditemukan** → project didaftarkan otomatis bila belum ada (prompt pertama sesi), lalu prompt
   diorkestrasi. **Root workspace** = folder solution, kecuali working directory berada di atasnya (mis. root repo
   dengan `src/App.sln`) — maka root = working directory.
4. **Solution tidak ditemukan** (atau working directory tidak ada) → sesi **diabaikan**: setiap prompt-nya diteruskan
   langsung ke 9router dengan model passthrough, sehingga Claude Code tetap bekerja sebagai agen biasa. Tidak ada
   thread, project, maupun memory yang dibuat.

Keputusan ini **diingat per sesi** selama 30 menit — prompt berikutnya di sesi yang sama tidak memindai disk lagi.
Sesi yang terhubung ke project diperpanjang setiap kali dipakai; sesi yang diabaikan dicek ulang setelah 30 menit
(berguna bila Anda baru menambahkan `.sln`). Log dashboard mencatat satu baris per keputusan:
`Sesi xxxxxxxx terhubung ke project …` atau `Sesi xxxxxxxx (…) bukan bagian project .sln/.slnx — prompt diabaikan orkestrasi…`.

Bila pengecekan gagal (mis. database mati), prompt juga diteruskan tanpa orkestrasi dan kesalahannya dicatat di log.

## 3. Planner

Planner menerima **brief**:

- permintaan terbaru developer (tanpa `<system-reminder>`), maks 40.000 karakter; lalu, terpisah, konteks yang disisipkan Claude Code (isi `CLAUDE.md`, reminder, file yang dilampirkan), maks 30.000 karakter. Bila melebihi batas, yang dibuang adalah bagian tengah; awal dan akhir teks tetap ada;
- **konteks sesi** dari Context Keeper bila chat ini sudah punya thread AiThread sebelumnya (lihat
  [Konteks sesi](#konteks-sesi-context-keeper)), maks 12.000 karakter;
- percakapan sebelumnya: tanpa konteks sesi, 12 pesan terakhir; dengan konteks sesi, hanya pesan setelah thread yang
  ringkasannya dipakai. Tiap pesan maks 1.500 karakter, total maks 12.000 karakter;
- info project: nama, root, path solution, daftar project di solution;
- struktur folder 2 tingkat (maks 200 entri);
- indeks memory (id + label, maks 25).

Lalu planner bekerja dalam putaran tool (maks *Langkah planner*). Semua tool **read-only** dan dibatasi ke root:

| Tool | Batas |
|---|---|
| `list_directory(path, depth)` | depth 1–4, maks 300 entri |
| `find_files(pattern)` | glob, mis. `**/*User*.cs`; maks 200 hasil |
| `search_text(query, glob?, regex?)` | tidak peka huruf besar/kecil; file ≤ 1 MB; maks 80 hasil |
| `read_file(path, startLine?, endLine?)` | bernomor baris; maks 600 baris per panggilan; file ≤ 2 MB |
| `search_memory(query)` | maks 25 label |
| `read_memory(ids)` | deskripsi lengkap |
| `read_thread(id)` | catatan lengkap thread sebelumnya di project ini (permintaan, rencana, tugas, balasan) |
| `submit_plan(plan)` | mengakhiri planning |

Folder `.git .vs .idea .nuget bin obj node_modules packages TestResults artifacts` dan `wwwroot/lib/` disembunyikan
dari semua tool. Beberapa tool dalam satu putaran dijalankan paralel. Di putaran terakhir hanya `submit_plan` yang
tersedia.

### Bentuk rencana

```json
{
  "summary": "Menambah field Deskripsi pada form User …",
  "answer": null,
  "contract": [
    { "name": "UI input id", "value": "txtDescription" },
    { "name": "DTO property", "value": "Description" },
    { "name": "DB column", "value": "Description NVARCHAR(500) NULL", "note": "table Users" }
  ],
  "tasks": [
    {
      "id": "T1",
      "title": "Add description textarea to the user form",
      "file": "src/Web/Views/User/Index.cshtml",
      "action": "edit",
      "instruction": "After the txtName form group add …",
      "focus": [{ "startLine": 40, "endLine": 60 }],
      "context": [],
      "dependsOn": []
    },
    {
      "id": "T2",
      "title": "Send description in saveUser",
      "file": "src/Web/wwwroot/js/user.js",
      "action": "edit",
      "instruction": "In saveUser add description: $('#txtDescription').val() …",
      "context": [{ "file": "src/Web/Views/User/Index.cshtml", "startLine": 1, "endLine": 12, "note": "form markup" }]
    }
  ]
}
```

- **Kontrak** adalah kunci akurasi: semua nama lintas file ditetapkan sekali, sehingga tugas bisa berjalan paralel
  tanpa saling melihat.
- `focus` menandai area file target yang besar; `context` melampirkan potongan file lain (read-only) untuk executor.
- Permintaan tanpa perubahan file → `tasks` kosong dan jawaban di `answer`; tidak ada executor yang berjalan.

### Validasi rencana

Rencana ditolak dan dikembalikan ke planner (memakan satu langkah) bila:

- lebih dari 40 tugas, atau `summary` kosong padahal ada tugas;
- id tugas ganda, `instruction` kosong, `action` bukan `edit`/`create`;
- path di luar root, menunjuk folder, atau berada di folder yang disembunyikan;
- `edit` untuk file yang tidak ada, `create` untuk file yang sudah ada, atau dua tugas membuat file yang sama;
- `dependsOn` menunjuk tugas yang tidak ada, dirinya sendiri, atau membentuk siklus.

Path dinormalisasi (`\` → `/`, relatif terhadap root). Penolakan tercatat di log aktivitas thread.

## 4. Eksekusi paralel

- Tugas tanpa ketergantungan langsung berjalan; tugas lain mulai begitu semua `dependsOn`-nya **berhasil**. Jika
  salah satu gagal, tugas penunggu **dilewati**.
- Dua batas berlaku bersamaan: *Maks paralel* (per thread) dan *Max paralel* tiap node (lintas semua thread).
- Executor dipilih: slot kosong terbanyak → prioritas terkecil → bergiliran.
- Waktu eksekusi = rantai tugas terpanjang. Dashboard membandingkannya dengan total waktu kerja executor (speedup).

### Yang dilihat executor

1. Prompt peran executor (+ instruksi tambahan node).
2. Judul, aksi, dan path file target; instruksi; kontrak; ringkasan rencana (sebagai konteks saja).
3. Potongan referensi dari `context` (maks 250 baris per potongan, total maks 40.000 karakter).
4. Isi file target:
   - file ≤ `ExecutorFullFileMaxLines` (400) baris → utuh;
   - file lebih besar → potongan di sekitar `focus` ±40 baris dengan penanda `... (lines X-Y not shown) ...`;
     tanpa `focus` → 400 baris pertama;
   - file baru → keterangan bahwa file belum ada.

### Format jawaban executor

```
<<<<<<< SEARCH
        <input type="text" id="txtName" class="form-control" />
    </div>
=======
        <input type="text" id="txtName" class="form-control" />
    </div>
    <div class="mb-3">
        <label for="txtDescription">Deskripsi</label>
        <textarea id="txtDescription" class="form-control" rows="3"></textarea>
    </div>
>>>>>>> REPLACE
SUMMARY: Added the txtDescription textarea after the name field.
```

Blok dengan SEARCH kosong berarti membuat file baru (isi lengkap di bagian REPLACE).

### Menerapkan edit

1. File target **dibaca ulang di dalam lock per file** — tugas lain di file yang sama mungkin sudah mengubahnya.
2. Setiap blok dicocokkan bertahap: teks persis → baris per baris mengabaikan spasi di akhir baris → mengabaikan
   indentasi (indentasi pengganti disesuaikan). Kecocokan lebih dari satu tempat **ditolak**, tidak ditebak.
3. **All-or-nothing**: file hanya ditulis bila semua blok berhasil.
4. BOM dan gaya line ending (CRLF/LF) file asli dipertahankan; file baru memakai line ending sistem.
5. Bila gagal (tidak ada blok valid, SEARCH tidak ketemu, output terpotong), executor menerima pesan error beserta
   isi file terkini lalu mencoba ulang hingga *Retry executor* kali.

Diff yang ditampilkan di detail thread dibangun dari blok-blok tersebut.

## 5. Wrapper

Wrapper menerima permintaan Anda, ringkasan rencana, kontrak, dan hasil setiap tugas (ringkasan, error, maks 60
baris diff per tugas), lalu menulis balasan dalam bahasa permintaan Anda. Balasan di-stream ke Claude Code.
Jika tidak ada wrapper aktif atau wrapper gagal, AiThread menyusun ringkasan otomatis (✅/❌/⏭️ per tugas).

## 6. Setelah thread

- Status akhir: *Selesai* bila ada tugas yang berhasil (atau jawaban langsung), *Gagal* bila tidak ada.
- Waktu, token per peran, dan setiap tugas tersimpan di database dan tampil di Dashboard.
- Ekstraksi memory berjalan di belakang — lihat [Memory](06-memory.md).
- Untuk thread dari Claude Code (juga yang gagal), Context Keeper memperbarui konteks sesi di belakang.

## Konteks sesi (Context Keeper)

Planner tidak membawa ingatan antar prompt: setiap prompt memulai planner baru. Supaya chat yang panjang tetap
nyambung tanpa mengirim seluruh percakapan, AiThread menyimpan **ringkasan sesi** per thread:

1. Baris pertama setiap balasan orkestrasi adalah penanda ``**AiThread** · `X.sln` · thread `3f2a1b9c` `` (selalu
   dikirim, juga saat progres dimatikan). Penanda ini ikut tersimpan di chat Claude Code.
2. Setelah thread selesai, Context Keeper menulis ringkasan sesi (tujuan, keputusan & batasan, log kerja per thread,
   fakta penting berupa penunjuk file, hal yang belum selesai) dan menyimpannya pada thread itu.
3. Di prompt berikutnya, proxy mencari penanda di history yang dikirim Claude Code, mengambil ringkasan dari thread
   terbaru yang punya ringkasan, dan memberikannya ke planner beserta pesan setelah thread itu saja.

Karena yang dipakai selalu penanda yang masih ada di chat, konteks otomatis mengikuti chat: setelah *rewind* atau
edit pesan, ringkasan kembali ke thread terakhir yang tersisa; `/clear` atau chat baru mulai dari kosong; resume sesi
lama memulihkan ringkasannya. Pencarian dibatasi ke project yang sama dan thread 60 hari terakhir.

- **Model**: node *Context Keeper* aktif dengan prioritas terkecil; tanpa node itu dipakai Wrapper. Tanpa keduanya,
  atau bila modelnya gagal, thread baru dicatat apa adanya di bagian *Not yet summarized* dan dirangkum pada
  pembaruan berikutnya.
- **Mencegah ringkasan melenceng**: ringkasan biasanya diperbarui dari ringkasan sebelumnya, tetapi setiap thread ke-8
  (dan setiap kali ringkasan sebelumnya tidak berasal dari thread tepat sebelumnya) disusun ulang dari catatan
  maksimal 15 thread terakhir di database.
- **Detail tidak hilang**: planner bisa memanggil `read_thread` untuk catatan lengkap thread mana pun, dan
  ringkasan berisi penunjuk file, bukan isi file, sehingga planner membaca versi terbaru.
- Ringkasan terbaru bisa dilihat di detail thread (panel *Konteks sesi dari Context Keeper*).

Untuk sesi Claude Code biasa (termasuk yang tidak lewat proxy), Context Keeper juga bisa menjembatani `/clear`
lewat hook resmi Claude Code — lihat [Context Keeper untuk /clear](03-integrasi-claude-code.md#context-keeper-untuk-clear).

## Batas keamanan

- Planner hanya bisa membaca; executor hanya bisa menulis file target tugasnya — keduanya dibatasi ke root workspace
  dan tidak bisa menyentuh folder yang disembunyikan.
- Tidak ada eksekusi perintah shell.
- AiThread menulis ke folder mana pun yang dikirim sebagai working directory. Biarkan aplikasi hanya mendengarkan
  `localhost`, dan isi *Proxy API key* bila mesin dapat diakses orang lain.
- API key 9router disimpan terenkripsi dengan ASP.NET Core Data Protection.
