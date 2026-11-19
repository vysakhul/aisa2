using System.Collections.Concurrent;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Aisa.Audio;

public sealed class SoundService : IDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider _time;
    private readonly IAudioOutputFactory _factory;
    private readonly string? _soundFolder;
    private readonly VoiceBank _ui = new();
    private readonly VoiceBank _alert = new();
    private readonly Dictionary<string, float[]?> _sounds = new();
    private readonly object _cacheLock = new();
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _worker;
    private readonly RootProvider _root;

    private long _lastActive;
    private volatile bool _muted;
    private volatile bool _disposed;
    private float _uiVolume = 0.12f;
    private float _alertVolume = 0.8f;

    // Touched only on the worker thread.
    private IAudioOutput? _output;
    private ITimer? _idleTimer;

    public SoundService(TimeProvider? time = null, IAudioOutputFactory? outputFactory = null, string? soundFolder = null)
    {
        _time = time ?? TimeProvider.System;
        _factory = outputFactory ?? new DefaultAudioOutputFactory();
        _soundFolder = soundFolder;
        _lastActive = _time.GetTimestamp();
        _root = new RootProvider(this);
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "Aisa.Audio" };
        _worker.Start();
    }

    public bool Muted { get => _muted; set => _muted = value; }

    public float UiVolume
    {
        get => Volatile.Read(ref _uiVolume);
        set => Volatile.Write(ref _uiVolume, Math.Clamp(value, 0f, 1f));
    }

    public float AlertVolume
    {
        get => Volatile.Read(ref _alertVolume);
        set => Volatile.Write(ref _alertVolume, Math.Clamp(value, 0f, 1f));
    }

    public bool IsAlertPlaying => _alert.HasActive;

    public void Play(SoundCue cue)
    {
        if (_disposed || _muted) return;
        var data = Load(cue.ToString());
        if (data == null) return;
        _ui.Add(new Voice(data, loop: false));
        Wake();
    }

    public void StartAlert(AlertTone tone)
    {
        if (_disposed) return;
        _alert.StopAll();
        var data = Load(tone.ToString());
        if (data == null) return;
        _alert.Add(new Voice(data, loop: tone == AlertTone.AlarmLoop));
        Wake();
    }

    public void StopAlert() => _alert.StopAll();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _work.Add(ReleaseDevice);
        _work.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
    }

    private void Wake()
    {
        Interlocked.Exchange(ref _lastActive, _time.GetTimestamp());
        PostWork(EnsureDevice);
    }

    private void PostWork(Action a)
    {
        try { _work.Add(a); } catch (InvalidOperationException) { }
    }

    private void WorkLoop()
    {
        foreach (var a in _work.GetConsumingEnumerable())
        {
            try { a(); } catch { /* a failing device must never take the app down */ }
        }
    }

    private void EnsureDevice()
    {
        if (_output != null || _disposed) return;
        IAudioOutput? o = null;
        try
        {
            o = _factory.Create();
            o.Init(_root);
            o.Play();
            _output = o;
            _idleTimer = _time.CreateTimer(_ => PostWork(CheckIdle), null, IdlePoll, IdlePoll);
        }
        catch
        {
            try { o?.Dispose(); } catch { }
        }
    }

    private void CheckIdle()
    {
        if (_output == null) return;
        if (_ui.HasActive || _alert.HasActive) return;
        var last = Interlocked.Read(ref _lastActive);
        if (_time.GetElapsedTime(last) >= IdleTimeout) ReleaseDevice();
    }

    private void ReleaseDevice()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
        var o = _output;
        _output = null;
        if (o == null) return;
        try { o.Stop(); } catch { }
        try { o.Dispose(); } catch { }
    }

    private float[]? Load(string name)
    {
        lock (_cacheLock)
        {
            if (_sounds.TryGetValue(name, out var sound)) return sound;
            sound = _soundFolder == null
                ? null
                : TryLoadWav(Path.Combine(_soundFolder, name + ".wav"));
            _sounds[name] = sound;
            return sound;
        }
    }

    private static float[]? TryLoadWav(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var reader = new WaveFileReader(path);
            ISampleProvider src = reader.ToSampleProvider();
            if (src.WaveFormat.SampleRate != AudioSampleRate)
                src = new WdlResamplingSampleProvider(src, AudioSampleRate);
            var ch = src.WaveFormat.Channels;
            var samples = new List<float>();
            var buf = new float[4800 * ch];
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0)
            {
                for (var i = 0; i + ch <= n; i += ch)
                {
                    var sum = 0f;
                    for (var c = 0; c < ch; c++) sum += buf[i + c];
                    samples.Add(sum / ch);
                }
                if (samples.Count > AudioSampleRate * 60) break;
            }
            return samples.Count > 0 ? samples.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    private const int AudioSampleRate = 48000;

    private sealed class Voice(float[] data, bool loop)
    {
        private const int FadeFrames = 960;
        public int Pos;
        public int FadeLeft = -1;
        public bool Done;

        public void BeginFade() { if (FadeLeft < 0) FadeLeft = FadeFrames; }

        public void MixInto(float[] dst, int frames)
        {
            for (var i = 0; i < frames && !Done; i++)
            {
                if (Pos >= data.Length)
                {
                    if (!loop) { Done = true; break; }
                    Pos = 0;
                }
                var s = data[Pos++];
                if (FadeLeft >= 0)
                {
                    s *= (float)FadeLeft / FadeFrames;
                    if (--FadeLeft < 0) { Done = true; }
                }
                dst[i] += s;
            }
        }
    }

    private sealed class VoiceBank
    {
        private readonly object _lock = new();
        private readonly List<Voice> _voices = new();
        private volatile bool _hasActive;

        /// <summary>True while any voice is playing and has not been told to stop.</summary>
        public bool HasActive => _hasActive;

        public void Add(Voice v)
        {
            lock (_lock) { _voices.Add(v); _hasActive = true; }
        }

        public void StopAll()
        {
            lock (_lock)
            {
                foreach (var v in _voices) v.BeginFade();
                _hasActive = false;
            }
        }

        public bool Mix(float[] dst, int frames)
        {
            lock (_lock)
            {
                if (_voices.Count == 0) return false;
                foreach (var v in _voices) v.MixInto(dst, frames);
                _voices.RemoveAll(v => v.Done);
                _hasActive = _voices.Any(v => v.FadeLeft < 0);
                return true;
            }
        }
    }

    /// <summary>Stereo float mixer that never ends; silence when idle.</summary>
    private sealed class RootProvider(SoundService svc) : ISampleProvider
    {
        private float[] _mono = new float[4096];
        private float _uiGain, _alertGain;

        public WaveFormat WaveFormat { get; } = global::NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(AudioSampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            // Zero via typed indexing instead of Array.Clear: NAudio's SampleToWaveProvider uses
            // WaveBuffer to alias byte[] as float[], so Array.Clear sees byte[] at runtime and
            // clears only 1/4 of the float buffer (at a byte offset), leaving stale audio looping.
            for (var i = 0; i < count; i++) buffer[offset + i] = 0f;

            var frames = count / 2;
            if (frames <= 0) return count;
            if (_mono.Length < frames) _mono = new float[frames];

            var uiTarget = svc._muted ? 0f : svc.UiVolume;
            var active = MixBank(svc._ui, buffer, offset, frames, ref _uiGain, uiTarget);
            active |= MixBank(svc._alert, buffer, offset, frames, ref _alertGain, svc.AlertVolume);
            if (active) Interlocked.Exchange(ref svc._lastActive, svc._time.GetTimestamp());
            return count;
        }

        private bool MixBank(VoiceBank bank, float[] buffer, int offset, int frames, ref float gain, float target)
        {
            Array.Clear(_mono, 0, frames);
            if (!bank.Mix(_mono, frames)) { gain = target; return false; }
            var g = gain;
            var step = (target - g) / frames;
            for (var i = 0; i < frames; i++)
            {
                g += step;
                var s = Math.Clamp(_mono[i] * g, -1f, 1f);
                buffer[offset + 2 * i] += s;
                buffer[offset + 2 * i + 1] += s;
            }
            gain = target;
            return true;
        }
    }
}
