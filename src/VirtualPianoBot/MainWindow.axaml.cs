using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using VirtualPianoBot.ViewModels;

namespace VirtualPianoBot;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _motionTimer;
    private double _motionTime;
    private double _pageTransition;
    private bool _pageTransitionActive;
    private INotifyPropertyChanged? _observedViewModel;

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        _motionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _motionTimer.Tick += OnMotionTick;
        _motionTimer.Start();
        Closed += (_, _) =>
        {
            _motionTimer.Stop();
            if (_observedViewModel != null)
                _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel != null)
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _observedViewModel = DataContext as INotifyPropertyChanged;
        if (_observedViewModel != null)
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;

        UpdateMotionMode();
    }

    private bool IsReducedMotion => (DataContext as MainWindowViewModel)?.ReduceMotion ?? false;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ReduceMotion))
            UpdateMotionMode();

        if (e.PropertyName == "CurrentPage" && !IsReducedMotion)
            BeginPageTransition();
    }

    private void UpdateMotionMode()
    {
        if (IsReducedMotion)
        {
            _motionTimer.Stop();
            _pageTransitionActive = false;
            var surface = this.FindControl<Grid>("WorkspaceSurface");
            var transform = surface?.RenderTransform as TranslateTransform;
            if (surface != null) surface.Opacity = 1;
            if (transform != null) transform.X = 0;
            var sweep = this.FindControl<Border>("PageTransitionSweep");
            var glow = this.FindControl<Border>("PageTransitionGlow");
            if (sweep != null) sweep.Opacity = 0;
            if (glow != null) glow.Opacity = 0;
        }
        else if (!_motionTimer.IsEnabled)
        {
            _motionTimer.Start();
        }
    }

    private void BeginPageTransition()
    {
        if (IsReducedMotion) return;
        _pageTransition = 0;
        _pageTransitionActive = true;

        var surface = this.FindControl<Grid>("WorkspaceSurface");
        var transform = surface?.RenderTransform as TranslateTransform;
        if (surface != null) surface.Opacity = 0.72;
        if (transform != null) transform.X = 26;
    }

    private void OnMotionTick(object? sender, EventArgs e)
    {
        _motionTime += 0.045;
        var pulse = (Math.Sin(_motionTime) + 1.0) * 0.5;
        var pulse2 = (Math.Sin(_motionTime * 0.72 + 1.7) + 1.0) * 0.5;

        var glow = this.FindControl<Ellipse>("HeroGlow");
        var glow2 = this.FindControl<Ellipse>("HeroGlow2");
        var ring1 = this.FindControl<Ellipse>("PulseRing1");
        var ring2 = this.FindControl<Ellipse>("PulseRing2");
        var bar1 = this.FindControl<Border>("SpectrumBar1");
        var bar2 = this.FindControl<Border>("SpectrumBar2");
        var bar3 = this.FindControl<Border>("SpectrumBar3");
        var bar4 = this.FindControl<Border>("SpectrumBar4");
        var scan = this.FindControl<Border>("ScanLine");
        var orb1 = this.FindControl<Ellipse>("AmbientOrb1");
        var orb2 = this.FindControl<Ellipse>("AmbientOrb2");
        var orb3 = this.FindControl<Ellipse>("AmbientOrb3");

        if (glow != null) glow.Opacity = 0.035 + pulse * 0.065;
        if (glow2 != null) glow2.Opacity = 0.035 + pulse2 * 0.075;
        if (ring1 != null) ring1.Opacity = 0.42 + pulse * 0.52;
        if (ring2 != null) ring2.Opacity = 0.32 + pulse2 * 0.52;

        if (bar1 != null) bar1.Height = 12 + (pulse * 24);
        if (bar2 != null) bar2.Height = 14 + (pulse2 * 31);
        if (bar3 != null) bar3.Height = 10 + ((1 - pulse) * 26);
        if (bar4 != null) bar4.Height = 13 + (((pulse + pulse2) * 0.5) * 29);
        if (scan != null) scan.Opacity = 0.18 + pulse2 * 0.32;

        if (orb1 != null)
        {
            Canvas.SetLeft(orb1, 570 + Math.Sin(_motionTime * 0.17) * 80);
            Canvas.SetTop(orb1, -230 + Math.Cos(_motionTime * 0.13) * 55);
            orb1.Opacity = 0.018 + pulse * 0.035;
        }
        if (orb2 != null)
        {
            Canvas.SetLeft(orb2, -120 + Math.Cos(_motionTime * 0.11) * 65);
            Canvas.SetTop(orb2, 420 + Math.Sin(_motionTime * 0.15) * 45);
            orb2.Opacity = 0.016 + pulse2 * 0.03;
        }
        if (orb3 != null)
        {
            Canvas.SetLeft(orb3, 820 + Math.Sin(_motionTime * 0.19) * 70);
            Canvas.SetTop(orb3, 620 + Math.Cos(_motionTime * 0.16) * 40);
            orb3.Opacity = 0.014 + pulse * 0.028;
        }

        AnimateParticles();
        AnimatePageTransition();
    }

    private void AnimateParticles()
    {
        var particleNames = new[] { "Particle1", "Particle2", "Particle3", "Particle4", "Particle5", "Particle6", "Particle7", "Particle8" };
        var baseX = new[] { 140d, 360d, 610d, 820d, 1030d, 1160d, 260d, 720d };
        var baseY = new[] { 130d, 310d, 190d, 390d, 160d, 500d, 650d, 710d };

        for (var i = 0; i < particleNames.Length; i++)
        {
            var particle = this.FindControl<Ellipse>(particleNames[i]);
            if (particle == null) continue;
            var phase = _motionTime * (0.10 + i * 0.013) + i * 0.9;
            Canvas.SetLeft(particle, baseX[i] + Math.Sin(phase) * (12 + i * 2));
            Canvas.SetTop(particle, baseY[i] + Math.Cos(phase * 0.8) * (18 + i));
            particle.Opacity = 0.20 + ((Math.Sin(phase * 1.7) + 1) * 0.5) * 0.55;
        }
    }

    private void AnimatePageTransition()
    {
        if (!_pageTransitionActive) return;

        _pageTransition += 0.055;
        var t = Math.Clamp(_pageTransition, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);

        var surface = this.FindControl<Grid>("WorkspaceSurface");
        var transform = surface?.RenderTransform as TranslateTransform;
        var sweep = this.FindControl<Border>("PageTransitionSweep");
        var glow = this.FindControl<Border>("PageTransitionGlow");

        if (surface != null) surface.Opacity = 0.72 + eased * 0.28;
        if (transform != null) transform.X = 26 * (1 - eased);
        if (sweep != null)
        {
            sweep.Opacity = t < 0.25 ? 0.75 * (1 - t / 0.25) : 0;
            Canvas.SetLeft(sweep, -5 + eased * 1550);
        }
        if (glow != null)
        {
            glow.Opacity = t < 0.4 ? 0.12 * (1 - t / 0.4) : 0;
            Canvas.SetLeft(glow, -120 + eased * 1550);
        }

        if (t >= 1)
        {
            _pageTransitionActive = false;
            if (surface != null) surface.Opacity = 1;
            if (transform != null) transform.X = 0;
            if (sweep != null) sweep.Opacity = 0;
            if (glow != null) glow.Opacity = 0;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
