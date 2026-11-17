using Aisa.Character;
using SkiaSharp;
using static Aisa.CharacterVariants.Kit;

namespace Aisa.CharacterVariants.Designs;

/// <summary>C. A warm bean with two rounded ears and a tail curl.</summary>
sealed class BeanDesign : ICharacterDesign
{
    public string Name => "C  Kitten Bean";
    public string Tagline => "A warm, wide bean with soft ears and a tail flick; ears perk, droop and flatten with mood.";

    static readonly SKPath Body = Superellipse(0.47f, 0.37f, 2.3f, 0, 0.07f);
    static readonly Rgb Cream = Rgb.FromHex(0xFFE7CF);
    static readonly Rgb InnerEar = Rgb.FromHex(0xFFA9BE);

    public void DrawHalo(SKCanvas c, CharacterPose p, SKPoint center, float d) =>
        Halo(c, p, center, d, p.Glow, Math.Clamp(p.GlowOpacity, 0, 1) * 0.4f);

    public void Draw(SKCanvas c, CharacterPose p, SKPoint center, float d)
    {
        if (d <= 0) return;
        var px = 1f / d;
        var fur = Mix(p.Body, Cream, 0.8f);
        var under = Mix(fur, p.Tint, 0.6f * p.TintAmount);
        var rim = C(Mix(Mix(fur, p.Tint, 0.4f), Black, 0.35f), 0.35f);
        var eyes = new EyeStyle(new SKColor(0x2B, 0x20, 0x26))
        {
            Spacing = 0.18f, Y = 0.06f, YawShift = 0.12f, PitchShift = 0.08f, PillW = 0.1f, PillH = 0.125f, Scale = 0.95f,
        };

        BeginBody(c, p, center, d, pivotY: 0.44f);

        // Ear pose from expression: perk when surprised, droop when tired/sleepy, flatten when annoyed.
        var earAngle = p.Eyes switch
        {
            EyeShape.Wide or EyeShape.Star or EyeShape.Heart => -0.12f,
            EyeShape.Tired or EyeShape.Closed => 0.42f,
            EyeShape.Annoyed or EyeShape.Flat => 0.75f,
            EyeShape.Spiral => 0.3f,
            _ => 0.12f,
        };
        var gazeTwist = Math.Clamp(p.Yaw, -1, 1) * 0.12f;

        // Tail: a short curl from the lower right that flicks with motion.
        var flick = Math.Clamp(p.OffsetX * 4f + p.Roll * 0.8f + p.Yaw * 0.15f, -0.5f, 0.5f);
        using (var tail = new SKPath())
        {
            // Ends well clear of the body so it reads as a tail, not a mug handle.
            tail.MoveTo(0.3f, 0.3f);
            tail.CubicTo(0.56f, 0.36f, 0.66f + flick * 0.08f, 0.2f, 0.62f + flick * 0.14f, 0.02f);
            tail.QuadTo(0.6f + flick * 0.16f, -0.08f, 0.68f + flick * 0.18f, -0.1f);
            Stroke.Shader = null;
            Stroke.Color = rim;
            Stroke.StrokeWidth = Math.Max(0.105f, 3.4f * px);
            c.DrawPath(tail, Stroke);
            Stroke.Color = C(Mix(fur, p.Tint, 0.3f * p.TintAmount));
            Stroke.StrokeWidth = Math.Max(0.075f, 2.2f * px);
            c.DrawPath(tail, Stroke);
        }

        foreach (var side in new[] { -1f, 1f })
        {
            c.Save();
            c.Translate(side * 0.27f, -0.2f);
            c.RotateRadians(side * earAngle + gazeTwist);
            using var ear = new SKPath();
            ear.MoveTo(-0.15f, 0.06f);
            ear.CubicTo(-0.14f, -0.12f, -0.07f, -0.24f, 0.0f, -0.25f);
            ear.CubicTo(0.07f, -0.24f, 0.14f, -0.12f, 0.15f, 0.06f);
            ear.Close();
            FillPathGradient(c, ear, new SKPoint(0, -0.25f), new SKPoint(0, 0.06f), [C(Mix(fur, White, 0.3f)), C(fur)], [0f, 1f]);
            Stroke.Color = rim; Stroke.StrokeWidth = Math.Max(0.025f, 0.9f * px);
            c.DrawPath(ear, Stroke);
            if (d >= 26)
            {
                c.Scale(0.55f, 0.6f, 0, -0.02f);
                Fill.Color = C(Mix(InnerEar, p.Tint, 0.35f * p.TintAmount), 0.85f);
                c.DrawPath(ear, Fill);
            }
            c.Restore();
        }

        FillPathGradient(c, Body, new SKPoint(0, -0.3f), new SKPoint(0, 0.44f),
            [C(Mix(fur, White, 0.4f)), C(fur), C(under)], [0f, 0.5f, 1f]);
        c.Save();
        c.ClipPath(Body, antialias: true);
        c.Save();
        c.Scale(1f, 0.4f, -0.12f, -0.2f);
        Radial(c, new SKPoint(-0.12f, -0.2f), 0.3f, SKColors.White.WithAlpha(d < 30 ? (byte)80 : (byte)150), SKColors.White.WithAlpha(0));
        c.Restore();
        Stroke.Color = rim; Stroke.StrokeWidth = Math.Max(0.03f, 1.1f * px);
        c.DrawPath(Body, Stroke);
        c.Restore();

        Blush(c, p, eyes, 0.3f + p.Blush, 0.31f, 0.1f, 0.08f);
        Eyes(c, p, d, eyes);
        Mouth(c, p, d, eyes, 0.11f, 0.045f, new SKColor(0x2B, 0x20, 0x26), MouthKind.Cat, minD: 40);
        Badge(c, p, d, -0.4f, -0.3f, SKColors.Black);
        c.Restore();
        Particles(c, p, center, d);
    }
}
