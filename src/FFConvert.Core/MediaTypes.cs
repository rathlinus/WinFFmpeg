namespace FFConvert.Core;

[Flags]
public enum MediaKind
{
    None = 0,
    Video = 1,
    Audio = 2,
    Image = 4,
}

/// <summary>
/// The file types FFConvert offers itself for. The context menu is registered for
/// every file and stays hidden unless the selection contains one of these.
/// </summary>
public static class MediaTypes
{
    public static readonly IReadOnlyList<string> VideoExtensions =
    [
        ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".flv", ".mpg", ".mpeg",
        ".ts", ".mts", ".m2ts", ".3gp", ".ogv", ".vob", ".gif",
    ];

    public static readonly IReadOnlyList<string> AudioExtensions =
    [
        ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".opus", ".wma", ".aiff", ".aif",
        ".ac3", ".mka", ".amr",
    ];

    public static readonly IReadOnlyList<string> ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".webp", ".tif", ".tiff", ".avif", ".heic",
    ];

    private static readonly Dictionary<string, MediaKind> s_byExtension = BuildMap();

    public static IEnumerable<string> AllExtensions => s_byExtension.Keys;

    public static MediaKind Classify(string path)
    {
        string extension = Path.GetExtension(path);
        return s_byExtension.TryGetValue(extension, out MediaKind kind) ? kind : MediaKind.None;
    }

    /// <summary>
    /// Lower-case extension without the dot, with aliases folded together so it can be
    /// compared against <see cref="OutputFormat.Extension"/>.
    /// </summary>
    public static string NormalizedExtension(string path)
    {
        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "jpeg" or "jfif" => "jpg",
            "tif" => "tiff",
            "aif" => "aiff",
            _ => extension,
        };
    }

    private static Dictionary<string, MediaKind> BuildMap()
    {
        var map = new Dictionary<string, MediaKind>(StringComparer.OrdinalIgnoreCase);
        foreach (string e in VideoExtensions) map[e] = MediaKind.Video;
        foreach (string e in AudioExtensions) map[e] = MediaKind.Audio;
        foreach (string e in ImageExtensions) map[e] = MediaKind.Image;
        return map;
    }
}

/// <summary>What a set of selected files has in common, as far as the menu cares.</summary>
public readonly record struct SelectionSummary(MediaKind Kinds, string? CommonExtension, int SupportedCount)
{
    public static SelectionSummary From(IEnumerable<string> paths)
    {
        MediaKind kinds = MediaKind.None;
        string? common = null;
        bool mixed = false;
        int count = 0;

        foreach (string path in paths)
        {
            MediaKind kind = MediaTypes.Classify(path);
            if (kind == MediaKind.None)
            {
                continue;
            }

            kinds |= kind;
            count++;

            string extension = MediaTypes.NormalizedExtension(path);
            if (common is null)
            {
                common = extension;
            }
            else if (common != extension)
            {
                mixed = true;
            }
        }

        return new SelectionSummary(kinds, mixed ? null : common, count);
    }
}
