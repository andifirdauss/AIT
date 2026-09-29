using AiThread.API;
using AiThread.Data;
using AiThread.Service;

var builder = WebApplication.CreateBuilder(args);

// Claude Code resends the whole conversation on every turn; allow large request bodies.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 100 * 1024 * 1024);

// Add services to the container.
builder.Services.AddDataLayer(builder.Configuration);
builder.Services.AddServiceLayer(builder.Configuration);
builder.Services.AddControllersWithViews().AddApiControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Claude Code calls the proxy (/v1/*) and the planner CLI calls its MCP server (/mcp/*) over plain HTTP;
// redirecting them to HTTPS would break the connection.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/v1") && !context.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseHttpsRedirection());
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.Services.InitializeDataLayerAsync();

app.Run();
