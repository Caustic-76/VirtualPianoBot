namespace VirtualPianoBot.Core;

public sealed class MidiImportResult
{
    public int Copied { get; internal set; }
    public int Skipped { get; internal set; }
    public List<string> Failures { get; } = [];
    public bool HasFailures => Failures.Count > 0;

    public override string ToString() =>
        $"Copied {Copied}, skipped {Skipped}, failed {Failures.Count}.";
}
