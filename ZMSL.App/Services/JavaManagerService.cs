using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ZMSL.App.Services;

public class JavaManagerService
{
    private readonly HttpClient _httpClient;
    private readonly string _javaInstallPath;

    public JavaManagerService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.mslmc.cn/v3/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ZMSL Client/1.0");
        
        // Java安装路径
        _javaInstallPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "java");
        Directory.CreateDirectory(_javaInstallPath);
    }

    /// <summary>
    /// 根据MC版本获取推荐的Java版本
    /// </summary>
    public static int GetRecommendedJavaVersion(string mcVersion)
    {
        if (string.IsNullOrWhiteSpace(mcVersion) || mcVersion == "latest") return 21;

        var normalizedVersion = mcVersion.Trim();
        var versionParts = normalizedVersion.Split('.');
        if (versionParts.Length < 2) return 21;

        if (!int.TryParse(versionParts[0], out int majorVersion) ||
            !int.TryParse(versionParts[1], out int minorVersion))
        {
            return 21;
        }

        // 新版版本号（如 26.1、26.2）：26.1+ -> Java 25
        if (majorVersion >= 26)
        {
            return minorVersion >= 1 ? 25 : 21;
        }

        // 兼容旧版 1.x 版本号（例如: 1.16.5, 1.20.4）
        if (majorVersion != 1) return 21;

        // 1.16以下 -> Java 8
        if (minorVersion < 16) return 8;

        // 1.16.x -> Java 16
        if (minorVersion == 16) return 16;

        // 1.17.x - 1.20.4 -> Java 17
        if (minorVersion >= 17 && minorVersion <= 20)
        {
            // 检查是否是1.20.5+
            if (minorVersion == 20 && versionParts.Length >= 3)
            {
                if (int.TryParse(versionParts[2], out int patchVersion) && patchVersion >= 5)
                {
                    return 21; // 1.20.5+ -> Java 21
                }
            }
            return 17;
        }

        // 1.21+ -> Java 21
        return 21;
    }

    /// <summary>
    /// 检测系统已安装的Java（多线程：JAVA_HOME、PATH、本地目录并行检测）
    /// </summary>
    public async Task<List<JavaInstallation>> DetectInstalledJavaAsync(IProgress<JavaInstallation>? progress = null)
    {
        System.Diagnostics.Debug.WriteLine("[JavaManager] 开始检测系统已安装的Java");

        var javaList = new System.Collections.Concurrent.ConcurrentBag<JavaInstallation>();
        var seenPaths = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        // 辅助方法：添加并报告
        void AddAndReport(JavaInstallation java)
        {
            if (seenPaths.TryAdd(java.Path, 0))
            {
                javaList.Add(java);
                progress?.Report(java);
                System.Diagnostics.Debug.WriteLine($"[JavaManager] Found Java: {java.Path} ({java.Version})");
            }
        }

        // 并行执行：JAVA_HOME、PATH、本地 java 目录
        var javaHomeTask = Task.Run(async () =>
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var javaExe = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(javaExe))
                {
                    var version = await GetJavaVersionAsync(javaExe);
                    if (version != null)
                    {
                        AddAndReport(new JavaInstallation { Path = javaExe, Version = version.Value, Source = "JAVA_HOME" });
                    }
                }
            }
        });

        var pathJavaTask = Task.Run(async () =>
        {
            var pathJava = await GetJavaFromPathAsync();
            if (pathJava != null)
            {
                AddAndReport(pathJava);
            }
        });

        var localJavasTask = Task.Run(async () =>
        {
            var localJavas = await ScanLocalJavaAsync();
            foreach (var java in localJavas)
            {
                AddAndReport(java);
            }
        });

        var fullScanTask = ScanAllDrivesForJavaAsync(progress, seenPaths, javaList);

        await Task.WhenAll(javaHomeTask, pathJavaTask, localJavasTask, fullScanTask);

        System.Diagnostics.Debug.WriteLine($"[JavaManager] 总共检测到 {javaList.Count} 个Java安装");
        return javaList.ToList();
    }

    /// <summary>
    /// 获取或下载指定版本的Java
    /// </summary>
    public async Task<string?> GetOrDownloadJavaAsync(int version, IProgress<double>? progress = null)
    {
        System.Diagnostics.Debug.WriteLine($"[JavaManager] 开始获取 Java {version}");
        
        // 先检查是否已安装
        var installed = await DetectInstalledJavaAsync();
        System.Diagnostics.Debug.WriteLine($"[JavaManager] 检测到 {installed.Count} 个已安装的Java");
        
        var matching = installed.FirstOrDefault(j => j.Version == version);
        if (matching != null)
        {
            System.Diagnostics.Debug.WriteLine($"[JavaManager] 找到匹配的Java {version}: {matching.Path} (来源: {matching.Source})");
            return matching.Path;
        }

        // 检查本地下载的Java
        var localPath = Path.Combine(_javaInstallPath, $"jdk{version}");
        var javaExe = Path.Combine(localPath, "bin", "java.exe");
        if (File.Exists(javaExe))
        {
            System.Diagnostics.Debug.WriteLine($"[JavaManager] 找到本地已下载的Java {version}: {javaExe}");
            return javaExe;
        }

        // 下载Java
        System.Diagnostics.Debug.WriteLine($"[JavaManager] 未找到Java {version}，开始下载...");
        try
        {
            var downloadUrl = await GetJavaDownloadUrlAsync(version);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                System.Diagnostics.Debug.WriteLine($"[JavaManager] 无法获取Java {version}的下载地址");
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"[JavaManager] 开始下载Java {version} from {downloadUrl}");
            await DownloadAndExtractJavaAsync(downloadUrl, localPath, progress);
            
            if (File.Exists(javaExe))
            {
                System.Diagnostics.Debug.WriteLine($"[JavaManager] Java {version} 下载并安装成功: {javaExe}");
                return javaExe;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JavaManager] 下载Java失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 获取Java下载地址
    /// </summary>
    private async Task<string?> GetJavaDownloadUrlAsync(int version)
    {
        try
        {
            var os = "windows";
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : "arm64";
            
            var response = await _httpClient.GetAsync($"download/jdk/{version}?os={os}&arch={arch}");
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (data.TryGetProperty("data", out var dataObj))
            {
                if (dataObj.TryGetProperty("url", out var urlProp))
                {
                    return urlProp.GetString();
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JavaManager] 获取下载地址失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 下载并解压Java
    /// </summary>
    private async Task DownloadAndExtractJavaAsync(string url, string targetPath, IProgress<double>? progress)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"java_{Guid.NewGuid()}.zip");

        try
        {
            // 下载
            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var fileStream = File.Create(tempZip))
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    var lastReportTime = DateTime.MinValue;
                    
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                        downloadedBytes += bytesRead;
                        
                        var now = DateTime.Now;
                        if (totalBytes > 0 && ((now - lastReportTime).TotalMilliseconds > 100 || downloadedBytes == totalBytes))
                        {
                            lastReportTime = now;
                            progress?.Report((double)downloadedBytes / totalBytes * 100);
                        }
                    }
                }
            }

            // 解压 (使用 Task.Run 避免阻塞 UI 线程)
            await Task.Run(() =>
            {
                Directory.CreateDirectory(targetPath);
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, targetPath, true);

                // 处理解压后的目录结构（JDK通常会有一层额外的目录）
                var subdirs = Directory.GetDirectories(targetPath);
                if (subdirs.Length == 1)
                {
                    var subdir = subdirs[0];
                    foreach (var item in Directory.GetFileSystemEntries(subdir))
                    {
                        var destName = Path.Combine(targetPath, Path.GetFileName(item));
                        if (Directory.Exists(item))
                        {
                            Directory.Move(item, destName);
                        }
                        else
                        {
                            File.Move(item, destName);
                        }
                    }
                    Directory.Delete(subdir);
                }
            });
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// 获取PATH中的Java
    /// </summary>
    private async Task<JavaInstallation?> GetJavaFromPathAsync()
    {
        try
        {
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "java",
                Arguments = "-version",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process == null) return null;

            var output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var version = ParseJavaVersion(output);
            if (version.HasValue)
            {
                return new JavaInstallation
                {
                    Path = "java", // PATH中的java
                    Version = version.Value,
                    Source = "PATH"
                };
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 扫描本地安装的Java（并行优化）
    /// </summary>
    private async Task<List<JavaInstallation>> ScanLocalJavaAsync()
    {
        var javaList = new System.Collections.Concurrent.ConcurrentBag<JavaInstallation>();

        // 扫描本地java目录
        if (Directory.Exists(_javaInstallPath))
        {
            var dirs = Directory.GetDirectories(_javaInstallPath);

            // 并行检测所有Java版本
            var tasks = dirs.Select(async dir =>
            {
                var javaExe = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(javaExe))
                {
                    var version = await GetJavaVersionAsync(javaExe);
                    if (version != null)
                    {
                        javaList.Add(new JavaInstallation
                        {
                            Path = javaExe,
                            Version = version.Value,
                            Source = "Local"
                        });
                    }
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }

        return javaList.ToList();
    }

    /// <summary>
    /// 全盘扫描 Java (多线程优化)
    /// </summary>
    private async Task<List<JavaInstallation>> ScanAllDrivesForJavaAsync(IProgress<JavaInstallation>? progress = null, 
        System.Collections.Concurrent.ConcurrentDictionary<string, byte>? seenPaths = null,
        System.Collections.Concurrent.ConcurrentBag<JavaInstallation>? sharedList = null)
    {
        var result = sharedList ?? new System.Collections.Concurrent.ConcurrentBag<JavaInstallation>();
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady);

        // 1. 收集所有待扫描的根目录和一级子目录，以实现更细粒度的并行
        var workItems = new System.Collections.Concurrent.ConcurrentBag<string>();

        // 辅助方法：添加并报告（仅用于此方法的本地扫描，如果提供了sharedList，则由调用者管理去重，或者我们在这里管理）
        // 为了简化，如果提供了 seenPaths，我们就用它去重并报告
        void AddResult(JavaInstallation java)
        {
            if (seenPaths != null)
            {
                if (seenPaths.TryAdd(java.Path, 0))
                {
                    result.Add(java);
                    progress?.Report(java);
                }
            }
            else
            {
                result.Add(java);
            }
        }

        await Task.Run(() =>
        {
            try 
            {
                Parallel.ForEach(drives, drive =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[JavaManager] Scanning drive root: {drive.Name}");
                        // 检查根目录本身
                        CheckDirectoryForJava(drive.RootDirectory.FullName, AddResult);

                        // 枚举一级子目录并添加到任务队列
                        foreach (var dir in Directory.EnumerateDirectories(drive.RootDirectory.FullName))
                        {
                            if (IsSystemDirectory(dir)) continue;
                            workItems.Add(dir);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error scanning drive root {drive.Name}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"Parallel drive scan error: {ex.Message}");
            }
        });

        System.Diagnostics.Debug.WriteLine($"[JavaManager] Collected {workItems.Count} top-level directories to scan.");

        // 2. 并行处理这些目录
        // 使用较高的并发度来利用 I/O 等待时间，但限制在合理范围内
        var parallelOptions = new ParallelOptions 
        { 
            MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount * 2) 
        };

        if (!workItems.IsEmpty)
        {
            // 使用 Task.Run 确保在线程池中执行，不阻塞调用线程
            await Task.Run(() =>
            {
                try 
                {
                    Parallel.ForEach(workItems, parallelOptions, dir =>
                    {
                        // 增加取消检查点，如果需要支持取消
                        ScanDirectoryRecursive(dir, AddResult);
                    });
                }
                catch (Exception ex)
                {
                     System.Diagnostics.Debug.WriteLine($"Parallel recursive scan error: {ex.Message}");
                }
            });
        }

        return result.ToList();
    }

    private void CheckDirectoryForJava(string directory, Action<JavaInstallation> onFound)
    {
        try
        {
            // Check bin/java.exe
            var javaExe = Path.Combine(directory, "bin", "java.exe");
            if (File.Exists(javaExe))
            {
                 System.Diagnostics.Debug.WriteLine($"[JavaManager] Found potential Java: {javaExe}");
                 var version = GetJavaVersionAsync(javaExe).GetAwaiter().GetResult();
                 if (version != null)
                 {
                     System.Diagnostics.Debug.WriteLine($"[JavaManager] Confirmed Java {version.Value}: {javaExe}");
                     onFound(new JavaInstallation
                     {
                         Path = javaExe,
                         Version = version.Value,
                         Source = "FullScan"
                     });
                 }
                 else
                 {
                     System.Diagnostics.Debug.WriteLine($"[JavaManager] Failed to get version for: {javaExe}");
                 }
            }
            else
            {
                // Check java.exe directly
                javaExe = Path.Combine(directory, "java.exe");
                if (File.Exists(javaExe))
                {
                     System.Diagnostics.Debug.WriteLine($"[JavaManager] Found potential Java (root): {javaExe}");
                     var version = GetJavaVersionAsync(javaExe).GetAwaiter().GetResult();
                     if (version != null)
                     {
                         System.Diagnostics.Debug.WriteLine($"[JavaManager] Confirmed Java {version.Value}: {javaExe}");
                         onFound(new JavaInstallation
                         {
                             Path = javaExe,
                             Version = version.Value,
                             Source = "FullScan"
                         });
                     }
                     else
                     {
                         System.Diagnostics.Debug.WriteLine($"[JavaManager] Failed to get version for: {javaExe}");
                     }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JavaManager] Error checking dir {directory}: {ex.Message}");
        }
    }

    private void ScanDirectoryRecursive(string directory, Action<JavaInstallation> onFound)
    {
        // Skip common system directories to save time
        if (IsSystemDirectory(directory)) return;

        try
        {
            // Check if this directory is a reparse point (symlink/junction) to avoid loops
            var dirInfo = new DirectoryInfo(directory);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                return;
            }

            // Check current directory for java.exe
            CheckDirectoryForJava(directory, onFound);

            // Recursively scan subdirectories
            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                 ScanDirectoryRecursive(subDir, onFound);
            }
        }
        catch (UnauthorizedAccessException) { } // Ignore permission errors
        catch (PathTooLongException) { } // Ignore path too long
        catch (Exception) { }
    }

    private bool IsSystemDirectory(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) return false; // Root

        // Skip Windows, Recycle Bin, System Volume Information, etc.
        if (name.Equals("Windows", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) return true;
        // if (name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase)) return true; // Do not skip ProgramData
        
        // Skip hidden folders starting with .
        if (name.StartsWith(".")) return true;

        return false;
    }

    /// <summary>
    /// 获取Java版本
    /// </summary>
    private async Task<int?> GetJavaVersionAsync(string javaPath)
    {
        try
        {
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process == null) return null;

            var output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return ParseJavaVersion(output);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析Java版本号
    /// </summary>
    private int? ParseJavaVersion(string versionOutput)
    {
        // 匹配 "version "1.8.0_xxx"" 或 "version "21.0.1""
        var match = System.Text.RegularExpressions.Regex.Match(versionOutput, @"version ""(\d+)\.?(\d+)?");
        if (match.Success)
        {
            var major = match.Groups[1].Value;
            if (major == "1" && match.Groups.Count > 2)
            {
                // Java 8 及以下: "1.8.0" -> 8
                if (int.TryParse(match.Groups[2].Value, out int minor))
                {
                    return minor;
                }
            }
            else
            {
                // Java 9+: "21.0.1" -> 21
                if (int.TryParse(major, out int version))
                {
                    return version;
                }
            }
        }

        return null;
    }
}

public class JavaInstallation
{
    public string Path { get; set; } = "";
    public int Version { get; set; }
    public string Source { get; set; } = "";
    
    // 转换为数据库模型
    public ZMSL.App.Models.JavaInfo ToModel()
    {
        return new ZMSL.App.Models.JavaInfo
        {
            Path = this.Path,
            Version = this.Version,
            Source = this.Source,
            DetectedAt = DateTime.Now,
            IsValid = true
        };
    }
}
