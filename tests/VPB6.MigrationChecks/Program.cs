using VirtualPianoBot.Studio;

var failures = new List<string>();
void Check(bool condition, string text)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {text}");
    if (!condition) failures.Add(text);
}

var mapper = new KeyboardMappingService();
var map = mapper.BuildMap();
Check(map.Count == 61, "Default mapping covers C2-C7.");
Check(map.First().Pitch == 36 && map.Last().Pitch == 96, "Mapping pitch bounds are C2-C7.");
Check(map.Select(x => x.DisplayKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 61, "Default mapping has one deterministic binding per pitch.");

var legacy = Enumerable.Repeat("A", 61).ToList();
var legacyMap = mapper.BuildMap(legacy);
Check(legacyMap.All(x => x.Key == "a" && x.Shift), "Legacy uppercase keymap entries are interpreted as shifted bindings.");

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? "All VPB 6 migration checks passed." : $"{failures.Count} migration check(s) failed.");
return failures.Count == 0 ? 0 : 1;
