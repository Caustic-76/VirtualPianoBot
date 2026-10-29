using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VirtualPianoBot.Analysis;

public sealed class MidiAnalysisService
{
    private const int MidiLow = 36;  // C2
    private const int MidiHigh = 96; // C7

    /// <summary>
    /// Loads only the MIDI note/timing data needed by Performance Studio.
    /// This deliberately skips all playability, density, melody and difficulty analysis.
    /// </summary>
    public MidiPlaybackData LoadPlaybackData(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var parsed = Parse(bytes);
        if (parsed.Notes.Count == 0)
            throw new InvalidDataException("The MIDI contains no playable note events.");

        var notes = parsed.Notes.OrderBy(n => n.StartMs).ThenBy(n => n.Pitch).ToArray();
        var duration = Math.Max(parsed.DurationMs, notes.Max(n => n.EndMs));
        return new MidiPlaybackData(
            Path.GetFileName(path),
            duration,
            parsed.SourceBpm,
            parsed.TrackCount,
            notes.Min(n => n.Pitch),
            notes.Max(n => n.Pitch),
            notes,
            parsed.Tracks);
    }

    public MidiAnalysisResult Analyze(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var parsed = Parse(bytes);
        if (parsed.Notes.Count == 0) throw new InvalidDataException("The MIDI contains no playable note events.");

        var notes = parsed.Notes.OrderBy(n => n.StartMs).ThenBy(n => n.Pitch).ToArray();
        var duration = Math.Max(parsed.DurationMs, notes.Max(n => n.EndMs));
        var low = notes.Min(n => n.Pitch);
        var high = notes.Max(n => n.Pitch);
        var nps = notes.Length / Math.Max(duration / 1000.0, 0.001);
        var peakNps = CalculatePeakDensity(notes);
        var typicalLength = Median(notes.Select(n => n.DurationMs));
        var overlap = CalculateOverlap(notes, out var chordWindows);
        var chordDensity = chordWindows / Math.Max(1.0, notes.Length);
        var playable = notes.Count(n => n.Pitch is >= MidiLow and <= MidiHigh);
        var coverage = playable / (double)notes.Length;
        var outOfRange = notes.Length - playable;
        var range = high - low;
        var motion = CalculatePitchMotion(notes);
        var melodyScore = CalculateMelodyScore(nps, peakNps, range, chordDensity, motion, typicalLength);
        var confidence = CalculateConfidence(notes.Length, duration, range, chordDensity);
        var difficulty = CalculateDifficulty(nps, peakNps, range, chordDensity, overlap);
        var transpose = RecommendTranspose(low, high);
        var rangeMode = outOfRange == 0 ? "Exact pitch" : coverage >= 0.92 ? "Smart Fit" : "Smart Fit / review outliers";
        var recommendedBpm = RecommendBpm(parsed.SourceBpm, nps, peakNps);
        var difficult = FindDifficultSections(notes, duration);
        var densityTimeline = BuildDensityTimeline(notes, duration, 48);
        var warnings = new List<string>();
        if (outOfRange > 0) warnings.Add($"{outOfRange:N0} note(s) fall outside C2–C7.");
        if (overlap >= 5) warnings.Add("Dense chords detected. Physical keyboard conflicts may occur.");
        if (range > 48) warnings.Add("Very wide pitch range. Consider Smart Fit or transpose.");

        return new MidiAnalysisResult(
            Path.GetFileName(path), notes.Length, duration, parsed.SourceBpm, parsed.TrackCount,
            low, high, nps, peakNps, typicalLength, chordDensity, overlap, coverage, outOfRange,
            melodyScore, confidence, difficulty.Label, difficulty.Detail,
            $"{recommendedBpm:0} BPM / {Math.Clamp(recommendedBpm / Math.Max(parsed.SourceBpm, 1), 0.1, 3.0) * 100:0}%",
            recommendedBpm, transpose, rangeMode, difficult, warnings, densityTimeline, notes);
    }

    private static double CalculateMelodyScore(double nps, double peak, int range, double chordDensity, double motion, double length)
    {
        var density = Math.Clamp(1.0 - Math.Abs(nps - 4.0) / 8.0, 0, 1);
        var rangeScore = Math.Clamp(1.0 - Math.Abs(range - 24) / 48.0, 0, 1);
        var motionScore = Math.Clamp(motion / 10.0, 0, 1);
        var chordScore = 1.0 - Math.Clamp(chordDensity * 4.0, 0, 1);
        var lengthScore = Math.Clamp(1.0 - Math.Abs(length - 280) / 500.0, 0, 1);
        return Math.Round(Math.Clamp((density * .25 + rangeScore * .2 + motionScore * .2 + chordScore * .25 + lengthScore * .1) * 100, 0, 100), 1);
    }

    private static double CalculateConfidence(int count, double duration, int range, double chordDensity) =>
        Math.Round(Math.Clamp((Math.Min(1, count / 80.0) * .45 + Math.Min(1, duration / 30000.0) * .2 + Math.Min(1, range / 36.0) * .15 + (1 - Math.Clamp(chordDensity, 0, 1)) * .2) * 100, 0, 100), 1);

    private static (string Label, string Detail) CalculateDifficulty(double nps, double peak, int range, double chords, int overlap)
    {
        var score = nps * 8 + peak * 5 + range * .35 + chords * 80 + overlap * 2;
        return score switch
        {
            < 55 => ("EASY", "Relaxed density and manageable range."),
            < 105 => ("MODERATE", "Some fast passages or coordination pressure."),
            < 165 => ("HARD", "High density, range or chord pressure."),
            _ => ("EXTREME", "Very dense or technically demanding material.")
        };
    }

    private static double RecommendBpm(double source, double nps, double peak)
    {
        var factor = nps > 7.5 || peak > 11 ? .72 : nps > 5.5 || peak > 8 ? .86 : 1.0;
        return Math.Clamp(source * factor, 40, 220);
    }

    private static int RecommendTranspose(int low, int high)
    {
        if (low >= MidiLow && high <= MidiHigh) return 0;
        var center = (low + high) / 2.0;
        var target = (MidiLow + MidiHigh) / 2.0;
        var raw = (int)Math.Round(target - center, MidpointRounding.AwayFromZero);
        return Math.Clamp(raw, -24, 24);
    }

    private static double CalculatePeakDensity(IReadOnlyList<MidiNote> notes)
    {
        if (notes.Count == 0) return 0;
        var starts = notes.Select(n => n.StartMs).OrderBy(x => x).ToArray();
        var best = 0;
        var left = 0;
        for (var right = 0; right < starts.Length; right++)
        {
            while (starts[right] - starts[left] > 1000 && left < right) left++;
            best = Math.Max(best, right - left + 1);
        }
        return best;
    }

    private static int CalculateOverlap(IReadOnlyList<MidiNote> notes, out int chordWindows)
    {
        var events = notes.SelectMany(n => new[] { (n.StartMs, 1), (n.EndMs, -1) }).OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToArray();
        var active = 0; var max = 0; chordWindows = 0; var previous = double.NaN;
        foreach (var e in events)
        {
            if (active >= 2 && !double.IsNaN(previous) && e.Item1 > previous) chordWindows++;
            active = Math.Max(0, active + e.Item2); max = Math.Max(max, active); previous = e.Item1;
        }
        return max;
    }

    private static double CalculatePitchMotion(IReadOnlyList<MidiNote> notes)
    {
        if (notes.Count < 2) return 0;
        double total = 0; var count = 0;
        for (var i = 1; i < notes.Count; i++) { total += Math.Abs(notes[i].Pitch - notes[i - 1].Pitch); count++; }
        return total / count;
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(x => x).ToArray(); if (a.Length == 0) return 0;
        var mid = a.Length / 2; return a.Length % 2 == 0 ? (a[mid - 1] + a[mid]) / 2 : a[mid];
    }

    private static IReadOnlyList<double> BuildDensityTimeline(IReadOnlyList<MidiNote> notes, double duration, int buckets)
    {
        if (notes.Count == 0 || duration <= 0) return Array.Empty<double>();
        var result = new double[buckets];
        var width = duration / buckets;
        foreach (var note in notes)
        {
            var index = Math.Clamp((int)(note.StartMs / Math.Max(width, 1)), 0, buckets - 1);
            result[index]++;
        }
        for (var i = 0; i < result.Length; i++) result[i] /= Math.Max(width / 1000.0, 0.001);
        return result;
    }

    private static IReadOnlyList<string> FindDifficultSections(IReadOnlyList<MidiNote> notes, double duration)
    {
        var sections = new List<string>();
        const int buckets = 8;
        var width = Math.Max(duration / buckets, 1000);
        for (var i = 0; i < buckets; i++)
        {
            var start = i * width; var end = (i + 1) * width;
            var local = notes.Count(n => n.StartMs >= start && n.StartMs < end);
            var density = local / Math.Max(width / 1000.0, 1);
            if (density >= 7) sections.Add($"{FormatTime(start)}–{FormatTime(end)} · {density:0.0} notes/sec");
        }
        return sections.Take(4).ToArray();
    }

    private static string FormatTime(double ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"m\:ss");

    private static ParsedMidi Parse(byte[] data)
    {
        if (data.Length < 14 || System.Text.Encoding.ASCII.GetString(data, 0, 4) != "MThd") throw new InvalidDataException("Not a standard MIDI file.");
        var headerLength = ReadU32(data, 4); if (headerLength < 6 || data.Length < 8 + headerLength) throw new InvalidDataException("Invalid MIDI header.");
        var format = ReadU16(data, 8); var trackCount = ReadU16(data, 10); var division = ReadU16(data, 12);
        if (format is not (0 or 1)) throw new InvalidDataException("MIDI format 2 is not supported yet.");
        if ((division & 0x8000) != 0 || division == 0) throw new InvalidDataException("SMPTE or invalid MIDI time division is not supported.");
        var all = new List<RawEvent>();
        var trackNames = new Dictionary<int, string>();
        var trackPrograms = new Dictionary<(int Track, int Channel), int>();
        var pos = (int)(8 + headerLength); var tracks = 0;
        for (var t = 0; t < trackCount && pos + 8 <= data.Length; t++)
        {
            if (System.Text.Encoding.ASCII.GetString(data, pos, 4) != "MTrk") break;
            var len = (int)ReadU32(data, pos + 4); var start = pos + 8; var end = start + len; if (end > data.Length) throw new InvalidDataException("A MIDI track is truncated.");
            ParseTrack(data, start, end, t, all, trackNames, trackPrograms); pos = end; tracks++;
        }
        all.Sort((a,b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Order.CompareTo(b.Order));
        var tempo = 500000.0; var bpm = 120.0; var foundTempo = false; long lastTick = 0; double ms = 0; var notes = new List<MidiNote>(); var open = new Dictionary<(int,int,int), Queue<(double,int)>>();
        foreach (var e in all)
        {
            var delta = e.Tick - lastTick; if (delta < 0) continue; ms += delta * tempo / division / 1000.0; lastTick = e.Tick;
            if (e.Kind == 1) { tempo = e.Value; if (e.Tick == 0 && !foundTempo) { bpm = 60000000.0 / tempo; foundTempo = true; } continue; }
            if (e.Kind == 2) continue;
            var key = (e.Track, e.Channel, e.Pitch);
            if (e.Kind == 3) { if (!open.TryGetValue(key, out var q)) open[key] = q = new Queue<(double,int)>(); q.Enqueue((ms, e.Velocity)); }
            else if (e.Kind == 4 && open.TryGetValue(key, out var pending) && pending.Count > 0) { var n = pending.Dequeue(); notes.Add(new MidiNote(n.Item1, Math.Max(ms, n.Item1 + 10), e.Pitch, n.Item2, e.Channel, e.Track)); }
        }
        foreach (var pair in open) while (pair.Value.Count > 0) { var n = pair.Value.Dequeue(); notes.Add(new MidiNote(n.Item1, Math.Max(ms, n.Item1 + 180), pair.Key.Item3, n.Item2, pair.Key.Item2, pair.Key.Item1)); }
        notes = notes.Where(n => n.Pitch is >= 0 and <= 127).ToList();
        var trackInfos = new List<MidiTrackInfo>(tracks);
        for (var track = 0; track < tracks; track++)
        {
            var trackNotes = notes.Where(n => n.TrackIndex == track).ToArray();
            var channel = trackNotes.Select(n => n.Channel).GroupBy(c => c).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            var program = trackPrograms.Where(x => x.Key.Track == track).Select(x => x.Value).FirstOrDefault(-1);
            var instrument = channel == 9 ? "Drums" : InstrumentName(program);
            var name = trackNames.TryGetValue(track, out var trackName) ? trackName : string.Empty;
            trackInfos.Add(new MidiTrackInfo(track, name, instrument, channel, Math.Max(program, 0), trackNotes.Length));
        }
        return new ParsedMidi(notes, Math.Max(ms, notes.Count == 0 ? 0 : notes.Max(n => n.EndMs)), bpm, tracks, trackInfos);
    }

    private static void ParseTrack(byte[] d, int start, int end, int track, List<RawEvent> events, Dictionary<int, string> trackNames, Dictionary<(int Track, int Channel), int> trackPrograms)
    {
        var p = start; long tick = 0; int? running = null; int order = track * 1000000;
        while (p < end)
        {
            var delta = ReadVlq(d, ref p, end); tick += delta; if (p >= end) break; var first = d[p++]; int status;
            if ((first & 0x80) != 0) { status = first; if (status < 0xF8 && status < 0xF0) running = status; }
            else { if (running is null) throw new InvalidDataException("MIDI running status is invalid."); status = running.Value; p--; }
            if (status == 0xFF) { if (p >= end) break; var type = d[p++]; var len = ReadVlq(d, ref p, end); if (p + len > end) throw new InvalidDataException("MIDI meta event is truncated."); if (type == 0x51 && len == 3) events.Add(new RawEvent(tick, order++, 1, track, 0, 0, 0, (d[p] << 16) | (d[p+1] << 8) | d[p+2])); else if (type == 0x03 && len > 0 && !trackNames.ContainsKey(track)) trackNames[track] = System.Text.Encoding.UTF8.GetString(d, p, len).Trim(); p += len; if (type == 0x2F) break; continue; }
            if (status is 0xF0 or 0xF7) { var len = ReadVlq(d, ref p, end); p += len; running = null; continue; }
            var command = status & 0xF0; var channel = status & 0x0F; var a = d[p++]; var b = (command is 0xC0 or 0xD0) ? 0 : d[p++];
            if (command == 0xC0) trackPrograms.TryAdd((track, channel), a);
            if (command == 0x90 && b > 0) events.Add(new RawEvent(tick, order++, 3, track, channel, a, b, 0));
            else if (command == 0x80 || (command == 0x90 && b == 0)) events.Add(new RawEvent(tick, order++, 4, track, channel, a, b, 0));
        }
    }

    private static int ReadVlq(byte[] d, ref int p, int end) { var value = 0; for (var i = 0; i < 4; i++) { if (p >= end) throw new InvalidDataException("Invalid MIDI variable-length value."); var b = d[p++]; value = (value << 7) | (b & 0x7F); if ((b & 0x80) == 0) return value; } throw new InvalidDataException("Invalid MIDI variable-length value."); }
    private static ushort ReadU16(byte[] d, int p) => (ushort)((d[p] << 8) | d[p + 1]);
    private static uint ReadU32(byte[] d, int p) => ((uint)d[p] << 24) | ((uint)d[p+1] << 16) | ((uint)d[p+2] << 8) | d[p+3];
    private sealed record RawEvent(long Tick, int Order, int Kind, int Track, int Channel, int Pitch, int Velocity, int Value);
    private static readonly string[] GeneralMidiInstruments =
    [
        "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano", "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavinet",
        "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
        "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
        "Acoustic Guitar (nylon)", "Acoustic Guitar (steel)", "Electric Guitar (jazz)", "Electric Guitar (clean)", "Electric Guitar (muted)", "Overdriven Guitar", "Distortion Guitar", "Guitar Harmonics",
        "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
        "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
        "String Ensemble 1", "String Ensemble 2", "SynthStrings 1", "SynthStrings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
        "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "SynthBrass 1", "SynthBrass 2",
        "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
        "Piccolo", "Flute", "Recorder", "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
        "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)", "Lead 4 (chiff)", "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass + lead)",
        "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)", "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)",
        "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)", "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)",
        "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bagpipe", "Fiddle", "Shanai",
        "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
        "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot"
    ];

    private static string InstrumentName(int program) => program >= 0 && program < GeneralMidiInstruments.Length ? GeneralMidiInstruments[program] : "Unknown Instrument";

    private sealed record ParsedMidi(List<MidiNote> Notes, double DurationMs, double SourceBpm, int TrackCount, IReadOnlyList<MidiTrackInfo> Tracks);
}
