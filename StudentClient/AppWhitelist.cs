namespace StudentClient;

/// <summary>
/// 软件白名单：允许在考试期间打开的程序（IDE、Dev-C++/Visual C++、画图、浏览器）。
/// 除这些以外的程序一旦被检测到，就记入违规并提示是否关闭。
/// </summary>
public static class AppWhitelist
{
    /// <summary>进程名匹配规则（不区分大小写，支持子串匹配）。</summary>
    private static readonly string[] ProcessKeywords =
    {
        // --- IDE / 编译器 ---
        "idea", "clion", "pycharm", "webstorm", "goland", "rider", "datagrip",   // JetBrains 全家桶
        "devenv", "vcexpress", "vswdexpress",                                     // Visual Studio / VC++
        "devcpp", "smallcpp",                                                     // Dev-C++
        "codeblocks", "qtcreator", "eclipse", "netbeans", "studio64",             // 其它常见 IDE
        "vscode", "codium",                                                       // VS Code
        "thonny", "spyder", "jetbrains",
        // --- 画图 ---
        "mspaint", "paint", "paint3d", "photoshop",
        // --- 浏览器 ---
        "chrome", "firefox", "msedge", "edge", "iexplore", "opera", "brave", "vivaldi", "safari",
        // --- 系统关键进程（不可能关闭，也不能算违规）---
        "explorer", "winlogon", "csrss", "svchost", "services", "lsass", "smss",
        "dwm", "taskhostw", "runtimebroker", "searchhost", "ctfmon", "dllhost",
        "conhost", "audiodg", "spoolsv", "sihost", "startmenuexperiencehost",
        "textinputhost", "applicationframehost", "shellexperiencehost", "everything",
    };

    /// <summary>窗口标题关键字兜底（有些程序进程名不固定，但标题可辨认）。</summary>
    private static readonly string[] TitleKeywords =
    {
        "intellij idea", "visual studio", "dev-c++", "devc++", "code::blocks",
        "visual c++", "vc++", "paint", "画图", "google chrome", "mozilla firefox",
        "microsoft edge", "safari", "opera", "visual studio code", "vs code",
    };

    /// <summary>本监考软件自身及其启动器。</summary>
    private static readonly string[] SelfProcessNames =
    {
        "监考学生端", "studentclient", "proctorclient", "proctor"
    };

    /// <summary>判断某进程+窗口标题是否被允许。true=放行，false=视为违规。</summary>
    public static bool IsAllowed(string processName, string windowTitle)
    {
        var p = (processName ?? "").ToLowerInvariant();
        var t = (windowTitle ?? "").ToLowerInvariant();

        if (SelfProcessNames.Any(s => p.Contains(s))) return true;
        if (ProcessKeywords.Any(k => p.Contains(k))) return true;
        if (TitleKeywords.Any(k => t.Contains(k))) return true;
        return false;
    }
}
