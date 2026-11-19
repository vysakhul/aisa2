using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aisa.Core.Settings;

public enum IslandAnchor { Left, Center, Right }

/// <summary>User settings (Spec 3.2 G). Immutable; changes go through <see cref="SettingsStore.Update"/>.</summary>
public sealed record AisaSettings
{
    public IslandAnchor Anchor { get; init; } = IslandAnchor.Center;
    public double EdgeMargin { get; init; } = 12;
    public int AutoCloseSeconds { get; init; } = 15;      // 5–120
    public int CompactHideSeconds { get; init; } = 10;
    public bool OpenOnFaceHover { get; init; } = true;
    public bool SoundEnabled { get; init; } = true;
    public double UiVolume { get; init; } = 0.35;
    public double AlarmVolume { get; init; } = 0.8;
    public int SnoozeMinutes { get; init; } = 10;
    public bool LmStudioEnabled { get; init; } = true;
    public string LmStudioUrl { get; init; } = "http://localhost:1234/v1/";
    public string? LmStudioModel { get; init; }
    public bool StartWithWindows { get; init; }
    public bool ToastFallback { get; init; }

    public AisaSettings Normalized() => this with
    {
        EdgeMargin = Math.Clamp(EdgeMargin, 0, 400),
        AutoCloseSeconds = Math.Clamp(AutoCloseSeconds, 5, 120),
        CompactHideSeconds = Math.Clamp(CompactHideSeconds, 2, 600),
        UiVolume = Math.Clamp(UiVolume, 0, 1),
        AlarmVolume = Math.Clamp(AlarmVolume, 0, 1),
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 1, 120),
        LmStudioUrl = string.IsNullOrWhiteSpace(LmStudioUrl) ? "http://localhost:1234/v1/" : LmStudioUrl.Trim(),
        LmStudioModel = string.IsNullOrWhiteSpace(LmStudioModel) ? null : LmStudioModel.Trim(),
    };
}

/// <summary>
/// Live settings: <see cref="Changed"/> fires on every update so the overlay and settings window apply
/// changes immediately. Persists as JSON with an atomic temp-file swap.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();
    private AisaSettings _current = new();

    public SettingsStore(string path)
    {
        _path = path;
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aisa");

    public AisaSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public event Action<AisaSettings>? Changed;

    public AisaSettings Load()
    {
        AisaSettings loaded;
        try
        {
            loaded = File.Exists(_path)
                ? JsonSerializer.Deserialize<AisaSettings>(File.ReadAllText(_path), Json) ?? new AisaSettings()
                : new AisaSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            loaded = new AisaSettings(); // a broken settings file must never stop the app from starting
        }

        lock (_gate) _current = loaded.Normalized();
        return Current;
    }

    public AisaSettings Update(Func<AisaSettings, AisaSettings> change)
    {
        AisaSettings next;
        lock (_gate)
        {
            next = change(_current).Normalized();
            if (next == _current) return next;
            _current = next;
            Save(next);
        }
        Changed?.Invoke(next);
        return next;
    }

    private void Save(AisaSettings settings)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
