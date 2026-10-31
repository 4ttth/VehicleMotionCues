using System.Diagnostics;

namespace VehicleMotionCues.Motion;

/// <summary>Synthetic turns and braking so you can preview the cues without a moving vehicle.</summary>
public sealed class DemoSource : IDisposable
{
    readonly MotionHub _hub;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Timer _timer;

    public DemoSource(MotionHub hub)
    {
        _hub = hub;
        _timer = new Timer(_ => Tick(), null, 0, 16);
    }

    void Tick()
    {
        double t = _clock.Elapsed.TotalSeconds;
        // A gentle S-bend every ~9 s and a speed-up / brake cycle every ~13 s.
        double lateral = 2.4 * Math.Sin(2 * Math.PI * t / 9.0) * Math.Abs(Math.Sin(2 * Math.PI * t / 18.0));
        double longitudinal = 1.8 * Math.Sin(2 * Math.PI * t / 13.0);
        _hub.Report(SourceKind.Demo, lateral, longitudinal);
    }

    public void Dispose() => _timer.Dispose();
}
