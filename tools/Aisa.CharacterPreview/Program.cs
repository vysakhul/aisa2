using Aisa.Character;
using Aisa.Character.Rendering;
using SkiaSharp;

// Renders every state, emote and interaction moment to /tmp/aisa-previews plus a labelled contact sheet.
var outDir = args.Length > 0 ? args[0] : "/tmp/aisa-previews";
Directory.CreateDirectory(outDir);

const float Diameter = 88;
const int Cell = 220, LabelH = 30;
const double Frame = 1.0 / 60;

var scenes = new List<(string Name, Action<CharacterEngine> Setup, double Seconds)>
{
    ("idle", _ => { }, 1.0),
    ("idle-look-left", e => e.SetGaze(-300, 40), 1.0),
    ("idle-blink", _ => { }, -1),
    ("working", e => e.SetAlertState(CharacterState.Working), 1.0),
    ("thinking", e => e.BeginOverride(CharacterState.Thinking), 1.0),
    ("searching", e => e.BeginOverride(CharacterState.Searching), 1.3),
    ("approval", e => e.SetAlertState(CharacterState.Approval), 1.3),
    ("question", e => e.SetDialogState(CharacterState.Question), 1.0),
    ("error", e => e.Flash(CharacterState.Error, TimeSpan.FromSeconds(1.2)), 0.11),
    ("finished", e => e.Flash(CharacterState.Finished, TimeSpan.FromSeconds(1.6)), 0.5),
    ("finished-settled", e => e.Flash(CharacterState.Finished, TimeSpan.FromSeconds(5)), 1.4),
    ("rate-limited", e => e.SetAlertState(CharacterState.RateLimited), 1.6),
    ("sleeping", e => e.SetAlertState(CharacterState.Sleeping), 2.6),
    ("emote-love", e => e.PlayEmote(Emote.Love), 0.6),
    ("emote-surprised", e => e.PlayEmote(Emote.Surprised), 0.2),
    ("emote-proud", e => e.PlayEmote(Emote.Proud), 0.45),
    ("emote-wink", e => e.PlayEmote(Emote.Wink), 0.5),
    ("emote-yawn", e => e.PlayEmote(Emote.Yawn), 0.65),
    ("emote-happy", e => e.PlayEmote(Emote.Happy), 0.5),
    ("emote-annoyed", e => e.PlayEmote(Emote.Annoyed), 0.5),
    ("hover", e => e.UpdateFacePointer(true, 0, 0), 0.5),
    ("slap-squash", e => e.Slap(), 0.07),
    ("slap-wobble", e => e.Slap(), 0.22),
    ("dizzy-roll", Dizzy, 0.55),
    ("dizzy-sway", Dizzy, 2.0),
    ("dizzy-recovered", Dizzy, 3.6),
};

static void Dizzy(CharacterEngine e)
{
    e.Slap(); e.Step(0.1); e.Slap(); e.Step(0.1); e.Slap();
}

using var typeface = File.Exists("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
    ? SKTypeface.FromFile("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
    : SKTypeface.Default;
using var font = new SKFont(typeface, 14);
using var labelPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0xB8, 0xB8, 0xC4) };

const int Cols = 6;
var extraRows = 1;
var rows = (scenes.Count + Cols - 1) / Cols + extraRows;
using var sheet = new SKBitmap(Cols * Cell, rows * (Cell + LabelH));
using var sheetCanvas = new SKCanvas(sheet);
sheetCanvas.Clear(SKColors.Black);

for (var i = 0; i < scenes.Count; i++)
{
    var (name, setup, seconds) = scenes[i];
    var engine = new CharacterEngine(new Random(7));
    engine.SetExpanded(true);
    setup(engine);
    if (seconds < 0)
    {
        // Run until mid-blink.
        for (var t = 0; t < 600 && engine.Pose.EyeOpen > 0.3f; t++) engine.Step(Frame);
    }
    else
    {
        for (double t = 0; t < seconds - 1e-9; t += Frame) engine.Step(Frame);
    }

    using var bmp = new SKBitmap(Cell, Cell);
    using (var c = new SKCanvas(bmp))
    {
        c.Clear(SKColors.Black);
        var center = new SKPoint(Cell / 2f, Cell / 2f + 14);
        CharacterRenderer.DrawHalo(c, engine.Pose, center, Diameter);
        CharacterRenderer.Draw(c, engine.Pose, center, Diameter);
    }
    Save(bmp, Path.Combine(outDir, $"{name}.png"));

    var x = i % Cols * Cell; var y = i / Cols * (Cell + LabelH);
    sheetCanvas.DrawBitmap(bmp, x, y);
    sheetCanvas.DrawText(name, x + Cell / 2f, y + Cell + 18, SKTextAlign.Center, font, labelPaint);
}

// Size ramp: compact (20) to 2x expanded (88).
{
    var y = (rows - 1) * (Cell + LabelH);
    var ramp = new (CharacterState State, float D)[]
    {
        (CharacterState.Idle, 20), (CharacterState.Working, 20), (CharacterState.Approval, 20), (CharacterState.Finished, 20),
        (CharacterState.Error, 20), (CharacterState.Question, 20), (CharacterState.Working, 32), (CharacterState.Working, 44),
        (CharacterState.Approval, 44), (CharacterState.Finished, 44), (CharacterState.Working, 66), (CharacterState.Working, 88),
    };
    var x = 30f;
    foreach (var (state, d) in ramp)
    {
        var engine = new CharacterEngine(new Random(3));
        engine.SetAlertState(state);
        for (var t = 0; t < 120; t++) engine.Step(Frame);
        var center = new SKPoint(x + d / 2, y + Cell / 2f);
        if (d >= 44) CharacterRenderer.DrawHalo(sheetCanvas, engine.Pose, center, d);
        CharacterRenderer.Draw(sheetCanvas, engine.Pose, center, d);
        x += d + (d < 30 ? 26 : 34);
    }
    sheetCanvas.DrawText("sizes: 20 px compact … 44 px expanded … 88 px (2×)", Cols * Cell / 2f, y + Cell + 18, SKTextAlign.Center, font, labelPaint);
}

var sheetPath = Path.Combine(outDir, "contact-sheet.png");
Save(sheet, sheetPath);
Console.WriteLine($"Wrote {scenes.Count} previews and {sheetPath}");

static void Save(SKBitmap bmp, string path)
{
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    using var fs = File.Create(path);
    data.SaveTo(fs);
}
