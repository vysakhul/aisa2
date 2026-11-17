using System.Windows;

namespace Aisa.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var overlay = new OverlayWindow();
        overlay.Show(); // ShowActivated=False, so this never steals focus at launch
    }
}
