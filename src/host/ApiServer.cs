using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using ExplorerPanel.Core;
using ExplorerPanel.Core.Api;

namespace ExplorerPanel.Host;

/// <summary>Что местный интерфейс умеет просить у программы.</summary>
interface IHostApi
{
    StatusResponse Status(string folder);
    ApiResult Set(string folder, JsonObject values);
    Task<ApiResult> Do(string folder, string action);
    ApiResult Trust(string folder, bool? state);
    ApiResult Reload(string folder);
}

/// <summary>
/// Местный интерфейс: по нему с программой говорят команда <c>explorer-panel</c> и внешние программы
/// (например, сервер проекта, который хочет мгновенно обновить свой статус в панели).
/// Слушает только этот компьютер и требует особый заголовок — запрос со страницы в браузере его не пройдёт.
/// </summary>
sealed class ApiServer(IHostApi api, string hostInfoFile, int preferredPort = ApiProtocol.DefaultPort) : IDisposable
{
    HttpListener? _listener;

    public int Port { get; private set; }

    public void Start()
    {
        // порт по умолчанию может быть занят — пробуем следующие
        for (int port = preferredPort; port <= preferredPort + 10 && _listener == null; port++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                _listener = listener;
                Port = port;
            }
            catch (HttpListenerException) { listener.Close(); }
        }
        if (_listener == null) throw new InvalidOperationException($"no free port for the local interface near {preferredPort}");

        // по этому файлу программу находит команда explorer-panel
        Directory.CreateDirectory(Path.GetDirectoryName(hostInfoFile)!);
        File.WriteAllText(hostInfoFile, JsonSerializer.Serialize(new HostInfo(Port, Environment.ProcessId), ApiProtocol.Json));

        _ = Task.Run(Loop);
    }

    async Task Loop()
    {
        var listener = _listener!;
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { break; }
            _ = Task.Run(() => Handle(context));
        }
    }

    async Task Handle(HttpListenerContext context)
    {
        var request = context.Request;
        try
        {
            if (request.Headers[ApiProtocol.Header] == null)
            {
                await Write(context, HttpStatusCode.Forbidden, new ApiResult(false, $"missing {ApiProtocol.Header} header"));
                return;
            }

            switch (request.HttpMethod, request.Url!.AbsolutePath)
            {
                case ("GET", "/v1/ping"):
                    await Write(context, HttpStatusCode.OK, new PingResponse(AppInfo.Version, Environment.ProcessId));
                    break;

                case ("GET", "/v1/status"):
                    var folder = HttpUtility.ParseQueryString(request.Url.Query)["folder"];
                    if (string.IsNullOrEmpty(folder)) await Write(context, HttpStatusCode.BadRequest, new ApiResult(false, "\"folder\" is required"));
                    else await Write(context, HttpStatusCode.OK, api.Status(folder));
                    break;

                case ("POST", "/v1/set"):
                    var set = await Read<SetRequest>(request);
                    await Write(context, HttpStatusCode.OK, api.Set(set.Folder, set.Values));
                    break;

                case ("POST", "/v1/do"):
                    var run = await Read<DoRequest>(request);
                    await Write(context, HttpStatusCode.OK, await api.Do(run.Folder, run.Action));
                    break;

                case ("POST", "/v1/trust"):
                    var trust = await Read<TrustRequest>(request);
                    await Write(context, HttpStatusCode.OK, api.Trust(trust.Folder, trust.Trusted));
                    break;

                case ("POST", "/v1/reload"):
                    var reload = await Read<FolderRequest>(request);
                    await Write(context, HttpStatusCode.OK, api.Reload(reload.Folder));
                    break;

                default:
                    await Write(context, HttpStatusCode.NotFound, new ApiResult(false, $"unknown path {request.Url.AbsolutePath}"));
                    break;
            }
        }
        catch (JsonException ex)
        {
            await Write(context, HttpStatusCode.BadRequest, new ApiResult(false, "invalid JSON: " + ex.Message));
        }
        catch (Exception ex)
        {
            Log.Write("local interface: " + ex);
            await Write(context, HttpStatusCode.InternalServerError, new ApiResult(false, ex.Message));
        }
    }

    static async Task<T> Read<T>(HttpListenerRequest request) where T : class
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(body, ApiProtocol.Json) ?? throw new JsonException("empty request");
    }

    static async Task Write(HttpListenerContext context, HttpStatusCode status, object body)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), ApiProtocol.Json);
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // тот, кто спрашивал, уже ушёл
        }
    }

    public void Dispose()
    {
        try
        {
            _listener?.Close();
            // файл убираем, только если он всё ещё про нас
            if (File.Exists(hostInfoFile) && JsonSerializer.Deserialize<HostInfo>(File.ReadAllText(hostInfoFile), ApiProtocol.Json)?.Pid == Environment.ProcessId)
                File.Delete(hostInfoFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ObjectDisposedException) { }
    }
}

/// <summary>Настоящая реализация: запросы местного интерфейса выполняются над проектами программы.</summary>
sealed class HostApi(HostContext host) : IHostApi
{
    static readonly IReadOnlyDictionary<string, ServiceInfo> NoServices = new Dictionary<string, ServiceInfo>();

    public StatusResponse Status(string folder) =>
        host.Projects.ForFolder(folder)?.BuildStatus()
        ?? new StatusResponse(null, null, false, [$"no {PanelLoader.DirName}/{PanelLoader.FileName} found for {folder}"], NoServices, [], null);

    public ApiResult Set(string folder, JsonObject values)
    {
        if (host.Projects.ForFolder(folder) is not { } project) return NoPanel(folder);
        foreach (var (name, value) in values) project.SetValue(name, value?.DeepClone());
        return new ApiResult(true);
    }

    public Task<ApiResult> Do(string folder, string action) =>
        host.Projects.ForFolder(folder) is { } project ? project.DoAsync(action) : Task.FromResult(NoPanel(folder));

    public ApiResult Trust(string folder, bool? state)
    {
        if (ProjectLocator.FindRoot(folder) is not { } root) return NoPanel(folder);
        host.Trust.Set(root, state);
        host.RunOnUi(host.Rescan); // панель должна появиться или исчезнуть сразу
        return new ApiResult(true);
    }

    public ApiResult Reload(string folder)
    {
        if (host.Projects.ForFolder(folder) is not { } project) return NoPanel(folder);
        project.Reload();
        return new ApiResult(true);
    }

    static ApiResult NoPanel(string folder) => new(false, $"no {PanelLoader.DirName}/{PanelLoader.FileName} found for {folder}");
}
