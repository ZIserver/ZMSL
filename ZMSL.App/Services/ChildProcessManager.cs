using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZMSL.App.Services;

/// <summary>
/// 子进程管理器 - 使用 Windows Job Object 确保子进程随父进程一起终止
/// 防止启动器崩溃后服务器/FRP进程仍在运行导致端口占用
/// </summary>
public class ChildProcessManager : IDisposable
{
    private IntPtr _jobHandle;
    private bool _disposed;
    private static ChildProcessManager? _instance;
    private static readonly object _lock = new();

    public static ChildProcessManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ChildProcessManager();
                }
            }
            return _instance;
        }
    }

    private ChildProcessManager()
    {
        _jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建 Job Object");
        }

        // 设置 Job Object 属性：当 Job 关闭时终止所有子进程
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr infoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, infoPtr, false);
            if (!SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, infoPtr, (uint)length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置 Job Object 信息");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }

        Debug.WriteLine("[ChildProcessManager] Job Object 创建成功");
    }

    /// <summary>
    /// 将进程添加到 Job Object，使其成为启动器的子进程
    /// </summary>
    public bool AddProcess(Process process)
    {
        if (_disposed || _jobHandle == IntPtr.Zero)
        {
            Debug.WriteLine($"[ChildProcessManager] Job Object 未初始化或已释放");
            return false;
        }

        try
        {
            // 获取当前进程ID用于调试
            var currentPid = Environment.ProcessId;
            Debug.WriteLine($"[ChildProcessManager] 当前进程 PID: {currentPid}, 子进程 PID: {process.Id}");

            bool result = AssignProcessToJobObject(_jobHandle, process.Handle);
            if (result)
            {
                Debug.WriteLine($"[ChildProcessManager] 进程 {process.Id} ({process.ProcessName}) 已成功添加到 Job Object");
            }
            else
            {
                var error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[ChildProcessManager] 添加进程 {process.Id} 失败, 错误码: {error}");

                // 错误码 5 = 拒绝访问，可能是进程已经在另一个 Job 中
                if (error == 5)
                {
                    Debug.WriteLine($"[ChildProcessManager] 进程可能已经属于另一个 Job Object");
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChildProcessManager] 添加进程异常: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 终止所有子进程
    /// </summary>
    public void TerminateAll()
    {
        if (_disposed || _jobHandle == IntPtr.Zero)
            return;

        try
        {
            TerminateJobObject(_jobHandle, 0);
            Debug.WriteLine("[ChildProcessManager] 已终止所有子进程");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChildProcessManager] 终止子进程异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取 Job Object 中的进程数量（用于调试）
    /// </summary>
    public int GetProcessCount()
    {
        if (_disposed || _jobHandle == IntPtr.Zero)
            return 0;

        try
        {
            var info = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
            int size = Marshal.SizeOf(info);
            IntPtr infoPtr = Marshal.AllocHGlobal(size);
            try
            {
                if (QueryInformationJobObject(_jobHandle, JobObjectInfoType.BasicAccountingInformation, infoPtr, (uint)size, out _))
                {
                    info = Marshal.PtrToStructure<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(infoPtr);
                    return (int)info.ActiveProcesses;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChildProcessManager] 获取进程数量异常: {ex.Message}");
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }

        Debug.WriteLine("[ChildProcessManager] 已释放 Job Object");
    }

    #region Win32 API

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength, out uint lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JobObjectInfoType
    {
        BasicAccountingInformation = 1,
        BasicLimitInformation = 2,
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    #endregion
}
