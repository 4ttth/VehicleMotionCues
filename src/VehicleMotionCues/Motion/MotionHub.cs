using System.Diagnostics;

namespace VehicleMotionCues.Motion;

public enum SourceKind
{
    Phone,
    LaptopSensor,
    Demo,
}

/// <summary>
/// Collects vehicle-frame acceleration from every source (on any thread) and hands the
/// freshest one to the overlay. The phone wins over the laptop sensor because it is
/// usually mounted more rigidly; demo motion wins over both while it is switched on.
/// </summary>
public sealed class MotionHub
{
    static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(0.75);

    readonly object _gate = new();
    readonly Dictionary<SourceKind, (double Lateral, double Longitudinal, long Timestamp)> _latest = new();

    public bool DemoEnabled { get; set; }

    /// <param name="lateral">m/s², positive when the vehicle accelerates to the right (turning right).</param>
    /// <param name="longitudinal">m/s², positive when the vehicle speeds up, negative when braking.</param>
    public void Report(SourceKind kind, double lateral, double longitudinal)
    {
        if (!double.IsFinite(lateral) || !double.IsFinite(longitudinal))
            return;
        lock (_gate)
            _latest[kind] = (lateral, longitudinal, Stopwatch.GetTimestamp());
    }

    public bool IsLive(SourceKind kind)
    {
        lock (_gate)
            return _latest.TryGetValue(kind, out var v) && Stopwatch.GetElapsedTime(v.Timestamp) < StaleAfter;
    }

    public SourceKind? Current(out double lateral, out double longitudinal)
    {
        lateral = longitudinal = 0;
        ReadOnlySpan<SourceKind> order = DemoEnabled
            ? [SourceKind.Demo, SourceKind.Phone, SourceKind.LaptopSensor]
            : [SourceKind.Phone, SourceKind.LaptopSensor];

        lock (_gate)
        {
            foreach (var kind in order)
            {
                if (_latest.TryGetValue(kind, out var v) && Stopwatch.GetElapsedTime(v.Timestamp) < StaleAfter)
                {
                    lateral = v.Lateral;
                    longitudinal = v.Longitudinal;
                    return kind;
                }
            }
        }
        return null;
    }
}
