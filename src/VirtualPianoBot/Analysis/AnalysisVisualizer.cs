using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VirtualPianoBot.Analysis;

public sealed class AnalysisVisualizer : Control
{
    public static readonly StyledProperty<MidiAnalysisResult?> ResultProperty =
        AvaloniaProperty.Register<AnalysisVisualizer, MidiAnalysisResult?>(nameof(Result));

    public MidiAnalysisResult? Result
    {
        get => GetValue(ResultProperty);
        set => SetValue(ResultProperty, value);
    }

    static AnalysisVisualizer()
    {
        AffectsRender<AnalysisVisualizer>(ResultProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        var bg = new SolidColorBrush(Color.Parse("#0B1118"));
        context.DrawRectangle(bg, new Pen(new SolidColorBrush(Color.Parse("#263342")), 1), new Rect(0, 0, bounds.Width, bounds.Height), 14, 14);

        var result = Result;
        if (result is null)
        {
            DrawEmpty(context, bounds);
            return;
        }

        var left = 18.0;
        var top = 18.0;
        var width = Math.Max(80, bounds.Width - 36);
        var height = Math.Max(60, bounds.Height - 48);
        var timelineHeight = height * 0.58;
        var pitchTop = top + timelineHeight + 12;

        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#1D2935")), 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = top + timelineHeight * i / 4.0;
            context.DrawLine(gridPen, new Point(left, y), new Point(left + width, y));
        }

        var bins = result.DensityTimeline ?? Array.Empty<double>();
        var max = Math.Max(1, bins.Count == 0 ? 1 : bins.Max());
        if (bins.Count > 0)
        {
            var barW = width / bins.Count;
            for (var i = 0; i < bins.Count; i++)
            {
                var normalized = Math.Clamp(bins[i] / max, 0, 1);
                var h = normalized * timelineHeight;
                var brush = new SolidColorBrush(Color.FromArgb((byte)(90 + normalized * 130), 184, 255, 101));
                context.DrawRectangle(brush, null, new Rect(left + i * barW + 1, top + timelineHeight - h, Math.Max(2, barW - 2), h));
            }
        }

        var cyan = new Pen(new SolidColorBrush(Color.Parse("#5DE4FF")), 2);
        var points = bins.Count > 1
            ? bins.Select((v, i) => new Point(left + width * i / (bins.Count - 1.0), top + timelineHeight - Math.Clamp(v / max, 0, 1) * timelineHeight)).ToArray()
            : Array.Empty<Point>();
        for (var i = 1; i < points.Length; i++) context.DrawLine(cyan, points[i - 1], points[i]);

        var sectionPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 169, 137, 255)), 1, DashStyle.Dash);
        for (var i = 0; i < Math.Min(4, result.DifficultSections.Count); i++)
        {
            var fraction = (i + 1.0) / Math.Max(5, result.DifficultSections.Count + 1);
            var x = left + width * fraction;
            context.DrawLine(sectionPen, new Point(x, top), new Point(x, top + timelineHeight));
        }

        var labelBrush = new SolidColorBrush(Color.Parse("#718091"));
        var typeface = new Typeface("Segoe UI");
        var label = new FormattedText("DENSITY PRESSURE", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, labelBrush);
        context.DrawText(label, new Point(left, 5));
        var peak = new FormattedText($"PEAK {result.PeakNotesPerSecond:0.0}/SEC", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, new SolidColorBrush(Color.Parse("#B8FF65")));
        context.DrawText(peak, new Point(Math.Max(left, bounds.Width - 120), 5));

        var pitchBrush = new SolidColorBrush(Color.Parse("#A989FF"));
        var pitchLineY = pitchTop + 9;
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#263342")), 1), new Point(left, pitchLineY), new Point(left + width, pitchLineY));
        var pitchStart = Math.Clamp(result.LowestPitch, 0, 127);
        var pitchEnd = Math.Clamp(result.HighestPitch, 0, 127);
        var x1 = left + width * pitchStart / 127.0;
        var x2 = left + width * pitchEnd / 127.0;
        context.DrawLine(new Pen(pitchBrush, 8), new Point(x1, pitchLineY), new Point(Math.Max(x1 + 4, x2), pitchLineY));
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#D1FF91")), null, new Point(x1, pitchLineY), 4, 4);
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#5DE4FF")), null, new Point(x2, pitchLineY), 4, 4);
        var rangeText = new FormattedText($"RANGE  {AnalysisDisplay.NoteName(result.LowestPitch)} → {AnalysisDisplay.NoteName(result.HighestPitch)}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, labelBrush);
        context.DrawText(rangeText, new Point(left, pitchTop + 20));
        var coverage = new FormattedText($"PLAYABLE {result.PlayableCoverage * 100:0}%", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, new SolidColorBrush(Color.Parse("#B8FF65")));
        context.DrawText(coverage, new Point(Math.Max(left, bounds.Width - 115), pitchTop + 20));
    }

    private static void DrawEmpty(DrawingContext context, Rect bounds)
    {
        var text = new FormattedText("SELECT A MIDI AND RUN ANALYSIS TO VISUALISE IT", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, new SolidColorBrush(Color.Parse("#718091")));
        context.DrawText(text, new Point(Math.Max(18, (bounds.Width - text.Width) / 2), Math.Max(20, (bounds.Height - text.Height) / 2)));
    }
}
