using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExplorerPanel.Core.Api;

/// <summary>Обращения к работающей фоновой программе через её местный интерфейс.</summary>
public sealed class HostClient : IDisposable
{
    static readonly TimeSpan Quick = TimeSpan.FromSeconds(5);
    static readonly TimeSpan Long = TimeSpan.FromMinutes(15); // действие может ждать серверы и выполнять долгую команду

    readonly HttpClient _http;

    public int Port { get; }

    public HostClient(int port)
    {
        Port = port;
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.Add(ApiProtocol.Header, "1");
    }

    /// <summary>Находит работающую программу по файлу host.json. Возвращает null, если она не запущена.</summary>
    public static async Task<HostClient?> TryConnectAsync(string? hostInfoFile = null)
    {
        try
        {
            var text = await File.ReadAllTextAsync(hostInfoFile ?? AppPaths.HostInfoFile);
            var info = JsonSerializer.Deserialize<HostInfo>(text, ApiProtocol.Json);
            if (info == null) return null;
            var client = new HostClient(info.Port);
            try
            {
                await client.PingAsync();
                return client;
            }
            catch
            {
                client.Dispose();
                return null; // файл остался от программы, которая уже не работает
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public Task<PingResponse> PingAsync() => GetAsync<PingResponse>("v1/ping", Quick);

    public Task<StatusResponse> StatusAsync(string folder) =>
        GetAsync<StatusResponse>("v1/status?folder=" + Uri.EscapeDataString(folder), Quick);

    public Task<ApiResult> SetAsync(string folder, JsonObject values) => PostAsync("v1/set", new SetRequest(folder, values), Quick);
    public Task<ApiResult> DoAsync(string folder, string action) => PostAsync("v1/do", new DoRequest(folder, action), Long);
    public Task<ApiResult> TrustAsync(string folder, bool? trusted) => PostAsync("v1/trust", new TrustRequest(folder, trusted), Quick);
    public Task<ApiResult> ReloadAsync(string folder) => PostAsync("v1/reload", new FolderRequest(folder), Quick);

    async Task<T> GetAsync<T>(string path, TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        using var response = await _http.GetAsync(path, cancel.Token);
        return await ReadAsync<T>(response, cancel.Token);
    }

    async Task<ApiResult> PostAsync<TRequest>(string path, TRequest body, TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        using var response = await _http.PostAsJsonAsync(path, body, ApiProtocol.Json, cancel.Token);
        return await ReadAsync<ApiResult>(response, cancel.Token);
    }

    static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancel)
    {
        var text = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"the host answered {(int)response.StatusCode}: {text}", null, response.StatusCode);
        return JsonSerializer.Deserialize<T>(text, ApiProtocol.Json) ?? throw new JsonException("empty answer from the host");
    }

    public void Dispose() => _http.Dispose();
}
