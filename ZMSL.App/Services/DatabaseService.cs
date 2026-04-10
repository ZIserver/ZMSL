using Microsoft.EntityFrameworkCore;
using ZMSL.App.Models;

namespace ZMSL.App.Services;

public class DatabaseService : DbContext
{
    public DbSet<LocalServer> Servers { get; set; } = null!;
    public DbSet<DownloadRecord> Downloads { get; set; } = null!;
    public DbSet<LocalFrpTunnel> FrpTunnels { get; set; } = null!;
    public DbSet<AppSettings> Settings { get; set; } = null!;
    public DbSet<ServerBackup> Backups { get; set; } = null!;
    public DbSet<LinuxNode> LinuxNodes { get; set; } = null!;
    public DbSet<RemoteServer> RemoteServers { get; set; } = null!;
    public DbSet<ZMSL.App.Models.JavaInfo> JavaInstallations { get; set; } = null!;

    private readonly string _dbPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isInitialized = false;
    
    public DatabaseService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZMSL");
        Directory.CreateDirectory(appDataPath);
        _dbPath = Path.Combine(appDataPath, "zmsl.db");
    }
    
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
            
        await _lock.WaitAsync();
        try
        {
            if (_isInitialized) return;
    
            // 确保数据库创建（在后台线程执行耗时操作）
            await Task.Run(() =>
            {
                try 
                {
                    // 创建一个新的临时上下文用于初始化，避免线程问题
                    // 使用 using 块确保 context 立即释放
                    using (var context = new DatabaseService(true))
                    {
                        // 在执行任何数据库操作前，确保 SQLite 驱动已加载
                        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                        context.Database.EnsureCreated();
                        context.MigrateDatabaseInternal();
                    }
                }
                catch (Exception ex)
                {
                    // 忽略“已添加相同键”的并发错误，这通常意味着模型已在另一个线程中构建完成
                    if (!ex.Message.Contains("same key"))
                    {
                        throw;
                    }
                }
            });
    
            _isInitialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    // 仅用于初始化的私有构造函数
    private DatabaseService(bool isInitializing) : this()
    {
        // 确保使用新的连接字符串或配置（如果有必要）
    }

    private void MigrateDatabase()
    {
        // 废弃，由 MigrateDatabaseInternal 替代
    }

    private void MigrateDatabaseInternal()
    {
        // 检查并添加新列（如果不存在）
        var connection = Database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
            opened = true;
        }
        
        try
        {
            // 检查 Servers 表的列
            var checkServersColumns = connection.CreateCommand();
            checkServersColumns.CommandText = "PRAGMA table_info(Servers)";
            var hasModeColumn = false;
            var hasPlayerCapacityColumn = false;
            var hasUseLatestPurpurColumn = false;
            var hasEnableAuthlibColumn = false;
            var hasAuthlibUrlColumn = false;
            var hasAuthlibDownloadedColumn = false;
            var hasEnglishAliasColumn = false;
            var hasStartupCommandColumn = false;
            var hasForgeInstalledColumn = false;
            var hasForgeVersionColumn = false;
            var hasIconPathColumn = false;
            
            using (var reader = checkServersColumns.ExecuteReader())
            {
                while (reader.Read())
                {
                    var columnName = reader.GetString(1);
                    if (columnName == "Mode") hasModeColumn = true;
                    if (columnName == "PlayerCapacity") hasPlayerCapacityColumn = true;
                    if (columnName == "UseLatestPurpur") hasUseLatestPurpurColumn = true;
                    if (columnName == "EnableAuthlib") hasEnableAuthlibColumn = true;
                    if (columnName == "AuthlibUrl") hasAuthlibUrlColumn = true;
                    if (columnName == "AuthlibDownloaded") hasAuthlibDownloadedColumn = true;
                    if (columnName == "EnglishAlias") hasEnglishAliasColumn = true;
                    if (columnName == "StartupCommand") hasStartupCommandColumn = true;
                    if (columnName == "ForgeInstalled") hasForgeInstalledColumn = true;
                    if (columnName == "ForgeVersion") hasForgeVersionColumn = true;
                    if (columnName == "IconPath") hasIconPathColumn = true;
                }
            }
            
            // 添加 Servers 表缺失的列
            if (!hasModeColumn)
            {
                var addModeColumn = connection.CreateCommand();
                addModeColumn.CommandText = "ALTER TABLE Servers ADD COLUMN Mode INTEGER NOT NULL DEFAULT 0";
                addModeColumn.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.Mode 列");
            }
            
            if (!hasPlayerCapacityColumn)
            {
                var addPlayerCapacityColumn = connection.CreateCommand();
                addPlayerCapacityColumn.CommandText = "ALTER TABLE Servers ADD COLUMN PlayerCapacity INTEGER NOT NULL DEFAULT 10";
                addPlayerCapacityColumn.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.PlayerCapacity 列");
            }
            
            if (!hasUseLatestPurpurColumn)
            {
                var addUseLatestPurpurColumn = connection.CreateCommand();
                addUseLatestPurpurColumn.CommandText = "ALTER TABLE Servers ADD COLUMN UseLatestPurpur INTEGER NOT NULL DEFAULT 0";
                addUseLatestPurpurColumn.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.UseLatestPurpur 列");
            }
            
            if (!hasEnableAuthlibColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Servers ADD COLUMN EnableAuthlib INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.EnableAuthlib 列");
            }
            
            if (!hasAuthlibUrlColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Servers ADD COLUMN AuthlibUrl TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.AuthlibUrl 列");
            }
            
            if (!hasAuthlibDownloadedColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Servers ADD COLUMN AuthlibDownloaded INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.AuthlibDownloaded 列");
            }
            
            if (!hasEnglishAliasColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Servers ADD COLUMN EnglishAlias TEXT NOT NULL DEFAULT ''";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.EnglishAlias 列");
            }

            if (!hasStartupCommandColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Servers ADD COLUMN StartupCommand TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.StartupCommand 列");
            }

            if (!hasForgeInstalledColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Servers ADD COLUMN ForgeInstalled INTEGER NOT NULL DEFAULT 1";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.ForgeInstalled 列");
            }

            if (!hasForgeVersionColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Servers ADD COLUMN ForgeVersion TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.ForgeVersion 列");
            }

            if (!hasIconPathColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Servers ADD COLUMN IconPath TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Servers.IconPath 列");
            }
            
            // 检查 Settings 表的备份相关列
            var checkSettingsColumns = connection.CreateCommand();
            checkSettingsColumns.CommandText = "PRAGMA table_info(Settings)";
            var hasEnableAutoBackupColumn = false;
            var hasBackupIntervalMinutesColumn = false;
            var hasBackupIntervalHoursColumn = false; // 旧字段,用于迁移
            var hasBackupDirectoryColumn = false;
            var hasBackupRetentionCountColumn = false;
            
            using (var reader = checkSettingsColumns.ExecuteReader())
            {
                while (reader.Read())
                {
                    var columnName = reader.GetString(1);
                    if (columnName == "EnableAutoBackup") hasEnableAutoBackupColumn = true;
                    if (columnName == "BackupIntervalMinutes") hasBackupIntervalMinutesColumn = true;
                    if (columnName == "BackupIntervalHours") hasBackupIntervalHoursColumn = true;
                    if (columnName == "BackupDirectory") hasBackupDirectoryColumn = true;
                    if (columnName == "BackupRetentionCount") hasBackupRetentionCountColumn = true;
                }
            }
            
            if (!hasEnableAutoBackupColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN EnableAutoBackup INTEGER NOT NULL DEFAULT 1";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.EnableAutoBackup 列");
            }
            
            // 迁移旧的小时字段到新的分钟字段
            if (hasBackupIntervalHoursColumn && !hasBackupIntervalMinutesColumn)
            {
                var cmd = connection.CreateCommand();
                // 先添加新列,默认值60分钟
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN BackupIntervalMinutes INTEGER NOT NULL DEFAULT 60";
                cmd.ExecuteNonQuery();
                
                // 将旧值转换为分钟(小时 * 60)
                cmd.CommandText = "UPDATE Settings SET BackupIntervalMinutes = BackupIntervalHours * 60 WHERE BackupIntervalHours > 0";
                cmd.ExecuteNonQuery();
                
                System.Diagnostics.Debug.WriteLine("已将 BackupIntervalHours 迁移到 BackupIntervalMinutes");
            }
            else if (!hasBackupIntervalMinutesColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN BackupIntervalMinutes INTEGER NOT NULL DEFAULT 60";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.BackupIntervalMinutes 列");
            }
            
            if (!hasBackupDirectoryColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN BackupDirectory TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.BackupDirectory 列");
            }
            
            if (!hasBackupRetentionCountColumn)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN BackupRetentionCount INTEGER NOT NULL DEFAULT 7";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.BackupRetentionCount 列");
            }

            // 检查日志分析相关列
            var hasLogAnalysisApiUrl = false;
            var hasLogAnalysisApiKey = false;
            var hasLogAnalysisModel = false;
            var hasUseCustomLogAnalysisConfig = false;

            var checkLogAnalysisColumns = connection.CreateCommand();
            checkLogAnalysisColumns.CommandText = "PRAGMA table_info(Settings)";
            using (var reader2 = checkLogAnalysisColumns.ExecuteReader())
            {
                while (reader2.Read())
                {
                    var columnName = reader2.GetString(1);
                    if (columnName == "LogAnalysisApiUrl") hasLogAnalysisApiUrl = true;
                    if (columnName == "LogAnalysisApiKey") hasLogAnalysisApiKey = true;
                    if (columnName == "LogAnalysisModel") hasLogAnalysisModel = true;
                    if (columnName == "UseCustomLogAnalysisConfig") hasUseCustomLogAnalysisConfig = true;
                }
            }

            if (!hasLogAnalysisApiUrl)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN LogAnalysisApiUrl TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.LogAnalysisApiUrl 列");
            }

            if (!hasLogAnalysisApiKey)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN LogAnalysisApiKey TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.LogAnalysisApiKey 列");
            }

            if (!hasLogAnalysisModel)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN LogAnalysisModel TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.LogAnalysisModel 列");
            }

            if (!hasUseCustomLogAnalysisConfig)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN UseCustomLogAnalysisConfig INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.UseCustomLogAnalysisConfig 列");
            }

            // 检查启动器增强功能相关列
            var hasAutoStartLastServer = false;
            var hasAutoRestartAppOnCrash = false;
            var hasAutoRestartServerOnCrash = false;
            var hasStartOnBoot = false;
            var hasDownloadThreads = false;
            var hasForceMultiThread = false;
            var hasConsoleEncoding = false;
            var hasConsoleFontSize = false;
            var hasUseCardView = false;
            var hasEnableMicaEffect = false;
            var hasUseCustomBackground = false;
            var hasBackgroundImagePath = false;
            var hasMicaIntensity = false;

            var checkLauncherFeaturesColumns = connection.CreateCommand();
            checkLauncherFeaturesColumns.CommandText = "PRAGMA table_info(Settings)";
            using (var reader3 = checkLauncherFeaturesColumns.ExecuteReader())
            {
                while (reader3.Read())
                {
                    var columnName = reader3.GetString(1);
                    if (columnName == "AutoStartLastServer") hasAutoStartLastServer = true;
                    if (columnName == "AutoRestartAppOnCrash") hasAutoRestartAppOnCrash = true;
                    if (columnName == "AutoRestartServerOnCrash") hasAutoRestartServerOnCrash = true;
                    if (columnName == "StartOnBoot") hasStartOnBoot = true;
                    if (columnName == "DownloadThreads") hasDownloadThreads = true;
                    if (columnName == "ForceMultiThread") hasForceMultiThread = true;
                    if (columnName == "ConsoleEncoding") hasConsoleEncoding = true;
                    if (columnName == "ConsoleFontSize") hasConsoleFontSize = true;
                    if (columnName == "UseCardView") hasUseCardView = true;
                    if (columnName == "EnableMicaEffect") hasEnableMicaEffect = true;
                    if (columnName == "UseCustomBackground") hasUseCustomBackground = true;
                    if (columnName == "BackgroundImagePath") hasBackgroundImagePath = true;
                    if (columnName == "MicaIntensity") hasMicaIntensity = true;
                }
            }

            if (!hasAutoStartLastServer)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN AutoStartLastServer INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.AutoStartLastServer 列");
            }
            if (!hasAutoRestartAppOnCrash)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN AutoRestartAppOnCrash INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.AutoRestartAppOnCrash 列");
            }
            if (!hasAutoRestartServerOnCrash)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN AutoRestartServerOnCrash INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.AutoRestartServerOnCrash 列");
            }
            if (!hasStartOnBoot)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN StartOnBoot INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.StartOnBoot 列");
            }
            if (!hasDownloadThreads)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN DownloadThreads INTEGER NOT NULL DEFAULT 8";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.DownloadThreads 列");
            }
            if (!hasForceMultiThread)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN ForceMultiThread INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.ForceMultiThread 列");
            }
            if (!hasConsoleEncoding)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN ConsoleEncoding TEXT DEFAULT 'UTF-8'";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.ConsoleEncoding 列");
            }
            
            if (!hasConsoleFontSize)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN ConsoleFontSize INTEGER NOT NULL DEFAULT 12";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.ConsoleFontSize 列");
            }

            if (!hasUseCardView)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN UseCardView INTEGER NOT NULL DEFAULT 1";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.UseCardView 列");
            }
            
            if (!hasEnableMicaEffect)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN EnableMicaEffect INTEGER NOT NULL DEFAULT 1";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.EnableMicaEffect 列");
            }
            if (!hasUseCustomBackground)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN UseCustomBackground INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.UseCustomBackground 列");
            }
            if (!hasBackgroundImagePath)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN BackgroundImagePath TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.BackgroundImagePath 列");
            }
            if (!hasMicaIntensity)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN MicaIntensity INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.MicaIntensity 列");
            }

            // 检查镜像源设置列
            var hasDownloadMirrorSource = false;
            var hasMeFrpToken = false;
            var hasStarryFrpToken = false;
            var checkMirrorSourceColumns = connection.CreateCommand();
            checkMirrorSourceColumns.CommandText = "PRAGMA table_info(Settings)";
            using (var reader = checkMirrorSourceColumns.ExecuteReader())
            {
                while (reader.Read())
                {
                    var columnName = reader.GetString(1);
                    if (columnName == "DownloadMirrorSource") hasDownloadMirrorSource = true;
                    if (columnName == "MeFrpToken") hasMeFrpToken = true;
                    if (columnName == "StarryFrpToken") hasStarryFrpToken = true;
                }
            }

            if (!hasDownloadMirrorSource)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN DownloadMirrorSource TEXT DEFAULT 'MSL'";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.DownloadMirrorSource 列");
            }

            if (!hasMeFrpToken)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN MeFrpToken TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.MeFrpToken 列");
            }

            if (!hasStarryFrpToken)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Settings ADD COLUMN StarryFrpToken TEXT";
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已添加 Settings.StarryFrpToken 列");
            }
            
            // 检查 Backups 表是否存在
            var checkBackupsTable = connection.CreateCommand();
            checkBackupsTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Backups'";
            var backupsTableExists = checkBackupsTable.ExecuteScalar() != null;
            
            if (!backupsTableExists)
            {
                var createBackupsTable = connection.CreateCommand();
                createBackupsTable.CommandText = @"
                    CREATE TABLE Backups (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ServerId INTEGER NOT NULL,
                        ServerName TEXT NOT NULL,
                        BackupPath TEXT NOT NULL,
                        FileSizeBytes INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL
                    )";
                createBackupsTable.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已创建 Backups 表");
            }
            
            // 检查 LinuxNodes 表是否存在
            var checkNodesTable = connection.CreateCommand();
            checkNodesTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='LinuxNodes'";
            var nodesTableExists = checkNodesTable.ExecuteScalar() != null;
            
            if (!nodesTableExists)
            {
                var createNodesTable = connection.CreateCommand();
                createNodesTable.CommandText = @"
                    CREATE TABLE LinuxNodes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Host TEXT NOT NULL,
                        Port INTEGER NOT NULL DEFAULT 8080,
                        Token TEXT NOT NULL,
                        IsOnline INTEGER NOT NULL DEFAULT 0,
                        Platform INTEGER NOT NULL DEFAULT 0,
                        CreatedAt TEXT NOT NULL,
                        LastConnectedAt TEXT
                    )";
                createNodesTable.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已创建 LinuxNodes 表");
            }
            else
            {
                // 检查 LinuxNodes 缺失的列
                var checkColumns = connection.CreateCommand();
                checkColumns.CommandText = "PRAGMA table_info(LinuxNodes)";
                var hasToken = false;
                var hasIsOnline = false;
                var hasLastConnectedAt = false;
                var hasPlatform = false;
                
                using (var reader = checkColumns.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var colName = reader.GetString(1);
                        if (colName == "Token") hasToken = true;
                        if (colName == "IsOnline") hasIsOnline = true;
                        if (colName == "LastConnectedAt") hasLastConnectedAt = true;
                        if (colName == "Platform") hasPlatform = true;
                    }
                }
                
                if (!hasToken)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "ALTER TABLE LinuxNodes ADD COLUMN Token TEXT NOT NULL DEFAULT ''";
                    cmd.ExecuteNonQuery();
                }
                if (!hasIsOnline)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "ALTER TABLE LinuxNodes ADD COLUMN IsOnline INTEGER NOT NULL DEFAULT 0";
                    cmd.ExecuteNonQuery();
                }
                if (!hasLastConnectedAt)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "ALTER TABLE LinuxNodes ADD COLUMN LastConnectedAt TEXT";
                    cmd.ExecuteNonQuery();
                }
                if (!hasPlatform)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "ALTER TABLE LinuxNodes ADD COLUMN Platform INTEGER NOT NULL DEFAULT 0";
                    cmd.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine("已添加 Platform 列");
                }
            }

            // 检查 FrpTunnels 表是否存在
            var checkFrpTunnelsTable = connection.CreateCommand();
            checkFrpTunnelsTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='FrpTunnels'";
            var frpTunnelsTableExists = checkFrpTunnelsTable.ExecuteScalar() != null;
            
            if (!frpTunnelsTableExists)
            {
                var createFrpTunnelsTable = connection.CreateCommand();
                createFrpTunnelsTable.CommandText = @"
                    CREATE TABLE FrpTunnels (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RemoteTunnelId INTEGER NOT NULL,
                        LocalServerId INTEGER NOT NULL,
                        Name TEXT NOT NULL,
                        NodeHost TEXT NOT NULL,
                        NodePort INTEGER NOT NULL,
                        Token TEXT NOT NULL,
                        Protocol TEXT NOT NULL DEFAULT 'tcp',
                        LocalPort INTEGER NOT NULL DEFAULT 25565,
                        RemotePort INTEGER NOT NULL,
                        ConnectAddress TEXT NOT NULL,
                        EnableProxyProtocol INTEGER NOT NULL DEFAULT 0,
                        CreatedAt TEXT NOT NULL
                    )";
                createFrpTunnelsTable.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已创建 FrpTunnels 表");
            }
            else
            {
                // 检查 FrpTunnels 缺失的列
                var checkColumns = connection.CreateCommand();
                checkColumns.CommandText = "PRAGMA table_info(FrpTunnels)";
                var hasEnableProxyProtocol = false;
                
                using (var reader = checkColumns.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var colName = reader.GetString(1);
                        if (colName == "EnableProxyProtocol") hasEnableProxyProtocol = true;
                    }
                }
                
                if (!hasEnableProxyProtocol)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "ALTER TABLE FrpTunnels ADD COLUMN EnableProxyProtocol INTEGER NOT NULL DEFAULT 0";
                    cmd.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine("已添加 FrpTunnels.EnableProxyProtocol 列");
                }
            }
            
            // 检查 RemoteServers 表是否存在
            var checkRemoteServersTable = connection.CreateCommand();
            checkRemoteServersTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='RemoteServers'";
            var remoteServersTableExists = checkRemoteServersTable.ExecuteScalar() != null;
            
            if (!remoteServersTableExists)
            {
                var createRemoteServersTable = connection.CreateCommand();
                createRemoteServersTable.CommandText = @"
                    CREATE TABLE RemoteServers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        NodeId INTEGER NOT NULL,
                        RemoteServerId TEXT NOT NULL,
                        Name TEXT NOT NULL,
                        CoreType TEXT NOT NULL,
                        MinecraftVersion TEXT NOT NULL,
                        JarFileName TEXT NOT NULL,
                        MinMemoryMB INTEGER NOT NULL DEFAULT 1024,
                        MaxMemoryMB INTEGER NOT NULL DEFAULT 2048,
                        Port INTEGER NOT NULL DEFAULT 25565,
                        IsRunning INTEGER NOT NULL DEFAULT 0,
                        CreatedAt TEXT NOT NULL
                    )";
                createRemoteServersTable.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已创建 RemoteServers 表");
            }
            else
            {
                // 检查 RemoteServers 缺失的列
                var checkColumns = connection.CreateCommand();
                checkColumns.CommandText = "PRAGMA table_info(RemoteServers)";
                var hasIsRunning = false;
                var hasJavaPath = false;
                var hasJvmArgs = false;
                var hasAutoRestart = false;
                
                using (var reader = checkColumns.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var colName = reader.GetString(1);
                        if (colName == "IsRunning") hasIsRunning = true;
                        if (colName == "JavaPath") hasJavaPath = true;
                        if (colName == "JvmArgs") hasJvmArgs = true;
                        if (colName == "AutoRestart") hasAutoRestart = true;
                    }
                }
                
                if (!hasIsRunning)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "ALTER TABLE RemoteServers ADD COLUMN IsRunning INTEGER NOT NULL DEFAULT 0";
                    cmd.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine("已添加 RemoteServers.IsRunning 列");
                }

                if (!hasJavaPath)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "ALTER TABLE RemoteServers ADD COLUMN JavaPath TEXT";
                    cmd.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine("已添加 RemoteServers.JavaPath 列");
                }

                if (!hasJvmArgs)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "ALTER TABLE RemoteServers ADD COLUMN JvmArgs TEXT NOT NULL DEFAULT ''";
                    cmd.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine("已添加 RemoteServers.JvmArgs 列");
                }

                if (!hasAutoRestart)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "ALTER TABLE RemoteServers ADD COLUMN AutoRestart INTEGER NOT NULL DEFAULT 0";
                    cmd.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine("已添加 RemoteServers.AutoRestart 列");
                }
            }

            // 检查 JavaInstallations 表是否存在
            var checkJavaTable = connection.CreateCommand();
            checkJavaTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='JavaInstallations'";
            var javaTableExists = checkJavaTable.ExecuteScalar() != null;
            
            if (!javaTableExists)
            {
                var createJavaTable = connection.CreateCommand();
                createJavaTable.CommandText = @"
                    CREATE TABLE JavaInstallations (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Path TEXT NOT NULL,
                        Version INTEGER NOT NULL,
                        Source TEXT NOT NULL,
                        DetectedAt TEXT NOT NULL,
                        IsValid INTEGER NOT NULL DEFAULT 1
                    )";
                createJavaTable.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已创建 JavaInstallations 表");
            }

            // 检查 Downloads 表是否存在
            var checkDownloadsTable = connection.CreateCommand();
            checkDownloadsTable.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Downloads'";
            var downloadsTableExists = checkDownloadsTable.ExecuteScalar() != null;
            
            if (!downloadsTableExists)
            {
                var createDownloadsTable = connection.CreateCommand();
                createDownloadsTable.CommandText = @"
                    CREATE TABLE Downloads (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Type TEXT NOT NULL,
                        Name TEXT NOT NULL,
                        Version TEXT NOT NULL,
                        Url TEXT NOT NULL,
                        LocalPath TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        TotalBytes INTEGER NOT NULL,
                        DownloadedBytes INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        CompletedAt TEXT
                    )";
                createDownloadsTable.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("已创建 Downloads 表");
            }
        }
        finally
        {
            if (opened)
            {
                connection.Close();
            }
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalServer>().HasKey(s => s.Id);
        modelBuilder.Entity<DownloadRecord>().HasKey(d => d.Id);
        modelBuilder.Entity<LocalFrpTunnel>().HasKey(t => t.Id);
        modelBuilder.Entity<AppSettings>().HasKey(s => s.Id);
        modelBuilder.Entity<ServerBackup>().HasKey(b => b.Id);
        modelBuilder.Entity<LinuxNode>().HasKey(n => n.Id);
        modelBuilder.Entity<RemoteServer>().HasKey(r => r.Id);
        modelBuilder.Entity<ZMSL.App.Models.JavaInfo>().HasKey(j => j.Id);

        // 初始化默认设置
        modelBuilder.Entity<AppSettings>().HasData(new AppSettings
        {
            Id = 1,
            DefaultServerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ZMSL", "Servers"),
            ApiBaseUrl = "http://localhost:5000",
            EnableAutoBackup = true,
            BackupIntervalMinutes = 60,
            BackupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ZMSL", "backups"),
            BackupRetentionCount = 7,
            // 新增字段默认值
            AutoStartLastServer = false,
            AutoRestartAppOnCrash = false,
            AutoRestartServerOnCrash = false,
            StartOnBoot = false,
            DownloadThreads = 8,
            ForceMultiThread = false,
            ConsoleEncoding = "UTF-8",
            UseCardView = true,
            EnableMicaEffect = true,
            UseCustomBackground = false,
            BackgroundImagePath = null,
            MicaIntensity = 0
        });
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        await InitializeAsync();
        await _lock.WaitAsync();
        try
        {
            var settings = await Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new AppSettings
                {
                    DefaultServerPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "ZMSL", "Servers"),
                    ApiBaseUrl = "http://localhost:5000",
                    EnableAutoBackup = true,
                    BackupIntervalMinutes = 60,
                    BackupDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "ZMSL", "backups"),
                    BackupRetentionCount = 7
                };
                Settings.Add(settings);
                await SaveChangesAsync();
            }
            else
            {
                // 确保新增字段有合理默认值
                if (string.IsNullOrWhiteSpace(settings.BackupDirectory))
                {
                    settings.BackupDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "ZMSL", "backups");
                }
                if (settings.BackupIntervalMinutes <= 0)
                {
                    settings.BackupIntervalMinutes = 60;
                }
                if (settings.BackupRetentionCount <= 0)
                {
                    settings.BackupRetentionCount = 7;
                }
                if (settings.DownloadThreads <= 0)
                {
                    settings.DownloadThreads = 8;
                }
                if (string.IsNullOrEmpty(settings.ConsoleEncoding))
                {
                    settings.ConsoleEncoding = "UTF-8";
                }
            }
            return settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await InitializeAsync();
        await _lock.WaitAsync();
        try
        {
            var existing = await Settings.FirstOrDefaultAsync();
            if (existing != null)
            {
                Entry(existing).CurrentValues.SetValues(settings);
            }
            else
            {
                Settings.Add(settings);
            }
            await SaveChangesAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> ExecuteWithLockAsync<T>(Func<DatabaseService, Task<T>> action)
    {
        await InitializeAsync();
        await _lock.WaitAsync();
        try
        {
            // 创建新的瞬态上下文以避免 ChangeTracker 内存泄漏
            using var transientContext = new DatabaseService(true);
            // 确保使用相同的连接字符串/路径
            // 由于无参构造函数已经设置了路径，这里直接使用即可
            // 如果需要确保连接复用，可以在这里手动配置，但在 SQLite 文件模式下，短连接通常也是安全的
            
            return await action(transientContext);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ExecuteWithLockAsync(Func<DatabaseService, Task> action)
    {
        await InitializeAsync();
        await _lock.WaitAsync();
        try
        {
            using var transientContext = new DatabaseService(true);
            await action(transientContext);
        }
        finally
        {
            _lock.Release();
        }
    }
}
