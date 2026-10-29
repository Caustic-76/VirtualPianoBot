using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using VirtualPianoBot.Analysis;

namespace VirtualPianoBot.Studio;

public sealed class StudioEffectsOverlay : Control
{
    public static readonly StyledProperty<MidiAnalysisResult?> ResultProperty =
        AvaloniaProperty.Register<StudioEffectsOverlay, MidiAnalysisResult?>(nameof(Result));
    public static readonly StyledProperty<double> PositionMsProperty =
        AvaloniaProperty.Register<StudioEffectsOverlay, double>(nameof(PositionMs));
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<StudioEffectsOverlay, bool>(nameof(IsPlaying));

    private readonly DispatcherTimer _timer;
    private double _phase;

    public MidiAnalysisResult? Result { get => GetValue(ResultProperty); set => SetValue(ResultProperty, value); }
    public double PositionMs { get => GetValue(PositionMsProperty); set => SetValue(PositionMsProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }

    static StudioEffectsOverlay() => AffectsRender<StudioEffectsOverlay>(ResultProperty, PositionMsProperty, IsPlayingProperty);

    public StudioEffectsOverlay()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) =>
        {
            _phase += IsPlaying ? 0.075 : 0.018;
            InvalidateVisual();
        };
        _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        AttachedToVisualTree += (_, _) => _timer.Start();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var b = Bounds;
        if (b.Width < 10 || b.Height < 10) return;

        var result = Result;
        var duration = Math.Max(1, result?.DurationMs ?? 1);
        var playX = Math.Clamp(PositionMs / duration * b.Width, 0, b.Width);
        var pulse = (Math.Sin(_phase) + 1) * 0.5;

        // Keep the overlay intentionally restrained. The piano roll owns the information;
        // this layer only adds depth and a little motion.
        var vignette = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(16, 0, 0, 0), 0),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.42),
                new GradientStop(Color.FromArgb(18, 0, 0, 0), 1)
            }
        };
        context.DrawRectangle(vignette, null, b);

        if (IsPlaying)
        {
            var sweepAlpha = (byte)(8 + pulse * 7);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(sweepAlpha, 184, 255, 101)), null,
                new Rect(Math.Max(0, playX - 28), 0, 56, b.Height));
        }

        // Tiny corner status ticks instead of the previous large floating circles.
        var tickAlpha = (byte)(35 + pulse * 35);
        var tickBrush = new SolidColorBrush(Color.FromArgb(tickAlpha, 93, 228, 255));
        const double inset = 10;
        context.DrawRectangle(tickBrush, null, new Rect(inset, inset, 28, 1));
        context.DrawRectangle(tickBrush, null, new Rect(b.Width - inset - 28, inset, 28, 1));
        context.DrawRectangle(tickBrush, null, new Rect(inset, b.Height - inset, 28, 1));
        context.DrawRectangle(tickBrush, null, new Rect(b.Width - inset - 28, b.Height - inset, 28, 1));
    }
}

public sealed class ReactivePianoKeyboard : Control
{
    public static readonly StyledProperty<MidiAnalysisResult?> ResultProperty =
        AvaloniaProperty.Register<ReactivePianoKeyboard, MidiAnalysisResult?>(nameof(Result));
    public static readonly StyledProperty<double> PositionMsProperty =
        AvaloniaProperty.Register<ReactivePianoKeyboard, double>(nameof(PositionMs));
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<ReactivePianoKeyboard, bool>(nameof(IsPlaying));
    public static readonly StyledProperty<int> TransposeSemitonesProperty =
        AvaloniaProperty.Register<ReactivePianoKeyboard, int>(nameof(TransposeSemitones), 0);
    public static readonly StyledProperty<IReadOnlyList<int>?> SelectedTrackIndicesProperty =
        AvaloniaProperty.Register<ReactivePianoKeyboard, IReadOnlyList<int>?>(nameof(SelectedTrackIndices));
    public static readonly StyledProperty<bool> FilterTracksEnabledProperty =
        AvaloniaProperty.Register<ReactivePianoKeyboard, bool>(nameof(FilterTracksEnabled), false);

    private MidiAnalysisResult? _cachedResult;
    private MidiNote[] _sortedNotes = [];
    private double[] _prefixMaxEndMs = [];

    public MidiAnalysisResult? Result { get => GetValue(ResultProperty); set => SetValue(ResultProperty, value); }
    public double PositionMs { get => GetValue(PositionMsProperty); set => SetValue(PositionMsProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public int TransposeSemitones { get => GetValue(TransposeSemitonesProperty); set => SetValue(TransposeSemitonesProperty, value); }
    public IReadOnlyList<int>? SelectedTrackIndices { get => GetValue(SelectedTrackIndicesProperty); set => SetValue(SelectedTrackIndicesProperty, value); }
    public bool FilterTracksEnabled { get => GetValue(FilterTracksEnabledProperty); set => SetValue(FilterTracksEnabledProperty, value); }

    static ReactivePianoKeyboard() => AffectsRender<ReactivePianoKeyboard>(ResultProperty, PositionMsProperty,
        IsPlayingProperty, TransposeSemitonesProperty, SelectedTrackIndicesProperty, FilterTracksEnabledProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var b = Bounds;
        if (b.Width < 10 || b.Height < 10) return;

        const int firstPitch = 36;
        const int lastPitch = 96;
        var active = new HashSet<int>();
        if (Result is { } result)
        {
            EnsureNoteCache(result);
            var selected = FilterTracksEnabled
                ? (SelectedTrackIndices ?? Array.Empty<int>()).ToHashSet()
                : null;
            var lower = FirstPrefixEndAtOrAfter(PositionMs);
            var upper = UpperBoundStart(PositionMs);
            for (var index = lower; index < upper; index++)
            {
                var note = _sortedNotes[index];
                if (PositionMs < note.StartMs || PositionMs > note.EndMs) continue;
                if (selected is not null && !selected.Contains(note.TrackIndex)) continue;
                var pitch = note.Pitch + TransposeSemitones;
                if (pitch is >= firstPitch and <= lastPitch) active.Add(pitch);
            }
        }

        var whitePitches = Enumerable.Range(firstPitch, lastPitch - firstPitch + 1).Where(p => !IsBlackKey(p)).ToList();
        var whiteWidth = b.Width / whitePitches.Count;
        var whiteHeight = b.Height;

        context.DrawRectangle(new SolidColorBrush(Color.Parse("#080C12")), null, b);

        // White keys first, with restrained borders and a soft active-state fill.
        for (var i = 0; i < whitePitches.Count; i++)
        {
            var pitch = whitePitches[i];
            var x = i * whiteWidth;
            var isActive = active.Contains(pitch);
            var fill = isActive ? Color.Parse("#B8FF65") : Color.Parse("#C9D1D8");
            context.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(Color.Parse("#354150")), 1),
                new Rect(x + 0.5, 0, Math.Max(1, whiteWidth - 1), whiteHeight));

            if (isActive)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(75, 184, 255, 101)), null,
                    new Rect(x, 0, whiteWidth, whiteHeight));
            }

            if (pitch % 12 == 0)
                DrawKeyLabel(context, AnalysisDisplay.NoteName(pitch), new Point(x + 4, whiteHeight - 14), 7, Color.Parse("#596675"));
        }

        // Black keys sit above the white keys, giving the keyboard its familiar silhouette.
        for (var pitch = firstPitch; pitch <= lastPitch; pitch++)
        {
            if (!IsBlackKey(pitch)) continue;
            var whiteBefore = Enumerable.Range(firstPitch, pitch - firstPitch).Count(p => !IsBlackKey(p));
            var x = whiteBefore * whiteWidth - whiteWidth * 0.34;
            var keyWidth = whiteWidth * 0.68;
            var isActive = active.Contains(pitch);
            var fill = isActive ? Color.Parse("#9F78FF") : Color.Parse("#101722");
            context.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(Color.Parse("#253241")), 1),
                new Rect(x, 0, keyWidth, whiteHeight * 0.60), 0, 0);
            if (isActive)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(55, 159, 120, 255)), null,
                    new Rect(x - 2, 0, keyWidth + 4, whiteHeight * 0.60));
            }
        }
    }

    private void EnsureNoteCache(MidiAnalysisResult result)
    {
        if (ReferenceEquals(_cachedResult, result)) return;
        _cachedResult = result;
        _sortedNotes = result.Notes.OrderBy(note => note.StartMs).ToArray();
        _prefixMaxEndMs = new double[_sortedNotes.Length];
        var maxEnd = double.NegativeInfinity;
        for (var i = 0; i < _sortedNotes.Length; i++)
        {
            maxEnd = Math.Max(maxEnd, _sortedNotes[i].EndMs);
            _prefixMaxEndMs[i] = maxEnd;
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

    private static bool IsBlackKey(int pitch) => pitch % 12 is 1 or 3 or 6 or 8 or 10;

    private static void DrawKeyLabel(DrawingContext context, string text, Point point, double size, Color color)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, size, new SolidColorBrush(color));
        context.DrawText(formatted, point);
    }
}
