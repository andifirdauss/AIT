using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    public async Task<RouterLaunchResultDto> EnsureRunningAsync(bool manual, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var status = await router.CheckStatusAsync(cancellationToken);
            if (status.IsOnline) return new RouterLaunchResultDto(true, false, false, Lang.T("9router sudah berjalan.", "9router is already running."));

            if (!NineRouterLocator.TryGetLocalPort(status.BaseUrl, out var port))
                return Fail(Lang.T($"URL 9router ({status.BaseUrl}) bukan alamat lokal; AIT hanya bisa menyalakan 9router di komputer ini.", $"The 9router URL ({status.BaseUrl}) is not a local address; AIT can only start 9router on this computer."));

            if (await IsListeningAsync(port, cancellationToken))
                return Fail(Lang.T($"Port {port} sudah dipakai, tetapi tidak menjawab sebagai 9router. AIT tidak menyalakan 9router karena 9router akan mematikan proses di port itu.", $"Port {port} is already in use, but does not answer as 9router. AIT does not start 9router because 9router would kill the process on that port."));

            var dirs = NineRouterLocator.SearchDirs();
            var node = NineRouterLocator.FindExecutable("node.exe", Settings.NodePath, dirs, File.Exists);
            if (node is null)
                return Fail(Lang.T("Node.js tidak ditemukan. Pasang Node.js 18 atau lebih baru (https://nodejs.org), lalu coba lagi.", "Node.js was not found. Install Node.js 18 or newer (https://nodejs.org), then try again."));

            var cli = FindCli(dirs);
            var installed = false;
            if (cli is null)
            {
                var mayInstall = manual || (Settings.AutoInstall && Interlocked.Exchange(ref _autoInstallTried, 1) == 0);
                if (!mayInstall)
                    return Fail(Lang.T("9router belum terpasang. Pasang dengan `npm install -g 9router`, klik Jalankan 9router di Dasbor, atau aktifkan AIThread:NineRouter:AutoInstall.", "9router is not installed yet. Install it with `npm install -g 9router`, click Start 9router on the Dashboard, or turn on AIThread:NineRouter:AutoInstall."));

                var error = await InstallAsync(node, dirs, cancellationToken);
                if (error is not null) return Fail(error);
                cli = FindCli(NineRouterLocator.SearchDirs());
                if (cli is null) return Fail(Lang.T("npm install -g 9router selesai, tetapi cli.js 9router tidak ditemukan di folder global npm.", "npm install -g 9router finished, but the 9router cli.js was not found in the global npm folder."));
                installed = true;
            }

            return await StartAsync(node, cli, port, installed, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
