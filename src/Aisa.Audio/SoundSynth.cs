namespace Aisa.Audio;

/// <summary>Procedural sounds; nothing is loaded from disk.</summary>
public static class SoundSynth
{
    public const int SampleRate = 48000;
    private const float PeakTarget = 0.8f;

    public static float[] Render(SoundCue cue) => cue switch
    {
        SoundCue.Peek => Single(560, 880, 0.11, 0.9),
        SoundCue.Open => Seq(0.8, (523.25, 523.25, 0.00, 0.12), (783.99, 783.99, 0.08, 0.20)),
        SoundCue.Close => Seq(0.8, (783.99, 783.99, 0.00, 0.12), (523.25, 523.25, 0.08, 0.20)),
        SoundCue.Send => Send(),
        SoundCue.Finish => Seq(0.8, (659.25, 659.25, 0.00, 0.30), (830.61, 830.61, 0.09, 0.30)),
        SoundCue.Blip => Single(1250, 1250, 0.035, 0.7),
        SoundCue.Error => Seq(0.8, (150, 130, 0.00, 0.13, Wave.Triangle), (140, 120, 0.17, 0.15, Wave.Triangle)),
        SoundCue.Proud => Seq(0.7, (523.25, 523.25, 0.00, 0.14), (659.25, 659.25, 0.11, 0.14), (783.99, 783.99, 0.22, 0.28)),
        SoundCue.Pop => Pop(),
        SoundCue.Hover => Single(2400, 2600, 0.03, 0.35),
        SoundCue.Love => Love(),
        SoundCue.Slap => Slap(),
        SoundCue.Annoyed => Annoyed(),
        SoundCue.Dizzy => Dizzy(),
        SoundCue.Happy => Seq(0.8, (659.25, 740, 0.00, 0.11), (880, 988, 0.12, 0.17)),
        SoundCue.Greeting => Seq(0.7,
            (523.25, 523.25, 0.00, 0.26), (659.25, 659.25, 0.18, 0.26),
            (783.99, 783.99, 0.36, 0.26), (1046.5, 1046.5, 0.56, 0.44)),
        _ => throw new ArgumentOutOfRangeException(nameof(cue)),
    };

    public static float[] Render(AlertTone tone) => tone switch
    {
        AlertTone.AlarmLoop => Alarm(),
        AlertTone.ReminderChime => Chime(),
        _ => throw new ArgumentOutOfRangeException(nameof(tone)),
    };

    private enum Wave { Sine, Triangle }

    private static float[] Buffer(double seconds) => new float[(int)(seconds * SampleRate)];

    private static float[] Single(double f0, double f1, double dur, double amp)
    {
        var b = Buffer(dur);
        Add(b, 0, dur, f0, f1, amp, Wave.Sine, 0.2);
        return Finish(b);
    }

    private static float[] Seq(double amp, params (double f0, double f1, double at, double dur, Wave wave)[] notes)
    {
        var end = notes.Max(n => n.at + n.dur);
        var b = Buffer(end);
        foreach (var n in notes) Add(b, n.at, n.dur, n.f0, n.f1, amp, n.wave, 0.25);
        return Finish(b);
    }

    private static float[] Seq(double amp, params (double f0, double f1, double at, double dur)[] notes) =>
        Seq(amp, notes.Select(n => (n.f0, n.f1, n.at, n.dur, Wave.Sine)).ToArray());

    /// <summary>Adds a note with a smooth attack and a smooth release that reaches zero at its end.</summary>
    private static void Add(float[] buf, double start, double dur, double f0, double f1, double amp,
        Wave wave, double attackFrac, double decay = 0, Func<double, double>? fm = null)
    {
        var s0 = (int)(start * SampleRate);
        var n = Math.Min((int)(dur * SampleRate), buf.Length - s0);
        var attack = Math.Max(0.004, dur * attackFrac * 0.4);
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / SampleRate;
            var u = t / dur;
            var f = f0 + (f1 - f0) * u;
            if (fm != null) f += fm(t);
            phase += 2 * Math.PI * f / SampleRate;
            var a = Smooth(t / attack);
            var r = Smooth((dur - t) / (dur * 0.6));
            var env = a * r * Math.Exp(-decay * t);
            var s = wave == Wave.Sine ? Math.Sin(phase) : Triangle(phase);
            buf[s0 + i] += (float)(s * env * amp);
        }
    }

    private static double Triangle(double phase)
    {
        var x = phase / (2 * Math.PI);
        x -= Math.Floor(x);
        return 4 * Math.Abs(x - 0.5) - 1;
    }

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    private static float[] Finish(float[] b)
    {
        var peak = 0f;
        foreach (var s in b) peak = Math.Max(peak, Math.Abs(s));
        var g = peak > PeakTarget ? PeakTarget / peak : 1f;
        var fade = Math.Min(SampleRate / 500, b.Length / 2); // 2 ms guard against clicks
        for (var i = 0; i < b.Length; i++)
        {
            var w = 1f;
            if (i < fade) w = (float)i / fade;
            else if (b.Length - 1 - i < fade) w = (float)(b.Length - 1 - i) / fade;
            b[i] *= g * w;
        }
        return b;
    }

    private static float[] Send()
    {
        var b = Buffer(0.12);
        Add(b, 0, 0.10, 420, 1700, 0.55, Wave.Sine, 0.3);
        Add(b, 0.07, 0.05, 1900, 1900, 0.35, Wave.Sine, 0.2);
        return Finish(b);
    }

    private static float[] Pop()
    {
        var b = Buffer(0.09);
        Add(b, 0, 0.09, 260, 950, 0.9, Wave.Sine, 0.1, decay: 25);
        return Finish(b);
    }

    private static float[] Love()
    {
        var b = Buffer(0.42);
        Add(b, 0, 0.42, 480, 880, 0.7, Wave.Sine, 0.3, fm: t => 18 * Math.Sin(2 * Math.PI * 9 * t));
        Add(b, 0.05, 0.34, 720, 1320, 0.2, Wave.Sine, 0.3);
        return Finish(b);
    }

    private static float[] Slap()
    {
        var b = Buffer(0.16);
        Add(b, 0, 0.16, 190, 55, 0.9, Wave.Sine, 0.05, decay: 14);
        var rng = new Random(7);
        var n = (int)(0.012 * SampleRate);
        for (var i = 0; i < n; i++)
            b[i] += (float)((rng.NextDouble() * 2 - 1) * 0.25 * Math.Sin(Math.PI * i / n));
        return Finish(b);
    }

    private static float[] Annoyed()
    {
        var b = Buffer(0.45);
        Add(b, 0, 0.45, 320, 150, 0.7, Wave.Triangle, 0.15, fm: t => 8 * Math.Sin(2 * Math.PI * 14 * t));
        return Finish(b);
    }

    private static float[] Dizzy()
    {
        var b = Buffer(0.55);
        Add(b, 0, 0.55, 420, 640, 0.75, Wave.Sine, 0.2, fm: t => 90 * Math.Sin(2 * Math.PI * 7 * t));
        return Finish(b);
    }

    private static float[] Alarm()
    {
        const double period = 1.2;
        var b = Buffer(period);
        for (var i = 0; i < 4; i++)
        {
            var f = i % 2 == 0 ? 880.0 : 1174.66;
            Add(b, i * 0.2, 0.14, f, f, 0.55, Wave.Sine, 0.2);
            Add(b, i * 0.2, 0.14, f * 2, f * 2, 0.12, Wave.Sine, 0.2);
        }
        return Finish(b);
    }

    private static float[] Chime()
    {
        var b = Buffer(1.2);
        double[] notes = [783.99, 987.77, 1174.66];
        for (var i = 0; i < notes.Length; i++)
        {
            var at = i * 0.22;
            var dur = 1.2 - at;
            Add(b, at, dur, notes[i], notes[i], 0.5, Wave.Sine, 0.02, decay: 3.5);
            Add(b, at, dur, notes[i] * 2.76, notes[i] * 2.76, 0.08, Wave.Sine, 0.02, decay: 8);
        }
        return Finish(b);
    }
}
