using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinFFmpeg.Core;

public sealed class AppSettings
{
    /// <summary>Folder containing ffmpeg.exe, or the exe itself. Null to search PATH.</summary>
    public string? FfmpegPath { get; set; }

    public bool UseHardwareEncoding { get; set; }

    /// <summary>Null to write each output next to its input.</summary>
    public string? OutputFolder { get; set; }

    public bool NotifyWhenDone { get; set; } = true;

    public bool CloseWhenDone { get; set; } = true;

    public List<Preset> UserPresets { get; set; } = [];
}

public static partial class AppPaths
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>
    /// Where settings live. Inside the package this is the package's LocalState folder,
    /// named explicitly so the app and the shell extension agree on it regardless of
    /// how AppData is virtualised for either process.
    /// </summary>
    public static string DataFolder { get; } = ResolveDataFolder();

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    private static string ResolveDataFolder()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? family = PackageFamilyName();
        return family is null
            ? Path.Combine(localAppData, "WinFFmpeg")
            : Path.Combine(localAppData, "Packages", family, "LocalState");
    }

    private static unsafe string? PackageFamilyName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        uint length = 0;
        if (GetCurrentPackageFamilyName(ref length, null) != ErrorInsufficientBuffer || length == 0)
        {
            return null;
        }

        char[] buffer = new char[length];
        fixed (char* name = buffer)
        {
            return GetCurrentPackageFamilyName(ref length, name) == ErrorSuccess
                ? new string(name)
                : null;
        }
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, char* packageFamilyName);
}

public static class SettingsStore
{
    /// <summary>Reads the settings. A missing or unreadable file gives the defaults.</summary>
    public static AppSettings Load(string? path = null)
    {
        try
        {
            using FileStream stream = File.OpenRead(path ?? AppPaths.SettingsFile);
            return JsonSerializer.Deserialize(stream, CoreJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a sibling and swap, so a crash never leaves half a settings file.
        string temporary = path + ".tmp";
        using (FileStream stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, settings, CoreJsonContext.Default.AppSettings);
        }

        File.Move(temporary, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(LaunchRequest))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
