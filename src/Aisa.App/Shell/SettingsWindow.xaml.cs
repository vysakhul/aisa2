using System.Windows;
using Aisa.Core.Settings;

namespace Aisa.App.Shell;

/// <summary>Live settings: every control change is applied immediately (Spec 3.2 G).</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private bool _loading;

    public SettingsWindow(SettingsStore store)
    {
        InitializeComponent();
        _store = store;
        AnchorBox.ItemsSource = Enum.GetValues<IslandAnchor>();
        Load(store.Current);
    }

    private void Load(AisaSettings s)
    {
        _loading = true;
        AnchorBox.SelectedItem = s.Anchor;
        EdgeMargin.Value = s.EdgeMargin;
        AutoClose.Value = s.AutoCloseSeconds;
        CompactHide.Value = s.CompactHideSeconds;
        FaceHover.IsChecked = s.OpenOnFaceHover;
        Snooze.Value = s.SnoozeMinutes;
        SoundOn.IsChecked = s.SoundEnabled;
        UiVolume.Value = s.UiVolume;
        AlarmVolume.Value = s.AlarmVolume;
        LmEnabled.IsChecked = s.LmStudioEnabled;
        LmUrl.Text = s.LmStudioUrl;
        LmModel.Text = s.LmStudioModel ?? "";
        StartWithWindows.IsChecked = s.StartWithWindows;
        _loading = false;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized) return;
        _store.Update(s => s with
        {
            Anchor = AnchorBox.SelectedItem is IslandAnchor a ? a : s.Anchor,
            EdgeMargin = EdgeMargin.Value,
            AutoCloseSeconds = (int)AutoClose.Value,
            CompactHideSeconds = (int)CompactHide.Value,
            OpenOnFaceHover = FaceHover.IsChecked == true,
            SnoozeMinutes = (int)Snooze.Value,
            SoundEnabled = SoundOn.IsChecked == true,
            UiVolume = UiVolume.Value,
            AlarmVolume = AlarmVolume.Value,
            LmStudioEnabled = LmEnabled.IsChecked == true,
            LmStudioUrl = LmUrl.Text,
            LmStudioModel = LmModel.Text,
            StartWithWindows = StartWithWindows.IsChecked == true,
        });
    }
}
