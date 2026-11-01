using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ProctorCommon;

namespace StudentClient;

/// <summary>
/// 学生端主窗口：
///   1. 开始全程录屏（ScreenRecorder）
///   2. 禁止打开所有软件，打开了就记录并提示是否关闭（ProcessMonitor + AppWhitelist）
///      —— IDEA / Dev-C++ / Visual C++ / 画图 / 浏览器 白名单放行
///   3. 导出比赛纪律信息（DisciplineInfo -> AES+HMAC -> .disc）
///   4. 在题目信息旁有“提交代码”按钮（见 MainWindow.xaml 的 BtnSubmit）
/// </summary>
public partial class MainWindow : Window
{
    private readonly ScreenRecorder _recorder = new();
    private readonly ProcessMonitor _monitor = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _examStarted;

    /// <summary>每道题各自的草稿代码。</summary>
    private readonly Dictionary<string, string> _codeDrafts = new();

    /// <summary>题号 -> 题名（实际部署时可从洛谷 API / 本地题库文件加载）。</summary>
    private static readonly (string Id, string Name)[] Problems =
    {
        ("P1001", "A+B 问题"),
        ("P1002", "过河卒"),
        ("P1008", "三连击"),
    };

    public MainWindow()
    {
        InitializeComponent();

        foreach (var (id, name) in Problems)
            CmbProblems.Items.Add($"{id}  {name}");
        CmbProblems.SelectedIndex = 0;

        _recorder.Log += m => Dispatcher.Invoke(() => AppendLog(m));
        _monitor.Log += m => Dispatcher.Invoke(() => AppendLog(m));
        _monitor.ViolationFound += OnViolationFound;

        _uiTimer.Tick += (_, _) =>
        {
            if (!_examStarted) return;
            LblRecordTime.Text = "已录制时长：" + (DateTime.Now - _recorder.StartTime).ToString(@"hh\:mm\:ss");
            LblFrameCount.Text = $"帧数：{_recorder.FrameCount}";
        };
    }

    // ================= 1&2. 开始考试：录屏 + 进程监控 =================
    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_examStarted) return;
        if (string.IsNullOrWhiteSpace(TxtStudentName.Text))
        {
            MessageBox.Show(this, "请先填写考生姓名。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(TxtPassword.Password))
        {
            MessageBox.Show(this, "请输入教师下发的考场加密口令（导出纪律信息时需要）。",
                "缺少口令", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _recorder.IntervalMs = CmbFps.SelectedIndex switch { 0 => 2000, 2 => 500, _ => 1000 };
        _recorder.Start();          // 需求 1：全程录屏
        _monitor.Start();           // 需求 2：禁止/记录其它软件
        _uiTimer.Start();

        _examStarted = true;
        BtnStart.IsEnabled = false;
        BtnExport.IsEnabled = true;
        LblRecordState.Text = "录屏状态：● 正在录制";
        LblRecordState.Foreground = System.Windows.Media.Brushes.Red;
        AppendLog("考试开始：录屏与软件监控已启动。除 IDE(含 Dev-C++/Visual C++)、画图、浏览器外的程序将被记录。");
    }

    // ================= 2. 发现违规 -> 弹窗询问是否关闭 =================
    private void OnViolationFound(ViolationRecord rec)
    {
        Dispatcher.Invoke(() =>
        {
            LstViolations.Items.Add(rec.ToString());
            LblViolationCount.Text = $"违规次数：{_monitor.Violations.Count}";

            var ans = MessageBox.Show(this,
                $"检测到禁止打开的软件！\n\n" +
                $"程序：{rec.ProcessName}\n窗口：{rec.WindowTitle}\n时间：{rec.Time:HH:mm:ss}\n\n" +
                $"该行为已记入比赛纪律信息。\n是否立即关闭该软件？\n\n" +
                "选择“是”将尝试正常关闭；选择“否”软件将继续运行但保留违规记录。",
                "监考提醒 - 违规软件",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);

            if (ans == MessageBoxResult.Yes)
            {
                bool ok = ProcessMonitor.TryCloseProcess(rec.Pid);
                AppendLog(ok ? $"已关闭违规软件 {rec.ProcessName} (PID={rec.Pid})"
                             : $"关闭 {rec.ProcessName} 失败，请监考老师人工处理");
            }
            else
            {
                AppendLog($"学生选择不关闭 {rec.ProcessName}，仅保留记录");
            }
        });
    }

    // ================= 4. 题目信息旁的“提交代码”按钮 =================
    private void BtnSubmit_Click(object sender, RoutedEventArgs e)
    {
        var code = TxtCode.Text;
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show(this, "代码为空，无法提交。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var (id, name) = ParseSelectedProblem();
        var lang = (CmbLang.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "C++14";

        _submissions.Add(new ProblemInfo
        {
            Contest = TxtContest.Text.Trim(),
            ProblemId = id,
            ProblemName = name,
            Language = lang,
            Code = code,
            SubmitTime = DateTime.Now
        });
        LblSubmitState.Text = $"已于 {DateTime.Now:HH:mm:ss} 提交 {_submissions.Count} 次（最后一次：{id}）";
        AppendLog($"提交代码：{id} {name} [{lang}]，共 {code.Length} 字符。提交记录将随纪律信息一并导出。");
    }

    private void CmbProblems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 保存旧题草稿
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is string old && old.Length >= 5)
            _codeDrafts[old.Substring(0, 5)] = TxtCode.Text;

        var (id, name) = ParseSelectedProblem();
        LblProblemId.Text = id;
        LblProblemName.Text = name;
        LblLanguage.Text = (CmbLang.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        if (_codeDrafts.TryGetValue(id, out var draft)) TxtCode.Text = draft;
    }

    private (string Id, string Name) ParseSelectedProblem()
    {
        var s = CmbProblems.SelectedItem?.ToString() ?? "P1001  A+B 问题";
        var sp = s.Split(' ', 2);
        return (sp[0], sp.Length > 1 ? sp[1].Trim() : "");
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择源代码文件",
            Filter = "源代码|*.cpp;*.cc;*.cxx;*.c;*.pas;*.py|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                TxtCode.Text = File.ReadAllText(dlg.FileName);
                AppendLog("已载入源码文件：" + dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "读取失败：" + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ================= 3. 导出比赛纪律信息 =================
    private readonly List<ProblemInfo> _submissions = new();

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var pwd = TxtPassword.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            MessageBox.Show(this, "缺少考场加密口令，无法导出。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 结束录屏以获得最终哈希（若还想继续考试可改为不 Stop）
        string sha = _recorder.FrameCount > 0 ? _recorder.StopAndPackage() : "";

        var info = new DisciplineInfo
        {
            StudentName = TxtStudentName.Text.Trim(),
            School = TxtSchool.Text.Trim(),
            IdNumber = TxtIdNumber.Text.Trim(),
            SeatNumber = TxtSeat.Text.Trim(),
            ContestName = TxtContest.Text.Trim(),
            RecordStart = _recorder.StartTime,
            RecordEnd = DateTime.Now,
            VideoPath = _recorder.OutputDirectory,
            VideoFileSha256 = sha,
            Violations = _monitor.Violations.ToList(),
            Submissions = _submissions.ToList(),
            ExportedAt = DateTime.Now
        };

        var saveDlg = new SaveFileDialog
        {
            Title = "导出比赛纪律信息",
            Filter = $"比赛纪律信息|*{DisciplineCrypto.FileExtension}|所有文件|*.*",
            FileName = $"{info.StudentName}_{info.SeatNumber}_纪律信息{DisciplineCrypto.FileExtension}"
                       .Replace(" ", "_")
        };
        if (saveDlg.ShowDialog(this) != true) return;

        try
        {
            byte[] packet = DisciplineCrypto.Encrypt(info.ToJson(), pwd);
            DisciplineCrypto.SavePacket(saveDlg.FileName, packet);
            AppendLog($"已导出加密纪律信息：{saveDlg.FileName}（{packet.Length} 字节，违规 {info.Violations.Count} 条，提交 {info.Submissions.Count} 次）");
            MessageBox.Show(this,
                $"比赛纪律信息已加密导出到：\n{saveDlg.FileName}\n\n请将该文件交给监考老师。",
                "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        if (!_examStarted) { Close(); return; }
        if (MessageBox.Show(this, "结束考试将停止录屏。若尚未导出纪律信息，建议先点击“导出”。\n确认结束？",
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _monitor.Stop();
        _recorder.StopAndPackage();
        _uiTimer.Stop();
        _examStarted = false;
        LblRecordState.Text = "录屏状态：已停止";
        LblRecordState.Foreground = System.Windows.Media.Brushes.Gray;
        AppendLog("考试结束，录屏与监控已停止。");
    }

    private void AppendLog(string msg)
    {
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        LogScroll.ScrollToEnd();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_examStarted &&
            MessageBox.Show(this, "考试仍在进行中，确定退出吗？", "确认退出",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _monitor.Dispose();
        _recorder.Dispose();
        base.OnClosing(e);
    }
}
