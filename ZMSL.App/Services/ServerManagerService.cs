using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.Models;
using TinyPinyin;

namespace ZMSL.App.Services;

/// <summary>
/// 服务器启动结果
/// </summary>
public enum ServerStartResult
{
    Success,
    AlreadyRunning,
    NeedEulaAccept,  // 需要用户同意 EULA
    JavaNotFound,    // Java 未找到
    Failed
}

public class ServerManagerService
{
    private readonly DatabaseService _db;
    private readonly ForgeInstallerService _forgeInstaller;
    private readonly Dictionary<int, Process> _runningServers = new();
    private readonly Dictionary<int, List<string>> _serverLogs = new(); // 日志缓存
    private const int MaxLogLines = 2000; // 增加日志缓存以便崩溃分析

    public event EventHandler<ServerOutputEventArgs>? ServerOutput;
    public event EventHandler<ServerStatusEventArgs>? ServerStatusChanged;
    public event EventHandler<ServerCrashEventArgs>? ServerCrashed;
    public event EventHandler<ForgeInstallProgressEventArgs>? ForgeInstallProgress;

    public ServerManagerService(DatabaseService db, ForgeInstallerService forgeInstaller)
    {
        _db = db;
        _forgeInstaller = forgeInstaller ?? throw new ArgumentNullException(nameof(forgeInstaller));
        _forgeInstaller.ProgressChanged += (s, e) =>
        {
            ForgeInstallProgress?.Invoke(this, new ForgeInstallProgressEventArgs
            {
                Message = e.Message,
                Progress = e.Progress,
                Stage = e.Stage
            });
        };
    }

    // 崩溃关键词
    private static readonly string[] CrashKeywords = new[]
    {
        "FATAL",
        "crashed",
        "Crash Report",
        "Exception in server tick loop",
        "Error occurred during initialization",
        "A single server tick took",
        "OutOfMemoryError",
        "StackOverflowError",
        "NoClassDefFoundError",
        "UnsatisfiedLinkError",
        "This crash report has been saved to",
        "---- Minecraft Crash Report ----"
    };

    public async Task<List<LocalServer>> GetServersAsync()
    {
        return await _db.ExecuteWithLockAsync(async db => 
            await db.Servers.OrderByDescending(s => s.LastStartedAt ?? s.CreatedAt).ToListAsync());
    }

    public List<string> GetServerLogs(int serverId)
    {
        return _serverLogs.TryGetValue(serverId, out var logs) ? new List<string>(logs) : new List<string>();
    }

    private void AddLog(int serverId, string message)
    {
        if (!_serverLogs.ContainsKey(serverId))
            _serverLogs[serverId] = new List<string>();

        _serverLogs[serverId].Add(message);
        if (_serverLogs[serverId].Count > MaxLogLines)
            _serverLogs[serverId].RemoveAt(0);

        // 检测崩溃关键词
        CheckForCrash(serverId, message);
    }

    private readonly Dictionary<int, bool> _crashDetected = new();
    private readonly Dictionary<int, string> _serverStartupCommands = new(); // 存储启动命令
    private readonly Dictionary<int, bool> _userRequestedStop = new(); // 标记用户主动停止

    private void CheckForCrash(int serverId, string message)
    {
        // 如果用户主动停止，或者已经检测到崩溃，不再重复触发
        if ((_userRequestedStop.TryGetValue(serverId, out var userStopped) && userStopped) ||
            (_crashDetected.TryGetValue(serverId, out var detected) && detected))
            return;

        // NoClassDefFoundError 特殊处理：只有在特定上下文中才认为是崩溃
        if (message.Contains("NoClassDefFoundError", StringComparison.OrdinalIgnoreCase))
        {
            // 这些情况下的 NoClassDefFoundError 是正常的，不应该触发崩溃检测
            var normalPatterns = new[]
            {
                "Suppressed:",  // 被抑制的异常
                "Caused by:",   // 原因链中的异常
                "at ",          // 堆栈跟踪（通常是警告）
                "[WARN]",       // 警告级别
                "[DEBUG]",      // 调试级别
                "optional",     // 可选依赖
                "Optional",     // 可选依赖
                "trying to load", // 尝试加载（可能失败）
                "Failed to load class", // 加载失败但不致命
            };

            // 如果消息包含这些模式，说明是正常的类加载失败，不是崩溃
            foreach (var pattern in normalPatterns)
            {
                if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return; // 不触发崩溃检测
                }
            }

            // 只有在错误级别且不在上述正常模式中时，才认为是崩溃
            if (!message.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) &&
                !message.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
            {
                return; // 不是错误级别，不触发崩溃检测
            }
        }

        foreach (var keyword in CrashKeywords)
        {
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                _crashDetected[serverId] = true;

                // 获取完整日志
                var fullLog = string.Join("\n", GetServerLogs(serverId));

                // 获取启动命令
                var startupCommand = _serverStartupCommands.TryGetValue(serverId, out var cmd) ? cmd : "";

                // 获取插件和Mod列表
                var (plugins, mods) = GetServerPluginsAndMods(serverId);

                ServerCrashed?.Invoke(this, new ServerCrashEventArgs
                {
                    ServerId = serverId,
                    CrashMessage = message,
                    FullLog = fullLog,
                    DetectedKeyword = keyword,
                    StartupCommand = startupCommand,
                    PluginList = plugins,
                    ModList = mods
                });
                break;
            }
        }
    }

    /// <summary>
    /// 获取服务器的插件和Mod列表（并行扫描 plugins 与 mods 目录）
    /// </summary>
    private (List<string> plugins, List<string> mods) GetServerPluginsAndMods(int serverId)
    {
        var plugins = new List<string>();
        var mods = new List<string>();

        try
        {
            var server = _db.Servers.Find(serverId);
            if (server == null) return (plugins, mods);

            var pluginsPath = Path.Combine(server.ServerPath, "plugins");
            var modsPath = Path.Combine(server.ServerPath, "mods");

            // 并行扫描 plugins 与 mods 目录
            Parallel.Invoke(
                () =>
                {
                    if (Directory.Exists(pluginsPath))
                    {
                        var jarFiles = Directory.GetFiles(pluginsPath, "*.jar");
                        plugins.AddRange(jarFiles.Select(Path.GetFileName).Where(n => n != null).Cast<string>());
                    }
                },
                () =>
                {
                    if (Directory.Exists(modsPath))
                    {
                        var jarFiles = Directory.GetFiles(modsPath, "*.jar");
                        mods.AddRange(jarFiles.Select(Path.GetFileName).Where(n => n != null).Cast<string>());
                    }
                });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取插件/Mod列表失败: {ex.Message}");
        }

        return (plugins, mods);
    }

    // 重置崩溃检测状态（服务器启动时调用）
    private void ResetCrashDetection(int serverId)
    {
        _crashDetected[serverId] = false;
        _userRequestedStop[serverId] = false;
    }

    /// <summary>
    /// 获取所有正在运行的服务器进程
    /// </summary>
    public IEnumerable<Process> GetRunningProcesses()
    {
        lock (_runningServers)
        {
            return _runningServers.Values.ToList();
        }
    }

    /// <summary>
    /// 检查服务器名称是否已存在
    /// </summary>
    /// <param name="name">服务器名称</param>
    /// <param name="excludeId">排除的服务器ID（用于更新时排除自身）</param>
    public async Task<bool> IsServerNameExistsAsync(string name, int? excludeId = null)
    {
        return await _db.ExecuteWithLockAsync(async db => 
            await db.Servers.AnyAsync(s => s.Name == name && (excludeId == null || s.Id != excludeId)));
    }

    /// <summary>
    /// 验证服务器目录是否有效
    /// </summary>
    public async Task<ServerValidationResult> ValidateServerDirectoryAsync(string serverPath)
    {
        var result = new ServerValidationResult();

        if (!Directory.Exists(serverPath))
        {
            result.IsValid = false;
            result.Message = "目录不存在";
            return result;
        }

        // 查找 jar 文件 (使用 Task.Run 避免阻塞 UI 线程)
        string[] jarFiles;
        try 
        {
            jarFiles = await Task.Run(() => Directory.GetFiles(serverPath, "*.jar", SearchOption.TopDirectoryOnly));
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Message = $"无法访问目录: {ex.Message}";
            return result;
        }

        if (jarFiles.Length == 0)
        {
            result.IsValid = false;
            result.Message = "未找到服务端 JAR 文件";
            return result;
        }

        // 优先选择包含特定关键字的 jar 文件
        string? selectedJar = null;
        var priorityKeywords = new[] { "server", "forge", "fabric", "neoforge", "paper", "spigot", "purpur", "velocity", "bungeecord" };

        foreach (var keyword in priorityKeywords)
        {
            selectedJar = jarFiles.FirstOrDefault(j =>
                Path.GetFileName(j).Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (selectedJar != null) break;
        }

        selectedJar ??= jarFiles[0];
        result.DetectedJarFile = Path.GetFileName(selectedJar);

        // 检测核心类型和版本
        var jarNameLower = result.DetectedJarFile.ToLowerInvariant();

        if (jarNameLower.Contains("neoforge"))
            result.DetectedCoreType = "neoforge";
        else if (jarNameLower.Contains("forge"))
            result.DetectedCoreType = "forge";
        else if (jarNameLower.Contains("fabric"))
            result.DetectedCoreType = "fabric";
        else if (jarNameLower.Contains("quilt"))
            result.DetectedCoreType = "quilt";
        else if (jarNameLower.Contains("paper"))
            result.DetectedCoreType = "paper";
        else if (jarNameLower.Contains("purpur"))
            result.DetectedCoreType = "purpur";
        else if (jarNameLower.Contains("spigot"))
            result.DetectedCoreType = "spigot";
        else if (jarNameLower.Contains("velocity"))
            result.DetectedCoreType = "velocity";
        else if (jarNameLower.Contains("bungeecord"))
            result.DetectedCoreType = "bungeecord";
        else if (jarNameLower.Contains("server"))
            result.DetectedCoreType = "vanilla";
        else
            result.DetectedCoreType = "unknown";

        // 尝试从文件名提取版本号
        var versionMatch = System.Text.RegularExpressions.Regex.Match(result.DetectedJarFile, @"(\d+\.\d+(?:\.\d+)?)");
        if (versionMatch.Success)
        {
            result.DetectedMcVersion = versionMatch.Groups[1].Value;
        }

        // 尝试从 server.properties 读取端口
        var propertiesPath = Path.Combine(serverPath, "server.properties");
        if (File.Exists(propertiesPath))
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(propertiesPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("server-port="))
                    {
                        if (int.TryParse(line.Substring("server-port=".Length), out var port))
                        {
                            result.DetectedPort = port;
                        }
                    }
                }
            }
            catch { }
        }

        // 建议的服务器名称
        result.SuggestedName = Path.GetFileName(serverPath);

        result.IsValid = true;
        result.Message = "服务器目录有效";
        return result;
    }

    /// <summary>
    /// 导入已有的服务器
    /// </summary>
    public async Task<ImportServerResult> ImportServerAsync(
        string serverName,
        string serverPath,
        string jarFileName,
        string coreType,
        string mcVersion,
        int minMemory,
        int maxMemory,
        int port)
    {
        try
        {
            // 检查重名
            if (await IsServerNameExistsAsync(serverName))
            {
                return new ImportServerResult { Success = false, Message = $"服务器名称 '{serverName}' 已存在" };
            }

            // 创建服务器记录
            var server = new LocalServer
            {
                Name = serverName,
                CoreType = coreType,
                CoreVersion = "",
                MinecraftVersion = mcVersion,
                ServerPath = serverPath,
                JarFileName = jarFileName,
                MinMemoryMB = minMemory,
                MaxMemoryMB = maxMemory,
                Port = port,
                Mode = CreateMode.Advanced,
                CreatedAt = DateTime.Now
            };

            await _db.ExecuteWithLockAsync(async db => 
            {
                db.Servers.Add(server);
                await db.SaveChangesAsync();
            });

            return new ImportServerResult { Success = true, Message = "导入成功", Server = server };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"导入服务器失败: {ex.Message}");
            return new ImportServerResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<LocalServer> CreateServerAsync(LocalServer server)
    {
        // 检查重名
        if (await IsServerNameExistsAsync(server.Name))
        {
            throw new InvalidOperationException($"服务器名称 '{server.Name}' 已存在，请使用其他名称");
        }

        // 创建服务器目录
        Directory.CreateDirectory(server.ServerPath);
        
        // 如果是小白模式且使用Purpur最新版本，设置默认值
        if (server.Mode == CreateMode.Beginner && server.UseLatestPurpur)
        {
            // 这些值会在确认页面中设置
            server.CoreType = server.CoreType ?? "purpur";
            server.CoreVersion = server.CoreVersion ?? "latest";
            server.MinecraftVersion = server.MinecraftVersion ?? "latest";
        }

        // 根据游戏版本自动选择 Java 版本
        await SelectJavaVersionByMinecraftVersionAsync(server);

        // 自动生成英文别名
        if (string.IsNullOrWhiteSpace(server.EnglishAlias))
        {
            server.EnglishAlias = GenerateEnglishAlias(server.Name);
        }

        await _db.ExecuteWithLockAsync(async db => 
        {
            db.Servers.Add(server);
            await db.SaveChangesAsync();
        });
        return server;
    }

    /// <summary>
    /// 根据 Minecraft 版本自动选择合适的 Java 版本
    /// </summary>
    private async Task SelectJavaVersionByMinecraftVersionAsync(LocalServer server)
    {
        // 如果已经指定了 Java 路径，不覆盖
        if (!string.IsNullOrWhiteSpace(server.JavaPath))
        {
            return;
        }

        try
        {
            // 解析 Minecraft 版本号
            var minecraftVersion = server.MinecraftVersion ?? "1.20";
            
            // 尝试解析版本号（支持 1.20.1 和 26.1 两种格式）
            if (double.TryParse(minecraftVersion, out var version))
            {
                // 如果版本大于 26.1，使用 Java 25
                if (version > 26.1)
                {
                    var java25Path = await FindJavaVersionAsync(25);
                    if (!string.IsNullOrEmpty(java25Path))
                    {
                        server.JavaPath = java25Path;
                        System.Diagnostics.Debug.WriteLine($"[ServerManager] 为版本 {minecraftVersion} 自动选择 Java 25: {java25Path}");
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ServerManager] 自动选择 Java 版本失败: {ex.Message}");
        }

        // 如果没有找到合适的 Java 版本，使用默认的 java
        server.JavaPath = "java";
    }

    /// <summary>
    /// 查找指定版本的 Java
    /// </summary>
    private async Task<string?> FindJavaVersionAsync(int targetVersion)
    {
        try
        {
            // 从数据库中查找指定版本的 Java
            var javaList = await _db.ExecuteWithLockAsync(async db =>
                await db.JavaInstallations
                    .Where(j => j.IsValid && j.Version == targetVersion)
                    .OrderByDescending(j => j.DetectedAt)
                    .FirstOrDefaultAsync());

            if (javaList != null && File.Exists(javaList.Path))
            {
                return javaList.Path;
            }

            // 如果数据库中没有，尝试从常见位置查找
            var commonPaths = new[]
            {
                $"C:\\Program Files\\Java\\jdk-{targetVersion}\\bin\\java.exe",
                $"C:\\Program Files (x86)\\Java\\jdk-{targetVersion}\\bin\\java.exe",
                $"C:\\Program Files\\OpenJDK\\jdk-{targetVersion}\\bin\\java.exe",
                $"C:\\Program Files\\Eclipse Adoptium\\jdk-{targetVersion}\\bin\\java.exe"
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ServerManager] 查找 Java {targetVersion} 失败: {ex.Message}");
        }

        return null;
    }

    private string GenerateEnglishAlias(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name)) return "server_" + DateTime.Now.Ticks;

            // 如果全是英文/数字/下划线，直接使用
            if (System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-zA-Z0-9_]+$"))
            {
                return name;
            }

            // 转换为拼音
            var pinyin = PinyinHelper.GetPinyin(name, "");
            // 移除特殊字符
            pinyin = System.Text.RegularExpressions.Regex.Replace(pinyin, "[^a-zA-Z0-9_]", "");
            
            if (string.IsNullOrWhiteSpace(pinyin))
            {
                return "server_" + DateTime.Now.Ticks;
            }
            
            return pinyin;
        }
        catch
        {
            // 降级处理
            return "server_" + DateTime.Now.Ticks;
        }
    }

    public async Task UpdateServerAsync(LocalServer server)
    {
        // 检查重名（排除自身）
        if (await IsServerNameExistsAsync(server.Name, server.Id))
        {
            throw new InvalidOperationException($"服务器名称 '{server.Name}' 已存在，请使用其他名称");
        }

        await _db.ExecuteWithLockAsync(async db => 
        {
            db.Servers.Update(server);
            await db.SaveChangesAsync();
        });
    }

    public async Task DeleteServerAsync(int serverId)
    {
        var server = await _db.Servers.FindAsync(serverId);
        if (server != null)
        {
            // 停止服务器(如果运行中)
            if (IsServerRunning(serverId))
            {
                await StopServerAsync(serverId);
            }

            // 将服务器目录移至回收站
            if (!string.IsNullOrEmpty(server.ServerPath) && Directory.Exists(server.ServerPath))
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        server.ServerPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"移至回收站失败: {ex.Message}");
                }
            }

            // 删除数据库记录
            _db.Servers.Remove(server);
            
            // 删除相关备份记录
            var backups = await _db.Backups.Where(b => b.ServerId == serverId).ToListAsync();
            _db.Backups.RemoveRange(backups);
            
            await _db.SaveChangesAsync();
        }
    }

    public bool IsServerRunning(int serverId)
    {
        return _runningServers.ContainsKey(serverId) && !_runningServers[serverId].HasExited;
    }

    // 获取所有运行中的服务器ID
    public List<int> GetRunningServerIds()
    {
        return _runningServers.Where(kv => !kv.Value.HasExited).Select(kv => kv.Key).ToList();
    }

    // 停止所有服务器（全并行，每台独立等待退出）
    public async Task StopAllServersAsync()
    {
        var runningIds = GetRunningServerIds();
        if (runningIds.Count == 0) return;

        // 同时向所有服务器发送 stop 命令，然后并行等待各自退出
        var stopTasks = runningIds.Select(serverId => StopServerAsync(serverId)).ToList();
        await Task.WhenAll(stopTasks);
    }

    public Process? GetServerProcess(int serverId)
    {
        return _runningServers.TryGetValue(serverId, out var process) && !process.HasExited ? process : null;
    }

    public Task<ServerStartResult> StartServerAsync(LocalServer server) => StartServerAsync(server.Id);

    /// <summary>
    /// 检查 EULA 状态
    /// </summary>
    public bool CheckEulaAccepted(string serverPath)
    {
        var eulaPath = Path.Combine(serverPath, "eula.txt");
        if (!File.Exists(eulaPath)) return false;
        
        var content = File.ReadAllText(eulaPath);
        return content.Contains("eula=true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 同意 EULA 并写入文件
    /// </summary>
    public async Task AcceptEulaAsync(string serverPath)
    {
        var eulaPath = Path.Combine(serverPath, "eula.txt");
        await File.WriteAllTextAsync(eulaPath, "#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\neula=true");
    }

    public async Task<ServerStartResult> StartServerAsync(int serverId)
    {
        var server = await _db.ExecuteWithLockAsync(async db => await db.Servers.FindAsync(serverId));
        if (server == null) return ServerStartResult.Failed;

        if (IsServerRunning(serverId))
        {
            return ServerStartResult.AlreadyRunning;
        }

        // 检查服务端文件是否存在
        if (string.IsNullOrWhiteSpace(server.StartupCommand))
        {
            // 如果是参数文件格式（@xxx.txt），去掉 @ 符号检查
            var jarFileName = server.JarFileName;
            if (jarFileName.StartsWith("@"))
            {
                jarFileName = jarFileName.Substring(1);
            }
            
            var jarPath = Path.Combine(server.ServerPath, jarFileName);
            if (!File.Exists(jarPath))
            {
                throw new FileNotFoundException($"服务端文件不存在: {jarPath}");
            }
        }

        // 检查 EULA 状态 (Velocity/BungeeCord 不需要 EULA)
        bool isProxy = server.CoreType?.ToLower().Contains("velocity") == true || 
                       server.CoreType?.ToLower().Contains("bungeecord") == true;

        if (!isProxy && !CheckEulaAccepted(server.ServerPath))
        {
            return ServerStartResult.NeedEulaAccept;
        }

        var javaPath = server.JavaPath ?? "java";

        // 检查 Java 是否存在（仅在无自定义启动命令时）
        if (string.IsNullOrWhiteSpace(server.StartupCommand))
        {
            if (!File.Exists(javaPath) && javaPath != "java")
            {
                return ServerStartResult.JavaNotFound;
            }
        }

        // 获取全局设置
        var settings = await _db.GetSettingsAsync();

        // 如果使用默认 java，检查是否在 PATH 中（仅在无自定义启动命令时）
        if (string.IsNullOrWhiteSpace(server.StartupCommand) && javaPath == "java")
        {
            try
            {
                var testProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (testProcess != null)
                {
                    testProcess.WaitForExit(3000);
                    if (testProcess.ExitCode != 0)
                    {
                        return ServerStartResult.JavaNotFound;
                    }
                }
                else
                {
                    return ServerStartResult.JavaNotFound;
                }
            }
            catch
            {
                return ServerStartResult.JavaNotFound;
            }
        }

        // 注册编码提供程序
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 确定编码
        Encoding consoleEncoding;
        string javaEncodingName;

        switch (settings.ConsoleEncoding)
        {
            case "ANSI":
                consoleEncoding = Encoding.Default;
                // 尝试获取系统默认代码页名称
                javaEncodingName = "GBK"; // 默认假设GBK以兼容中文Windows
                break;
            case "GB18030":
                try { consoleEncoding = Encoding.GetEncoding("GB18030"); } catch { consoleEncoding = Encoding.GetEncoding("GBK"); }
                javaEncodingName = "GB18030";
                break;
            case "UTF-8":
            default:
                consoleEncoding = Encoding.UTF8;
                javaEncodingName = "UTF-8";
                break;
        }

        string fileName;
        string arguments;
        string fullCommand;

        if (!string.IsNullOrWhiteSpace(server.StartupCommand))
        {
            // 使用自定义启动命令
            fileName = "cmd.exe";
            arguments = $"/c {server.StartupCommand}";
            fullCommand = server.StartupCommand;
        }
        else
        {
            var encodingArgs = $"-Dfile.encoding={javaEncodingName} -Dsun.stdout.encoding={javaEncodingName} -Dsun.stderr.encoding={javaEncodingName}";
            
            // authlib-injector 支持
            var authlibArgs = "";
            if (server.EnableAuthlib && !string.IsNullOrWhiteSpace(server.AuthlibUrl))
            {
                var authlibPath = Path.Combine(server.ServerPath, "authlib-injector-1.2.7.jar");
                if (File.Exists(authlibPath))
                {
                    authlibArgs = $"-javaagent:{authlibPath}={server.AuthlibUrl} ";
                }
                else if (!server.AuthlibDownloaded)
                {
                    // 尝试下载 authlib-injector
                    try
                    {
                        await DownloadAuthlibAsync(server);
                        if (File.Exists(authlibPath))
                        {
                            authlibArgs = $"-javaagent:{authlibPath}={server.AuthlibUrl} ";
                        }
                    }
                    catch { }
                }
            }
            
            var jvmArgs = !string.IsNullOrWhiteSpace(server.JvmArgs)
                ? $"{authlibArgs}{encodingArgs} -Xmx{server.MaxMemoryMB}M -Xms{server.MinMemoryMB}M {server.JvmArgs}"
                : $"{authlibArgs}{encodingArgs} -Xmx{server.MaxMemoryMB}M -Xms{server.MinMemoryMB}M";

            fileName = javaPath;
            
            // 检查是否为参数文件格式（@xxx.txt）
            if (server.JarFileName.StartsWith("@"))
            {
                // Forge 1.17+ 使用参数文件格式
                arguments = $"{jvmArgs} {server.JarFileName} nogui";
                fullCommand = $"\"{javaPath}\" {jvmArgs} {server.JarFileName} nogui";
            }
            else
            {
                // 传统 JAR 格式
                arguments = $"{jvmArgs} -jar \"{server.JarFileName}\" nogui";
                fullCommand = $"\"{javaPath}\" {jvmArgs} -jar \"{server.JarFileName}\" nogui";
            }
        }

        // 存储完整启动命令用于崩溃分析
        _serverStartupCommands[serverId] = fullCommand;

        // 重置崩溃检测状态
        ResetCrashDetection(serverId);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = server.ServerPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = consoleEncoding,
                StandardErrorEncoding = consoleEncoding
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AddLog(serverId, e.Data);
                ServerOutput?.Invoke(this, new ServerOutputEventArgs
                {
                    ServerId = serverId,
                    Message = e.Data,
                    IsError = false
                });
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AddLog(serverId, e.Data);
                ServerOutput?.Invoke(this, new ServerOutputEventArgs
                {
                    ServerId = serverId,
                    Message = e.Data,
                    IsError = true
                });
            }
        };

        process.Exited += (s, e) =>
        {
            var exitCode = 0;
            try { exitCode = process.ExitCode; } catch { }

            _runningServers.Remove(serverId);

            // 检查是否是非正常退出（用户未主动停止且未检测到崩溃关键词）
            var userStopped = _userRequestedStop.TryGetValue(serverId, out var stopped) && stopped;
            var crashAlreadyDetected = _crashDetected.TryGetValue(serverId, out var detected) && detected;

            if (!userStopped && !crashAlreadyDetected)
            {
                // 进程意外退出，触发崩溃检测
                Debug.WriteLine($"[ServerManager] 服务器 {serverId} 意外退出, ExitCode: {exitCode}");

                var fullLog = string.Join("\n", GetServerLogs(serverId));
                var startupCommand = _serverStartupCommands.TryGetValue(serverId, out var cmd) ? cmd : "";
                var (plugins, mods) = GetServerPluginsAndMods(serverId);

                _crashDetected[serverId] = true;

                ServerCrashed?.Invoke(this, new ServerCrashEventArgs
                {
                    ServerId = serverId,
                    CrashMessage = $"服务器进程意外退出 (ExitCode: {exitCode})",
                    FullLog = fullLog,
                    DetectedKeyword = $"进程退出码: {exitCode}",
                    StartupCommand = startupCommand,
                    PluginList = plugins,
                    ModList = mods
                });
            }

            ServerStatusChanged?.Invoke(this, new ServerStatusEventArgs
            {
                ServerId = serverId,
                IsRunning = false
            });
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 将进程添加到 Job Object，确保启动器关闭时子进程也会终止
        var added = ChildProcessManager.Instance.AddProcess(process);
        Debug.WriteLine($"[ServerManager] 服务器进程 {process.Id} 添加到 Job Object: {added}, 当前 Job 中进程数: {ChildProcessManager.Instance.GetProcessCount()}");

        _runningServers[serverId] = process;

        server.LastStartedAt = DateTime.Now;
        await _db.ExecuteWithLockAsync(async db => await db.SaveChangesAsync());

        ServerStatusChanged?.Invoke(this, new ServerStatusEventArgs
        {
            ServerId = serverId,
            IsRunning = true
        });

        return ServerStartResult.Success;
    }

    public async Task StopServerAsync(int serverId)
    {
        // 标记为用户主动停止，避免触发崩溃检测
        _userRequestedStop[serverId] = true;

        if (_runningServers.TryGetValue(serverId, out var process) && !process.HasExited)
        {
            // 发送 stop 命令
            await process.StandardInput.WriteLineAsync("stop");

            // 异步等待最多 30 秒，不阻塞 UI 线程
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 超时则强制结束
                try { process.Kill(); } catch { }
            }

            _runningServers.Remove(serverId);
        }
    }

    public async Task SendCommandAsync(int serverId, string command)
    {
        if (_runningServers.TryGetValue(serverId, out var process) && !process.HasExited)
        {
            await process.StandardInput.WriteLineAsync(command);
        }
    }

    public string? DetectJava()
    {
        var javas = DetectAllJava();
        return javas.FirstOrDefault()?.Path;
    }

    /// <summary>
    /// 异步检测默认 Java 路径（多线程优化）
    /// </summary>
    public async Task<string?> DetectJavaAsync()
    {
        var javas = await DetectAllJavaAsync();
        return javas.FirstOrDefault()?.Path;
    }

    /// <summary>
    /// 异步并行检测所有 Java 安装（多线程优化，避免阻塞 UI）
    /// </summary>
    public async Task<List<JavaInfo>> DetectAllJavaAsync()
    {
        var result = new List<JavaInfo>();
        var checkedPaths = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        // 1. 收集所有待检测的 java.exe 路径
        var candidates = new List<string>();

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
            candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "java",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var path = line.Trim();
                    if (!string.IsNullOrEmpty(path)) candidates.Add(path);
                }
            }
        }
        catch { }

        var commonPaths = new[]
        {
            @"C:\Program Files\Java",
            @"C:\Program Files (x86)\Java",
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\Zulu",
            @"C:\Program Files\Microsoft",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"),
        };

        foreach (var basePath in commonPaths)
        {
            if (!Directory.Exists(basePath)) continue;
            try
            {
                foreach (var dir in Directory.GetDirectories(basePath))
                {
                    candidates.Add(Path.Combine(dir, "bin", "java.exe"));
                }
            }
            catch { }
        }

        // 2. 并行检测每个路径的 Java 版本（限制并发数避免过多进程）
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        var bag = new System.Collections.Concurrent.ConcurrentBag<JavaInfo>();

        var tasks = candidates.Select(async path =>
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var fullPath = Path.GetFullPath(path);
            if (!checkedPaths.TryAdd(fullPath, 0)) return;

            await semaphore.WaitAsync();
            try
            {
                var version = GetJavaVersion(fullPath);
                if (version != null)
                    bag.Add(new JavaInfo { Path = fullPath, Version = version });
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        result = bag.OrderBy(j => j.Path).ToList();
        return result;
    }

    /// <summary>
    /// 同步检测 Java（在线程池执行异步方法，避免在 UI 线程上 GetResult 导致死锁）
    /// </summary>
    public List<JavaInfo> DetectAllJava()
    {
        return Task.Run(() => DetectAllJavaAsync()).GetAwaiter().GetResult();
    }

    private void AddJavaIfValid(List<JavaInfo> list, HashSet<string> checkedPaths, string javaPath)
    {
        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath)) return;
        var fullPath = Path.GetFullPath(javaPath);
        if (checkedPaths.Contains(fullPath)) return;
        checkedPaths.Add(fullPath);

        var version = GetJavaVersion(fullPath);
        if (version != null)
        {
            list.Add(new JavaInfo { Path = fullPath, Version = version });
        }
    }

    private string? GetJavaVersion(string javaPath)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process != null)
            {
                var output = process.StandardError.ReadToEnd();
                process.WaitForExit(3000);
                // 解析版本号，如 "17.0.1" 或 "1.8.0_301"
                var match = System.Text.RegularExpressions.Regex.Match(output, @"version ""([^""]+)""");
                if (match.Success) return match.Groups[1].Value;
            }
        }
        catch { }
        return null;
    }
    
    private async Task DownloadAuthlibAsync(LocalServer server)
    {
        try
        {
            var authlibPath = Path.Combine(server.ServerPath, "authlib-injector-1.2.7.jar");
            
            if (File.Exists(authlibPath))
            {
                server.AuthlibDownloaded = true;
                await _db.SaveChangesAsync();
                return;
            }
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            
            var url = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/55/authlib-injector-1.2.7.jar";
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(authlibPath, content);
            
            server.AuthlibDownloaded = true;
            await _db.SaveChangesAsync();
            System.Diagnostics.Debug.WriteLine($"authlib-injector 下载完成: {authlibPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"下载 authlib-injector 失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 从整合包创建服务器
    /// </summary>
    public async Task<ModpackCreateResult> CreateServerFromModpackAsync(
        string serverName,
        string modpackPath,
        int minMemory,
        int maxMemory,
        int port,
        bool saveClientPack)
    {
        try
        {
            // 检查重名
            if (await IsServerNameExistsAsync(serverName))
            {
                return new ModpackCreateResult { Success = false, Message = $"服务器名称 '{serverName}' 已存在" };
            }

            // 获取默认服务器路径
            var settings = await _db.Settings.FirstOrDefaultAsync() ?? new AppSettings();
            var basePath = !string.IsNullOrEmpty(settings.DefaultServerPath)
                ? settings.DefaultServerPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZMSL", "servers");

            var serverPath = Path.Combine(basePath, serverName);
            Directory.CreateDirectory(serverPath);

            // 解析整合包
            var modpackInfo = await ParseModpackAsync(modpackPath);
            if (modpackInfo == null)
            {
                return new ModpackCreateResult { Success = false, Message = "无法解析整合包文件" };
            }

            // 解压整合包
            await ExtractModpackOverridesAsync(modpackPath, serverPath, modpackInfo.IsCurseForgeServerPack);

            // 如果不是 CurseForge 服务端包，需要下载整合包中的文件
            if (!modpackInfo.IsCurseForgeServerPack)
            {
                await DownloadModpackFilesAsync(modpackInfo, serverPath);
            }

            // 确定核心类型和版本
            var coreType = DetermineLoaderType(modpackInfo);
            var mcVersion = modpackInfo.Dependencies?.TryGetValue("minecraft", out var v) == true ? v : "";

            string? jarFileName = null;

            // CurseForge 服务端包通常已经包含了服务端核心
            if (modpackInfo.IsCurseForgeServerPack)
            {
                // 查找已存在的 jar 文件
                jarFileName = FindExistingServerJar(serverPath);

                // 如果找到了 jar 文件，尝试从中检测版本信息
                if (!string.IsNullOrEmpty(jarFileName))
                {
                    var detectedInfo = DetectServerInfoFromFiles(serverPath, jarFileName);
                    if (!string.IsNullOrEmpty(detectedInfo.coreType))
                        coreType = detectedInfo.coreType;
                    if (!string.IsNullOrEmpty(detectedInfo.mcVersion))
                        mcVersion = detectedInfo.mcVersion;
                }
            }

            // 如果没有找到 jar 文件，下载服务端核心
            if (string.IsNullOrEmpty(jarFileName))
            {
                jarFileName = await DownloadServerCoreAsync(coreType, mcVersion, modpackInfo, serverPath);
                if (string.IsNullOrEmpty(jarFileName))
                {
                    return new ModpackCreateResult { Success = false, Message = "无法下载服务端核心" };
                }
            }

            // 保存客户端整合包
            if (saveClientPack)
            {
                var clientPackDir = Path.Combine(serverPath, "client-pack");
                Directory.CreateDirectory(clientPackDir);
                var destPath = Path.Combine(clientPackDir, Path.GetFileName(modpackPath));
                File.Copy(modpackPath, destPath, true);
            }

            // 创建服务器记录
            var server = new LocalServer
            {
                Name = serverName,
                CoreType = coreType,
                CoreVersion = modpackInfo.Dependencies?.GetValueOrDefault(coreType, "") ?? "",
                MinecraftVersion = mcVersion,
                ServerPath = serverPath,
                JarFileName = jarFileName,
                MinMemoryMB = minMemory,
                MaxMemoryMB = maxMemory,
                Port = port,
                Mode = CreateMode.Advanced,
                CreatedAt = DateTime.Now
            };

            _db.Servers.Add(server);
            await _db.SaveChangesAsync();

            return new ModpackCreateResult { Success = true, Message = "服务器创建成功", Server = server };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"创建整合包服务器失败: {ex.Message}");
            return new ModpackCreateResult { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// 从整合包创建服务器（带核心选择）
    /// </summary>
    public async Task<ModpackCreateResult> CreateServerFromModpackWithCoreAsync(
        string serverName,
        string modpackPath,
        int minMemory,
        int maxMemory,
        int port,
        bool saveClientPack,
        string coreName,
        string mcVersion,
        CancellationToken cancellationToken,
        Action<string, double>? progressCallback = null)
    {
        try
        {
            // 检查重名
            if (await IsServerNameExistsAsync(serverName))
            {
                return new ModpackCreateResult { Success = false, Message = $"服务器名称 '{serverName}' 已存在" };
            }

            progressCallback?.Invoke("正在准备...", -1);

            // 获取默认服务器路径
            var settings = await _db.Settings.FirstOrDefaultAsync(cancellationToken) ?? new AppSettings();
            var basePath = !string.IsNullOrEmpty(settings.DefaultServerPath)
                ? settings.DefaultServerPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZMSL", "servers");

            var serverPath = Path.Combine(basePath, serverName);
            Directory.CreateDirectory(serverPath);

            cancellationToken.ThrowIfCancellationRequested();

            // 解析整合包
            progressCallback?.Invoke("正在解析整合包...", -1);
            var modpackInfo = await ParseModpackAsync(modpackPath);

            // 解压整合包
            progressCallback?.Invoke("正在解压整合包...", -1);
            var isCurseForgeServerPack = modpackInfo?.IsCurseForgeServerPack ?? false;
            await ExtractModpackOverridesAsync(modpackPath, serverPath, isCurseForgeServerPack);

            cancellationToken.ThrowIfCancellationRequested();

            // 如果不是 CurseForge 服务端包且有 modpackInfo，需要下载整合包中的文件
            if (!isCurseForgeServerPack && modpackInfo?.Files != null)
            {
                progressCallback?.Invoke("正在下载整合包文件...", -1);
                await DownloadModpackFilesAsync(modpackInfo, serverPath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            string? jarFileName = null;

            // CurseForge 服务端包通常已经包含了服务端核心
            if (isCurseForgeServerPack)
            {
                jarFileName = FindExistingServerJar(serverPath);
            }

            // 如果没有找到 jar 文件，下载用户选择的服务端核心
            if (string.IsNullOrEmpty(jarFileName))
            {
                progressCallback?.Invoke($"正在下载 {coreName} {mcVersion} 服务端核心...", 0);

                var downloadService = App.Services.GetRequiredService<ServerDownloadService>();
                jarFileName = await downloadService.DownloadServerCoreAsync(
                    coreName,
                    mcVersion,
                    serverPath,
                    "latest",
                    cancellationToken);

                if (string.IsNullOrEmpty(jarFileName))
                {
                    return new ModpackCreateResult { Success = false, Message = "无法下载服务端核心" };
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 保存客户端整合包
            if (saveClientPack)
            {
                progressCallback?.Invoke("正在保存客户端整合包...", -1);
                await Task.Run(() =>
                {
                    var clientPackDir = Path.Combine(serverPath, "client-pack");
                    Directory.CreateDirectory(clientPackDir);
                    var destPath = Path.Combine(clientPackDir, Path.GetFileName(modpackPath));
                    File.Copy(modpackPath, destPath, true);
                });
            }

            progressCallback?.Invoke("正在保存服务器配置...", -1);

            // 创建服务器记录
            var server = new LocalServer
            {
                Name = serverName,
                CoreType = coreName,
                CoreVersion = "",
                MinecraftVersion = mcVersion,
                ServerPath = serverPath,
                JarFileName = Path.GetFileName(jarFileName),
                MinMemoryMB = minMemory,
                MaxMemoryMB = maxMemory,
                Port = port,
                Mode = CreateMode.Advanced,
                CreatedAt = DateTime.Now
            };

            _db.Servers.Add(server);
            await _db.SaveChangesAsync(cancellationToken);

            return new ModpackCreateResult { Success = true, Message = "服务器创建成功", Server = server };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"创建整合包服务器失败: {ex.Message}");
            return new ModpackCreateResult { Success = false, Message = ex.Message };
        }
    }

    private string? FindExistingServerJar(string serverPath)
    {
        // 查找常见的服务端 jar 文件名
        var commonNames = new[]
        {
            "server.jar", "forge-*.jar", "neoforge-*.jar", "fabric-server-*.jar",
            "quilt-server-*.jar", "minecraft_server.*.jar", "paper-*.jar", "spigot-*.jar"
        };

        foreach (var pattern in commonNames)
        {
            var files = Directory.GetFiles(serverPath, pattern, SearchOption.TopDirectoryOnly);
            if (files.Length > 0)
            {
                return Path.GetFileName(files[0]);
            }
        }

        // 查找任何 jar 文件
        var allJars = Directory.GetFiles(serverPath, "*.jar", SearchOption.TopDirectoryOnly);
        if (allJars.Length > 0)
        {
            // 优先选择包含 server/forge/fabric/neoforge 的 jar
            var serverJar = allJars.FirstOrDefault(j =>
            {
                var name = Path.GetFileName(j).ToLowerInvariant();
                return name.Contains("server") || name.Contains("forge") ||
                       name.Contains("fabric") || name.Contains("neoforge");
            });
            return Path.GetFileName(serverJar ?? allJars[0]);
        }

        return null;
    }

    private (string coreType, string mcVersion) DetectServerInfoFromFiles(string serverPath, string jarFileName)
    {
        var coreType = "";
        var mcVersion = "";
        var jarNameLower = jarFileName.ToLowerInvariant();

        // 从 jar 文件名检测核心类型
        if (jarNameLower.Contains("neoforge"))
            coreType = "neoforge";
        else if (jarNameLower.Contains("forge"))
            coreType = "forge";
        else if (jarNameLower.Contains("fabric"))
            coreType = "fabric";
        else if (jarNameLower.Contains("quilt"))
            coreType = "quilt";
        else if (jarNameLower.Contains("paper"))
            coreType = "paper";
        else if (jarNameLower.Contains("spigot"))
            coreType = "spigot";

        // 尝试从文件名提取版本号（例如 forge-1.20.1-47.2.0.jar）
        var versionMatch = System.Text.RegularExpressions.Regex.Match(jarFileName, @"(\d+\.\d+(?:\.\d+)?)");
        if (versionMatch.Success)
        {
            mcVersion = versionMatch.Groups[1].Value;
        }

        return (coreType, mcVersion);
    }

    private async Task<ModpackIndex?> ParseModpackAsync(string modpackPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(modpackPath);

            // 尝试 Modrinth 格式
            var indexEntry = archive.GetEntry("modrinth.index.json");
            if (indexEntry != null)
            {
                using var stream = indexEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                return JsonSerializer.Deserialize<ModpackIndex>(json);
            }

            // 尝试 CurseForge 格式
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry != null)
            {
                using var stream = manifestEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                var cfManifest = JsonSerializer.Deserialize<CurseForgeManifest>(json);
                if (cfManifest != null)
                {
                    return ConvertCurseForgeManifest(cfManifest);
                }
            }

            // CurseForge 服务端包格式：直接包含服务端文件（没有 manifest）
            // 检查是否包含典型的服务端文件结构
            var hasModsFolder = archive.Entries.Any(e => e.FullName.StartsWith("mods/", StringComparison.OrdinalIgnoreCase));
            var hasConfigFolder = archive.Entries.Any(e => e.FullName.StartsWith("config/", StringComparison.OrdinalIgnoreCase));
            var hasServerJar = archive.Entries.Any(e => e.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
            var hasStartScript = archive.Entries.Any(e =>
                e.Name.Equals("start.bat", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals("start.sh", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals("run.bat", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals("run.sh", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals("ServerStart.bat", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals("startserver.bat", StringComparison.OrdinalIgnoreCase));

            if (hasModsFolder || hasConfigFolder || hasServerJar || hasStartScript)
            {
                // 这是一个 CurseForge 服务端包，创建一个虚拟的 ModpackIndex
                return new ModpackIndex
                {
                    FormatVersion = 1,
                    Game = "minecraft",
                    Name = Path.GetFileNameWithoutExtension(modpackPath),
                    IsCurseForgeServerPack = true,  // 标记为 CurseForge 服务端包
                    Dependencies = new Dictionary<string, string>()
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"解析整合包失败: {ex.Message}");
            return null;
        }
    }

    private ModpackIndex ConvertCurseForgeManifest(CurseForgeManifest manifest)
    {
        var index = new ModpackIndex
        {
            FormatVersion = 1,
            Game = "minecraft",
            Name = manifest.Name,
            VersionId = manifest.Version,
            Dependencies = new Dictionary<string, string>()
        };

        // 转换 Minecraft 版本
        if (!string.IsNullOrEmpty(manifest.Minecraft?.Version))
        {
            index.Dependencies["minecraft"] = manifest.Minecraft.Version;
        }

        // 转换 mod loader
        if (manifest.Minecraft?.ModLoaders != null)
        {
            foreach (var loader in manifest.Minecraft.ModLoaders)
            {
                if (string.IsNullOrEmpty(loader.Id)) continue;

                if (loader.Id.StartsWith("forge-", StringComparison.OrdinalIgnoreCase))
                {
                    index.Dependencies["forge"] = loader.Id.Substring(6);
                }
                else if (loader.Id.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
                {
                    index.Dependencies["neoforge"] = loader.Id.Substring(9);
                }
                else if (loader.Id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
                {
                    index.Dependencies["fabric-loader"] = loader.Id.Substring(7);
                }
            }
        }

        return index;
    }

    private async Task ExtractModpackOverridesAsync(string modpackPath, string serverPath, bool isCurseForgeServerPack = false)
    {
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(modpackPath);

            // 先创建所有需要的目录
            var directories = new HashSet<string>();

            if (isCurseForgeServerPack)
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var destDir = Path.GetDirectoryName(Path.Combine(serverPath, entry.FullName));
                    if (!string.IsNullOrEmpty(destDir))
                        directories.Add(destDir);
                }
            }
            else
            {
                var overridesPrefixes = new[] { "server-overrides/", "overrides/" };
                foreach (var prefix in overridesPrefixes)
                {
                    foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(prefix) && !string.IsNullOrEmpty(e.Name)))
                    {
                        var relativePath = entry.FullName.Substring(prefix.Length);
                        var destDir = Path.GetDirectoryName(Path.Combine(serverPath, relativePath));
                        if (!string.IsNullOrEmpty(destDir))
                            directories.Add(destDir);
                    }
                }
            }

            // 批量创建目录
            foreach (var dir in directories)
            {
                Directory.CreateDirectory(dir);
            }

            // 并行解压文件
            if (isCurseForgeServerPack)
            {
                var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    entry =>
                    {
                        try
                        {
                            var destPath = Path.Combine(serverPath, entry.FullName);
                            entry.ExtractToFile(destPath, true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"解压文件失败 {entry.FullName}: {ex.Message}");
                        }
                    });
            }
            else
            {
                var overridesPrefixes = new[] { "server-overrides/", "overrides/" };
                foreach (var prefix in overridesPrefixes)
                {
                    var entries = archive.Entries.Where(e => e.FullName.StartsWith(prefix) && !string.IsNullOrEmpty(e.Name)).ToList();
                    Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        entry =>
                        {
                            try
                            {
                                var relativePath = entry.FullName.Substring(prefix.Length);
                                var destPath = Path.Combine(serverPath, relativePath);
                                entry.ExtractToFile(destPath, true);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"解压文件失败 {entry.FullName}: {ex.Message}");
                            }
                        });
                }
            }
        });
    }

    private async Task DownloadModpackFilesAsync(ModpackIndex modpackInfo, string serverPath)
    {
        if (modpackInfo.Files == null) return;

        // 只下载服务端需要的文件
        var serverFiles = modpackInfo.Files.Where(f =>
            f.Env == null ||
            (f.Env.TryGetValue("server", out var serverEnv) && serverEnv != "unsupported"))
            .Where(f => f.Downloads != null && f.Downloads.Count > 0)
            .ToList();

        if (serverFiles.Count == 0) return;

        // 先创建所有需要的目录
        var directories = serverFiles
            .Select(f => Path.GetDirectoryName(Path.Combine(serverPath, f.Path ?? "")))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .ToList();

        foreach (var dir in directories)
        {
            Directory.CreateDirectory(dir!);
        }

        // 使用信号量限制并发下载数
        var semaphore = new SemaphoreSlim(5); // 最多5个并发下载
        var downloadedCount = 0;
        var totalCount = serverFiles.Count;

        var downloadTasks = serverFiles.Select(async file =>
        {
            await semaphore.WaitAsync();
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                httpClient.DefaultRequestHeaders.Add("User-Agent", "ZMSL-App/1.0");

                var destPath = Path.Combine(serverPath, file.Path ?? "");
                var url = file.Downloads![0];

                var bytes = await httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(destPath, bytes);

                var count = Interlocked.Increment(ref downloadedCount);
                Debug.WriteLine($"下载文件 ({count}/{totalCount}): {file.Path}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"下载文件失败 {file.Path}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(downloadTasks);
    }

    private string DetermineLoaderType(ModpackIndex modpackInfo)
    {
        if (modpackInfo.Dependencies == null) return "fabric";

        if (modpackInfo.Dependencies.ContainsKey("fabric-loader")) return "fabric";
        if (modpackInfo.Dependencies.ContainsKey("quilt-loader")) return "quilt";
        if (modpackInfo.Dependencies.ContainsKey("forge")) return "forge";
        if (modpackInfo.Dependencies.ContainsKey("neoforge")) return "neoforge";

        return "fabric";
    }

    private async Task<string?> DownloadServerCoreAsync(string coreType, string mcVersion, ModpackIndex modpackInfo, string serverPath)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        httpClient.DefaultRequestHeaders.Add("User-Agent", "ZMSL-App/1.0");

        try
        {
            if (coreType == "fabric")
            {
                var loaderVersion = modpackInfo.Dependencies?.GetValueOrDefault("fabric-loader", "");
                if (string.IsNullOrEmpty(loaderVersion))
                {
                    // 获取最新 loader 版本
                    var loaderJson = await httpClient.GetStringAsync("https://meta.fabricmc.net/v2/versions/loader");
                    var loaders = JsonSerializer.Deserialize<List<FabricLoaderVersion>>(loaderJson);
                    loaderVersion = loaders?.FirstOrDefault(l => l.Stable)?.Version ?? loaders?.FirstOrDefault()?.Version;
                }

                // 获取最新 installer 版本
                var installerJson = await httpClient.GetStringAsync("https://meta.fabricmc.net/v2/versions/installer");
                var installers = JsonSerializer.Deserialize<List<FabricInstallerVersion>>(installerJson);
                var installerVersion = installers?.FirstOrDefault(i => i.Stable)?.Version ?? installers?.FirstOrDefault()?.Version;

                if (string.IsNullOrEmpty(loaderVersion) || string.IsNullOrEmpty(installerVersion))
                    return null;

                var jarUrl = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/{installerVersion}/server/jar";
                var jarFileName = $"fabric-server-mc.{mcVersion}-loader.{loaderVersion}-launcher.{installerVersion}.jar";
                var jarPath = Path.Combine(serverPath, jarFileName);

                var bytes = await httpClient.GetByteArrayAsync(jarUrl);
                await File.WriteAllBytesAsync(jarPath, bytes);

                return jarFileName;
            }
            else if (coreType == "forge" || coreType == "neoforge")
            {
                var jarFileName = await DownloadAndInstallForgeAsync(coreType, mcVersion, serverPath, null, CancellationToken.None);
                return jarFileName;
            }
            else if (coreType == "quilt")
            {
                var loaderVersion = modpackInfo.Dependencies?.GetValueOrDefault("quilt-loader", "");
                if (string.IsNullOrEmpty(loaderVersion))
                {
                    var loaderJson = await httpClient.GetStringAsync("https://meta.quiltmc.org/v3/versions/loader");
                    var loaders = JsonSerializer.Deserialize<List<QuiltLoaderVersion>>(loaderJson);
                    loaderVersion = loaders?.FirstOrDefault()?.Version;
                }

                var installerJson = await httpClient.GetStringAsync("https://meta.quiltmc.org/v3/versions/installer");
                var installers = JsonSerializer.Deserialize<List<QuiltInstallerVersion>>(installerJson);
                var installerVersion = installers?.FirstOrDefault()?.Version;

                if (string.IsNullOrEmpty(loaderVersion) || string.IsNullOrEmpty(installerVersion))
                    return null;

                var jarUrl = $"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}/{loaderVersion}/{installerVersion}/server/jar";
                var jarFileName = $"quilt-server-launch.jar";
                var jarPath = Path.Combine(serverPath, jarFileName);

                var bytes = await httpClient.GetByteArrayAsync(jarUrl);
                await File.WriteAllBytesAsync(jarPath, bytes);

                return jarFileName;
            }

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"下载服务端核心失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 下载并安装 Forge/NeoForge 服务端
    /// </summary>
    public async Task<string?> DownloadAndInstallForgeAsync(
        string coreType,
        string mcVersion,
        string serverPath,
        string? javaPath,
        CancellationToken cancellationToken,
        Action<string, double?>? progressCallback = null)
    {
        try
        {
            progressCallback?.Invoke($"正在下载 {coreType} 安装器...", 0);

            var downloadService = App.Services.GetRequiredService<ServerDownloadService>();
            var installerPath = await downloadService.DownloadForgeInstallerAsync(
                coreType, mcVersion, serverPath, "latest", cancellationToken);

            if (string.IsNullOrEmpty(installerPath))
            {
                progressCallback?.Invoke("下载安装器失败", 0);
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(javaPath))
            {
                javaPath = await DetectJavaAsync();
                if (string.IsNullOrEmpty(javaPath))
                {
                    progressCallback?.Invoke("未找到Java环境，无法完成安装", 0);
                    return null;
                }
            }

            progressCallback?.Invoke("正在安装 Forge/NeoForge...", 10);

            var result = await _forgeInstaller.InstallForgeAsync(
                serverPath,
                installerPath,
                javaPath,
                "MSL",
                cancellationToken);

            if (!result.Success)
            {
                progressCallback?.Invoke($"安装失败: {result.Message}", 0);
                return null;
            }

            progressCallback?.Invoke("安装完成!", 100);

            return result.LaunchJar;
        }
        catch (OperationCanceledException)
        {
            progressCallback?.Invoke("安装已取消", 0);
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServerManager] Forge安装失败: {ex.Message}");
            progressCallback?.Invoke($"安装失败: {ex.Message}", 0);
            return null;
        }
    }

    /// <summary>
    /// 检查服务器是否需要Forge安装流程
    /// </summary>
    public bool NeedsForgeInstall(LocalServer server)
    {
        if (server.CoreType?.Equals("forge", StringComparison.OrdinalIgnoreCase) == true ||
            server.CoreType?.Equals("neoforge", StringComparison.OrdinalIgnoreCase) == true)
        {
            var jarName = server.JarFileName.ToLowerInvariant();
            return jarName.Contains("installer");
        }
        return false;
    }

    /// <summary>
    /// 执行Forge安装并更新服务器配置
    /// </summary>
    public async Task<ForgeInstallResult> InstallForgeForServerAsync(
        LocalServer server,
        string? javaPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!NeedsForgeInstall(server))
        {
            return new ForgeInstallResult { Success = true, Message = "不需要安装" };
        }

        if (string.IsNullOrEmpty(javaPath))
        {
            javaPath = server.JavaPath ?? await DetectJavaAsync();
        }

        if (string.IsNullOrEmpty(javaPath))
        {
            return new ForgeInstallResult { Success = false, Message = "未找到Java环境" };
        }

        var installerPath = Path.Combine(server.ServerPath, server.JarFileName);
        if (!File.Exists(installerPath))
        {
            return new ForgeInstallResult { Success = false, Message = "安装器文件不存在" };
        }

        var result = await _forgeInstaller.InstallForgeAsync(
            server.ServerPath,
            installerPath,
            javaPath,
            "MSL",
            cancellationToken);

        if (result.Success && !string.IsNullOrEmpty(result.LaunchJar))
        {
            server.JarFileName = result.LaunchJar;
            if (!string.IsNullOrEmpty(result.MinecraftVersion))
            {
                server.MinecraftVersion = result.MinecraftVersion;
            }
            await UpdateServerAsync(server);
        }

        return result;
    }
}

/// <summary>
/// 整合包创建结果
/// </summary>
public class ModpackCreateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public LocalServer? Server { get; set; }
}

/// <summary>
/// 服务器目录验证结果
/// </summary>
public class ServerValidationResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DetectedJarFile { get; set; }
    public string? DetectedCoreType { get; set; }
    public string? DetectedMcVersion { get; set; }
    public int DetectedPort { get; set; }
    public string? SuggestedName { get; set; }
}

/// <summary>
/// 导入服务器结果
/// </summary>
public class ImportServerResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public LocalServer? Server { get; set; }
}

/// <summary>
/// Modrinth 整合包索引
/// </summary>
public class ModpackIndex
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("game")]
    public string? Game { get; set; }

    [JsonPropertyName("versionId")]
    public string? VersionId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }

    [JsonPropertyName("files")]
    public List<ModpackFile>? Files { get; set; }

    /// <summary>
    /// 标记是否为 CurseForge 服务端包（直接解压即可使用）
    /// </summary>
    [JsonIgnore]
    public bool IsCurseForgeServerPack { get; set; }
}

public class ModpackFile
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("hashes")]
    public Dictionary<string, string>? Hashes { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonPropertyName("downloads")]
    public List<string>? Downloads { get; set; }

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }
}

public class FabricLoaderVersion
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}

public class FabricInstallerVersion
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}

public class QuiltLoaderVersion
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public class QuiltInstallerVersion
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>
/// CurseForge 整合包 manifest.json 格式
/// </summary>
public class CurseForgeManifest
{
    [JsonPropertyName("minecraft")]
    public CurseForgeMinecraft? Minecraft { get; set; }

    [JsonPropertyName("manifestType")]
    public string? ManifestType { get; set; }

    [JsonPropertyName("manifestVersion")]
    public int ManifestVersion { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("files")]
    public List<CurseForgeManifestFile>? Files { get; set; }

    [JsonPropertyName("overrides")]
    public string? Overrides { get; set; }
}

public class CurseForgeMinecraft
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("modLoaders")]
    public List<CurseForgeModLoader>? ModLoaders { get; set; }
}

public class CurseForgeModLoader
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

public class CurseForgeManifestFile
{
    [JsonPropertyName("projectID")]
    public int ProjectId { get; set; }

    [JsonPropertyName("fileID")]
    public int FileId { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public class JavaInfo
{
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DisplayName => $"Java {Version}";
}

public class ServerOutputEventArgs : EventArgs
{
    public int ServerId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

public class ServerStatusEventArgs : EventArgs
{
    public int ServerId { get; set; }
    public bool IsRunning { get; set; }
}

public class ServerCrashEventArgs : EventArgs
{
    public int ServerId { get; set; }
    public string CrashMessage { get; set; } = string.Empty;
    public string FullLog { get; set; } = string.Empty;
    public string DetectedKeyword { get; set; } = string.Empty;
    public string StartupCommand { get; set; } = string.Empty;
    public List<string> PluginList { get; set; } = new();
    public List<string> ModList { get; set; } = new();
}

public class ForgeInstallProgressEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
    public double? Progress { get; set; }
    public ForgeInstallerService.InstallStage Stage { get; set; }
}

/// <summary>
/// 崩溃分析数据，用于传递给日志分析页面
/// </summary>
public class CrashAnalysisData
{
    public string FullLog { get; set; } = string.Empty;
    public string StartupCommand { get; set; } = string.Empty;
    public List<string> PluginList { get; set; } = new();
    public List<string> ModList { get; set; } = new();

    /// <summary>
    /// 格式化为AI分析的完整内容
    /// </summary>
    public string ToAnalysisContent()
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== 启动命令 ===");
        sb.AppendLine(StartupCommand);
        sb.AppendLine();

        if (PluginList.Count > 0)
        {
            sb.AppendLine("=== 插件列表 ===");
            foreach (var plugin in PluginList)
            {
                sb.AppendLine($"- {plugin}");
            }
            sb.AppendLine();
        }

        if (ModList.Count > 0)
        {
            sb.AppendLine("=== Mod列表 ===");
            foreach (var mod in ModList)
            {
                sb.AppendLine($"- {mod}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("=== 服务器日志 ===");
        sb.AppendLine(FullLog);

        return sb.ToString();
    }
}
