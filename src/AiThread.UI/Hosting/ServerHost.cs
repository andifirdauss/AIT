using AiThread.API;
using AiThread.Data;
using AiThread.Service;

namespace AiThread.UI.Hosting;

// The web application, buildable more than once per process so the menu can restart it (new port, edited appsettings).
public sealed class ServerHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private WebApplication? _app;
    private Task? _stopTask;

    public bool IsRunning => _app is not null;


    // Completes when the host stops for any reason, including POST /api/system/shutdown.
    public Task Stopped { get; private set; } = Task.CompletedTask;

    // args: command-line arguments handed to the host builder. port: null keeps Kestrel's own configuration
    // (--urls, ASPNETCORE_URLS, launch profile), which is what dotnet run and Visual Studio rely on.
    public static WebApplication Build(string[] args, int? port)
    {
        var builder = port is null
            ? WebApplication.CreateBuilder(args)
            // Installed exe: settings, wwwroot and docs sit beside the exe, whatever the working directory is.
            : WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

        if (port is not null) builder.WebHost.UseUrls($"http://localhost:{port}");

        // Claude Code resends the whole conversation on every turn; allow large request bodies.
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 100 * 1024 * 1024);

        builder.Services.AddDataLayer(builder.Configuration);
        builder.Services.AddServiceLayer(builder.Configuration);
        builder.Services.AddControllersWithViews().AddApiControllers();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        // Coding agents call the proxy (/v1/*, and /v1beta/* for Gemini), Claude Code its HTTP hooks (/api/hooks/*), and
        // the planner CLI its MCP server (/mcp/*), over plain HTTP; redirecting them to HTTPS would break the connection.
        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/v1")
                       && !context.Request.Path.StartsWithSegments("/v1beta")
                       && !context.Request.Path.StartsWithSegments("/mcp")
                       && !context.Request.Path.StartsWithSegments("/api/hooks"),
            branch => branch.UseHttpsRedirection());
        app.UseRouting();

        app.UseAuthorization();

        app.MapStaticAssets();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
            .WithStaticAssets();

        return app;
    }

    // Returns once Kestrel is listening. Throws when the port is taken.
    public async Task StartAsync(string[] args, int? port)
    {
        if (_app is not null) throw new InvalidOperationException("Server sudah berjalan.");

        var app = Build(args, port);
        try
        {
            // Migrate before serving, as the plain host does; a database outage is logged, not fatal.
            await app.Services.InitializeDataLayerAsync();
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        _app = app;
        var stopped = new TaskCompletionSource();
        app.Lifetime.ApplicationStopped.Register(() => stopped.TrySetResult());
        Stopped = stopped.Task;

        // Only app.Run() reacts to IHostApplicationLifetime.StopApplication (POST /api/system/shutdown) by itself.
        app.Lifetime.ApplicationStopping.Register(() => _ = Task.Run(StopAsync));
    }

    // Graceful: running threads are cancelled and recorded, then Garnet flushes. Every caller gets the same stop,
    // so whoever asks second still waits until the host is really down.
    public Task StopAsync()
    {
        lock (_gate)
        {
            var app = _app;
            if (app is null) return _stopTask ?? Task.CompletedTask;
            _app = null;
            return _stopTask = StopCoreAsync(app);
        }
    }

    private static async Task StopCoreAsync(WebApplication app)
    {
        try { await app.StopAsync(); }
        finally { await app.DisposeAsync(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
