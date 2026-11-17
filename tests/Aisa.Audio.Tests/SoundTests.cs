using Aisa.Audio;
using Microsoft.Extensions.Time.Testing;
using NAudio.Wave;

namespace Aisa.Audio.Tests;

public class SynthTests
{
    public static IEnumerable<object[]> Cues() => Enum.GetValues<SoundCue>().Select(c => new object[] { c });

    private static void CheckCommon(float[] s)
    {
        Assert.NotEmpty(s);
        Assert.All(s, x => Assert.True(float.IsFinite(x)));
        Assert.True(s.Max(Math.Abs) <= 0.9f);
        Assert.True(s.Max(Math.Abs) > 0.05f);
        Assert.True(Math.Abs(s[0]) < 0.01f);
        Assert.True(Math.Abs(s[^1]) < 0.01f);
    }

    [Theory, MemberData(nameof(Cues))]
    public void CueRenders(SoundCue cue)
    {
        var s = SoundSynth.Render(cue);
        CheckCommon(s);
        var max = cue == SoundCue.Greeting ? 1.3 : 0.6;
        Assert.True(s.Length <= max * SoundSynth.SampleRate);
    }

    [Fact]
    public void GreetingIsAboutOneSecond()
    {
        var sec = SoundSynth.Render(SoundCue.Greeting).Length / (double)SoundSynth.SampleRate;
        Assert.InRange(sec, 0.8, 1.3);
    }

    [Fact]
    public void AlarmLoopIsSeamless()
    {
        var s = SoundSynth.Render(AlertTone.AlarmLoop);
        CheckCommon(s);
        Assert.InRange(s.Length / (double)SoundSynth.SampleRate, 1.0, 1.5);
        Assert.True(Math.Abs(s[0] - s[^1]) < 0.01f);
    }

    [Fact]
    public void ReminderChimeRenders()
    {
        var s = SoundSynth.Render(AlertTone.ReminderChime);
        CheckCommon(s);
        Assert.InRange(s.Length / (double)SoundSynth.SampleRate, 1.0, 1.4);
    }
}

public class ServiceTests : IDisposable
{
    private sealed class FakeOutput : IAudioOutput
    {
        public ISampleProvider? Source;
        public bool Disposed;
        public void Init(ISampleProvider source) => Source = source;
        public void Play() { }
        public void Stop() { }
        public void Dispose() => Disposed = true;

        public float Pull(int frames = 4800)
        {
            var buf = new float[frames * 2];
            Source!.Read(buf, 0, buf.Length);
            return buf.Max(Math.Abs);
        }
    }

    private sealed class FakeFactory : IAudioOutputFactory
    {
        public readonly List<FakeOutput> Outputs = new();
        public IAudioOutput Create() { var o = new FakeOutput(); lock (Outputs) Outputs.Add(o); return o; }
        public int Count { get { lock (Outputs) return Outputs.Count; } }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aisa-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _svcs = new();

    public ServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var s in _svcs) s.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private SoundService Make(FakeTimeProvider t, FakeFactory f, string? folder = null)
    {
        var s = new SoundService(t, f, folder);
        _svcs.Add(s);
        return s;
    }

    private static void WaitFor(Func<bool> cond)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException();
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void MuteSilencesUiButNotAlerts()
    {
        var f = new FakeFactory();
        var svc = Make(new FakeTimeProvider(), f);
        svc.UiVolume = 0.5f;
        svc.Play(SoundCue.Finish);
        WaitFor(() => f.Count == 1);
        Assert.True(f.Outputs[0].Pull() > 0.01f);

        svc.Muted = true;
        f.Outputs[0].Pull(48000); // drain
        svc.Play(SoundCue.Finish);
        Assert.Equal(0f, f.Outputs[0].Pull());

        svc.StartAlert(AlertTone.ReminderChime);
        Assert.True(svc.IsAlertPlaying);
        Assert.True(f.Outputs[0].Pull() > 0.05f);
    }

    [Fact]
    public void UiVolumeScalesOutput()
    {
        var f = new FakeFactory();
        var svc = Make(new FakeTimeProvider(), f);
        svc.UiVolume = 0.12f;
        svc.Play(SoundCue.Finish);
        WaitFor(() => f.Count == 1);
        var peak = f.Outputs[0].Pull(24000);
        Assert.InRange(peak, 0.01f, 0.12f);
    }

    [Fact]
    public void DeviceReleasedAfterSilenceAndRecreated()
    {
        var t = new FakeTimeProvider();
        var f = new FakeFactory();
        var svc = Make(t, f);
        svc.Play(SoundCue.Blip);
        WaitFor(() => f.Count == 1);
        f.Outputs[0].Pull(48000); // let the cue finish

        t.Advance(TimeSpan.FromSeconds(1));
        Thread.Sleep(100);
        Assert.False(f.Outputs[0].Disposed);

        t.Advance(TimeSpan.FromSeconds(1));
        WaitFor(() => f.Outputs[0].Disposed);

        svc.Play(SoundCue.Blip);
        WaitFor(() => f.Count == 2);
        Assert.False(f.Outputs[1].Disposed);
    }

    [Fact]
    public void AlarmLoopsUntilStopped()
    {
        var t = new FakeTimeProvider();
        var f = new FakeFactory();
        var svc = Make(t, f);
        svc.StartAlert(AlertTone.AlarmLoop);
        WaitFor(() => f.Count == 1);
        for (var i = 0; i < 5; i++) Assert.True(f.Outputs[0].Pull(48000) > 0.05f); // 5 s > one period
        Assert.True(svc.IsAlertPlaying);

        svc.StopAlert();
        Assert.False(svc.IsAlertPlaying);
        f.Outputs[0].Pull(4800);
        Assert.Equal(0f, f.Outputs[0].Pull(48000));

        t.Advance(TimeSpan.FromSeconds(2));
        WaitFor(() => f.Outputs[0].Disposed);
    }

    [Fact]
    public void ReminderChimePlaysOnce()
    {
        var f = new FakeFactory();
        var svc = Make(new FakeTimeProvider(), f);
        svc.StartAlert(AlertTone.ReminderChime);
        WaitFor(() => f.Count == 1);
        f.Outputs[0].Pull(72000);
        Assert.False(svc.IsAlertPlaying || f.Outputs[0].Pull() > 0f);
    }

    private void WriteWav(string name, float amplitude, int rate = 44100, int channels = 1)
    {
        using var w = new WaveFileWriter(Path.Combine(_dir, name + ".wav"), WaveFormat.CreateIEEEFloatWaveFormat(rate, channels));
        var samples = new float[rate / 4 * channels];
        Array.Fill(samples, amplitude);
        w.WriteSamples(samples, 0, samples.Length);
    }

    [Fact]
    public void OverrideWavIsUsedAndResampled()
    {
        WriteWav("Blip", 0.5f, 22050, 2);
        var f = new FakeFactory();
        var svc = Make(new FakeTimeProvider(), f, _dir);
        svc.UiVolume = 1f;
        svc.Play(SoundCue.Blip);
        WaitFor(() => f.Count == 1);
        f.Outputs[0].Pull(240); // gain ramp
        Assert.InRange(f.Outputs[0].Pull(2400), 0.45f, 0.55f);
    }

    [Fact]
    public void CorruptAlertOverrideFallsBackToSynth()
    {
        File.WriteAllBytes(Path.Combine(_dir, "AlarmLoop.wav"), [1, 2, 3, 4, 5]);
        var f = new FakeFactory();
        var svc = Make(new FakeTimeProvider(), f, _dir);
        svc.StartAlert(AlertTone.AlarmLoop);
        WaitFor(() => f.Count == 1);
        Assert.True(f.Outputs[0].Pull(24000) > 0.05f);
    }
}
