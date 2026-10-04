using System.Globalization;

namespace FFConvert.Core;

/// <summary>Positions in a clip as people type them: "90", "1:30" or "0:01:30.5".</summary>
public static class TimeText
{
    /// <summary>
    /// Parses a position into seconds. Empty text is valid and gives null; anything
    /// that is not a time returns false.
    /// </summary>
    public static bool TryParse(string? text, out double? seconds)
    {
        seconds = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string[] parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3)
        {
            return false;
        }

        double total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            // Only the last part, the seconds, may have a fraction.
            NumberStyles style = i == parts.Length - 1 ? NumberStyles.AllowDecimalPoint : NumberStyles.None;
            if (!double.TryParse(parts[i], style, CultureInfo.InvariantCulture, out double value))
            {
                return false;
            }

            if (parts.Length > 1 && i > 0 && value >= 60)
            {
                return false;
            }

            total = total * 60 + value;
        }

        seconds = total;
        return true;
    }

    public static string Format(TimeSpan time) =>
        time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
