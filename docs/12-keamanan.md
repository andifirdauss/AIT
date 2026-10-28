# Keamanan & privasi

AIT membaca kode Anda supaya model AI bisa merencanakan dan mengubahnya. Semua isi itu melewati AIT dulu sebelum sampai
ke model, jadi AIT bisa mengatur apa yang ikut terkirim dan apa yang tersimpan. Halaman ini menjelaskan apa yang
dilakukan AIT, cara mengaturnya, dan batasannya.

Penjelasan singkatnya muncul sebagai popup **Keamanan & privasi** saat AIT pertama kali dibuka. Popup itu bisa dibuka
lagi kapan saja dari sidebar (grup Bantuan).

```
File & prompt Anda ──► AIT ── secret → {{AIT_SECRET:…}} ──► 9router ──► model
       ▲                 │
       └── nilai asli ◄──┘  (hanya saat AIT / Claude Code menulis ke file Anda)
```

## Ringkasan

| Perlindungan | Apa yang dilakukan |
|---|---|
| Penyamaran secret | Password, API key, token, JWT, private key, dan password di connection string atau URL diganti placeholder sebelum dikirim ke model. |
| Pemulihan saat menulis | Nilai asli hanya ada di memori proses AIT, lalu dipasang kembali saat executor menulis file, Tester memanggil aplikasi, atau Claude Code menjalankan Edit/Write/Bash (lewat hook). |
| File kredensial diblokir | Kunci, sertifikat, kunci SSH, file token, dan folder kredensial di profil Windows tidak pernah dibaca atau diubah agen AIT. |
| Tidak tersimpan | Prompt, riwayat chat, log aktivitas, catatan investigasi planner, memory, wiki bisnis, dan file export memory disamarkan sebelum disimpan. |
| Lokal | Database, memory, dan log ada di komputer Anda. Tidak ada telemetri. |

## Penyamaran secret

### Yang dikenali

- **Nilai pengaturan yang namanya menunjukkan secret**, misalnya `Password`, `DB_PASSWORD`, `smtpPassword`, `ApiKey`,
  `Jwt:Key`, `ClientSecret`, `access_token`, `APP_KEY`, atau `passphrase`. Pola ini dikenali di JSON (`"Password": "…"`),
  C#/Python/JS (`Password = "…"`), PHP (`'password' => '…'`), XML/web.config (`<add key="SmtpPassword" value="…"/>`,
  `password="…"`, `<Password>…</Password>`), dan `.env` (`DB_PASSWORD=…`). Di file konfigurasi (`.yml`, `.yaml`, `.ini`,
  `.properties`, `.toml`, `.conf`, `.cfg`, `.env*`), baris tanpa tanda kutip seperti `password: rahasia` juga dikenali.
- **Password di connection string**: `Password=…`, `Pwd=…`, `AccountKey=…`, `SharedAccessKey=…`. Bagian lain seperti
  `Server` dan `Database` tetap terlihat.
- **Password di URL**: `postgres://user:rahasia@host`.
- **Format token yang dikenal**: AWS access key, token GitHub/GitLab, API key Anthropic/OpenAI/Google, token Slack,
  npm, Stripe, bot Telegram, JWT, dan blok `-----BEGIN … PRIVATE KEY-----`.
- **Secret yang sudah pernah ditemukan**: nilai yang cukup panjang dan acak (minimal 8 karakter dengan campuran huruf,
  angka, atau simbol) ikut disamarkan di mana pun ia muncul lagi, misalnya saat password dari `appsettings.json`
  ditempel ke prompt. Nilai yang hanya berisi huruf (dengan `_`, `.`, `-`, `/`, atau `:` di antaranya) seperti
  `LunchClaimRateHeaderDto` dianggap nama kode: tetap disamarkan di tempat ia menjadi secret, tetapi tidak di tempat lain,
  agar nama class dan file tidak ikut tersamarkan.

Nama pengaturan yang hanya *membicarakan* secret tidak disamarkan, misalnya `PasswordLength`, `TokenEndpoint`,
`ApiKeyHeaderName`, atau `RequireUppercase`. Begitu juga nilai yang merujuk ke tempat lain, seperti `${DB_PASS}`,
`%PASSWORD%`, `{{password}}`, atau `****`.

### Bentuk placeholder

```
"ConnectionStrings": { "Default": "Server=.;Database=Toko;User Id=sa;Password={{AIT_SECRET:Password:3f2a9c1b7e}};" }
"Jwt": { "Key": "{{AIT_SECRET:Key:91d0c4aa52}}" }
```

- Label (`Password`, `Key`, `DB_PASSWORD`, `jwt`, …) memberi tahu model nilai apa yang disembunyikan.
- Id adalah HMAC-SHA256 dari nilainya dengan kunci yang hanya ada di komputer ini
  (`%LOCALAPPDATA%\AIThread\secret-mask.key`, dienkripsi dengan ASP.NET Data Protection). Nilainya tidak bisa ditebak
  dari placeholder. Secret yang sama selalu mendapat placeholder yang sama, juga setelah AIT di-restart, sehingga
  prompt cache provider tetap efektif.

### Di mana disamarkan

| Jalur | Keterangan |
|---|---|
| Semua panggilan model AIT | Planner, tools node, executor, reviewer, Tester, wrapper, ekstraksi memory, penulis wiki, dan Skill Orchestrator disaring di satu titik sebelum dikirim ke 9router. |
| Tool planner | `read_file` dan `search_text` membaca isi file dengan aturan file konfigurasi. `git_diff`/`git_show`/`git_blame`, pencarian simbol, output build/test, dan memory juga disaring. |
| Executor | Isi file yang dikerjakan dan potongan referensi dari file lain. |
| Tester | Respons `http_request` dan halaman browser, misalnya token dari endpoint login. |
| Prompt dari CLI | Prompt, konteks prompt, dan riwayat percakapan Claude Code/Codex/Gemini, termasuk file yang dibaca CLI itu sendiri. |
| Passthrough | Request yang diteruskan apa adanya ke 9router (judul sesi, compact, sesi di luar project). Bisa dimatikan terpisah di Pengaturan. |

### Pemulihan nilai asli

Nilai asli disimpan di memori proses AIT (vault), tidak pernah ke disk. Nilai itu dipakai kembali hanya di tempat AIT
sendiri yang bertindak:

- **Executor**: blok SEARCH/REPLACE yang berisi placeholder diganti dengan nilai asli tepat sebelum diterapkan ke file.
  Sebelumnya AIT membaca ulang file target, jadi placeholder yang disalin dari file itu tetap bisa dipulihkan setelah
  restart. Placeholder yang tidak dikenali membuat edit ditolak dan executor diminta mengulang. Diff yang ditampilkan
  di laporan dan disimpan di thread tetap memakai placeholder.
- **Tool planner dan rencana**: path atau nama yang disalin model dari output yang disamarkan (`read_file`,
  `find_references`, …, serta file tugas di `submit_plan`) dipulihkan sebelum tool berjalan dan sebelum rencana
  divalidasi, jadi file tersebut tetap bisa dibaca dan diubah. Output tool tetap disamarkan lagi.
- **Tester**: token yang tadi dikembalikan aplikasi (dan disamarkan) dipasang kembali di request berikutnya, sehingga
  alur login → panggil API tetap berjalan.
- **Claude Code** (hook `PreToolUse`): sebelum Edit, MultiEdit, Write, NotebookEdit, atau Bash dijalankan, AIT mengganti
  placeholder di input tool dengan nilai aslinya. Claude Code tetap meminta konfirmasi Anda, kecuali sesi memakai mode
  `acceptEdits` (untuk edit file) atau `bypassPermissions`. Placeholder yang tidak dikenali menghentikan tool dengan
  pesan ke model, sehingga tidak tertulis ke file apa adanya. Endpoint hook ini hanya menjawab request dari komputer
  ini (loopback). Pasang hook di menu **CLI → Claude Code** ([Integrasi CLI](03-integrasi-cli.md#context-keeper-untuk-clear)).

> CLI lain (Codex, Gemini CLI, opencode, Qwen Code, oh-my-pi) belum punya hook seperti ini. Dalam mode passthrough,
> model bisa menulis placeholder ke file. Bila CLI tersebut sering mengedit file berisi secret, matikan
> **Samarkan juga request passthrough** di Pengaturan. Thread yang diorkestrasi AIT tetap aman karena file ditulis oleh
> executor AIT.

## File kredensial yang diblokir

Agen AIT (planner, tools node, executor) tidak membaca dan tidak mengubah file berikut. `list_directory` dan
`find_files` tetap menampilkan namanya dengan tanda `(protected: credential file)`, dan `search_text` melewatinya.

| Jenis | Contoh |
|---|---|
| Kunci & sertifikat | `*.pem`, `*.key`, `*.pfx`, `*.p12`, `*.jks`, `*.keystore`, `*.snk`, `*.p8`, `*.ppk`, `*.kdbx` |
| Kunci SSH | `id_rsa`, `id_dsa`, `id_ecdsa`, `id_ed25519`, folder `.ssh` |
| File token | `.git-credentials`, `.npmrc`, `.pypirc`, `.netrc`, `.pgpass`, `.htpasswd`, `secrets.json`, `auth.json`, `credentials.json`, `client_secret.json`, `.dockercfg`, `.s3cfg` |
| Folder profil | `~/.aws`, `~/.azure`, `~/.kube`, `~/.docker`, `~/.gnupg`, `~/.config/gcloud`, `~/.codex`, `~/.omp`, `%APPDATA%\Microsoft\UserSecrets`, `%APPDATA%\Microsoft\Protect`, `%APPDATA%\Microsoft\Credentials`, `%APPDATA%\9router`, `%LOCALAPPDATA%\AIThread`, kunci Data Protection ASP.NET |

Saat CLI **Claude Code** diarahkan ke AIT (menu CLI), aturan `permissions.deny` yang setara (`Read(//**/*.pem)`,
`Read(~/.ssh/**)`, …) ditambahkan ke `settings.json`. Dengan begitu Claude Code sendiri juga tidak membacanya.
Aturan buatan Anda tidak disentuh, dan saat integrasi dilepas, hanya aturan dari AIT yang dihapus.

## Izin di luar batas project

Perubahan file di luar folder project, perintah `run_command` yang bisa mengubah sesuatu, `curl` ke host luar, serta
commit/push selalu menunggu [izin developer](04-penggunaan.md#izin-developer) (sekali atau selalu). Batas yang tidak
bisa diizinkan:

- file kredensial di atas tetap ditolak untuk dibaca, ditulis, maupun disebut dalam perintah;
- folder Windows, Program Files, dan ProgramData, juga drive root dan folder Home/Desktop/Documents/Downloads sebagai
  target folder (hanya file tunggal di dalamnya yang bisa diizinkan);
- push tidak pernah memakai `--force`, dan push ke `main`/`master`/`production`/`release/*` tidak bisa diizinkan
  *selalu*;
- commit hanya memuat file yang ditulis thread, bukan `git add -A`.
- pola izin perintah (`npm install *`) hanya menutupi perintah tunggal tanpa pipe/operator/`$()`/backtick, dan tidak
  pernah ditawarkan untuk perintah yang menghapus/memindah file, membungkus perintah lain (`sudo`, `bash -c`, `xargs`,
  `curl`, `ssh`, …), memublikasikan (`push`, `publish`, `deploy`), atau mengubah repository git;
- izin *semua perintah* (grant perintah `*`) hanya diberikan bila developer memilihnya secara eksplisit (opsi 5); setelah
  itu planner bisa menjalankan perintah apa pun di project tersebut tanpa bertanya, kecuali yang menyebut file
  kredensial. Host luar, file di luar project, dan commit/push hasil thread tetap punya izin sendiri;
- `web_fetch` hanya GET, tanpa cookie/kredensial, setiap redirect ke host lain dicek ulang, dan alamat metadata cloud
  (`169.254.169.254`, `metadata.google.internal`, …) selalu ditolak, juga bila nama host baru menunjuk ke sana setelah DNS;
- proses latar belakang (`start_process`) maks 3 per thread, dihentikan beserta proses anaknya setelah rencana diserahkan,
  saat thread berakhir, atau setelah 30 menit;
- branch baru tidak pernah menimpa branch yang sudah ada, dan PR dibuka tanpa shell (`gh`/`glab` dengan prompt
  dimatikan).

Output perintah disamarkan seperti isi file konfigurasi (`KEY=nilai` yang rahasia diganti placeholder) sebelum sampai ke
model. Izin *selalu* tersimpan di database lokal mesin ini saja.

## Aturan per project

Tambahkan bagian `secrets` di `.aithread/project.json` (file yang sama dengan `run` untuk Auto Testing). File ini ikut
di-commit, jadi berlaku untuk seluruh tim:

```json
{
  "secrets": {
    "deny":  ["config/production/**", "*.secret.json"],
    "allow": ["tests/certs/test.pem"],
    "keys":  ["LicenseCode", "ConnectionKey"]
  }
}
```

| Kunci | Arti |
|---|---|
| `deny` | Glob (relatif ke root project, seperti `.gitignore`) untuk file tambahan yang tidak boleh dibaca atau diubah. Nama tanpa `/` berlaku di semua folder. |
| `allow` | File kredensial yang *boleh* dibaca, misalnya sertifikat dummy untuk test. Mengalahkan `deny` dan daftar bawaan. |
| `keys` | Nama pengaturan tambahan yang nilainya selalu disamarkan, untuk nama yang tidak terdeteksi otomatis. |

## Pengaturan

**Pengaturan → Keamanan & privasi**:

| Opsi | Default | Arti |
|---|---|---|
| Samarkan secret sebelum dikirim ke model dan sebelum disimpan | Aktif | Mematikannya membuat isi file, prompt, dan output tool dikirim apa adanya. Blokir file kredensial tetap berlaku. |
| Samarkan juga request passthrough | Aktif | Lihat catatan CLI lain di atas. |

Tab ini juga menampilkan hitungan sejak AIT berjalan: secret yang disamarkan, secret berbeda yang bisa dipasang kembali,
berapa kali dipasang kembali, dan akses file kredensial yang ditolak. Di akhir setiap thread, footer laporan
menampilkan `🔒 N secret disamarkan` dan log aktivitas mencatatnya di kategori `keamanan`.

### Secret yang terdeteksi (bukan secret)

Kartu **Secret yang terdeteksi** di tab yang sama mendaftar nilai yang disamarkan sejak AIT berjalan: label tempat ia
ditemukan (nama pengaturan seperti `ref_token`, atau jenis token seperti `jwt`), cuplikan beberapa karakter di kedua
ujungnya, panjangnya, berapa kali disamarkan, dan kapan terakhir terlihat. **Tampilkan** memperlihatkan nilai aslinya,
hanya dari browser di komputer ini.

Bila yang tersamarkan ternyata bukan secret, misalnya nama class, nama file, atau potongan kode, klik **Bukan secret**:

- Nilai itu tidak disamarkan lagi di mesin ini, di mana pun ia muncul (prompt, isi file, output tool, passthrough).
- Yang disimpan di database hanya id placeholder (HMAC dengan kunci mesin ini), label, dan cuplikannya, bukan nilai
  aslinya. Daftarnya berlaku per mesin dan tetap ada setelah restart.
- Placeholder yang sudah terkirim sebelumnya tetap bisa dipulihkan.
- **Samarkan lagi** mengembalikannya ke perilaku semula.

Setelah AIT di-restart, nilai yang belum ditandai baru muncul lagi di daftar saat disamarkan lagi.

## Data yang disimpan dan dikirim

- **Disimpan lokal**: database AIT (SQL Server), short memory (Garnet di dalam proses), log di `%LOCALAPPDATA%\AIThread`,
  dan snapshot file untuk rollback (isi file apa adanya, termasuk secret yang memang ada di file Anda). Long memory dan
  wiki bisnis bisa dipindah ke database tim (Pengaturan → Penyimpanan memory); isinya sudah disamarkan.
- **Dikirim keluar**: ke 9router (lalu ke provider model yang Anda pilih), ke Telegram bila diaktifkan, cek versi
  terbaru di registry npm (dari browser), serta unduhan yang Anda minta (sumber skill dari GitHub/GitLab, Node.js dan
  browser untuk Auto Testing). AIT tidak mengirim telemetri.
- **Kunci & token**: API key 9router dan token bot Telegram disimpan terenkripsi (ASP.NET Data Protection) dan tidak
  pernah dikembalikan lewat API. Login yang Anda berikan untuk testing hanya ada di memori, dan model hanya melihat
  password sebagai `{{password}}` ([Cara kerja → Testing](07-cara-kerja.md)).
- **Tester** hanya boleh mengakses `localhost`.
- **Log & diagnostik**: insiden (error, stack trace, output mentah executor) disimpan di
  `%LOCALAPPDATA%\AIThread\diagnostics` dan tidak dikirim ke mana pun. ZIP export dibuat di mesin ini; secret yang
  dikenali pola diganti `[secret:<label>]`, tapi kode, path file, dan prompt tetap ada — bagikan hanya ke orang yang
  boleh melihatnya ([Log & diagnostik](05-menu.md#log--diagnostik)).

## Batasan

- Penyamaran bekerja berdasarkan pola dan nama setting. Secret dengan format yang tidak lazim, atau yang ditulis langsung
  di kode tanpa nama yang jelas (misalnya `Connect("admin", "rahasia")`), bisa lolos. Simpan secret di user-secrets,
  environment variable, atau secret manager, dan tambahkan nama pengaturan khusus di `secrets.keys`.
- Mematikan penyamaran tidak menghapus placeholder yang sudah terkirim; nilainya tetap bisa dipulihkan selama AIT
  berjalan atau file sumbernya masih ada.
- Vault ada di memori. Setelah restart, placeholder lama hanya bisa dipulihkan dari file yang masih berisi nilai yang
  sama (AIT membaca ulang file target sebelum menulis). Placeholder untuk nilai yang sudah tidak ada di mana pun akan
  ditolak, bukan ditulis apa adanya.
