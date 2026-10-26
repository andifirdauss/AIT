# 6. Memory

Memory makes the planner understand the business and technical context of a project better over time, without you
repeating explanations.

## Change entries (project memory)

Project memory is written like a `MEMORY.md`: what is stored is **what changed**, not loose technical facts. It holds two
kinds:

| Kind | Content | Count |
|---|---|---|
| `Change` | One entry per thread: title, the feature that changed, **the files that changed**, validation added/changed (`before → after`), flow added/changed, and notes (pitfalls, decisions, unfinished work). | One per thread |
| `Summary` | **The current state of the module**: features, main files and their roles, the validation and flow that apply, notes. Updated from that module's `Change` entries. | One per module (per project key) |

An example `Change` entry (in English; an entry written in Indonesian uses Indonesian headings, see
[Memory language](#memory-language)):

```markdown
### Add a Description column to User
**Feature:** Users can fill in a description (optional) when adding/editing a user.

**Changed files:**
- `Views/User/Index.cshtml` (modified) — input `#txtDescription`
- `wwwroot/js/pages/user.js` (modified) — `saveUser()` sends `description`
- `Migrations/20261026_AddUserDescription.cs` (new) — column `Users.Description nvarchar(500)`

**Validation:**
- `Description` at most 500 characters → "Description is at most 500 characters"

**Flow:**
1. `user.js` `saveUser()` → `POST /User/Save`
2. `UserService.Save` validates, then `UserRepository.Upsert`

**Notes:**
- The migration has not been run on the dev server yet.
```

- **The file list is taken from the files the thread really changed** (the rollback snapshot), not from the model: files
  the model names but did not change are dropped, files that changed but that the model did not describe are still
  listed. The model only writes the title, feature, a note per file, validation, flow and notes. The format is written
  by AIT itself, so every entry looks the same.
- The content is written in the developer's language; identifiers, paths, values and messages in backticks.
- `Summary` only exists in long memory and is rewritten automatically — see [Module summary](#module-summary).
- Memory with an old kind (`Pitfall`, `Rule`, `Flow`, …) is migrated into its module's `Summary` — see
  [Migrating old memory](#migrating-old-memory-to-module-summaries). Knowledge memory keeps the old kinds.

## The content of one memory

| Part | Function |
|---|---|
| **Label** | `[Area ·] Module · Layer · Kind`, e.g. `User · Logic · Rule`; a memory about several modules: `Order+Invoice · Logic · Flow`. The first search layer: the planner filters by module (and when needed layer/kind). |
| **Keywords** | At most 16 words, e.g. `Description, Deskripsi, UserValidator`. The second layer: within the results of the first layer, which ones hold the terms looked for. |
| **Description** | A self-contained explanation as labeled lines per kind (see [Description format](#description-format)), at most ±700 characters. For project memory it holds file paths and identifiers in backticks. Only read after passing both layers. |

Every part of the label and the keywords is written in **PascalCase**, normalized by AIT (not left to the model):
`user dto` → `UserDTO`, `api client` → `APIClient`, `users` → `Users`. Abbreviations are written in full capitals: a
built-in list (API, DTO, UI, SQL, DB, ID, URL, JSON, …) plus 2–3 letter words that are written in capitals anyway (`TO`,
`PO`, `SKU`); the list can be extended with `AIThread:MemoryAcronyms`. Identifiers from the code stay as they are
(`UserDto`, `UserBL`), only their first letter is capitalized.

**Module names** are often abbreviations, so parts written in capitals are always kept, whatever their length: `MKPP`
stays `MKPP` (a full abbreviation), `KlaimDF` / `Klaim DF` → `KlaimDF` (PascalCase + abbreviation). A name written all in
lowercase only gets a capital first letter (`klaimdf` → `Klaimdf`), because AIT cannot guess the abbreviation. A name
that is not quite right is fixed with [renaming a module](#module-list--merging-modules).

### Description format

A description is written line by line. Each line starts with a fixed label for its kind, in the developer's language.
Empty parts may be skipped.

| Kind | Label order |
|---|---|
| Pitfall | Problem · Cause · Solution · Location |
| Rule | Rule · Enforced in · Exceptions |
| Flow | Trigger · Steps (numbered) · Result · Location |
| Convention | Convention · Example · Reason |
| Architecture | Component · Relations · Reason |
| Pattern | Problem · Solution · When to use |

(In Indonesian: Masalah · Penyebab · Solusi · Lokasi, Aturan · Ditegakkan di · Pengecualian, and so on.)

- Identifiers, paths, config keys, values and messages are written in backticks.
- Name things, not "this" or "the bug earlier".
- Write the fact, not the story of the thread.
- Knowledge memory has no project paths or identifiers.

Example (Rule):

```text
Rule: `TokenBadge` is always 24 columns wide so `[` and `]` line up.
Enforced in: `src/AIThread.Service/Orchestration/ClientProgress.cs`, `WithBadge`.
Exceptions: a badge that does not fit in the rest of the line moves whole to a new line, right-aligned.
```

Old memory keeps its free format. This format applies to memory extracted afterwards.

### Module

A business feature or entity (`User`, `Invoice`, `TransferOrder`), the same for every layer of that feature:
`UserController`, `UserBL`, `UserRepository` and `Views/User/Index.cshtml` are all the `User` module. For knowledge memory,
the module is a technology or domain (`EFCore`, `Redis`, `Auth`). The module comes from the project's
[module list](#module-list--merging-modules), the same as the business wiki modules, and for project memory it is derived
from the [code map](#code-map) of the files discussed.

A memory has one **main module** and may have up to 3 **other modules** when the same fact really covers several modules
(e.g. the `Order` posting flow that creates an `Invoice`). The module filter in the Memory menu, the planner's search, the
memory map and the memory count per module also count those other modules. The main module is stored in the `Module`
column (also read by old AIT versions), the other modules in the `ExtraModules` column.

### Layer

The role of a file in its own codebase, not its position in the FE or BE. A memory may have several layers (a flow
memory).

| Layer | Role | Examples |
|---|---|---|
| `UI` | Display & interaction | `.cshtml`, React/Vue components, CSS, `pages/*.js` |
| `Entry` | Entry point of a request/command | controller, route, handler, CLI command, consumer |
| `Logic` | Business rules & processes, outgoing clients | service, BL, validator, store, API client |
| `Data` | Storage & data shape | repository, DbContext, migration, query, entity, DTO |
| `Infra` | Wiring, configuration, build | `Program.cs`, DI, `appsettings`, Dockerfile, CI |
| `Test` | Testing | `*Tests.cs`, `*.spec.ts` |

### Kind

New project memory is always `Change` (a change entry) or `Summary` (the module state) — see
[Change entries](#change-entries-project-memory). The kinds below are the fact format of the previous version, still used
by knowledge memory; for project memory they are folded into their module's `Summary`.

The kind of fact stored. Decided with questions **in order** — the first "yes" answer is used, so ambiguous cases always
fall into the same kind:

1. A mistake or pitfall to avoid (with its solution)? → `Pitfall`
2. A business condition the system enforces (allowed, required, computed, validated)? → `Rule`
3. A sequence of steps across components or time? → `Flow`
4. How developers write code in this project? → `Convention`
5. The arrangement/relation of components or a design reason? → `Architecture`
6. Anything else → `Pattern`

Knowledge memory may only be `Pitfall`, `Pattern` or `Architecture` (never `Change`/`Summary`). One memory, one kind; a
fact with two kinds is split into two memories.

### Area

An application in a repository that holds several applications (e.g. a React `apps/web` and a .NET `apps/api`): the
nearest manifest folder, without the prefix shared by every area. Only filled in when the manifests are of different
stacks or there are several applications under `apps/`, `packages/`, `services/`, `projects/`; a .NET solution with
several projects stays one application without an area.

## Types

| Type | Content | Scope |
|---|---|---|
| **Project** | Business rules, validation, data flow, architecture, conventions, code locations. | Its own project — all its clones, on any developer's machine (see [project key](#sharing-memory-between-developers-project-key)). |
| **Knowledge** | General technical patterns useful in other projects, e.g. "cache master data in Redis, refresh every 5 minutes". | Every project. |
| **Business** | A business wiki per module: requirements, business rules, status & flow, validation, screenshots. Written for non-technical readers. | Its own project (per project key). See [Business memory](#business-memory-business-wiki). |

## Two levels

| Level | Storage | Lifetime |
|---|---|---|
| **Short** | Default: Garnet inside AIT (Redis protocol, no TTL). Can be changed — see [Memory storage](#memory-storage). | Until it is `ShortMemoryHours` old (default 24 hours). |
| **Long** | Default: the `LongMemories` table in the main database. Can be changed — see [Memory storage](#memory-storage). | Permanent until deleted. |

```
thread done ─► Change entry (wrapper model) ─► short memory (Garnet)
                                                 │  consolidation worker, every 10 minutes:
                                                 │  older than 24 hours ─► long memory (DB, same id)
                                                 ▼
                                          long memory (database)
                                                 │  then, in the same pass:
                                                 ▼
                                          module Summary updated (Tools node → Wrapper → Planner)
```

- Memory is moved by a worker, not by expiry (TTL) — so nothing is lost even when the application is down when a memory
  turns 24 hours old. The id stays the same, so the move is safe to repeat.
- Short memory survives an AIT restart or crash: Garnet records every write on disk
  (`%LOCALAPPDATA%\AIThread\garnet\<port>`) before it is confirmed.
- **Short memory not available** (e.g. Garnet failed to start, or the external short memory server is down): new memory
  is stored directly as long memory (logged as a warning). An external database store is checked again every 15 seconds
  and used again automatically as soon as it is back.
- **Long memory not available** (e.g. the external PostgreSQL server is down): threads still run, the planner just does
  not see long memory. The activity log records `Long memory (…) cannot be reached` at most once every 10 minutes.
  Consolidation postpones the move: short memories that are due stay in short memory until the database is back, and
  the Memory menu statistics show the long memory store as *not connected*.
- Short memory of a project that has been deleted becomes knowledge memory when it is moved.

## Sharing memory between developers (project key)

A project's root folder differs per machine (`D:\a\eep` on Andi's laptop, `C:\src\eep` on Budi's laptop), so memory is not
bound to the folder but to a **project key** that is the same for every clone of a repository:

1. the `.aithread/project.json` file in the repository root, e.g. `{ "key": "eep" }` — committed by the team, always wins
   (the same file can hold `"run"`, the command that runs the application for testing; see
   [Running the application](07-cara-kerja.md#running-the-application));
2. a **manual key** in the Project menu → project detail (for a repository without a remote);
3. the **git remote** `origin` (or the first remote), normalized: `https://user:token@gitlab.com/Org/EEP.git`,
   `git@gitlab.com:org/eep.git` and `ssh://git@gitlab.com:22/org/eep` all become `gitlab.com/org/eep` (credentials never
   come along). Worktrees and submodules (`.git` as a file) follow their main repository;
4. without any of those: a **local** key (`local:…`, from the folder path) — the memory only exists on this machine. The
   Project menu marks it *local key — not shared yet*.

AIT reads that file directly (it does not run `git`) and computes the key again every time the project is seen, and once
on start. To gather the long memory of every developer in one place, point the **Long memory** (and **Business memory**)
of every developer to the same database in Setting → Memory storage. Short memory may stay local: after 24 hours it moves
to the shared long memory.

- New memory stores the key and the name of its project, so it keeps its name even when that project is not registered
  on your machine. In the Memory menu, memory from other developers is shown under the name of the clone of the same
  project on your machine.
- Memory from an old version (without a key) gets its project's key automatically when AIT starts. In a shared store,
  each developer fills in their own rows, so over time everything gets bound to a key.
- When a project's key changes from **local** to a shared key (a remote, a file or a manual key is added), its memory
  moves along. Memory under a shared key is never moved, because another developer's clone may still use it.
- **Deleting a project**: memory that only exists on this machine is deleted too; memory in a shared store is kept for
  other developers, unless you agree to *also delete it for ALL developers*. When another clone with the same key is
  still registered on this machine, the memory moves to that clone.
- The module list, the code map and the project's [unit test catalog](07-cara-kerja.md#unit-tests) follow the same rules:
  they move along when the key changes from local to a shared key, and are deleted together with its memory.

### Git origin of memory

Project memory made from a thread keeps **who** (the developer's git identity, `Name <email>`), the **branch**, and the
**commit** the thread started from. The values are taken from the thread when the memory moves to long memory, so other
developers read them in a shared store, and they go along in `aithread-memory` export/import files. In the Memory menu
the branch shows under the *thread* link (hover for the author and commit). Where the thread's change moves afterwards
(committed, discarded, on another branch) shows on the thread page — see
[Git state of a thread's change](07-cara-kerja.md#git-state-of-a-threads-change).

Knowledge memory and memory from older versions have no git origin.

### Memory and git

The git state of every thread's change is shared with the team through the `ThreadChanges` table in the long memory
database (see [Git state of a thread's change](07-cara-kerja.md#git-state-of-a-threads-change)). From there:

- The **Memory menu** shows a badge with the state of the memory's change in its author's clone (*committed*, *pushed*,
  *discarded*, *on another branch*, …) under the *thread* link.
- The **planner** checks the clone in use:

  | The memory's change | Planner |
  |---|---|
  | In the branch checked out (`HEAD` holds its commit, or all its blobs, e.g. after a squash merge) | Read as usual |
  | On another branch not merged here | Read with the mark `(not in this branch: committed on <branch> by <author>)`, put after the other memory |
  | Not committed yet or stashed on its author's machine | Read with the mark `(not in this branch: not committed yet …)`, put after the other memory |
  | Discarded or its commit lost | Not read |
  | Unknown (knowledge, older memory, not a git repository) | Read as usual |

- The **module summary** leaves out change entries whose change was discarded or whose commit was lost. Such an entry
  keeps waiting, so it is summarized after all when the change comes back (e.g. `stash pop`, a branch restored). An
  entry already summarized before it was discarded is not taken out again.

## Memory storage

The **Setting → Memory storage** menu (`/Setting#memory`) decides where each level is stored. Changes apply right away
without a restart.

| Provider | Short | Long | Business | Fields used |
|---|---|---|---|---|
| Built-in | Garnet inside AIT (`ConnectionStrings:Redis`) | The main database (`ConnectionStrings:Default`) | The main database | — |
| Redis | ✓ | — | — | host, port (6379), database = index (optional), username (Redis 6+ ACL), password |
| SQL Server | ✓ | ✓ | ✓ | host, port (1433), database, username/password — an empty username = Windows authentication |
| PostgreSQL | ✓ | ✓ | ✓ | host, port (5432), database, username, password |
| MySQL | ✓ | ✓ | ✓ | host, port (3306), database, username, password |
| Oracle | ✓ | ✓ | ✓ | host, port (1521), database = service name (e.g. `FREEPDB1`), username, password |
| SQLite | ✓ | ✓ | ✓ | database = file path (empty = `%LOCALAPPDATA%\AIThread\memory.db`) |

- **Extra options** are added to the connection string and override the fields above, e.g. `SSL Mode=Require`
  (PostgreSQL), `Encrypt=True` (SQL Server), or `ssl=true` (Redis, comma-separated). Unknown keywords are rejected when
  saving.
- **Test connection** uses the form content without saving. An empty password = the stored password is used.
- When **saved**, AIT connects to the new store and creates the database (when it does not exist yet and the user has the
  right) and the `ShortMemories`/`LongMemories` tables, or `BusinessPages`/`BusinessPageRevisions`/`BusinessAssets` for
  business memory. The module list, the code map and the module merge history (`ProjectModules`, `CodeMapEntries`,
  `ModuleMergeLogs`) live in the long memory database too, so a shared store also shares its module list. So does the
  [unit test catalog](07-cara-kerja.md#unit-tests) (`UnitTestCases`, `UnitTestRuns`, `UnitTestSettings`): developers who
  use a shared store run the same tests. A store that cannot be reached is rejected, so the old setting stays in effect.
  External databases do not use EF migrations; the tables are created on first use, and tables from an old version get
  the new columns automatically (`ProjectKey`, `ProjectName`, the label columns `Module`, `Layers`, `Kind`, `Area`,
  `Keywords`, `LabelVersion`, `LegacyLabel`, `ReviewReason`, `RelabelAttempts`) — the user needs the `ALTER TABLE` right
  that one time.
- **Copy the existing memory** (on by default) copies memory from the old store to the new store by id, so it is safe to
  repeat and does not duplicate. The unit test catalog is copied along with long memory. The old store is not deleted or
  changed. Without a copy, the old memory is not visible until the provider is switched back.
- Passwords are stored encrypted (ASP.NET Core Data Protection) and never sent back to the browser.
- Short, long and business memory may use the same database; each has its own tables.
- Long memory in an external database has no foreign key to `Projects`: memory is bound to the project key and stores
  its own project name. See [Sharing memory between developers](#sharing-memory-between-developers-project-key) for what
  happens when a project is deleted.
- Copying business memory to a store that already holds another developer's wiki skips pages whose title already exists
  there (reported as a warning), so there are no duplicate pages.

## When memory is made

After a thread finishes (at least one task succeeded, or the planner answered directly):

1. AIT takes **the files the thread changed** from its rollback snapshot (`created`/`modified`/`deleted`). Without a
   snapshot (an old thread), the files of the tasks that succeeded are used.
2. Those files, the planned files and the files the prompt names are mapped to a module and layer
   ([code map](#code-map); waiting for the code map model when needed).
3. The **Wrapper** model reads the request, the plan, the contract, the task results, the list of changed files, the
   final answer, the code map, the module list, and that module's `Summary` and 2 latest entries (so it does not repeat
   them), then writes **one** `Change` entry.
4. AIT assembles its text (see [Change entries](#change-entries-project-memory)), masks secrets, and stores it as short
   memory. The module comes from the model (from the module list) or, when empty, the most common module of the changed
   files; layers and area come from the code map.

A thread that **does not change files** (questions, investigation, review) is only recorded when the model judges there
is a finding the next threads should remember — the cause of a bug, a decision, a rule or flow that was unclear.
Otherwise there is no entry. A thread without a project (the project was deleted while the thread ran) is not recorded.
This runs in the background and does not delay the reply to the CLI.

## Module summary

Every pass of the consolidation worker (every `MemoryConsolidationIntervalMinutes`, default 10 minutes), after short
memory is moved to long memory, AIT updates the `Summary` of every module that has new `Change` entries in long memory:

- A cheap model (the Tools node, then the Wrapper, then the Planner) gets the current summary + the new entries (oldest
  first, at most 15 per call) and rewrites the module state: newer entries win, what was replaced is removed, duplicates
  are merged. An entry that names several modules goes into the summary of each of those modules.
- A `Summary` has a fixed id per project key + module, so several machines sharing a store do not create duplicate
  summaries. The *moved to DB* column of a summary means **the time of the last entry already folded in**; later entries
  are processed in the next pass.
- `Change` entries are kept (history); the summary is only an addition on top of them.
- Merged modules bring their own summaries; both are folded into one in the next pass.
- At most 30 modules per pass; the rest waits for the next pass. Without an active model node, the summary waits.
- **The language is decided by AIT**, not guessed by the model — see [Memory language](#memory-language).

### Memory language

Memory is written in Indonesian or English, and AIT decides which:

- A `Change` entry: the language of the developer's request (and the plan summary); when unclear (e.g. only pasted
  logs), the [display language](02-konfigurasi.md#display-language).
- A module summary: the language of the entries and facts being folded, when unclear the language of the project's
  latest `Change` entries, then the language of the current summary, then the display language. The model is asked to
  translate other content into that language.
- The section headings follow the language of the content: *Fitur / File berubah / Validasi / Flow / Catatan* with the
  file actions `baru`/`ubah`/`hapus`, or *Feature / Changed files / Validation / Flow / Notes* with
  `new`/`modified`/`deleted` (summary: *kondisi saat ini* / *current state*, *File utama* / *Main files*). The display
  language does not change memory already stored, because memory is shared with team members who may use the other
  language.
- An answer that turns out to be in another language (e.g. Vietnamese) is not stored; a summary already written in
  another language is rewritten in the next pass.

## Migrating old memory to module summaries

Project memory in the old format (`Pitfall`, `Rule`, `Flow`, `Convention`, `Architecture`, `Pattern`) that is labeled and
has a project key is migrated automatically by the same worker:

1. A **backup** once: every long project memory to
   `%LOCALAPPDATA%\AIThread\backup\memory-before-changelog-<time>.json` (the Memory menu import format). When the backup
   fails, the migration is postponed (summaries from `Change` entries still run).
2. Per module, the old facts (at most 40 per call) are folded into that module's `Summary` together with the waiting
   `Change` entries: `Rule` → validation, `Flow` → flow, `Pitfall`/`Convention`/`Architecture` → notes, etc.
3. Facts that were folded in are **deleted** — their content is now in the summary (and in the backup file). The summary
   is saved and the facts are deleted in one transaction, so an AIT that stops halfway does not fold the same facts
   twice.

**A restart in the middle of the migration is safe**: every finished module is stored right away, and after AIT starts
again the migration continues with the remaining modules (at most ±20 seconds after start). The module being processed
when AIT stopped is repeated from the start. The overall progress (the number of old memories when the migration
started) is stored in `%LOCALAPPDATA%\AIThread\backup\memory-changelog-progress.json`; the rest is counted from the
store, so the work of other machines sharing the store is counted too. That file is deleted once every old memory is
migrated.

Old memory that is not labeled yet is labeled first by the [label migration](#label-migration-old-memory), then migrated
too. Memory waiting for review is not touched. Knowledge memory is not migrated.

The **Migrating old memory to module summaries** banner in the Memory menu shows as long as old memories are waiting:
the overall progress `120/300 migrated (40%)` with a progress bar (kept after a restart), how many are waiting and in how
many modules, the number of module summaries, how many are still in short memory, the status (the running pass `module
3/12 (User)`, waiting for a model node, or the time of the last pass), the last message (e.g. the backup failed), and the
backup path. The banner updates through SSE every time a module finishes and disappears by itself once everything is
migrated.

> **Shared store**: AIT versions before change entries do not know the `Change`/`Summary` kinds. When long memory is
> shared by a team, update the AIT of every developer.

## How the planner uses memory

```
Layer 1 — Module (· Layer · Kind)  → the list of that module's memories
Layer 2 — Keywords                 → within that list, the ones holding the terms looked for
read_memory                        → only then is the description read
```

1. Before the planner runs, the files and classes the prompt names are mapped to modules (the stored code map + rules;
   uncertain ones are mapped by a model in the background without holding the planner).
2. The planner's brief holds:
   - the **code map** of the files the prompt names (`path: Module · Layer`);
   - **module memory** (`# Module memory`), written **in full**, not only the labels: for at most 3 modules the request
     is about (from the code map, or the top memory modules of the search), the module's `Summary` and then its 3 latest
     `Change` entries (short or long), at most ±14,000 characters. Without a module: the project's 3 latest `Change`
     entries (`# Recent changes in this project`). The planner is asked to follow the validation and flow in it unless
     the request changes them, and still to read the latest code before planning a change;
   - the **memory map**: each module with its number of memories per layer and kind, e.g.
     `User (12): Logic 5 · Entry 3 | Rule 4 · Flow 3`;
   - the **memory index** (id + label + keywords, at most 25 lines, without what is already written in full above;
     `Change` entries come with their date and title, e.g.
     `User · UI/Logic · Change "Add a Description column to User" — Description, Deskripsi`): the memory of the modules
     from the code map or named in the prompt (the module name or alias), sorted by keywords; plus the latest short
     memories. Business wiki pages have their own quota (at most 8), so do old unlabeled memories (marked `unlabeled`).
3. `search_memory` takes `module`, `layer`, `kind` (`Change`, `Summary`, or an old kind), and `keywords`. Without a
   module, the modules named in the keywords are used; when layer 2 is empty, the whole of layer 1 is returned so the
   planner is not blind. For history older than the 3 entries in the brief, the planner searches `kind: Change` + the
   feature's keywords; the results include the date and title of each entry too.
4. `read_memory` reads the description (or the content of a wiki page, with images replaced by their captions, at most
   15,000 characters); the planner is asked to read a module's page before changing its behaviour and to follow its
   rules.
5. Every time a long memory is read, its *Used* column goes up — a marker of valuable memories.

Keywords are matched **per token**, not as substrings: `UserValidator` matches `user`, `validator` and `uservalidator`;
`form` does not match `information`. Plural `-s`/`-es` forms count as the same. A keyword used by many project memories
(e.g. `Service`) weighs little, a rare keyword (an identifier) weighs a lot. That is why keywords should hold the English
terms **and** the terms you use every day, plus the relevant class names.

## Code map

The module and layer of every file, decided once and then stored (per project key + path relative to the root, so it
applies on every developer's machine) so the same file always gets the same answer.

```
a file named in a prompt / touched by a task
  ① stored map? ──yes──► use it
  ② rules: the layer from the suffix/folder/extension (Controller→Entry, BL/Service→Logic, Repository→Data, .cshtml→UI, …),
     the module from the name without role words (UserController→User) matching the module list ──sure──► store (rule)
  ③ unsure: a model (Tools node → Wrapper → Planner), one call per group of the same name, must choose an existing
     module; a new module only when none matches ──► store (AI)
```

- Before the planner only ① and ② are waited for; ③ runs in parallel, and memory extraction waits for its result.
- Plumbing files without a feature (startup, configuration, layout, helpers) go to the `Core` module.
- Without an active model node, the module is taken from the file name.
- **Manual corrections** from the Project menu → detail → *Code map* (*Set file* / *Change*), or through a prompt: the
  planner has the `set_module` tool ("treat `UserBL` as the Account module"). A manual correction is never overwritten by
  rules or a model.
- **Scan** maps every source file that is not mapped yet (at most 3,000 files per scan; `bin`, `obj`, `node_modules`,
  `wwwroot/lib`, etc. are skipped). Every file surely gets an entry (from the model or the file name), so an interrupted
  scan (e.g. AIT restarted) continues from the files not mapped yet. Run automatically during the label migration.
- A model call holds 40 file groups (per name stem). The first batch runs alone to fill the module list, the rest 3 calls
  at once (still limited by the node's *Max concurrency*); their results are stored in turn so a new module from one
  batch is used by the next batch instead of being created twice.

## Module list & merging modules

One module list per project key (and one for knowledge memory), shared by memory labels and business wiki modules. Names
are matched regardless of upper/lower case and separators, through **aliases** (other names/translations, e.g. `Pengguna`
for `User`), then English plurals. A merged module is still stored as a redirect, so its old name still leads to the
target module.

**Duplicate modules** (split by accident) are looked for during the migration, every time a new module is created, and
with the *Check duplicate modules* button:

| Condition | Action |
|---|---|
| The same name written differently (upper/lower case, separators, plural) | Merged automatically into the module used more |
| The model judges them the same with ≥ 85% confidence (a translation, a longer name, a typo) | Merged automatically, recorded, can be undone |
| The model is unsure | A suggestion in **Memory → Needs review** (*Merge* / *Not a duplicate*) |

Merging modules moves the memories (short & long, with their labels rewritten), the code map and the business wiki
pages; the name and aliases of the source module become aliases of the target module. Wiki pages whose title already
exists in the target module are not moved (reported in the log). **Undo** in the module merge history (Project → detail)
returns exactly the rows that were moved. A rejected suggestion is not suggested again.

**Renaming a module** (the memory form: the *Rename* button next to the module dropdown; Project → detail: the *Name*
field in *Manage*) can also change only the upper/lower case, e.g. `Klaimdf` → `KlaimDF`. The memories (main or other
module, with their labels rewritten), the code map, the business wiki pages, the redirects of merged modules and the
merge history are renamed too. An old name that differs (not only in case) is kept as an alias. A name already used by
another module (including its aliases or plural) is refused — use *Merge* for that.

## Label migration (old memory)

Memory from versions before structured labels (and from other developers' old AIT versions in a shared store) is
migrated automatically by a worker when AIT starts and then every `MemoryRelabelIntervalMinutes` (default 10 minutes):

1. A **backup** of every short & long memory to `%LOCALAPPDATA%\AIThread\backup\memory-before-labels-v1-<time>.json`
   (plus the modules & labels of wiki pages), once per label version. When the backup fails, the migration does not run.
2. **Business wiki**: page modules are aligned with the module list (filling the first list) and page labels are
   normalized to PascalCase. The page content is not changed, no revision is made.
3. **Per registered project** that has waiting memory (the most first):
   - **Code map**: only the files named in the descriptions of those memories (paths and class names, at most 10 per
     memory, at most 3 files per name), looked up with a single walk of the project folder. Only those are read during
     the relabel;
   - **Relabel** that project's memories, 20 memories per model call: the paths/identifiers in the description are
     matched to the code map first, the model decides the module (from the list), layers, kind and keywords. The old
     label is kept as the *old label*. Each row is claimed first (a conditional update), so several machines sharing a
     store do not process the same row.
4. **Relabel the rest**: knowledge memory, project memory of projects registered on other machines, and memory without a
   project key.
5. Memory whose module cannot be decided, or that failed 3 times, goes to **Memory → Needs review**.
6. **Full code map**: a full scan of every registered project (those with memory first), after every label is done. Once
   per AIT process; an interrupted scan continues.
7. **Duplicate module check** and the list of **possible duplicate memories** (same module & kind, very similar keywords;
   suggestions only).

The *Memory label migration* banner shows the phase and its progress, e.g.
`Code map: KN2018_KCO (1/5) · 12/40 files named in memory`, `Relabel: KN2018_KCO (1/5)`, or
`Full code map: KN2018_KCO (1/12) · 363/1,700 files`.

During the migration, old memory stays visible to the planner as `unlabeled` (matched by its old label). The old `Label`
column is still filled with `Module · Layer · Kind | keywords` so old AIT versions sharing the store can still read it.
The migration needs an active Tools node, Wrapper or Planner; without one the worker waits.

## Business memory (business wiki)

Project and knowledge memory are written for the planner (label + short description). **Business memory** is written for
people: one page per business module (e.g. the **TO — Transfer Order** module in the EEP project) that explains what the
system does and the rules it follows, simple and friendly to read for analysts, testers, business users and new team
members. Its content is Markdown with a fixed structure (the headings are written in the page's language):

```
# TO — Transfer Order
> Moving stock between warehouses.
## Purpose
## Terms              (table Term | Meaning)
## Flow & status      (table Status | Meaning | Actor | Next)
## Business rules     (numbered: BR-TO-01, BR-TO-02, …)
## Validation         (table Field | Rule | Message)
## Screens            (screenshots with captions)
## Code location      (optional, file paths only)
```

### How the wiki is built

```
thread done (each developer) ──► waiting to be summarized (local)
                                   │  every N hours (default 6) or the "Update now" button:
                                   ▼
             1. the model chooses pages: which thread touches which module (purely technical changes are skipped)
             2. per page: the model rewrites the page from its old content + the new threads (+ screenshots)
                                   ▼
             page + revision + images ──► business memory store (can be shared by every developer)
```

- The material per thread: the developer's request, the plan summary, the name contract, the list of file changes, and
  the final reply. At most 15 threads / ±60,000 characters per round, at most 5 rounds per run.
- **Screenshots**: images you attach to a prompt (Claude Code, Codex, …) are stored with the thread, then given to the
  wiki writer. Images used on a page are copied to the business memory store; the rest is discarded after the thread is
  summarized (or after the log retention period, default 14 days). A model that cannot read images still writes from the
  text alone.
- The wiki writer is asked to keep the old content (including hand-written text), to write only facts the threads
  support, to mark uncertain ones with *(perlu konfirmasi)* or *(needs confirmation)*, and to use the page's language
  (for a new page, that of the developer requests, by default the display language).
- A thread is marked *summarized* only when every page it touches was saved; failed ones are tried again in the next
  run.
- **Many developers**: each AIT summarizes its own threads into the same store. Every page has a version number; saving
  over a version that already changed is refused (the wiki writer reads it again and rewrites it once, the editor in the
  Memory menu shows a *already changed …* message). Every save makes a new revision that can be restored.
- A **locked** page (*Updated by AI* turned off) is never changed by the wiki writer — suits pages that are curated by
  hand.

### Editing by hand

The **Memory → Business memory** menu: choose a project, open a page, then *Edit*, or create a *New page*. Paste a
screenshot straight into the editor (Ctrl+V) or use *Insert image*; images (PNG/JPEG/GIF/WebP, at most 5 MB) are stored
in the store and written as `![caption](asset:<id>)`. *Preview* shows the page exactly as it will be after saving. Raw
HTML is not rendered, and links may only go to `http(s)`, `mailto` or inside the application — the wiki can be written by
anyone who shares the store, so its content is not trusted as it is.

### Wiki per environment

When the project has [environments](07-cara-kerja.md#environments), the business wiki keeps a **separate page per
environment** besides the working wiki (the newest version from every branch). An environment's page is the **newest
revision** of the working page whose source threads (of that revision and the ones before) all reached that environment:

- a manual revision (no thread) holds when the one before it holds;
- a thread git knows nothing of (older threads, not a repository) and a change discarded or with its commit lost do not
  hold a page back;
- the content is **copied** from that revision, never rewritten by a model — exactly what the wiki once said. When an
  older revision's thread has not arrived, the page stays at the revision before it even if a newer one has.

Environment pages are updated after the environments are checked and after the wiki writer writes pages. In
**Memory → Business memory**, choose an *Environment* to read that environment's wiki (read-only; a yellow badge when
the working wiki is newer), and **Compare** two sides (environments or the working wiki): the pages that differ with a
line diff, the pages on one side only, and the changes (threads/commits) on one side only — e.g. *in staging, not in
prod yet*.

## Managing from the Memory menu

- **Add memory**: write knowledge you want the planner to always remember, e.g. a change made outside AIT or a team
  decision. The default kind is `Change`; an empty description shows an example of its format. Fill in the module
  (suggestions from the module list), layers, keywords (at most 16) and the description. It comes in as short memory,
  then moves to long memory automatically and goes into its module's summary. A `Summary` cannot be added (AIT writes
  it), but it can be opened and edited; an old kind for project memory will be folded into the module summary.
- **Open**: change the type, project, label, description. Fix a memory that is not quite right — the planner trusts it.
  The description (Markdown) opens in **Preview** mode; switch to **Write** to edit it (or double-click the preview). A
  new memory with an empty description opens in Write mode right away. Saving an unlabeled memory gives it a label (its
  old label stays recorded).
- **Filter** module, layer, kind and *labeled / unlabeled* above the table; the *Memory label migration* banner shows as
  long as memories are waiting for labels (a *Run now* button).
- The **Needs review** tab: memories without a label (*Label it*), module merge suggestions, and possible duplicate
  memories.
- The **Project → detail** menu: the module list (description, aliases, merge into another module, *Check duplicate
  modules*), the code map (search, *Set file*, *Scan*), and the module merge history (*Undo*).
- **→ DB**: move a short memory to long memory now.
- **Delete**: delete a wrong or outdated memory.
- **Consolidate now**: run the short → long move without waiting for the worker.
- **Export / Import**: save memory to a file or bring it in from a file — see [Export & import](#export--import).
- The **Business memory** tab: the business wiki pages per project — see [Business memory](#business-memory-business-wiki).

### Export & import

For backups, moving machines, or sending one project's memory to a teammate without a shared store. To move stores on
the same machine, use *Copy the existing memory* in [Memory storage](#memory-storage).

- **Export** downloads the short and long memory **matching the table filter** (level, type, project, search) as a JSON
  file (`"format": "aithread-memory"`). When short memory is selected but its store is not connected, the export is
  refused (choose the *Long* level for long memory only), so nothing is lost silently. Business memory is not included.
- The **unit test catalog** of the exported projects (every project when not filtered) comes along in the file, so a
  team without a shared store still shares its catalog: title, scenario, status, obsolete reason, last result, and the
  **snapshot** of the test file content (except projects that turned off *Keep test snapshots*). Not included when the
  filter uses a text search or the *Knowledge* type. The run history is not included.
- **Test accounts**, **test scenarios** (steps, roles, form columns; without run results) and the project's testing
  settings (the dev password, the *login as* detection result) come along with the same rules (the `testing` part). The
  dev password comes along because it only holds a bypass password in dev that the whole team sees.
- Projects are written with their **project key** (old memory without a key uses its project's key on this machine), so
  on another machine the memory goes to the clone of the same project even when its root folder differs.
- **Import** checks the file first (*dry run*) and shows a summary: what will be added, overwritten, already exists,
  invalid, and the projects in the file and whether they are registered on this machine. The new data is written after
  the *Import* button.
- Format v2 carries structured labels (module, layers, kind, area, keywords, old label); its modules join the module
  list of their key on the target machine. Items without a label (a v1 file) come in as *unlabeled* and are migrated by
  the worker. Format v3 adds the `unitTests` part, v4 (current) the `testing` part; v1–v3 files are still accepted.
- **Test accounts** from a file are matched by id, then by project key + username (case-insensitive), and testing
  settings by project key. Existing ones are only updated when the copy in the file is newer. The import summary shows
  the number of new, updated and skipped test accounts.
- **Unit tests** from a file are matched by id, then by project key + test name. Existing ones are only updated when the
  copy in the file is newer (changed or run later), regardless of the *skip/overwrite* choice. New tests keep their
  number, unless that number is already used by another test in the same project — then they get the next number
  (reported). The *Removed* status comes along, so a test the team removed is not restored again. The dry run summary
  shows the number of new, updated, renumbered and skipped unit tests (without snapshot content).
- Memory ids are kept: a memory whose id already exists is **skipped** (default) or **overwritten** (type, project,
  label, description from the file). A memory with the same content (project, type, label, description — case and spaces
  ignored) but another id is skipped too. An import is safe to repeat.
- Short memory stays short with its original creation time, then the worker moves it to long memory according to its
  age. When the short memory store is not connected, short memory from the file is stored directly as long memory.
- A project not registered yet: the memory is still stored under its key and shows up as soon as that project is added.
  Project memory without a key and without a registered project becomes knowledge memory. A **local** key (`local:…`)
  from another machine will not match any project — give the project a shared key on the source machine before
  exporting.
- The file holds plain text (descriptions can hold paths, code fragments or internal info; unit test snapshots hold the
  project's test code). Store connection settings and passwords never come along. At most 100,000 memories per import.

Tips for writing memory by hand:

- Choose an existing module (see the suggestions in the Module field); create a new module only when it really is
  another feature.
- Specific keywords: `Tax, Pajak, Rounding, InvoiceService` is more useful than `Business`.
- A description names locations: "The tax calculation is in `InvoiceService.CalculateTax`; rounding up per line."
- One fact, one kind per memory.

## Configuration

| `appsettings.json` key | Default |
|---|---|
| `AIThread:ShortMemoryHours` | 24 |
| `AIThread:MemoryConsolidationIntervalMinutes` | 10 |
| `AIThread:MemoryRelabelIntervalMinutes` | 10 (interval of the label migration & duplicate module check) |
| `AIThread:MemoryAcronyms` | `[]` (extra abbreviations written in capitals in labels, e.g. `["SKU", "GL"]`) |
| `ConnectionStrings:Redis` | `localhost:6379` (the Garnet port; see [Installation](01-instalasi.md#1-short-memory-garnet)) |
| `AIThread:EmbeddedRedis:Enabled` | `true` |

The last two keys are only used when short memory uses the *Built-in* provider.

Business memory is set in the Setting menu (on, interval, model, developer name) — see
[Configuration](02-konfigurasi.md#business-memory).

The "±N hours until long memory" estimate in the Memory table is always counted from 24 hours.
