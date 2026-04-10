using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using ZMSL.App.Models;

namespace ZMSL.App.Services
{
    /// <summary>
    /// 自动备份服务
    /// </summary>
    public class AutoBackupService
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Dictionary<int, BackupSchedule> _backupSchedules;
        private readonly System.Timers.Timer _backupTimer;
        private readonly string _backupBasePath;

        public AutoBackupService(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
            _backupSchedules = new Dictionary<int, BackupSchedule>();
            _backupTimer = new System.Timers.Timer(60000); // 每分钟检查一次
            _backupTimer.Elapsed += CheckBackupSchedules;
            _backupTimer.AutoReset = true;
            
            // 设置备份文件存储路径
            _backupBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "ZMSL", "Backups");
            
            // 确保备份目录存在
            Directory.CreateDirectory(_backupBasePath);
        }

        /// <summary>
        /// 启动自动备份服务
        /// </summary>
        public void Start()
        {
            _backupTimer.Start();
        }

        /// <summary>
        /// 停止自动备份服务
        /// </summary>
        public void Stop()
        {
            _backupTimer.Stop();
        }

        /// <summary>
        /// 添加备份计划
        /// </summary>
        /// <param name="serverId">服务器ID</param>
        /// <param name="scheduleType">备份周期类型</param>
        /// <param name="interval">间隔时间</param>
        /// <param name="backupTime">备份时间（仅限每日/每周）</param>
        /// <param name="enabled">是否启用</param>
        /// <returns>计划ID</returns>
        public int AddBackupSchedule(int serverId, BackupScheduleType scheduleType, 
            int interval, TimeSpan? backupTime = null, bool enabled = true)
        {
            var scheduleId = GenerateScheduleId();
            var schedule = new BackupSchedule
            {
                Id = scheduleId,
                ServerId = serverId,
                ScheduleType = scheduleType,
                Interval = interval,
                BackupTime = backupTime ?? new TimeSpan(2, 0, 0), // 默认凌晨2点
                Enabled = enabled,
                LastBackupTime = DateTime.MinValue,
                RetentionDays = 7, // 默认保留7天
                MaxBackups = 10 // 默认最多保留10个备份
            };

            _backupSchedules[scheduleId] = schedule;
            return scheduleId;
        }

        /// <summary>
        /// 更新备份计划
        /// </summary>
        /// <param name="scheduleId">计划ID</param>
        /// <param name="scheduleType">备份周期类型</param>
        /// <param name="interval">间隔时间</param>
        /// <param name="backupTime">备份时间</param>
        /// <param name="enabled">是否启用</param>
        /// <param name="retentionDays">保留天数</param>
        /// <param name="maxBackups">最大备份数量</param>
        public void UpdateBackupSchedule(int scheduleId, BackupScheduleType scheduleType, 
            int interval, TimeSpan? backupTime, bool enabled, int retentionDays, int maxBackups)
        {
            if (_backupSchedules.ContainsKey(scheduleId))
            {
                var schedule = _backupSchedules[scheduleId];
                schedule.ScheduleType = scheduleType;
                schedule.Interval = interval;
                schedule.BackupTime = backupTime ?? schedule.BackupTime;
                schedule.Enabled = enabled;
                schedule.RetentionDays = retentionDays;
                schedule.MaxBackups = maxBackups;
            }
        }

        /// <summary>
        /// 删除备份计划
        /// </summary>
        /// <param name="scheduleId">计划ID</param>
        public void RemoveBackupSchedule(int scheduleId)
        {
            _backupSchedules.Remove(scheduleId);
        }

        /// <summary>
        /// 获取所有备份计划
        /// </summary>
        /// <returns>备份计划列表</returns>
        public List<BackupSchedule> GetAllSchedules()
        {
            return new List<BackupSchedule>(_backupSchedules.Values);
        }

        /// <summary>
        /// 获取指定服务器的备份计划
        /// </summary>
        /// <param name="serverId">服务器ID</param>
        /// <returns>备份计划列表</returns>
        public List<BackupSchedule> GetSchedulesForServer(int serverId)
        {
            var schedules = new List<BackupSchedule>();
            foreach (var schedule in _backupSchedules.Values)
            {
                if (schedule.ServerId == serverId)
                {
                    schedules.Add(schedule);
                }
            }
            return schedules;
        }

        /// <summary>
        /// 立即执行备份
        /// </summary>
        /// <param name="serverId">服务器ID</param>
        public async Task<bool> ExecuteBackupNow(int serverId)
        {
            var serverPath = GetServerPath(serverId);
            if (string.IsNullOrEmpty(serverPath) || !Directory.Exists(serverPath))
            {
                return false;
            }

            return await CreateBackup(serverId, serverPath);
        }

        /// <summary>
        /// 检查备份计划并执行到期的备份
        /// </summary>
        private void CheckBackupSchedules(object? sender, System.Timers.ElapsedEventArgs e)
        {
            var now = DateTime.Now;
            var schedulesToExecute = new List<BackupSchedule>();

            foreach (var schedule in _backupSchedules.Values)
            {
                if (!schedule.Enabled) continue;

                bool shouldExecute = false;

                switch (schedule.ScheduleType)
                {
                    case BackupScheduleType.Minutely:
                        if ((now - schedule.LastBackupTime).TotalMinutes >= schedule.Interval)
                            shouldExecute = true;
                        break;
                    case BackupScheduleType.Hourly:
                        if ((now - schedule.LastBackupTime).TotalHours >= schedule.Interval)
                            shouldExecute = true;
                        break;
                    case BackupScheduleType.Daily:
                        var dailyTargetTime = new DateTime(now.Year, now.Month, now.Day, 
                            schedule.BackupTime.Hours, schedule.BackupTime.Minutes, 0);
                        if (now >= dailyTargetTime && schedule.LastBackupTime.Date < now.Date)
                            shouldExecute = true;
                        break;
                    case BackupScheduleType.Weekly:
                        var daysSinceLastBackup = (now.Date - schedule.LastBackupTime.Date).Days;
                        if (daysSinceLastBackup >= 7 && now.TimeOfDay >= schedule.BackupTime)
                            shouldExecute = true;
                        break;
                }

                if (shouldExecute)
                {
                    schedulesToExecute.Add(schedule);
                }
            }

            // 在UI线程中执行备份
            foreach (var schedule in schedulesToExecute)
            {
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    await ExecuteScheduledBackup(schedule);
                });
            }
        }

        /// <summary>
        /// 执行计划备份
        /// </summary>
        private async Task ExecuteScheduledBackup(BackupSchedule schedule)
        {
            try
            {
                var serverPath = GetServerPath(schedule.ServerId);
                if (string.IsNullOrEmpty(serverPath) || !Directory.Exists(serverPath))
                {
                    return;
                }

                var success = await CreateBackup(schedule.ServerId, serverPath);
                if (success)
                {
                    schedule.LastBackupTime = DateTime.Now;
                    
                    // 清理过期备份
                    await CleanupOldBackups(schedule);
                    
                    // 触发备份完成事件
                    OnBackupCompleted?.Invoke(this, new BackupEventArgs
                    {
                        ServerId = schedule.ServerId,
                        Schedule = schedule,
                        BackupTime = DateTime.Now,
                        Success = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"服务器 {schedule.ServerId} 备份失败: {ex.Message}");
                
                OnBackupCompleted?.Invoke(this, new BackupEventArgs
                {
                    ServerId = schedule.ServerId,
                    Schedule = schedule,
                    BackupTime = DateTime.Now,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        /// <summary>
        /// 创建备份
        /// </summary>
        private async Task<bool> CreateBackup(int serverId, string serverPath)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"server_{serverId}_backup_{timestamp}.zip";
                var backupFilePath = Path.Combine(_backupBasePath, backupFileName);

                // 创建ZIP压缩包
                await Task.Run(() =>
                {
                    ZipFile.CreateFromDirectory(serverPath, backupFilePath);
                });

                System.Diagnostics.Debug.WriteLine($"服务器 {serverId} 备份完成: {backupFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"服务器 {serverId} 备份失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 清理过期备份
        /// </summary>
        private async Task CleanupOldBackups(BackupSchedule schedule)
        {
            await Task.Run(() =>
            {
                try
                {
                    var backupFiles = Directory.GetFiles(_backupBasePath, $"server_{schedule.ServerId}_backup_*.zip");
                    var backupInfos = new List<(string FilePath, DateTime CreationTime)>();

                    // 获取备份文件信息
                    foreach (var file in backupFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        backupInfos.Add((file, fileInfo.CreationTime));
                    }

                    // 按创建时间排序
                    backupInfos.Sort((a, b) => b.CreationTime.CompareTo(a.CreationTime));

                    // 删除超出保留天数的备份
                    var cutoffDate = DateTime.Now.AddDays(-schedule.RetentionDays);
                    foreach (var (filePath, creationTime) in backupInfos)
                    {
                        if (creationTime < cutoffDate)
                        {
                            File.Delete(filePath);
                            System.Diagnostics.Debug.WriteLine($"删除过期备份: {filePath}");
                        }
                    }

                    // 如果备份数量超过限制，删除最旧的
                    if (backupInfos.Count > schedule.MaxBackups)
                    {
                        for (int i = schedule.MaxBackups; i < backupInfos.Count; i++)
                        {
                            File.Delete(backupInfos[i].FilePath);
                            System.Diagnostics.Debug.WriteLine($"删除多余备份: {backupInfos[i].FilePath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"清理备份失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 获取服务器路径（需要根据实际情况实现）
        /// </summary>
        private string GetServerPath(int serverId)
        {
            // 这里需要根据您的服务器管理逻辑来获取实际路径
            // 暂时返回模拟路径
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "ZMSL", "Servers", $"server_{serverId}");
        }

        /// <summary>
        /// 生成唯一计划ID
        /// </summary>
        private int GenerateScheduleId()
        {
            return _backupSchedules.Count > 0 ? _backupSchedules.Keys.Max() + 1 : 1;
        }

        /// <summary>
        /// 获取备份文件列表
        /// </summary>
        /// <param name="serverId">服务器ID</param>
        /// <returns>备份文件信息列表</returns>
        public List<BackupFileInfo> GetBackupFiles(int serverId)
        {
            var backupFiles = new List<BackupFileInfo>();
            var pattern = $"server_{serverId}_backup_*.zip";
            var files = Directory.GetFiles(_backupBasePath, pattern);

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                backupFiles.Add(new BackupFileInfo
                {
                    FilePath = file,
                    FileName = Path.GetFileName(file),
                    CreationTime = fileInfo.CreationTime,
                    Size = fileInfo.Length
                });
            }

            backupFiles.Sort((a, b) => b.CreationTime.CompareTo(a.CreationTime));
            return backupFiles;
        }

        /// <summary>
        /// 恢复备份
        /// </summary>
        /// <param name="backupFilePath">备份文件路径</param>
        /// <param name="serverId">服务器ID</param>
        public async Task<bool> RestoreBackup(string backupFilePath, int serverId)
        {
            try
            {
                var serverPath = GetServerPath(serverId);
                if (string.IsNullOrEmpty(serverPath))
                {
                    return false;
                }

                // 确保目标目录存在
                Directory.CreateDirectory(serverPath);

                // 解压备份文件
                await Task.Run(() =>
                {
                    // 先清空目标目录
                    foreach (var file in Directory.GetFiles(serverPath))
                    {
                        File.Delete(file);
                    }
                    foreach (var dir in Directory.GetDirectories(serverPath))
                    {
                        Directory.Delete(dir, true);
                    }

                    // 解压备份
                    ZipFile.ExtractToDirectory(backupFilePath, serverPath);
                });

                System.Diagnostics.Debug.WriteLine($"服务器 {serverId} 恢复完成");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"服务器 {serverId} 恢复失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 备份完成事件
        /// </summary>
        public event EventHandler<BackupEventArgs>? OnBackupCompleted;
    }

    /// <summary>
    /// 备份计划
    /// </summary>
    public class BackupSchedule
    {
        public int Id { get; set; }
        public int ServerId { get; set; }
        public BackupScheduleType ScheduleType { get; set; }
        public int Interval { get; set; }
        public TimeSpan BackupTime { get; set; }
        public bool Enabled { get; set; }
        public DateTime LastBackupTime { get; set; }
        public int RetentionDays { get; set; }
        public int MaxBackups { get; set; }
        public string Description => $"{ScheduleType} 备份，间隔 {Interval} {(ScheduleType == BackupScheduleType.Minutely ? "分钟" : ScheduleType == BackupScheduleType.Hourly ? "小时" : "天")}";
    }

    /// <summary>
    /// 备份计划类型
    /// </summary>
    public enum BackupScheduleType
    {
        Minutely,
        Hourly,
        Daily,
        Weekly
    }

    /// <summary>
    /// 备份事件参数
    /// </summary>
    public class BackupEventArgs : EventArgs
    {
        public int ServerId { get; set; }
        public required BackupSchedule Schedule { get; set; }
        public DateTime BackupTime { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 备份文件信息
    /// </summary>
    public class BackupFileInfo
    {
        public required string FilePath { get; set; }
        public required string FileName { get; set; }
        public DateTime CreationTime { get; set; }
        public long Size { get; set; }
        public string FormattedSize => FormatFileSize(Size);

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}