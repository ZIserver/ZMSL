using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ZMSL.App.Models;

namespace ZMSL.App.Services;

public class MeFrpTunnelStateChangedEventArgs : EventArgs
{
    public int? ProxyId { get; set; }
    public string ProxyName { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
}

public class MeFrpTunnelLogEventArgs : EventArgs
{
    public int ProxyId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class MeFrpService
{
    public const string DownloadUrl = "https://drive.mcsl.com.cn/d/ME-Frp/Lanzou/MEFrp-Core/0.67.0_20260302_f1907e56/mefrpc_windows_amd64_0.67.0_20260302_f1907e56.zip";
    public const string MeFrpExeName = "mefrp.exe";
    public const string MeFrpTomlName = "mefrp.toml";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DatabaseService _db;
    private readonly ILogger<MeFrpService> _logger;
    private Process? _meFrpProcess;
    private readonly Dictionary<int, List<string>> _proxyLogs = new();

    public event EventHandler<FrpcDownloadProgressEventArgs>? ProgressChanged;
    public event EventHandler<FrpcDownloadStateChangedEventArgs>? StateChanged;
    public event EventHandler<MeFrpTunnelStateChangedEventArgs>? TunnelStateChanged;
    public event EventHandler<MeFrpTunnelLogEventArgs>? TunnelLogReceived;

    public bool IsDownloading { get; private set; }
    public int? CurrentRunningProxyId { get; private set; }
    public string CurrentRunningProxyName { get; private set; } = string.Empty;

    public MeFrpService(IHttpClientFactory httpClientFactory, DatabaseService db, ILogger<MeFrpService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
        _logger = logger;
    }

    public static string GetMeFrpExePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "frpc", MeFrpExeName);
    }

    public static string GetMeFrpConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZMSL",
            "frp",
            MeFrpTomlName);
    }

    public static bool IsMeFrpInstalled()
    {
        return File.Exists(GetMeFrpExePath());
    }

    public async Task<string?> GetSavedTokenAsync()
    {
        var settings = await _db.GetSettingsAsync();
        return string.IsNullOrWhiteSpace(settings.MeFrpToken) ? null : settings.MeFrpToken;
    }

    public async Task SaveTokenAsync(string token)
    {
        var settings = await _db.GetSettingsAsync();
        settings.MeFrpToken = token;
        await _db.SaveSettingsAsync(settings);
    }

    public async Task ClearTokenAsync()
    {
        var settings = await _db.GetSettingsAsync();
        settings.MeFrpToken = null;
        await _db.SaveSettingsAsync(settings);
    }

    public async Task<(bool Success, string Message, MeFrpUserInfo? User)> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "请输入访问密钥 Token", null);
        }

        try
        {
            var client = CreateClient(token);
            using var response = await client.GetAsync("api/auth/user/info");

            if ((int)response.StatusCode == 403)
            {
                return (false, "Token 无效或已过期", null);
            }

            var result = await response.Content.ReadFromJsonAsync<MeFrpApiResponse<MeFrpUserInfo>>(JsonOptions);
            if (result?.IsSuccess == true && result.Data != null)
            {
                return (true, result.Message, result.Data);
            }

            return (false, result?.Message ?? $"请求失败：{response.StatusCode}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证 MeFrp Token 失败");
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Success, string Message, MeFrpUserInfo? User)> GetAndValidateSavedTokenAsync()
    {
        var token = await GetSavedTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "未登录", null);
        }

        var result = await ValidateTokenAsync(token);
        if (!result.Success)
        {
            await ClearTokenAsync();
        }

        return result;
    }

    public async Task<List<MeFrpProxy>> GetProxiesAsync(string token)
    {
        var client = CreateClient(token);
        var result = await client.GetFromJsonAsync<MeFrpApiResponse<MeFrpProxyListData>>("api/auth/proxy/list", JsonOptions);
        return result?.Data?.Proxies ?? new List<MeFrpProxy>();
    }

    public async Task<List<MeFrpNode>> GetProxyNodesAsync(string token)
    {
        var client = CreateClient(token);
        var result = await client.GetFromJsonAsync<MeFrpApiResponse<MeFrpProxyListData>>("api/auth/proxy/list", JsonOptions);
        return result?.Data?.Nodes ?? new List<MeFrpNode>();
    }

    public async Task<MeFrpCreateProxyData?> GetCreateProxyDataAsync(string token)
    {
        var client = CreateClient(token);
        var result = await client.GetFromJsonAsync<MeFrpApiResponse<MeFrpCreateProxyData>>("api/auth/createProxyData", JsonOptions);
        return result?.Data;
    }

    public async Task<int?> GetFreePortAsync(string token, int nodeId, string protocol)
    {
        var client = CreateClient(token);
        var response = await client.PostAsJsonAsync("api/auth/node/freePort", new
        {
            nodeId,
            protocol
        });
        var result = await response.Content.ReadFromJsonAsync<MeFrpApiResponse<int>>(JsonOptions);
        return result?.IsSuccess == true ? result.Data : null;
    }

    public async Task<(bool Success, string Message)> CreateProxyAsync(string token, object request)
    {
        var client = CreateClient(token);
        var response = await client.PostAsJsonAsync("api/auth/proxy/create", request);
        var result = await response.Content.ReadFromJsonAsync<MeFrpApiResponse<object>>(JsonOptions);

        if ((int)response.StatusCode == 403)
        {
            return (false, "Token 无效或已过期");
        }

        return (result?.IsSuccess == true, result?.Message ?? response.ReasonPhrase ?? "创建失败");
    }

    public async Task<(bool Success, string Message, string? Config)> GetProxyConfigAsync(string token, int proxyId)
    {
        var client = CreateClient(token);
        var response = await client.PostAsJsonAsync("api/auth/proxy/config", new
        {
            proxyId,
            format = "toml"
        });
        var result = await response.Content.ReadFromJsonAsync<MeFrpApiResponse<MeFrpConfigData>>(JsonOptions);
        return (result?.IsSuccess == true, result?.Message ?? "获取配置失败", result?.Data?.Config);
    }

    public async Task<(bool Success, string Message)> StartTunnelAsync(int proxyId, string proxyName, string config)
    {
        if (!IsMeFrpInstalled())
        {
            return (false, "请先安装 MeFrp 客户端");
        }

        if (string.IsNullOrWhiteSpace(config))
        {
            return (false, "配置内容为空");
        }

        try
        {
            await SaveConfigAsync(config);

            if (_meFrpProcess is { HasExited: false })
            {
                try
                {
                    _meFrpProcess.Kill(true);
                    await _meFrpProcess.WaitForExitAsync();
                }
                catch
                {
                }
            }

            CurrentRunningProxyId = proxyId;
            CurrentRunningProxyName = proxyName;
            _proxyLogs[proxyId] = new List<string>();
            TunnelStateChanged?.Invoke(this, new MeFrpTunnelStateChangedEventArgs
            {
                ProxyId = proxyId,
                ProxyName = proxyName,
                IsRunning = true
            });

            AddTunnelLog(proxyId, $"开始启动隧道：{proxyName}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetMeFrpExePath(),
                    Arguments = $"-c \"{GetMeFrpConfigPath()}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data) && CurrentRunningProxyId.HasValue)
                {
                    AddTunnelLog(CurrentRunningProxyId.Value, e.Data);
                    _logger.LogInformation("[MeFrp] {Message}", e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data) && CurrentRunningProxyId.HasValue)
                {
                    AddTunnelLog(CurrentRunningProxyId.Value, $"[ERROR] {e.Data}");
                    _logger.LogWarning("[MeFrp] {Message}", e.Data);
                }
            };

            process.Exited += (_, _) =>
            {
                var exitProxyId = CurrentRunningProxyId;
                var exitProxyName = CurrentRunningProxyName;

                if (exitProxyId.HasValue)
                {
                    AddTunnelLog(exitProxyId.Value, $"隧道已退出，ExitCode: {process.ExitCode}");
                }

                CurrentRunningProxyId = null;
                CurrentRunningProxyName = string.Empty;
                _meFrpProcess = null;
                TunnelStateChanged?.Invoke(this, new MeFrpTunnelStateChangedEventArgs
                {
                    ProxyId = exitProxyId,
                    ProxyName = exitProxyName,
                    IsRunning = false
                });
                _logger.LogInformation("MeFrp 进程已退出，ExitCode: {ExitCode}", process.ExitCode);
            };

            process.Start();
            ChildProcessManager.Instance.AddProcess(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _meFrpProcess = process;

            AddTunnelLog(proxyId, $"隧道已启动，配置已保存到 {GetMeFrpConfigPath()}");
            return (true, $"隧道已启动：{proxyName}");
        }
        catch (Exception ex)
        {
            AddTunnelLog(proxyId, $"启动失败：{ex.Message}");
            CurrentRunningProxyId = null;
            CurrentRunningProxyName = string.Empty;
            _meFrpProcess = null;
            TunnelStateChanged?.Invoke(this, new MeFrpTunnelStateChangedEventArgs
            {
                ProxyId = proxyId,
                ProxyName = proxyName,
                IsRunning = false
            });
            _logger.LogError(ex, "启动 MeFrp 隧道失败");
            return (false, $"启动失败：{ex.Message}");
        }
    }

    public async Task StopTunnelAsync()
    {
        var proxyId = CurrentRunningProxyId;
        var proxyName = CurrentRunningProxyName;

        try
        {
            if (_meFrpProcess is { HasExited: false })
            {
                _meFrpProcess.Kill(true);
                await _meFrpProcess.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "停止 MeFrp 隧道时出现异常");
            if (proxyId.HasValue)
            {
                AddTunnelLog(proxyId.Value, $"停止隧道时出现异常：{ex.Message}");
            }
        }
        finally
        {
            _meFrpProcess = null;
            CurrentRunningProxyId = null;
            CurrentRunningProxyName = string.Empty;
            TunnelStateChanged?.Invoke(this, new MeFrpTunnelStateChangedEventArgs
            {
                ProxyId = proxyId,
                ProxyName = proxyName,
                IsRunning = false
            });
        }
    }

    public List<string> GetLogsForProxy(int proxyId)
    {
        return _proxyLogs.TryGetValue(proxyId, out var logs) ? new List<string>(logs) : new List<string>();
    }

    public void ClearLogsForProxy(int proxyId)
    {
        _proxyLogs[proxyId] = new List<string>();
    }

    private void AddTunnelLog(int proxyId, string message)
    {
        var normalizedMessage = NormalizeLogMessage(message);
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return;
        }

        var logLine = $"[{DateTime.Now:HH:mm:ss}] {normalizedMessage}";
        if (!_proxyLogs.TryGetValue(proxyId, out var logs))
        {
            logs = new List<string>();
            _proxyLogs[proxyId] = logs;
        }

        logs.Add(logLine);
        if (logs.Count > 200)
        {
            logs.RemoveAt(0);
        }

        TunnelLogReceived?.Invoke(this, new MeFrpTunnelLogEventArgs
        {
            ProxyId = proxyId,
            Message = logLine
        });
    }

    private static string NormalizeLogMessage(string message)
    {
        var cleaned = AnsiEscapeRegex.Replace(message, string.Empty)
            .Replace("\u001b", string.Empty)
            .Replace("\0", string.Empty)
            .Trim();

        return cleaned;
    }

    public async Task SaveConfigAsync(string config)
    {
        var configPath = GetMeFrpConfigPath();
        var directory = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(configPath, config);
    }

    public async Task<bool> EnsureClientInstalledAsync()
    {
        if (IsMeFrpInstalled())
        {
            return true;
        }

        return await DownloadAndInstallAsync();
    }

    public async Task<bool> DownloadAndInstallAsync()
    {
        if (IsDownloading)
        {
            return false;
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"mefrp_{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), $"mefrp_extract_{Guid.NewGuid():N}");

        try
        {
            IsDownloading = true;
            StateChanged?.Invoke(this, new FrpcDownloadStateChangedEventArgs
            {
                IsDownloading = true,
                Message = "正在下载 MeFrp 客户端..."
            });

            await DownloadFileAsync(DownloadUrl, tempZip);

            StateChanged?.Invoke(this, new FrpcDownloadStateChangedEventArgs
            {
                IsDownloading = true,
                Message = "正在解压客户端..."
            });

            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(tempZip, extractDir);

            var exePath = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(path).Contains("mefrpc", StringComparison.OrdinalIgnoreCase))
                ?? Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();

            if (exePath == null)
            {
                throw new Exception("压缩包中未找到 MeFrp 客户端可执行文件");
            }

            var targetPath = GetMeFrpExePath();
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(exePath, targetPath, true);

            StateChanged?.Invoke(this, new FrpcDownloadStateChangedEventArgs
            {
                IsDownloading = false,
                IsComplete = true,
                Message = "MeFrp 客户端已安装完成"
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载并安装 MeFrp 失败");
            StateChanged?.Invoke(this, new FrpcDownloadStateChangedEventArgs
            {
                IsDownloading = false,
                IsComplete = false,
                Message = $"MeFrp 下载失败：{ex.Message}"
            });
            return false;
        }
        finally
        {
            IsDownloading = false;
            TryDelete(tempZip);
            TryDeleteDirectory(extractDir);
        }
    }

    private async Task DownloadFileAsync(string url, string targetPath)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZMSL", "3.1.0"));

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long read = 0;
        int chunk;

        while ((chunk = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, chunk));
            read += chunk;

            if (totalBytes > 0)
            {
                ProgressChanged?.Invoke(this, new FrpcDownloadProgressEventArgs
                {
                    Progress = read * 100d / totalBytes,
                    DownloadedBytes = read,
                    TotalBytes = totalBytes
                });
            }
        }
    }

    private HttpClient CreateClient(string token)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.mefrp.com/");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZMSL", "3.1.0"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
}
