using NAudio.Wave;

namespace Aisa.Audio;

public enum SoundCue { Peek, Open, Close, Send, Finish, Blip, Error, Proud, Pop, Hover, Love, Slap, Annoyed, Dizzy, Happy, Greeting }

public enum AlertTone { AlarmLoop, ReminderChime }

public interface IAudioOutput : IDisposable
{
    void Init(ISampleProvider source);
    void Play();
    void Stop();
}

public interface IAudioOutputFactory
{
    IAudioOutput Create();
}

/// <summary>WASAPI shared-mode output on Windows; silent no-op elsewhere.</summary>
public sealed class DefaultAudioOutputFactory : IAudioOutputFactory
{
    public IAudioOutput Create() => OperatingSystem.IsWindows() ? new WasapiOutput() : new NullOutput();

    private sealed class NullOutput : IAudioOutput
    {
        public void Init(ISampleProvider source) { }
        public void Play() { }
        public void Stop() { }
        public void Dispose() { }
    }

    /// <summary>
    /// Converts an ISampleProvider to IWaveProvider using a real float[] scratch buffer and
    /// Buffer.BlockCopy, avoiding NAudio's WaveBuffer byte[]/float[] explicit-layout aliasing.
    /// </summary>
    private sealed class SafeSampleToWaveProvider(ISampleProvider source) : IWaveProvider
    {
        private float[] _scratch = [];

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            var samplesNeeded = count / sizeof(float);
            if (samplesNeeded <= 0) return 0;
            if (_scratch.Length < samplesNeeded) _scratch = new float[samplesNeeded];
            var samplesRead = source.Read(_scratch, 0, samplesNeeded);
            var bytesRead = samplesRead * sizeof(float);
            Buffer.BlockCopy(_scratch, 0, buffer, offset, bytesRead);
            return bytesRead;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class WasapiOutput : IAudioOutput
    {
        private readonly NAudio.Wave.WasapiOut _out =
            new(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 60);

        public void Init(ISampleProvider source) => _out.Init(new SafeSampleToWaveProvider(source));
        public void Play() => _out.Play();
        public void Stop() => _out.Stop();
        public void Dispose() => _out.Dispose();
    }
}
