using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiThread.API.Controllers;
using AiThread.Common.DTOs;

namespace AiThread.UI.Hosting;

// Talks to an AiThread instance that is already running (this process or another one) through /api/system, and
// /api/setup for the setup command.
public sealed class ServerClient(int port) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // As the API and the profile file write them: enums by name.
    public static readonly JsonSerializerOptions ProfileJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    // An import may connect to a new memory store and copy memory into it, far longer than a status probe.
    private readonly HttpClient _setup = new() { Timeout = TimeSpan.FromMinutes(10) };

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

    // The team profile exactly as the Setup page downloads it.
    public Task<string> ExportProfileAsync() => _setup.GetStringAsync($"{BaseUrl}/api/setup/profile");

    // A refused import (invalid file, validation) comes back as Success = false with the message.
    public async Task<ApiResponse<SetupProfileImportResultDto>> ImportProfileAsync(SetupProfileImportRequest request)
    {
        using var response = await _setup.PostAsJsonAsync($"{BaseUrl}/api/setup/profile/import", request, ProfileJson);
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiResponse<SetupProfileImportResultDto>>(ProfileJson)
                   ?? new ApiResponse<SetupProfileImportResultDto>(false, null, $"HTTP {(int)response.StatusCode}");
        }
        catch (JsonException)
        {
            return new ApiResponse<SetupProfileImportResultDto>(false, null, $"HTTP {(int)response.StatusCode}");
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _setup.Dispose();
    }
}
