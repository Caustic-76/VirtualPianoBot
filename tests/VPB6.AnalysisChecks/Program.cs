using VirtualPianoBot.Analysis;

var root = Path.Combine(Path.GetTempPath(), "VPB6AnalysisChecks");
Directory.CreateDirectory(root);
var path = Path.Combine(root, "smoke.mid");
File.WriteAllBytes(path, BuildMidi());
var result = new MidiAnalysisService().Analyze(path);
if (result.NoteCount != 4) throw new Exception($"Expected 4 notes, got {result.NoteCount}.");
if (result.LowestPitch != 60 || result.HighestPitch != 64) throw new Exception("Pitch range regression.");
if (result.TrackCount != 1) throw new Exception("Track count regression.");
if (result.SourceBpm != 120) throw new Exception("Tempo regression.");
Console.WriteLine("VPB6 Phase 4 analysis smoke test passed.");

static byte[] BuildMidi()
{
    // Format 0, 1 track, 480 PPQN, 120 BPM, four quarter-note events.
    var track = new List<byte>();
    track.AddRange(new byte[] { 0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 });
    AddNote(track, 60, 0x00); AddNote(track, 62, 0x00); AddNote(track, 64, 0x00); AddNote(track, 60, 0x00);
    track.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });
    var data = new List<byte>();
    data.AddRange(System.Text.Encoding.ASCII.GetBytes("MThd")); data.AddRange(U32(6)); data.AddRange(U16(0)); data.AddRange(U16(1)); data.AddRange(U16(480));
    data.AddRange(System.Text.Encoding.ASCII.GetBytes("MTrk")); data.AddRange(U32((uint)track.Count)); data.AddRange(track); return data.ToArray();
    static void AddNote(List<byte> t, int pitch, byte startDelta) { t.Add(startDelta); t.Add(0x90); t.Add((byte)pitch); t.Add(100); t.Add(0x83); t.Add(0x60); t.Add(0x80); t.Add((byte)pitch); t.Add(0); }
    static byte[] U16(ushort v) => new[] { (byte)(v >> 8), (byte)v };
    static byte[] U32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
}
