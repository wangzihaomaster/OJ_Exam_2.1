using System.Runtime.InteropServices;

namespace StudentClient;

/// <summary>
/// 与用户桌面 / 窗口管理相关的 Win32 封装：
///   - 枚举所有可见顶层窗口（用于判断某个进程是否真的“打开”了界面）
///   - 关闭指定窗口（WM_CLOSE，等价于用户点右上角 ×，让程序正常退出）
///   - 全屏截图（供录屏器逐帧编码）
/// </summary>
internal static class NativeMethods
{
    public const uint WM_CLOSE = 0x0010;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    // ---- 截屏所需 ----
    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h,
                                     IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    public const uint SRCCOPY = 0x00CC0020;
    public const int HORZRES = 8;
    public const int VERTRES = 10;

    /// <summary>
    /// 返回 pid -> 第一个可见窗口标题 的映射。只有拥有可见窗口的进程才算“打开了界面”。
    /// </summary>
    public static Dictionary<uint, string> GetVisibleWindowTitles()
    {
        var map = new Dictionary<uint, string>();
        IntPtr shell = GetShellWindow();
        EnumWindows((hWnd, _) =>
        {
            if (hWnd == shell || !IsWindowVisible(hWnd)) return true;
            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return true; // 无标题的后台消息窗口忽略
            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != 0 && !map.ContainsKey(pid))
                map[pid] = sb.ToString();
            return true;
        }, IntPtr.Zero);
        return map;
    }

    /// <summary>向窗口发送 WM_CLOSE，请求其正常退出。</summary>
    public static void CloseWindow(IntPtr hWnd) => PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>按 PID 关闭它的第一个可见窗口。</summary>
    public static bool CloseWindowOfProcess(uint pid)
    {
        bool closed = false;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out uint p);
            if (p == pid)
            {
                CloseWindow(hWnd);
                closed = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return closed;
    }
}
