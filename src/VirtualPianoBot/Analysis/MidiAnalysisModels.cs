namespace VirtualPianoBot.Analysis;

public sealed record MidiTrackInfo(int TrackIndex, string Name, string Instrument, int Channel, int Program, int NoteCount)
{
    public bool IsDrumTrack => Channel == 9;
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? $"Track {TrackIndex + 1} · {Instrument}"
        : $"{Name} · {Instrument}";
    public string Detail => IsDrumTrack
        ? $"Channel 10 · {NoteCount:N0} notes"
        : $"Ch {Channel + 1} · {NoteCount:N0} notes";
}

public sealed record MidiNote(double StartMs, double EndMs, int Pitch, int Velocity, int Channel, int TrackIndex = 0)
{
    public double DurationMs => Math.Max(10, EndMs - StartMs);
}

public sealed record MidiPlaybackData(
    string FileName,
    double DurationMs,
    double SourceBpm,
    int TrackCount,
    int LowestPitch,
    int HighestPitch,
    IReadOnlyList<MidiNote> Notes,
    IReadOnlyList<MidiTrackInfo> Tracks);

public sealed record MidiAnalysisResult(
    string FileName,
    int NoteCount,
    double DurationMs,
    double SourceBpm,
    int TrackCount,
    int LowestPitch,
    int HighestPitch,
    double NotesPerSecond,
    double PeakNotesPerSecond,
    double TypicalNoteLengthMs,
    double ChordDensity,
    int MaxOverlap,
    double PlayableCoverage,
    int OutOfRangeNotes,
    double MelodyCandidateScore,
    double Confidence,
    string Difficulty,
    string DifficultyDetail,
    string RecommendedSpeed,
    double RecommendedBpm,
    int RecommendedTranspose,
    string RecommendedRangeMode,
    IReadOnlyList<string> DifficultSections,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<double> DensityTimeline,
    IReadOnlyList<MidiNote> Notes);
