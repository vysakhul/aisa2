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

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class WasapiOutput : IAudioOutput
    {
        private readonly NAudio.Wave.WasapiOut _out =
            new(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 60);

        public void Init(ISampleProvider source) => _out.Init(source);
        public void Play() => _out.Play();
        public void Stop() => _out.Stop();
        public void Dispose() => _out.Dispose();
    }
}
