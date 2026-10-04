using System.Text;
using System.Text.Json;

namespace FFConvert.Core;

/// <summary>
/// What the app was started to do. Travels as command-line arguments from the shell
/// extension and as one JSON line between a second app instance and the running one.
/// </summary>
public sealed record LaunchRequest
{
    private const string RunOption = "--run";
    private const string FilesFromOption = "--files-from";

    // Comfortably below the 32,767 character limit of a Windows command line.
    private const int MaxInlineLength = 8000;

    /// <summary>
    /// A preset id, <see cref="ContextMenuModel.CompressId"/> or
    /// <see cref="ContextMenuModel.CustomId"/>; null to just open the app.
    /// </summary>
    public string? Command { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];

    public static LaunchRequest Parse(IReadOnlyList<string> args)
    {
        string? command = null;
        var files = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg == RunOption && i + 1 < args.Count)
            {
                command = args[++i];
            }
            else if (arg == FilesFromOption && i + 1 < args.Count)
            {
                files.AddRange(ReadFileList(args[++i]));
            }
            else if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                files.Add(Path.GetFullPath(arg));
            }
        }

        return new LaunchRequest { Command = command, Files = files };
    }

    /// <summary>
    /// Builds the arguments for starting the app. A long selection is written to a list
    /// file in <paramref name="listFolder"/> instead of being passed inline.
    /// </summary>
    public static List<string> ToArguments(string command, IReadOnlyList<string> files, string listFolder)
    {
        var args = new List<string> { RunOption, command };

        if (files.Sum(f => f.Length + 3) <= MaxInlineLength)
        {
            args.AddRange(files);
            return args;
        }

        Directory.CreateDirectory(listFolder);
        string listPath = Path.Combine(listFolder, $"{Guid.NewGuid():N}.txt");
        File.WriteAllLines(listPath, files, Encoding.UTF8);
        args.AddRange([FilesFromOption, listPath]);
        return args;
    }

    public string ToJson() => JsonSerializer.Serialize(this, CoreJsonContext.Default.LaunchRequest);

    public static LaunchRequest? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, CoreJsonContext.Default.LaunchRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string[] ReadFileList(string listPath)
    {
        try
        {
            string[] lines = File.ReadAllLines(listPath, Encoding.UTF8);
            File.Delete(listPath);
            return [.. lines.Where(l => l.Length > 0)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
