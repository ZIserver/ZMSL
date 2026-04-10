using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace ZMSL.App.Services
{
    /// <summary>
    /// 插件自动更新检测服务（简化版）
    /// </summary>
    public class PluginUpdateService
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Dictionary<string, PluginInfo> _installedPlugins;
        private readonly System.Timers.Timer _updateCheckTimer;
        private readonly string _pluginsDirectory;

        public PluginUpdateService(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
            _installedPlugins = new Dictionary<string, PluginInfo>();
            _updateCheckTimer = new System.Timers.Timer(3600000); // 每小时检查一次
            _updateCheckTimer.Elapsed += CheckForUpdates;
            _updateCheckTimer.AutoReset = true;
            
            _pluginsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ZMSL", "Plugins");
            
            Directory.CreateDirectory(_pluginsDirectory);
        }

        private async void CheckForUpdates(object? sender, System.Timers.ElapsedEventArgs e)
        {
            await CheckForUpdatesAsync();
        }

        /// <summary>
        /// 启动插件更新服务
        /// </summary>
        public void Start()
        {
            _updateCheckTimer.Start();
        }

        /// <summary>
        /// 停止插件更新服务
        /// </summary>
        public void Stop()
        {
            _updateCheckTimer.Stop();
        }

        /// <summary>
        /// 添加已安装插件
        /// </summary>
        public void AddInstalledPlugin(string pluginName, string version, string pluginPath)
        {
            _installedPlugins[pluginName] = new PluginInfo
            {
                Name = pluginName,
                CurrentVersion = version,
                InstalledPath = pluginPath,
                LastChecked = DateTime.Now,
                UpdateAvailable = false
            };
        }

        /// <summary>
        /// 获取所有已安装插件
        /// </summary>
        public List<PluginInfo> GetInstalledPlugins()
        {
            return new List<PluginInfo>(_installedPlugins.Values);
        }

        /// <summary>
        /// 手动检查更新
        /// </summary>
        public async void CheckForUpdatesManually()
        {
            await Task.Run(async () => 
            {
                await CheckForUpdatesAsync();
            });
        }

        /// <summary>
        /// 检查插件更新
        /// </summary>
        public Task CheckForUpdatesAsync()
        {
            // 简化的更新检查逻辑
            foreach (var plugin in _installedPlugins.Values)
            {
                // 模拟检查更新
                plugin.LastChecked = DateTime.Now;
            }

            // 触发更新事件
            OnPluginsUpdated?.Invoke(this, new PluginUpdateEventArgs
            {
                CheckTime = DateTime.Now
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// 扫描已安装的插件
        /// </summary>
        public void ScanInstalledPlugins()
        {
            try
            {
                var pluginFiles = Directory.GetFiles(_pluginsDirectory, "*.jar");
                
                foreach (var pluginFile in pluginFiles)
                {
                    var pluginName = Path.GetFileNameWithoutExtension(pluginFile);
                    var version = "1.0.0"; // 默认版本
                    
                    if (!_installedPlugins.ContainsKey(pluginName))
                    {
                        AddInstalledPlugin(pluginName, version, pluginFile);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"扫描插件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 插件更新事件
        /// </summary>
        public event EventHandler<PluginUpdateEventArgs>? OnPluginsUpdated;
    }

    /// <summary>
    /// 插件信息
    /// </summary>
    public class PluginInfo
    {
        public required string Name { get; set; }
        public required string CurrentVersion { get; set; }
        public required string InstalledPath { get; set; }
        public bool UpdateAvailable { get; set; }
        public DateTime LastChecked { get; set; }
        public string? LatestVersion { get; set; }
        public string? UpdateUrl { get; set; }
    }

    /// <summary>
    /// 插件更新事件参数
    /// </summary>
    public class PluginUpdateEventArgs : EventArgs
    {
        public DateTime CheckTime { get; set; }
    }
}