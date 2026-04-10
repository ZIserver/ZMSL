using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZMSL.App.Services;

public class ForgeInstallerService
{
    public class InstallProgressEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public double? Progress { get; set; }
        public InstallStage Stage { get; set; }
    }

    public enum InstallStage
    {
        Preparing,
        Extracting,
        DownloadingVanilla,
        DownloadingLibraries,
        Processing,
        Building,
        Cleaning,
        Completed,
        Failed
    }

    public event EventHandler<InstallProgressEventArgs>? ProgressChanged;

    private string _installBasePath = string.Empty;
    private string _installerPath = string.Empty;
    private string _installMcVersion = "";
    private uint _installVersionType = 5;
    private string _installName = "NeoForge";
    private string _mirrorSource = "MSL";

    private static readonly string ClassPathSeparator = OperatingSystem.IsWindows() ? ";" : ":";

    public async Task<ForgeInstallResult> InstallForgeAsync(
        string serverPath,
        string installerJarPath,
        string javaPath,
        string mirrorSource = "MSL",
        CancellationToken cancellationToken = default)
    {
        try
        {
            _installBasePath = serverPath;
            _installerPath = installerJarPath;
            _mirrorSource = mirrorSource;
            _installName = installerJarPath.Contains("neo", StringComparison.OrdinalIgnoreCase) ? "NeoForge" : "Forge";

            ReportProgress($"开始执行 {_installName} 安装进程...", InstallStage.Preparing, 0);

            await PrepareInstallEnvironment(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress($"正在解压 {_installName} 安装器...", InstallStage.Extracting, 5);
            var extractSuccess = await ExtractInstallerAsync(cancellationToken);
            if (!extractSuccess)
            {
                return new ForgeInstallResult { Success = false, Message = "解压安装器失败" };
            }
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress("正在读取安装信息...", InstallStage.Preparing, 10);
            var installInfo = await ReadInstallProfileAsync(cancellationToken);
            if (installInfo == null)
            {
                return new ForgeInstallResult { Success = false, Message = "无法读取安装配置" };
            }
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress($"正在下载 {_installMcVersion} 原版服务端...", InstallStage.DownloadingVanilla, 15);
            var vanillaSuccess = await DownloadVanillaServerAsync(cancellationToken);
            if (!vanillaSuccess)
            {
                return new ForgeInstallResult { Success = false, Message = "下载原版服务端失败" };
            }
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress($"正在下载 {_installName} 运行库文件...", InstallStage.DownloadingLibraries, 30);
            var librariesSuccess = await DownloadLibrariesAsync(cancellationToken);
            if (!librariesSuccess)
            {
                return new ForgeInstallResult { Success = false, Message = "下载运行库文件失败" };
            }
            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress("正在处理安装文件...", InstallStage.Processing, 60);
            await ProcessInstallerFilesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (_installVersionType < 5)
            {
                ReportProgress($"正在执行 {_installName} 构建任务...", InstallStage.Building, 70);
                var buildSuccess = await ExecuteBuildTasksAsync(javaPath, cancellationToken);
                if (!buildSuccess)
                {
                    return new ForgeInstallResult { Success = false, Message = "执行构建任务失败" };
                }
            }

            ReportProgress("正在清理临时文件...", InstallStage.Cleaning, 90);
            await CleanupAsync(cancellationToken);

            ReportProgress($"{_installName} 安装完成！", InstallStage.Completed, 100);

            var launchJar = FindLaunchJar();
            return new ForgeInstallResult
            {
                Success = true,
                Message = "安装成功",
                MinecraftVersion = _installMcVersion,
                LaunchJar = launchJar
            };
        }
        catch (OperationCanceledException)
        {
            return new ForgeInstallResult { Success = false, Message = "安装已取消" };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForgeInstaller] 安装失败: {ex.Message}");
            ReportProgress($"安装失败: {ex.Message}", InstallStage.Failed, 0);
            return new ForgeInstallResult { Success = false, Message = ex.Message };
        }
    }

    private void ReportProgress(string message, InstallStage stage, double? progress)
    {
        Debug.WriteLine($"[ForgeInstaller] {message}");
        ProgressChanged?.Invoke(this, new InstallProgressEventArgs
        {
            Message = message,
            Stage = stage,
            Progress = progress
        });
    }

    private async Task PrepareInstallEnvironment(CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var librariesPath = Path.Combine(_installBasePath, "libraries");
            if (Directory.Exists(librariesPath))
            {
                try
                {
                    Directory.Delete(librariesPath, true);
                    ReportProgress("已删除旧的 libraries 文件夹", InstallStage.Preparing, 1);
                }
                catch { }
            }

            var tempPath = Path.Combine(_installBasePath, "temp");
            if (!Directory.Exists(tempPath))
            {
                Directory.CreateDirectory(tempPath);
            }

            foreach (var file in Directory.GetFiles(_installBasePath, "*.jar"))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                    !fileName.Equals(Path.GetFileName(_installerPath), StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(file);
                        ReportProgress($"已删除旧文件: {fileName}", InstallStage.Preparing, 2);
                    }
                    catch { }
                }
            }
        }, ct);
    }

    private async Task<bool> ExtractInstallerAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                var tempPath = Path.Combine(_installBasePath, "temp");
                ZipFile.ExtractToDirectory(_installerPath, tempPath, true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeInstaller] 解压失败: {ex.Message}");
                return false;
            }
        }, ct);
    }

    private async Task<JsonObject?> ReadInstallProfileAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                var profilePath = Path.Combine(_installBasePath, "temp", "install_profile.json");
                if (!File.Exists(profilePath)) return null;

                var json = File.ReadAllText(profilePath);
                var profile = JsonNode.Parse(json)?.AsObject();
                if (profile == null) return null;

                _installMcVersion = profile["minecraft"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(_installMcVersion))
                {
                    _installMcVersion = profile["install"]?["minecraft"]?.ToString() ?? "";
                }

                if (string.IsNullOrEmpty(_installMcVersion))
                {
                    return null;
                }

                _installVersionType = DetermineVersionType(_installMcVersion);
                Debug.WriteLine($"[ForgeInstaller] MC版本: {_installMcVersion}, 类型: {_installVersionType}");

                return profile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeInstaller] 读取安装配置失败: {ex.Message}");
                return null;
            }
        }, ct);
    }

    private uint DetermineVersionType(string mcVersion)
    {
        if (CompareVersions(mcVersion, "1.20.3") >= 0)
            return 1;
        if (CompareVersions(mcVersion, "1.18") >= 0 && CompareVersions(mcVersion, "1.20.3") < 0)
            return 2;
        if (CompareVersions(mcVersion, "1.17.1") == 0)
            return 3;
        if (CompareVersions(mcVersion, "1.12") >= 0 && CompareVersions(mcVersion, "1.17.1") < 0)
            return 4;
        return 5;
    }

    private int CompareVersions(string v1, string v2)
    {
        var parts1 = ExtractVersionNumbers(v1);
        var parts2 = ExtractVersionNumbers(v2);

        var maxLen = Math.Max(parts1.Count, parts2.Count);
        for (int i = 0; i < maxLen; i++)
        {
            var p1 = i < parts1.Count ? parts1[i] : 0;
            var p2 = i < parts2.Count ? parts2[i] : 0;
            if (p1 > p2) return 1;
            if (p1 < p2) return -1;
        }

        var v1IsSnapshot = v1.Contains("-") || Regex.IsMatch(v1, "[a-zA-Z]");
        var v2IsSnapshot = v2.Contains("-") || Regex.IsMatch(v2, "[a-zA-Z]");
        if (!v1IsSnapshot && v2IsSnapshot) return 1;
        if (v1IsSnapshot && !v2IsSnapshot) return -1;

        return 0;
    }

    private List<int> ExtractVersionNumbers(string version)
    {
        var numbers = new List<int>();
        foreach (Match match in Regex.Matches(version, @"\d+"))
        {
            if (int.TryParse(match.Value, out var num))
                numbers.Add(num);
        }
        return numbers;
    }

    private async Task<bool> DownloadVanillaServerAsync(CancellationToken ct)
    {
        try
        {
            var isSnapshot = _installMcVersion.Contains("snapshot", StringComparison.OrdinalIgnoreCase);
            var apiUrl = $"https://api.mslmc.cn/v3/download/server/vanilla{(isSnapshot ? "-snapshot" : "")}/{_installMcVersion}";

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "ZMSL-App/1.0");

            var response = await httpClient.GetStringAsync(apiUrl, ct);
            var json = JsonNode.Parse(response);
            var vanillaUrl = json?["data"]?["url"]?.ToString();

            if (string.IsNullOrEmpty(vanillaUrl))
            {
                return false;
            }

            vanillaUrl = ApplyMirror(vanillaUrl);

            string serverJarPath;
            if (_installVersionType <= 3)
            {
                var profilePath = Path.Combine(_installBasePath, "temp", "install_profile.json");
                var profileJson = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject();
                var serverJarPathRaw = profileJson?["serverJarPath"]?.ToString() ?? "";
                serverJarPath = ReplaceVariables(serverJarPathRaw);
            }
            else
            {
                serverJarPath = Path.Combine(_installBasePath, $"minecraft_server.{_installMcVersion}.jar");
            }

            await DownloadFileWithProgressAsync(vanillaUrl, serverJarPath, "原版服务端", ct);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForgeInstaller] 下载原版服务端失败: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> DownloadLibrariesAsync(CancellationToken ct)
    {
        try
        {
            var profilePath = Path.Combine(_installBasePath, "temp", "install_profile.json");
            var profileJson = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject();
            if (profileJson == null) return false;

            var downloadTasks = new List<Task>();
            var failedCount = 0;
            var lockObj = new object();

            // 使用信号量限制并发数
            var semaphore = new SemaphoreSlim(8); // 最多 8 个并发下载

            if (_installVersionType != 5)
            {
                var versionPath = Path.Combine(_installBasePath, "temp", "version.json");
                JsonObject? versionJson = null;
                if (File.Exists(versionPath))
                {
                    versionJson = JsonNode.Parse(File.ReadAllText(versionPath))?.AsObject();
                }

                var addedPaths = new HashSet<string>();

                if (versionJson?["libraries"] is JsonArray versionLibs)
                {
                    foreach (var lib in versionLibs.OfType<JsonObject>())
                    {
                        var task = ProcessLibraryWithSemaphoreAsync(lib, addedPaths, semaphore, () => { lock (lockObj) { failedCount++; } }, ct);
                        downloadTasks.Add(task);
                    }
                }

                if (profileJson["libraries"] is JsonArray profileLibs)
                {
                    foreach (var lib in profileLibs.OfType<JsonObject>())
                    {
                        var task = ProcessLibraryWithSemaphoreAsync(lib, addedPaths, semaphore, () => { lock (lockObj) { failedCount++; } }, ct);
                        downloadTasks.Add(task);
                    }
                }
            }
            else
            {
                if (profileJson["versionInfo"]?["libraries"] is JsonArray libs)
                {
                    foreach (var lib in libs.OfType<JsonObject>())
                    {
                        var task = ProcessLegacyLibraryWithSemaphoreAsync(lib, semaphore, () => { lock (lockObj) { failedCount++; } }, ct);
                        downloadTasks.Add(task);
                    }
                }
            }

            await Task.WhenAll(downloadTasks);

            if (failedCount > 0)
            {
                Debug.WriteLine($"[ForgeInstaller] 警告: {failedCount} 个库文件下载失败");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForgeInstaller] 下载运行库失败: {ex.Message}");
            return false;
        }
    }

    private async Task ProcessLibraryWithSemaphoreAsync(JsonObject lib, HashSet<string> addedPaths, SemaphoreSlim semaphore, Action onFailed, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            await ProcessLibraryAsync(lib, addedPaths, ct);
        }
        catch
        {
            onFailed();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task ProcessLegacyLibraryWithSemaphoreAsync(JsonObject lib, SemaphoreSlim semaphore, Action onFailed, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            await ProcessLegacyLibraryAsync(lib, ct);
        }
        catch
        {
            onFailed();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task ProcessLibraryAsync(JsonObject lib, HashSet<string> addedPaths, CancellationToken ct)
    {
        try
        {
            var artifact = lib["downloads"]?["artifact"];
            if (artifact == null) return;

            var path = artifact["path"]?.ToString();
            if (string.IsNullOrEmpty(path)) return;

            // 查重
            if (!addedPaths.Add(path)) return;

            // 过滤不需要的文件
            if (path.Contains("-client.jar"))
            {
                Debug.WriteLine($"[ForgeInstaller] 跳过客户端JAR: {path}");
                return;
            }

            // 注意：不要跳过 mcp_config 中的 srg2off.jar，它是运行时需要的
            // 只跳过其他临时处理文件
            if (path.Contains("-srg2on.jar"))
            {
                Debug.WriteLine($"[ForgeInstaller] 跳过临时处理文件: {path}");
                return;
            }

            if (path.EndsWith(".zip") || path.EndsWith(".txt") || path.EndsWith(".pack.mcmeta"))
            {
                Debug.WriteLine($"[ForgeInstaller] 跳过非JAR文件: {path}");
                return;
            }

            var destPath = Path.Combine(_installBasePath, "libraries", path);

            // 构建多个下载源
            var urls = BuildDownloadUrls(lib, path);

            // 多源重试下载
            var downloaded = false;
            foreach (var tryUrl in urls)
            {
                try
                {
                    await DownloadFileWithProgressAsync(tryUrl, destPath, path, ct);
                    downloaded = true;
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ForgeInstaller] 尝试 {tryUrl} 失败: {ex.Message}");
                }
            }

            if (!downloaded)
            {
                Debug.WriteLine($"[ForgeInstaller] 所有下载源均失败: {path}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForgeInstaller] 处理库文件失败: {ex.Message}");
        }
    }

    private List<string> BuildDownloadUrls(JsonObject lib, string path)
    {
        var urls = new List<string>();
        var name = lib["name"]?.ToString();
        var originalUrl = lib["downloads"]?["artifact"]?["url"]?.ToString();

        // 优先使用 JSON 中的 URL
        if (!string.IsNullOrEmpty(originalUrl) && originalUrl.StartsWith("http"))
        {
            urls.Add(originalUrl);
        }

        // 根据路径特征构建镜像源 URL
        if (!string.IsNullOrEmpty(name))
        {
            var namePath = NameToPath(name);
            
            if (path.Contains("neoforged"))
            {
                urls.Add($"https://maven.neoforged.net/{namePath}");
                if (_mirrorSource == "MSL")
                {
                    urls.Add($"https://neoforge.mirrors.mslmc.cn/{namePath}");
                }
            }
            else if (path.Contains("minecraftforge"))
            {
                urls.Add($"https://maven.minecraftforge.net/{namePath}");
                if (_mirrorSource == "MSL")
                {
                    urls.Add($"https://forge-maven.mirrors.mslmc.cn/{namePath}");
                }
            }
            else if (path.Contains("mojang") || path.Contains("minecraft"))
            {
                urls.Add($"https://libraries.minecraft.net/{namePath}");
                if (_mirrorSource == "MSL")
                {
                    urls.Add($"https://mclibs.mirrors.mslmc.cn/{namePath}");
                }
            }
            else if (path.Contains("spongepowered") || path.Contains("mixin"))
            {
                urls.Add($"https://repo1.maven.org/maven2/{namePath}");
                urls.Add($"https://maven.neoforged.net/{namePath}");
            }
            else if (path.Contains("de/oceanlabs") || path.Contains("mcp_config"))
            {
                urls.Add($"https://maven.neoforged.net/{namePath}");
                urls.Add($"https://maven.minecraftforge.net/{namePath}");
            }
            else
            {
                urls.Add($"https://repo1.maven.org/maven2/{namePath}");
                urls.Add($"https://maven.minecraftforge.net/{namePath}");
            }
        }

        return urls.Distinct().ToList();
    }

    private async Task ProcessLegacyLibraryAsync(JsonObject lib, CancellationToken ct)
    {
        try
        {
            var name = lib["name"]?.ToString();
            if (string.IsNullOrEmpty(name)) return;

            var libPath = NameToPath(name);
            var libUrl = lib["url"]?.ToString();

            string url;
            if (string.IsNullOrEmpty(libUrl))
            {
                url = $"https://maven.minecraftforge.net/{libPath}";
            }
            else
            {
                url = $"{libUrl.TrimEnd('/')}/{libPath}";
            }

            url = ApplyMirror(url);
            var destPath = Path.Combine(_installBasePath, "libraries", libPath);

            await DownloadFileWithProgressAsync(url, destPath, libPath, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForgeInstaller] 处理旧版库文件失败: {ex.Message}");
        }
    }

    private async Task ProcessInstallerFilesAsync(CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                if (_installVersionType == 1)
                {
                    var profilePath = Path.Combine(_installBasePath, "temp", "install_profile.json");
                    var profileJson = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject();
                    var pathName = profileJson?["path"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(pathName))
                    {
                        var namePath = NameToPath(pathName);
                        var srcPath = Path.Combine(_installBasePath, "temp", "maven", namePath);
                        
                        // 复制到 libraries 文件夹
                        var libDestPath = Path.Combine(_installBasePath, "libraries", namePath);
                        if (File.Exists(srcPath) && !File.Exists(libDestPath))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(libDestPath)!);
                            File.Copy(srcPath, libDestPath, true);
                            Debug.WriteLine($"[ForgeInstaller] 复制 shim.jar 到 libraries: {namePath}");
                        }
                        
                        // 同时复制到根目录（某些版本需要）
                        var fileName = Path.GetFileName(namePath);
                        var rootDestPath = Path.Combine(_installBasePath, fileName);
                        if (File.Exists(srcPath) && !File.Exists(rootDestPath))
                        {
                            File.Copy(srcPath, rootDestPath, true);
                            Debug.WriteLine($"[ForgeInstaller] 复制 shim.jar 到根目录: {fileName}");
                        }
                    }
                }
                else if (_installVersionType == 4)
                {
                    var sourceDir = Path.Combine(_installBasePath, "temp", "maven", "net");
                    var targetDir = Path.Combine(_installBasePath, "libraries", "net");
                    
                    if (Directory.Exists(sourceDir))
                    {
                        MergeDirectories(sourceDir, targetDir);
                        CopyJarFiles(sourceDir, _installBasePath);
                        Debug.WriteLine("[ForgeInstaller] 合并 NeoForge 库文件完成");
                    }
                }
                else if (_installVersionType == 5)
                {
                    var tempDir = Path.Combine(_installBasePath, "temp");
                    CopyJarFiles(tempDir, _installBasePath, false);
                    Debug.WriteLine("[ForgeInstaller] 复制旧版库文件完成");
                }

                if (_installVersionType <= 2)
                {
                    var serverJarPath = _installVersionType == 1
                        ? FindServerJarPath()
                        : Path.Combine(_installBasePath, $"minecraft_server.{_installMcVersion}.jar");

                    if (!File.Exists(serverJarPath)) return;

                    var vanillaTempPath = Path.Combine(_installBasePath, "temp", "vanilla");
                    ZipFile.ExtractToDirectory(serverJarPath, vanillaTempPath, true);

                    var vanillaLibDir = Path.Combine(vanillaTempPath, "META-INF", "libraries");
                    if (Directory.Exists(vanillaLibDir))
                    {
                        foreach (var file in Directory.GetFiles(vanillaLibDir))
                        {
                            var dest = Path.Combine(_installBasePath, Path.GetFileName(file));
                            File.Copy(file, dest, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeInstaller] 处理安装文件失败: {ex.Message}");
            }
        }, ct);
    }

    private void MergeDirectories(string source, string target)
    {
        if (!Directory.Exists(target))
            Directory.CreateDirectory(target);

        foreach (var dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var newDir = dirPath.Replace(source, target);
            if (!Directory.Exists(newDir))
                Directory.CreateDirectory(newDir);
        }

        foreach (var filePath in Directory.GetFiles(source, "*.jar", SearchOption.AllDirectories))
        {
            var newFile = filePath.Replace(source, target);
            Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
            File.Copy(filePath, newFile, true);
        }
    }

    private void CopyJarFiles(string source, string target, bool recursive = true)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var filePath in Directory.GetFiles(source, "*.jar", searchOption))
        {
            var fileName = Path.GetFileName(filePath);
            var destPath = Path.Combine(target, fileName);
            File.Copy(filePath, destPath, true);
        }
    }

    private async Task<bool> ExecuteBuildTasksAsync(string javaPath, CancellationToken ct)
    {
        try
        {
            var profilePath = Path.Combine(_installBasePath, "temp", "install_profile.json");
            var profileJson = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject();
            if (profileJson == null) return false;

            if (profileJson["processors"] is not JsonArray processors)
                return true;

            foreach (var processor in processors.OfType<JsonObject>())
            {
                ct.ThrowIfCancellationRequested();

                var sides = processor["sides"] as JsonArray;
                if (sides != null && !sides.Any(s => s?.ToString() == "server"))
                    continue;

                var jarName = processor["jar"]?.ToString();
                if (string.IsNullOrEmpty(jarName)) continue;

                var jarPath = Path.Combine(_installBasePath, "libraries", NameToPath(jarName));
                if (!File.Exists(jarPath)) continue;

                var mainClass = GetJarMainClass(jarPath);
                if (string.IsNullOrEmpty(mainClass)) continue;

                var classpath = new List<string> { jarPath };

                if (processor["classpath"] is JsonArray classpathArray)
                {
                    foreach (var cp in classpathArray)
                    {
                        var cpPath = Path.Combine(_installBasePath, "libraries", NameToPath(cp?.ToString() ?? ""));
                        if (File.Exists(cpPath))
                            classpath.Add(cpPath);
                    }
                }

                var args = new List<string>();
                if (processor["args"] is JsonArray argsArray)
                {
                    foreach (var arg in argsArray)
                    {
                        var argStr = arg?.ToString() ?? "";
                        if (argStr.StartsWith("[") && argStr.EndsWith("]"))
                        {
                            argStr = NameToPath(argStr);
                            argStr = Path.Combine(_installBasePath, "libraries", argStr);
                        }
                        argStr = ReplaceVariables(argStr);
                        args.Add(argStr);
                    }
                }

                var buildArg = $"-Djavax.net.ssl.trustStoreType=Windows-ROOT -cp \"{string.Join(ClassPathSeparator, classpath)}\" {mainClass} {string.Join(" ", args.Select(a => $"\"{a}\""))}";

                if (buildArg.Contains("DOWNLOAD_MOJMAPS"))
                {
                    var mojmapsSuccess = await DownloadMojmapsAsync(ct);
                    if (!mojmapsSuccess) continue;
                }

                ReportProgress($"执行构建任务: {mainClass}", InstallStage.Building, null);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = javaPath,
                        Arguments = buildArg,
                        WorkingDirectory = _installBasePath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(ct);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForgeInstaller] 执行构建任务失败: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> DownloadMojmapsAsync(CancellationToken ct)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            string manifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
            var manifestJson = JsonNode.Parse(await httpClient.GetStringAsync(manifestUrl, ct))?.AsObject();
            var versionEntry = manifestJson?["versions"]?.AsArray()
                .FirstOrDefault(v => v?["id"]?.ToString() == _installMcVersion);
            var versionUrl = versionEntry?["url"]?.ToString();
            if (string.IsNullOrEmpty(versionUrl)) return false;

            var versionJson = JsonNode.Parse(await httpClient.GetStringAsync(versionUrl, ct))?.AsObject();
            var mappingsUrl = versionJson?["downloads"]?["server_mappings"]?["url"]?.ToString();
            if (string.IsNullOrEmpty(mappingsUrl)) return false;

            var profilePath = Path.Combine(_installBasePath, "temp", "install_profile.json");
            var profileJson = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject();
            var mojmapsPath = ReplaceVariables("{MOJMAPS}");

            try
            {
                await DownloadFileWithProgressAsync(mappingsUrl, mojmapsPath, "映射表", ct);
            }
            catch
            {
                try
                {
                    await DownloadFileWithProgressAsync(ApplyMirror(mappingsUrl), mojmapsPath, "映射表", ct);
                }
                catch
                {
                    Debug.WriteLine("[ForgeInstaller] 映射表下载失败，跳过");
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForgeInstaller] 下载映射表失败: {ex.Message}");
            return false;
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        await Task.Run(() =>
        {
            try
            {
                var tempPath = Path.Combine(_installBasePath, "temp");
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }

                if (File.Exists(_installerPath))
                {
                    File.Delete(_installerPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeInstaller] 清理失败: {ex.Message}");
            }
        }, ct);
    }

    private string FindLaunchJar()
    {
        // 1. 检查 run.bat/run.sh 脚本
        var runBatPath = Path.Combine(_installBasePath, "run.bat");
        var runShPath = Path.Combine(_installBasePath, "run.sh");

        if (File.Exists(runBatPath) || File.Exists(runShPath))
        {
            try
            {
                var runContent = File.Exists(runBatPath)
                    ? File.ReadAllText(runBatPath)
                    : File.ReadAllText(runShPath);

                // 匹配 @libraries/xxx.txt 或 @xxx.txt
                var match = Regex.Match(runContent, @"java\s+@user_jvm_args\.txt\s+@(.+\.txt)");
                if (match.Success)
                {
                    var argsFile = match.Groups[1].Value;
                    var argsFilePath = Path.Combine(_installBasePath, argsFile);
                    if (File.Exists(argsFilePath))
                    {
                        Debug.WriteLine($"[ForgeInstaller] 从启动脚本找到参数文件: @{argsFile}");
                        return $"@{argsFile}";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeInstaller] 读取启动脚本失败: {ex.Message}");
            }
        }

        var jars = Directory.GetFiles(_installBasePath, "*.jar", SearchOption.TopDirectoryOnly);
        var installerName = Path.GetFileName(_installerPath);

        // 2. 查找根目录中的 Forge JAR（排除 installer、universal、shim、minecraft_server）
        foreach (var jar in jars)
        {
            var name = Path.GetFileName(jar);
            var nameLower = name.ToLowerInvariant();

            if (name.Equals(installerName, StringComparison.OrdinalIgnoreCase)) continue;
            if (nameLower.Contains("installer")) continue;
            if (nameLower.Contains("shim")) continue;
            if (nameLower.Contains("minecraft_server")) continue;
            if (nameLower.Contains("universal")) continue;

            if (nameLower.Contains("forge") || nameLower.Contains("neoforge"))
            {
                Debug.WriteLine($"[ForgeInstaller] 找到启动核心: {name}");
                return name;
            }
        }

        // 3. 查找 universal JAR
        foreach (var jar in jars)
        {
            var name = Path.GetFileName(jar);
            var nameLower = name.ToLowerInvariant();

            if (name.Equals(installerName, StringComparison.OrdinalIgnoreCase)) continue;
            if (nameLower.Contains("installer")) continue;

            if (nameLower.Contains("universal") && 
                (nameLower.Contains("forge") || nameLower.Contains("neoforge")))
            {
                Debug.WriteLine($"[ForgeInstaller] 找到 universal 核心: {name}");
                return name;
            }
        }

        // 4. 搜索 libraries 文件夹
        var libsPath = Path.Combine(_installBasePath, "libraries");
        if (Directory.Exists(libsPath))
        {
            try
            {
                var libsJars = Directory.GetFiles(libsPath, "*.jar", SearchOption.AllDirectories);
                
                // 优先查找 universal
                foreach (var jar in libsJars)
                {
                    var name = Path.GetFileName(jar);
                    var nameLower = name.ToLowerInvariant();

                    if ((nameLower.Contains("forge") || nameLower.Contains("neoforge")) &&
                        nameLower.Contains("universal"))
                    {
                        var relativePath = Path.GetRelativePath(_installBasePath, jar);
                        Debug.WriteLine($"[ForgeInstaller] 在 libraries 中找到 universal 核心: {relativePath}");
                        return relativePath;
                    }
                }

                // 查找任何 forge/neoforge JAR
                foreach (var jar in libsJars)
                {
                    var name = Path.GetFileName(jar);
                    var nameLower = name.ToLowerInvariant();

                    if (nameLower.Contains("forge") || nameLower.Contains("neoforge"))
                    {
                        var relativePath = Path.GetRelativePath(_installBasePath, jar);
                        Debug.WriteLine($"[ForgeInstaller] 在 libraries 中找到核心: {relativePath}");
                        return relativePath;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ForgeInstaller] 搜索 libraries 失败: {ex.Message}");
            }
        }

        // 5. 最后尝试使用任何非 installer 的 JAR
        foreach (var jar in jars)
        {
            var name = Path.GetFileName(jar);
            if (!name.Equals(installerName, StringComparison.OrdinalIgnoreCase) &&
                !name.ToLowerInvariant().Contains("installer"))
            {
                Debug.WriteLine($"[ForgeInstaller] 使用备选 JAR: {name}");
                return name;
            }
        }

        Debug.WriteLine($"[ForgeInstaller] 未找到启动核心");
        return "";
    }

    private string FindServerJarPath()
    {
        try
        {
            var profilePath = Path.Combine(_installBasePath, "temp", "install_profile.json");
            if (!File.Exists(profilePath)) return "";
            
            var profileJson = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject();
            var serverJarPathRaw = profileJson?["serverJarPath"]?.ToString() ?? "";
            
            if (string.IsNullOrEmpty(serverJarPathRaw)) return "";
            
            serverJarPathRaw = serverJarPathRaw.Replace("{LIBRARY_DIR}", Path.Combine(_installBasePath, "libraries"));
            serverJarPathRaw = serverJarPathRaw.Replace("{MINECRAFT_VERSION}", _installMcVersion);
            
            return serverJarPathRaw;
        }
        catch
        {
            return "";
        }
    }

    private string ApplyMirror(string url)
    {
        if (_mirrorSource == "MSL")
        {
            url = url.Replace("https://maven.neoforged.net", "https://neoforge.mirrors.mslmc.cn");
            url = url.Replace("https://maven.minecraftforge.net", "https://forge-maven.mirrors.mslmc.cn");
            url = url.Replace("https://files.minecraftforge.net", "https://forge-files.mirrors.mslmc.cn");
            url = url.Replace("https://libraries.minecraft.net", "https://mclibs.mirrors.mslmc.cn");
            url = url.Replace("https://piston-meta.mojang.com", "https://mc-meta.mirrors.mslmc.cn");
            url = url.Replace("piston-data.mojang.com/v1/objects/", "file.mslmc.cn/mirrors/vanilla/");
        }
        return url;
    }

    private string ApplyMirrorToPath(string path, string? name)
    {
        var namePath = !string.IsNullOrEmpty(name) ? NameToPath(name) : path;
        
        if (path.Contains("neoforged"))
        {
            return $"https://neoforge.mirrors.mslmc.cn/{namePath}";
        }
        if (path.Contains("minecraftforge"))
        {
            return $"https://forge-maven.mirrors.mslmc.cn/{namePath}";
        }
        if (path.Contains("mojang") || path.Contains("minecraft"))
        {
            return $"https://mclibs.mirrors.mslmc.cn/{namePath}";
        }
        
        return "";
    }

    private string ReplaceVariables(string str)
    {
        str = str.Replace("{LIBRARY_DIR}", Path.Combine(_installBasePath, "libraries"));
        str = str.Replace("{MINECRAFT_VERSION}", _installMcVersion);
        str = str.Replace("{INSTALLER}", _installerPath);
        str = str.Replace("{ROOT}", _installBasePath);
        str = str.Replace("{SIDE}", "server");

        if (_installVersionType <= 3)
        {
            str = str.Replace("{MINECRAFT_JAR}", FindServerJarPath());
        }
        else
        {
            str = str.Replace("{MINECRAFT_JAR}", Path.Combine(_installBasePath, $"minecraft_server.{_installMcVersion}.jar"));
        }

        try
        {
            var profilePath = Path.Combine(_installBasePath, "temp", "install_profile.json");
            if (File.Exists(profilePath))
            {
                var profileJson = JsonNode.Parse(File.ReadAllText(profilePath))?.AsObject();
                var data = profileJson?["data"]?.AsObject();
                if (data != null)
                {
                    str = ReplaceDataVariable(str, data, "MAPPINGS");
                    str = ReplaceDataVariable(str, data, "MC_UNPACKED");
                    str = ReplaceDataVariable(str, data, "MOJMAPS");
                    str = ReplaceDataVariable(str, data, "MERGED_MAPPINGS");
                    str = ReplaceDataVariable(str, data, "MC_SRG");
                    str = ReplaceDataVariable(str, data, "PATCHED");
                    str = ReplaceDataVariable(str, data, "MC_SLIM");
                    str = ReplaceDataVariable(str, data, "MC_EXTRA");

                    if (data["BINPATCH"] is JsonObject binpatch)
                    {
                        var serverPath = binpatch["server"]?.ToString() ?? "";
                        str = str.Replace("{BINPATCH}", Path.Combine(_installBasePath, "temp", serverPath.TrimStart('/')));
                    }
                }
            }
        }
        catch { }

        return str;
    }

    private string ReplaceDataVariable(string str, JsonObject data, string key)
    {
        if (data[key] is JsonObject obj && obj["server"] is JsonValue serverValue)
        {
            var value = serverValue.ToString();
            str = str.Replace($"{{{key}}}", Path.Combine(_installBasePath, "libraries", NameToPath(value)));
        }
        return str;
    }

    private string NameToPath(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        if (name.StartsWith("[") && name.EndsWith("]"))
        {
            name = name.Substring(1, name.Length - 2);
        }

        var extentTag = "";
        if (name.Contains("@"))
        {
            var atParts = name.Split('@');
            name = atParts[0];
            extentTag = atParts[1];
        }

        try
        {
            var parts = name.Split(':');
            if (parts.Length < 3) return "";

            var groupId = parts[0];
            var artifactId = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? parts[3] : null;

            var groupPath = groupId.Replace('.', '/');

            string fileName;
            if (!string.IsNullOrEmpty(classifier))
            {
                fileName = $"{artifactId}-{version}-{classifier}.jar";
            }
            else
            {
                fileName = $"{artifactId}-{version}.jar";
            }

            var result = $"{groupPath}/{artifactId}/{version}/{fileName}";

            if (!string.IsNullOrEmpty(extentTag))
            {
                result = result.Replace(".jar", $".{extentTag}");
            }

            return result;
        }
        catch
        {
            return "";
        }
    }

    private static string? GetJarMainClass(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var manifestEntry = archive.GetEntry("META-INF/MANIFEST.MF");
            if (manifestEntry == null) return null;

            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring("Main-Class:".Length).Trim();
                }
            }
        }
        catch { }
        return null;
    }

    private async Task DownloadFileWithProgressAsync(string url, string destPath, string fileName, CancellationToken ct)
    {
        try
        {
            var directory = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long downloadedBytes = 0;
            int bytesRead;
            var lastReportTime = DateTime.MinValue;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;

                var now = DateTime.Now;
                if ((now - lastReportTime).TotalMilliseconds > 500)
                {
                    lastReportTime = now;
                    var progress = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;
                    Debug.WriteLine($"[ForgeInstaller] 下载 {fileName}: {progress:F1}%");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForgeInstaller] 下载文件失败 {fileName}: {ex.Message}");
            throw;
        }
    }

    public static bool IsForgeInstaller(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        return (name.Contains("forge") || name.Contains("neoforge")) &&
               name.Contains("installer") &&
               name.EndsWith(".jar");
    }

    public static bool NeedsForgeInstall(string coreType, string fileName)
    {
        var name = fileName.ToLowerInvariant();
        return (coreType.Equals("forge", StringComparison.OrdinalIgnoreCase) ||
                coreType.Equals("neoforge", StringComparison.OrdinalIgnoreCase)) &&
               name.Contains("installer");
    }
}

public class ForgeInstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? MinecraftVersion { get; set; }
    public string? LaunchJar { get; set; }
}
