using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using ZMSL.App.Models;

namespace ZMSL.App.Services
{
    /// <summary>
    /// 服务器定时重启服务
    /// </summary>
    public class ScheduledRestartService
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Dictionary<int, ScheduledRestartTask> _scheduledTasks;
        private readonly System.Timers.Timer _checkTimer;

        public ScheduledRestartService(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
            _scheduledTasks = new Dictionary<int, ScheduledRestartTask>();
            _checkTimer = new System.Timers.Timer(60000); // 每分钟检查一次
            _checkTimer.Elapsed += CheckScheduledRestarts;
            _checkTimer.AutoReset = true;
        }

        /// <summary>
        /// 启动定时重启服务
        /// </summary>
        public void Start()
        {
            _checkTimer.Start();
        }

        /// <summary>
        /// 停止定时重启服务
        /// </summary>
        public void Stop()
        {
            _checkTimer.Stop();
        }

        /// <summary>
        /// 添加定时重启任务
        /// </summary>
        /// <param name="serverId">服务器ID</param>
        /// <param name="restartTime">重启时间</param>
        /// <param name="enabled">是否启用</param>
        /// <returns>任务ID</returns>
        public int AddScheduledRestart(int serverId, TimeSpan restartTime, bool enabled = true)
        {
            var taskId = GenerateTaskId();
            var task = new ScheduledRestartTask
            {
                Id = taskId,
                ServerId = serverId,
                RestartTime = restartTime,
                Enabled = enabled,
                LastRunDate = DateTime.MinValue
            };

            _scheduledTasks[taskId] = task;
            return taskId;
        }

        /// <summary>
        /// 更新定时重启任务
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="restartTime">新的重启时间</param>
        /// <param name="enabled">是否启用</param>
        public void UpdateScheduledRestart(int taskId, TimeSpan restartTime, bool enabled)
        {
            if (_scheduledTasks.ContainsKey(taskId))
            {
                _scheduledTasks[taskId].RestartTime = restartTime;
                _scheduledTasks[taskId].Enabled = enabled;
            }
        }

        /// <summary>
        /// 删除定时重启任务
        /// </summary>
        /// <param name="taskId">任务ID</param>
        public void RemoveScheduledRestart(int taskId)
        {
            _scheduledTasks.Remove(taskId);
        }

        /// <summary>
        /// 获取所有定时重启任务
        /// </summary>
        /// <returns>任务列表</returns>
        public List<ScheduledRestartTask> GetAllTasks()
        {
            return new List<ScheduledRestartTask>(_scheduledTasks.Values);
        }

        /// <summary>
        /// 获取指定服务器的定时重启任务
        /// </summary>
        /// <param name="serverId">服务器ID</param>
        /// <returns>任务列表</returns>
        public List<ScheduledRestartTask> GetTasksForServer(int serverId)
        {
            var tasks = new List<ScheduledRestartTask>();
            foreach (var task in _scheduledTasks.Values)
            {
                if (task.ServerId == serverId)
                {
                    tasks.Add(task);
                }
            }
            return tasks;
        }

        /// <summary>
        /// 检查并执行到期的重启任务
        /// </summary>
        private async void CheckScheduledRestarts(object? sender, System.Timers.ElapsedEventArgs e)
        {
            await CheckScheduledRestartsAsync();
        }

        private async Task CheckScheduledRestartsAsync()
        {
            var now = DateTime.Now;
            var tasksToExecute = new List<ScheduledRestartTask>();
            await Task.CompletedTask; // Placeholder for async work if needed later

            foreach (var task in _scheduledTasks.Values)
            {
                if (!task.Enabled) continue;

                // 检查是否到了重启时间
                var scheduledDateTime = new DateTime(now.Year, now.Month, now.Day, 
                    task.RestartTime.Hours, task.RestartTime.Minutes, task.RestartTime.Seconds);

                // 如果今天已经执行过了，跳过
                if (task.LastRunDate.Date == now.Date) continue;

                // 如果当前时间超过了预定时间，则执行重启
                if (now >= scheduledDateTime)
                {
                    tasksToExecute.Add(task);
                }
            }

            // 在UI线程中执行重启操作
            foreach (var task in tasksToExecute)
            {
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    await ExecuteServerRestart(task);
                });
            }
        }

        /// <summary>
        /// 执行服务器重启
        /// </summary>
        /// <param name="task">重启任务</param>
        private async Task ExecuteServerRestart(ScheduledRestartTask task)
        {
            try
            {
                // 这里需要调用实际的服务器管理服务来执行重启
                // 暂时使用模拟实现
                await SimulateServerRestart(task.ServerId);
                
                task.LastRunDate = DateTime.Now;
                
                // 触发重启完成事件
                OnServerRestarted?.Invoke(this, new ServerRestartEventArgs
                {
                    ServerId = task.ServerId,
                    RestartTime = task.RestartTime,
                    ExecutionTime = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                // 记录错误日志
                System.Diagnostics.Debug.WriteLine($"服务器 {task.ServerId} 重启失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 模拟服务器重启（实际实现时需要替换为真实的重启逻辑）
        /// </summary>
        /// <param name="serverId">服务器ID</param>
        private async Task SimulateServerRestart(int serverId)
        {
            // 模拟重启过程，实际应该调用服务器管理服务
            await Task.Delay(2000); // 模拟2秒重启时间
            System.Diagnostics.Debug.WriteLine($"服务器 {serverId} 已重启");
        }

        /// <summary>
        /// 生成唯一任务ID
        /// </summary>
        private int GenerateTaskId()
        {
            return _scheduledTasks.Count > 0 ? _scheduledTasks.Keys.Max() + 1 : 1;
        }

        /// <summary>
        /// 服务器重启事件
        /// </summary>
        public event EventHandler<ServerRestartEventArgs>? OnServerRestarted;
    }

    /// <summary>
    /// 定时重启任务
    /// </summary>
    public class ScheduledRestartTask
    {
        public int Id { get; set; }
        public int ServerId { get; set; }
        public TimeSpan RestartTime { get; set; }
        public bool Enabled { get; set; }
        public DateTime LastRunDate { get; set; }
        public string Description => $"每天 {RestartTime:hh\\:mm} 重启服务器 {ServerId}";
    }

    /// <summary>
    /// 服务器重启事件参数
    /// </summary>
    public class ServerRestartEventArgs : EventArgs
    {
        public int ServerId { get; set; }
        public TimeSpan RestartTime { get; set; }
        public DateTime ExecutionTime { get; set; }
    }
}