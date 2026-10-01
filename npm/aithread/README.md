# aithread

Proxy, planner, dan orkestrasi multi-agent untuk Claude Code, dibungkus sebagai paket npm. Isinya `AiThread.exe`
standalone (tanpa .NET runtime); hanya Windows x64.

```powershell
npm i -g aithread
aithread            # menu interaktif
aithread serve      # server di jendela ini
aithread tray       # background dengan ikon tray
aithread help       # semua perintah
```

Atau tanpa memasang: `npx aithread serve`.

## Konfigurasi

Edit `%LOCALAPPDATA%\AiThread\appsettings.json` (dibuat sendiri; isi hanya kunci yang ingin diubah). File ini menimpa
`appsettings.json` bawaan paket dan tidak hilang saat `npm update`. Contoh:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=AiThread;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

Membutuhkan SQL Server (default LocalDB). Data dan log ada di `%LOCALAPPDATA%\AiThread`. Dokumentasi lengkap ada di
repo AiThread (`docs/`).
