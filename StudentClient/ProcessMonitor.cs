using System.Diagnostics;
using ProctorCommon;

namespace StudentClient;

/// <summary>
/// 进程监视器：周期扫描所有拥有可见窗口的进程，
/// 对不在白名单里的程序记录违规并回调 UI（由 UI 决定如何提示/关闭）。
/// </summary>
public class ProcessMonitor : IDisposable
{
    private Timer? _timer;
    private readonly object _lock = new();

    /// <summary>已经提示过、且仍在运行的 PID，避免反复弹窗。</summary>
    private readonly HashSet<int> _notifiedPids = new();

    /// <summary>全部违规记录（导出纪律信息时使用）。</summary>
    public List<ViolationRecord> Violations { get; } = new();

    /// <summary>发现新违规时的回调（在定时器线程触发，UI 需自行调度到主线程）。</summary>
    public event Action<ViolationRecord>? ViolationFound;

    /// <summary>日志输出（写入界面与本地文件）。</summary>
    public event Action<string>? Log;

    public int ScanIntervalMs { get; set; } = 2000;

    public void Start()
    {
        Stop();
        _timer = new Timer(_ => Scan(), null, 500, ScanIntervalMs);
        Log?.Invoke($"进程监视已启动（每 {ScanIntervalMs / 1000.0:0.#} 秒扫描一次）");
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>扫描一轮。可在单元测试中直接调用。</summary>
    public void Scan()
    {
        try
        {
            var windows = NativeMethods.GetVisibleWindowTitles(); // pid -> title
            lock (_lock)
            {
                // 清理已退出的进程的“已提示”标记
                var alive = new HashSet<int>(windows.Keys.Select(k => (int)k));
                _notifiedPids.RemoveWhere(pid => !alive.Contains(pid));

                foreach (var (pid, title) in windows)
                {
                    if (_notifiedPids.Contains((int)pid)) continue;

                    string processName;
                    try
                    {
                        using var proc = Process.GetProcessById((int)pid);
                        // 忽略本进程自己
                        if (proc.Id == Environment.ProcessId) continue;
                        processName = proc.ProcessName;
                    }
                    catch
                    {
                        continue; // 进程可能刚刚退出
                    }

                    if (AppWhitelist.IsAllowed(processName, title)) continue;

                    var rec = new ViolationRecord
                    {
                        Time = DateTime.Now,
                        Pid = (int)pid,
                        ProcessName = processName + ".exe",
                        WindowTitle = title
                    };
                    Violations.Add(rec);
                    _notifiedPids.Add((int)pid);
                    Log?.Invoke($"检测到违规软件：{rec}");
                    ViolationFound?.Invoke(rec);
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke("扫描异常：" + ex.Message);
        }
    }

    /// <summary>用户选择“关闭软件”时调用：先尝试 WM_CLOSE，超时后强制 Kill。</summary>
    public static bool TryCloseProcess(int pid, int graceMs = 3000)
    {
        NativeMethods.CloseWindowOfProcess((uint)pid);
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.WaitForExit(graceMs))
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(2000);
            }
            return true;
        }
        catch (ArgumentException)
        {
            return true; // 已经退出
        }
        catch
        {
            return false; // 权限不足等
        }
    }

    public void Dispose() => Stop();
}
