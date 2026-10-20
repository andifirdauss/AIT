using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    public async Task<RouterLaunchResultDto> EnsureRunningAsync(bool manual, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var status = await router.CheckStatusAsync(cancellationToken);
            if (status.IsOnline) return new RouterLaunchResultDto(true, false, false, "9router sudah berjalan.");

            if (!NineRouterLocator.TryGetLocalPort(status.BaseUrl, out var port))
                return Fail($"URL 9router ({status.BaseUrl}) bukan alamat lokal; AIT hanya bisa menyalakan 9router di komputer ini.");

            if (await IsListeningAsync(port, cancellationToken))
                return Fail($"Port {port} sudah dipakai, tetapi tidak menjawab sebagai 9router. AIT tidak menyalakan 9router karena 9router akan mematikan proses di port itu.");

            var dirs = NineRouterLocator.SearchDirs();
            var node = NineRouterLocator.FindExecutable("node.exe", Settings.NodePath, dirs, File.Exists);
            if (node is null)
                return Fail("Node.js tidak ditemukan. Pasang Node.js 18 atau lebih baru (https://nodejs.org), lalu coba lagi.");

            var cli = FindCli(dirs);
            var installed = false;
            if (cli is null)
            {
                var mayInstall = manual || (Settings.AutoInstall && Interlocked.Exchange(ref _autoInstallTried, 1) == 0);
                if (!mayInstall)
                    return Fail("9router belum terpasang. Pasang dengan `npm install -g 9router`, klik Jalankan 9router di Dashboard, atau aktifkan AiThread:NineRouter:AutoInstall.");

                var error = await InstallAsync(node, dirs, cancellationToken);
                if (error is not null) return Fail(error);
                cli = FindCli(NineRouterLocator.SearchDirs());
                if (cli is null) return Fail("npm install -g 9router selesai, tetapi cli.js 9router tidak ditemukan di folder global npm.");
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
