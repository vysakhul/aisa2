using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Aisa.Core.Settings;

namespace Aisa.App.Shell;

/// <summary>Spec 4.1 tray: Open, Upcoming, Position, Settings, Pause, Quit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pause;
    private readonly Dictionary<IslandAnchor, ToolStripMenuItem> _positions = [];
    private readonly IntPtr _hicon;

    public TrayIcon(SettingsStore settings, Action open, Action upcoming, Action showSettings, Action<bool> pauseChanged, Action quit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => open());
        menu.Items.Add("Upcoming", null, (_, _) => upcoming());

        var position = new ToolStripMenuItem("Position");
        foreach (var anchor in Enum.GetValues<IslandAnchor>())
        {
            var item = new ToolStripMenuItem(anchor.ToString(), null, (_, _) => settings.Update(s => s with { Anchor = anchor }));
            _positions[anchor] = item;
            position.DropDownItems.Add(item);
        }
        menu.Items.Add(position);
        menu.Items.Add("Settings…", null, (_, _) => showSettings());

        _pause = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        _pause.CheckedChanged += (_, _) => pauseChanged(_pause.Checked);
        menu.Items.Add(_pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        using var bitmap = DrawIcon();
        _hicon = bitmap.GetHicon();
        _icon = new NotifyIcon
        {
            Icon = Icon.FromHandle(_hicon),
            Text = "Aisa",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) open(); };

        UpdateChecks(settings.Current.Anchor);
        settings.Changed += s => menu.BeginInvoke(() => UpdateChecks(s.Anchor));
    }

    private void UpdateChecks(IslandAnchor anchor)
    {
        foreach (var (key, item) in _positions) item.Checked = key == anchor;
    }

    /// <summary>A tiny notch with two eyes, drawn at runtime so no icon asset is needed.</summary>
    private static Bitmap DrawIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var body = new GraphicsPath();
        body.AddArc(2, 4, 28, 24, 0, 360);
        g.FillPath(Brushes.Black, body);
        using var outline = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f);
        g.DrawPath(outline, body);
        g.FillEllipse(Brushes.White, 10, 12, 4, 8);
        g.FillEllipse(Brushes.White, 18, 12, 4, 8);
        return bmp;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        DestroyIcon(_hicon);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
