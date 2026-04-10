using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using ZMSL.App.Models;

namespace ZMSL.App.Services
{
    /// <summary>
    /// 资源监控告警服务
    /// </summary>
    public class ResourceMonitoringService
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Dictionary<int, ResourceAlertRule> _alertRules;
        private readonly System.Timers.Timer _monitorTimer;
        private readonly SystemResourceSampler _resourceSampler;
        private readonly Dictionary<int, ServerResourceHistory> _resourceHistories;
        
        /// <summary>
        /// 资源告警事件
        /// </summary>
        public event EventHandler<ResourceAlertEventArgs>? OnResourceAlert;

        public ResourceMonitoringService(DispatcherQueue dispatcherQueue, SystemResourceSampler resourceSampler)
        {
            _dispatcherQueue = dispatcherQueue;
            _resourceSampler = resourceSampler;
            _alertRules = new Dictionary<int, ResourceAlertRule>();
            _resourceHistories = new Dictionary<int, ServerResourceHistory>();
            _monitorTimer = new System.Timers.Timer(30000); // 每30秒检查一次
            _monitorTimer.Elapsed += MonitorResources;
            _monitorTimer.AutoReset = true;
        }

        /// <summary>
        /// 启动资源监控服务
        /// </summary>
        public void Start()
        {
            _monitorTimer.Start();
        }

        /// <summary>
        /// 停止资源监控服务
        /// </summary>
        public void Stop()
        {
            _monitorTimer.Stop();
        }

        /// <summary>
        /// 添加资源告警规则
        /// </summary>
        public int AddAlertRule(int serverId, ResourceType resourceType, double threshold, 
            ComparisonOperator comparison, bool enabled = true)
        {
            var ruleId = GenerateRuleId();
            var rule = new ResourceAlertRule
            {
                Id = ruleId,
                ServerId = serverId,
                ResourceType = resourceType,
                Threshold = threshold,
                Comparison = comparison,
                Enabled = enabled,
                LastTriggered = DateTime.MinValue,
                TriggerCount = 0
            };

            _alertRules[ruleId] = rule;
            return ruleId;
        }

        /// <summary>
        /// 更新告警规则
        /// </summary>
        public void UpdateAlertRule(int ruleId, double threshold, ComparisonOperator comparison, bool enabled)
        {
            if (_alertRules.ContainsKey(ruleId))
            {
                _alertRules[ruleId].Threshold = threshold;
                _alertRules[ruleId].Comparison = comparison;
                _alertRules[ruleId].Enabled = enabled;
            }
        }

        /// <summary>
        /// 删除告警规则
        /// </summary>
        public void RemoveAlertRule(int ruleId)
        {
            _alertRules.Remove(ruleId);
        }

        /// <summary>
        /// 获取所有告警规则
        /// </summary>
        public List<ResourceAlertRule> GetAllRules()
        {
            return new List<ResourceAlertRule>(_alertRules.Values);
        }

        /// <summary>
        /// 获取指定服务器的告警规则
        /// </summary>
        public List<ResourceAlertRule> GetRulesForServer(int serverId)
        {
            var rules = new List<ResourceAlertRule>();
            foreach (var rule in _alertRules.Values)
            {
                if (rule.ServerId == serverId)
                {
                    rules.Add(rule);
                }
            }
            return rules;
        }

        /// <summary>
        /// 监控资源使用情况
        /// </summary>
        private async void MonitorResources(object? sender, System.Timers.ElapsedEventArgs e)
        {
            await MonitorResourcesAsync();
        }

        private async Task MonitorResourcesAsync()
        {
            var alertsToTrigger = new List<(ResourceAlertRule Rule, double CurrentValue)>();
            await Task.CompletedTask; // Placeholder for async work if needed later

            try
            {
                // Copy values to avoid collection modification issues
                List<ResourceAlertRule> rules;
                lock(_alertRules)
                {
                    rules = _alertRules.Values.ToList();
                }
                 
                foreach (var rule in rules)
                {
                    if (!rule.Enabled) continue;

                    // 获取当前资源使用情况
                    var (cpuPercent, memoryPercent) = _resourceSampler.Sample();
                    
                    double currentValue = 0;
                    switch (rule.ResourceType)
                    {
                        case ResourceType.CPU:
                            currentValue = cpuPercent;
                            break;
                        case ResourceType.Memory:
                            currentValue = memoryPercent;
                            break;
                    }

                    // 检查是否触发告警
                    if (ShouldTriggerAlert(rule, currentValue))
                    {
                        alertsToTrigger.Add((rule, currentValue));
                    }

                    // 记录资源历史
                    RecordResourceHistory(rule.ServerId, cpuPercent, memoryPercent);
                }

                // 在UI线程中触发告警
                foreach (var (rule, currentValue) in alertsToTrigger)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        TriggerAlert(rule, currentValue);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"监控资源失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 判断是否应该触发告警
        /// </summary>
        private bool ShouldTriggerAlert(ResourceAlertRule rule, double currentValue)
        {
            // 检查冷却期（避免频繁告警）
            if ((DateTime.Now - rule.LastTriggered).TotalMinutes < 5)
            {
                return false;
            }

            bool shouldTrigger = false;
            switch (rule.Comparison)
            {
                case ComparisonOperator.GreaterThan:
                    shouldTrigger = currentValue > rule.Threshold;
                    break;
                case ComparisonOperator.LessThan:
                    shouldTrigger = currentValue < rule.Threshold;
                    break;
                case ComparisonOperator.EqualTo:
                    shouldTrigger = Math.Abs(currentValue - rule.Threshold) < 0.1;
                    break;
            }

            if (shouldTrigger)
            {
                rule.LastTriggered = DateTime.Now;
                rule.TriggerCount++;
            }

            return shouldTrigger;
        }

        /// <summary>
        /// 触发告警
        /// </summary>
        private void TriggerAlert(ResourceAlertRule rule, double currentValue)
        {
            var alertMessage = $"服务器 {rule.ServerId} {rule.ResourceType} 使用率达到 {currentValue:F1}%，" +
                              $"已超过设定阈值 {rule.Threshold}%";

            // 触发告警事件
            OnResourceAlert?.Invoke(this, new ResourceAlertEventArgs
            {
                Rule = rule,
                CurrentValue = currentValue,
                AlertTime = DateTime.Now,
                Message = alertMessage
            });

            // 这里可以添加更多告警方式，如：
            // - 系统通知
            // - 邮件通知
            // - 短信通知
            // - 日志记录
            System.Diagnostics.Debug.WriteLine(alertMessage);
        }

        /// <summary>
        /// 记录资源使用历史
        /// </summary>
        private void RecordResourceHistory(int serverId, double cpuPercent, double memoryPercent)
        {
            if (!_resourceHistories.ContainsKey(serverId))
            {
                _resourceHistories[serverId] = new ServerResourceHistory { ServerId = serverId };
            }

            var history = _resourceHistories[serverId];
            var record = new ResourceRecord
            {
                Timestamp = DateTime.Now,
                CpuPercent = cpuPercent,
                MemoryPercent = memoryPercent
            };

            history.Records.Add(record);

            // 只保留最近24小时的数据
            var cutoffTime = DateTime.Now.AddHours(-24);
            history.Records.RemoveAll(r => r.Timestamp < cutoffTime);
        }

        /// <summary>
        /// 获取服务器资源历史数据
        /// </summary>
        public ServerResourceHistory GetResourceHistory(int serverId)
        {
            return _resourceHistories.GetValueOrDefault(serverId, 
                new ServerResourceHistory { ServerId = serverId });
        }

        /// <summary>
        /// 生成唯一规则ID
        /// </summary>
        private int GenerateRuleId()
        {
            return _alertRules.Count > 0 ? _alertRules.Keys.Max() + 1 : 1;
        }
    }

    /// <summary>
    /// 资源告警规则
    /// </summary>
    public class ResourceAlertRule
    {
        public int Id { get; set; }
        public int ServerId { get; set; }
        public ResourceType ResourceType { get; set; }
        public double Threshold { get; set; }
        public ComparisonOperator Comparison { get; set; }
        public bool Enabled { get; set; }
        public DateTime LastTriggered { get; set; }
        public int TriggerCount { get; set; }
        public string Description => $"{ResourceType} {Comparison.GetDescription()} {Threshold}%";
    }

    /// <summary>
    /// 资源类型枚举
    /// </summary>
    public enum ResourceType
    {
        CPU,
        Memory
    }

    /// <summary>
    /// 比较操作符枚举
    /// </summary>
    public enum ComparisonOperator
    {
        GreaterThan,
        LessThan,
        EqualTo
    }

    /// <summary>
    /// 资源告警事件参数
    /// </summary>
    public class ResourceAlertEventArgs : EventArgs
    {
        public required ResourceAlertRule Rule { get; set; }
        public double CurrentValue { get; set; }
        public DateTime AlertTime { get; set; }
        public required string Message { get; set; }
    }

    /// <summary>
    /// 服务器资源历史
    /// </summary>
    public class ServerResourceHistory
    {
        public int ServerId { get; set; }
        public List<ResourceRecord> Records { get; set; } = new List<ResourceRecord>();
    }

    /// <summary>
    /// 资源记录
    /// </summary>
    public class ResourceRecord
    {
        public DateTime Timestamp { get; set; }
        public double CpuPercent { get; set; }
        public double MemoryPercent { get; set; }
    }

    /// <summary>
    /// 比较操作符扩展方法
    /// </summary>
    public static class ComparisonOperatorExtensions
    {
        public static string GetDescription(this ComparisonOperator op)
        {
            return op switch
            {
                ComparisonOperator.GreaterThan => ">",
                ComparisonOperator.LessThan => "<",
                ComparisonOperator.EqualTo => "=",
                _ => ""
            };
        }
    }
}
