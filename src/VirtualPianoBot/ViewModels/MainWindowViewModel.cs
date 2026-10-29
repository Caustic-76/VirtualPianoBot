using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Threading;
using VirtualPianoBot.Core;
using VirtualPianoBot.Core.Commands;
using VirtualPianoBot.Analysis;
using VirtualPianoBot.Studio;

namespace VirtualPianoBot.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly AppSettingsService _settingsService;
    private readonly MidiLibraryService _libraryService;
    private readonly LegacyMidiMigrationService _migrationService;
    private readonly AppSettings _settings;
    private string _currentPage = "Dashboard";
    private string _searchQuery = string.Empty;
    private string _libraryFilterMode = "All MIDI";
    private string _librarySortMode = "Recently Modified";
    private Func<Task<string?>>? _pickMidiFolderAsync;
    private string _statusMessage = "Preparing the permanent MIDI library…";
    private string _lastRefreshLabel = "Not scanned yet";
    private bool _isRefreshing = true;
    private bool _isImportingMidiFolder;
    private MidiLibraryItem? _selectedLibraryItem;
    private MidiAnalysisResult? _analysisResult;
    private MidiAnalysisResult? _studioResult;
    private bool _isAnalyzing;
    private double _studioSpeed = 1.0;
    private int _studioPlaybackGeneration;
    private double _studioPositionMs;
    private bool _studioLoop;
    private bool _studioMetronome;
    private bool _studioKeyHighlight = true;
    private double _studioVolume = 0.85;
    private int _studioTranspose;
    private string _studioRangeMode = "Smart Fit";
    private string _studioPerformanceMode = "All Instruments";
    private bool _inputArmed;
    private bool _studioRangePickerOpen;
    private bool _studioPerformancePickerOpen;
    private bool _studioHudVisible = true;
    private string _themeOption = "Dark";
    private string _accentColorOption = "Lime";
    private bool _reduceMotion;
    private string _studioVisualizerLookAheadOption = "6 seconds";
    private string _studioVisualizerColorMode = "Register";
    private string _studioVisualizerDetailMode = "Auto";
    private bool _studioVisualizerShowBeatGrid = true;
    private bool _studioVisualizerShowNoteLabels;
    private readonly ObservableCollection<StudioTrackOption> _studioTrackOptions = [];
    private readonly object _studioHudGate = new();
    private readonly Dictionary<int, int> _studioHudActivePitchCounts = [];
    private readonly Queue<long> _studioHudRecentNoteOns = new();
    private long _studioHudNoteOnsProcessed;
    private double _studioHudPeakNoteRate;
    private double _studioHudNoteRateSnapshot;
    private double _studioHudPeakNoteRateSnapshot;
    private long _studioHudNoteOnsSnapshot;
    private string _studioHudActiveChordSnapshot = "0 · —";

    private readonly MidiAnalysisService _analysisService;
    private readonly StudioPlaybackEngine _studioEngine;
    private readonly KeyboardMappingService _keyboardMappingService;
    private readonly NativeKeyboardInputService _nativeKeyboardInput;
    private PlaybackPlan? _studioPlan;

    public MainWindowViewModel(
        AppPaths paths,
        AppLogger logger,
        AppSettingsService settingsService,
        MidiLibraryService libraryService,
        LegacyMidiMigrationService migrationService,
        AppSettings settings)
    {
        _paths = paths;
        _logger = logger;
        _settingsService = settingsService;
        _libraryService = libraryService;
        _migrationService = migrationService;
        _settings = settings;
        _themeOption = StudioAppearanceService.NormalizeTheme(settings.Theme);
        _accentColorOption = StudioAppearanceService.AccentNameFromHex(settings.AccentColor);
        _reduceMotion = settings.ReduceMotion;
        _settings.Theme = _themeOption;
        _settings.AccentColor = StudioAppearanceService.StoredAccentHexForName(_accentColorOption);
        StudioAppearanceService.Apply(_themeOption, _settings.AccentColor);
        _studioVisualizerLookAheadOption = settings.StudioVisualizerLookAheadSeconds switch
        {
            4 => "4 seconds",
            8 => "8 seconds",
            10 => "10 seconds",
            _ => "6 seconds"
        };
        _studioVisualizerColorMode = settings.StudioVisualizerColorMode switch
        {
            "Track" => "Track",
            "Velocity" => "Velocity",
            _ => "Register"
        };
        _studioVisualizerDetailMode = settings.StudioVisualizerDetailMode switch
        {
            "Detail" => "Detail",
            "Performance" => "Performance",
            _ => "Auto"
        };
        _studioVisualizerShowBeatGrid = settings.StudioVisualizerShowBeatGrid;
        _studioVisualizerShowNoteLabels = settings.StudioVisualizerShowNoteLabels;
        _analysisService = new MidiAnalysisService();
        _studioEngine = new StudioPlaybackEngine(_logger);
        _keyboardMappingService = new KeyboardMappingService();
        _nativeKeyboardInput = new NativeKeyboardInputService();
        _studioEngine.Changed += OnStudioChanged;
        _studioEngine.ActionTriggered += OnStudioActionTriggered;

        _currentPage = settings.LastWorkspace;
        NavigateCommand = new RelayCommand(Navigate);
        OpenMidiFolderCommand = new RelayCommand(_ => OpenFolder(_paths.MidiDirectory));
        OpenDataFolderCommand = new RelayCommand(_ => OpenFolder(_paths.DataDirectory));
        OpenLogsFolderCommand = new RelayCommand(_ => OpenFolder(_paths.LogsDirectory));
        RefreshLibraryCommand = new AsyncRelayCommand(RefreshLibraryAsync, () => !IsRefreshing && !_isImportingMidiFolder);
        ImportMidiFolderCommand = new AsyncRelayCommand(ImportMidiFolderAsync, () => !IsRefreshing && !_isImportingMidiFolder);
        ToggleFavoriteMidiCommand = new RelayCommand(ToggleFavoriteMidi, parameter => parameter is MidiLibraryItem || SelectedLibraryItem is not null);
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        ResetAppearanceCommand = new RelayCommand(_ => ResetAppearance());
        SelectMidiCommand = new RelayCommand(SelectMidi);
        OpenSelectedMidiFolderCommand = new RelayCommand(_ => OpenSelectedMidiFolder(), _ => SelectedLibraryItem is not null);
        AnalyzeSelectedCommand = new AsyncRelayCommand(AnalyzeSelectedAsync, () => HasSelectedMidi && !IsAnalyzing);
        ApplyRecommendationCommand = new RelayCommand(_ => ApplyRecommendation(), _ => AnalysisResult is not null);
        StudioPlayPauseCommand = new RelayCommand(_ => ToggleStudioPlayback(), _ => HasStudioSource);
        StudioStopCommand = new RelayCommand(_ => StopStudio(), _ => HasStudioSource);
        StudioRestartCommand = new RelayCommand(_ => RestartStudio(), _ => HasStudioSource);
        SetStudioSpeedCommand = new RelayCommand(SetStudioSpeed, _ => HasStudioSource);
        StudioSeekCommand = new RelayCommand(SeekStudio);
        StudioPanicCommand = new RelayCommand(_ => PanicStudio());
        StudioInputArmCommand = new RelayCommand(_ => ToggleInputArmed());
        ToggleStudioHudCommand = new RelayCommand(_ => StudioHudVisible = !StudioHudVisible);
        OpenStudioRangePickerCommand = new RelayCommand(_ => StudioRangePickerOpen = true);
        CloseStudioRangePickerCommand = new RelayCommand(_ => StudioRangePickerOpen = false);
        OpenStudioPerformancePickerCommand = new RelayCommand(_ => StudioPerformancePickerOpen = true);
        CloseStudioPerformancePickerCommand = new RelayCommand(_ => StudioPerformancePickerOpen = false);
        SetStudioRangeModeCommand = new RelayCommand(SetStudioRangeMode);
        SetStudioPerformancePresetCommand = new RelayCommand(SetStudioPerformancePreset);
        SelectAllStudioTracksCommand = new RelayCommand(_ => SetAllStudioTracks(true));
        ClearStudioTracksCommand = new RelayCommand(_ => SetAllStudioTracks(false));

        _ = RefreshLibraryAsync();
    }

    public ObservableCollection<MidiLibraryItem> LibraryItems { get; } = [];
    public ObservableCollection<MidiLibraryItem> FilteredLibraryItems { get; } = [];
    public ObservableCollection<MidiLibraryItem> RecentLibraryItems { get; } = [];
    public IReadOnlyList<string> LibraryFilterModes { get; } = ["All MIDI", "Favorites", "Modified in 30 days"];
    public IReadOnlyList<string> LibrarySortModes { get; } = ["Recently Modified", "Title A-Z", "Largest First", "Folder"];

    public RelayCommand NavigateCommand { get; }
    public RelayCommand OpenMidiFolderCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenLogsFolderCommand { get; }
    public AsyncRelayCommand RefreshLibraryCommand { get; }
    public AsyncRelayCommand ImportMidiFolderCommand { get; }
    public RelayCommand ToggleFavoriteMidiCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ResetAppearanceCommand { get; }
    public RelayCommand SelectMidiCommand { get; }
    public RelayCommand OpenSelectedMidiFolderCommand { get; }
    public AsyncRelayCommand AnalyzeSelectedCommand { get; }
    public RelayCommand ApplyRecommendationCommand { get; }
    public RelayCommand StudioPlayPauseCommand { get; }
    public RelayCommand StudioStopCommand { get; }
    public RelayCommand StudioRestartCommand { get; }
    public RelayCommand SetStudioSpeedCommand { get; }
    public RelayCommand StudioSeekCommand { get; }
    public RelayCommand StudioPanicCommand { get; }
    public RelayCommand StudioInputArmCommand { get; }
    public RelayCommand ToggleStudioHudCommand { get; }
    public RelayCommand OpenStudioRangePickerCommand { get; }
    public RelayCommand CloseStudioRangePickerCommand { get; }
    public RelayCommand OpenStudioPerformancePickerCommand { get; }
    public RelayCommand CloseStudioPerformancePickerCommand { get; }
    public RelayCommand SetStudioRangeModeCommand { get; }
    public RelayCommand SetStudioPerformancePresetCommand { get; }
    public RelayCommand SelectAllStudioTracksCommand { get; }
    public RelayCommand ClearStudioTracksCommand { get; }

    public IReadOnlyList<string> ThemeOptions => StudioAppearanceService.ThemeNames;
    public IReadOnlyList<string> AccentColorOptions => StudioAppearanceService.AccentNames;

    public string ThemeOption
    {
        get => _themeOption;
        set
        {
            var theme = StudioAppearanceService.NormalizeTheme(value);
            if (!SetProperty(ref _themeOption, theme)) return;
            _settings.Theme = theme;
            OnPropertyChanged(nameof(StudioThemeMode));
            OnPropertyChanged(nameof(AccentColorHex));
            StudioAppearanceService.Apply(_themeOption, _settings.AccentColor);
            SaveAppearanceSettings();
        }
    }

    public string AccentColorOption
    {
        get => _accentColorOption;
        set
        {
            var accent = StudioAppearanceService.NormalizeAccentName(value);
            if (!SetProperty(ref _accentColorOption, accent)) return;
            _settings.AccentColor = StudioAppearanceService.StoredAccentHexForName(accent);
            OnPropertyChanged(nameof(AccentColorHex));
            StudioAppearanceService.Apply(_themeOption, _settings.AccentColor);
            SaveAppearanceSettings();
        }
    }

    public string AccentColorHex => StudioAppearanceService.AccentHexForName(AccentColorOption, ThemeOption);
    public string StudioThemeMode => ThemeOption;

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (!SetProperty(ref _reduceMotion, value)) return;
            _settings.ReduceMotion = value;
            SaveAppearanceSettings();
        }
    }

    public string CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(CurrentPageTitle));
            OnPropertyChanged(nameof(CurrentPageSubtitle));
            OnPropertyChanged(nameof(IsDashboard));
            OnPropertyChanged(nameof(IsLibrary));
            OnPropertyChanged(nameof(IsAnalysis));
            OnPropertyChanged(nameof(IsStudio));
            OnPropertyChanged(nameof(IsSettings));
            OnPropertyChanged(nameof(IsDashboardSelected));
            OnPropertyChanged(nameof(IsLibrarySelected));
            OnPropertyChanged(nameof(IsAnalysisSelected));
            OnPropertyChanged(nameof(IsStudioSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
        }
    }

    public string CurrentPageTitle => CurrentPage switch
    {
        "Dashboard" => "Dashboard",
        "Library" => "MIDI Library",
        "Analysis" => "Analysis Studio",
        "Studio" => "Performance Studio",
        "Settings" => "Settings",
        _ => "Dashboard"
    };

    public string CurrentPageSubtitle => CurrentPage switch
    {
        "Dashboard" => "Your music workspace, ready when you are.",
        "Library" => "Browse the collection that stays with you between versions.",
        "Analysis" => "Measure playability, density, range, chords and keyboard pressure before you press play.",
        "Studio" => "Watch tempo, note flow, active voices and input state live while the notes fall.",
        "Settings" => "Your settings and library live outside the installation folder.",
        _ => "Your music workspace, ready when you are."
    };

    public bool IsDashboard => CurrentPage == "Dashboard";
    public bool IsLibrary => CurrentPage == "Library";
    public bool IsAnalysis => CurrentPage == "Analysis";
    public bool IsStudio => CurrentPage == "Studio";
    public bool IsSettings => CurrentPage == "Settings";
    public bool IsDashboardSelected => IsDashboard;
    public bool IsLibrarySelected => IsLibrary;
    public bool IsAnalysisSelected => IsAnalysis;
    public bool IsStudioSelected => IsStudio;
    public bool IsSettingsSelected => IsSettings;

    public MidiLibraryItem? SelectedLibraryItem
    {
        get => _selectedLibraryItem;
        set
        {
            if (!SetProperty(ref _selectedLibraryItem, value)) return;
            OnPropertyChanged(nameof(HasSelectedMidi));
            OnPropertyChanged(nameof(SelectedMidiTitle));
            OnPropertyChanged(nameof(SelectedMidiPath));
            OnPropertyChanged(nameof(SelectedMidiSize));
            OnPropertyChanged(nameof(SelectedMidiModified));
            OnPropertyChanged(nameof(SelectedFavoriteButtonLabel));
            OnPropertyChanged(nameof(SelectedMidiDetailsLabel));
            ToggleFavoriteMidiCommand.RaiseCanExecuteChanged();
            OpenSelectedMidiFolderCommand.RaiseCanExecuteChanged();
            AnalyzeSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedMidi => SelectedLibraryItem is not null;
    public string SelectedMidiTitle => SelectedLibraryItem?.Title ?? "Nothing selected";
    public string SelectedMidiPath => SelectedLibraryItem?.RelativePath ?? "Choose a MIDI from the library to inspect it.";
    public string SelectedMidiSize => SelectedLibraryItem?.FileSizeLabel ?? "";
    public string SelectedMidiModified => SelectedLibraryItem?.ModifiedLabel ?? "";

    public MidiAnalysisResult? AnalysisResult
    {
        get => _analysisResult;
        private set
        {
            if (!SetProperty(ref _analysisResult, value)) return;
            RaiseAnalysisProperties();
            ApplyRecommendationCommand.RaiseCanExecuteChanged();
            StudioPlayPauseCommand.RaiseCanExecuteChanged();
            StudioStopCommand.RaiseCanExecuteChanged();
            if (value is not null)
            {
                _studioResult = value;
                _studioEngine.Load(value);
                RebuildStudioPlan();
                StudioPositionMs = 0;
                if (StudioTranspose == 0) StudioTranspose = value.RecommendedTranspose;
            }
            OnPropertyChanged(nameof(StudioResult));
            OnPropertyChanged(nameof(HasStudioSource));
            OnPropertyChanged(nameof(StudioDurationMs));
        }
    }

    public bool HasAnalysis => AnalysisResult is not null;
    /// <summary>
    /// Performance Studio has its own lightweight MIDI source. A MIDI can be played
    /// immediately after selection without running the optional analysis pipeline.
    /// </summary>
    public MidiAnalysisResult? StudioResult => _studioResult;
    public bool HasStudioSource => StudioResult is not null;
    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (!SetProperty(ref _isAnalyzing, value)) return;
            OnPropertyChanged(nameof(AnalyzeButtonLabel));
            AnalyzeSelectedCommand.RaiseCanExecuteChanged();
        }
    }
    public string AnalyzeButtonLabel => IsAnalyzing ? "ANALYSING…" : "ANALYSE SELECTED MIDI";
    public string AnalysisDifficulty => AnalysisResult?.Difficulty ?? "WAITING";
    public string AnalysisDifficultyDetail => AnalysisResult?.DifficultyDetail ?? "Select a MIDI and run analysis.";
    public string AnalysisScore => AnalysisResult is null ? "--" : $"{AnalysisResult.MelodyCandidateScore:0}";
    public string AnalysisConfidence => AnalysisResult is null ? "--" : $"{AnalysisResult.Confidence:0}%";
    public string AnalysisNotes => AnalysisResult?.NoteCount.ToString("N0") ?? "--";
    public string AnalysisDuration => AnalysisResult is null ? "--" : AnalysisDisplay.FormatTime(AnalysisResult.DurationMs);
    public string AnalysisRange => AnalysisResult is null ? "--" : $"{AnalysisDisplay.NoteName(AnalysisResult.LowestPitch)} – {AnalysisDisplay.NoteName(AnalysisResult.HighestPitch)}";
    public string AnalysisDensity => AnalysisResult is null ? "--" : $"{AnalysisResult.NotesPerSecond:0.0} / sec";
    public string AnalysisPeakDensity => AnalysisResult is null ? "--" : $"{AnalysisResult.PeakNotesPerSecond:0.0} / sec";
    public string AnalysisChords => AnalysisResult is null ? "--" : $"{AnalysisResult.ChordDensity * 100:0}%";
    public string AnalysisOverlap => AnalysisResult?.MaxOverlap.ToString() ?? "--";
    public string AnalysisCoverage => AnalysisResult is null ? "--" : $"{AnalysisResult.PlayableCoverage * 100:0}%";
    public string AnalysisBpm => AnalysisResult is null ? "--" : $"{AnalysisResult.SourceBpm:0} → {AnalysisResult.RecommendedBpm:0} BPM";
    public string AnalysisTranspose => AnalysisResult is null ? "--" : AnalysisResult.RecommendedTranspose == 0 ? "No transpose" : $"{(AnalysisResult.RecommendedTranspose > 0 ? "+" : "")}{AnalysisResult.RecommendedTranspose} semitones";
    public string AnalysisRangeMode => AnalysisResult?.RecommendedRangeMode ?? "--";
    public string AnalysisWarnings => AnalysisResult is null ? "No warnings yet." : AnalysisResult.Warnings.Count == 0 ? "No immediate warnings. This file sits comfortably inside the current keyboard envelope." : string.Join("  •  ", AnalysisResult.Warnings);
    public string AnalysisDifficultSections => AnalysisResult is null ? "Run analysis to locate high-pressure passages." : AnalysisResult.DifficultSections.Count == 0 ? "No obvious density spikes detected." : string.Join("  •  ", AnalysisResult.DifficultSections);

    public double StudioPositionMs { get => _studioPositionMs; set { var v = Math.Clamp(value, 0, StudioDurationMs); if (!SetProperty(ref _studioPositionMs, v)) return; if (Math.Abs(_studioEngine.PositionMs - v) > 25) _studioEngine.Seek(v); OnPropertyChanged(nameof(StudioPositionLabel)); OnPropertyChanged(nameof(StudioProgress)); } }
    public double StudioDurationMs => StudioResult?.DurationMs ?? 0;
    public double StudioProgress => StudioDurationMs <= 0 ? 0 : Math.Clamp(StudioPositionMs / StudioDurationMs * 100, 0, 100);
    public string StudioPositionLabel => $"{AnalysisDisplay.FormatTime(StudioPositionMs)} / {AnalysisDisplay.FormatTime(StudioDurationMs)}";
    public string StudioElapsedLabel => AnalysisDisplay.FormatTime(StudioPositionMs);
    public string StudioRemainingLabel => $"-{AnalysisDisplay.FormatTime(Math.Max(0, StudioDurationMs - StudioPositionMs))}";
    public bool IsStudioPlaying => _studioEngine.IsPlaying;
    public bool IsStudioCountingDown => _studioEngine.IsCountingDown;
    public string StudioCountdownLabel => _studioEngine.IsCountingDown ? $"{_studioEngine.CountdownRemaining}" : "";
    public string StudioPlayPauseLabel => IsStudioCountingDown ? "COUNTDOWN" : IsStudioPlaying ? "PAUSE" : "PLAY";
    public double StudioSpeed { get => _studioSpeed; set { var v = Math.Clamp(value, 0.1, 3.0); if (!SetProperty(ref _studioSpeed, v)) return; _studioEngine.Speed = v; OnPropertyChanged(nameof(StudioSpeedLabel)); RebuildStudioPlan(); } }
    public string StudioSpeedLabel => $"{StudioSpeed * 100:0}%";
    public bool StudioLoop { get => _studioLoop; set { if (!SetProperty(ref _studioLoop, value)) return; _studioEngine.Loop = value; } }
    public bool StudioMetronome { get => _studioMetronome; set => SetProperty(ref _studioMetronome, value); }
    public bool StudioRangePickerOpen { get => _studioRangePickerOpen; set => SetProperty(ref _studioRangePickerOpen, value); }
    public bool StudioPerformancePickerOpen { get => _studioPerformancePickerOpen; set => SetProperty(ref _studioPerformancePickerOpen, value); }
    public ObservableCollection<StudioTrackOption> StudioTrackOptions => _studioTrackOptions;
    public IReadOnlyList<int> StudioSelectedTrackIndices => _studioTrackOptions.Where(x => x.IsSelected).Select(x => x.Info.TrackIndex).ToArray();
    public IReadOnlyList<int> StudioVisibleTrackIndices
    {
        get
        {
            if (StudioResult is null) return StudioSelectedTrackIndices;
            if (StudioPerformanceMode == "Custom Selection") return StudioSelectedTrackIndices;
            var mode = StudioPerformanceMode == "All Instruments" ? "All Tracks" : StudioPerformanceMode;
            var resolved = _keyboardMappingService.ResolveTrackSelection(StudioResult.Notes, mode);
            return resolved?.ToArray() ?? StudioResult.Notes.Select(n => n.TrackIndex).Distinct().OrderBy(x => x).ToArray();
        }
    }
    public bool StudioVisualizerFilterTracksEnabled => StudioPerformanceMode != "All Instruments";
    public string StudioTrackSelectionLabel => _studioPerformanceMode == "Custom Selection" ? $"{StudioSelectedTrackIndices.Count} selected" : _studioPerformanceMode;
    public bool StudioKeyHighlight { get => _studioKeyHighlight; set => SetProperty(ref _studioKeyHighlight, value); }
    public double StudioVolume { get => _studioVolume; set => SetProperty(ref _studioVolume, Math.Clamp(value, 0, 1)); }
    public int StudioTranspose { get => _studioTranspose; set { if (!SetProperty(ref _studioTranspose, Math.Clamp(value, -24, 24))) return; OnPropertyChanged(nameof(StudioTransposeLabel)); RebuildStudioPlan(); } }
    public IReadOnlyList<string> StudioRangeModes { get; } = ["Smart Fit", "Exact Pitch"];
    public IReadOnlyList<string> StudioPerformanceModes { get; } = ["All Instruments", "Best Piano", "Best Melody", "Custom Selection"];
    public string StudioPerformanceMode
    {
        get => _studioPerformanceMode;
        set
        {
            var mode = value switch
            {
                "Best Piano" => "Best Piano",
                "Best Melody" => "Best Melody",
                "Custom Selection" => "Custom Selection",
                _ => "All Instruments"
            };
            if (!SetProperty(ref _studioPerformanceMode, mode)) return;
            RebuildStudioPlan();
            OnPropertyChanged(nameof(StudioTrackSelectionLabel));
            OnPropertyChanged(nameof(StudioSelectedTrackIndices));
            OnPropertyChanged(nameof(StudioVisualizerFilterTracksEnabled));
            StatusMessage = $"Performance mode set to {mode}.";
        }
    }
    public string StudioRangeMode
    {
        get => _studioRangeMode;
        set
        {
            var mode = string.Equals(value, "Exact Pitch", StringComparison.OrdinalIgnoreCase) ? "Exact Pitch" : "Smart Fit";
            if (!SetProperty(ref _studioRangeMode, mode)) return;
            RebuildStudioPlan();
        }
    }
    public string StudioTransposeLabel => StudioTranspose == 0 ? "0" : $"{StudioTranspose:+#;-#;0}";
    public string StudioTrackLabel => SelectedLibraryItem?.Title ?? "No MIDI selected";
    public string StudioStatusLabel => IsStudioCountingDown ? $"COUNTDOWN · {_studioEngine.CountdownRemaining}" : HasStudioSource ? (IsStudioPlaying ? (InputArmed ? "PLAYING · NATIVE INPUT ACTIVE" : "PLAYING · VISUAL ONLY") : _studioEngine.IsPaused ? (InputArmed ? "PAUSED · NATIVE INPUT ARMED" : "PAUSED · VISUAL ONLY") : (InputArmed ? "READY · NATIVE INPUT ARMED" : "READY · VISUAL ONLY")) : "SELECT A MIDI TO LOAD IT INTO THE STUDIO";
    public bool InputArmed => _inputArmed;
    public string InputArmLabel => InputArmed ? "DISARM INPUT" : "ARM INPUT";
    public string InputSafetyLabel => InputArmed ? "ARMED · THE FOREGROUND WINDOWS APP WILL RECEIVE PIANO KEYS" : "DISARMED · PLAYBACK IS VISUAL ONLY";
    public string StudioPlayableNotesLabel => _studioPlan is null ? "--" : $"{_studioPlan.PlayableNotes:N0} / {_studioPlan.TotalNotes:N0}";
    public string StudioSkippedNotesLabel => _studioPlan?.SkippedNotes.ToString("N0") ?? "--";
    public string StudioMappedKeysLabel => _studioPlan?.UniqueKeys.ToString("N0") ?? "--";
    public string StudioConflictLabel => _studioPlan is null ? "--" : _studioPlan.ConflictCount == 0 ? "NONE" : _studioPlan.ConflictCount.ToString("N0");
    public string StudioMappingModeLabel => StudioRangeMode;

    public bool StudioHudVisible
    {
        get => _studioHudVisible;
        set
        {
            if (!SetProperty(ref _studioHudVisible, value)) return;
            OnPropertyChanged(nameof(StudioHudToggleLabel));
        }
    }
    public string StudioHudToggleLabel => StudioHudVisible ? "HUD: ON" : "HUD: OFF";

    public IReadOnlyList<string> StudioVisualizerLookAheadOptions { get; } = ["4 seconds", "6 seconds", "8 seconds", "10 seconds"];
    public IReadOnlyList<string> StudioVisualizerColorModes { get; } = ["Register", "Track", "Velocity"];
    public IReadOnlyList<string> StudioVisualizerDetailModes { get; } = ["Auto", "Detail", "Performance"];

    public string StudioVisualizerLookAheadOption
    {
        get => _studioVisualizerLookAheadOption;
        set
        {
            var option = value is "4 seconds" or "6 seconds" or "8 seconds" or "10 seconds" ? value : "6 seconds";
            if (!SetProperty(ref _studioVisualizerLookAheadOption, option)) return;
            _settings.StudioVisualizerLookAheadSeconds = StudioVisualizerLookAheadSeconds;
            OnPropertyChanged(nameof(StudioVisualizerLookAheadSeconds));
            SaveVisualizerSettings();
        }
    }

    public double StudioVisualizerLookAheadSeconds => _studioVisualizerLookAheadOption switch
    {
        "4 seconds" => 4,
        "8 seconds" => 8,
        "10 seconds" => 10,
        _ => 6
    };

    public string StudioVisualizerColorMode
    {
        get => _studioVisualizerColorMode;
        set
        {
            var mode = value is "Track" or "Velocity" ? value : "Register";
            if (!SetProperty(ref _studioVisualizerColorMode, mode)) return;
            _settings.StudioVisualizerColorMode = mode;
            SaveVisualizerSettings();
        }
    }

    public string StudioVisualizerDetailMode
    {
        get => _studioVisualizerDetailMode;
        set
        {
            var mode = value is "Detail" or "Performance" ? value : "Auto";
            if (!SetProperty(ref _studioVisualizerDetailMode, mode)) return;
            _settings.StudioVisualizerDetailMode = mode;
            SaveVisualizerSettings();
        }
    }

    public bool StudioVisualizerShowBeatGrid
    {
        get => _studioVisualizerShowBeatGrid;
        set
        {
            if (!SetProperty(ref _studioVisualizerShowBeatGrid, value)) return;
            _settings.StudioVisualizerShowBeatGrid = value;
            SaveVisualizerSettings();
        }
    }

    public bool StudioVisualizerShowNoteLabels
    {
        get => _studioVisualizerShowNoteLabels;
        set
        {
            if (!SetProperty(ref _studioVisualizerShowNoteLabels, value)) return;
            _settings.StudioVisualizerShowNoteLabels = value;
            SaveVisualizerSettings();
        }
    }
    public string StudioHudModeLabel => $"{StudioTrackSelectionLabel}  ·  {StudioRangeMode}";
    public string StudioHudStateLabel => IsStudioCountingDown ? "COUNTDOWN" : IsStudioPlaying ? "PLAYING" : _studioEngine.IsPaused ? "PAUSED" : HasStudioSource ? "READY" : "NO MIDI";
    public string StudioHudTempoLabel => StudioResult is { SourceBpm: > 0 } result ? $"{result.SourceBpm * StudioSpeed:0.#} BPM" : "-- BPM";
    public string StudioHudNotesPerSecondLabel => $"{_studioHudNoteRateSnapshot:0.0}";
    public string StudioHudPeakRateLabel => $"{_studioHudPeakNoteRateSnapshot:0}";
    public string StudioHudActiveNotesLabel => _studioHudActiveChordSnapshot;
    public string StudioHudNoteOnsLabel => _studioHudNoteOnsSnapshot.ToString("N0");
    public string StudioHudTracksLabel => $"{_studioTrackOptions.Count(x => x.IsSelected):N0}";
    public string StudioHudInputLabel => InputArmed ? "ARMED" : "VISUAL ONLY";
    public string StudioHudProgressLabel => $"{StudioProgress:0.0}%";

    private void RaiseAnalysisProperties()
    {
        foreach (var name in new[] { nameof(HasAnalysis), nameof(AnalysisDifficulty), nameof(AnalysisDifficultyDetail), nameof(AnalysisScore), nameof(AnalysisConfidence), nameof(AnalysisNotes), nameof(AnalysisDuration), nameof(AnalysisRange), nameof(AnalysisDensity), nameof(AnalysisPeakDensity), nameof(AnalysisChords), nameof(AnalysisOverlap), nameof(AnalysisCoverage), nameof(AnalysisBpm), nameof(AnalysisTranspose), nameof(AnalysisRangeMode), nameof(AnalysisWarnings), nameof(AnalysisDifficultSections), nameof(StudioDurationMs), nameof(StudioTrackLabel), nameof(StudioResult), nameof(HasStudioSource), nameof(StudioRangeMode), nameof(StudioRangeModes), nameof(StudioPerformanceMode), nameof(StudioPerformanceModes), nameof(StudioSelectedTrackIndices), nameof(StudioVisibleTrackIndices), nameof(StudioTrackSelectionLabel) }) OnPropertyChanged(name);
    }

    public string LibraryFilterMode
    {
        get => _libraryFilterMode;
        set
        {
            if (!SetProperty(ref _libraryFilterMode, value ?? "All MIDI")) return;
            ApplyLibraryFilter();
        }
    }

    public string LibrarySortMode
    {
        get => _librarySortMode;
        set
        {
            if (!SetProperty(ref _librarySortMode, value ?? "Recently Modified")) return;
            ApplyLibraryFilter();
        }
    }

    public int FavoriteMidiCount => LibraryItems.Count(item => item.IsFavorite);
    public int RecentMidiCount => LibraryItems.Count(item => item.ModifiedAt >= DateTime.Now.AddDays(-30));
    public string FavoriteMidiCountLabel => $"{FavoriteMidiCount:N0} FAVORITES";
    public string FilteredLibraryCountLabel => $"SHOWING {FilteredLibraryItems.Count:N0} OF {LibraryItems.Count:N0}";
    public string RecentMidiCountLabel => $"{RecentMidiCount:N0} RECENT";
    public string SelectedFavoriteButtonLabel => SelectedLibraryItem?.IsFavorite == true ? "★ REMOVE FAVORITE" : "☆ ADD FAVORITE";
    public string SelectedMidiDetailsLabel => SelectedLibraryItem is null
        ? "Select a song to see its location and details."
        : $"{SelectedLibraryItem.FileSizeLabel}  ·  Modified {SelectedLibraryItem.ModifiedLabel}";

    public void ConfigureMidiFolderPicker(Func<Task<string?>> picker) => _pickMidiFolderAsync = picker;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value ?? string.Empty)) return;
            ApplyLibraryFilter();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LastRefreshLabel
    {
        get => _lastRefreshLabel;
        private set => SetProperty(ref _lastRefreshLabel, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (!SetProperty(ref _isRefreshing, value)) return;
            OnPropertyChanged(nameof(RefreshButtonLabel));
            RefreshLibraryCommand.RaiseCanExecuteChanged();
            ImportMidiFolderCommand.RaiseCanExecuteChanged();
        }
    }

    public string RefreshButtonLabel => IsRefreshing ? "SCANNING…" : "RESCAN LIBRARY";
    public int LibraryFileCount => LibraryItems.Count;
    public string LibraryFileCountLabel => LibraryItems.Count == 1 ? "1 MIDI FILE" : $"{LibraryItems.Count:N0} MIDI FILES";
    public string LibraryFolder => _paths.MidiDirectory;
    public string DataFolder => _paths.DataDirectory;
    public string SettingsFile => _paths.SettingsFile;
    public string LogsFolder => _paths.LogsDirectory;
    public string TotalLibrarySizeLabel => FormatBytes(LibraryItems.Sum(item => item.FileSizeBytes));
    public bool HasLibraryItems => LibraryItems.Count > 0;
    public bool HasNoLibraryFiles => !IsRefreshing && LibraryItems.Count == 0;
    public bool HasFilteredItems => FilteredLibraryItems.Count > 0;
    public bool IsEmptySearch => !IsRefreshing && FilteredLibraryItems.Count == 0;

    private void Navigate(object? parameter)
    {
        var requested = parameter?.ToString();
        if (requested is not ("Dashboard" or "Library" or "Analysis" or "Studio" or "Settings")) return;
        CurrentPage = requested;
        _settings.LastWorkspace = requested;
        if (!_settingsService.Save(_settings)) StatusMessage = "Workspace changed, but settings could not be saved. See logs.";
    }

    private async Task RefreshLibraryAsync()
    {
        if (IsRefreshing && LibraryItems.Count > 0) return;
        IsRefreshing = true;
        StatusMessage = "Scanning MIDI files. Existing songs will not be changed.";
        try
        {
            var result = await Task.Run(() =>
            {
                // Migration is intentionally idempotent. Re-run the discovery on every
                // refresh so a legacy path that was unavailable on an earlier launch
                // can still be recovered later. Copy-only collision protection makes
                // repeated scans safe.
                var migration = _migrationService.MigrateKnownLegacyFolders(AppContext.BaseDirectory);
                var items = _libraryService.Scan();
                return (migration, items);
            });

            var selectedRelativePath = SelectedLibraryItem?.RelativePath;
            LibraryItems.Clear();
            var favoritePaths = new HashSet<string>(_settings.FavoriteMidiPaths ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (var item in result.items)
            {
                item.IsFavorite = favoritePaths.Contains(item.RelativePath);
                LibraryItems.Add(item);
            }
            SelectedLibraryItem = selectedRelativePath is null
                ? null
                : LibraryItems.FirstOrDefault(item => string.Equals(item.RelativePath, selectedRelativePath, StringComparison.OrdinalIgnoreCase));
            ApplyLibraryFilter();
            RebuildRecentItems();
            LastRefreshLabel = $"Scanned {DateTime.Now:HH:mm}";
            OnPropertyChanged(nameof(LibraryFileCount));
            OnPropertyChanged(nameof(LibraryFileCountLabel));
            OnPropertyChanged(nameof(TotalLibrarySizeLabel));
            OnPropertyChanged(nameof(FavoriteMidiCount));
            OnPropertyChanged(nameof(FavoriteMidiCountLabel));
            OnPropertyChanged(nameof(RecentMidiCount));
            OnPropertyChanged(nameof(RecentMidiCountLabel));
            OnPropertyChanged(nameof(HasLibraryItems));
            OnPropertyChanged(nameof(HasNoLibraryFiles));
            OnPropertyChanged(nameof(HasFilteredItems));
            OnPropertyChanged(nameof(IsEmptySearch));

            var migration = result.migration;
            if (migration.Copied > 0)
            {
                StatusMessage = $"Library ready. Recovered {migration.Copied} MIDI file(s) from an older VPB folder. Original files were kept.";
            }
            else if (migration.HasFailures)
            {
                StatusMessage = $"Library ready with {LibraryItems.Count:N0} file(s). Some legacy files could not be copied. See logs.";
            }
            else
            {
                StatusMessage = $"Permanent library ready: {LibraryItems.Count:N0} MIDI file(s). Updates do not replace this folder.";
            }

            _logger.Info($"MIDI library scan complete: {LibraryItems.Count} file(s).");
        }
        catch (Exception exception)
        {
            _logger.Error("The MIDI library could not be scanned.", exception);
            StatusMessage = $"The library could not be scanned: {exception.Message}. Your source files were not deleted.";
        }
        finally
        {
            IsRefreshing = false;
            OnPropertyChanged(nameof(RefreshButtonLabel));
            OnPropertyChanged(nameof(IsEmptySearch));
            OnPropertyChanged(nameof(HasNoLibraryFiles));
        }
    }

    private void ApplyLibraryFilter()
    {
        IEnumerable<MidiLibraryItem> matches = LibraryItems;
        var now = DateTime.Now;
        matches = LibraryFilterMode switch
        {
            "Favorites" => matches.Where(item => item.IsFavorite),
            "Modified in 30 days" => matches.Where(item => item.ModifiedAt >= now.AddDays(-30)),
            _ => matches
        };

        var query = SearchQuery.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            matches = matches.Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        matches = LibrarySortMode switch
        {
            "Title A-Z" => matches.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
            "Largest First" => matches.OrderByDescending(item => item.FileSizeBytes)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            "Folder" => matches.OrderBy(item => Path.GetDirectoryName(item.RelativePath) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            _ => matches.OrderByDescending(item => item.ModifiedAt)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
        };

        FilteredLibraryItems.Clear();
        foreach (var item in matches) FilteredLibraryItems.Add(item);
        OnPropertyChanged(nameof(HasFilteredItems));
        OnPropertyChanged(nameof(IsEmptySearch));
        OnPropertyChanged(nameof(FilteredLibraryCountLabel));
    }

    private async Task ImportMidiFolderAsync()
    {
        if (_pickMidiFolderAsync is null)
        {
            StatusMessage = "Folder picker is not available in this window.";
            return;
        }

        var startedImport = false;
        try
        {
            _isImportingMidiFolder = true;
            startedImport = true;
            RefreshLibraryCommand.RaiseCanExecuteChanged();
            ImportMidiFolderCommand.RaiseCanExecuteChanged();

            var source = await _pickMidiFolderAsync();
            if (string.IsNullOrWhiteSpace(source)) return;

            StatusMessage = "Importing MIDI files by copying only. Existing songs will never be overwritten.";
            var result = await Task.Run(() => _libraryService.ImportFolder(source));
            await RefreshLibraryAsync();
            StatusMessage = $"Import complete: copied {result.Copied:N0}, skipped {result.Skipped:N0}, failed {result.Failures.Count:N0}. Existing files were not overwritten and source files were kept.";
            _logger.Info($"User MIDI folder import complete. {result}");
            foreach (var failure in result.Failures.Take(20)) _logger.Warning($"MIDI import: {failure}");
        }
        catch (Exception exception)
        {
            _logger.Error("MIDI folder import failed. Source files were left untouched.", exception);
            StatusMessage = $"MIDI import failed: {exception.Message}. Source files were left untouched.";
        }
        finally
        {
            if (startedImport)
            {
                _isImportingMidiFolder = false;
                RefreshLibraryCommand.RaiseCanExecuteChanged();
                ImportMidiFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void ToggleFavoriteMidi(object? parameter)
    {
        var item = parameter as MidiLibraryItem ?? SelectedLibraryItem;
        if (item is null) return;

        item.IsFavorite = !item.IsFavorite;
        var favoritePaths = _settings.FavoriteMidiPaths ??= [];
        favoritePaths.RemoveAll(path => string.Equals(path, item.RelativePath, StringComparison.OrdinalIgnoreCase));
        if (item.IsFavorite) favoritePaths.Add(item.RelativePath);

        if (_settingsService.Save(_settings))
        {
            StatusMessage = item.IsFavorite
                ? $"Added {item.Title} to Favorites."
                : $"Removed {item.Title} from Favorites.";
        }
        else
        {
            StatusMessage = "Favorite changed for this session, but settings could not be saved. See logs.";
        }

        ApplyLibraryFilter();
        OnPropertyChanged(nameof(FavoriteMidiCount));
        OnPropertyChanged(nameof(FavoriteMidiCountLabel));
        OnPropertyChanged(nameof(RecentMidiCount));
        OnPropertyChanged(nameof(RecentMidiCountLabel));
        OnPropertyChanged(nameof(SelectedFavoriteButtonLabel));
    }

    private void RebuildRecentItems()
    {
        RecentLibraryItems.Clear();
        foreach (var item in LibraryItems.OrderByDescending(item => item.ModifiedAt).Take(5))
            RecentLibraryItems.Add(item);
    }

    private void SelectMidi(object? parameter)
    {
        if (parameter is MidiLibraryItem item)
        {
            SelectedLibraryItem = item;
            LoadStudioSource(item);
            StatusMessage = $"Loaded {item.Title} into Performance Studio. Analysis is optional.";
        }
    }

    private async Task AnalyzeSelectedAsync()
    {
        if (SelectedLibraryItem is null) return;
        IsAnalyzing = true;
        StatusMessage = $"Analysing {SelectedLibraryItem.Title}…";
        try
        {
            var path = SelectedLibraryItem.FullPath;
            AnalysisResult = await Task.Run(() => _analysisService.Analyze(path));
            StatusMessage = $"Analysis complete: {AnalysisResult.Difficulty} difficulty, {AnalysisResult.NoteCount:N0} notes, {AnalysisResult.NotesPerSecond:0.0} notes/sec.";
            _logger.Info($"Analysis complete for '{SelectedLibraryItem.FileName}': {AnalysisResult.NoteCount} notes, {AnalysisResult.Difficulty} difficulty.");
        }
        catch (Exception exception)
        {
            AnalysisResult = null;
            _logger.Error("MIDI analysis failed.", exception);
            StatusMessage = $"Analysis failed: {exception.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private static MidiAnalysisResult BuildStudioPlaybackResult(MidiPlaybackData data)
    {
        var notes = data.Notes;
        var duration = Math.Max(data.DurationMs, 1);
        var playable = notes.Count(n => n.Pitch is >= KeyboardMappingService.LowPitch and <= KeyboardMappingService.HighPitch);
        var outOfRange = notes.Count - playable;
        var coverage = notes.Count == 0 ? 0 : playable / (double)notes.Count;
        return new MidiAnalysisResult(
            data.FileName, notes.Count, duration, data.SourceBpm, data.TrackCount,
            data.LowestPitch, data.HighestPitch, notes.Count / (duration / 1000.0),
            0, 0, 0, 0, coverage, outOfRange, 0, 0,
            "NOT ANALYSED", "Studio playback source loaded directly from MIDI note events.",
            $"{data.SourceBpm:0} BPM / 100%", data.SourceBpm, 0,
            outOfRange == 0 ? "Exact pitch" : "Direct pitch / out-of-range",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<double>(), notes);
    }

    private void LoadStudioSource(MidiLibraryItem item)
    {
        _studioPlaybackGeneration++;
        _studioEngine.Stop();
        _nativeKeyboardInput.ReleaseAll();
        ResetStudioHudTelemetry();
        try
        {
            var data = _analysisService.LoadPlaybackData(item.FullPath);
            SetStudioTrackOptions(data.Tracks);
            _studioResult = BuildStudioPlaybackResult(data);
            _studioEngine.Load(_studioResult);
            RebuildStudioPlan();
            StudioPositionMs = 0;
            OnPropertyChanged(nameof(StudioResult));
            OnPropertyChanged(nameof(HasStudioSource));
            OnPropertyChanged(nameof(StudioDurationMs));
            OnPropertyChanged(nameof(StudioPositionLabel));
            StudioPlayPauseCommand.RaiseCanExecuteChanged();
            StudioStopCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            _studioResult = null;
            _studioEngine.Load(null);
            RebuildStudioPlan();
            OnPropertyChanged(nameof(StudioResult));
            OnPropertyChanged(nameof(HasStudioSource));
            OnPropertyChanged(nameof(StudioDurationMs));
            StudioPlayPauseCommand.RaiseCanExecuteChanged();
            StudioStopCommand.RaiseCanExecuteChanged();
            _logger.Error("MIDI playback source load failed.", exception);
            StatusMessage = $"Could not load MIDI into Studio: {exception.Message}";
        }
    }

    private void SetStudioTrackOptions(IReadOnlyList<MidiTrackInfo> tracks)
    {
        foreach (var option in _studioTrackOptions)
            option.PropertyChanged -= OnStudioTrackOptionChanged;
        _studioTrackOptions.Clear();
        foreach (var track in tracks)
        {
            var option = new StudioTrackOption(track, true);
            option.PropertyChanged += OnStudioTrackOptionChanged;
            _studioTrackOptions.Add(option);
        }
        _studioPerformanceMode = "All Instruments";
        OnPropertyChanged(nameof(StudioPerformanceMode));
        OnPropertyChanged(nameof(StudioVisualizerFilterTracksEnabled));
        OnPropertyChanged(nameof(StudioSelectedTrackIndices));
        OnPropertyChanged(nameof(StudioTrackSelectionLabel));
    }

    private void OnStudioTrackOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StudioTrackOption.IsSelected)) return;
        if (StudioPerformanceMode != "Custom Selection") return;
        RebuildStudioPlan();
        OnPropertyChanged(nameof(StudioSelectedTrackIndices));
        OnPropertyChanged(nameof(StudioVisibleTrackIndices));
        OnPropertyChanged(nameof(StudioTrackSelectionLabel));
    }

    private void SetAllStudioTracks(bool selected)
    {
        foreach (var option in _studioTrackOptions) option.IsSelected = selected;
        if (StudioPerformanceMode != "Custom Selection") StudioPerformanceMode = "Custom Selection";
        RebuildStudioPlan();
        OnPropertyChanged(nameof(StudioSelectedTrackIndices));
        OnPropertyChanged(nameof(StudioVisibleTrackIndices));
        OnPropertyChanged(nameof(StudioTrackSelectionLabel));
    }

    private void SetStudioRangeMode(object? parameter)
    {
        if (parameter is not string mode) return;
        StudioRangeMode = mode;
        StudioRangePickerOpen = false;
        StatusMessage = $"Range mode set to {StudioRangeMode}.";
    }

    private void SetStudioPerformancePreset(object? parameter)
    {
        if (parameter is not string mode) return;
        StudioPerformanceMode = mode;

        if (mode == "All Instruments")
        {
            foreach (var option in _studioTrackOptions) option.IsSelected = true;
        }
        else if (mode is "Best Piano" or "Best Melody" && StudioResult is not null)
        {
            var resolved = _keyboardMappingService.ResolveTrackSelection(StudioResult.Notes, mode);
            foreach (var option in _studioTrackOptions) option.IsSelected = resolved?.Contains(option.Info.TrackIndex) ?? false;
        }

        OnPropertyChanged(nameof(StudioSelectedTrackIndices));
        OnPropertyChanged(nameof(StudioVisibleTrackIndices));
        OnPropertyChanged(nameof(StudioTrackSelectionLabel));
        RebuildStudioPlan();
        StudioPerformancePickerOpen = false;
    }

    private void RebuildStudioPlan()
    {
        if (StudioResult is null)
        {
            _studioPlan = null;
            _studioEngine.SetPlan(null);
        }
        else
        {
            var selected = StudioPerformanceMode == "Custom Selection" ? StudioSelectedTrackIndices.ToHashSet() : null;
            _studioPlan = _keyboardMappingService.BuildPlan(StudioResult, StudioTranspose, StudioRangeMode, _settings.LegacyV5Keymap, StudioPerformanceMode == "All Instruments" ? "All Tracks" : StudioPerformanceMode, selected);
            _studioEngine.SetPlan(_studioPlan);
        }
        foreach (var name in new[] { nameof(StudioPlayableNotesLabel), nameof(StudioSkippedNotesLabel), nameof(StudioMappedKeysLabel), nameof(StudioConflictLabel), nameof(StudioMappingModeLabel) }) OnPropertyChanged(name);
    }

    private void ApplyRecommendation()
    {
        if (AnalysisResult is null) return;
        StudioSpeed = Math.Clamp(AnalysisResult.RecommendedBpm / Math.Max(AnalysisResult.SourceBpm, 1), 0.1, 3.0);
        StudioTranspose = AnalysisResult.RecommendedTranspose;
        StudioRangeMode = AnalysisResult.RecommendedRangeMode.StartsWith("Smart Fit", StringComparison.OrdinalIgnoreCase) ? "Smart Fit" : "Exact Pitch";
        RebuildStudioPlan();
        StatusMessage = $"Recommendation staged: {AnalysisResult.RecommendedRangeMode}, {AnalysisResult.RecommendedBpm:0} BPM, transpose {AnalysisResult.RecommendedTranspose:+#;-#;0}. Playback controls will consume these values in the Studio phase.";
    }

    private void ToggleStudioPlayback()
    {
        if (!HasStudioSource || _studioEngine.IsCountingDown) return;
        if (_studioEngine.IsPlaying)
        {
            _studioEngine.Pause();
            _nativeKeyboardInput.ReleaseAll();
            ClearStudioHudActiveNotes(clearRecentRate: true);
            StatusMessage = "Studio paused. Press PLAY to resume from this position.";
            return;
        }

        _studioEngine.Play();
        _logger.Info("Studio playback requested from MainWindowViewModel.");
        StatusMessage = "Studio playback starting…";
    }

    private void RestartStudio()
    {
        if (!HasStudioSource) return;
        _studioPlaybackGeneration++;
        _studioEngine.Stop();
        _nativeKeyboardInput.ReleaseAll();
        ResetStudioHudTelemetry();
        _studioEngine.Seek(0);
        _studioEngine.Play();
        StatusMessage = "Studio restarted from the beginning.";
    }

    private void SetStudioSpeed(object? parameter)
    {
        if (parameter is null) return;
        if (double.TryParse(parameter.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed))
            StudioSpeed = speed;
    }

    private void StopStudio()
    {
        _studioPlaybackGeneration++;
        _studioEngine.Stop();
        _nativeKeyboardInput.ReleaseAll();
        ResetStudioHudTelemetry();
        OnPropertyChanged(nameof(StudioPlayPauseLabel));
        OnPropertyChanged(nameof(StudioStatusLabel));
        StudioPlayPauseCommand.RaiseCanExecuteChanged();
        StatusMessage = "Studio transport stopped.";
    }

    private void ToggleInputArmed()
    {
        if (_inputArmed)
        {
            _nativeKeyboardInput.Disarm();
            _inputArmed = false;
            StatusMessage = "Native keyboard input disarmed. Studio remains visual-only.";
        }
        else
        {
            _nativeKeyboardInput.Arm();
            _inputArmed = true;
            StatusMessage = "Native keyboard input armed. Playback will send mapped keys to the current foreground Windows app.";
        }
        OnPropertyChanged(nameof(InputArmed));
        OnPropertyChanged(nameof(InputArmLabel));
        OnPropertyChanged(nameof(InputSafetyLabel));
        OnPropertyChanged(nameof(StudioStatusLabel));
        OnPropertyChanged(nameof(StudioHudInputLabel));
    }

    private void SeekStudio(object? parameter)
    {
        _nativeKeyboardInput.ReleaseAll();
        ClearStudioHudActiveNotes(clearRecentRate: true);
        if (parameter is double d) _studioEngine.Seek(d);
        else if (double.TryParse(parameter?.ToString(), out var parsed)) _studioEngine.Seek(parsed);
    }

    private void PanicStudio()
    {
        _studioEngine.Stop();
        _nativeKeyboardInput.ReleaseAll();
        ResetStudioHudTelemetry();
        StatusMessage = "PANIC STOP: transport halted and all native keyboard keys released.";
    }

    private void OnStudioActionTriggered(PlaybackAction action)
    {
        RecordStudioAction(action);
        try
        {
            if (_inputArmed) _nativeKeyboardInput.HandleAction(action);
        }
        catch (Exception exception)
        {
            _logger.Error($"Native keyboard action failed: pitch={action.Pitch}, key={action.Key}, shift={action.Shift}, noteOn={action.IsNoteOn}, time={action.TimeMs:0.###}ms.", exception);
            _nativeKeyboardInput.ReleaseAll();
        }
    }

    public void Dispose()
    {
        _studioEngine.ActionTriggered -= OnStudioActionTriggered;
        _studioEngine.Changed -= OnStudioChanged;
        _studioEngine.Dispose();
        _nativeKeyboardInput.Dispose();
    }

    private void OnStudioChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnStudioChanged(sender, e));
            return;
        }

        var position = _studioEngine.PositionMs;
        if (Math.Abs(_studioPositionMs - position) > 5)
        {
            _studioPositionMs = position;
            OnPropertyChanged(nameof(StudioPositionMs));
            OnPropertyChanged(nameof(StudioPositionLabel));
            OnPropertyChanged(nameof(StudioElapsedLabel));
            OnPropertyChanged(nameof(StudioRemainingLabel));
            OnPropertyChanged(nameof(StudioProgress));
        }
        OnPropertyChanged(nameof(IsStudioPlaying));
        OnPropertyChanged(nameof(IsStudioCountingDown));
        OnPropertyChanged(nameof(StudioCountdownLabel));
        OnPropertyChanged(nameof(StudioPlayPauseLabel));
        OnPropertyChanged(nameof(StudioStatusLabel));
        RefreshStudioHudTelemetry();
        RaiseStudioHudStateProperties();
    }

    private void RecordStudioAction(PlaybackAction action)
    {
        if (!action.IsPlayable) return;

        lock (_studioHudGate)
        {
            if (action.IsNoteOn)
            {
                _studioHudActivePitchCounts[action.Pitch] = _studioHudActivePitchCounts.GetValueOrDefault(action.Pitch) + 1;
                _studioHudNoteOnsProcessed++;
                _studioHudRecentNoteOns.Enqueue(Stopwatch.GetTimestamp());
            }
            else if (_studioHudActivePitchCounts.TryGetValue(action.Pitch, out var count))
            {
                if (count <= 1) _studioHudActivePitchCounts.Remove(action.Pitch);
                else _studioHudActivePitchCounts[action.Pitch] = count - 1;
            }
        }
    }

    private void RefreshStudioHudTelemetry()
    {
        var cutoff = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
        int activeCount;
        int[] pitches;
        int recentRate;
        long noteOns;
        double peakRate;

        lock (_studioHudGate)
        {
            while (_studioHudRecentNoteOns.Count > 0 && _studioHudRecentNoteOns.Peek() < cutoff)
                _studioHudRecentNoteOns.Dequeue();

            activeCount = _studioHudActivePitchCounts.Values.Sum();
            pitches = _studioHudActivePitchCounts.Keys.OrderBy(p => p).ToArray();
            recentRate = _studioHudRecentNoteOns.Count;
            noteOns = _studioHudNoteOnsProcessed;
            if (recentRate > _studioHudPeakNoteRate) _studioHudPeakNoteRate = recentRate;
            peakRate = _studioHudPeakNoteRate;
        }

        var pitchLabel = pitches.Length == 0
            ? "—"
            : string.Join(" ", pitches.Take(3).Select(p => AnalysisDisplay.NoteName(p))) + (pitches.Length > 3 ? $" +{pitches.Length - 3}" : string.Empty);
        var activeLabel = $"{activeCount:N0} · {pitchLabel}";

        if (Math.Abs(_studioHudNoteRateSnapshot - recentRate) > 0.01)
        {
            _studioHudNoteRateSnapshot = recentRate;
            OnPropertyChanged(nameof(StudioHudNotesPerSecondLabel));
        }
        if (Math.Abs(_studioHudPeakNoteRateSnapshot - peakRate) > 0.01)
        {
            _studioHudPeakNoteRateSnapshot = peakRate;
            OnPropertyChanged(nameof(StudioHudPeakRateLabel));
        }
        if (_studioHudNoteOnsSnapshot != noteOns)
        {
            _studioHudNoteOnsSnapshot = noteOns;
            OnPropertyChanged(nameof(StudioHudNoteOnsLabel));
        }
        if (!string.Equals(_studioHudActiveChordSnapshot, activeLabel, StringComparison.Ordinal))
        {
            _studioHudActiveChordSnapshot = activeLabel;
            OnPropertyChanged(nameof(StudioHudActiveNotesLabel));
        }
    }

    private void ClearStudioHudActiveNotes(bool clearRecentRate)
    {
        lock (_studioHudGate)
        {
            _studioHudActivePitchCounts.Clear();
            if (clearRecentRate) _studioHudRecentNoteOns.Clear();
        }
        RefreshStudioHudTelemetry();
    }

    private void ResetStudioHudTelemetry()
    {
        lock (_studioHudGate)
        {
            _studioHudActivePitchCounts.Clear();
            _studioHudRecentNoteOns.Clear();
            _studioHudNoteOnsProcessed = 0;
            _studioHudPeakNoteRate = 0;
        }
        RefreshStudioHudTelemetry();
    }

    private void RaiseStudioHudStateProperties()
    {
        foreach (var name in new[]
        {
            nameof(StudioHudModeLabel), nameof(StudioHudStateLabel), nameof(StudioHudTempoLabel),
            nameof(StudioHudTracksLabel), nameof(StudioHudInputLabel), nameof(StudioHudProgressLabel)
        })
        {
            OnPropertyChanged(name);
        }
    }

    private void OpenSelectedMidiFolder()
    {
        if (SelectedLibraryItem is null) return;
        try
        {
            var folder = Path.GetDirectoryName(SelectedLibraryItem.FullPath);
            if (!string.IsNullOrWhiteSpace(folder)) OpenFolder(folder);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open the selected MIDI folder.", exception);
            StatusMessage = $"Could not open selected MIDI folder: {exception.Message}";
        }
    }

    private void ResetAppearance()
    {
        ThemeOption = "Dark";
        AccentColorOption = "Lime";
        ReduceMotion = false;
        StatusMessage = "Appearance restored to defaults.";
    }

    private void SaveAppearanceSettings()
    {
        if (!_settingsService.Save(_settings))
            StatusMessage = "Appearance changed for this session but could not be saved. Your MIDI library is unaffected; see logs.";
        else
            StatusMessage = "Appearance preference saved.";
    }

    private void SaveVisualizerSettings()
    {
        if (!_settingsService.Save(_settings))
            _logger.Warning("A visualiser preference could not be saved. The permanent MIDI library was not changed.");
    }

    private void SaveSettings()
    {
        if (_settingsService.Save(_settings)) StatusMessage = "Settings saved.";
        else StatusMessage = "Settings could not be saved. Your MIDI library is unaffected; see logs.";
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusMessage = $"Opened {path}";
        }
        catch (Exception exception)
        {
            _logger.Error($"Could not open folder '{path}'.", exception);
            StatusMessage = $"Could not open folder: {exception.Message}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.0} MB";
        return $"{bytes / (1024d * 1024 * 1024):0.00} GB";
    }

    public sealed class StudioTrackOption : ObservableObject
    {
        public MidiTrackInfo Info { get; }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
        public string Label => Info.DisplayName;
        public string Detail => Info.Detail;
        public string ProgramLabel => Info.IsDrumTrack ? "Percussion" : $"Program {Info.Program + 1}";

        public StudioTrackOption(MidiTrackInfo info, bool selected)
        {
            Info = info;
            _isSelected = selected;
        }
    }

}
