using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.System.Diagnostics;

namespace ZMSL.App.Services;

/// <summary>
/// 采样本机应用及子进程（服务器）的 CPU 与内存占用
/// </summary>
public class SystemResourceSampler
{
    private readonly ServerManagerService _serverManager;
    private readonly Process _currentProcess;
    
    // 存储每个进程上一次的 TotalProcessorTime
    private readonly Dictionary<int, TimeSpan> _lastCpuTimes = new();
    
    private DateTime _lastSampleTime;
    private bool _firstSample = true;
    
    // 系统总物理内存（用于计算内存百分比）
    private ulong _totalPhysicalMemory;

    // P/Invoke for Memory Status
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public SystemResourceSampler(ServerManagerService serverManager)
    {
        _serverManager = serverManager;
        _currentProcess = Process.GetCurrentProcess();
        _lastSampleTime = DateTime.UtcNow;
        
        // 获取系统总内存
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                _totalPhysicalMemory = memStatus.ullTotalPhys;
            }
        }
        catch { }
        
        if (_totalPhysicalMemory == 0) _totalPhysicalMemory = 16UL * 1024 * 1024 * 1024; // 默认16G兜底
    }

    /// <summary>
    /// 返回 (App+Servers CPU百分比 0-100, App+Servers 内存百分比 0-100)
    /// </summary>
    public (double CpuPercent, double MemoryPercent) Sample()
    {
        var now = DateTime.UtcNow;
        var realElapsed = (now - _lastSampleTime).TotalSeconds;
        
        // 收集所有需要监控的进程
        var processesToMonitor = new List<Process> { _currentProcess };
        try
        {
            processesToMonitor.AddRange(_serverManager.GetRunningProcesses());
        }
        catch { }

        // 1. 计算总内存占用
        long totalWorkingSet = 0;
        foreach (var p in processesToMonitor)
        {
            try
            {
                p.Refresh();
                totalWorkingSet += p.WorkingSet64;
            }
            catch { }
        }
        
        double memoryPercent = 0;
        if (_totalPhysicalMemory > 0)
        {
            memoryPercent = (double)totalWorkingSet / _totalPhysicalMemory * 100;
        }

        // 2. 计算总 CPU 占用
        double totalCpuPercent = 0;
        
        if (realElapsed > 0.1 && !_firstSample)
        {
            double totalCpuDelta = 0;
            var currentProcessIds = new HashSet<int>();

            foreach (var p in processesToMonitor)
            {
                try
                {
                    currentProcessIds.Add(p.Id);
                    var currentTotalProcessorTime = p.TotalProcessorTime;

                    if (_lastCpuTimes.TryGetValue(p.Id, out var lastTime))
                    {
                        var delta = (currentTotalProcessorTime - lastTime).TotalSeconds;
                        if (delta > 0)
                        {
                            totalCpuDelta += delta;
                        }
                    }
                    
                    _lastCpuTimes[p.Id] = currentTotalProcessorTime;
                }
                catch 
                {
                    // 进程可能已退出
                }
            }
            
            // 清理已退出的进程记录
            var idsToRemove = _lastCpuTimes.Keys.Where(id => !currentProcessIds.Contains(id)).ToList();
            foreach (var id in idsToRemove) _lastCpuTimes.Remove(id);

            // CPU % = (Total CPU Time Delta) / (Real Time Delta * Processor Count)
            totalCpuPercent = (totalCpuDelta / (realElapsed * Environment.ProcessorCount)) * 100;
            totalCpuPercent = Math.Min(100, Math.Max(0, totalCpuPercent));
        }
        else
        {
            // 初始化/重置
            _lastCpuTimes.Clear();
            foreach (var p in processesToMonitor)
            {
                try { _lastCpuTimes[p.Id] = p.TotalProcessorTime; } catch { }
            }
        }

        _firstSample = false;
        _lastSampleTime = now;

        return (totalCpuPercent, memoryPercent);
    }
}
