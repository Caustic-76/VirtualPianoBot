using System.Diagnostics;
using Avalonia.Threading;
using VirtualPianoBot.Core;

namespace VirtualPianoBot.Studio;

/// <summary>
/// High-resolution Studio transport. The v4.1.0 engine is the timing reference:
/// MIDI action timestamps stay in source time, the transport applies speed once,
/// and keyboard actions are dispatched from a dedicated high-resolution loop.
/// </summary>
public sealed class StudioPlaybackEngine : IDisposable
{
    private readonly DispatcherTimer _uiTimer;
    private readonly Stopwatch _clock = new();
    private IReadOnlyList<PlaybackAction> _actions = Array.Empty<PlaybackAction>();
    private double _durationMs;
    private double _positionMs;
    private double _playStartPositionMs;
    private bool _disposed;
    private PlaybackPlan? _plan;
    private int _nextActionIndex;
    private CancellationTokenSource? _playbackCts;
    private Task? _playbackTask;
    private bool _paused;
    private readonly object _gate = new();
    private readonly AppLogger? _logger;

    public StudioPlaybackEngine(AppLogger? logger = null)
    {
        _logger = logger;
        _logger?.Info("StudioPlaybackEngine created.");
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _uiTimer.Tick += OnUiTick;
    }

    public event EventHandler? Changed;
    public event Action<PlaybackAction>? ActionTriggered;
    public bool IsPlaying { get; private set; }
    public bool IsCountingDown { get; private set; }
    public bool IsPaused { get { lock (_gate) return _paused; } }
    public int CountdownRemaining { get; private set; }
    public double PositionMs => _positionMs;
    public double DurationMs => _durationMs;
    public double Speed { get; set; } = 1.0;
    public bool Loop { get; set; }
    public PlaybackPlan? Plan => _plan;

    public void Load(VirtualPianoBot.Analysis.MidiAnalysisResult? result)
    {
        _logger?.Info($"Studio Load: result={(result is null ? "null" : result.FileName)}, duration={result?.DurationMs:0.###}ms.");
        StopInternal(resetPosition: true, raiseChanged: false);
        _durationMs = result?.DurationMs ?? 0;
        _positionMs = 0;
        _plan = null;
        _actions = Array.Empty<PlaybackAction>();
        _nextActionIndex = 0;
        RaiseChanged();
    }

    public void SetPlan(PlaybackPlan? plan)
    {
        _logger?.Info($"Studio SetPlan: actions={plan?.Actions.Count ?? 0}, playable={plan?.PlayableNotes ?? 0}, skipped={plan?.SkippedNotes ?? 0}, range={plan?.RangeMode ?? "none"}.");
        _plan = plan;
        _actions = plan?.Actions ?? Array.Empty<PlaybackAction>();
        _durationMs = Math.Max(_durationMs, _actions.Count == 0 ? 0 : _actions.Max(a => a.TimeMs));
        _nextActionIndex = FindNextActionIndex(_positionMs);
        RaiseChanged();
    }

    public void Play()
    {
        _logger?.Info($"Studio Play requested: position={_positionMs:0.###}ms, duration={_durationMs:0.###}ms, speed={Speed:0.###}, loop={Loop}, paused={_paused}.");
        lock (_gate)
        {
            if (_disposed || IsPlaying || IsCountingDown || _durationMs <= 0 || _actions.Count == 0)
                return;

            if (_paused && _playbackTask is not null && !_playbackTask.IsCompleted)
            {
                // Resume exactly where v4.1.0 left off. No second countdown and no
                // catch-up burst: the transport clock simply starts again at the
                // current MIDI position.
                _paused = false;
                IsPlaying = true;
                _clock.Restart();
                _playStartPositionMs = _positionMs;
                _uiTimer.Start();
                RaiseChanged();
                return;
            }

            if (_positionMs >= _durationMs)
                _positionMs = 0;

            _paused = false;
            _nextActionIndex = FindNextActionIndex(_positionMs);
            IsCountingDown = true;
            CountdownRemaining = 3;
            _playbackCts?.Cancel();
            _playbackCts = new CancellationTokenSource();
            var token = _playbackCts.Token;
            _playbackTask = Task.Run(() => RunPlaybackAsync(token), token);
            _uiTimer.Start();
        }
        RaiseChanged();
    }

    public void Pause()
    {
        _logger?.Info($"Studio Pause requested at {_positionMs:0.###}ms.");
        lock (_gate)
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            _paused = true;
            _clock.Stop();
            _uiTimer.Stop();
        }
        RaiseChanged();
    }

    public void Stop()
    {
        _logger?.Info("Studio Stop requested.");
        StopInternal(resetPosition: true, raiseChanged: true);
    }

    public void Seek(double positionMs)
    {
        _logger?.Info($"Studio Seek requested: {positionMs:0.###}ms.");
        lock (_gate)
        {
            _positionMs = Math.Clamp(positionMs, 0, Math.Max(0, _durationMs));
            _nextActionIndex = FindNextActionIndex(_positionMs);
            if (IsPlaying)
            {
                _playStartPositionMs = _positionMs;
                _clock.Restart();
            }
        }
        RaiseChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _logger?.Info("StudioPlaybackEngine disposing.");
        _disposed = true;
        StopInternal(resetPosition: false, raiseChanged: false);
        _uiTimer.Tick -= OnUiTick;
    }

    private async Task RunPlaybackAsync(CancellationToken token)
    {
        try
        {
            // Countdown is deliberately outside the transport clock. It cannot
            // compress or otherwise alter the MIDI timing.
            for (var remaining = 3; remaining >= 1; remaining--)
            {
                lock (_gate)
                {
                    if (token.IsCancellationRequested) return;
                    CountdownRemaining = remaining;
                }
                RaiseChanged();
                await Task.Delay(1000, token).ConfigureAwait(false);
            }

            lock (_gate)
            {
                if (token.IsCancellationRequested) return;
                IsCountingDown = false;
                CountdownRemaining = 0;
                IsPlaying = true;
                _playStartPositionMs = _positionMs;
                _clock.Restart();
                _nextActionIndex = FindNextActionIndex(_positionMs);
            }
            RaiseChanged();

            while (!token.IsCancellationRequested)
            {
                bool paused;
                lock (_gate)
                {
                    paused = !IsPlaying;
                    if (!paused)
                    {
                        var speed = Math.Clamp(Speed, 0.1, 3.0);
                        var current = _playStartPositionMs + _clock.Elapsed.TotalMilliseconds * speed;
                        _positionMs = Math.Min(_durationMs, current);
                    }
                }

                if (paused)
                {
                    await Task.Delay(10, token).ConfigureAwait(false);
                    continue;
                }

                DispatchActionsUpTo(_positionMs);

                if (_positionMs >= _durationMs)
                {
                    if (Loop && _durationMs > 0)
                    {
                        lock (_gate)
                        {
                            _positionMs = 0;
                            _playStartPositionMs = 0;
                            _clock.Restart();
                            _nextActionIndex = 0;
                        }
                        RaiseChanged();
                        continue;
                    }

                    lock (_gate)
                    {
                        _positionMs = _durationMs;
                        IsPlaying = false;
                        _clock.Stop();
                    }
                    RaiseChanged();
                    break;
                }

                await Task.Delay(1, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop/cancel path.
        }
        catch (Exception exception)
        {
            _logger?.Error("Studio playback task faulted.", exception);
            lock (_gate)
            {
                IsPlaying = false;
                IsCountingDown = false;
                CountdownRemaining = 0;
                _clock.Stop();
            }
            RaiseChanged();
        }
        finally
        {
            lock (_gate)
            {
                IsCountingDown = false;
                CountdownRemaining = 0;
                if (!IsPlaying) _clock.Stop();
            }
            RaiseChanged();
        }
    }

    private void DispatchActionsUpTo(double current)
    {
        while (true)
        {
            PlaybackAction? action = null;
            lock (_gate)
            {
                if (_nextActionIndex >= _actions.Count) return;
                var candidate = _actions[_nextActionIndex];
                if (candidate.TimeMs > current) return;
                action = candidate;
                _nextActionIndex++;
            }
            try
            {
                ActionTriggered?.Invoke(action);
            }
            catch (Exception exception)
            {
                _logger?.Error($"Studio action dispatch failed: pitch={action.Pitch}, key={action.Key}, shift={action.Shift}, noteOn={action.IsNoteOn}, time={action.TimeMs:0.###}ms.", exception);
                throw;
            }
        }
    }

    private int FindNextActionIndex(double positionMs)
    {
        var actions = _actions;
        var lo = 0;
        var hi = actions.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (actions[mid].TimeMs < positionMs) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private void StopInternal(bool resetPosition, bool raiseChanged)
    {
        lock (_gate)
        {
            _playbackCts?.Cancel();
            _playbackCts = null;
            IsPlaying = false;
            _paused = false;
            IsCountingDown = false;
            CountdownRemaining = 0;
            _clock.Stop();
            _uiTimer.Stop();
            if (resetPosition)
            {
                _positionMs = 0;
                _nextActionIndex = 0;
            }
        }
        if (raiseChanged) RaiseChanged();
    }

    private void OnUiTick(object? sender, EventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger?.Error("Studio Changed event handler failed.", exception);
        }
    }
}
