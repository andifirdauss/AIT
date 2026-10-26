# 11. Prompts from Telegram

Send prompts to AIT from your phone while the laptop is left running at the office or at home. You chat with a private
Telegram bot; AIT on the laptop runs planner → executor → wrapper as usual, reports progress to the chat, then sends the
summary.

```
Phone (Telegram) ──► Telegram server ◄──(long polling, outgoing connection)── AIT on the laptop ─► Planner ─► Executor × N ─► Wrapper
                 ◄── live status & summary ─────────────────────────────────────┘
```

AIT **pulls** messages from Telegram (long polling `getUpdates`). All it needs is an outgoing HTTPS connection to
`api.telegram.org` — no public IP, port forwarding or tunnel such as ngrok, so it still works behind an office
NAT/firewall.

The bot's messages follow the [display language](02-konfigurasi.md#display-language) of the laptop's AIT; its commands
are the same in both languages.

## Prerequisites

- AIT runs on the laptop, and the laptop stays on and connected to the internet (see
  [Keeping the laptop reachable](#keeping-the-laptop-reachable)).
- The Planner, Executor and Wrapper nodes are configured; at least one project is registered in the **Project** menu.
- A Telegram account.

## Setup (once, at the laptop)

1. **Create a bot.** In Telegram open `@BotFather` → `/newbot` → fill in a name and a username (it must end with `bot`) →
   copy the **token** (format `123456789:AAE…`). Treat the token like a password.
2. **Setting → the Telegram tab** (directly: http://localhost:5225/Setting#telegram): paste the token, tick *Turn on the
   Telegram bot*, click **Save Telegram**. The token is verified with Telegram before it is saved; the badge changes to
   **On · @bot_name**.
3. On the phone, open your bot (click `@bot_name` on the card, or search for it in Telegram) and press **Start**. The bot
   replies that the chat is not allowed yet, with your chat ID.
4. Back on the Telegram card: your chat shows as **Waiting for permission**. Match the name and ID, then click **Allow**.
   The bot sends a confirmation message.
5. In Telegram send `/projects` and choose a project with the buttons. When only one project is registered, that project
   is chosen automatically at the first prompt.

After that the laptop may be left alone.

## Sending a prompt

Send a plain text message, like typing in the CLI. The bot replies to your message with a **status message** that keeps
updating (updates do not trigger notifications):

```
🧵 MyApp · thread 1a2b3c4d
⚙️ Executing · 1/3 tasks · 42s

📋 Adds a description column to the user form.

✅ T1 Add description input — Views/User/Index.cshtml
🔄 T2 Send description field — wwwroot/js/pages/user.js
⏳ T3 Validate description — Services/UserService.cs
```

- During planning, the 🔍 line shows the tool the planner is using (`read_file`, `search_text`, …).
- The **⛔ Cancel** button below the status message stops the thread.
- When it is done, the bot sends a **new message** (with a notification) holding the final status, the number of tasks
  that succeeded, the duration and the wrapper's summary. Markdown headings, lists, bold, code blocks and tables are
  converted to Telegram's format; a long summary is split into several messages.
- Send `/diff` to see the file changes of that thread.

Questions that need no file changes (e.g. "explain the login flow of this project") are answered directly by the
planner. One chat runs **one thread at a time**; the next prompt is refused until the thread finishes or is cancelled.

Threads from Telegram are recorded like other threads: the source **Telegram** in the Dashboard, the session
`telegram:<chat id>` on the Project page, and memory is extracted as usual.

## Commands

| Command | Function |
|---|---|
| *(plain text)* | Runs the prompt in the active project. When there is no active project yet, the bot shows the project choices and runs the prompt after you choose. |
| `/projects` | The list of registered projects with buttons to choose. |
| `/project <number or name>` | Chooses a project without buttons. Part of the name is enough as long as it matches only one project. |
| `/status` | The active project, this chat's running thread, and the last 5 threads from **every** source (including the CLI). |
| `/cancel` | Cancels this chat's thread. `/cancel <id>` cancels another running thread — the 8-character id from `/status`, for example a CLI thread left running on the laptop. |
| `/diff` | The diff of the file changes of this chat's last thread. `/diff <id>` for another thread. |
| `/new` | Starts a new conversation: the next prompt is sent without history. |
| `/help`, `/start` | A short help and the active project. |

The command list also shows in Telegram's `/` menu button.

## Follow-up conversation

As in the CLI, the next prompt carries the conversation history for the planner: up to the last 6 exchanges (prompt +
summary) of this chat **in the same project**, cut when long. So "now add it to the detail page too" is understood
without repeating the context. The history starts from zero after `/new` or changing project.

When the planner asks back, the result is a numbered question with the status ❓ *Waiting for an answer*. Send the
number of your choice or your answer as a regular message; the same thread continues. See
[Questions from the planner](07-cara-kerja.md#questions-from-the-planner).

## Security

- Only **private chats allowed** in the Setting menu can run prompts. Messages from groups and channels are ignored.
- Allow only your own Telegram account: as in the CLI, a prompt can change project files on the laptop.
- A strange chat that messages the bot is recorded as *Waiting for permission* (the latest 20 are kept) and the
  dashboard log shows a warning. Click **Delete** when you do not know it; **Revoke** ends the access of an allowed chat
  right away.
- Threads only run in registered projects, and executors are still limited to the project folder.
- The token is stored encrypted (ASP.NET Core Data Protection), never sent back to the browser (only masked), and not
  written to logs.
- Prompts sent while AIT is not running (the laptop is off/asleep, the bot is off) are **not run** when AIT comes back —
  the bot replies so you send them again. Commands such as `/status` are still answered.
- AIT does not need to be opened to the internet. Do not forward port 5225 just for this feature.

## Keeping the laptop reachable

- Turn off sleep while the laptop is plugged in: Windows **Settings → System → Power & battery** → sleep when *plugged
  in* = **Never**, or run `powercfg /change standby-timeout-ac 0`. When the lid will be closed: **Control Panel → Power
  Options → Choose what closing the lid does** → *When plugged in* = **Do nothing**.
- Run AIT in the background (`.\scripts\aithread.ps1 start`, optionally `autostart-on`) so it does not stop when the
  browser, the terminal or Visual Studio is closed — see [Running in the background](01-instalasi.md#5-running-in-the-background).
- Make sure AIT and 9router are running before you leave, then test once with `/status` from the phone.
- When the internet connection drops, AIT tries again automatically (the pause grows up to 60 seconds) and the dashboard
  log records when the connection to Telegram recovered.

## Status on the Telegram card

| Status | Meaning |
|---|---|
| **Off** | The bot is turned off or the token is not filled in yet. |
| **Connecting…** | The token is being verified and long polling is starting. |
| **On** · `@bot` · *last check …* | Waiting for messages. *Last check* updates every long polling cycle (±50 seconds when there are no messages). |
| **Problem** | The cause shows below it: the token was rejected, Telegram cannot be reached, or the token is in use by another program. |

The chat table shows the name, username, ID, permission status, active project and the time of the last message; the
table updates live when a new chat comes in.

## `appsettings.json` configuration

The `AIThread:Telegram` section (the token and on/off are set from the Setting menu, not here):

| Key | Default | Function |
|---|---|---|
| `ApiBaseUrl` | `https://api.telegram.org` | The Bot API endpoint. Change it when you use your own [Bot API server](https://github.com/tdlib/telegram-bot-api) or a proxy. |
| `PollTimeoutSeconds` | `50` | How long one `getUpdates` call waits for new messages (1–50). |

The Windows system HTTP proxy is used automatically.

## Troubleshooting

| Symptom | Cause | Solution |
|---|---|---|
| Save: *Telegram rejected the token* | The token is wrong, cut off, or revoked. | Copy it again from `@BotFather` → `/mybots` → your bot → *API Token*. |
| Save: *The token cannot be verified: … cannot be reached* | The network blocks `api.telegram.org`. | Check the internet/office proxy; set `ApiBaseUrl` when it must go through a proxy. |
| Problem: *This token is in use by another program…* | Another AIT instance (or another program) receives messages with the same token. | Stop the other instance, or create a separate bot for each instance. |
| The bot does not reply at all | AIT is not running, the laptop is asleep/off, or the bot is off. | Check the laptop. Once it is back, send the prompt again. |
| The bot replies *This chat is not allowed yet* | The chat is not allowed yet. | Setting → Telegram → **Allow**. |
| *Thread … is still running* | One chat runs only one thread. | Wait until it finishes, or `/cancel`. |
| The status message stops updating | The status message was deleted, or the network dropped for a while. | The final result is still sent as a new message; check `/status`. |
| *AIT stopped while thread … was running* | AIT was stopped or restarted cleanly while the thread ran. | Once AIT is back, review the tasks that finished with `/diff <id>`, then send the prompt again when needed. |
| A prompt's final result never comes | AIT died suddenly (crash, power, laptop asleep) while the thread ran — it had no chance to tell. | Check `/status`, then as in the row above. |
| The summary shows without formatting | Telegram rejected that message's format, so AIT sent it again as plain text. | No action needed. |
| *Thread failed: No active Planner node yet* | The pipeline is not complete. | Complete the nodes in the Node menu (it must be done from the laptop). |
