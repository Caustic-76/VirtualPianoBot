using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VirtualPianoBot.Analysis;

namespace VirtualPianoBot.Studio;

/// <summary>
/// Time-windowed falling-note renderer. MIDI notes are indexed once per source load so
/// animation frames only inspect notes that can actually be visible.
/// </summary>
public sealed class PianoRollVisualizer : Control
{
    public static readonly StyledProperty<MidiAnalysisResult?> ResultProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, MidiAnalysisResult?>(nameof(Result));
    public static readonly StyledProperty<double> PositionMsProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, double>(nameof(PositionMs));
    public static readonly StyledProperty<IReadOnlyList<int>?> SelectedTrackIndicesProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, IReadOnlyList<int>?>(nameof(SelectedTrackIndices));
    public static readonly StyledProperty<double> LookAheadSecondsProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, double>(nameof(LookAheadSeconds), 6.0);
    public static readonly StyledProperty<string> ColorModeProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, string>(nameof(ColorMode), "Register");
    public static readonly StyledProperty<string> DetailModeProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, string>(nameof(DetailMode), "Auto");
    public static readonly StyledProperty<bool> ShowBeatGridProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, bool>(nameof(ShowBeatGrid), true);
    public static readonly StyledProperty<bool> ShowNoteLabelsProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, bool>(nameof(ShowNoteLabels), false);
    public static readonly StyledProperty<int> TransposeSemitonesProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, int>(nameof(TransposeSemitones), 0);
    public static readonly StyledProperty<bool> FilterTracksEnabledProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, bool>(nameof(FilterTracksEnabled), false);
    public static readonly StyledProperty<string> AccentColorProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, string>(nameof(AccentColor), "#B8FF65");
    public static readonly StyledProperty<string> ThemeModeProperty =
        AvaloniaProperty.Register<PianoRollVisualizer, string>(nameof(ThemeMode), "Dark");

    private static readonly Color[] TrackPalette =
    [
        Color.Parse("#9E72FF"), Color.Parse("#5DE4FF"), Color.Parse("#B8FF65"),
        Color.Parse("#FF8BCB"), Color.Parse("#FFB86B"), Color.Parse("#7DDEB5"),
        Color.Parse("#82A8FF"), Color.Parse("#F1E56D"), Color.Parse("#D39BFF"),
        Color.Parse("#70D6FF"), Color.Parse("#FFA69E"), Color.Parse("#C3F584")
    ];

    private MidiAnalysisResult? _cachedResult;
    private MidiNote[] _sortedNotes = [];
    private double[] _prefixMaxEndMs = [];

    public MidiAnalysisResult? Result { get => GetValue(ResultProperty); set => SetValue(ResultProperty, value); }
    public double PositionMs { get => GetValue(PositionMsProperty); set => SetValue(PositionMsProperty, value); }
    public IReadOnlyList<int>? SelectedTrackIndices { get => GetValue(SelectedTrackIndicesProperty); set => SetValue(SelectedTrackIndicesProperty, value); }
    public double LookAheadSeconds { get => GetValue(LookAheadSecondsProperty); set => SetValue(LookAheadSecondsProperty, value); }
    public string ColorMode { get => GetValue(ColorModeProperty); set => SetValue(ColorModeProperty, value); }
    public string DetailMode { get => GetValue(DetailModeProperty); set => SetValue(DetailModeProperty, value); }
    public bool ShowBeatGrid { get => GetValue(ShowBeatGridProperty); set => SetValue(ShowBeatGridProperty, value); }
    public bool ShowNoteLabels { get => GetValue(ShowNoteLabelsProperty); set => SetValue(ShowNoteLabelsProperty, value); }
    public int TransposeSemitones { get => GetValue(TransposeSemitonesProperty); set => SetValue(TransposeSemitonesProperty, value); }
    public bool FilterTracksEnabled { get => GetValue(FilterTracksEnabledProperty); set => SetValue(FilterTracksEnabledProperty, value); }
    public string AccentColor { get => GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public string ThemeMode { get => GetValue(ThemeModeProperty); set => SetValue(ThemeModeProperty, value); }

    static PianoRollVisualizer() => AffectsRender<PianoRollVisualizer>(
        ResultProperty, PositionMsProperty, SelectedTrackIndicesProperty, LookAheadSecondsProperty,
        ColorModeProperty, DetailModeProperty, ShowBeatGridProperty, ShowNoteLabelsProperty,
        TransposeSemitonesProperty, FilterTracksEnabledProperty, AccentColorProperty, ThemeModeProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 20 || bounds.Height <= 20) return;

        var accent = ParseAccentColor();
        var lightMode = string.Equals(ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);
        var background = ThemeMode switch
        {
            "Light" => Color.Parse("#F2F5F9"),
            "Midnight" => Color.Parse("#040711"),
            "Graphite" => Color.Parse("#0D0E11"),
            _ => Color.Parse("#05080C")
        };
        context.DrawRectangle(new SolidColorBrush(background), null, bounds);

        var result = Result;
        if (result is null || result.Notes.Count == 0)
        {
            DrawLabel(context, "STUDIO READY", new Point(20, bounds.Height / 2 - 16), 11, accent);
            DrawLabel(context, "Load a MIDI to populate the falling-note view", new Point(20, bounds.Height / 2 + 6), 10, lightMode ? Color.Parse("#69768A") : Color.Parse("#718091"));
            return;
        }

        EnsureNoteCache(result);

        const int firstPitch = 36;
        const int lastPitch = 96;
        const double hitLineOffset = 18;
        var lookAheadMs = Math.Clamp(double.IsFinite(LookAheadSeconds) ? LookAheadSeconds : 6.0, 2.0, 12.0) * 1000.0;
        var hitLine = Math.Max(40, bounds.Height - hitLineOffset);
        var travelHeight = Math.Max(60, hitLine - 18);
        var laneWidth = bounds.Width / (lastPitch - firstPitch + 1.0);
        var selected = FilterTracksEnabled
            ? (SelectedTrackIndices ?? Array.Empty<int>()).ToHashSet()
            : null;

        DrawBackground(context, bounds.Width, bounds.Height, firstPitch, lastPitch, laneWidth, hitLine, PositionMs, lookAheadMs, accent, ThemeMode, lightMode);
        if (ShowBeatGrid && result.SourceBpm is > 0)
            DrawBeatGrid(context, bounds.Width, hitLine, travelHeight, PositionMs, lookAheadMs, result.SourceBpm);
        DrawVisibleNotes(context, bounds.Width, firstPitch, lastPitch, laneWidth, hitLine, travelHeight, lookAheadMs, selected);
        DrawHitLine(context, bounds.Width, hitLine, accent);
    }

    private void EnsureNoteCache(MidiAnalysisResult result)
    {
        if (ReferenceEquals(_cachedResult, result)) return;

        _cachedResult = result;
        _sortedNotes = result.Notes
            .OrderBy(note => note.StartMs)
            .ThenBy(note => note.TrackIndex)
            .ThenBy(note => note.Pitch)
            .ToArray();
        _prefixMaxEndMs = new double[_sortedNotes.Length];
        var maxEnd = double.NegativeInfinity;
        for (var i = 0; i < _sortedNotes.Length; i++)
        {
            maxEnd = Math.Max(maxEnd, _sortedNotes[i].EndMs);
            _prefixMaxEndMs[i] = maxEnd;
        }
    }

    private void DrawVisibleNotes(
        DrawingContext context,
        double width,
        int firstPitch,
        int lastPitch,
        double laneWidth,
        double hitLine,
        double travelHeight,
        double lookAheadMs,
        HashSet<int>? selectedTracks)
    {
        if (_sortedNotes.Length == 0) return;

        // Find the first note whose prefix can still overlap the current play position,
        // then the last note that starts before the top of the visible window.
        var cutoff = PositionMs - 100;
        var lower = FirstPrefixEndAtOrAfter(cutoff);
        var upper = UpperBoundStart(PositionMs + lookAheadMs);
        if (lower >= upper) return;

        var visibleCount = upper - lower;
        var target = DetailMode switch
        {
            "Performance" => 1400,
            "Detail" => 10000,
            _ => 4500
        };
        var stride = visibleCount > target ? (int)Math.Ceiling(visibleCount / (double)target) : 1;

        for (var index = lower; index < upper; index++)
        {
            var note = _sortedNotes[index];
            if (note.EndMs < cutoff) continue;
            if (selectedTracks is not null && !selectedTracks.Contains(note.TrackIndex)) continue;
            var displayPitch = note.Pitch + TransposeSemitones;
            if (displayPitch < firstPitch || displayPitch > lastPitch) continue;

            var active = PositionMs >= note.StartMs && PositionMs <= note.EndMs;
            // Keep all active notes even when dense passages trigger adaptive sampling.
            if (stride > 1 && !active && (index - lower) % stride != 0) continue;

            var timeToStart = note.StartMs - PositionMs;
            var timeToEnd = note.EndMs - PositionMs;
            var bottom = hitLine - (timeToStart / lookAheadMs) * travelHeight;
            var top = hitLine - (timeToEnd / lookAheadMs) * travelHeight;
            var x = (displayPitch - firstPitch) * laneWidth + 1.5;
            var noteWidth = Math.Max(2.5, laneWidth - 3);
            var visibleTop = Math.Max(16, top);
            var visibleBottom = Math.Min(hitLine + 22, bottom);
            if (visibleBottom <= visibleTop) continue;

            var color = GetNoteColor(note, displayPitch);
            var noteHeight = Math.Max(3, visibleBottom - visibleTop);

            if (active)
            {
                var glowLeft = Math.Max(0, x - 5);
                var glowWidth = Math.Max(1, Math.Min(width - glowLeft, noteWidth + 10));
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(42, color.R, color.G, color.B)), null,
                    new Rect(glowLeft, Math.Max(0, visibleTop - 5), glowWidth, noteHeight + 10), 5, 5);
            }

            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(active ? (byte)250 : (byte)220, color.R, color.G, color.B)), null,
                new Rect(x, visibleTop, noteWidth, noteHeight), 3, 3);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(active ? (byte)240 : (byte)95, 255, 255, 255)), null,
                new Rect(x + 1, visibleTop + 1, Math.Min(2, noteWidth), Math.Max(1, noteHeight - 2)), 1, 1);

            if (ShowNoteLabels && laneWidth >= 12 && noteHeight >= 14)
                DrawLabel(context, AnalysisDisplay.NoteName(displayPitch), new Point(x + 2, visibleTop + 2), 7, Color.Parse("#071018"));
        }
    }

    private int FirstPrefixEndAtOrAfter(double positionMs)
    {
        var low = 0;
        var high = _prefixMaxEndMs.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_prefixMaxEndMs[middle] < positionMs) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private int UpperBoundStart(double positionMs)
    {
        var low = 0;
        var high = _sortedNotes.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_sortedNotes[middle].StartMs <= positionMs) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private Color GetNoteColor(MidiNote note, int displayPitch)
    {
        return ColorMode switch
        {
            "Track" => TrackPalette[(int)((uint)Math.Max(0, note.TrackIndex) % (uint)TrackPalette.Length)],
            "Velocity" => VelocityColor(note.Velocity, ParseAccentColor()),
            _ => PitchColor(displayPitch)
        };
    }

    private static Color VelocityColor(int velocity, Color accent)
    {
        var normalized = Math.Clamp(velocity, 0, 127) / 127.0;
        if (normalized < 0.5)
            return Lerp(Color.Parse("#7563DE"), Color.Parse("#5DE4FF"), normalized * 2);
        return Lerp(Color.Parse("#5DE4FF"), accent, (normalized - 0.5) * 2);
    }

    private static Color Lerp(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)(first.A + (second.A - first.A) * amount),
            (byte)(first.R + (second.R - first.R) * amount),
            (byte)(first.G + (second.G - first.G) * amount),
            (byte)(first.B + (second.B - first.B) * amount));
    }

    private static void DrawBackground(
        DrawingContext context,
        double width,
        double height,
        int firstPitch,
        int lastPitch,
        double laneWidth,
        double hitLine,
        double positionMs,
        double lookAheadMs,
        Color accent,
        string themeMode,
        bool lightMode)
    {
        for (var pitch = firstPitch; pitch <= lastPitch; pitch++)
        {
            var x = (pitch - firstPitch) * laneWidth;
            var isC = pitch % 12 == 0;
            var isBlack = IsBlackKey(pitch);
            var lane = lightMode
                ? (isC ? "#D5DEE9" : isBlack ? "#E6EBF2" : "#F1F4F8")
                : themeMode == "Midnight"
                    ? (isC ? "#202C43" : isBlack ? "#080E1B" : "#0C1424")
                    : themeMode == "Graphite"
                        ? (isC ? "#303641" : isBlack ? "#111318" : "#191C22")
                        : (isC ? "#253544" : isBlack ? "#0A1017" : "#101923");
            context.DrawRectangle(new SolidColorBrush(Color.Parse(lane)), null,
                new Rect(x, 0, Math.Max(1, laneWidth - 0.6), height));
            if (isC)
                context.DrawLine(new Pen(new SolidColorBrush(lightMode ? Color.Parse("#AEBBCB") : Color.Parse("#344757")), 1), new Point(x, 0), new Point(x, hitLine));
        }

        var seconds = Math.Clamp((int)Math.Ceiling(lookAheadMs / 1000.0), 2, 12);
        for (var second = 0; second <= seconds; second++)
        {
            var deltaMs = second * 1000.0;
            if (deltaMs > lookAheadMs) continue;
            var y = hitLine - (deltaMs / lookAheadMs) * (hitLine - 18);
            var alpha = (byte)(second == 0 ? 18 : 14 + Math.Min(12, second * 2));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(alpha, 93, 228, 255)), second == 0 ? 1.2 : 0.7),
                new Point(0, y), new Point(width, y));
            if (second > 0)
                DrawLabel(context, $"+{second}s", new Point(7, y - 11), 7, Color.Parse("#637687"));
        }

        DrawLabel(context, "FALLING NOTES", new Point(10, 8), 8, lightMode ? Color.Parse("#566579") : Color.Parse("#708090"));
        DrawLabel(context, AnalysisDisplay.FormatTime(positionMs), new Point(Math.Max(4, width - 52), 8), 8, accent);
    }

    private static void DrawBeatGrid(DrawingContext context, double width, double hitLine, double travelHeight, double positionMs, double lookAheadMs, double bpm)
    {
        if (!double.IsFinite(bpm) || bpm is < 30 or > 300) return;
        var beatMs = 60000.0 / bpm;
        var firstFutureBeat = Math.Floor(Math.Max(0, positionMs) / beatMs) + 1;
        var lastFutureBeat = Math.Ceiling((positionMs + lookAheadMs) / beatMs);
        if (lastFutureBeat - firstFutureBeat > 64) return;

        for (var beatIndex = firstFutureBeat; beatIndex <= lastFutureBeat; beatIndex++)
        {
            var delta = beatIndex * beatMs - positionMs;
            if (delta <= 0 || delta > lookAheadMs) continue;
            var y = hitLine - delta / lookAheadMs * travelHeight;
            var isBar = (long)beatIndex % 4 == 0;
            var alpha = (byte)(isBar ? 76 : 28);
            var penWidth = isBar ? 1.2 : 0.65;
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(alpha, 93, 228, 255)), penWidth),
                new Point(0, y), new Point(width, y));
            if (isBar)
            {
                var barNumber = (long)beatIndex / 4 + 1;
                DrawLabel(context, $"BAR {barNumber}", new Point(width - 64, y - 10), 7, Color.Parse("#6B9AAB"));
            }
        }
    }

    private static void DrawHitLine(DrawingContext context, double width, double y, Color accent)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)), null,
            new Rect(0, Math.Max(0, y - 12), width, 24));
        context.DrawLine(new Pen(new SolidColorBrush(accent), 2), new Point(0, y), new Point(width, y));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), 1), new Point(0, y - 3), new Point(width, y - 3));
        DrawLabel(context, "PLAY LINE", new Point(9, y - 27), 7, accent);
    }

    private Color ParseAccentColor()
    {
        try { return Color.Parse(AccentColor); }
        catch (FormatException) { return Color.Parse("#B8FF65"); }
    }

    private static bool IsBlackKey(int pitch) => pitch % 12 is 1 or 3 or 6 or 8 or 10;

    private static Color PitchColor(int pitch)
    {
        var register = (pitch - 36) / 12;
        return register switch
        {
            <= 1 => Color.Parse("#8C6BFF"),
            2 => Color.Parse("#9E72FF"),
            3 => Color.Parse("#B878FF"),
            4 => Color.Parse("#5DE4FF"),
            5 => Color.Parse("#55D7FF"),
            _ => Color.Parse("#72CFFF")
        };
    }

    private static void DrawLabel(DrawingContext context, string text, Point point, double size, Color color)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, new SolidColorBrush(color));
        context.DrawText(formatted, point);
    }
}
