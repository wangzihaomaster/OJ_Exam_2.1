using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace StudentClient;

/// <summary>
/// 录屏器：定时全屏截图 -> JPEG -> 打包为 ZIP（内含按序号命名的帧与 manifest.txt）。
/// 说明：不依赖任何第三方编码器，产物可直接交给教师回放工具逐帧查看；
///       若考场安装了 ffmpeg，可在启动时传入参数自动把帧合成为 mp4。
/// </summary>
public class ScreenRecorder : IDisposable
{
    private Timer? _timer;
    private readonly object _lock = new();

    public string OutputDirectory { get; private set; } = "";
    public string ZipPath => Path.Combine(OutputDirectory, "frames.zip");
    public string Mp4Path => Path.Combine(OutputDirectory, "record.mp4");

    public DateTime StartTime { get; private set; }
    public int FrameCount { get; private set; }
    public int IntervalMs { get; set; } = 1000; // 默认 1 帧/秒，考试监控足够
    public int JpegQuality { get; set; } = 70;

    public event Action<string>? Log;

    /// <summary>开始录制。outputDir 为空则使用 %LOCALAPPDATA%\Proctor\record_时间戳。</summary>
    public void Start(string? outputDir = null)
    {
        Stop();
        OutputDirectory = string.IsNullOrEmpty(outputDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "Proctor", "record_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"))
            : outputDir;
        Directory.CreateDirectory(OutputDirectory);
        StartTime = DateTime.Now;
        FrameCount = 0;
        _timer = new Timer(_ => CaptureFrame(), null, 200, IntervalMs);
        Log?.Invoke($"录屏已开始，保存目录：{OutputDirectory}");
    }

    /// <summary>抓一帧并写入磁盘（JPEG）。可在测试中直接调用。</summary>
    public void CaptureFrame()
    {
        try
        {
            using var bmp = GrabScreen();
            if (bmp == null) return;
            lock (_lock)
            {
                var name = $"frame_{FrameCount:D6}.jpg";
                SaveJpeg(bmp, Path.Combine(OutputDirectory, name));
                FrameCount++;
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke("抓帧失败：" + ex.Message);
        }
    }

    private static System.Drawing.Imaging.ImageCodecInfo? GetJpegEncoder() =>
        System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.MimeType == "image/jpeg");

    /// <summary>按指定质量保存 JPEG。</summary>
    private void SaveJpeg(System.Drawing.Bitmap bmp, string path)
    {
        var codec = GetJpegEncoder();
        if (codec == null) { bmp.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg); return; }
        using var eps = new System.Drawing.Imaging.EncoderParameters(1); // 单参数：质量
        eps.Param[0] = new System.Drawing.Imaging.EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, (long)JpegQuality);
        bmp.Save(path, codec, eps);
    }

    /// <summary>GDI BitBlt 抓取主屏全屏。</summary>
    private static System.Drawing.Bitmap? GrabScreen()
    {
        IntPtr hDesk = NativeMethods.GetDesktopWindow();
        IntPtr hSrc = NativeMethods.GetWindowDC(hDesk);
        if (hSrc == IntPtr.Zero) return null;
        try
        {
            int w = NativeMethods.GetDeviceCaps(hSrc, NativeMethods.HORZRES);
            int h = NativeMethods.GetDeviceCaps(hSrc, NativeMethods.VERTRES);
            var bmp = new System.Drawing.Bitmap(w, h);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            IntPtr hdcDest = g.GetHdc();
            NativeMethods.BitBlt(hdcDest, 0, 0, w, h, hSrc, 0, 0, NativeMethods.SRCCOPY);
            g.ReleaseHdc(hdcDest);
            return bmp;
        }
        finally
        {
            NativeMethods.ReleaseDC(hDesk, hSrc);
        }
    }

    /// <summary>
    /// 结束录制：写 manifest、打包 ZIP、计算 SHA-256；如系统里有 ffmpeg 则顺便合成 mp4。
    /// 返回视频证据的 SHA-256（用于写入纪律信息防篡改）。
    /// </summary>
    public string StopAndPackage()
    {
        Stop();
        if (string.IsNullOrEmpty(OutputDirectory)) return "";

        // manifest：记录起止时间与帧数，方便回放对齐时间轴
        File.WriteAllText(Path.Combine(OutputDirectory, "manifest.txt"),
            $"start={StartTime:yyyy-MM-dd HH:mm:ss}\r\nend={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nframes={FrameCount}\r\nintervalMs={IntervalMs}\r\n");

        // 可选：ffmpeg 合成 mp4（存在才做）
        TryMuxWithFfmpeg();

        // 打包 ZIP（含所有帧和 manifest）
        var zip = ZipPath;
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(OutputDirectory, zip, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

        var evidence = File.Exists(Mp4Path) ? Mp4Path : zip;
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(evidence);
        var hash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        Log?.Invoke($"录屏已停止：{FrameCount} 帧，证据文件 {evidence}\r\nSHA-256={hash}");
        return hash;
    }

    private void TryMuxWithFfmpeg()
    {
        try
        {
            // 在 PATH 里找 ffmpeg.exe
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            var ff = pathVar.Split(';')
                .Select(p => Path.Combine(p.Trim(), "ffmpeg.exe"))
                .FirstOrDefault(File.Exists);
            if (ff == null) return;

            var psi = new ProcessStartInfo(ff,
                $"-y -framerate {1000.0 / IntervalMs} -i frame_%06d.jpg -c:v libx264 -pix_fmt yuv420p record.mp4")
            {
                WorkingDirectory = OutputDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            p.StandardError.ReadToEnd(); // 防止管道阻塞
            if (!p.WaitForExit(120_000)) { try { p.Kill(); } catch { } }
            else Log?.Invoke("ffmpeg 合成 record.mp4 完成（退出码 " + p.ExitCode + "）");
        }
        catch (Exception ex)
        {
            Log?.Invoke("ffmpeg 合成跳过：" + ex.Message);
        }
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}
