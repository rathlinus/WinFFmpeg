namespace FFConvert.Core;

/// <param name="Ffprobe">Null when ffprobe is missing; conversions then run without a progress percentage.</param>
public sealed record FfmpegPaths(string Ffmpeg, string? Ffprobe);

public static class FfmpegLocator
{
    private const string FfmpegExe = "ffmpeg.exe";
    private const string FfprobeExe = "ffprobe.exe";

    /// <summary>
    /// Finds ffmpeg: the configured location first, then PATH, then the places
    /// common installers put it.
    /// </summary>
    /// <param name="configuredPath">A folder or the path of ffmpeg.exe itself; null or empty for none.</param>
    public static FfmpegPaths? Locate(string? configuredPath)
    {
        foreach (string folder in CandidateFolders(configuredPath))
        {
            string ffmpeg = Path.Combine(folder, FfmpegExe);
            if (File.Exists(ffmpeg))
            {
                string ffprobe = Path.Combine(folder, FfprobeExe);
                return new FfmpegPaths(ffmpeg, File.Exists(ffprobe) ? ffprobe : null);
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateFolders(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string configured = configuredPath.Trim().Trim('"');
            yield return configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(configured) ?? configured
                : configured;
        }

        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(
            Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return folder.Trim('"');
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links");
        yield return @"C:\ffmpeg\bin";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin");
    }
}
