namespace WinFFmpeg.Core;

public static class OutputPathResolver
{
    /// <summary>
    /// Picks the output path for a conversion. The result never names an existing file,
    /// so the source and earlier outputs are never overwritten: collisions get
    /// " (1)", " (2)", … appended like Explorer does.
    /// </summary>
    /// <param name="extension">Output extension without the dot.</param>
    /// <param name="outputFolder">Null to write next to the input.</param>
    /// <param name="isTaken">Whether a candidate path is already used.</param>
    public static string Resolve(
        string inputPath, string extension, string? outputFolder, string? suffix, Func<string, bool> isTaken)
    {
        string folder = string.IsNullOrWhiteSpace(outputFolder)
            ? Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ""
            : outputFolder;
        string stem = Path.GetFileNameWithoutExtension(inputPath) + suffix;

        string candidate = Path.Combine(folder, $"{stem}.{extension}");
        for (int n = 1; isTaken(candidate); n++)
        {
            candidate = Path.Combine(folder, $"{stem} ({n}).{extension}");
        }

        return candidate;
    }

    /// <summary>
    /// Where ffmpeg writes while a conversion runs. The real extension stays at the end
    /// so ffmpeg still picks the right muxer from the file name.
    /// </summary>
    public static string PartialPathFor(string finalPath)
    {
        string folder = Path.GetDirectoryName(finalPath) ?? "";
        string stem = Path.GetFileNameWithoutExtension(finalPath);
        return Path.Combine(folder, $"{stem}.partial{Path.GetExtension(finalPath)}");
    }
}
