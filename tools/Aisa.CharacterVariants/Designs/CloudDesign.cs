using Aisa.Character;
using SkiaSharp;
using static Aisa.CharacterVariants.Kit;

namespace Aisa.CharacterVariants.Designs;

/// <summary>F. A lumpy little cloud; the mood colour tints its underside like a sunset.</summary>
sealed class CloudDesign : ICharacterDesign
{
    public string Name => "F  Cloud Puff";
    public string Tagline => "A soft, sleepy cloud of puffs with rosy cheeks; mood colour glows on its underside.";

    static readonly (float X, float Y, float R)[] Lumps =
    [
        (-0.27f, 0.1f, 0.22f), (0.28f, 0.12f, 0.2f), (-0.09f, -0.08f, 0.27f), (0.17f, -0.1f, 0.22f),
    ];
    static readonly SKPath Body = BuildBody();

    static SKPath BuildBody()
    {
        var path = new SKPath();
        path.AddRoundRect(SKRect.Create(-0.48f, 0.0f, 0.96f, 0.32f), 0.16f, 0.16f);
        foreach (var (x, y, r) in Lumps)
        {
            using var circle = new SKPath();
            circle.AddCircle(x, y, r);
            var merged = path.Op(circle, SKPathOp.Union)!;
            path.Dispose();
            path = merged;
        }
        return path;
    }

    public void DrawHalo(SKCanvas c, CharacterPose p, SKPoint center, float d) =>
        Halo(c, p, center, d, p.Glow, Math.Clamp(p.GlowOpacity, 0, 1) * 0.38f);

    public void Draw(SKCanvas c, CharacterPose p, SKPoint center, float d)
    {
        if (d <= 0) return;
        var px = 1f / d;
        var top = Mix(p.Body, White, 0.7f);
        var under = Mix(p.Body, p.Tint, 0.95f * p.TintAmount);
        var eyes = new EyeStyle(new SKColor(0x2E, 0x2B, 0x40))
        {
            Spacing = 0.15f, Y = 0.1f, YawShift = 0.12f, PitchShift = 0.07f, PillW = 0.085f, PillH = 0.15f, Scale = 0.85f,
        };

        BeginBody(c, p, center, d, pivotY: 0.32f);

        FillPathGradient(c, Body, new SKPoint(0, -0.35f), new SKPoint(0, 0.32f),
            [C(top), C(p.Body), C(under)], [0f, 0.5f, 1f]);
        c.Save();
        c.ClipPath(Body, antialias: true);
        // Each puff gets its own soft top highlight so the lumps read as separate.
        foreach (var (x, y, r) in Lumps)
        {
            c.Save();
            c.Scale(1f, 0.7f, x - r * 0.15f, y - r * 0.45f);
            Radial(c, new SKPoint(x - r * 0.15f, y - r * 0.45f), r * 0.75f,
                SKColors.White.WithAlpha(d < 30 ? (byte)90 : (byte)170), SKColors.White.WithAlpha(0));
            c.Restore();
        }
        c.Restore();
        Stroke.Color = SKColors.White;
        Stroke.StrokeWidth = Math.Max(0.028f, 1f * px);
        using (var g = SKShader.CreateLinearGradient(new SKPoint(0, -0.2f), new SKPoint(0, 0.32f),
                   [SKColors.Transparent, C(Mix(p.Tint, Black, 0.35f), 0.4f)], [0f, 1f], SKShaderTileMode.Clamp))
        {
            Stroke.Shader = g;
            c.DrawPath(Body, Stroke);
            Stroke.Shader = null;
        }

        Blush(c, p, eyes, 0.35f + p.Blush, 0.26f, 0.1f, 0.075f);
        Eyes(c, p, d, eyes);
        Mouth(c, p, d, eyes, 0.11f, 0.035f, new SKColor(0x2E, 0x2B, 0x40), MouthKind.Smile, minD: 40);
        Badge(c, p, d, -0.38f, -0.2f, SKColors.Black);
        c.Restore();
        Particles(c, p, center, d);
    }
}
