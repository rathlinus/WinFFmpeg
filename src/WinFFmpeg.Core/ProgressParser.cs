using System.Globalization;

namespace WinFFmpeg.Core;

public readonly record struct ProgressSnapshot(TimeSpan? OutTime, double? Speed, long? TotalSize, bool Finished);

/// <summary>
/// Parses the <c>key=value</c> blocks ffmpeg writes with <c>-progress</c>. Each block
/// ends with a <c>progress=</c> line, which is when a snapshot is produced.
/// </summary>
public sealed class ProgressParser
{
    private TimeSpan? _outTime;
    private double? _speed;
    private long? _totalSize;

    public ProgressSnapshot? Feed(string line)
    {
        int separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return null;
        }

        ReadOnlySpan<char> key = line.AsSpan(0, separator).Trim();
        ReadOnlySpan<char> value = line.AsSpan(separator + 1).Trim();

        switch (key)
        {
            // out_time_ms is in microseconds too; old ffmpeg builds only write that one.
            case "out_time_us" or "out_time_ms":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long microseconds)
                    && microseconds >= 0)
                {
                    _outTime = TimeSpan.FromMicroseconds(microseconds);
                }

                break;

            case "speed":
                if (value.EndsWith("x")
                    && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double speed))
                {
                    _speed = speed;
                }

                break;

            case "total_size":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long size))
                {
                    _totalSize = size;
                }

                break;

            case "progress":
                return new ProgressSnapshot(_outTime, _speed, _totalSize, value is "end");
        }

        return null;
    }
}
