using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace VirtualPianoBot.Core;

/// <summary>
/// Applies user-selected appearance presets to the application's dynamic resources.
/// The selected values are stored in AppSettings, outside the installation folder.
/// </summary>
public static class StudioAppearanceService
{
    private sealed record ThemePalette(
        string Background,
        string Sidebar,
        string Surface,
        string Card,
        string Input,
        string Border,
        string PrimaryText,
        string SecondaryText,
        string MutedText,
        string Hover,
        string Selected,
        string Cyan,
        string Purple);

    private static readonly IReadOnlyDictionary<string, ThemePalette> Themes =
        new Dictionary<string, ThemePalette>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dark"] = new(
                "#080B10", "#0B0F15", "#10161F", "#151C26", "#0D131B", "#263342",
                "#F4F7FA", "#B0BBC7", "#718091", "#19232E", "#1C2A35", "#5DE4FF", "#A989FF"),
            ["Midnight"] = new(
                "#060914", "#090E1B", "#0E1626", "#131E31", "#0B1220", "#25344D",
                "#F2F5FF", "#B0BED7", "#7184A5", "#17243A", "#1D2D47", "#70DDF7", "#A7A0FF"),
            ["Graphite"] = new(
                "#0D0E11", "#121419", "#191C22", "#20242C", "#13161C", "#343A45",
                "#F1F2F5", "#BBC1CC", "#838C9A", "#252A32", "#2B313B", "#77D9E8", "#B4A0F2"),
            ["Light"] = new(
                "#F4F6FA", "#E9EDF3", "#FFFFFF", "#F8FAFD", "#EEF2F7", "#D6DDE8",
                "#18202C", "#4B5668", "#69768A", "#E0E7F0", "#D6E0EE", "#007E9C", "#7651B8")
        };

    private sealed record AccentPalette(string Hex, string Bright, string Soft, string Dim, string LightHex, string LightBright, string LightSoft, string LightDim);

    private static readonly IReadOnlyDictionary<string, AccentPalette> Accents =
        new Dictionary<string, AccentPalette>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lime"] = new("#B8FF65", "#D1FF91", "#55783A", "#1B3217", "#4E7A1E", "#60952A", "#D4E7BE", "#EEF7E4"),
            ["Cyan"] = new("#5DE4FF", "#99F0FF", "#277889", "#11313A", "#007C96", "#0098B5", "#BBDDE5", "#E0F6FA"),
            ["Violet"] = new("#B69CFF", "#D5C6FF", "#6955A0", "#2A2146", "#7250B5", "#8665CA", "#D8CCF0", "#F0EAFE"),
            ["Rose"] = new("#FF83B5", "#FFB4D2", "#9B4B70", "#401D2E", "#B83D74", "#CF5288", "#E9C0D2", "#FCE5EF"),
            ["Amber"] = new("#FFC46B", "#FFDA9A", "#976D2F", "#3B2A12", "#93600A", "#AE7718", "#E8D2AB", "#FFF2DB")
        };

    public static IReadOnlyList<string> ThemeNames { get; } = ["Dark", "Midnight", "Graphite", "Light"];
    public static IReadOnlyList<string> AccentNames { get; } = ["Lime", "Cyan", "Violet", "Rose", "Amber"];

    public static string NormalizeTheme(string? theme) =>
        Themes.Keys.FirstOrDefault(candidate => string.Equals(candidate, theme, StringComparison.OrdinalIgnoreCase)) ?? "Dark";

    public static string NormalizeAccentName(string? name) =>
        Accents.Keys.FirstOrDefault(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) ?? "Lime";

    public static string AccentHexForName(string? name, string? themeName = null)
    {
        var accent = Accents[NormalizeAccentName(name)];
        return string.Equals(NormalizeTheme(themeName), "Light", StringComparison.OrdinalIgnoreCase)
            ? accent.LightHex
            : accent.Hex;
    }

    public static string StoredAccentHexForName(string? name) => Accents[NormalizeAccentName(name)].Hex;

    public static string AccentNameFromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "Lime";
        foreach (var pair in Accents)
        {
            if (string.Equals(pair.Value.Hex, hex, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        }

        return "Lime";
    }

    public static void Apply(string? themeName, string? accentHex)
    {
        var application = Application.Current;
        if (application is null) return;

        var resources = application.Resources;
        void SetBrush(string key, string color) => resources[key] = new SolidColorBrush(Color.Parse(color));
        void SetGradient(string key, string start, string middle, string end) => resources[key] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse(start), 0),
                new GradientStop(Color.Parse(middle), 0.55),
                new GradientStop(Color.Parse(end), 1)
            }
        };

        var normalizedTheme = NormalizeTheme(themeName);
        var theme = Themes[normalizedTheme];
        var accent = GetAccentFromHex(accentHex);
        var lightMode = normalizedTheme == "Light";
        application.RequestedThemeVariant = lightMode ? ThemeVariant.Light : ThemeVariant.Dark;

        SetBrush("AppBackgroundBrush", theme.Background);
        SetBrush("SidebarBrush", theme.Sidebar);
        SetBrush("SurfaceBrush", theme.Surface);
        SetBrush("CardBrush", theme.Card);
        SetBrush("InputBrush", theme.Input);
        SetBrush("BorderBrush", theme.Border);
        SetBrush("PrimaryTextBrush", theme.PrimaryText);
        SetBrush("SecondaryTextBrush", theme.SecondaryText);
        SetBrush("MutedBrush", theme.MutedText);
        SetBrush("HoverBrush", theme.Hover);
        SetBrush("SelectedBrush", theme.Selected);
        SetBrush("CyanBrush", theme.Cyan);
        SetBrush("PurpleBrush", theme.Purple);
        var activeAccent = lightMode ? accent.LightHex : accent.Hex;
        SetBrush("AccentBrush", activeAccent);
        SetBrush("AccentBrightBrush", lightMode ? accent.LightBright : accent.Bright);
        SetBrush("AccentSoftBrush", lightMode ? accent.LightSoft : accent.Soft);
        SetBrush("AccentDimBrush", lightMode ? accent.LightDim : accent.Dim);
        SetGradient("AccentGradientBrush", activeAccent, theme.Cyan, accent.Bright);
        SetGradient("HeroBrush", theme.Background, theme.Surface, lightMode ? "#F8FAFD" : theme.Card);
    }

    private static AccentPalette GetAccentFromHex(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            foreach (var accent in Accents.Values)
            {
                if (string.Equals(accent.Hex, hex, StringComparison.OrdinalIgnoreCase)) return accent;
            }
        }

        return Accents["Lime"];
    }

}
