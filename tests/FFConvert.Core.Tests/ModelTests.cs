using System.Text.RegularExpressions;
using System.Xml.Linq;
using FFConvert.Core;

namespace FFConvert.Core.Tests;

public class OutputPathResolverTests
{
    private static readonly string s_folder = Path.Combine(Path.GetTempPath(), "ffconvert-paths");

    [Fact]
    public void WritesNextToTheInputByDefault()
    {
        string result = OutputPathResolver.Resolve(Path.Combine(s_folder, "clip.mkv"), "mp4", null, null, _ => false);

        Assert.Equal(Path.Combine(s_folder, "clip.mp4"), result);
    }

    [Fact]
    public void WritesToTheOutputFolderWhenSet()
    {
        string target = Path.Combine(s_folder, "converted");

        string result = OutputPathResolver.Resolve(Path.Combine(s_folder, "clip.mkv"), "mp4", target, null, _ => false);

        Assert.Equal(Path.Combine(target, "clip.mp4"), result);
    }

    [Fact]
    public void NumbersTheNameInsteadOfOverwriting()
    {
        var taken = new HashSet<string>
        {
            Path.Combine(s_folder, "clip.mp4"),
            Path.Combine(s_folder, "clip (1).mp4"),
        };

        string result = OutputPathResolver.Resolve(Path.Combine(s_folder, "clip.mp4"), "mp4", null, null, taken.Contains);

        Assert.Equal(Path.Combine(s_folder, "clip (2).mp4"), result);
    }

    [Fact]
    public void SuffixGoesBeforeTheExtension()
    {
        string result = OutputPathResolver.Resolve(
            Path.Combine(s_folder, "clip.mp4"), "mp4", null, " (compressed)", _ => false);

        Assert.Equal(Path.Combine(s_folder, "clip (compressed).mp4"), result);
    }

    [Fact]
    public void DotsInTheNameAreKept()
    {
        string result = OutputPathResolver.Resolve(Path.Combine(s_folder, "holiday.2024.final.mov"), "mp4", null, null, _ => false);

        Assert.Equal(Path.Combine(s_folder, "holiday.2024.final.mp4"), result);
    }

    [Fact]
    public void PartialPathKeepsTheExtensionLast()
    {
        Assert.Equal(
            Path.Combine(s_folder, "clip (1).partial.mp4"),
            OutputPathResolver.PartialPathFor(Path.Combine(s_folder, "clip (1).mp4")));
    }
}

public class MediaTypesTests
{
    [Theory]
    [InlineData(@"C:\a\clip.MP4", MediaKind.Video)]
    [InlineData(@"C:\a\clip.mkv", MediaKind.Video)]
    [InlineData(@"C:\a\loop.gif", MediaKind.Video)]
    [InlineData(@"C:\a\song.flac", MediaKind.Audio)]
    [InlineData(@"C:\a\photo.JPEG", MediaKind.Image)]
    [InlineData(@"C:\a\notes.txt", MediaKind.None)]
    [InlineData(@"C:\a\noextension", MediaKind.None)]
    public void ClassifiesByExtension(string path, MediaKind expected)
    {
        Assert.Equal(expected, MediaTypes.Classify(path));
    }

    [Fact]
    public void SummaryIgnoresUnsupportedFiles()
    {
        SelectionSummary summary = SelectionSummary.From([@"C:\a\one.jpeg", @"C:\a\two.jpg", @"C:\a\notes.txt"]);

        Assert.Equal(MediaKind.Image, summary.Kinds);
        Assert.Equal("jpg", summary.CommonExtension);
        Assert.Equal(2, summary.SupportedCount);
    }

    [Fact]
    public void SummaryHasNoCommonExtensionWhenTheyDiffer()
    {
        SelectionSummary summary = SelectionSummary.From([@"C:\a\one.mp4", @"C:\a\two.wav"]);

        Assert.Equal(MediaKind.Video | MediaKind.Audio, summary.Kinds);
        Assert.Null(summary.CommonExtension);
    }
}

public class ContextMenuModelTests
{
    private static readonly Preset s_userVideoPreset = new()
    {
        Id = "user-1",
        Name = "Phone 720p",
        Settings = new() { Format = "mp4", MaxHeight = 720 },
    };

    /// <summary>The titles in the order the flyout shows them, with separators as "-".</summary>
    private static string[] Menu(string[] paths, params Preset[] userPresets)
    {
        IReadOnlyDictionary<string, string> visible = ContextMenuModel.Visible(SelectionSummary.From(paths), userPresets);
        return
        [
            .. ContextMenuModel.AllEntries(userPresets)
                .Where(e => visible.ContainsKey(e.Id))
                .Select(e => e.Kind == MenuEntryKind.Separator ? "-" : visible[e.Id]),
        ];
    }

    [Fact]
    public void VideoGetsVideoFormatsAndOneAudioEntry()
    {
        Assert.Equal(
            ["MP4 (H.264)", "MP4 (HEVC)", "WebM (VP9)", "GIF", "Extract audio (MP3)", "-", "Compress…", "Custom…"],
            Menu([@"C:\a\clip.mkv"]));
    }

    [Fact]
    public void AudioHidesTheFormatItAlreadyHas()
    {
        Assert.Equal(["MP3", "FLAC", "Opus", "-", "Custom…"], Menu([@"C:\a\tone.wav"]));
    }

    [Fact]
    public void ImageHidesTheFormatItAlreadyHas()
    {
        Assert.Equal(["PNG", "WebP", "-", "Custom…"], Menu([@"C:\a\photo.jpeg"]));
    }

    [Fact]
    public void Mp4CanStillBeReencodedToMp4()
    {
        Assert.Contains("MP4 (H.264)", Menu([@"C:\a\clip.mp4"]));
    }

    [Fact]
    public void GifIsNotOfferedForAGif()
    {
        Assert.DoesNotContain("GIF", Menu([@"C:\a\loop.gif"]));
    }

    [Fact]
    public void VideoAndAudioTogetherGetTheAudioFormats()
    {
        Assert.Equal(["MP3", "FLAC", "WAV", "Opus", "-", "Custom…"], Menu([@"C:\a\clip.mkv", @"C:\a\tone.aac"]));
    }

    [Fact]
    public void VideoAndImageTogetherOnlyGetCustom()
    {
        Assert.Equal(["Custom…"], Menu([@"C:\a\clip.mkv", @"C:\a\photo.png"]));
    }

    [Fact]
    public void UnsupportedSelectionGetsNothing()
    {
        Assert.Empty(Menu([@"C:\a\notes.txt"]));
    }

    [Fact]
    public void UserPresetsFollowTheBuiltInOnesAfterASeparator()
    {
        string[] menu = Menu([@"C:\a\clip.mkv"], s_userVideoPreset);

        Assert.Equal(
            ["MP4 (H.264)", "MP4 (HEVC)", "WebM (VP9)", "GIF", "Extract audio (MP3)", "-", "Phone 720p", "-", "Compress…", "Custom…"],
            menu);
    }

    [Fact]
    public void UserPresetsAreHiddenForFilesTheyCannotConvert()
    {
        Assert.DoesNotContain("Phone 720p", Menu([@"C:\a\tone.wav"], s_userVideoPreset));
    }

    [Fact]
    public void EveryVisibleIdIsAnEnumeratedEntry()
    {
        // Explorer only asks entries from AllEntries for their state, so an id
        // missing there could never be shown.
        HashSet<string> enumerated = [.. ContextMenuModel.AllEntries([s_userVideoPreset]).Select(e => e.Id)];
        foreach (string path in new[] { @"C:\a\clip.mkv", @"C:\a\tone.wav", @"C:\a\photo.png" })
        {
            Assert.Subset(enumerated, ContextMenuModel.Visible(SelectionSummary.From([path]), [s_userVideoPreset]).Keys.ToHashSet());
        }
    }

    [Fact]
    public void BuiltInPresetsUseKnownFormatsAndUniqueIds()
    {
        Assert.All(PresetCatalog.BuiltIn, p => Assert.NotNull(FormatCatalog.Find(p.Settings.Format)));
        Assert.Equal(PresetCatalog.BuiltIn.Count, PresetCatalog.BuiltIn.Select(p => p.Id).Distinct().Count());
    }
}

public class FormatCatalogTests
{
    [Fact]
    public void VideoCanBecomeVideoOrAudio()
    {
        string[] ids = [.. FormatCatalog.ForInputs(MediaKind.Video).Select(f => f.Id)];

        Assert.Contains("mp4", ids);
        Assert.Contains("mp3", ids);
        Assert.DoesNotContain("png", ids);
    }

    [Fact]
    public void AudioCanOnlyBecomeAudio()
    {
        Assert.All(FormatCatalog.ForInputs(MediaKind.Audio), f => Assert.Equal(MediaKind.Audio, f.Kind));
    }

    [Fact]
    public void VideoAndImageShareNoFormat()
    {
        Assert.Empty(FormatCatalog.ForInputs(MediaKind.Video | MediaKind.Image));
    }
}

public class LaunchRequestTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ffconvert-launch-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void NoArgumentsJustOpensTheApp()
    {
        LaunchRequest request = LaunchRequest.Parse([]);

        Assert.Null(request.Command);
        Assert.Empty(request.Files);
    }

    [Fact]
    public void ShortSelectionsTravelInline()
    {
        string[] files = [@"C:\videos\a b.mkv", @"C:\videos\c.mkv"];

        List<string> args = LaunchRequest.ToArguments("video-mp4-h264", files, _folder);
        LaunchRequest request = LaunchRequest.Parse(args);

        Assert.Equal("video-mp4-h264", request.Command);
        Assert.Equal(files, request.Files);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void LongSelectionsTravelInAListFileThatIsDeletedAfterReading()
    {
        string[] files = [.. Enumerable.Range(0, 400).Select(i => $@"C:\videos\holiday recordings\clip number {i:0000} ü.mkv")];

        List<string> args = LaunchRequest.ToArguments("custom", files, _folder);
        Assert.Equal(4, args.Count);
        Assert.True(File.Exists(args[3]));

        LaunchRequest request = LaunchRequest.Parse(args);

        Assert.Equal("custom", request.Command);
        Assert.Equal(files, request.Files);
        Assert.False(File.Exists(args[3]));
    }

    [Fact]
    public void BareFileArgumentsAreMadeAbsolute()
    {
        LaunchRequest request = LaunchRequest.Parse(["clip.mkv"]);

        Assert.Null(request.Command);
        Assert.Equal(Path.GetFullPath("clip.mkv"), Assert.Single(request.Files));
    }

    [Fact]
    public void SurvivesTheTripBetweenInstances()
    {
        var request = new LaunchRequest { Command = "compress", Files = [@"C:\a\ä.mp4", @"C:\a\b.mp4"] };

        LaunchRequest? received = LaunchRequest.FromJson(request.ToJson());

        Assert.NotNull(received);
        Assert.Equal(request.Command, received.Command);
        Assert.Equal(request.Files, received.Files);
    }

    [Fact]
    public void GarbageFromThePipeIsRejected()
    {
        Assert.Null(LaunchRequest.FromJson("not json"));
    }
}

public class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ffconvert-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        AppSettings settings = SettingsStore.Load(Path.Combine(_folder, "settings.json"));

        Assert.Null(settings.FfmpegPath);
        Assert.False(settings.UseHardwareEncoding);
        Assert.True(settings.NotifyWhenDone);
        Assert.True(settings.CloseWhenDone);
        Assert.Empty(settings.UserPresets);
    }

    [Fact]
    public void RoundTripsEverythingIncludingPresets()
    {
        string path = Path.Combine(_folder, "settings.json");
        var settings = new AppSettings
        {
            FfmpegPath = @"D:\tools\ffmpeg",
            UseHardwareEncoding = true,
            OutputFolder = @"D:\converted",
            NotifyWhenDone = false,
            CloseWhenDone = false,
            UserPresets =
            [
                new Preset
                {
                    Id = "user-1",
                    Name = "Phone 720p",
                    Settings = new() { Format = "mp4", VideoCodec = Codecs.Hevc, Quality = Quality.Small, MaxHeight = 720, FrameRate = 30 },
                },
            ],
        };

        SettingsStore.Save(settings, path);
        AppSettings loaded = SettingsStore.Load(path);

        Assert.Equal(settings.FfmpegPath, loaded.FfmpegPath);
        Assert.True(loaded.UseHardwareEncoding);
        Assert.Equal(settings.OutputFolder, loaded.OutputFolder);
        Assert.False(loaded.NotifyWhenDone);
        Assert.False(loaded.CloseWhenDone);
        Preset preset = Assert.Single(loaded.UserPresets);
        Assert.Equal(settings.UserPresets[0], preset);
        Assert.Equal(MediaKind.Video, preset.Inputs);
    }

    [Fact]
    public void CorruptFileGivesDefaultsInsteadOfFailing()
    {
        string path = Path.Combine(_folder, "settings.json");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, "{ this is not json");

        Assert.Empty(SettingsStore.Load(path).UserPresets);
    }
}

/// <summary>Things that have to agree across projects but that the compiler cannot check.</summary>
public class ManifestTests
{
    private static readonly string s_root = FindRepositoryRoot();
    private static readonly XDocument s_manifest =
        XDocument.Load(Path.Combine(s_root, "src", "FFConvert.App", "Package.appxmanifest"));

    [Fact]
    public void ContextMenuIsRegisteredForAllFiles()
    {
        // Explorer ignores verbs registered for single extensions; the handler
        // filters by file type itself.
        string[] registered =
        [
            .. s_manifest.Descendants().Where(e => e.Name.LocalName == "ItemType").Select(e => e.Attribute("Type")!.Value),
        ];

        Assert.Equal(["*"], registered);
    }

    [Fact]
    public void ManifestAndShellExtensionAgreeOnTheClassId()
    {
        string source = File.ReadAllText(Path.Combine(s_root, "src", "FFConvert.Shell", "Exports.cs"));
        string clsid = Regex.Match(source, "CommandClsid = new\\(\"([0-9A-Fa-f-]+)\"\\)").Groups[1].Value;
        Assert.NotEmpty(clsid);

        string[] inManifest =
        [
            .. s_manifest.Descendants()
                .Where(e => e.Name.LocalName is "Verb" or "Class")
                .Select(e => (e.Attribute("Clsid") ?? e.Attribute("Id"))!.Value)
                .Where(v => Guid.TryParse(v, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        Assert.Equal(clsid, Assert.Single(inManifest), ignoreCase: true);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "FFConvert.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("FFConvert.slnx was not found above the test output folder.");
    }
}
