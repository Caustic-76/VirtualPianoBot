using VirtualPianoBot.Core;
using VirtualPianoBot.Analysis;

namespace VirtualPianoBot.Studio;

public sealed record PianoKeyBinding(int Pitch, string Key, bool Shift, string DisplayKey)
{
    public string Note => AnalysisDisplay.NoteName(Pitch);
}

public sealed record PlaybackAction(double TimeMs, int Pitch, string Key, bool Shift, bool IsNoteOn, bool IsPlayable)
{
    public string Display => Shift ? $"SHIFT + {Key}" : Key;
}

public sealed record PlaybackPlan(
    IReadOnlyList<PlaybackAction> Actions,
    int PlayableNotes,
    int SkippedNotes,
    int ConflictCount,
    int UniqueKeys,
    string RangeMode)
{
    public int TotalNotes => PlayableNotes + SkippedNotes;
}

public sealed class KeyboardMappingService
{
    public const int LowPitch = 36;
    public const int HighPitch = 96;

    // 61 positions, matching C2 through C7. The mapping uses the familiar
    // two-level QWERTY piano layout and remains deterministic for action plans.
    // Exact v4.1.0 default map. Shifted characters share their physical key
    // with the unshifted character, e.g. C#2 = ! = SHIFT+1.
    private static readonly string[] DefaultKeymap =
    [
        "1", "!", "2", "@", "3", "4", "$", "5", "%", "6", "^", "7",
        "8", "*", "9", "(", "0", "q", "Q", "w", "W", "e", "E", "r",
        "t", "T", "y", "Y", "u", "i", "I", "o", "O", "p", "P", "a",
        "s", "S", "d", "D", "f", "g", "G", "h", "H", "j", "J", "k",
        "l", "L", "z", "Z", "x", "c", "C", "v", "V", "b", "B", "n", "m"
    ];

    private static readonly Dictionary<char, char> ShiftedSymbols = new()
    {
        ['!'] = '1', ['@'] = '2', ['$'] = '4', ['%'] = '5', ['^'] = '6',
        ['&'] = '7', ['*'] = '8', ['('] = '9', [')'] = '0'
    };

    public IReadOnlyList<PianoKeyBinding> BuildMap(IReadOnlyList<string>? legacyKeymap = null)
    {
        var bindings = new List<PianoKeyBinding>(HighPitch - LowPitch + 1);
        var legacy = legacyKeymap?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];

        for (var pitch = LowPitch; pitch <= HighPitch; pitch++)
        {
            var index = pitch - LowPitch;
            var raw = index < legacy.Count ? legacy[index] : DefaultKeymap[index];
            var parsedBinding = ParseBinding(raw) ?? ParseBinding(DefaultKeymap[index]);
            if (parsedBinding is null)
                continue;

            var binding = parsedBinding.Value;
            bindings.Add(new PianoKeyBinding(
                pitch,
                binding.Key,
                binding.Shift,
                binding.Shift ? $"SHIFT + {binding.Key.ToUpperInvariant()}" : binding.Key.ToUpperInvariant()));
        }

        return bindings;
    }

    public IReadOnlySet<int>? ResolveTrackSelection(IReadOnlyList<MidiNote> notes, string performanceMode, IReadOnlySet<int>? selectedTracks = null)
    {
        if (selectedTracks is not null)
            return selectedTracks;
        if (string.Equals(performanceMode, "Custom Selection", StringComparison.OrdinalIgnoreCase))
            return new HashSet<int>();
        if (string.Equals(performanceMode, "All Tracks", StringComparison.OrdinalIgnoreCase) || string.Equals(performanceMode, "All Instruments", StringComparison.OrdinalIgnoreCase) || notes.Count == 0)
            return null;

        var groups = notes.GroupBy(n => n.TrackIndex).ToList();
        if (groups.Count <= 1)
            return groups.Count == 0 ? new HashSet<int>() : new HashSet<int> { groups[0].Key };

        // v4.1's presets are role selectors, not alternate pitch mappings.
        // Use deterministic musical heuristics here because VPB6's parser does not
        // yet expose General MIDI program names. Drum-channel tracks are excluded.
        var scored = groups.Select(g =>
        {
            var list = g.ToList();
            var nonDrum = list.Count(n => n.Channel != 9);
            var range = list.Count == 0 ? 0 : list.Max(n => n.Pitch) - list.Min(n => n.Pitch);
            var avg = list.Count == 0 ? 0 : list.Average(n => n.Pitch);
            var density = list.Count == 0 ? 0 : list.Count / Math.Max(1.0, (list.Max(n => n.EndMs) - list.Min(n => n.StartMs)) / 1000.0);
            var polyphony = list.GroupBy(n => Math.Round(n.StartMs, 1)).Select(x => x.Count()).DefaultIfEmpty(1).Average();
            var piano = nonDrum * 1.0 + Math.Clamp(1.0 - Math.Abs(avg - 60) / 36.0, 0, 1) * 80 + Math.Clamp(1.0 - Math.Abs(range - 48) / 60.0, 0, 1) * 40 - Math.Max(0, polyphony - 5) * 4;
            var melody = nonDrum * 1.0 + Math.Clamp(1.0 - Math.Abs(polyphony - 1.4) / 3.0, 0, 1) * 90 + Math.Clamp(1.0 - Math.Abs(range - 30) / 50.0, 0, 1) * 35 + Math.Clamp(density / 8.0, 0, 1) * 20;
            return (Track: g.Key, Piano: piano, Melody: melody);
        }).ToList();

        var best = string.Equals(performanceMode, "Best Melody", StringComparison.OrdinalIgnoreCase)
            ? scored.OrderByDescending(x => x.Melody).First().Track
            : scored.OrderByDescending(x => x.Piano).First().Track;

        return new HashSet<int> { best };
    }

    public PlaybackPlan BuildPlan(MidiAnalysisResult result, int transpose, string rangeMode, IReadOnlyList<string>? legacyKeymap = null, string performanceMode = "All Tracks", IReadOnlySet<int>? selectedTracks = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var map = BuildMap(legacyKeymap).ToDictionary(x => x.Pitch);
        var selected = ResolveTrackSelection(result.Notes, performanceMode, selectedTracks);
        var eligible = selected is null ? result.Notes : result.Notes.Where(n => selected.Contains(n.TrackIndex)).ToList();
        var mapped = new List<(MidiNote Note, PianoKeyBinding Binding)>();
        var skipped = 0;

        var smartFit = string.Equals(rangeMode, "Smart Fit", StringComparison.OrdinalIgnoreCase);

        foreach (var note in eligible)
        {
            var pitch = note.Pitch + transpose;

            if (smartFit)
            {
                // Smart Fit is deliberately octave-only. Notes already inside the
                // C2-C7 keyboard stay at their exact pitch. Only outliers are
                // folded into the playable range by whole octaves.
                while (pitch < LowPitch) pitch += 12;
                while (pitch > HighPitch) pitch -= 12;
            }

            if (!map.TryGetValue(pitch, out var binding))
            {
                skipped++;
                continue;
            }
            mapped.Add((note with { Pitch = pitch }, binding));
        }

        // The original VPB playback engine did not emit one independent key-up and
        // key-down for every MIDI note. It collapsed simultaneous note events by
        // physical key, then resolved overlaps into down/up/retrigger operations.
        // That distinction matters enormously for chords and repeated notes because
        // Windows keyboard input is stateful. Preserve that behaviour here.
        var raw = new SortedDictionary<double, EventBucket>();
        foreach (var (note, binding) in mapped)
        {
            Add(raw, Math.Round(note.StartMs, 3), binding, isOn: true);
            Add(raw, Math.Round(Math.Max(note.EndMs, note.StartMs + 10.0), 3), binding, isOn: false);
        }

        var active = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var actions = new List<PlaybackAction>(raw.Count);
        var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conflictKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (timeMs, bucket) in raw)
        {
            var keys = bucket.Offs.Keys.Concat(bucket.Ons.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            foreach (var key in keys)
            {
                var before = active.GetValueOrDefault(key);
                var offs = bucket.Offs.GetValueOrDefault(key);
                var ons = bucket.Ons.GetValueOrDefault(key);
                var afterOff = Math.Max(0, before - offs);

                // A note ending at exactly the same timestamp as its successor is
                // both an up and a down. Do not use mutually exclusive isUp/isDown
                // branches here: the old branch emitted only the up and silently
                // swallowed the new note-on. Dense repeated chords commonly contain
                // this exact legato boundary.
                if (ons > 0)
                {
                    var binding = bucket.BindingFor(key);
                    if (before > 0)
                    {
                        // The physical key is already held. Always release it before
                        // pressing again so Windows and the target piano receive a
                        // distinct retrigger, including when all old notes end here.
                        actions.Add(new PlaybackAction(timeMs, binding.Pitch, binding.Key, binding.Shift, false, true));
                    }
                    actions.Add(new PlaybackAction(timeMs, binding.Pitch, binding.Key, binding.Shift, true, true));
                }
                else if (before > 0 && afterOff == 0)
                {
                    var binding = bucket.BindingFor(key);
                    actions.Add(new PlaybackAction(timeMs, binding.Pitch, binding.Key, binding.Shift, false, true));
                }

                if (ons > 0) uniqueKeys.Add(key);
                if (before > 0 && ons > 0 && offs > 0) conflictKeys.Add(key);
                var after = afterOff + ons;
                if (after > 0) active[key] = after;
                else active.Remove(key);
            }
        }

        // Stable ordering is important when several operations share a timestamp:
        // releases/retriggers must happen before new key-downs.
        actions.Sort((a, b) =>
        {
            var time = a.TimeMs.CompareTo(b.TimeMs);
            if (time != 0) return time;
            if (a.IsNoteOn == b.IsNoteOn) return string.CompareOrdinal(a.Key, b.Key);
            return a.IsNoteOn ? 1 : -1;
        });

        var playable = mapped.Count;
        return new PlaybackPlan(actions, playable, skipped, conflictKeys.Count, uniqueKeys.Count, rangeMode);
    }

    private sealed class EventBucket
    {
        public Dictionary<string, int> Ons { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Offs { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PianoKeyBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);

        public void Add(PianoKeyBinding binding, bool isOn)
        {
            _bindings[binding.DisplayKey] = binding;
            var target = isOn ? Ons : Offs;
            target[binding.DisplayKey] = target.GetValueOrDefault(binding.DisplayKey) + 1;
        }

        public PianoKeyBinding BindingFor(string key) => _bindings[key];
    }

    private static void Add(SortedDictionary<double, EventBucket> raw, double timeMs, PianoKeyBinding binding, bool isOn)
    {
        if (!raw.TryGetValue(timeMs, out var bucket))
        {
            bucket = new EventBucket();
            raw[timeMs] = bucket;
        }
        bucket.Add(binding, isOn);
    }

    private static (string Key, bool Shift)? ParseBinding(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var token = value.Trim();
        if (token.Length != 1)
        {
            if (token.StartsWith("SHIFT+", StringComparison.OrdinalIgnoreCase) && token.Length > 6)
                return (token[6..].Trim().ToLowerInvariant(), true);
            return null;
        }

        var c = token[0];
        if (ShiftedSymbols.TryGetValue(c, out var baseKey))
            return (baseKey.ToString().ToLowerInvariant(), true);
        if (char.IsUpper(c))
            return (char.ToLowerInvariant(c).ToString(), true);
        if (char.IsLetterOrDigit(c) || c == ';')
            return (char.ToLowerInvariant(c).ToString(), false);
        return null;
    }
}
