using System.IO;
using System.Windows;
using System.Windows.Threading;
using Aisa.App.Alerts;
using Aisa.App.Chat;
using Aisa.App.Shell;
using Aisa.Audio;
using Aisa.Character;
using Aisa.Core.Island;
using Aisa.Core.Settings;
using Aisa.Nlu;
using Aisa.Scheduling;
using Microsoft.Win32;

namespace Aisa.App;

/// <summary>Composition root: settings, services, overlay and tray.</summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private SettingsStore _settings = null!;
    private SoundService? _sound;
    private SchedulerService? _scheduler;
    private LmStudioClient? _llm;
    private IslandStateMachine? _island;
    private OverlayWindow? _overlay;
    private TrayIcon? _tray;
    private ChatController? _chat;
    private SettingsWindow? _settingsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Aisa.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        // Timers inside the headless machines post back through this context onto the UI thread.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));
        DispatcherUnhandledException += (_, args) => args.Handled = true; // never let a UI glitch kill alarms

        string dataDir = SettingsStore.DefaultDirectory;
        Directory.CreateDirectory(dataDir);
        _settings = new SettingsStore(Path.Combine(dataDir, "settings.json"));
        var settings = _settings.Load();
        var time = TimeProvider.System;

        _sound = new SoundService(time, soundFolder: Path.Combine(dataDir, "sounds"));
        var character = new CharacterEngine();
        character.SoundRequested += cue => _sound.Play(Map(cue));

        _island = new IslandStateMachine(time, IslandOptionsFrom(settings));
        _island.Changed += OnIslandChanged;

        _scheduler = new SchedulerService(new JsonScheduleStore(Path.Combine(dataDir, "schedule.json")), time,
            new SchedulerOptions { SnoozeLength = TimeSpan.FromMinutes(settings.SnoozeMinutes) });
        _llm = new LmStudioClient(LmOptionsFrom(settings), time);
        var nlu = new NluPipeline(_llm);

        _chat = new ChatController(nlu, _scheduler, character, _sound, time);
        var alerts = new AlertCoordinator(_scheduler, _island, character, _sound, time, () => _settings.Current.SnoozeMinutes, Dispatcher);

        _overlay = new OverlayWindow(_island, character, _chat, alerts, _settings);
        _overlay.SettingsRequested += ShowSettings;
        _overlay.Show(); // ShowActivated=False: never steals focus at launch

        _tray = new TrayIcon(_settings,
            open: () => _island.Open(),
            upcoming: () => { _island.Open(); _ = _chat.SendAsync("What's coming up?"); },
            showSettings: ShowSettings,
            pauseChanged: SetPaused,
            quit: Quit);

        ApplySettings(settings);
        _settings.Changed += s => Dispatcher.BeginInvoke(() => ApplySettings(s));
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;

        _overlay.StartGreeting();
        _sound.Play(SoundCue.Greeting);

        // Started after the overlay exists: alerts that fire during the greeting are held by the island machine.
        await _scheduler.StartAsync();
        alerts.Refresh();
    }

    private void OnIslandChanged(IslandSnapshot s)
    {
        // Alarms must ring even while paused: an alert brings the overlay back.
        if (s.View == IslandView.Alert && _overlay is { IsVisible: false }) _overlay.Show();

        // Peek / open / close sounds follow geometry changes.
        if (_lastMode is { } last && last != s.Mode && _sound is not null)
        {
            if (s.Mode == IslandMode.Compact && last == IslandMode.Closed) _sound.Play(SoundCue.Peek);
            else if (s.Mode == IslandMode.Expanded && s.View != IslandView.Greeting) _sound.Play(SoundCue.Open);
            else if (s.Mode == IslandMode.Compact && last == IslandMode.Expanded) _sound.Play(SoundCue.Close);
        }
        _lastMode = s.Mode;
    }

    private IslandMode? _lastMode;

    private void ApplySettings(AisaSettings s)
    {
        if (_sound is not null)
        {
            _sound.Muted = !s.SoundEnabled;
            _sound.UiVolume = (float)s.UiVolume;
            _sound.AlertVolume = (float)s.AlarmVolume;
        }
        _island?.UpdateOptions(IslandOptionsFrom(s));
        if (_scheduler is not null) _scheduler.Options.SnoozeLength = TimeSpan.FromMinutes(s.SnoozeMinutes);
        if (_llm is not null)
        {
            var o = LmOptionsFrom(s);
            _llm.Options.Enabled = o.Enabled;
            _llm.Options.BaseUrl = o.BaseUrl;
            _llm.Options.Model = o.Model;
        }
        try { StartupRegistration.Apply(s.StartWithWindows); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
    }

    private static IslandOptions IslandOptionsFrom(AisaSettings s) => new()
    {
        AutoClose = TimeSpan.FromSeconds(s.AutoCloseSeconds),
        CompactHide = TimeSpan.FromSeconds(s.CompactHideSeconds),
        OpenOnFaceHover = s.OpenOnFaceHover,
    };

    private static LmStudioOptions LmOptionsFrom(AisaSettings s)
    {
        string url = s.LmStudioUrl.EndsWith('/') ? s.LmStudioUrl : s.LmStudioUrl + "/";
        return new LmStudioOptions
        {
            Enabled = s.LmStudioEnabled && Uri.TryCreate(url, UriKind.Absolute, out _),
            BaseUrl = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : new Uri("http://localhost:1234/v1/"),
            Model = s.LmStudioModel,
        };
    }

    private static SoundCue Map(CharacterSound sound) => sound switch
    {
        CharacterSound.Hover => SoundCue.Hover,
        CharacterSound.Love => SoundCue.Love,
        CharacterSound.Slap => SoundCue.Slap,
        CharacterSound.Annoyed => SoundCue.Annoyed,
        CharacterSound.Dizzy => SoundCue.Dizzy,
        _ => SoundCue.Happy,
    };

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Pause hides the overlay; alarms keep ringing and bring it back.</summary>
    private void SetPaused(bool paused)
    {
        if (_overlay is null || _island is null) return;
        if (paused)
        {
            _island.Hide();
            _overlay.Hide();
        }
        else
        {
            _overlay.Show();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _scheduler?.RecheckNow();
    }

    private void OnTimeChanged(object? sender, EventArgs e) => _scheduler?.RecheckNow();

    private async void Quit()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        _tray?.Dispose();
        _overlay?.Shutdown();
        if (_scheduler is not null) await _scheduler.DisposeAsync();
        _sound?.Dispose();
        _llm?.Dispose();
        _island?.Dispose();
        _singleInstance?.ReleaseMutex();
        Shutdown();
    }
}
