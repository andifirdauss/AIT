using System.Net.Http.Json;
using System.Text.Json;
using AiThread.API.Controllers;
using AiThread.Common.DTOs;

namespace AiThread.UI.Hosting;

// Talks to an AiThread instance that is already running (this process or another one) through /api/system.
public sealed class ServerClient(int port) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public int Port { get; } = port;

    public string BaseUrl => $"http://localhost:{Port}";

    public async Task<SystemInfoDto?> GetInfoAsync()
    {
        try
        {
            var response = await _http.GetFromJsonAsync<ApiResponse<SystemInfoDto>>($"{BaseUrl}/api/system", Json);
            return response is { Success: true } ? response.Data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    // Graceful stop: running threads are cancelled and recorded before the process exits.
    public async Task<bool> ShutdownAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/system/shutdown");
            request.Headers.Add(SystemController.ShutdownHeader, "1");
            using var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    public void Dispose() => _http.Dispose();
}
