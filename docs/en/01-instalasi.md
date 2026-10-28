# 1. Installation

## Prerequisites

| Component | Required | Check | Notes |
|---|---|---|---|
| .NET SDK 10 | Yes | `dotnet --list-sdks` | The solution targets `net10.0`. |
| SQL Server | Yes | `sqllocaldb info` | The default is LocalDB `(localdb)\MSSQLLocalDB` (installed with Visual Studio). |
| Redis / Docker | No | — | Short memory uses Garnet running inside AIT (see step 1). |
| Node.js + 9router | Yes | `node --version` | The source of every AI model. Node.js 18+ is required; 9router comes with the npm package `aithread` and is started by AIT (see step 2). |
| Git | Recommended | `git --version` | Executors write straight to the project files; git makes reviewing/undoing easy. |

## 1. Short memory (Garnet)

Nothing has to be installed. AIT runs [Garnet](https://microsoft.github.io/garnet/) (a Redis-protocol server from
Microsoft) inside its own process, on the port of `ConnectionStrings:Redis` (default `localhost:6379`), reachable from
this computer only. It adds ±30 MB of RAM.

- Data: `%LOCALAPPDATA%\AIThread\garnet\<port>`. Every write is recorded in an *append-only file* before it is
  confirmed, so short memory is not lost even when AIT is killed or crashes. An hourly checkpoint trims that file.
- Another instance (e.g. for a trial) just uses another port in `ConnectionStrings:Redis`; its data is kept apart
  automatically.
- When that port is already used by another program (e.g. an old `aithread-redis` Docker container), Garnet is not
  started and AIT uses the server already on that port (logged as a warning).
- External Redis: set `AIThread:EmbeddedRedis:Enabled` = `false` and point `ConnectionStrings:Redis` to that server
  (StackExchange.Redis format, e.g. `myhost:6379,password=secret`).

**Moving from Docker Redis.** Short memory still in the old container is not moved automatically. Before stopping the
container, open the **Memory** menu and click **→ DB** on the short memories you want to keep (or leave the container
running for ±24 hours so the worker moves them). Then: `docker rm -f aithread-redis` and
`docker volume rm ai-multi-agent_aithread-redis` (check the volume name with `docker volume ls`).

## 2. 9router

```powershell
npm install -g 9router
9router --no-browser        # or just: 9router
```

The step above is optional when AIT is installed through npm (`npm i -g aithread`): that package depends on `9router`,
and on start AIT starts 9router itself when it is not running yet.

| Behaviour | Explanation |
|---|---|
| When it is started | Once when AIT starts, when 9router is *Offline* and the Base URL in Setting is a local address (`localhost`, `127.0.0.1`, `[::1]`). The port is taken from that URL. When 9router stops later, AIT only reports it (it may have been closed on purpose); click **Start 9router** on the Dashboard to start it again. |
| Which 9router | In order: `NineRouter:CliPath` → the global npm 9router (`npm i -g 9router`, usually the latest version) → the copy inside the `aithread` package. So removing the global 9router does not turn this feature off. |
| How it runs | `node cli.js --tray --no-browser --skip-update -p <port>`, the same as 9router's own *Hide to Tray*: no window, the 9router tray icon shows up. Output goes to `%LOCALAPPDATA%\AIThread\logs\9router.log`. It stays alive when AIT stops, unless `NineRouter:StopOnExit` = `true`. |
| Port in use | On start, 9router kills other processes on its port. That is why AIT does not start 9router when the port is already used by another program that does not answer as 9router. |
| Not installed yet | No 9router at all (e.g. the standalone exe without npm): the **Start 9router** button on the Dashboard runs `npm install -g 9router` and then starts it. On start, it is installed automatically only when `NineRouter:AutoInstall` = `true` (once per process). Needs Node.js + npm and internet. |

1. Open the dashboard at http://localhost:20128.
2. Connect at least one provider (the *Providers* menu). Ideally one strong provider for the planner and one cheap/free
   one for the executors.
3. (Optional) Create a *combo* so a model falls back to another provider automatically when its limit runs out.
4. Copy the **API key** from the 9router dashboard when the *require API key* option is on (on by default).

AIT uses these 9router endpoints: `GET /api/health`, `GET /v1/models`, `POST /v1/chat/completions`,
`POST /v1/messages`, `POST /v1/messages/count_tokens`. For *Quota left* in the reply footer AIT also reads the 9router
dashboard API (`GET /api/combos`, `/api/providers/client`, `/api/usage/{id}`). *Require login* in the 9router Settings
(on by default) closes this API. For a 9router on the same computer, AIT can still read it with the local CLI token,
the way the 9router CLI itself does (the `x-9r-cli-token` header from `%APPDATA%\9router\machine-id` +
`auth\cli-secret`). This token is not sent to a 9router on another computer, so turn *Require login* off there;
otherwise the footer is shown without the quota.

## 3. Database

There is no need to create the database by hand — EF Core migrations are applied automatically when the application
starts.

To use a SQL Server other than LocalDB, change `ConnectionStrings:Default` in `src/AIThread.UI/appsettings.json`, for
example:

```json
"Default": "Server=localhost;Database=AIT;User Id=aithread;Password=...;TrustServerCertificate=True"
```

## 4. Running AIT

```powershell
dotnet run --project src/AIThread.UI --launch-profile http
```

Open http://localhost:5225. From Visual Studio, the `https` profile also opens `http://localhost:5225` — **use the http
address** for CLIs, because Node.js based CLIs (e.g. Claude Code) do not trust the ASP.NET Core development
certificate.

Changing the port:

```powershell
dotnet run --project src/AIThread.UI --no-launch-profile --urls http://localhost:6000
```

After the port changes, turn the active CLI switches in the **CLI** menu off and on again so the new URL is written
(see [CLI integration](03-integrasi-cli.md)).

## 5. Running in the background

`dotnet run` and Visual Studio only run AIT while their terminal/debugger is open. In Visual Studio, closing the browser
the debugger opened also stops the application. For everyday use (CLI, Telegram), run AIT in the background so it stays
alive when the browser, the terminal and Visual Studio are closed:

```powershell
.\scripts\aithread.ps1 start          # publish Release, then run without a window at http://localhost:5225
.\scripts\aithread.ps1 status         # PID, since when, log location, autostart status
.\scripts\aithread.ps1 logs -Follow   # follow the log (Ctrl+C to quit)
.\scripts\aithread.ps1 restart        # after the code changed: stop, publish again, start
.\scripts\aithread.ps1 stop           # shut down cleanly
.\scripts\aithread.ps1 autostart-on   # start automatically at Windows login (autostart-off removes it)
```

Add `-Open` to `start`/`restart` to open the dashboard right away. When PowerShell refuses to run scripts (*running
scripts is disabled on this system*), use `scripts\aithread.cmd` with the same arguments.

Port 5225 can be used by one instance only: stop the instance from Visual Studio / `dotnet run` first. `start` refuses
when the port is already in use and names the process.

| Item | Details |
|---|---|
| Application location | `%LOCALAPPDATA%\AIThread\app` (the output of `dotnet publish -c Release`). The build uses its own folder (`%LOCALAPPDATA%\AIThread\build`), so it does not clash with a `bin/` locked by Visual Studio. When the publish fails, the old version keeps being used. |
| Logs | `%LOCALAPPDATA%\AIThread\logs\aithread-<time>.log` (and `.err.log`); the last 10 runs are kept. The thread activity log is still in the Dashboard. One line per entry (`time level category: message`); warnings show only the error message (`→ Type: message`) without a stack trace, and the exact same warning within 5 minutes is printed once with a *Nx similar hidden* note. Errors keep their full stack trace. |
| Configuration | `appsettings.json` is published along: change `src/AIThread.UI/appsettings.json`, then `restart`. Environment `Production`. Settings from the UI (database) stay the same as during development. |
| Another port | `-Port 6000` — use the same `-Port` for every command, then apply the CLI integrations again in the CLI menu. |
| Autostart | Task Scheduler `AIThread`, 30 seconds after login, running as your own account (no admin). That way LocalDB and the encrypted 9router API key keep working. It uses the last publish without rebuilding. |
| Stop | Through `POST /api/system/shutdown`: running threads are cancelled and recorded (±15 seconds at most), then the process exits. When it does not stop within 45 seconds, the process is killed. |
| Sudden stop | Logoff, a crash or a kill: on the next start, threads left behind are marked *Cancelled* with the message *AIT stopped while this thread was running.* |

9router runs as a separate process. AIT starts it on start when it is not alive yet (see step 2), and reconnects
automatically when 9router comes up after AIT. Short memory (Garnet) runs inside the AIT process.

## 6. Standalone AIThread.exe (without .NET)

To use AIT without the .NET SDK or `dotnet run`, build a self-contained `AIThread.exe` (Windows x64, ±80 MB, one file):

```powershell
.\scripts\publish-exe.ps1 -Zip     # output: dist\AIThread\ and dist\AIThread-win-x64.zip
```

The `dist\AIThread` folder holds `AIThread.exe`, `appsettings.json` (can be changed, e.g. the connection string),
`wwwroot\` and `docs\`; copy the whole folder to another computer. SQL Server (LocalDB) and Node.js are still needed;
9router is installed with `npm i -g 9router` or the **Start 9router** button on the Dashboard.
Run `AIThread.exe` without arguments for the **interactive menu**; the server runs in that window and logs to a file:

| Menu | Function |
|---|---|
| 1 Web UI | Open the dashboard in the browser. |
| 2 Hide to the tray | Move to the background with an icon in the system tray; the terminal window may be closed. Running threads are cancelled and recorded, because the server restarts as a process without a window (±10 seconds). |
| 3 Live log | Follow the server log; any key goes back to the menu. |
| 4 Restart server | Apply a freshly changed `appsettings.json`. |
| 5 Change port | Stored in `%LOCALAPPDATA%\AIThread\cli.json`. Apply the CLI integrations again (CLI menu) because the address changes. |
| 6 Autostart | Run the tray automatically at Windows login (the user's `Run` key, no admin). |
| 0 Exit | Shut the server down cleanly. |

When an instance is already running on that port (e.g. in the tray), the menu controls that instance: open the Web UI,
see the latest log, or shut it down. Right-click the tray icon for *Open Web UI*, *View log*, *Open log folder*, *Run at
Windows login* and *Exit*; a double click opens the Web UI.

The menu and the tray follow the [display language](02-konfigurasi.md#display-language) (stored in `cli.json` too).

Commands without the menu (`AIThread.exe help` shows them all):

```powershell
AIThread.exe serve            # in this window, the log shows in the console (Ctrl+C to stop)
AIThread.exe tray --open      # background with a tray icon, then open the dashboard
AIThread.exe status           # PID, since when, log location, autostart status
AIThread.exe logs -f          # follow the latest log
AIThread.exe logs export      # diagnostic ZIP (incidents + stack traces, logs); --days 7, or a file name
AIThread.exe stop             # shut down cleanly
AIThread.exe autostart on     # or off
AIThread.exe setup            # open the Setup page in the browser
AIThread.exe setup export team.json   # team setup profile, without API keys and passwords
AIThread.exe setup import team.json   # preview, ask for API keys/passwords, then apply (--yes = no confirmation)
AIThread.exe serve --port 6000
```

The `setup` commands use the running instance (run `tray` first). The content and rules of a profile are in
[Team setup profile](02-konfigurasi.md#team-setup-profile).

The default port is 5225 (or the one last saved from the menu); `--port` applies to one command. Logs are in
`%LOCALAPPDATA%\AIThread\logs` (the same folder as `scripts\aithread.ps1`). With `--urls`/`--environment` or without a
terminal (e.g. `dotnet run`, Visual Studio), the exe runs as a plain host without the menu.

The exe autostart (`Run` key) and `scripts\aithread.ps1 autostart-on` (Task Scheduler) do not need to be on together;
a second instance detects the first one and only opens the dashboard.

### Through npm

`.\scripts\publish-npm.ps1` creates two packages in `dist\npm`: `aithread-win-x64` (holding `AIThread.exe`, ±80 MB
compressed) and `aithread` (a small launcher, `optionalDependencies` on the platform package, `dependencies` on
`9router`). Install them with `npm i -g dist\npm\aithread-win-x64-<version>.tgz dist\npm\aithread-<version>.tgz`, then
run `aithread` (same commands and options as the exe). `-Publish` runs `npm publish` for both packages; the version is
taken from `<Version>` in `AIThread.UI.csproj`. Windows x64 only.

Because the `node_modules` folder is overwritten on update, change the configuration in
`%LOCALAPPDATA%\AIThread\appsettings.json` (optional, fill in only the keys you change); this file overrides the
`appsettings.json` next to the exe, also for the exe built by `publish-exe.ps1`.

## 7. Verification

Open the **Setup** menu: each step shows its status from the current state of AIT, and an unfinished step can be done
right there (see [Menu guide → Setup](05-menu.md#setup)). On the **Dashboard**, the four status cards at the top should be:

| Card | Normal state |
|---|---|
| 9router | *Online* · latency · number of models & combos · *API key OK* |
| Short memory · Redis | *Connected* |
| Database | *Connected* |
| Node pipeline | *Ready* (after the nodes are configured in the next step) |

## `appsettings.json` configuration

The `AIThread` section controls infrastructure (other runtime settings are in the Setting menu):

| Key | Default | Function |
|---|---|---|
| `ShortMemoryHours` | `24` | Age of a short memory before it is moved to the database. |
| `MemoryConsolidationIntervalMinutes` | `10` | Interval of the worker that moves short → long memory. |
| `MemoryRelabelIntervalMinutes` | `10` | Interval of the worker that gives old memories structured labels and checks for duplicate modules (see [Memory](06-memory.md#label-migration-old-memory)). |
| `MemoryAcronyms` | `[]` | Extra abbreviations written in capitals in memory labels, e.g. `["SKU", "GL"]` (API, DTO, UI, etc. are built in). |
| `RouterMonitorIntervalSeconds` | `15` | Interval of the 9router health check for the dashboard. |
| `ActivityLogRetentionDays` | `14` | Older dashboard log entries are deleted automatically. |
| `ExecutorFullFileMaxLines` | `400` | Files up to this length are sent whole to an executor; larger files are sent as excerpts. |
| `Telegram:ApiBaseUrl` | `https://api.telegram.org` | The Bot API endpoint for [prompts from Telegram](11-telegram.md) (the bot token is set in the Setting menu). |
| `Telegram:PollTimeoutSeconds` | `50` | How long one `getUpdates` long poll waits for new messages (1–50). |
| `EmbeddedRedis:Enabled` | `true` | Run Garnet inside AIT for short memory. `false` = use the external Redis in `ConnectionStrings:Redis`. |
| `EmbeddedRedis:DataDirectory` | *(empty)* | Location of the Garnet data. Empty = `%LOCALAPPDATA%\AIThread\garnet\<port>`. |
| `EmbeddedRedis:MemorySize` | `16m` | RAM limit of the Garnet log. The oldest records beyond it are dropped, so do not make it smaller (one short memory is ±1 KB). |
| `EmbeddedRedis:CheckpointIntervalMinutes` | `60` | Interval of the checkpoint that trims the *append-only file*. |
| `NineRouter:AutoStart` | `true` | Start a local 9router when AIT starts and it is not running yet (see [step 2](#2-9router)). |
| `NineRouter:AutoInstall` | `false` | On start, run `npm install -g 9router` when 9router is not found. The *Start 9router* button may always install it. |
| `NineRouter:StopOnExit` | `false` | Stop the 9router started by AIT when AIT stops. A 9router you started yourself is never stopped. |
| `NineRouter:StartTimeoutSeconds` | `90` | How long to wait for 9router to answer after starting it (the first start also prepares its SQLite runtime). |
| `NineRouter:InstallTimeoutSeconds` | `300` | Time limit of `npm install -g 9router`. |
| `NineRouter:NodePath` / `NineRouter:CliPath` | *(empty)* | Path of a specific `node.exe` / 9router `cli.js`. Empty = look on PATH, in the global npm folder and in the `aithread` package. |

Restart the application after changing `appsettings.json`.

Next: [Configuration](02-konfigurasi.md).
