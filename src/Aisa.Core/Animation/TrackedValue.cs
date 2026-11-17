namespace Aisa.Core.Animation;

/// <summary>CSS-style cubic-bezier easing. Shrink uses (0.45, 0, 0.2, 1): firm, no overshoot.</summary>
public readonly record struct CubicBezier(double X1, double Y1, double X2, double Y2)
{
    public static readonly CubicBezier Shrink = new(0.45, 0, 0.2, 1);

    public double Evaluate(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        // Newton first, bisection if the slope collapses.
        double t = x;
        for (int i = 0; i < 8; i++)
        {
            double err = Sample(t, X1, X2) - x;
            if (Math.Abs(err) < 1e-6) return Sample(t, Y1, Y2);
            double d = Slope(t, X1, X2);
            if (Math.Abs(d) < 1e-6) break;
            t -= err / d;
        }

        double lo = 0, hi = 1;
        t = x;
        for (int i = 0; i < 30; i++)
        {
            double v = Sample(t, X1, X2);
            if (Math.Abs(v - x) < 1e-6) break;
            if (v < x) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return Sample(t, Y1, Y2);
    }

    private static double Sample(double t, double p1, double p2) =>
        3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;

    private static double Slope(double t, double p1, double p2) =>
        3 * (1 - t) * (1 - t) * p1 + 6 * (1 - t) * t * (p2 - p1) + 3 * t * t * (1 - p2);
}

/// <summary>
/// One animated scalar driven either by a damped spring (grow) or a timed bezier (shrink), per Spec 4.4.
/// Springs use SwiftUI's response/damping-fraction parameterisation and integrate in 1/240 s sub-steps.
/// </summary>
public sealed class TrackedValue(double initial)
{
    private const double SubStep = 1.0 / 240;
    private const double Epsilon = 0.01;

    private enum Mode { Idle, Spring, Curve }

    private Mode _mode;
    private double _velocity, _target, _stiffness, _damping, _accumulator;
    private double _from, _duration, _elapsed;
    private CubicBezier _ease;

    public double Value { get; private set; } = initial;
    public double Target => _mode == Mode.Idle ? Value : _target;
    public bool IsMoving => _mode != Mode.Idle;

    public void Snap(double value)
    {
        Value = _target = value;
        _velocity = 0;
        _mode = Mode.Idle;
    }

    public void AnimateSpring(double target, double response = 0.5, double dampingFraction = 0.72)
    {
        _target = target;
        double omega = 2 * Math.PI / response;
        _stiffness = omega * omega;                 // unit mass
        _damping = 2 * dampingFraction * omega;
        _accumulator = 0;
        _mode = Mode.Spring;                        // keeps current velocity, so retargeting is seamless
    }

    public void AnimateCurve(double target, double durationSeconds, CubicBezier ease)
    {
        _from = Value;
        _target = target;
        _duration = durationSeconds;
        _elapsed = 0;
        _ease = ease;
        _mode = Mode.Curve;
    }

    /// <returns>True while still moving.</returns>
    public bool Step(double dt)
    {
        switch (_mode)
        {
            case Mode.Spring:
                _accumulator += dt;
                while (_accumulator >= SubStep)
                {
                    _accumulator -= SubStep;
                    double accel = -_stiffness * (Value - _target) - _damping * _velocity;
                    _velocity += accel * SubStep;    // semi-implicit Euler: stable at this step size
                    Value += _velocity * SubStep;
                }
                if (Math.Abs(Value - _target) < Epsilon && Math.Abs(_velocity) < Epsilon) Snap(_target);
                break;

            case Mode.Curve:
                double before = Value;
                _elapsed += dt;
                double t = Math.Min(1, _elapsed / _duration);
                Value = _from + (_target - _from) * _ease.Evaluate(t);
                _velocity = dt > 0 ? (Value - before) / dt : 0; // lets a later spring pick up the motion
                if (t >= 1) Snap(_target);
                break;
        }
        return IsMoving;
    }
}

