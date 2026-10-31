using System.Numerics;

namespace VehicleMotionCues.Motion;

/// <summary>
/// Turns device-frame acceleration into the vehicle's lateral/longitudinal acceleration.
///
/// The device can sit at any tilt. "Up" comes from gravity; "forward" is whichever of the
/// candidate device axes lies flattest in the horizontal plane (e.g. a phone lying flat with
/// its top toward the windscreen uses +Y, a phone standing up facing you uses -Z).
/// </summary>
public sealed class VehicleFrameSolver
{
    const double UpSmoothingSeconds = 0.4;

    readonly Vector3[] _forwardCandidates;
    Vector3 _up;
    bool _hasUp;
    double _lastTime = double.NaN;

    public VehicleFrameSolver(params Vector3[] forwardCandidates) => _forwardCandidates = forwardCandidates;

    /// <param name="linearAccel">Device-frame acceleration with gravity removed, m/s².</param>
    /// <param name="up">Device-frame vector pointing at the sky (any length).</param>
    /// <param name="timeSeconds">Sample time, monotonic.</param>
    public (double Lateral, double Longitudinal) Solve(Vector3 linearAccel, Vector3 up, double timeSeconds)
    {
        double dt = double.IsNaN(_lastTime) ? 0 : Math.Clamp(timeSeconds - _lastTime, 0, 0.5);
        _lastTime = timeSeconds;

        if (up.LengthSquared() > 1e-6f)
        {
            up = Vector3.Normalize(up);
            if (!_hasUp)
            {
                _up = up;
                _hasUp = true;
            }
            else
            {
                float k = (float)(1 - Math.Exp(-dt / UpSmoothingSeconds));
                _up = Vector3.Normalize(Vector3.Lerp(_up, up, k));
            }
        }
        if (!_hasUp)
            return (0, 0);

        Vector3 forward = default;
        float bestLength = 0;
        foreach (var candidate in _forwardCandidates)
        {
            var horizontal = candidate - Vector3.Dot(candidate, _up) * _up;
            float length = horizontal.Length();
            if (length > bestLength)
            {
                bestLength = length;
                forward = horizontal;
            }
        }
        if (bestLength < 1e-3f)
            return (0, 0);

        forward /= bestLength;
        var right = Vector3.Cross(forward, _up);
        return (Vector3.Dot(linearAccel, right), Vector3.Dot(linearAccel, forward));
    }
}
