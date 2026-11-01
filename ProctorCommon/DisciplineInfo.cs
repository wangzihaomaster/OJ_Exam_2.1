using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProctorCommon;

/// <summary>
/// 单条违规记录：学生打开了一个被禁止的软件。
/// </summary>
public class ViolationRecord
{
    /// <summary>发现时间（本地时间）。</summary>
    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    /// <summary>进程 ID。</summary>
    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    /// <summary>进程可执行文件名，如 wechat.exe。</summary>
    [JsonPropertyName("process")]
    public string ProcessName { get; set; } = "";

    /// <summary>窗口标题（若窗口已消失则为空）。</summary>
    [JsonPropertyName("title")]
    public string WindowTitle { get; set; } = "";

    /// <summary>教师端展示用的一行文本。</summary>
    public override string ToString() =>
        $"[{Time:yyyy-MM-dd HH:mm:ss}] PID={Pid}  进程={ProcessName}  窗口={WindowTitle}";
}

/// <summary>
/// 一次提交所对应的题目信息（显示在“提交代码”按钮旁）。
/// </summary>
public class ProblemInfo
{
    [JsonPropertyName("contest")]
    public string Contest { get; set; } = "";

    [JsonPropertyName("problemId")]
    public string ProblemId { get; set; } = "";

    [JsonPropertyName("problemName")]
    public string ProblemName { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    /// <summary>提交的源代码内容。</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("submitTime")]
    public DateTime SubmitTime { get; set; }
}

/// <summary>
/// 比赛纪律信息：学生端导出、教师端解密的根对象。
/// </summary>
public class DisciplineInfo
{
    /// <summary>数据格式版本，便于以后升级。</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>生成数据的软件名称与版本。</summary>
    [JsonPropertyName("generator")]
    public string Generator { get; set; } = "监考学生端 1.0.0";

    [JsonPropertyName("studentName")]
    public string StudentName { get; set; } = "";

    [JsonPropertyName("school")]
    public string School { get; set; } = "";

    /// <summary>身份证号或学籍号（教师端解密后默认部分打码显示）。</summary>
    [JsonPropertyName("idNumber")]
    public string IdNumber { get; set; } = "";

    [JsonPropertyName("seatNumber")]
    public string SeatNumber { get; set; } = "";

    /// <summary>计算机名。</summary>
    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>局域网 MAC 地址（第一块启用中的网卡）。</summary>
    [JsonPropertyName("macAddress")]
    public string MacAddress { get; set; } = GetFirstMacAddress();

    [JsonPropertyName("contestName")]
    public string ContestName { get; set; } = "";

    /// <summary>录屏开始时间。</summary>
    [JsonPropertyName("recordStart")]
    public DateTime RecordStart { get; set; }

    /// <summary>录屏结束/导出时间。</summary>
    [JsonPropertyName("recordEnd")]
    public DateTime RecordEnd { get; set; }

    /// <summary>录屏时长（秒），由起止时间自动计算。</summary>
    [JsonIgnore]
    public double RecordDurationSeconds => Math.Max(0, (RecordEnd - RecordStart).TotalSeconds);

    /// <summary>录屏视频文件的 SHA-256（防止事后替换录像）。</summary>
    [JsonPropertyName("videoSha256")]
    public string VideoFileSha256 { get; set; } = "";

    [JsonPropertyName("videoPath")]
    public string VideoPath { get; set; } = "";

    /// <summary>违规打开软件的记录列表。</summary>
    [JsonPropertyName("violations")]
    public List<ViolationRecord> Violations { get; set; } = new();

    /// <summary>历次代码提交（含题目信息与源码快照）。</summary>
    [JsonPropertyName("submissions")]
    public List<ProblemInfo> Submissions { get; set; } = new();

    /// <summary>导出时间。</summary>
    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; } = DateTime.Now;

    private static string GetFirstMacAddress()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                    ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                {
                    var mac = ni.GetPhysicalAddress().ToString();
                    if (!string.IsNullOrEmpty(mac))
                    {
                        // 6A1B2C... -> 6A-1B-2C-...
                        return string.Join("-", Enumerable.Range(0, mac.Length / 2)
                            .Select(i => mac.Substring(i * 2, 2)));
                    }
                }
            }
        }
        catch { /* 忽略采集失败 */ }
        return "";
    }

    public string ToJson(JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(this, options ?? DefaultOptions);

    public static DisciplineInfo FromJson(string json) =>
        JsonSerializer.Deserialize<DisciplineInfo>(json, DefaultOptions)
        ?? throw new InvalidDataException("纪律信息 JSON 解析失败");

    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
