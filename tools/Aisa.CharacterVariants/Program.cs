using Aisa.Character;
using Aisa.CharacterVariants;
using Aisa.CharacterVariants.Designs;
using SkiaSharp;

// Renders a comparison sheet of alternative Aisa base designs plus one detail sheet per design.
var outDir = args.Length > 0 ? args[0] : "/tmp/verify/aisa-variants";
Directory.CreateDirectory(outDir);

const double Frame = 1.0 / 60;
var designs = new (string Key, ICharacterDesign Design)[]
{
    ("0", new CurrentDesign()),
    ("a", new OrbDesign()),
    ("b", new RobotDesign()),
    ("c", new BeanDesign()),
    ("d", new DropletDesign()),
    ("e", new GlyphDesign()),
    ("f", new CloudDesign()),
};

var scenes = new (string Name, Action<CharacterEngine> Setup, double Seconds)[]
{
    ("Idle", e => e.SetGaze(260, 30), 1.0),
    ("Happy", e => e.PlayEmote(Emote.Happy), 0.5),
    ("Working", e => e.SetAlertState(CharacterState.Working), 1.0),
    ("Approval", e => e.SetAlertState(CharacterState.Approval), 1.3),
    ("Love", e => e.PlayEmote(Emote.Love), 0.6),
    ("Annoyed", e => e.Slap(), 0.22),
    ("Dizzy", e => { e.Slap(); e.Step(0.1); e.Slap(); e.Step(0.1); e.Slap(); }, 1.6),
    ("Sleeping", e => e.SetAlertState(CharacterState.Sleeping), 2.6),
};

static CharacterPose Run(Action<CharacterEngine> setup, double seconds)
{
    var e = new CharacterEngine(new Random(7));
    e.SetExpanded(true);
    setup(e);
    for (double t = 0; t < seconds - 1e-9; t += Frame) e.Step(Frame);
    return e.Pose;
}

var poses = scenes.Select(s => Run(s.Setup, s.Seconds)).ToArray();

using var typeface = File.Exists("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
    ? SKTypeface.FromFile("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf") : SKTypeface.Default;
using var boldface = File.Exists("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf")
    ? SKTypeface.FromFile("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf") : typeface;
using var font = new SKFont(typeface, 12);
using var titleFont = new SKFont(boldface, 15);
using var headFont = new SKFont(typeface, 13);
using var text = new SKPaint { IsAntialias = true, Color = new SKColor(0xB8, 0xB8, 0xC4) };
using var title = new SKPaint { IsAntialias = true, Color = SKColors.White };
using var grid = new SKPaint { Color = new SKColor(0x26, 0x26, 0x2E), StrokeWidth = 1, Style = SKPaintStyle.Stroke };

// ---- overview.png ----
const int LabelW = 250, Cell = 168, HeaderH = 34, SmallW = 64, MidW = 92;
const float D = 88;
{
    var width = LabelW + scenes.Length * Cell + SmallW + MidW;
    var height = HeaderH + designs.Length * Cell;
    using var bmp = new SKBitmap(width, height);
    using var c = new SKCanvas(bmp);
    c.Clear(SKColors.Black);
    var batch = new Batch();

    for (var i = 0; i <= designs.Length; i++) c.DrawLine(0, HeaderH + i * Cell + 0.5f, width, HeaderH + i * Cell + 0.5f, grid);
    for (var j = 0; j <= scenes.Length; j++) c.DrawLine(LabelW + j * Cell + 0.5f, HeaderH, LabelW + j * Cell + 0.5f, height, grid);
    c.DrawLine(LabelW + scenes.Length * Cell + SmallW + 0.5f, HeaderH, LabelW + scenes.Length * Cell + SmallW + 0.5f, height, grid);

    for (var j = 0; j < scenes.Length; j++)
        c.DrawText(scenes[j].Name, LabelW + j * Cell + Cell / 2f, 22, SKTextAlign.Center, headFont, text);
    c.DrawText("20 px", LabelW + scenes.Length * Cell + SmallW / 2f, 22, SKTextAlign.Center, headFont, text);
    c.DrawText("44 px", LabelW + scenes.Length * Cell + SmallW + MidW / 2f, 22, SKTextAlign.Center, headFont, text);

    for (var i = 0; i < designs.Length; i++)
    {
        var design = designs[i].Design;
        var y = HeaderH + i * Cell;
        c.DrawText(design.Name, 14, y + 34, SKTextAlign.Left, titleFont, title);
        var ly = y + 58f;
        foreach (var l in Wrap(design.Tagline, font, LabelW - 28)) { c.DrawText(l, 14, ly, SKTextAlign.Left, font, text); ly += 17; }

        for (var j = 0; j < scenes.Length; j++)
            batch.Add(design, poses[j], new SKPoint(LabelW + j * Cell + Cell / 2f, y + Cell / 2f + 8), D);
        // Actual-size readability check, idle face (no upscaling). The 20 px compact bar has no halo.
        var x0 = LabelW + scenes.Length * Cell;
        batch.Add(design, poses[0], new SKPoint(x0 + SmallW / 2f, y + Cell / 2f + 8), 20, halo: false);
        batch.Add(design, poses[0], new SKPoint(x0 + SmallW + MidW / 2f, y + Cell / 2f + 8), 44);
    }
    batch.Flush(c);
    Save(bmp, Path.Combine(outDir, "overview.png"));
}

// ---- compact-zoom.png: 20 px renders blown up 4x with nearest-neighbour so every pixel is visible ----
var states = Enum.GetValues<CharacterState>();
var statePoses = states.Select(s => Run(e => e.SetAlertState(s), s == CharacterState.Sleeping ? 2.6 : 1.2)).ToArray();
{
    var zoomStates = new[] { CharacterState.Idle, CharacterState.Working, CharacterState.Approval, CharacterState.Error, CharacterState.Finished, CharacterState.Sleeping };
    const int Px = 20, Pad = 6, Zoom = 4, Tile = (Px + Pad * 2) * Zoom, Label = 250;
    using var bmp = new SKBitmap(Label + zoomStates.Length * Tile, 30 + designs.Length * Tile);
    using var c = new SKCanvas(bmp);
    c.Clear(SKColors.Black);
    for (var j = 0; j < zoomStates.Length; j++)
        c.DrawText(zoomStates[j] + " @20px", Label + j * Tile + Tile / 2f, 20, SKTextAlign.Center, headFont, text);
    for (var i = 0; i < designs.Length; i++)
    {
        c.DrawText(designs[i].Design.Name, 14, 30 + i * Tile + Tile / 2f + 5, SKTextAlign.Left, titleFont, title);
        for (var j = 0; j < zoomStates.Length; j++)
        {
            using var small = new SKBitmap(Px + Pad * 2, Px + Pad * 2);
            using (var sc = new SKCanvas(small))
            {
                sc.Clear(SKColors.Black);
                designs[i].Design.Draw(sc, statePoses[(int)zoomStates[j]], new SKPoint(small.Width / 2f, small.Height / 2f), Px);
            }
            using var img = SKImage.FromBitmap(small);
            c.DrawImage(img, SKRect.Create(Label + j * Tile, 30 + i * Tile, Tile, Tile), new SKSamplingOptions(SKFilterMode.Nearest));
        }
    }
    Save(bmp, Path.Combine(outDir, "compact-zoom.png"));
}

// ---- design-<key>.png ----
var shapes = Enum.GetValues<EyeShape>();
var badges = Enum.GetValues<BadgeKind>().Where(b => b != BadgeKind.None).ToArray();
foreach (var (key, design) in designs)
{
    const int Hero = 400, Small = 120, SmallD = 88, StateCell = 104, StateD = 60;
    const int cols = 6;
    var width = Math.Max(Hero + cols * Small + 20, 32 + states.Length * StateCell);
    var height = Math.Max(Hero, 3 * (Small + 18) + 50) + 46 + StateCell + 26;
    using var bmp = new SKBitmap(width, height);
    using var c = new SKCanvas(bmp);
    c.Clear(SKColors.Black);
    var batch = new Batch();
    c.DrawText(design.Name, 16, 28, SKTextAlign.Left, titleFont, title);
    c.DrawText(design.Tagline, 16, 48, SKTextAlign.Left, font, text);

    batch.Add(design, poses[0], new SKPoint(Hero / 2f, 60 + Hero / 2f - 10), 220);

    void Tile(CharacterPose pose, string label, int idx, int row)
    {
        var x = Hero + idx * Small;
        var y = 60 + row * (Small + 18);
        batch.Add(design, pose, new SKPoint(x + Small / 2f, y + Small / 2f), SmallD);
        c.DrawText(label, x + Small / 2f, y + Small + 12, SKTextAlign.Center, font, text);
    }

    for (var k = 0; k < shapes.Length; k++)
    {
        var pose = Run(_ => { }, 0.3);
        pose.Eyes = shapes[k]; pose.EyeOpen = 1; pose.Particles = []; pose.Yaw = 0; pose.Pitch = 0;
        pose.SpiralPhase = 0.8f;
        Tile(pose, shapes[k].ToString(), k % cols, k / cols);
    }
    for (var k = 0; k < badges.Length; k++)
    {
        var pose = Run(_ => { }, 0.3);
        pose.Badge = badges[k]; pose.BadgeScale = 1; pose.Particles = []; pose.Yaw = 0; pose.Pitch = 0;
        pose.BadgeColor = badges[k] switch
        {
            BadgeKind.Exclamation => StateConfig.For(CharacterState.Approval).Accent,
            BadgeKind.Question => StateConfig.For(CharacterState.Question).Accent,
            BadgeKind.RedDot => StateConfig.For(CharacterState.Error).Accent,
            BadgeKind.GreenDot => StateConfig.For(CharacterState.Finished).Accent,
            BadgeKind.OrangeDot => StateConfig.For(CharacterState.RateLimited).Accent,
            _ => StateConfig.For(CharacterState.Working).Accent,
        };
        Tile(pose, "badge " + badges[k], k, 2);
    }

    var sy = height - StateCell - 26;
    c.DrawLine(0, sy - 8, width, sy - 8, grid);
    for (var k = 0; k < states.Length; k++)
    {
        var x = 16 + k * StateCell;
        batch.Add(design, statePoses[k], new SKPoint(x + StateCell / 2f, sy + StateCell / 2f), StateD);
        c.DrawText(states[k].ToString(), x + StateCell / 2f, sy + StateCell + 14, SKTextAlign.Center, font, text);
    }
    batch.Flush(c);
    Save(bmp, Path.Combine(outDir, $"design-{key}.png"));
}

Console.WriteLine($"Wrote overview.png, compact-zoom.png and {designs.Length} design sheets to {outDir}");

static IEnumerable<string> Wrap(string s, SKFont font, float max)
{
    var line = "";
    foreach (var word in s.Split(' '))
    {
        var next = line.Length == 0 ? word : line + " " + word;
        if (font.MeasureText(next) > max && line.Length > 0) { yield return line; line = word; }
        else line = next;
    }
    if (line.Length > 0) yield return line;
}

static void Save(SKBitmap bmp, string path)
{
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    using var fs = File.Create(path);
    data.SaveTo(fs);
}

/// <summary>Draws every halo before any body, so halos fade out softly instead of being clipped or tinting neighbours.</summary>
sealed class Batch
{
    readonly List<(ICharacterDesign Design, CharacterPose Pose, SKPoint Center, float Size, bool Halo)> _items = [];

    public void Add(ICharacterDesign design, CharacterPose pose, SKPoint center, float size, bool halo = true) =>
        _items.Add((design, pose, center, size, halo));

    public void Flush(SKCanvas c)
    {
        foreach (var i in _items) if (i.Halo) i.Design.DrawHalo(c, i.Pose, i.Center, i.Size);
        foreach (var i in _items) i.Design.Draw(c, i.Pose, i.Center, i.Size);
        _items.Clear();
    }
}
