using Aisa.Character;
using SkiaSharp;

namespace Aisa.CharacterVariants;

/// <summary>How a design draws its eyes. Geometry is in unit (face-diameter) space.</summary>
sealed record EyeStyle(SKColor Ink)
{
    public float Spacing { get; init; } = 0.175f;
    public float Y { get; init; } = 0.02f;
    public float YawShift { get; init; } = 0.13f;
    public float PitchShift { get; init; } = 0.12f;
    public float PillW { get; init; } = 0.105f;
    public float PillH { get; init; } = 0.21f;
    /// <summary>Corner radius as a fraction of the shorter side; 0.5 = fully round.</summary>
    public float Corner { get; init; } = 0.5f;
    /// <summary>Scales the non-pill glyphs (arcs, hearts, stars...).</summary>
    public float Scale { get; init; } = 1f;
    public bool Catchlight { get; init; } = true;
    /// <summary>Light-emitting eyes get a blurred underglow in this colour.</summary>
    public SKColor? Glow { get; init; }
    public float GlowSigma { get; init; } = 0.03f;
}

enum MouthKind { None, Smile, Cat, Led }

static class Kit
{
    public static readonly SKColor HeartRed = new(0xFF, 0x4F, 0x86);
    public static readonly SKColor StarGold = new(0xFF, 0xC9, 0x3C);
    public static readonly SKColor StarEdge = new(0xD9, 0x86, 0x10);
    public static readonly SKColor BlushPink = new(0xFF, 0x6B, 0x9A);
    public static readonly Rgb White = new(1, 1, 1);
    public static readonly Rgb Black = new(0, 0, 0);

    public static readonly SKPath HeartPath = Heart();
    public static readonly SKPath StarPath = Star(5, 1f, 0.47f);
    public static readonly SKPath SparkPath = Star(4, 1f, 0.28f);
    public static readonly SKPath SpiralPath = Spiral();
    public static readonly SKPath ZPath = ZGlyph();
    public static readonly SKPath DropPath = Drop();

    public static readonly SKPaint Fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    public static readonly SKPaint Stroke = new()
    {
        IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    };
    public static readonly SKPaint Additive = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };

    // ---- Colour ----

    public static Rgb Mix(Rgb a, Rgb b, float t) => Rgb.Lerp(a, b, Math.Clamp(t, 0, 1));
    public static SKColor C(Rgb c) => new(B8(c.R), B8(c.G), B8(c.B));
    public static SKColor C(Rgb c, float alpha) => new(B8(c.R), B8(c.G), B8(c.B), A(alpha));
    public static byte A(float a) => (byte)Math.Clamp(MathF.Round(a * 255), 0, 255);
    static byte B8(float v) => (byte)Math.Clamp(MathF.Round(v * 255), 0, 255);

    // ---- Transforms ----

    /// <summary>Enters unit space with offset, tilt/roll and squash applied; pair with <c>canvas.Restore()</c>.</summary>
    public static void BeginBody(SKCanvas c, CharacterPose p, SKPoint center, float d, float pivotY = 0.32f, float squashGain = 1f)
    {
        c.Save();
        c.Translate(center.X + p.OffsetX * d, center.Y + p.OffsetY * d);
        c.Scale(d);
        c.RotateRadians(p.Tilt + p.Roll);
        c.Translate(0, pivotY);
        c.Scale(1 + (p.ScaleX - 1) * squashGain, 1 + (p.ScaleY - 1) * squashGain);
        c.Translate(0, -pivotY);
    }

    public static void Halo(SKCanvas c, CharacterPose p, SKPoint center, float d, Rgb color, float opacity, float radius = 1.1f)
    {
        if (opacity <= 0.001f || d <= 0) return;
        var at = new SKPoint(center.X + p.OffsetX * d * 0.6f, center.Y + p.OffsetY * d * 0.6f);
        var r = d * radius;
        var col = C(color);
        using var shader = SKShader.CreateRadialGradient(at, r,
            [col.WithAlpha(A(opacity)), col.WithAlpha(A(opacity * 0.72f)), col.WithAlpha(A(opacity * 0.3f)), col.WithAlpha(A(opacity * 0.08f)), col.WithAlpha(0)],
            [0f, 0.32f, 0.58f, 0.8f, 1f], SKShaderTileMode.Clamp);
        Shade(shader);
        c.DrawCircle(at, r, Fill);
        Fill.Shader = null;
    }

    /// <summary>Fills a circle with a radial gradient; handy for highlights and pools.</summary>
    public static void Radial(SKCanvas c, SKPoint at, float r, SKColor inner, SKColor outer, float innerStop = 0f)
    {
        using var g = SKShader.CreateRadialGradient(at, r, [inner, outer], [innerStop, 1f], SKShaderTileMode.Clamp);
        Shade(g);
        c.DrawCircle(at, r, Fill);
        Fill.Shader = null;
    }

    /// <summary>Paint alpha multiplies a shader, so shaded fills must start from opaque white.</summary>
    public static void Shade(SKShader shader)
    {
        Fill.Shader = shader;
        Fill.Color = SKColors.White;
    }

    public static void FillPathGradient(SKCanvas c, SKPath path, SKPoint from, SKPoint to, SKColor[] colors, float[] stops)
    {
        using var g = SKShader.CreateLinearGradient(from, to, colors, stops, SKShaderTileMode.Clamp);
        Shade(g);
        c.DrawPath(path, Fill);
        Fill.Shader = null;
    }

    /// <summary>Gaze-shifted centre of the face features.</summary>
    public static SKPoint FaceOffset(CharacterPose p, EyeStyle s) =>
        new(Math.Clamp(p.Yaw, -1, 1) * s.YawShift, s.Y + Math.Clamp(p.Pitch, -1, 1) * s.PitchShift);

    // ---- Blush ----

    public static void Blush(SKCanvas c, CharacterPose p, EyeStyle s, float amount, float spread, float dy, float r = 0.085f)
    {
        if (amount <= 0.01f) return;
        var f = FaceOffset(p, s);
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var cx = f.X * 0.65f + side * spread; var cy = f.Y + dy;
            using var g = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r,
                [BlushPink.WithAlpha(A(0.75f * Math.Min(1, amount))), BlushPink.WithAlpha(0)], [0.2f, 1f], SKShaderTileMode.Clamp);
            Shade(g);
            c.Save();
            c.Scale(1f, 0.6f, cx, cy);
            c.DrawCircle(cx, cy, r, Fill);
            c.Restore();
        }
        Fill.Shader = null;
    }

    // ---- Eyes ----

    public static void Eyes(SKCanvas c, CharacterPose p, float d, EyeStyle s)
    {
        var px = 1f / d;
        if (s.Glow is { } glow)
        {
            using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(s.GlowSigma, 1.2f * px));
            Fill.MaskFilter = blur; Stroke.MaskFilter = blur;
            EyesPass(c, p, d, px, s, glow, glowPass: true);
            Fill.MaskFilter = null; Stroke.MaskFilter = null;
        }
        EyesPass(c, p, d, px, s, s.Ink, glowPass: false);
    }

    static void EyesPass(SKCanvas c, CharacterPose p, float d, float px, EyeStyle s, SKColor ink, bool glowPass)
    {
        var yaw = Math.Clamp(p.Yaw, -1, 1);
        var f = FaceOffset(p, s);
        var spacing = s.Spacing * (1 - 0.12f * MathF.Abs(yaw));
        var open = Math.Clamp(p.EyeOpen, 0, 1);
        var k = s.Scale;
        var line = Math.Max(0.045f * k, 1.4f * px);

        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var near = 1 - 0.1f * MathF.Max(0, side * yaw);
            c.Save();
            c.Translate(f.X + side * spacing, f.Y);
            c.Scale(p.EyeScale * near, p.EyeScale);

            var shape = p.Eyes;
            if (shape == EyeShape.Wink) shape = side < 0 ? EyeShape.Pill : EyeShape.Happy;

            Stroke.Shader = null; Fill.Shader = null;
            Stroke.Color = ink; Stroke.StrokeWidth = line;
            Fill.Color = ink;
            switch (shape)
            {
                case EyeShape.Pill: Pill(c, s, s.PillW, s.PillH, open, !glowPass && s.Catchlight && d >= 30, px); break;
                case EyeShape.Wide: Pill(c, s, s.PillW * 1.19f, s.PillH * 1.14f, open, !glowPass && s.Catchlight && d >= 24, px); break;
                case EyeShape.Dot:
                    c.DrawOval(0, 0, Math.Max(0.05f * k, 1.1f * px), Math.Max(0.05f * k * Math.Max(0.15f, open), 0.7f * px), Fill);
                    break;
                case EyeShape.Flat:
                    var fh = Math.Max(0.045f * k, 1.4f * px);
                    c.DrawRoundRect(SKRect.Create(-0.065f * k, -fh / 2 + 0.02f * k, 0.13f * k, fh), fh / 2, fh / 2, Fill);
                    break;
                case EyeShape.Happy:
                    using (var path = new SKPath())
                    {
                        path.MoveTo(-0.06f * k, 0.035f * k);
                        path.QuadTo(0, -0.075f * k, 0.06f * k, 0.035f * k);
                        c.DrawPath(path, Stroke);
                    }
                    break;
                case EyeShape.Closed:
                    using (var path = new SKPath())
                    {
                        path.MoveTo(-0.062f * k, 0);
                        path.QuadTo(0, 0.075f * k, 0.062f * k, 0);
                        c.DrawPath(path, Stroke);
                    }
                    break;
                case EyeShape.Tired:
                    c.Save();
                    c.ClipRect(SKRect.Create(-0.12f * k, 0.01f * k, 0.24f * k, 0.25f * k));
                    Pill(c, s, s.PillW, s.PillH * 0.9f, open, false, px);
                    c.Restore();
                    Stroke.StrokeWidth = line * 0.8f;
                    c.DrawLine(-0.068f * k, 0.01f * k, 0.068f * k, 0.01f * k, Stroke);
                    break;
                case EyeShape.Spiral:
                    Stroke.StrokeWidth = Math.Max(0.028f * k, 1.1f * px);
                    c.RotateRadians(p.SpiralPhase * side);
                    c.Scale(side * k, k);
                    Stroke.StrokeWidth /= k;
                    c.DrawPath(SpiralPath, Stroke);
                    break;
                case EyeShape.Heart:
                    c.Save();
                    c.Scale(0.1f * k);
                    Fill.Color = glowPass ? HeartRed.WithAlpha(ink.Alpha) : HeartRed;
                    c.DrawPath(HeartPath, Fill);
                    c.Restore();
                    if (!glowPass && d >= 30)
                    {
                        Fill.Color = SKColors.White.WithAlpha(200);
                        c.DrawCircle(-0.038f * k, -0.038f * k, 0.017f * k, Fill);
                    }
                    break;
                case EyeShape.Star:
                    c.Save();
                    c.Scale(0.095f * k);
                    Fill.Color = glowPass ? StarGold.WithAlpha(ink.Alpha) : StarGold;
                    c.DrawPath(StarPath, Fill);
                    if (!glowPass)
                    {
                        Stroke.Color = StarEdge;
                        Stroke.StrokeWidth = Math.Max(0.14f, 0.9f * px / (0.095f * k));
                        c.DrawPath(StarPath, Stroke);
                    }
                    c.Restore();
                    break;
                case EyeShape.Annoyed:
                    using (var path = new SKPath())
                    {
                        path.MoveTo(0.045f * k * side, -0.05f * k);
                        path.LineTo(-0.05f * k * side, 0);
                        path.LineTo(0.045f * k * side, 0.05f * k);
                        c.DrawPath(path, Stroke);
                    }
                    break;
            }
            c.Restore();
        }
    }

    static void Pill(SKCanvas c, EyeStyle s, float w, float h, float open, bool catchlight, float px)
    {
        // Keep eyes at least ~2.4 x 4 px so the face survives the 20 px compact bar.
        var grow = Math.Max(1f, Math.Max(2.4f * px / w, 4f * px / h));
        w *= grow; h *= grow;
        var hh = Math.Max(Math.Max(0.03f, 1.3f * px), h * open);
        var r = Math.Min(w, hh) * s.Corner;
        c.DrawRoundRect(SKRect.Create(-w / 2, -hh / 2, w, hh), r, r, Fill);
        if (catchlight && open > 0.6f)
        {
            var ink = Fill.Color;
            Fill.Color = SKColors.White.WithAlpha(235);
            c.DrawCircle(w * 0.12f, -hh * 0.24f, w * 0.24f, Fill);
            Fill.Color = ink;
        }
    }

    // ---- Mouth ----

    /// <summary>Small mouth that follows gaze and reads expression from the eye shape. Skipped below <paramref name="minD"/> px.</summary>
    public static void Mouth(SKCanvas c, CharacterPose p, float d, EyeStyle s, float dy, float w, SKColor ink, MouthKind rest,
        float minD = 36, SKColor? glow = null)
    {
        if (rest == MouthKind.None || d < minD) return;
        if (glow is { } g)
        {
            using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(s.GlowSigma, 1.2f / d));
            Fill.MaskFilter = blur; Stroke.MaskFilter = blur;
            MouthPass(c, p, d, s, dy, w, g, rest);
            Fill.MaskFilter = null; Stroke.MaskFilter = null;
        }
        MouthPass(c, p, d, s, dy, w, ink, rest);
    }

    static void MouthPass(SKCanvas c, CharacterPose p, float d, EyeStyle s, float dy, float w, SKColor ink, MouthKind rest)
    {
        var f = FaceOffset(p, s);
        var line = Math.Max(0.032f, 1.1f / d);
        c.Save();
        c.Translate(f.X * 0.85f, f.Y + dy);
        Fill.Color = ink; Fill.Shader = null;
        Stroke.Color = ink; Stroke.Shader = null; Stroke.StrokeWidth = line;
        using var path = new SKPath();
        switch (p.Eyes)
        {
            case EyeShape.Happy or EyeShape.Heart or EyeShape.Star or EyeShape.Wink:
                // Open grin with a tongue.
                path.MoveTo(-w, -0.01f);
                path.CubicTo(-w * 0.9f, w * 1.15f, w * 0.9f, w * 1.15f, w, -0.01f);
                path.Close();
                c.DrawPath(path, Fill);
                if (rest != MouthKind.Led)
                {
                    c.Save();
                    c.ClipPath(path, antialias: true);
                    Fill.Color = new SKColor(0xFF, 0x7A, 0x95);
                    c.DrawOval(0, w * 0.75f, w * 0.5f, w * 0.32f, Fill);
                    c.Restore();
                }
                break;
            case EyeShape.Wide:
                c.DrawOval(0, w * 0.2f, w * 0.38f, w * 0.48f, Fill);
                break;
            case EyeShape.Closed:
                c.DrawOval(0, w * 0.15f, w * 0.22f, w * 0.26f, Fill);
                break;
            case EyeShape.Flat:
                c.DrawLine(-w * 0.6f, w * 0.2f, w * 0.6f, w * 0.2f, Stroke);
                break;
            case EyeShape.Annoyed or EyeShape.Spiral:
                path.MoveTo(-w * 0.8f, w * 0.25f);
                path.CubicTo(-w * 0.5f, -w * 0.15f, -w * 0.3f, -w * 0.15f, 0, w * 0.2f);
                path.CubicTo(w * 0.3f, w * 0.55f, w * 0.5f, w * 0.55f, w * 0.8f, w * 0.15f);
                c.DrawPath(path, Stroke);
                break;
            case EyeShape.Tired:
                path.MoveTo(-w * 0.6f, w * 0.35f);
                path.QuadTo(0, -w * 0.1f, w * 0.6f, w * 0.35f);
                c.DrawPath(path, Stroke);
                break;
            default:
                if (rest == MouthKind.Cat)
                {
                    // ω
                    path.MoveTo(-w * 0.75f, 0);
                    path.QuadTo(-w * 0.375f, w * 0.65f, 0, 0);
                    path.QuadTo(w * 0.375f, w * 0.65f, w * 0.75f, 0);
                }
                else
                {
                    path.MoveTo(-w * 0.6f, 0);
                    path.QuadTo(0, w * 0.6f, w * 0.6f, 0);
                }
                c.DrawPath(path, Stroke);
                break;
        }
        c.Restore();
    }

    // ---- Badge ----

    public static void Badge(SKCanvas c, CharacterPose p, float d, float cx, float cy, SKColor ring)
    {
        if (p.Badge == BadgeKind.None || p.BadgeScale <= 0.01f) return;
        var px = 1f / d;
        var dot = p.Badge is BadgeKind.RedDot or BadgeKind.GreenDot or BadgeKind.OrangeDot;
        var r = (dot ? 0.115f : 0.16f) * p.BadgeScale;
        var ringW = Math.Max(0.045f, 1.3f * px);

        c.Save();
        Fill.Shader = null;
        Fill.Color = ring;
        c.DrawCircle(cx, cy, r + ringW, Fill);
        FillPathGradientCircle(c, cx, cy, r, C(Mix(p.BadgeColor, White, 0.25f)), C(p.BadgeColor));

        c.Translate(cx, cy);
        c.Scale(r);
        Fill.Color = SKColors.White;
        Stroke.Color = SKColors.White;
        Stroke.Shader = null;
        switch (p.Badge)
        {
            case BadgeKind.Dots:
                var dr = Math.Max(0.15f, 0.7f * px / Math.Max(r, 0.01f));
                for (var i = -1; i <= 1; i++) c.DrawCircle(i * 0.42f, 0.04f, dr, Fill);
                break;
            case BadgeKind.Exclamation:
                c.DrawRoundRect(SKRect.Create(-0.11f, -0.6f, 0.22f, 0.7f), 0.11f, 0.11f, Fill);
                c.DrawCircle(0, 0.38f, 0.13f, Fill);
                break;
            case BadgeKind.Question:
                Stroke.StrokeWidth = Math.Max(0.22f, 1.1f * px / Math.Max(r, 0.01f));
                using (var path = new SKPath())
                {
                    path.MoveTo(-0.3f, -0.25f);
                    path.CubicTo(-0.3f, -0.7f, 0.33f, -0.72f, 0.33f, -0.3f);
                    path.CubicTo(0.33f, -0.05f, 0f, -0.02f, 0f, 0.14f);
                    c.DrawPath(path, Stroke);
                }
                c.DrawCircle(0, 0.44f, 0.12f, Fill);
                break;
            default:
                if (d >= 30)
                {
                    Fill.Color = SKColors.White.WithAlpha(150);
                    c.DrawCircle(-0.3f, -0.3f, 0.25f, Fill);
                }
                break;
        }
        c.Restore();
    }

    static void FillPathGradientCircle(SKCanvas c, float cx, float cy, float r, SKColor top, SKColor bottom)
    {
        using var g = SKShader.CreateLinearGradient(new SKPoint(cx, cy - r), new SKPoint(cx, cy + r), [top, bottom], [0f, 1f], SKShaderTileMode.Clamp);
        Shade(g);
        c.DrawCircle(cx, cy, r, Fill);
        Fill.Shader = null;
    }

    // ---- Particles ----

    public static void Particles(SKCanvas c, CharacterPose p, SKPoint center, float d)
    {
        if (p.Particles.Count == 0) return;
        var px = 1f / d;
        c.Save();
        c.Translate(center.X, center.Y);
        c.Scale(d);
        foreach (var q in p.Particles)
        {
            if (q.Alpha <= 0.01f || q.Size <= 0) continue;
            c.Save();
            c.Translate(q.X, q.Y);
            c.RotateRadians(q.Rotation);
            var a = A(q.Alpha);
            switch (q.Kind)
            {
                case ParticleKind.Heart:
                    c.Scale(q.Size * 0.55f);
                    Fill.Color = HeartRed.WithAlpha(a);
                    c.DrawPath(HeartPath, Fill);
                    break;
                case ParticleKind.Star:
                    c.Scale(q.Size * 0.5f);
                    Additive.Color = StarGold.WithAlpha(a);
                    c.DrawPath(StarPath, Additive);
                    break;
                case ParticleKind.Spark:
                    c.Scale(q.Size * 0.5f);
                    Additive.Color = new SKColor(0xD8, 0xFF, 0xEC).WithAlpha(a);
                    c.DrawPath(SparkPath, Additive);
                    break;
                case ParticleKind.Sweat:
                    c.Scale(q.Size * 0.5f);
                    Fill.Color = new SKColor(0x9C, 0xD8, 0xFF).WithAlpha(a);
                    c.DrawPath(DropPath, Fill);
                    break;
                case ParticleKind.Z:
                    c.Scale(q.Size);
                    Stroke.Color = new SKColor(0xC8, 0xD3, 0xEA).WithAlpha(a);
                    Stroke.StrokeWidth = Math.Max(0.16f, 1.1f * px / q.Size);
                    Stroke.Shader = null;
                    c.DrawPath(ZPath, Stroke);
                    break;
            }
            c.Restore();
        }
        c.Restore();
    }

    // ---- Geometry ----

    public static SKPath Superellipse(float a, float b, float n, float cx = 0, float cy = 0)
    {
        var path = new SKPath();
        const int steps = 128;
        for (var i = 0; i < steps; i++)
        {
            var t = i * MathF.PI * 2 / steps;
            var co = MathF.Cos(t); var si = MathF.Sin(t);
            var x = cx + a * MathF.Sign(co) * MathF.Pow(MathF.Abs(co), 2 / n);
            var y = cy + b * MathF.Sign(si) * MathF.Pow(MathF.Abs(si), 2 / n);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        path.Close();
        return path;
    }

    static SKPath Heart()
    {
        var path = new SKPath();
        path.MoveTo(0, 0.95f);
        path.CubicTo(-0.35f, 0.6f, -1.05f, 0.25f, -1.0f, -0.3f);
        path.CubicTo(-0.95f, -0.85f, -0.25f, -1.0f, 0, -0.5f);
        path.CubicTo(0.25f, -1.0f, 0.95f, -0.85f, 1.0f, -0.3f);
        path.CubicTo(1.05f, 0.25f, 0.35f, 0.6f, 0, 0.95f);
        path.Close();
        return path;
    }

    static SKPath Star(int points, float outer, float inner)
    {
        var path = new SKPath();
        for (var i = 0; i < points * 2; i++)
        {
            var r = i % 2 == 0 ? outer : inner;
            var a = -MathF.PI / 2 + i * MathF.PI / points;
            var x = MathF.Cos(a) * r; var y = MathF.Sin(a) * r;
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        path.Close();
        return path;
    }

    static SKPath Spiral()
    {
        var path = new SKPath();
        const int steps = 80;
        const float turns = 2.4f, maxR = 0.085f;
        for (var i = 0; i <= steps; i++)
        {
            var u = i / (float)steps;
            var a = u * turns * MathF.PI * 2;
            var r = maxR * u;
            var x = MathF.Cos(a) * r; var y = MathF.Sin(a) * r;
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        return path;
    }

    static SKPath ZGlyph()
    {
        var path = new SKPath();
        path.MoveTo(-0.45f, -0.5f);
        path.LineTo(0.45f, -0.5f);
        path.LineTo(-0.45f, 0.5f);
        path.LineTo(0.45f, 0.5f);
        return path;
    }

    static SKPath Drop()
    {
        var path = new SKPath();
        path.MoveTo(0, -1f);
        path.CubicTo(0.35f, -0.45f, 0.75f, -0.05f, 0.75f, 0.35f);
        path.CubicTo(0.75f, 0.8f, 0.4f, 1f, 0, 1f);
        path.CubicTo(-0.4f, 1f, -0.75f, 0.8f, -0.75f, 0.35f);
        path.CubicTo(-0.75f, -0.05f, -0.35f, -0.45f, 0, -1f);
        path.Close();
        return path;
    }
}
