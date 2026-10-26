# Rancangan: Memory & wiki bisnis yang mengikuti git

> Status: **F1 dan F2 sudah diimplementasikan** (2026-10-26), F3–F4 rancangan. Dokumen ini tidak tampil di menu Docs. Perilaku yang sudah
> berlaku dipindah ke dokumen pengguna ([06-memory.md](../06-memory.md), [07-cara-kerja.md](../07-cara-kerja.md),
> [09-referensi-api.md](../09-referensi-api.md)) setiap fase selesai.

## Latar belakang

Satu project disentuh banyak developer, di banyak branch, dengan banyak commit. Memory dan wiki bisnis AIT saat ini
hanya terikat ke `ProjectKey`:

| Ada | Belum ada |
|---|---|
| `LongMemory.ProjectKey`, `ProjectName`, `SourceThreadId` | Siapa yang menulis, di branch apa, dari commit mana |
| `ThreadFileSnapshot.OriginalHash` / `AfterHash` (SHA-256) per file thread | Hubungan ke commit: hash hanya dipakai rollback |
| `GitTools.HeadAsync`, `ContainsAsync` (`merge-base --is-ancestor`) | Baru dipakai katalog unit test (`SourceCommit/SourceBranch`) |
| `BusinessPage` + `BusinessPageRevision` (versi, `Author`, `SourceThreadIds`) | Konsep environment (dev, staging, prod, …) |

Akibatnya AIT tidak bisa menjawab:

- Memory ini dari siapa, branch apa, commit apa?
- Perubahan yang mendasari memory ini sudah di-commit? Sudah di-push? Sudah masuk dev / staging / prod?
- Perubahan AIT dibuang developer di luar AIT (rollback, `checkout -- .`, `reset --hard`)?
- File yang diubah AIT kemudian diubah lagi di luar AIT?
- Wiki bisnis versi prod berbeda apa dengan versi staging?

## Keputusan

| # | Keputusan |
|---|---|
| 1 | Memory dari branch yang **belum di-merge** tetap terlihat developer lain, diberi label branch-nya dan **peringkat lebih rendah** di pencarian planner. |
| 2 | **Squash merge** (dan rebase / cherry-pick) didukung sejak awal: pencocokan perubahan memakai **blob git** dan `patch-id`, bukan SHA commit. |
| 3 | AIT boleh menjalankan **`git fetch` otomatis** berkala (bisa diatur / dimatikan per project) supaya pergerakan branch lain terlihat. |
| 4 | Wiki bisnis punya **halaman terpisah per environment**, plus fitur **compare** antar environment. |
| 5 | Knowledge memory (EFCore, Redis, …) tidak terikat git dan tetap global. |
| 6 | Memory dan wiki tidak pernah dihapus karena status git; status hanya menyembunyikan, menandai, atau menurunkan peringkat. Perubahan yang muncul lagi (mis. `stash pop`) mengaktifkan kembali. |
| 7 | Semua perintah git dijalankan AIT dengan argumen tetap (tanpa shell), read-only kecuali `git fetch`. Tidak pernah `checkout`, `reset`, `commit`, atau `push`. |

## Konsep

### 1. Asal-usul (provenance)

Dicatat saat thread dimulai (`ThreadOrchestrator.PrepareAsync`) dan diturunkan ke memory saat dipromosikan ke long memory:

| Field | Sumber |
|---|---|
| `GitAuthor` | `git config user.name` + `user.email` di repo (config lokal repo menang atas global) |
| `GitBranch` | `git rev-parse --abbrev-ref HEAD` (`HEAD` = detached) |
| `GitBaseCommit` | `git rev-parse HEAD` saat thread mulai |

### 2. Sidik jari perubahan

Saat thread menulis memory, perubahannya **belum di-commit**, jadi memory tidak bisa langsung diberi commit id. AIT
menyimpan sidik jari perubahan: **blob id git** dari isi setiap file setelah ditulis thread
(`ThreadFileSnapshot.AfterBlob`, dihitung dengan `git hash-object --stdin-paths` agar `.gitattributes` / `autocrlf`
ikut diterapkan, sama seperti saat developer meng-commit).

Blob id tidak berubah karena squash, rebase, atau cherry-pick, sehingga AIT bisa bertanya ke git *"isi ini sekarang
ada di mana"*:

- `git ls-files -s -- <path>` → blob di index (staged).
- `git log -n1 --format=%H --find-object=<blob> <ref> -- <path>` → commit pertama di `<ref>` yang membawa isi itu.
- Bila blob tidak ketemu (developer mengedit lagi sebelum commit): fallback `git patch-id --stable` dari diff thread
  dibanding diff commit, lalu pencocokan per hunk → status *sebagian*.

### 3. Status perubahan thread

Dihitung per file, lalu diringkas per thread (status terburuk yang masih relevan menang):

| Status | Arti | Cara AIT tahu |
|---|---|---|
| `Uncommitted` | Belum di-commit | Isi file = `AfterHash`, blob belum ada di index maupun commit |
| `Staged` | Sudah di-stage | Blob ada di index |
| `Committed` | Di-commit di branch lokal | `--find-object` menemukan commit di `HEAD` |
| `Pushed` | Ada di remote branch | Remote ref (`origin/<branch>`) memuat commit itu (F2) |
| `InEnvironment` | Masuk environment | Ref environment memuat commit / blob itu (F3) |
| `ModifiedOutside` | Diubah lagi di luar AIT, belum di-commit | Isi sekarang ≠ `AfterHash` dan ≠ `OriginalHash` |
| `Discarded` | Dibuang di luar AIT | Isi sekarang = `OriginalHash`, blob tidak ada di commit mana pun |
| `Stashed` | Di-stash | Blob ditemukan di `refs/stash` (F2) |
| `Lost` | Commit hilang | Commit tidak bisa dicapai dari ref mana pun (reset / force-push) (F2) |
| `RolledBack` | Di-rollback lewat AIT | `ThreadRun.RolledBackAt` |

### 4. Kapan AIT tahu

| Pemicu | Isi |
|---|---|
| Thread selesai | Hitung `AfterBlob`, status awal (`Uncommitted`) |
| Watcher `.git` | `HEAD`, `index`, `refs/**`, `packed-refs`, `logs/HEAD` (reflog) di-debounce → hitung ulang status thread yang belum final. Reflog memberi jenis kejadian (checkout, commit, reset, rebase, merge). Worktree / submodule: folder dari `git rev-parse --git-dir` + `--git-common-dir`. |
| Sebelum planner | Sinkron singkat project thread baru, supaya brief sesuai kondisi git |
| `git fetch` berkala | Ref environment + remote branch (F2/F3), interval per project, bisa dimatikan |
| Manual | Tombol *Sinkronkan git* di Project > Detail |

### 5. Environment (F3)

Disetel per project (store bersama, keyed `ProjectKey`):

| Nama | Ref | Urutan | Production |
|---|---|---|---|
| dev | `origin/develop` | 1 | |
| staging | `origin/staging` | 2 | |
| staging2 | `origin/release/2` | 3 | |
| prod | `origin/main` | 4 | ✓ |

Tabel bersama `CommitPresence(ProjectKey, Commit, Environment, FirstSeenAt, SeenBy)`: SHA commit bersifat global,
sehingga AIT developer B yang fetch bisa mempromosikan memory tulisan developer A. Untuk squash merge, keberadaan dicek
lewat blob (`--find-object` di ref environment), lalu commit squash-nya dicatat juga.

### 6. Memory di planner

Project memory sudah bergaya MEMORY.md: satu entri `Change` per thread (file dari `ThreadFileSnapshots`) dan satu
`Summary` per modul. Status git melekat ke entri `Change` lewat `SourceThreadId`; `Summary` modul disusun ulang
`ModuleSummaryWriter` hanya dari entri `Change` yang tidak `Discarded` / `RolledBack` / `Lost` (F2), dan per
environment dari entri yang sudah masuk environment itu (F3).

| Kondisi memory | Perlakuan |
|---|---|
| Perubahannya ada di `HEAD` (ancestor) | Normal |
| Branch lain, belum di-merge | Tampil dengan tanda `[branch <x>, belum di branch ini]`, peringkat turun |
| `ModifiedOutside` | Tanda *mungkin usang* |
| `Discarded` / `RolledBack` / `Lost` | Disembunyikan dari planner, tetap di menu Memory (filter *Dibuang*) |
| Thread investigasi tanpa perubahan | *Berlaku per commit `<base>`* |
| Knowledge memory | Tidak terpengaruh |

`SessionChanges` (brief `# Changes in this session`) melewati thread `Discarded` seperti thread yang di-rollback.

### 7. Wiki bisnis per environment (F3)

- **Halaman kerja** (yang sekarang) tetap ditulis writer dari thread, mengikuti perubahan terbaru semua branch.
- **Halaman environment** terpisah per environment: `BusinessEnvPage(PageId, Environment, Content, Version,
  SourceThreadIds, SourceCommits, UpdatedAt)`. Diperbarui saat thread sumber suatu revisi masuk ke environment itu:
  - semua thread sumber revisi sudah masuk → salin isi revisi;
  - hanya sebagian → writer menyusun ulang halaman environment dari halaman environment sebelumnya + digest thread yang
    sudah masuk saja.
- **Compare**: pilih dua environment (atau environment vs halaman kerja) → diff per halaman (Markdown, per paragraf),
  daftar halaman yang hanya ada di satu sisi, dan daftar thread/commit pembedanya
  (*"ada di staging, belum di prod"*).

### 8. Perubahan di luar AIT sepenuhnya (F4, opsional)

Worker *ingest commit* merangkum commit yang tidak berasal dari thread AIT (diff + pesan commit) menjadi memory dan
digest wiki, dengan author dari author commit. Memakai token, jadi opt-in per project.

## Fase

| Fase | Isi |
|---|---|
| **F1** ✓ | Asal-usul di `ThreadRun` + memory (`GitAuthor`, `GitBranch`, `GitBaseCommit`); `AfterBlob` (diambil `FileJournal` saat menulis, bukan saat thread selesai: developer bisa commit / pindah branch lebih dulu); `GitChangeTracker` status lokal (`Uncommitted`, `Staged`, `Committed`, `ModifiedOutside`, `Discarded`, `OnOtherBranch`); watcher `.git` (`GitWatchWorker`); kartu Git di thread & branch sumber di memory; API `git/refresh`; `SessionChanges` melewati `Discarded`/`OnOtherBranch`. |
| **F2** ✓ | Tabel bersama `ThreadChanges` (status + commit + blob per file per thread, ditulis AIT pemilik thread, dibaca AIT developer lain); `Pushed`, `Stashed`, `Lost`; `git fetch --all --prune` berkala per project (bawaan 15 menit, 0 = mati) + kartu Git di Proyek > Detail; `PresenceAsync` (commit di `HEAD` atau semua blob di `HEAD` = ada di branch ini) → planner melewati memory yang dibuang/hilang dan menandai + menurunkan memory dari branch lain / belum di-commit; ringkasan modul melewati entri yang dibuang/hilang; badge status di menu Memori. Beda dari rancangan: fallback `patch-id` / hunk diganti pencocokan baris diff tugas (≥ 80% baris tambahan ada dan ≥ 80% baris hapus hilang di `HEAD`, `GitEdited`); commit revert di riwayat `HEAD` = `Discarded`, bukan `OnOtherBranch`; isi yang sama ditulis ulang memakai commit terbaru yang menambah blob. Batas: entri yang sudah masuk ringkasan modul sebelum dibuang tidak dikeluarkan lagi. |
| **F3** | Environment per project, `CommitPresence`, halaman wiki per environment, compare, filter memory per environment. |
| **F4** | Ingest commit di luar AIT. |

## Skenario

| Kejadian | Hasil |
|---|---|
| Developer commit perubahan AIT | `Committed` + SHA commit; memory mendapat commit |
| Developer push | `Pushed` (F2) |
| PR di-squash merge ke `develop` | Blob ditemukan di `origin/develop` → masuk env dev, commit squash dicatat (F3) |
| Developer `git checkout -- file` / discard di IDE | `Discarded`, memory disembunyikan dari planner |
| `git stash` lalu `stash pop` | `Stashed` → kembali `Uncommitted` (F2) |
| Developer mengedit file AIT lalu commit | Blob tidak cocok → `patch-id` / hunk → `Committed` (sebagian) (F2) |
| Pindah branch | Status dihitung ulang; perubahan di branch lain tetap tercatat di branch itu |
| `reset --hard` setelah commit | Commit tak tercapai → `Lost` (F2) |
| Commit developer lain tanpa AIT | F4 |
