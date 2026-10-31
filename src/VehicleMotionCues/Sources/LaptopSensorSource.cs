#if WINDOWS
using System.Diagnostics;
using System.Numerics;
using VehicleMotionCues.Motion;
using Windows.Devices.Sensors;

namespace VehicleMotionCues.Sources;

/// <summary>
/// Reads the laptop's built-in accelerometer (2-in-1s, Surface and some convertibles have one).
/// Windows reports acceleration in g with gravity included and pointing down the Z axis when the
/// screen faces the sky, so the reading is negated to get specific force before solving.
/// </summary>
public sealed class LaptopSensorSource : IDisposable
{
    const double GravitySmoothingSeconds = 1.5;
    const float StandardGravity = 9.80665f;

    readonly MotionHub _hub;
    readonly Accelerometer _accelerometer;
    // On a lap the screen faces you, so "forward" is out of the back of the lid (-Z);
    // folded flat as a tablet with its top toward the windscreen it is +Y.
    readonly VehicleFrameSolver _solver = new(new Vector3(0, 0, -1), new Vector3(0, 1, 0));
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Vector3 _gravity;
    bool _hasGravity;
    double _lastTime;

    LaptopSensorSource(MotionHub hub, Accelerometer accelerometer)
    {
        _hub = hub;
        _accelerometer = accelerometer;
        _accelerometer.ReportInterval = Math.Max(_accelerometer.MinimumReportInterval, 16);
        _accelerometer.ReadingChanged += OnReadingChanged;
    }

    public static LaptopSensorSource? TryCreate(MotionHub hub)
    {
        try
        {
            var accelerometer = Accelerometer.GetDefault();
            return accelerometer is null ? null : new LaptopSensorSource(hub, accelerometer);
        }
        catch (Exception)
        {
            return null;
        }
    }

    void OnReadingChanged(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
    {
        var r = args.Reading;
        var specificForce = -StandardGravity * new Vector3((float)r.AccelerationX, (float)r.AccelerationY, (float)r.AccelerationZ);
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0, 0.5);
        _lastTime = now;

        if (!_hasGravity)
        {
            _gravity = specificForce;
            _hasGravity = true;
        }
        else
        {
            float k = (float)(1 - Math.Exp(-dt / GravitySmoothingSeconds));
            _gravity = Vector3.Lerp(_gravity, specificForce, k);
        }

        var (lateral, longitudinal) = _solver.Solve(specificForce - _gravity, _gravity, now);
        _hub.Report(SourceKind.LaptopSensor, lateral, longitudinal);
    }

    public void Dispose() => _accelerometer.ReadingChanged -= OnReadingChanged;
}
#endif
