using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using ProctorCommon;

namespace TeacherDecoder;

/// <summary>
/// 教师端：读取学生导出的 .disc 封包，用考场口令解密 + HMAC 验签，
/// 展示考生信息、录屏区间、违规记录与代码提交。
/// </summary>
public partial class MainWindow : Window
{
    private string? _currentFile;
    private byte[]? _packet;
    private DisciplineInfo? _decoded;

    public MainWindow() => InitializeComponent();

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择比赛纪律信息文件",
            Filter = $"比赛纪律信息|*{DisciplineCrypto.FileExtension}|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) == true) LoadFile(dlg.FileName);
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length > 0) LoadFile(files[0]);
        }
    }

    private void LoadFile(string path)
    {
        try
        {
            _packet = DisciplineCrypto.LoadPacket(path);
            _currentFile = path;
            LblStatus.Text = $"已加载：{path}（{_packet.Length} 字节），请输入口令后点击“解密”";
            TxtPassword.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "读取文件失败：" + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDecrypt_Click(object sender, RoutedEventArgs e)
    {
        if (_packet == null || _currentFile == null)
        {
            MessageBox.Show(this, "请先打开一个 .disc 文件。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var pwd = TxtPassword.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            MessageBox.Show(this, "请输入解密口令。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            string json = DisciplineCrypto.Decrypt(_packet, pwd); // 内含 HMAC 验签
            _decoded = DisciplineInfo.FromJson(json);
            ShowResult(json, _decoded);
            LblStatus.Text = $"解密成功：{_currentFile}（完整性校验通过 ✓）";
            BtnExportPlain.IsEnabled = true;
        }
        catch (DisciplineDecryptException ex)
        {
            _decoded = null;
            LblStatus.Text = "解密失败：" + ex.Message;
            MessageBox.Show(this, ex.Message, "解密失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowResult(string json, DisciplineInfo d)
    {
        TxtJson.Text = json;

        var sb = new StringBuilder();
        sb.AppendLine($"考生：{d.StudentName}    学校：{d.School}    座位：{d.SeatNumber}");
        sb.AppendLine($"证件号：{MaskId(d.IdNumber)}");
        sb.AppendLine($"机器名：{d.MachineName}    MAC：{d.MacAddress}");
        sb.AppendLine($"比赛：{d.ContestName}");
        sb.AppendLine($"录屏：{d.RecordStart:yyyy-MM-dd HH:mm:ss} ~ {d.RecordEnd:yyyy-MM-dd HH:mm:ss}" +
                      $"（{TimeSpan.FromSeconds(d.RecordDurationSeconds):hh\\:mm\\:ss}）");
        sb.AppendLine($"录屏目录：{(string.IsNullOrEmpty(d.VideoPath) ? "(无)" : d.VideoPath)}");
        sb.AppendLine($"录屏 SHA-256：{d.VideoFileSha256}");
        sb.AppendLine($"导出时间：{d.ExportedAt:yyyy-MM-dd HH:mm:ss}    数据版本：v{d.Version}    生成器：{d.Generator}");
        sb.AppendLine();
        sb.AppendLine($"违规软件：{d.Violations.Count} 次    代码提交：{d.Submissions.Count} 次");
        if (d.Violations.Count == 0) sb.AppendLine("✔ 未发现违规打开其它软件的行为");
        TxtSummary.Text = sb.ToString();

        LstViolations.Items.Clear();
        foreach (var v in d.Violations) LstViolations.Items.Add(v.ToString());

        LstSubmissions.Items.Clear();
        foreach (var s in d.Submissions)
            LstSubmissions.Items.Add($"{s.SubmitTime:HH:mm:ss}  {s.ProblemId} {s.ProblemName} [{s.Language}]");
        if (LstSubmissions.Items.Count > 0) LstSubmissions.SelectedIndex = 0;
        TxtCodeView.Text = "";

        Tabs.SelectedIndex = 0;
    }

    private void LstSubmissions_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        int i = LstSubmissions.SelectedIndex;
        if (_decoded != null && i >= 0 && i < _decoded.Submissions.Count)
            TxtCodeView.Text = _decoded.Submissions[i].Code;
    }

    /// <summary>身份证号打码：保留前 4 后 4。</summary>
    private static string MaskId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "(未填写)";
        if (id.Length <= 8) return new string('*', id.Length);
        return id[..4] + new string('*', id.Length - 8) + id[^4..];
    }

    private void BtnExportPlain_Click(object sender, RoutedEventArgs e)
    {
        if (_decoded == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "导出明文 JSON（供存档/打印）",
            Filter = "JSON 文件|*.json",
            FileName = $"{_decoded.StudentName}_{_decoded.SeatNumber}_纪律信息.json".Replace(" ", "_")
        };
        if (dlg.ShowDialog(this) == true)
        {
            File.WriteAllText(dlg.FileName, TxtJson.Text, Encoding.UTF8);
            MessageBox.Show(this, "已导出：" + dlg.FileName, "完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
