using System.Diagnostics;
using System.Text;

namespace WinFFmpeg.Core;

public sealed record ProcessResult(int ExitCode, string StandardError, bool Canceled);

public static class FfmpegProcess
{
    private const int ErrorTailLines = 200;
    private static readonly TimeSpan s_quitGracePeriod = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Runs ffmpeg or ffprobe to completion. Cancelling asks ffmpeg to quit by sending
    /// "q" and kills it if it has not exited after a few seconds.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken,
        bool lowPriority = false)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var errorTail = new Queue<string>();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (errorTail)
            {
                errorTail.Enqueue(e.Data);
                if (errorTail.Count > ErrorTailLines)
                {
                    errorTail.Dequeue();
                }
            }
        };

        process.Start();
        ChildProcessGuard.Add(process);
        if (lowPriority)
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process already exited, or the priority cannot be changed. Neither matters.
            }
        }

        process.BeginErrorReadLine();

        bool canceled = false;
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            canceled = true;
            RequestStop(process);
        });

        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            onOutputLine?.Invoke(line);
        }

        await process.WaitForExitAsync(CancellationToken.None);

        string standardError;
        lock (errorTail)
        {
            standardError = string.Join(Environment.NewLine, errorTail);
        }

        return new ProcessResult(process.ExitCode, standardError, canceled);
    }

    private static void RequestStop(Process process)
    {
        try
        {
            process.StandardInput.Write('q');
            process.StandardInput.Flush();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // stdin is gone, so the process is exiting or has exited.
        }

        _ = Task.Delay(s_quitGracePeriod).ContinueWith(
            _ =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already exited and disposed.
                }
            },
            TaskScheduler.Default);
    }
}

public static class MediaProber
{
    /// <summary>Runs ffprobe on a file. Returns null when ffprobe fails or its output cannot be read.</summary>
    public static async Task<MediaInfo?> ProbeAsync(string ffprobe, string path, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        try
        {
            ProcessResult result = await FfmpegProcess.RunAsync(
                ffprobe, [.. FfprobeParser.Arguments, path], line => output.AppendLine(line), cancellationToken);
            return result.ExitCode == 0 && !result.Canceled ? FfprobeParser.Parse(output.ToString()) : null;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or System.ComponentModel.Win32Exception
            or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

public static class EncoderProbe
{
    private static readonly string[] s_h264 = ["h264_nvenc", "h264_qsv", "h264_amf"];
    private static readonly string[] s_hevc = ["hevc_nvenc", "hevc_qsv", "hevc_amf"];

    /// <summary>
    /// Finds GPU encoders that work here. An encoder being compiled into ffmpeg says
    /// nothing about the GPU or driver, so each candidate has to encode a few frames.
    /// </summary>
    public static async Task<HardwareEncoders> DetectAsync(string ffmpeg, CancellationToken cancellationToken)
    {
        try
        {
            var listing = new StringBuilder();
            await FfmpegProcess.RunAsync(
                ffmpeg, ["-hide_banner", "-encoders"], line => listing.AppendLine(line), cancellationToken);
            string available = listing.ToString();

            return new HardwareEncoders(
                await FirstWorkingAsync(ffmpeg, s_h264, available, cancellationToken),
                await FirstWorkingAsync(ffmpeg, s_hevc, available, cancellationToken));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return HardwareEncoders.None;
        }
    }

    private static async Task<string?> FirstWorkingAsync(
        string ffmpeg, string[] candidates, string available, CancellationToken cancellationToken)
    {
        foreach (string encoder in candidates)
        {
            if (!available.Contains(encoder, StringComparison.Ordinal))
            {
                continue;
            }

            ProcessResult result = await FfmpegProcess.RunAsync(
                ffmpeg,
                ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=640x360:r=30:d=1",
                    "-frames:v", "5", "-c:v", encoder, "-f", "null", "-"],
                onOutputLine: null,
                cancellationToken);
            if (result.ExitCode == 0 && !result.Canceled)
            {
                return encoder;
            }
        }

        return null;
    }
}
