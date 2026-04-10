using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using ZMSL.App.Models;

namespace ZMSL.App.Services;

/// <summary>
/// 自动备份服务
/// </summary>
public class BackupService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly ServerManagerService _serverManager;
    private Timer? _timer;
    private bool _disposed;
    private readonly string _tempRoot;
    private readonly string _backupRoot;

    public BackupService(DatabaseService db, ServerManagerService serverManager)
    {
        _db = db;
        _serverManager = serverManager;
        
        var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _tempRoot = Path.Combine(docsPath, "ZMSL", "TEMP");
        _backupRoot = Path.Combine(docsPath, "ZMSL", "Backups");
        
        // 清理可能残留的TEMP目录
        CleanupTempDirectory();
    }

    public async Task StartAsync()
    {
        var settings = await _db.GetSettingsAsync();
        if (!settings.EnableAutoBackup || settings.BackupIntervalMinutes <= 0) return;
                
        var interval = TimeSpan.FromMinutes(settings.BackupIntervalMinutes);
        _timer = new Timer(async _ => await RunAutoBackupAsync(), null, interval, interval);
    }

    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async Task RunAutoBackupAsync()
    {
        try
        {
            var settings = await _db.GetSettingsAsync();
            if (!settings.EnableAutoBackup) return;

            var servers = await _db.Servers.ToListAsync();
            if (servers.Count == 0) return;

            // 多线程优化：最多 2 个服务器并行备份，避免磁盘与内存压力过大
            var semaphore = new SemaphoreSlim(2, 2);
            var tasks = servers.Select(async server =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await BackupServerAsync(server.Id);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"备份服务器 {server.Name} 失败: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"自动备份失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 手动备份指定服务器
    /// </summary>
    public async Task<ServerBackup> BackupServerAsync(int serverId)
    {
        var server = await _db.Servers.FindAsync(serverId);
        if (server == null)
            throw new InvalidOperationException($"服务器 ID {serverId} 不存在");

        if (string.IsNullOrEmpty(server.ServerPath) || !Directory.Exists(server.ServerPath))
            throw new InvalidOperationException($"服务器目录不存在: {server.ServerPath}");

        var settings = await _db.GetSettingsAsync();
        
        // 1. 准备临时目录
        var tempServerDir = Path.Combine(_tempRoot, server.Name);
        if (Directory.Exists(tempServerDir))
            Directory.Delete(tempServerDir, true);
        Directory.CreateDirectory(tempServerDir);

        try
        {
            // 2. 复制服务器文件到临时目录(排除session.lock)
            await Task.Run(() => CopyDirectory(server.ServerPath, tempServerDir));

            // 3. 压缩为zip文件
            var backupDir = Path.Combine(_backupRoot, server.Name);
            Directory.CreateDirectory(backupDir);
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var zipFileName = $"{server.Name}_{timestamp}.zip";
            var zipPath = Path.Combine(backupDir, zipFileName);

            await Task.Run(() =>
            {
                ZipFile.CreateFromDirectory(tempServerDir, zipPath, CompressionLevel.Optimal, false);
            });

            // 4. 记录到数据库
            var fileInfo = new FileInfo(zipPath);
            var backup = new ServerBackup
            {
                ServerId = server.Id,
                ServerName = server.Name,
                BackupPath = zipPath,
                FileSizeBytes = fileInfo.Length,
                CreatedAt = DateTime.Now
            };
            
            _db.Backups.Add(backup);
            await _db.SaveChangesAsync();

            // 5. 应用保留策略
            await ApplyRetentionPolicyAsync(server.Id, settings.BackupRetentionCount);

            System.Diagnostics.Debug.WriteLine($"已备份: {server.Name} -> {zipPath} ({FormatFileSize(fileInfo.Length)})");
            
            return backup;
        }
        finally
        {
            // 6. 清理临时目录
            if (Directory.Exists(tempServerDir))
            {
                try
                {
                    Directory.Delete(tempServerDir, true);
                }
                catch { /* 清理失败不影响备份结果 */ }
            }
        }
    }

    /// <summary>
    /// 复制目录,排除session.lock等锁文件（并行优化版本）
    /// </summary>
    private void CopyDirectory(string sourceDir, string targetDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"源目录不存在: {sourceDir}");

        Directory.CreateDirectory(targetDir);

        // 并行复制文件
        var files = dir.GetFiles()
            .Where(f => !f.Name.Equals("session.lock", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                var targetPath = Path.Combine(targetDir, file.Name);
                try
                {
                    file.CopyTo(targetPath, false);
                }
                catch (IOException)
                {
                    System.Diagnostics.Debug.WriteLine($"跳过锁定文件: {file.Name}");
                }
            });

        // 并行递归复制子目录
        var subDirs = dir.GetDirectories();
        Parallel.ForEach(subDirs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            subDir =>
            {
                var targetSubDir = Path.Combine(targetDir, subDir.Name);
                CopyDirectory(subDir.FullName, targetSubDir);
            });
    }

    /// <summary>
    /// 应用备份保留策略（并行优化版本）
    /// </summary>
    private async Task ApplyRetentionPolicyAsync(int serverId, int retentionCount)
    {
        var backups = await _db.Backups
            .Where(b => b.ServerId == serverId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        var toDelete = backups.Skip(retentionCount).ToList();

        if (toDelete.Count == 0) return;

        // 并行删除文件
        await Task.Run(() =>
        {
            Parallel.ForEach(toDelete, backup =>
            {
                try
                {
                    if (File.Exists(backup.BackupPath))
                        File.Delete(backup.BackupPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"删除旧备份失败: {backup.BackupPath}, {ex.Message}");
                }
            });
        });

        // 从数据库中移除记录
        foreach (var backup in toDelete)
        {
            _db.Backups.Remove(backup);
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 获取服务器的备份列表
    /// </summary>
    public async Task<List<ServerBackup>> GetServerBackupsAsync(int serverId)
    {
        return await _db.Backups
            .Where(b => b.ServerId == serverId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// 删除单个备份
    /// </summary>
    public async Task DeleteBackupAsync(int backupId)
    {
        var backup = await _db.Backups.FindAsync(backupId);
        if (backup == null) return;

        try
        {
            if (File.Exists(backup.BackupPath))
                File.Delete(backup.BackupPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"删除备份文件失败: {ex.Message}");
        }

        _db.Backups.Remove(backup);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 恢复备份到指定路径
    /// </summary>
    public async Task RestoreBackupAsync(int backupId, string targetPath)
    {
        var backup = await _db.Backups.FindAsync(backupId);
        if (backup == null)
            throw new InvalidOperationException($"备份记录 ID {backupId} 不存在");

        if (!File.Exists(backup.BackupPath))
            throw new FileNotFoundException($"备份文件不存在: {backup.BackupPath}");

        // 如果目标目录存在,先备份
        if (Directory.Exists(targetPath))
        {
            var backupOldDir = $"{targetPath}_old_{DateTime.Now:yyyyMMddHHmmss}";
            Directory.Move(targetPath, backupOldDir);
        }

        Directory.CreateDirectory(targetPath);

        await Task.Run(() =>
        {
            ZipFile.ExtractToDirectory(backup.BackupPath, targetPath);
        });

        System.Diagnostics.Debug.WriteLine($"已恢复备份: {backup.BackupPath} -> {targetPath}");
    }

    /// <summary>
    /// 清理临时目录
    /// </summary>
    private void CleanupTempDirectory()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"清理TEMP目录失败: {ex.Message}");
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _timer?.Dispose();
        CleanupTempDirectory();
        _disposed = true;
    }
}
