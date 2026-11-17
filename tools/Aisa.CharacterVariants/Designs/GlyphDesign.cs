using Aisa.Character;
using SkiaSharp;
using static Aisa.CharacterVariants.Kit;

namespace Aisa.CharacterVariants.Designs;

/// <summary>E. Notch-native minimalism: glowing eyes and a thin open ring, no body.</summary>
sealed class GlyphDesign : ICharacterDesign
{
    public string Name => "E  Glyph";
    public string Tagline => "Discreet and notch-native: two glowing eyes inside a thin open ring, no body at all.";

    public void DrawHalo(SKCanvas c, CharacterPose p, SKPoint center, float d) =>
        Halo(c, p, center, d, p.Glow, Math.Clamp(p.GlowOpacity, 0, 1) * 0.22f, 0.9f);

    public void Draw(SKCanvas c, CharacterPose p, SKPoint center, float d)
    {
        if (d <= 0) return;
        var px = 1f / d;
        var accent = Mix(p.Tint, White, 0.15f);
        var eyeCol = Mix(p.Tint, White, 0.7f);
        var eyes = new EyeStyle(C(eyeCol))
        {
            Spacing = 0.15f, Y = 0.02f, YawShift = 0.11f, PitchShift = 0.1f, PillW = 0.1f, PillH = 0.2f,
            Catchlight = false, Glow = C(p.Tint, 0.8f), GlowSigma = 0.04f,
        };

        BeginBody(c, p, center, d, pivotY: 0.3f);

        // Faint inner wash so the face has presence on pure black.
        Radial(c, new SKPoint(0, 0.04f), 0.44f, C(p.Tint, 0.16f), C(p.Tint, 0f));

        // Ring with a gap at the top; the gap is where the badge sits. It turns slightly with gaze.
        const float R = 0.43f;
        var turn = Math.Clamp(p.Yaw, -1, 1) * 18f;
        var ringW = Math.Max(0.04f, 1.25f * px);
        using (var sweep = SKShader.CreateSweepGradient(SKPoint.Empty,
                   [C(accent, 0.2f), C(accent, 1f), C(accent, 1f), C(accent, 0.2f)], [0.05f, 0.3f, 0.7f, 0.95f]))
        {
            c.Save();
            c.RotateDegrees(-60 + turn);
            Stroke.Shader = sweep;
            Stroke.StrokeWidth = ringW;
            c.DrawArc(new SKRect(-R, -R, R, R), 0, 300, false, Stroke);
            Stroke.Shader = null;
            c.Restore();
        }

        Blush(c, p, eyes, p.Blush, 0.25f, 0.13f);
        Eyes(c, p, d, eyes);
        Badge(c, p, d, 0.0f, -0.42f, SKColors.Black);
        c.Restore();
        Particles(c, p, center, d);
    }
}
