using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ZMSL.App.Services
{
    /// <summary>
    /// 本地日志服务，用于将日志写入文件
    /// </summary>
    public class LogService
    {
        private static LogService? _instance;
        public static LogService Instance => _instance ??= new LogService();

        private readonly string _logDirectory;
        // private readonly object _lock = new object(); // 不再需要锁
        
        // 异步日志队列
        private readonly System.Collections.Concurrent.BlockingCollection<string> _logQueue = new();
        private readonly Task _writeTask;
        private readonly System.Threading.CancellationTokenSource _cts = new();

        private LogService()
        {
            _logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
            
            // 启动后台写入任务
            _writeTask = Task.Factory.StartNew(ProcessLogQueue, TaskCreationOptions.LongRunning);
        }
        
        /// <summary>
        /// 后台处理日志队列
        /// </summary>
        private void ProcessLogQueue()
        {
            try
            {
                foreach (var logEntry in _logQueue.GetConsumingEnumerable(_cts.Token))
                {
                    try
                    {
                        var now = DateTime.Now;
                        var fileName = $"ZMSL_{now:yyyy-MM-dd}.log";
                        var filePath = Path.Combine(_logDirectory, fileName);
                        
                        File.AppendAllText(filePath, logEntry + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to write log to file: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
        }

        /// <summary>
        /// 记录普通信息
        /// </summary>
        public void Info(string message, string source = "")
        {
            WriteLog("INFO", message, source);
        }

        /// <summary>
        /// 记录警告信息
        /// </summary>
        public void Warning(string message, string source = "")
        {
            WriteLog("WARN", message, source);
        }

        /// <summary>
        /// 记录错误信息
        /// </summary>
        public void Error(string message, string source = "", Exception? ex = null)
        {
            var sb = new StringBuilder();
            sb.Append(message);
            if (ex != null)
            {
                sb.AppendLine();
                sb.Append(ex.ToString());
            }
            WriteLog("ERROR", sb.ToString(), source);
        }

        /// <summary>
        /// 记录调试信息
        /// </summary>
        public void Debug(string message, string source = "")
        {
#if DEBUG
            WriteLog("DEBUG", message, source);
#endif
        }

        private void WriteLog(string level, string message, string source)
        {
            try
            {
                var now = DateTime.Now;
                var sourceStr = string.IsNullOrEmpty(source) ? "" : $"[{source}]";
                var logEntry = $"{now:HH:mm:ss.fff} [{level}] {sourceStr} {message}";

                // 同时输出到调试控制台 (为了实时调试，这行保留在调用线程，因为Debug.WriteLine通常很快)
                System.Diagnostics.Debug.WriteLine(logEntry);

                // 将日志放入队列，由后台线程写入文件
                if (!_cts.IsCancellationRequested && !_logQueue.IsAddingCompleted)
                {
                    _logQueue.Add(logEntry);
                }
            }
            catch (Exception ex)
            {
                // 如果日志入队失败，至少尝试输出到控制台
                System.Diagnostics.Debug.WriteLine($"Failed to queue log: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 停止日志服务并等待写入完成
        /// </summary>
        public void Stop()
        {
            _logQueue.CompleteAdding();
            _cts.Cancel();
            try
            {
                _writeTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }
    }
}
