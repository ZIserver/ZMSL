using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZMSL.App.Models;

using System.Text.Json.Serialization;

namespace ZMSL.App.Services;

// 下载任务状态
public class DownloadTaskStatus
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("downloaded")]
    public long Downloaded { get; set; }
    
    [JsonPropertyName("total")]
    public long Total { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

// 备份信息
public class BackupInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("size")]
    public long Size { get; set; }
    
    [JsonPropertyName("time")]
    public DateTime? Time { get; set; }
}

public class LinuxNodeService
{
    private readonly DatabaseService _db;
    private readonly HttpClient _httpClient;

    public LinuxNodeService(DatabaseService db)
    {
        _db = db;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) }; // 5 分钟超时，适用于 Java 下载安装
    }

    // 节点管理
    public async Task<List<LinuxNode>> GetNodesAsync()
    {
        return await _db.ExecuteWithLockAsync(async db => 
            await db.LinuxNodes.ToListAsync());
    }

    public async Task<LinuxNode?> GetNodeAsync(int id)
    {
        return await _db.ExecuteWithLockAsync(async db => 
            await db.LinuxNodes.FindAsync(id));
    }

    public async Task<LinuxNode> AddNodeAsync(LinuxNode node)
    {
        await _db.ExecuteWithLockAsync(async db =>
        {
            db.LinuxNodes.Add(node);
            await db.SaveChangesAsync();
        });
        
        // 测试连接
        await TestConnectionAsync(node);
        
        return node;
    }

    public async Task UpdateNodeAsync(LinuxNode node)
    {
        await _db.ExecuteWithLockAsync(async db =>
        {
            db.LinuxNodes.Update(node);
            await db.SaveChangesAsync();
        });
    }

    public async Task DeleteNodeAsync(int id)
    {
        await _db.ExecuteWithLockAsync(async db =>
        {
            var node = await db.LinuxNodes.FindAsync(id);
            if (node != null)
            {
                // 删除节点上的所有远程服务器记录
                var remoteServers = await db.RemoteServers.Where(r => r.NodeId == id).ToListAsync();
                db.RemoteServers.RemoveRange(remoteServers);
                
                db.LinuxNodes.Remove(node);
                await db.SaveChangesAsync();
            }
        });
    }

    // 连接测试
    public async Task<(bool Success, string Message)> TestConnectionAsync(LinuxNode node)
    {
        try
        {
            var url = $"http://{node.Host}:{node.Port}/ping";
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                node.IsOnline = true;
                node.LastConnectedAt = DateTime.Now;
                await UpdateNodeAsync(node);
                return (true, "连接成功");
            }
            else
            {
                node.IsOnline = false;
                await UpdateNodeAsync(node);
                return (false, $"连接失败: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            node.IsOnline = false;
            await UpdateNodeAsync(node);
            return (false, $"连接失败: {ex.Message}");
        }
    }

    // API请求辅助方法
    private HttpRequestMessage CreateRequest(LinuxNode node, HttpMethod method, string endpoint)
    {
        var url = $"http://{node.Host}:{node.Port}{endpoint}";
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Authorization", $"Bearer {node.Token}");
        return request;
    }

    // 系统信息
    public async Task<NodeSystemInfo?> GetSystemInfoAsync(LinuxNode node)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, "/api/system/info");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<NodeSystemInfo>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<NodeSystemResources?> GetSystemResourcesAsync(LinuxNode node)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, "/api/system/resources");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<NodeSystemResources>();
        }
        catch
        {
            return null;
        }
    }

    // Java管理
    public async Task<List<NodeJavaInfo>?> ListJavaAsync(LinuxNode node)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, "/api/java/list");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<NodeJavaInfo>>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool Success, string Message)> InstallJavaAsync(LinuxNode node, string version)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, "/api/java/install");
            var json = JsonSerializer.Serialize(new { version });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"]?.ToString() ?? "安装成功");
            }
            else
            {
                return (false, result?["error"]?.ToString() ?? "安装失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"安装失败: {ex.Message}");
        }
    }

    // 通过下载安装 Java（使用 MSL API）
    public async Task<(bool Success, string Message)> InstallJavaFromDownloadAsync(LinuxNode node, string version)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, "/api/java/install-download");
            var json = JsonSerializer.Serialize(new { version });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            
            if (response.IsSuccessStatusCode)
            {
                var message = result?["message"]?.ToString() ?? "安装成功";
                var path = result?["path"]?.ToString();
                if (!string.IsNullOrEmpty(path))
                {
                    message += $"\nJava 路径: {path}";
                }
                return (true, message);
            }
            else
            {
                return (false, result?["error"]?.ToString() ?? "安装失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"安装失败: {ex.Message}");
        }
    }

    // 服务器管理
    public async Task<List<RemoteServerInfo>?> ListServersAsync(LinuxNode node)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, "/api/servers");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<RemoteServerInfo>>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 同步远程服务器信息：从后端API获取最新数据并更新本地数据库（批量写库优化）
    /// </summary>
    public async Task<(int Updated, int Failed)> SyncRemoteServersAsync(LinuxNode node)
    {
        int failedCount = 0;
        List<RemoteServer>? localServers = null;

        try
        {
            // 1. 从后端API获取该节点的所有服务器
            var remoteServers = await ListServersAsync(node);
            if (remoteServers == null || remoteServers.Count == 0)
            {
                return (0, 0);
            }

            // 2. 获取本地数据库中该节点的所有远程服务器
            localServers = await _db.ExecuteWithLockAsync(async db =>
                await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(db.RemoteServers.Where(s => s.NodeId == node.Id)));

            // 3. 在内存中批量计算需要更新的服务器（无 DB 锁）
            var toUpdate = new List<RemoteServer>();
            foreach (var localServer in localServers)
            {
                try
                {
                    var remoteInfo = remoteServers.FirstOrDefault(r => r.Id == localServer.RemoteServerId);

                    if (remoteInfo != null)
                    {
                        bool needsUpdate = false;

                        if (string.IsNullOrEmpty(localServer.Name) || localServer.Name != remoteInfo.Name)
                        {
                            localServer.Name = remoteInfo.Name;
                            needsUpdate = true;
                        }

                        if (string.IsNullOrEmpty(localServer.CoreType) || localServer.CoreType != remoteInfo.CoreType)
                        {
                            localServer.CoreType = remoteInfo.CoreType;
                            needsUpdate = true;
                        }

                        if (string.IsNullOrEmpty(localServer.MinecraftVersion) || localServer.MinecraftVersion != remoteInfo.MinecraftVersion)
                        {
                            localServer.MinecraftVersion = remoteInfo.MinecraftVersion;
                            needsUpdate = true;
                        }

                        if (localServer.Port != remoteInfo.Port && remoteInfo.Port > 0)
                        {
                            localServer.Port = remoteInfo.Port;
                            needsUpdate = true;
                        }

                        if (needsUpdate)
                            toUpdate.Add(localServer);
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch
                {
                    failedCount++;
                }
            }

            // 4. 一次性批量写库，减少锁竞争
            if (toUpdate.Count > 0)
            {
                await _db.ExecuteWithLockAsync(async db =>
                {
                    foreach (var localServer in toUpdate)
                    {
                        db.RemoteServers.Update(localServer);
                    }
                    await db.SaveChangesAsync();
                });
            }

            return (toUpdate.Count, failedCount);
        }
        catch
        {
            return (0, localServers?.Count ?? 0);
        }
    }

    public async Task<(bool Success, string Message, string? ServerId)> CreateServerAsync(LinuxNode node, CreateServerRequest serverData)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, "/api/servers");
            request.Content = JsonContent.Create(serverData);
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                // 解析响应: {"server": {...}, "id": "server_xxx"}
                var responseObj = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
                
                if (responseObj != null && responseObj.ContainsKey("id"))
                {
                    var serverId = responseObj["id"].ToString();
                    
                    // 从 server 对象中提取信息
                    RemoteServerInfo? serverInfo = null;
                    if (responseObj.ContainsKey("server"))
                    {
                        var serverJson = JsonSerializer.Serialize(responseObj["server"]);
                        System.Diagnostics.Debug.WriteLine($"Server JSON: {serverJson}");

                        serverInfo = JsonSerializer.Deserialize<RemoteServerInfo>(serverJson);

                        System.Diagnostics.Debug.WriteLine($"Deserialized Name: {serverInfo?.Name}");
                        System.Diagnostics.Debug.WriteLine($"Deserialized CoreType: {serverInfo?.CoreType}");
                    }
                    
                    // 如果无法从 server 对象获取信息，则使用请求数据
                    var remoteServer = new RemoteServer
                    {
                        NodeId = node.Id,
                        RemoteServerId = serverId ?? "",
                        Name = serverInfo?.Name ?? serverData.Name,
                        CoreType = serverInfo?.CoreType ?? serverData.CoreType,
                        MinecraftVersion = serverInfo?.MinecraftVersion ?? serverData.MinecraftVersion,
                        JarFileName = serverInfo?.JarFileName ?? serverData.JarFileName,
                        MinMemoryMB = serverInfo?.MinMemoryMB > 0 ? serverInfo.MinMemoryMB : serverData.MinMemoryMB,
                        MaxMemoryMB = serverInfo?.MaxMemoryMB > 0 ? serverInfo.MaxMemoryMB : serverData.MaxMemoryMB,
                        Port = serverInfo?.Port > 0 ? serverInfo.Port : serverData.Port,
                        AutoRestart = serverData.AutoRestart,
                        CreatedAt = DateTime.Now
                    };
                    
                    await _db.ExecuteWithLockAsync(async db =>
                    {
                        db.RemoteServers.Add(remoteServer);
                        await db.SaveChangesAsync();
                    });
                    
                    return (true, "服务器创建成功", serverId);
                }
                else
                {
                    return (false, "服务器创建失败：无效响应格式", null);
                }
            }
            else
            {
                var error = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                return (false, error?["error"] ?? "创建失败", null);
            }
        }
        catch (Exception ex)
        {
            return (false, $"创建失败: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Message)> StartServerAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, $"/api/servers/{serverId}/start");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            
            if (response.IsSuccessStatusCode)
            {
                // 更新本地状态
                await _db.ExecuteWithLockAsync(async db =>
                {
                    var remoteServer = await db.RemoteServers.FirstOrDefaultAsync(r => r.RemoteServerId == serverId);
                    if (remoteServer != null)
                    {
                        remoteServer.IsRunning = true;
                        await db.SaveChangesAsync();
                    }
                });
                
                return (true, result?["message"]?.ToString() ?? "启动成功");
            }
            else
            {
                return (false, result?["error"]?.ToString() ?? "启动失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"启动失败: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> StopServerAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, $"/api/servers/{serverId}/stop");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            
            if (response.IsSuccessStatusCode)
            {
                // 更新本地状态
                await _db.ExecuteWithLockAsync(async db =>
                {
                    var remoteServer = await db.RemoteServers.FirstOrDefaultAsync(r => r.RemoteServerId == serverId);
                    if (remoteServer != null)
                    {
                        remoteServer.IsRunning = false;
                        await db.SaveChangesAsync();
                    }
                });
                
                return (true, result?["message"]?.ToString() ?? "停止成功");
            }
            else
            {
                return (false, result?["error"]?.ToString() ?? "停止失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"停止失败: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> SendCommandAsync(LinuxNode node, string serverId, string command)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, $"/api/servers/{serverId}/command");
            var json = JsonSerializer.Serialize(new { command });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"]?.ToString() ?? "命令发送成功");
            }
            else
            {
                return (false, result?["error"]?.ToString() ?? "命令发送失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"命令发送失败: {ex.Message}");
        }
    }

    public async Task<ServerStatus?> GetServerStatusAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, $"/api/servers/{serverId}/status");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ServerStatus>();
        }
        catch
        {
            return null;
        }
    }

    // 文件上传
    public async Task<(bool Success, string Message)> UploadFileAsync(LinuxNode node, string serverId, string filePath)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(serverId), "server_id");
            
            var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", Path.GetFileName(filePath));
            
            var request = CreateRequest(node, HttpMethod.Post, "/api/files/upload");
            request.Content = content;
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"]?.ToString() ?? "上传成功");
            }
            else
            {
                return (false, result?["error"]?.ToString() ?? "上传失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"上传失败: {ex.Message}");
        }
    }

    // 获取本地保存的远程服务器列表
    public async Task<List<RemoteServer>> GetLocalRemoteServersAsync(int nodeId)
    {
        return await _db.ExecuteWithLockAsync(async db => 
            await db.RemoteServers.Where(r => r.NodeId == nodeId).ToListAsync());
    }

    // 重启服务器
    public async Task<(bool Success, string Message)> RestartServerAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, $"/api/servers/{serverId}/restart");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "重启成功");
            }
            else
            {
                return (false, result?["error"] ?? "重启失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"重启失败: {ex.Message}");
        }
    }

    // 获取服务器配置文件
    public async Task<string?> GetServerPropertiesAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, $"/api/servers/{serverId}/properties");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            return result?["content"];
        }
        catch
        {
            return null;
        }
    }

    // 更新服务器配置文件
    public async Task<(bool Success, string Message)> UpdateServerPropertiesAsync(LinuxNode node, string serverId, string content)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Put, $"/api/servers/{serverId}/properties");
            var json = JsonSerializer.Serialize(new { content });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "更新成功");
            }
            else
            {
                return (false, result?["error"] ?? "更新失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"更新失败: {ex.Message}");
        }
    }

    // 更新服务器配置（名称、内存、Java等）
    public async Task<(bool Success, string Message)> UpdateServerConfigAsync(LinuxNode node, RemoteServer server)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Put, $"/api/servers/{server.RemoteServerId}");
            var data = new
            {
                name = server.Name,
                core_type = server.CoreType,
                minecraft_version = server.MinecraftVersion,
                jar_file_name = server.JarFileName,
                java_path = server.JavaPath,
                min_memory_mb = server.MinMemoryMB,
                max_memory_mb = server.MaxMemoryMB,
                jvm_args = server.JvmArgs,
                port = server.Port,
                auto_restart = server.AutoRestart
            };
            
            var json = JsonSerializer.Serialize(data);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                // 更新本地数据库
                await _db.ExecuteWithLockAsync(async db =>
                {
                    db.RemoteServers.Update(server);
                    await db.SaveChangesAsync();
                });
                
                // 解析响应中的 message (如果有)
                try {
                    var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(); // Go returns the server object, not message wrapper usually, but let's check
                    // Go code: c.JSON(http.StatusOK, server) -> returns the server object directly.
                    return (true, "更新成功");
                } catch {
                    return (true, "更新成功");
                }
            }
            else
            {
                try {
                    var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                    return (false, result?["error"] ?? "更新失败");
                } catch {
                    return (false, "更新失败");
                }
            }
        }
        catch (Exception ex)
        {
            return (false, $"更新失败: {ex.Message}");
        }
    }

    // 删除远程服务器
    public async Task<(bool Success, string Message)> DeleteRemoteServerAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Delete, $"/api/servers/{serverId}");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
            {
                // 从本地数据库删除
                await _db.ExecuteWithLockAsync(async db =>
                {
                    var server = await db.RemoteServers.FirstOrDefaultAsync(s => s.RemoteServerId == serverId && s.NodeId == node.Id);
                    if (server != null)
                    {
                        db.RemoteServers.Remove(server);
                        await db.SaveChangesAsync();
                    }
                });
                
                return (true, result?["message"] ?? "删除成功");
            }
            else
            {
                return (false, result?["error"] ?? "删除失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"删除失败: {ex.Message}");
        }
    }

    // 列出服务器插件
    public async Task<List<string>?> ListPluginsAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, $"/api/servers/{serverId}/plugins");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<string>>();
        }
        catch
        {
            return null;
        }
    }

    // 安装插件
    public async Task<(bool Success, string Message, string? TaskId)> InstallPluginAsync(LinuxNode node, string serverId, string url, string fileName)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, $"/api/servers/{serverId}/plugins");
            var json = JsonSerializer.Serialize(new { url, file_name = fileName });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "安装开始", result?["task_id"]);
            }
            else
            {
                return (false, result?["error"] ?? "安装失败", null);
            }
        }
        catch (Exception ex)
        {
            return (false, $"安装失败: {ex.Message}", null);
        }
    }

    // 删除插件
    public async Task<(bool Success, string Message)> DeletePluginAsync(LinuxNode node, string serverId, string pluginName)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Delete, $"/api/servers/{serverId}/plugins/{pluginName}");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "删除成功");
            }
            else
            {
                return (false, result?["error"] ?? "删除失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"删除失败: {ex.Message}");
        }
    }

    // 发起下载任务
    public async Task<(bool Success, string Message, string? TaskId)> StartDownloadAsync(LinuxNode node, string url, string path, string taskId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, "/api/downloads");
            var json = JsonSerializer.Serialize(new { url, path, id = taskId });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "下载开始", result?["task_id"]);
            }
            else
            {
                return (false, result?["error"] ?? "下载失败", null);
            }
        }
        catch (Exception ex)
        {
            return (false, $"下载失败: {ex.Message}", null);
        }
    }

    // 查询下载进度
    public async Task<DownloadTaskStatus?> GetDownloadStatusAsync(LinuxNode node, string taskId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, $"/api/downloads/status?id={taskId}");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<DownloadTaskStatus>();
        }
        catch
        {
            return null;
        }
    }

    // 创建备份
    public async Task<(bool Success, string Message)> CreateBackupAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, $"/api/servers/{serverId}/backup");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "备份开始");
            }
            else
            {
                return (false, result?["error"] ?? "备份失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"备份失败: {ex.Message}");
        }
    }

    // 列出备份
    public async Task<List<BackupInfo>?> ListBackupsAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, $"/api/servers/{serverId}/backups");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<BackupInfo>>();
        }
        catch
        {
            return null;
        }
    }

    // 列出文件
    public async Task<List<RemoteFileInfo>?> ListFilesAsync(LinuxNode node, string serverId, string path = "")
    {
        try
        {
            var queryString = $"?server_id={serverId}";
            if (!string.IsNullOrEmpty(path))
            {
                queryString += $"&path={Uri.EscapeDataString(path)}";
            }
            
            var request = CreateRequest(node, HttpMethod.Get, $"/api/files/list{queryString}");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<RemoteFileInfo>>();
        }
        catch
        {
            return null;
        }
    }

    // 读取文件内容
    public async Task<string?> ReadFileAsync(LinuxNode node, string serverId, string filePath)
    {
        try
        {
            var queryString = $"?server_id={serverId}&path={Uri.EscapeDataString(filePath)}";
            var request = CreateRequest(node, HttpMethod.Get, $"/api/files/read{queryString}");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            return result?["content"];
        }
        catch
        {
            return null;
        }
    }

    // 写入文件内容
    public async Task<bool> WriteFileAsync(LinuxNode node, string serverId, string filePath, string content)
    {
        try
        {
            var data = new
            {
                server_id = serverId,
                path = filePath,
                content = content
            };

            var request = CreateRequest(node, HttpMethod.Post, "/api/files/write");
            request.Content = JsonContent.Create(data);
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // 创建目录
    public async Task<(bool Success, string Message)> MkdirAsync(LinuxNode node, string serverId, string path)
    {
        try
        {
            var data = new { server_id = serverId, path };
            var request = CreateRequest(node, HttpMethod.Post, "/api/files/mkdir");
            request.Content = JsonContent.Create(data);
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
                return (true, result?["message"] ?? "创建成功");
            else
                return (false, result?["error"] ?? "创建失败");
        }
        catch (Exception ex)
        {
            return (false, $"操作失败: {ex.Message}");
        }
    }

    // 重命名文件
    public async Task<(bool Success, string Message)> RenameFileAsync(LinuxNode node, string serverId, string oldPath, string newPath)
    {
        try
        {
            var data = new { server_id = serverId, old_path = oldPath, new_path = newPath };
            var request = CreateRequest(node, HttpMethod.Post, "/api/files/rename");
            request.Content = JsonContent.Create(data);
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
                return (true, result?["message"] ?? "重命名成功");
            else
                return (false, result?["error"] ?? "重命名失败");
        }
        catch (Exception ex)
        {
            return (false, $"操作失败: {ex.Message}");
        }
    }

    // 复制文件
    public async Task<(bool Success, string Message)> CopyFileAsync(LinuxNode node, string serverId, string srcPath, string dstPath)
    {
        try
        {
            var data = new { server_id = serverId, src_path = srcPath, dst_path = dstPath };
            var request = CreateRequest(node, HttpMethod.Post, "/api/files/copy");
            request.Content = JsonContent.Create(data);
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
                return (true, result?["message"] ?? "复制成功");
            else
                return (false, result?["error"] ?? "复制失败");
        }
        catch (Exception ex)
        {
            return (false, $"操作失败: {ex.Message}");
        }
    }

    // 删除文件
    public async Task<(bool Success, string Message)> DeleteFileAsync(LinuxNode node, string serverId, string path)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Delete, $"/api/files/delete?server_id={serverId}&path={Uri.EscapeDataString(path)}");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
                return (true, result?["message"] ?? "删除成功");
            else
                return (false, result?["error"] ?? "删除失败");
        }
        catch (Exception ex)
        {
            return (false, $"操作失败: {ex.Message}");
        }
    }

    // 压缩文件
    public async Task<(bool Success, string Message)> CompressFileAsync(LinuxNode node, string serverId, List<string> paths, string dstPath)
    {
        try
        {
            var data = new { server_id = serverId, paths, dst_path = dstPath };
            var request = CreateRequest(node, HttpMethod.Post, "/api/files/compress");
            request.Content = JsonContent.Create(data);
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
                return (true, result?["message"] ?? "压缩成功");
            else
                return (false, result?["error"] ?? "压缩失败");
        }
        catch (Exception ex)
        {
            return (false, $"操作失败: {ex.Message}");
        }
    }

    // 解压文件
    public async Task<(bool Success, string Message)> DecompressFileAsync(LinuxNode node, string serverId, string zipPath, string dstPath)
    {
        try
        {
            var data = new { server_id = serverId, zip_path = zipPath, dst_path = dstPath };
            var request = CreateRequest(node, HttpMethod.Post, "/api/files/decompress");
            request.Content = JsonContent.Create(data);
            
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            
            if (response.IsSuccessStatusCode)
                return (true, result?["message"] ?? "解压成功");
            else
                return (false, result?["error"] ?? "解压失败");
        }
        catch (Exception ex)
        {
            return (false, $"操作失败: {ex.Message}");
        }
    }

    #region FRP 管理

    // 获取 FRP 状态
    public async Task<FrpStatus?> GetFrpStatusAsync(LinuxNode node)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, "/api/frp/status");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<FrpStatus>();
        }
        catch
        {
            return null;
        }
    }

    // 列出所有 FRP 隧道
    public async Task<List<FrpTunnelInfo>?> ListFrpTunnelsAsync(LinuxNode node)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, "/api/frp/tunnels");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<FrpTunnelInfo>>();
        }
        catch
        {
            return null;
        }
    }

    // 启动 FRP 隧道
    public async Task<(bool Success, string Message, int? Pid)> StartFrpTunnelAsync(LinuxNode node, StartFrpTunnelRequest tunnelRequest)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, "/api/frp/tunnels/start");
            request.Content = JsonContent.Create(tunnelRequest);

            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

            if (response.IsSuccessStatusCode)
            {
                int? pid = null;
                if (result?.ContainsKey("pid") == true)
                {
                    if (result["pid"] is JsonElement pidElement && pidElement.TryGetInt32(out int pidValue))
                    {
                        pid = pidValue;
                    }
                }
                return (true, result?["message"]?.ToString() ?? "隧道启动成功", pid);
            }
            else
            {
                return (false, result?["error"]?.ToString() ?? "隧道启动失败", null);
            }
        }
        catch (Exception ex)
        {
            return (false, $"隧道启动失败: {ex.Message}", null);
        }
    }

    // 停止 FRP 隧道
    public async Task<(bool Success, string Message)> StopFrpTunnelAsync(LinuxNode node, string tunnelId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, $"/api/frp/tunnels/{tunnelId}/stop");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "隧道停止成功");
            }
            else
            {
                return (false, result?["error"] ?? "隧道停止失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"隧道停止失败: {ex.Message}");
        }
    }

    // 获取 FRP 隧道日志
    public async Task<List<string>?> GetFrpTunnelLogsAsync(LinuxNode node, string tunnelId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, $"/api/frp/tunnels/{tunnelId}/logs");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, List<string>>>();
            return result?["logs"];
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region 增强版插件/Mod 管理

    // 列出服务器的所有插件和 Mod（增强版）
    public async Task<ServerPluginsResult?> ListServerPluginsEnhancedAsync(LinuxNode node, string serverId)
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Get, $"/api/plugins/{serverId}");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ServerPluginsResult>();
        }
        catch
        {
            return null;
        }
    }

    // 上传插件/Mod
    public async Task<(bool Success, string Message)> UploadPluginAsync(LinuxNode node, string serverId, string filePath, string type = "plugin")
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(type), "type");

            var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", Path.GetFileName(filePath));

            var request = CreateRequest(node, HttpMethod.Post, $"/api/plugins/{serverId}/upload");
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "上传成功");
            }
            else
            {
                return (false, result?["error"] ?? "上传失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"上传失败: {ex.Message}");
        }
    }

    // 删除插件/Mod（增强版）
    public async Task<(bool Success, string Message)> DeleteServerPluginAsync(LinuxNode node, string serverId, string filename, string type = "plugin")
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Delete, $"/api/plugins/{serverId}/{filename}?type={type}");
            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "删除成功");
            }
            else
            {
                return (false, result?["error"] ?? "删除失败");
            }
        }
        catch (Exception ex)
        {
            return (false, $"删除失败: {ex.Message}");
        }
    }

    // 从 URL 下载插件/Mod
    public async Task<(bool Success, string Message, string? TaskId)> DownloadPluginFromURLAsync(LinuxNode node, string serverId, string url, string fileName, string type = "plugin")
    {
        try
        {
            var request = CreateRequest(node, HttpMethod.Post, $"/api/plugins/{serverId}/download");
            var json = JsonSerializer.Serialize(new { url, file_name = fileName, type });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

            if (response.IsSuccessStatusCode)
            {
                return (true, result?["message"] ?? "下载开始", result?["task_id"]);
            }
            else
            {
                return (false, result?["error"] ?? "下载失败", null);
            }
        }
        catch (Exception ex)
        {
            return (false, $"下载失败: {ex.Message}", null);
        }
    }

    #endregion
}

// DTO类
public class NodeSystemInfo
{
    [JsonPropertyName("os")]
    public string Os { get; set; } = string.Empty;
    
    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = string.Empty;
    
    [JsonPropertyName("cpu_cores")]
    public int CpuCores { get; set; }
    
    [JsonPropertyName("total_memory")]
    public long TotalMemory { get; set; }
    
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;
}

public class NodeSystemResources
{
    [JsonPropertyName("cpu_percent")]
    public double CpuPercent { get; set; }
    
    [JsonPropertyName("memory_used")]
    public long MemoryUsed { get; set; }
    
    [JsonPropertyName("memory_total")]
    public long MemoryTotal { get; set; }
    
    [JsonPropertyName("memory_percent")]
    public double MemoryPercent { get; set; }
    
    [JsonPropertyName("disk_used")]
    public long DiskUsed { get; set; }
    
    [JsonPropertyName("disk_total")]
    public long DiskTotal { get; set; }
    
    [JsonPropertyName("disk_percent")]
    public double DiskPercent { get; set; }
}

public class NodeJavaInfo
{
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public class RemoteServerInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("core_type")]
    public string CoreType { get; set; } = string.Empty;

    [JsonPropertyName("minecraft_version")]
    public string MinecraftVersion { get; set; } = string.Empty;

    [JsonPropertyName("jar_file_name")]
    public string JarFileName { get; set; } = string.Empty;

    [JsonPropertyName("java_path")]
    public string JavaPath { get; set; } = string.Empty;

    [JsonPropertyName("min_memory_mb")]
    public int MinMemoryMB { get; set; }

    [JsonPropertyName("max_memory_mb")]
    public int MaxMemoryMB { get; set; }

    [JsonPropertyName("jvm_args")]
    public string JvmArgs { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("auto_restart")]
    public bool AutoRestart { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class CreateServerRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("core_type")]
    public string CoreType { get; set; } = string.Empty;
    
    [JsonPropertyName("minecraft_version")]
    public string MinecraftVersion { get; set; } = string.Empty;
    
    [JsonPropertyName("jar_file_name")]
    public string JarFileName { get; set; } = string.Empty;
    
    [JsonPropertyName("java_path")]
    public string JavaPath { get; set; } = "/usr/bin/java";
    
    [JsonPropertyName("min_memory_mb")]
    public int MinMemoryMB { get; set; } = 1024;
    
    [JsonPropertyName("max_memory_mb")]
    public int MaxMemoryMB { get; set; } = 2048;
    
    [JsonPropertyName("jvm_args")]
    public string JvmArgs { get; set; } = string.Empty;
    
    [JsonPropertyName("port")]
    public int Port { get; set; } = 25565;

    [JsonPropertyName("auto_restart")]
    public bool AutoRestart { get; set; }
    
    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }
}

public class ServerStatus
{
    public bool Running { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryUsed { get; set; }
    public float MemoryPercent { get; set; }
}

public class RemoteFileInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("is_directory")]
    public bool IsDirectory { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("mod_time")]
    public long ModTime { get; set; }
}

// FRP 相关 DTO
public class FrpStatus
{
    [JsonPropertyName("frpc_path")]
    public string FrpcPath { get; set; } = "";

    [JsonPropertyName("total_tunnels")]
    public int TotalTunnels { get; set; }

    [JsonPropertyName("running")]
    public int Running { get; set; }
}

public class FrpTunnelInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("server_id")]
    public string ServerId { get; set; } = "";

    [JsonPropertyName("local_port")]
    public int LocalPort { get; set; }

    [JsonPropertyName("remote_port")]
    public int RemotePort { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "tcp";

    [JsonPropertyName("is_running")]
    public bool IsRunning { get; set; }

    [JsonPropertyName("pid")]
    public int Pid { get; set; }
}

public class StartFrpTunnelRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("server_id")]
    public string ServerId { get; set; } = "";

    [JsonPropertyName("server_addr")]
    public string ServerAddr { get; set; } = "";

    [JsonPropertyName("server_port")]
    public int ServerPort { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("local_port")]
    public int LocalPort { get; set; }

    [JsonPropertyName("remote_port")]
    public int RemotePort { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "tcp";

    [JsonPropertyName("proxy_name")]
    public string? ProxyName { get; set; }
}

// 增强版插件信息
public class NodePluginInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("mod_time")]
    public string ModTime { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "plugin";
}

public class ServerPluginsResult
{
    [JsonPropertyName("plugins")]
    public List<NodePluginInfo> Plugins { get; set; } = new();

    [JsonPropertyName("mods")]
    public List<NodePluginInfo> Mods { get; set; } = new();
}
