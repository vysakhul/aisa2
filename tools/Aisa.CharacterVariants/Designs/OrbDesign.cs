using Aisa.Character;
using SkiaSharp;
using static Aisa.CharacterVariants.Kit;

namespace Aisa.CharacterVariants.Designs;

/// <summary>A. A sphere of light circled by a thin ring of light; the mood colour is the light itself.</summary>
sealed class OrbDesign : ICharacterDesign
{
    public string Name => "A  Halo Orb";
    public string Tagline => "A little sphere of light circled by a tilted ring with a riding spark; mood colour is its glow.";

    static Rgb Light(CharacterPose p) => Mix(p.Tint, White, 0.12f);

    public void DrawHalo(SKCanvas c, CharacterPose p, SKPoint center, float d) =>
        // It is a light source, so it always glows a little.
        Halo(c, p, center, d, Light(p), 0.16f + 0.26f * Math.Clamp(p.GlowOpacity, 0, 1), 0.95f);

    public void Draw(SKCanvas c, CharacterPose p, SKPoint center, float d)
    {
        if (d <= 0) return;
        var px = 1f / d;
        var light = Light(p);
        var deep = Mix(p.Tint, Black, 0.2f);
        var eyes = new EyeStyle(C(Mix(p.Tint, Black, 0.84f)))
        {
            Spacing = 0.15f, Y = -0.01f, YawShift = 0.12f, PitchShift = 0.1f, PillW = 0.095f, PillH = 0.175f, Scale = 0.92f,
        };

        BeginBody(c, p, center, d, pivotY: 0.4f);
        const float R = 0.38f;
        var body = new SKPoint(0, 0.02f);

        // Ring tilts against yaw and opens up with pitch, giving a sense of depth.
        var ringTilt = -16f - Math.Clamp(p.Yaw, -1, 1) * 10f;
        var ry = 0.12f + Math.Clamp(-p.Pitch, -1, 1) * 0.04f;
        var ring = new SKRect(-0.56f, body.Y - ry, 0.56f, body.Y + ry);
        var ringW = Math.Max(0.032f, 1.15f * px);
        using var ringBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.03f, 1.5f * px));

        void RingHalf(float start, float alpha)
        {
            c.Save();
            c.RotateDegrees(ringTilt, 0, body.Y);
            Stroke.Shader = null;
            Stroke.MaskFilter = ringBlur;
            Stroke.Color = C(light, 0.8f * alpha);
            Stroke.StrokeWidth = ringW * 2.4f;
            c.DrawArc(ring, start, 180, false, Stroke);
            Stroke.MaskFilter = null;
            Stroke.Color = C(Mix(light, White, 0.55f), alpha);
            Stroke.StrokeWidth = ringW;
            c.DrawArc(ring, start, 180, false, Stroke);
            c.Restore();
        }

        RingHalf(180, 0.55f);

        // Corona just outside the sphere.
        Radial(c, body, R + 0.12f, C(light, 0.55f), C(light, 0f), R / (R + 0.12f) * 0.86f);

        // Sphere: hot white core fading into the mood colour.
        using (var g = SKShader.CreateRadialGradient(new SKPoint(-0.05f, -0.06f), R * 1.12f,
                   [SKColors.White, C(Mix(light, White, 0.55f)), C(light), C(deep)], [0f, 0.35f, 0.78f, 1f], SKShaderTileMode.Clamp))
        {
            Shade(g);
            c.DrawCircle(body, R, Fill);
            Fill.Shader = null;
        }
        c.Save();
        c.Scale(1f, 0.55f, -0.14f, -0.2f);
        Radial(c, new SKPoint(-0.14f, -0.2f), 0.12f, SKColors.White.WithAlpha(d < 30 ? (byte)120 : (byte)210), SKColors.White.WithAlpha(0));
        c.Restore();

        Blush(c, p, eyes, p.Blush, 0.25f, 0.11f, 0.075f);
        Eyes(c, p, d, eyes);

        RingHalf(0, 1f);

        // A spark riding the front of the ring.
        if (d >= 30)
        {
            // Front-left of the ring, clear of the eyes.
            var a = (150f - Math.Clamp(p.Yaw, -1, 1) * 15f + p.SpiralPhase * 57.3f) * MathF.PI / 180f;
            var local = new SKPoint(MathF.Cos(a) * 0.56f, body.Y + MathF.Sin(a) * ry);
            var t = ringTilt * MathF.PI / 180f;
            var m = new SKPoint(local.X * MathF.Cos(t) - (local.Y - body.Y) * MathF.Sin(t),
                body.Y + local.X * MathF.Sin(t) + (local.Y - body.Y) * MathF.Cos(t));
            Radial(c, m, 0.07f, C(light, 0.9f), C(light, 0f));
            Fill.Color = SKColors.White;
            c.Save();
            c.Translate(m);
            c.Scale(0.045f);
            c.DrawPath(SparkPath, Fill);
            c.Restore();
        }

        Badge(c, p, d, -0.3f, -0.3f, SKColors.Black);
        c.Restore();
        Particles(c, p, center, d);
    }
}
