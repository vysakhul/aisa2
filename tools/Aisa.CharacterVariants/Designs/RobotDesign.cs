using Aisa.Character;
using SkiaSharp;
using static Aisa.CharacterVariants.Kit;

namespace Aisa.CharacterVariants.Designs;

/// <summary>B. A tiny rounded robot head with a glass visor and LED face.</summary>
sealed class RobotDesign : ICharacterDesign
{
    public string Name => "B  Visor Bot";
    public string Tagline => "A pocket robot: soft shell, dark glass visor, LED eyes and ear lights in the mood colour.";

    static readonly SKPath Shell = new SKPath();
    static readonly SKPath Visor = new SKPath();

    static RobotDesign()
    {
        Shell.AddRoundRect(SKRect.Create(-0.45f, -0.36f, 0.9f, 0.78f), 0.26f, 0.26f);
        Visor.AddRoundRect(SKRect.Create(-0.35f, -0.25f, 0.7f, 0.5f), 0.17f, 0.17f);
    }

    static Rgb Led(CharacterPose p) => Mix(p.Tint, White, 0.38f);

    public void DrawHalo(SKCanvas c, CharacterPose p, SKPoint center, float d) =>
        Halo(c, p, center, d, p.Glow, Math.Clamp(p.GlowOpacity, 0, 1) * 0.38f);

    public void Draw(SKCanvas c, CharacterPose p, SKPoint center, float d)
    {
        if (d <= 0) return;
        var px = 1f / d;
        // Brighter LEDs when tiny, where the glow has no room to carry the colour.
        var led = d < 30 ? Mix(p.Tint, White, 0.6f) : Led(p);
        var eyes = new EyeStyle(C(led))
        {
            Spacing = 0.15f, Y = -0.02f, YawShift = 0.08f, PitchShift = 0.06f, PillW = 0.1f, PillH = 0.16f, Corner = 0.38f,
            Scale = 0.9f, Catchlight = false, Glow = C(p.Tint, 0.95f), GlowSigma = 0.035f,
        };

        BeginBody(c, p, center, d, pivotY: 0.42f);

        // Ear lights.
        foreach (var side in new[] { -1f, 1f })
        {
            var e = new SKRect(side < 0 ? -0.53f : 0.43f, -0.08f, side < 0 ? -0.43f : 0.53f, 0.16f);
            Fill.Color = C(Mix(p.Body, Black, 0.35f));
            c.DrawRoundRect(e, 0.05f, 0.05f, Fill);
            var dot = new SKPoint(side * 0.505f, 0.04f);
            Radial(c, dot, 0.09f, C(p.Tint, 0.6f), C(p.Tint, 0f));
            Fill.Color = C(led);
            c.DrawRoundRect(SKRect.Create(dot.X - 0.022f, dot.Y - 0.075f, 0.044f, 0.15f), 0.022f, 0.022f, Fill);
        }

        // Shell.
        var shellTop = Mix(p.Body, White, 0.6f);
        var shellBottom = Mix(p.Body, p.Tint, 0.45f * p.TintAmount);
        FillPathGradient(c, Shell, new SKPoint(0, -0.36f), new SKPoint(0, 0.42f),
            [C(shellTop), C(p.Body), C(shellBottom)], [0f, 0.45f, 1f]);
        c.Save();
        c.ClipPath(Shell, antialias: true);
        c.Save();
        c.Scale(1f, 0.35f, -0.12f, -0.33f);
        Radial(c, new SKPoint(-0.12f, -0.33f), 0.3f, SKColors.White.WithAlpha(d < 30 ? (byte)90 : (byte)190), SKColors.White.WithAlpha(0));
        c.Restore();
        Stroke.Color = C(Mix(p.Tint, Black, 0.4f), 0.35f);
        Stroke.StrokeWidth = Math.Max(0.03f, 1.1f * px);
        c.DrawPath(Shell, Stroke);
        c.Restore();

        // Visor glass.
        FillPathGradient(c, Visor, new SKPoint(0, -0.25f), new SKPoint(0, 0.25f),
            [new SKColor(0x2A, 0x28, 0x3A), new SKColor(0x12, 0x11, 0x1C)], [0f, 1f]);
        c.Save();
        c.ClipPath(Visor, antialias: true);
        Radial(c, new SKPoint(0, 0.3f), 0.42f, C(p.Tint, 0.22f), C(p.Tint, 0f));
        if (d >= 60)
        {
            Stroke.Color = SKColors.Black.WithAlpha(60);
            Stroke.StrokeWidth = 0.6f * px;
            for (var y = -0.24f; y < 0.25f; y += 2.2f * px) c.DrawLine(-0.36f, y, 0.36f, y, Stroke);
        }
        // Diagonal glass sheen.
        using (var sheen = new SKPath())
        {
            sheen.MoveTo(-0.36f, -0.06f); sheen.LineTo(-0.1f, -0.26f); sheen.LineTo(0.04f, -0.26f); sheen.LineTo(-0.36f, 0.05f); sheen.Close();
            Fill.Color = SKColors.White.WithAlpha(d < 30 ? (byte)14 : (byte)26);
            c.DrawPath(sheen, Fill);
        }
        Blush(c, p, eyes, p.Blush, 0.24f, 0.11f, 0.07f);
        Eyes(c, p, d, eyes);
        Mouth(c, p, d, eyes, 0.14f, 0.045f, C(led), MouthKind.Led, minD: 40, glow: C(p.Tint, 0.9f));
        c.Restore();
        Stroke.Color = SKColors.Black.WithAlpha(90);
        Stroke.StrokeWidth = Math.Max(0.02f, 0.8f * px);
        c.DrawPath(Visor, Stroke);

        Badge(c, p, d, -0.38f, -0.34f, SKColors.Black);
        c.Restore();
        Particles(c, p, center, d);
    }
}
