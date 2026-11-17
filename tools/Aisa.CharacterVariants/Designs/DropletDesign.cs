using Aisa.Character;
using SkiaSharp;
using static Aisa.CharacterVariants.Kit;

namespace Aisa.CharacterVariants.Designs;

/// <summary>D. A glossy jelly droplet whose tip bends with motion and gaze.</summary>
sealed class DropletDesign : ICharacterDesign
{
    public string Name => "D  Jelly Drop";
    public string Tagline => "A glossy, see-through droplet tinted by mood; the tip sways and the body squishes extra.";

    public void DrawHalo(SKCanvas c, CharacterPose p, SKPoint center, float d) =>
        Halo(c, p, center, d, p.Glow, Math.Clamp(p.GlowOpacity, 0, 1) * 0.4f);

    public void Draw(SKCanvas c, CharacterPose p, SKPoint center, float d)
    {
        if (d <= 0) return;
        var px = 1f / d;
        var jelly = Mix(Mix(p.Tint, White, 0.3f), p.Body, 0.25f);
        var deep = Mix(p.Tint, Black, 0.25f);
        var eyes = new EyeStyle(C(Mix(p.Tint, Black, 0.85f)))
        {
            Spacing = 0.15f, Y = 0.14f, YawShift = 0.12f, PitchShift = 0.08f, PillW = 0.095f, PillH = 0.17f, Scale = 0.9f,
        };

        BeginBody(c, p, center, d, pivotY: 0.5f, squashGain: 1.6f);

        var bend = Math.Clamp(-p.OffsetX * 2.2f + p.Yaw * 0.09f - p.Roll * 0.15f, -0.2f, 0.2f);
        using var body = new SKPath();
        var tip = new SKPoint(bend, -0.56f);
        body.MoveTo(tip);
        body.CubicTo(bend * 0.6f + 0.06f, -0.4f, 0.41f, -0.2f, 0.41f, 0.12f);
        body.CubicTo(0.41f, 0.36f, 0.23f, 0.5f, 0, 0.5f);
        body.CubicTo(-0.23f, 0.5f, -0.41f, 0.36f, -0.41f, 0.12f);
        body.CubicTo(-0.41f, -0.2f, bend * 0.6f - 0.06f, -0.4f, tip.X, tip.Y);
        body.Close();

        FillPathGradient(c, body, new SKPoint(0, -0.56f), new SKPoint(0, 0.5f),
            [C(Mix(jelly, White, 0.45f)), C(jelly), C(deep)], [0f, 0.5f, 1f]);
        c.Save();
        c.ClipPath(body, antialias: true);
        // Refracted light pooling inside the bottom.
        c.Save();
        c.Scale(1f, 0.5f, 0.04f, 0.38f);
        Radial(c, new SKPoint(0.04f, 0.38f), 0.3f, C(Mix(jelly, White, 0.6f), 0.75f), C(jelly, 0f));
        c.Restore();
        Stroke.Color = C(Mix(deep, Black, 0.3f), 0.45f);
        Stroke.StrokeWidth = Math.Max(0.03f, 1.1f * px);
        c.DrawPath(body, Stroke);
        c.Restore();

        // Long glossy streak along the upper-left edge plus a bead.
        using (var streak = new SKPath())
        {
            streak.MoveTo(-0.3f, 0.08f);
            streak.CubicTo(-0.3f, -0.12f, -0.2f, -0.26f, -0.08f + bend * 0.5f, -0.38f);
            Stroke.Color = SKColors.White.WithAlpha(d < 30 ? (byte)120 : (byte)200);
            Stroke.StrokeWidth = Math.Max(0.055f, 1.4f * px);
            c.DrawPath(streak, Stroke);
        }
        if (d >= 30)
        {
            Fill.Color = SKColors.White.WithAlpha(220);
            c.DrawCircle(-0.3f, 0.19f, 0.028f, Fill);
        }

        Blush(c, p, eyes, p.Blush, 0.25f, 0.1f, 0.075f);
        Eyes(c, p, d, eyes);
        Mouth(c, p, d, eyes, 0.13f, 0.04f, C(Mix(p.Tint, Black, 0.85f)), MouthKind.Smile, minD: 40);
        Badge(c, p, d, -0.33f, -0.22f, SKColors.Black);
        c.Restore();
        Particles(c, p, center, d);
    }
}
