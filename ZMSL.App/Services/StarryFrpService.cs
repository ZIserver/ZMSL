using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ZMSL.App.Models;

namespace ZMSL.App.Services;

public class StarryFrpTunnelStateChangedEventArgs : EventArgs
{
    public int? ProxyId { get; set; }
    public string ProxyName { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
}

public class StarryFrpTunnelLogEventArgs : EventArgs
{
    public int ProxyId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class StarryFrpService
{
    public const string DownloadUrl = "https://hi.oss.ioll.cc/hi168-19258-4197gawv/soft/starryfrp/0.66.0-starry-26.1.30/frpc_windows_amd64.exe";
    public const string StarryFrpExeName = "starryfrp.exe";
    public const string StarryFrpTomlName = "starryfrp.toml";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DatabaseService _db;
    private readonly ILogger<StarryFrpService> _logger;
    private readonly Dictionary<int, List<string>> _proxyLogs = new();
    private Process? _process;
    public event EventHandler<FrpcDownloadProgressEventArgs>? ProgressChanged;
    public event EventHandler<FrpcDownloadStateChangedEventArgs>? StateChanged;
    public event EventHandler<StarryFrpTunnelStateChangedEventArgs>? TunnelStateChanged;
    public event EventHandler<StarryFrpTunnelLogEventArgs>? TunnelLogReceived;
    public bool IsDownloading { get; private set; }
    public int? CurrentRunningProxyId { get; private set; }
    public string CurrentRunningProxyName { get; private set; } = string.Empty;
    public StarryFrpService(IHttpClientFactory httpClientFactory, DatabaseService db, ILogger<StarryFrpService> logger) { _httpClientFactory = httpClientFactory; _db = db; _logger = logger; }
    private void LogApiRequest(string method, string path) => _logger.LogInformation("[StarryFrp API] 请求 {Method} {Path}", method, path);
    private void LogApiResponse(string method, string path, int statusCode) => _logger.LogInformation("[StarryFrp API] 响应 {Method} {Path} => {StatusCode}", method, path, statusCode);
    public static string GetStarryFrpExePath() => Path.Combine(AppContext.BaseDirectory, "frpc", StarryFrpExeName);
    public static string GetStarryFrpConfigPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZMSL", "frp", StarryFrpTomlName);
    public static bool IsStarryFrpInstalled() => File.Exists(GetStarryFrpExePath());
    public async Task<string?> GetSavedTokenAsync() { var s = await _db.GetSettingsAsync(); return string.IsNullOrWhiteSpace(s.StarryFrpToken) ? null : s.StarryFrpToken; }
    public async Task SaveTokenAsync(string token) { var s = await _db.GetSettingsAsync(); s.StarryFrpToken = token; await _db.SaveSettingsAsync(s); }
    public async Task ClearTokenAsync() { var s = await _db.GetSettingsAsync(); s.StarryFrpToken = null; await _db.SaveSettingsAsync(s); }
    public async Task<(bool Success, string Message, StarryFrpUserInfo? User)> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return (false, "请输入访问密钥 Token", null);
        try
        {
            var client = CreateClient(token); LogApiRequest("GET", "api/user/info"); using var response = await client.GetAsync("api/user/info"); LogApiResponse("GET", "api/user/info", (int)response.StatusCode);
            if ((int)response.StatusCode == 401) return (false, "Token 无效或已过期", null);
            var result = await response.Content.ReadFromJsonAsync<StarryFrpApiResponse<StarryFrpUserInfo>>(JsonOptions);
            return result?.IsSuccess == true && result.Data != null ? (true, result.Message, result.Data) : (false, result?.Message ?? $"请求失败：{response.StatusCode}", null);
        }
        catch (Exception ex) { _logger.LogError(ex, "验证 StarryFrp Token 失败"); return (false, ex.Message, null); }
    }
    public async Task<List<StarryFrpProxy>> GetProxiesAsync(string token) { var c = CreateClient(token); LogApiRequest("GET", "api/tunnels"); using var resp = await c.GetAsync("api/tunnels"); LogApiResponse("GET", "api/tunnels", (int)resp.StatusCode); var r = await resp.Content.ReadFromJsonAsync<StarryFrpApiResponse<List<StarryFrpProxy>>>(JsonOptions); return r?.Data ?? new(); }
    public async Task<List<StarryFrpNode>> GetNodesAsync(string token) { var c = CreateClient(token); LogApiRequest("GET", "api/nodes"); using var resp = await c.GetAsync("api/nodes"); LogApiResponse("GET", "api/nodes", (int)resp.StatusCode); var r = await resp.Content.ReadFromJsonAsync<StarryFrpApiResponse<List<StarryFrpNode>>>(JsonOptions); return r?.Data ?? new(); }
    public async Task<int?> GetFreePortAsync(string token, int nodeId) { var c = CreateClient(token); var path = $"api/node/ports?node_id={nodeId}&limit=1"; LogApiRequest("GET", path); using var resp = await c.GetAsync(path); LogApiResponse("GET", path, (int)resp.StatusCode); var r = await resp.Content.ReadFromJsonAsync<StarryFrpApiResponse<StarryFrpPortsData>>(JsonOptions); return r?.Data?.AvailablePorts.FirstOrDefault(); }
    public async Task<(bool Success, string Message)> CreateTunnelAsync(string token, object request)
    {
        try
        {
            var c = CreateClient(token);
            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            LogApiRequest("POST", "api/tunnel/create");
            _logger.LogInformation("[StarryFrp API] 请求体 POST api/tunnel/create => {Body}", requestJson);
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var resp = await c.PostAsync("api/tunnel/create", content);
            LogApiResponse("POST", "api/tunnel/create", (int)resp.StatusCode);
            var responseText = await resp.Content.ReadAsStringAsync();
            _logger.LogInformation("[StarryFrp API] 响应体 POST api/tunnel/create => {Body}", responseText);
            if ((int)resp.StatusCode == 401) return (false, "Token 无效或已过期");
            if (string.IsNullOrWhiteSpace(responseText)) return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? "隧道创建请求已提交成功" : (resp.ReasonPhrase ?? "创建失败"));
            var r = JsonSerializer.Deserialize<StarryFrpApiResponse<StarryFrpCreateTunnelResponse>>(responseText, JsonOptions);
            return (r?.IsSuccess == true || resp.IsSuccessStatusCode, r?.Message ?? resp.ReasonPhrase ?? (resp.IsSuccessStatusCode ? "隧道创建成功" : "创建失败"));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "创建 StarryFrp 隧道请求失败");
            return (false, $"创建隧道请求失败：{ex.Message}");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "创建 StarryFrp 隧道网络连接中断");
            return (false, $"创建隧道连接中断：{ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析 StarryFrp 创建隧道响应失败");
            return (false, $"解析创建响应失败：{ex.Message}");
        }
    }
    public async Task<(bool Success, string Message, string? Config)> GetProxyConfigAsync(string token, int nodeId)
    {
        try
        {
            var c = CreateClient(token);
            var path = $"api/tunnel/config?node_id={nodeId}";
            LogApiRequest("GET", path);
            using var resp = await c.GetAsync(path);
            LogApiResponse("GET", path, (int)resp.StatusCode);

            var content = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var statusElement) && statusElement.TryGetInt32(out var statusCode) ? statusCode : 0;
            var message = root.TryGetProperty("msg", out var msgElement) ? msgElement.GetString() ?? "获取配置失败" : "获取配置失败";

            if (status != 200)
            {
                return (false, message, null);
            }

            if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.String)
            {
                _logger.LogWarning("StarryFrp 配置响应格式异常: {Content}", content);
                return (false, "配置响应格式不正确", null);
            }

            return (true, message, dataElement.GetString());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取 StarryFrp 配置请求失败");
            return (false, $"获取配置请求失败：{ex.Message}", null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析 StarryFrp 配置响应失败");
            return (false, $"解析配置响应失败：{ex.Message}", null);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "获取 StarryFrp 配置网络连接中断");
            return (false, $"获取配置连接中断：{ex.Message}", null);
        }
    }
    public async Task<(bool Success, string Message)> StartTunnelAsync(int proxyId, string proxyName, string config)
    {
        if (!IsStarryFrpInstalled()) return (false, "请先安装 StarryFrp 客户端"); if (string.IsNullOrWhiteSpace(config)) return (false, "配置内容为空");
        try
        {
            await SaveConfigAsync(config); if (_process is { HasExited: false }) { try { _process.Kill(true); await _process.WaitForExitAsync(); } catch { } }
            CurrentRunningProxyId = proxyId; CurrentRunningProxyName = proxyName; _proxyLogs[proxyId] = new(); TunnelStateChanged?.Invoke(this, new() { ProxyId = proxyId, ProxyName = proxyName, IsRunning = true }); AddTunnelLog(proxyId, $"开始启动隧道：{proxyName}");
            var p = new Process { StartInfo = new ProcessStartInfo { FileName = GetStarryFrpExePath(), Arguments = $"-c \"{GetStarryFrpConfigPath()}\"", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, CreateNoWindow = true }, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data) && CurrentRunningProxyId.HasValue) { AddTunnelLog(CurrentRunningProxyId.Value, e.Data); _logger.LogInformation("[StarryFrp] {Message}", e.Data); } };
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data) && CurrentRunningProxyId.HasValue) { AddTunnelLog(CurrentRunningProxyId.Value, $"[ERROR] {e.Data}"); _logger.LogWarning("[StarryFrp] {Message}", e.Data); } };
            p.Exited += (_, _) => { var id = CurrentRunningProxyId; var name = CurrentRunningProxyName; if (id.HasValue) AddTunnelLog(id.Value, $"隧道已退出，ExitCode: {p.ExitCode}"); CurrentRunningProxyId = null; CurrentRunningProxyName = string.Empty; _process = null; TunnelStateChanged?.Invoke(this, new() { ProxyId = id, ProxyName = name, IsRunning = false }); };
            p.Start(); ChildProcessManager.Instance.AddProcess(p); p.BeginOutputReadLine(); p.BeginErrorReadLine(); _process = p; AddTunnelLog(proxyId, $"隧道已启动，配置已保存到 {GetStarryFrpConfigPath()}"); return (true, $"隧道已启动：{proxyName}");
        }
        catch (Exception ex) { AddTunnelLog(proxyId, $"启动失败：{ex.Message}"); CurrentRunningProxyId = null; CurrentRunningProxyName = string.Empty; _process = null; TunnelStateChanged?.Invoke(this, new() { ProxyId = proxyId, ProxyName = proxyName, IsRunning = false }); _logger.LogError(ex, "启动 StarryFrp 隧道失败"); return (false, $"启动失败：{ex.Message}"); }
    }
    public async Task StopTunnelAsync() { var id = CurrentRunningProxyId; var name = CurrentRunningProxyName; try { if (_process is { HasExited: false }) { _process.Kill(true); await _process.WaitForExitAsync(); } } catch (Exception ex) { _logger.LogWarning(ex, "停止 StarryFrp 隧道时出现异常"); if (id.HasValue) AddTunnelLog(id.Value, $"停止隧道时出现异常：{ex.Message}"); } finally { _process = null; CurrentRunningProxyId = null; CurrentRunningProxyName = string.Empty; TunnelStateChanged?.Invoke(this, new() { ProxyId = id, ProxyName = name, IsRunning = false }); } }
    public List<string> GetLogsForProxy(int proxyId) => _proxyLogs.TryGetValue(proxyId, out var logs) ? new(logs) : new();
    public void ClearLogsForProxy(int proxyId) => _proxyLogs[proxyId] = new();
    private void AddTunnelLog(int proxyId, string message) { var m = AnsiEscapeRegex.Replace(message, string.Empty).Replace("\u001b", string.Empty).Replace("\0", string.Empty).Trim(); if (string.IsNullOrWhiteSpace(m)) return; var line = $"[{DateTime.Now:HH:mm:ss}] {m}"; if (!_proxyLogs.TryGetValue(proxyId, out var logs)) _proxyLogs[proxyId] = logs = new(); logs.Add(line); if (logs.Count > 200) logs.RemoveAt(0); TunnelLogReceived?.Invoke(this, new() { ProxyId = proxyId, Message = line }); }
    public async Task SaveConfigAsync(string config) { var path = GetStarryFrpConfigPath(); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, config); }
    public async Task<bool> DownloadAndInstallAsync() { if (IsDownloading) return false; try { IsDownloading = true; StateChanged?.Invoke(this, new() { IsDownloading = true, Message = "正在下载 StarryFrp 客户端..." }); _logger.LogInformation("[StarryFrp API] 请求 GET {Path}", DownloadUrl); using var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZMSL", "3.1.0")); using var r = await c.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead); _logger.LogInformation("[StarryFrp API] 响应 GET {Path} => {StatusCode}", DownloadUrl, (int)r.StatusCode); r.EnsureSuccessStatusCode(); var total = r.Content.Headers.ContentLength ?? -1L; var target = GetStarryFrpExePath(); Directory.CreateDirectory(Path.GetDirectoryName(target)!); await using var input = await r.Content.ReadAsStreamAsync(); await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true); var buffer = new byte[81920]; long read = 0; int chunk; while ((chunk = await input.ReadAsync(buffer)) > 0) { await output.WriteAsync(buffer.AsMemory(0, chunk)); read += chunk; if (total > 0) ProgressChanged?.Invoke(this, new() { Progress = read * 100d / total, DownloadedBytes = read, TotalBytes = total }); } StateChanged?.Invoke(this, new() { IsDownloading = false, IsComplete = true, Message = "StarryFrp 客户端已安装完成" }); return true; } catch (Exception ex) { _logger.LogError(ex, "下载并安装 StarryFrp 失败"); StateChanged?.Invoke(this, new() { IsDownloading = false, IsComplete = false, Message = $"StarryFrp 下载失败：{ex.Message}" }); return false; } finally { IsDownloading = false; } }
    private HttpClient CreateClient(string token) { var c = _httpClientFactory.CreateClient(); c.BaseAddress = new Uri("https://api.starryfrp.com/"); c.DefaultRequestHeaders.Accept.Clear(); c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json")); c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZMSL", "3.1.0")); c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return c; }
}
