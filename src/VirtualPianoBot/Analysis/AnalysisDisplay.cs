namespace VirtualPianoBot.Analysis;

public static class AnalysisDisplay
{
    public static string FormatTime(double ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"m\:ss");
    public static string NoteName(int pitch)
    {
        var names = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        if (pitch is < 0 or > 127) return "?";
        return $"{names[pitch % 12]}{(pitch / 12) - 1}";
    }
}
