namespace VehicleMotionCues.Motion;

/// <summary>
/// Turns vehicle acceleration into motion of the dot field, the way iOS Vehicle Motion Cues does.
///
/// The dots behave like loose objects sliding on a surface inside the cabin. Acceleration pushes
/// the field (turning right sends it left, braking sends it up/forward, speeding up sends it
/// down/toward you), friction slows it again, and it never springs back. A sustained turn therefore
/// keeps the dots streaming sideways for as long as you feel the turn, which is what makes the cue
/// match the inner ear instead of just nudging a few pixels.
/// </summary>
public sealed class CueDynamics
{
    const double InputSmoothingSeconds = 0.06;
    const double BiasSeconds = 4.0;         // removes sensor offset and sloped roads
    const double DeadZone = 0.15;           // m/s², ignore idle engine rumble
    const double FrictionSeconds = 0.7;     // how long the field keeps gliding after a push
    const double MaxSpeed = 450;            // DIPs/s
    const double ActivityThreshold = 0.25;  // m/s² RMS that counts as "vehicle is moving"
    const double ActivityHoldSeconds = 8;   // keep cues up through traffic-light pauses

    double _smoothX, _smoothY, _biasX, _biasY, _inX, _inY, _energy;

    /// <summary>Total field displacement in DIPs. Unbounded; the renderer wraps it.</summary>
    public double FlowX { get; private set; }
    public double FlowY { get; private set; }

    public double VelocityX { get; private set; }
    public double VelocityY { get; private set; }

    public double SecondsSinceActive { get; private set; } = double.PositiveInfinity;

    /// <summary>True when the field has stopped and nothing is pushing it.</summary>
    public bool IsAtRest => Math.Abs(VelocityX) + Math.Abs(VelocityY) < 0.5 && _inX == 0 && _inY == 0;

    /// <param name="gain">Field acceleration in DIPs/s² per m/s² of vehicle acceleration.</param>
    public void Step(double lateral, double longitudinal, bool live, double dt, double gain, int flipX, int flipY)
    {
        if (!live)
            lateral = longitudinal = 0;

        double a = 1 - Math.Exp(-dt / InputSmoothingSeconds);
        _smoothX += (lateral - _smoothX) * a;
        _smoothY += (longitudinal - _smoothY) * a;

        // Without a source, forget the bias quickly so nothing keeps drifting.
        double b = 1 - Math.Exp(-dt / (live ? BiasSeconds : 0.5));
        _biasX += ((live ? _smoothX : 0) - _biasX) * b;
        _biasY += ((live ? _smoothY : 0) - _biasY) * b;

        double x = _smoothX - _biasX, y = _smoothY - _biasY;

        // Activity: smoothed energy of the bias-free signal (turns, braking, road texture).
        _energy += (x * x + y * y - _energy) * (1 - Math.Exp(-dt / 1.5));
        SecondsSinceActive = live && Math.Sqrt(_energy) > ActivityThreshold ? 0 : SecondsSinceActive + dt;

        _inX = ApplyDeadZone(x);
        _inY = ApplyDeadZone(y);

        double pushX = -_inX * gain * flipX;
        double pushY = _inY * gain * flipY;
        VelocityX = Math.Clamp(VelocityX + (pushX - VelocityX / FrictionSeconds) * dt, -MaxSpeed, MaxSpeed);
        VelocityY = Math.Clamp(VelocityY + (pushY - VelocityY / FrictionSeconds) * dt, -MaxSpeed, MaxSpeed);
        if (_inX == 0 && _inY == 0 && Math.Abs(VelocityX) + Math.Abs(VelocityY) < 0.5)
            VelocityX = VelocityY = 0;

        FlowX += VelocityX * dt;
        FlowY += VelocityY * dt;
    }

    public bool IsVehicleMoving => SecondsSinceActive < ActivityHoldSeconds;

    static double ApplyDeadZone(double v) => Math.Sign(v) * Math.Max(0, Math.Abs(v) - DeadZone);
}
