using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using VehicleMotionCues.Motion;

namespace VehicleMotionCues.Overlay;

/// <summary>
/// Draws a loose, staggered field of small dots in a band along one screen edge and slides it with
/// the vehicle, modelled on iOS Vehicle Motion Cues. The field wraps inside the band (fading at the
/// band boundaries), so sustained turns stream the dots continuously.
///
/// Each edge has its own narrow window (far cheaper to composite than a full-screen overlay). The
/// left strip is the leader: it runs the simulation and frame loop; the right strip follows and
/// just paints the leader's state mirrored.
/// </summary>
public sealed class DotsControl : Control
{
    public const double StripWidth = 150;

    const double FadePerSecond = 3;
    const double BoundaryFade = 16;
    const double ActiveInterval = 1 / 30.0;
    const double IdleInterval = 0.1;

    static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
    static readonly IPen Outline = new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 1.2);

    readonly MotionHub _hub;
    readonly Settings _settings;
    readonly CueDynamics _dynamics = new();
    readonly DispatcherTimer _timer;
    readonly System.Diagnostics.Stopwatch _clock = new();

    (double U, double V)[] _pattern = [];
    double _patternBand, _patternPeriod;
    bool _patternMore;

    bool _enabled;
    double _visibility;
    double _paintedX = double.NaN, _paintedY, _paintedVisibility = -1;

    readonly bool _rightSide;
    readonly DotsControl? _leader;
    readonly List<DotsControl> _followers = [];

    /// <summary>Creates the leader strip (left edge).</summary>
    public DotsControl(MotionHub hub, Settings settings)
    {
        _hub = hub;
        _settings = settings;
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(ActiveInterval), DispatcherPriority.Render, OnTick);
        _timer.Stop();
    }

    /// <summary>Creates a follower strip that mirrors the leader on the right edge.</summary>
    public DotsControl(DotsControl leader) : this(leader._hub, leader._settings)
    {
        _rightSide = true;
        _leader = leader;
        leader._followers.Add(this);
    }

    public void Repaint()
    {
        InvalidateVisual();
        foreach (var follower in _followers)
            follower.InvalidateVisual();
    }

    /// <summary>Master switch (tray / hotkey). In Automatic mode dots still only appear while moving.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            EnsureLoop();
        }
    }

    public bool IsShowingDots => _visibility > 0;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureLoop();
    }

    void EnsureLoop()
    {
        if (_leader is not null)
            return;
        _clock.Restart();
        _timer.Interval = TimeSpan.FromSeconds(ActiveInterval);
        _timer.Start();
    }

    double Step(double dt)
    {
        bool live = _hub.Current(out double lateral, out double longitudinal) is not null;
        _dynamics.Step(lateral, longitudinal, live, dt, _settings.Gain,
            _settings.FlipLeftRight ? -1 : 1, _settings.FlipForwardBack ? -1 : 1);

        bool show = _enabled && (_settings.DisplayMode == "Always" || _dynamics.IsVehicleMoving || _hub.DemoEnabled);
        return show ? 1 : 0;
    }

    /// <summary>
    /// Runs at 30 Hz while dots are moving or fading and 10 Hz otherwise. A timer rather than
    /// RequestAnimationFrame: presenting transparent top-most windows is the expensive part, and
    /// 30 fps is smooth for gliding dots at roughly half the CPU of vsync-driven 60 fps.
    /// </summary>
    void OnTick(object? sender, EventArgs e)
    {
        double dt = Math.Clamp(_clock.Elapsed.TotalSeconds, 0, 0.15);
        _clock.Restart();

        double target = Step(dt);
        _visibility = _visibility < target
            ? Math.Min(target, _visibility + dt * FadePerSecond)
            : Math.Max(target, _visibility - dt * FadePerSecond);

        if (double.IsNaN(_paintedX) || Math.Abs(_dynamics.FlowX - _paintedX) > 0.05 ||
            Math.Abs(_dynamics.FlowY - _paintedY) > 0.05 || Math.Abs(_visibility - _paintedVisibility) > 0.001)
        {
            _paintedX = _dynamics.FlowX;
            _paintedY = _dynamics.FlowY;
            _paintedVisibility = _visibility;
            Repaint();
        }

        bool idle = _visibility == target && (_dynamics.IsAtRest || _visibility == 0);
        _timer.Interval = TimeSpan.FromSeconds(idle ? IdleInterval : ActiveInterval);
    }

    public override void Render(DrawingContext context)
    {
        var state = _leader ?? this;
        double visibility = state._paintedVisibility;
        if (visibility <= 0.001)
            return;

        double width = Bounds.Width, height = Bounds.Height;
        bool more = _settings.MoreDots;
        double band = Math.Min(more ? StripWidth : 120, width);
        double period = more ? 300 : 360;
        if (_pattern.Length == 0 || band != _patternBand || period != _patternPeriod || more != _patternMore)
            BuildPattern(band, period, more);

        double radius = _settings.LargerDots ? 6.5 : 4.5;
        double fx = state._paintedX, fy = state._paintedY;
        // The band hugs the screen edge: left strip from x = 0, right strip ending at its right side.
        double bandLeft = _rightSide ? width - band : 0;

        foreach (var (u0, v0) in _pattern)
        {
            // Mirror the pattern on the right so both edges look the same, but move them together.
            double u = Mod((_rightSide ? band - u0 : u0) + fx, band);
            double uFade = Math.Clamp(Math.Min(u, band - u) / BoundaryFade, 0, 1);
            if (uFade <= 0)
                continue;

            for (double y = Mod(v0 + fy, period) - period; y < height + period; y += period)
            {
                if (y < -radius || y > height + radius)
                    continue;
                double vFade = Math.Clamp(Math.Min(y, height - y) / 40, 0, 1);
                double alpha = visibility * uFade * vFade;
                if (alpha <= 0.01)
                    continue;
                using (context.PushOpacity(alpha))
                    context.DrawEllipse(Fill, Outline, new Point(bandLeft + u, y), radius, radius);
            }
        }
    }

    /// <summary>
    /// A deterministic, slightly irregular staggered grid: two (or three) lanes across the band,
    /// alternate rows offset, each dot nudged a little so the field reads as loose dots, not a table.
    /// </summary>
    void BuildPattern(double band, double period, bool more)
    {
        _patternBand = band;
        _patternPeriod = period;
        _patternMore = more;

        int lanes = more ? 3 : 2;
        int rows = more ? 6 : 5;
        var rng = new Random(20241016);
        var dots = new List<(double, double)>();
        for (int row = 0; row < rows; row++)
        {
            for (int lane = 0; lane < lanes; lane++)
            {
                double u = band * (lane + (row % 2 == 0 ? 0.35 : 0.85)) / lanes;
                double v = period * (row + (lane % 2) * 0.5) / rows;
                u += (rng.NextDouble() - 0.5) * band * 0.18;
                v += (rng.NextDouble() - 0.5) * period / rows * 0.35;
                dots.Add((Mod(u, band), Mod(v, period)));
            }
        }
        _pattern = dots.ToArray();
    }

    static double Mod(double value, double m) => ((value % m) + m) % m;
}
