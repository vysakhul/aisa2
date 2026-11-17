using Aisa.Character;
using SkiaSharp;

namespace Aisa.Character.Rendering;

/// <summary>
/// Procedural SkiaSharp drawing of an <see cref="CharacterPose"/>. Paints and paths are cached statically,
/// so call from one render thread at a time.
/// </summary>
public static class CharacterRenderer
{
    static readonly SKColor EyeInk = new(0x24, 0x21, 0x33);
    static readonly SKColor HeartRed = new(0xFF, 0x4F, 0x86);
    static readonly SKColor StarGold = new(0xFF, 0xC9, 0x3C);
    static readonly SKColor StarEdge = new(0xD9, 0x86, 0x10);

    // Unit-space geometry (face diameter = 1, centred on the origin).
    static readonly SKPath Body = Squircle(0.5f, 0.5f, 2.9f);
    static readonly SKPath HeartPath = Heart();
    static readonly SKPath StarPath = Star(5, 1f, 0.47f);
    static readonly SKPath SparkPath = Star(4, 1f, 0.28f);
    static readonly SKPath SpiralPath = Spiral();
    static readonly SKPath ZPath = ZGlyph();
    static readonly SKPath DropPath = Drop();

    static readonly SKPaint Fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    static readonly SKPaint Stroke = new()
    {
        IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    };
    static readonly SKPaint Additive = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Plus };

    public static void DrawHalo(SKCanvas canvas, CharacterPose pose, SKPoint center, float diameter)
    {
        if (pose.GlowOpacity <= 0.001f || diameter <= 0) return;
        var c = new SKPoint(center.X + pose.OffsetX * diameter * 0.6f, center.Y + pose.OffsetY * diameter * 0.6f);
        var r = diameter * 1.1f;
        var a = Math.Clamp(pose.GlowOpacity, 0, 1) * 0.42f;
        var col = ToColor(pose.Glow);
        using var shader = SKShader.CreateRadialGradient(c, r,
            [col.WithAlpha(A(a)), col.WithAlpha(A(a * 0.72f)), col.WithAlpha(A(a * 0.3f)), col.WithAlpha(A(a * 0.08f)), col.WithAlpha(0)],
            [0f, 0.32f, 0.58f, 0.8f, 1f], SKShaderTileMode.Clamp);
        Fill.Shader = shader;
        Fill.Color = SKColors.White;
        canvas.DrawCircle(c, r, Fill);
        Fill.Shader = null;
    }

    public static void Draw(SKCanvas canvas, CharacterPose pose, SKPoint center, float diameter)
    {
        if (diameter <= 0) return;
        var d = diameter;
        // Minimum stroke widths in unit space so lines stay crisp at compact (≈20 px) sizes.
        var px = 1f / d;

        canvas.Save();
        canvas.Translate(center.X + pose.OffsetX * d, center.Y + pose.OffsetY * d);
        canvas.Scale(d);
        canvas.RotateRadians(pose.Tilt + pose.Roll);
        // Squash pivots near the base so the body looks pressed into the ground.
        canvas.Translate(0, 0.32f);
        canvas.Scale(pose.ScaleX, pose.ScaleY);
        canvas.Translate(0, -0.32f);

        DrawSprout(canvas, pose, px);
        DrawBody(canvas, pose, d);
        DrawBlush(canvas, pose);
        DrawEyes(canvas, pose, d, px);
        DrawBadge(canvas, pose, d, px);
        canvas.Restore();

        if (pose.Particles.Count > 0)
        {
            canvas.Save();
            canvas.Translate(center.X, center.Y);
            canvas.Scale(d);
            foreach (var p in pose.Particles) DrawParticle(canvas, p, px);
            canvas.Restore();
        }
    }

    // ---- Body ----

    static void DrawBody(SKCanvas canvas, CharacterPose pose, float d)
    {
        var body = ToColor(pose.Body);
        var under = ToColor(Rgb.Lerp(pose.Body, pose.Tint, Math.Clamp(pose.TintAmount, 0, 1)));
        var top = ToColor(Rgb.Lerp(pose.Body, new Rgb(1, 1, 1), 0.5f));

        using (var g = SKShader.CreateLinearGradient(new SKPoint(0, -0.5f), new SKPoint(0, 0.5f),
                   [top, body, body, under], [0f, 0.3f, 0.5f, 1f], SKShaderTileMode.Clamp))
        {
            Fill.Shader = g;
            Fill.Color = SKColors.White;
            canvas.DrawPath(Body, Fill);
        }

        canvas.Save();
        canvas.ClipPath(Body, antialias: true);

        // Soft underside glow pooling at the bottom.
        var pool = ToColor(pose.Tint);
        using (var g = SKShader.CreateRadialGradient(new SKPoint(0, 0.62f), 0.62f,
                   [pool.WithAlpha(A(0.55f * pose.TintAmount)), pool.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp))
        {
            Fill.Shader = g;
            canvas.DrawRect(-0.6f, -0.6f, 1.2f, 1.2f, Fill);
        }

        // Gentle top highlight.
        using (var g = SKShader.CreateRadialGradient(new SKPoint(-0.1f, -0.33f), 0.3f,
                   [SKColors.White.WithAlpha(d < 30 ? (byte)110 : (byte)200), SKColors.White.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp))
        {
            Fill.Shader = g;
            canvas.Save();
            canvas.Scale(1f, 0.42f, -0.1f, -0.33f);
            canvas.DrawCircle(-0.1f, -0.33f, 0.3f, Fill);
            canvas.Restore();
        }
        Fill.Shader = null;

        // Thin inner rim shadow defines the silhouette against the halo.
        Stroke.Color = ToColor(Rgb.Lerp(pose.Tint, new Rgb(0, 0, 0), 0.35f)).WithAlpha(70);
        Stroke.StrokeWidth = Math.Max(0.035f, 1.2f / d);
        using (var g = SKShader.CreateLinearGradient(new SKPoint(0, -0.1f), new SKPoint(0, 0.5f),
                   [SKColors.Transparent, Stroke.Color], [0f, 1f], SKShaderTileMode.Clamp))
        {
            Stroke.Shader = g;
            canvas.DrawPath(Body, Stroke);
            Stroke.Shader = null;
        }
        canvas.Restore();
    }

    /// <summary>Aisa's signature: a short curled antenna whose tip carries the state accent.</summary>
    static void DrawSprout(SKCanvas canvas, CharacterPose pose, float px)
    {
        Stroke.Shader = null;
        Stroke.Color = ToColor(Rgb.Lerp(pose.Body, pose.Tint, 0.25f));
        Stroke.StrokeWidth = Math.Max(0.04f, 1.1f * px);
        // Sways slightly against head motion.
        var lean = Math.Clamp(-pose.OffsetX * 0.6f + pose.Yaw * 0.04f, -0.08f, 0.08f);
        var tip = new SKPoint(0.17f + lean, -0.64f);
        using (var p = new SKPath())
        {
            p.MoveTo(0.07f, -0.44f);
            p.QuadTo(0.08f + lean * 0.5f, -0.6f, tip.X, tip.Y);
            canvas.DrawPath(p, Stroke);
        }
        var tipColor = ToColor(Rgb.Lerp(pose.Tint, new Rgb(1, 1, 1), 0.15f));
        var r = Math.Max(0.05f, 1.3f * px);
        using (var g = SKShader.CreateRadialGradient(tip, r * 2.6f, [tipColor.WithAlpha(90), tipColor.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp))
        {
            Fill.Shader = g;
            canvas.DrawCircle(tip, r * 2.6f, Fill);
            Fill.Shader = null;
        }
        Fill.Color = tipColor;
        canvas.DrawCircle(tip, r, Fill);
        Fill.Color = SKColors.White.WithAlpha(170);
        canvas.DrawCircle(tip.X - r * 0.3f, tip.Y - r * 0.3f, r * 0.35f, Fill);
    }

    static void DrawBlush(SKCanvas canvas, CharacterPose pose)
    {
        if (pose.Blush <= 0.01f) return;
        var c = new SKColor(0xFF, 0x6B, 0x9A);
        var dx = pose.Yaw * 0.08f;
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var cx = side * 0.29f + dx; const float cy = 0.14f;
            using var g = SKShader.CreateRadialGradient(new SKPoint(cx, cy), 0.09f,
                [c.WithAlpha(A(0.75f * pose.Blush)), c.WithAlpha(0)], [0.2f, 1f], SKShaderTileMode.Clamp);
            Fill.Shader = g;
            canvas.Save();
            canvas.Scale(1f, 0.6f, cx, cy);
            canvas.DrawCircle(cx, cy, 0.09f, Fill);
            canvas.Restore();
        }
        Fill.Shader = null;
    }

    // ---- Eyes ----

    static void DrawEyes(SKCanvas canvas, CharacterPose pose, float d, float px)
    {
        var yaw = Math.Clamp(pose.Yaw, -1, 1);
        var pitch = Math.Clamp(pose.Pitch, -1, 1);
        var spacing = 0.175f * (1 - 0.12f * MathF.Abs(yaw));
        var ox = yaw * 0.13f;
        var oy = 0.02f + pitch * 0.12f;
        var s = pose.EyeScale;
        var open = Math.Clamp(pose.EyeOpen, 0, 1);
        var line = Math.Max(0.045f, 1.4f * px);

        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            // The eye on the side being looked towards foreshortens slightly.
            var near = 1 - 0.1f * MathF.Max(0, side * yaw);
            var cx = ox + side * spacing;
            var cy = oy;
            canvas.Save();
            canvas.Translate(cx, cy);
            canvas.Scale(s * near, s);

            var shape = pose.Eyes;
            if (shape == EyeShape.Wink) shape = side < 0 ? EyeShape.Pill : EyeShape.Happy;

            switch (shape)
            {
                case EyeShape.Pill: Pill(canvas, 0.105f, 0.21f, open, d >= 30, px); break;
                case EyeShape.Wide: Pill(canvas, 0.125f, 0.24f, open, d >= 24, px); break;
                case EyeShape.Dot:
                    Fill.Color = EyeInk;
                    canvas.DrawOval(0, 0, 0.05f, 0.05f * Math.Max(0.15f, open), Fill);
                    break;
                case EyeShape.Flat:
                    Fill.Color = EyeInk;
                    canvas.DrawRoundRect(SKRect.Create(-0.065f, 0.0f, 0.13f, Math.Max(0.045f, 1.4f * px)), 0.03f, 0.03f, Fill);
                    break;
                case EyeShape.Happy:
                    StrokeInk(line);
                    using (var p = new SKPath())
                    {
                        p.MoveTo(-0.06f, 0.035f);
                        p.QuadTo(0, -0.075f, 0.06f, 0.035f);
                        canvas.DrawPath(p, Stroke);
                    }
                    break;
                case EyeShape.Closed:
                    StrokeInk(line);
                    using (var p = new SKPath())
                    {
                        p.MoveTo(-0.062f, 0.0f);
                        p.QuadTo(0, 0.075f, 0.062f, 0.0f);
                        canvas.DrawPath(p, Stroke);
                    }
                    break;
                case EyeShape.Tired:
                    canvas.Save();
                    canvas.ClipRect(SKRect.Create(-0.1f, 0.01f, 0.2f, 0.2f));
                    Pill(canvas, 0.105f, 0.19f, open, false, px);
                    canvas.Restore();
                    StrokeInk(line * 0.8f);
                    canvas.DrawLine(-0.068f, 0.01f, 0.068f, 0.01f, Stroke);
                    break;
                case EyeShape.Spiral:
                    StrokeInk(Math.Max(0.028f, 1.1f * px));
                    canvas.RotateRadians(pose.SpiralPhase * side);
                    canvas.Scale(side, 1);
                    canvas.DrawPath(SpiralPath, Stroke);
                    break;
                case EyeShape.Heart:
                    Fill.Color = HeartRed;
                    canvas.Save();
                    canvas.Scale(0.1f);
                    canvas.DrawPath(HeartPath, Fill);
                    canvas.Restore();
                    if (d >= 30)
                    {
                        Fill.Color = SKColors.White.WithAlpha(200);
                        canvas.DrawCircle(-0.038f, -0.038f, 0.017f, Fill);
                    }
                    break;
                case EyeShape.Star:
                    canvas.Save();
                    canvas.Scale(0.095f);
                    Fill.Color = StarGold;
                    canvas.DrawPath(StarPath, Fill);
                    Stroke.Color = StarEdge;
                    Stroke.StrokeWidth = Math.Max(0.14f, 0.9f * px / 0.095f);
                    canvas.DrawPath(StarPath, Stroke);
                    canvas.Restore();
                    break;
                case EyeShape.Annoyed:
                    StrokeInk(line);
                    using (var p = new SKPath())
                    {
                        // ">" on the left eye, "<" on the right.
                        p.MoveTo(0.045f * side, -0.05f);
                        p.LineTo(-0.05f * side, 0f);
                        p.LineTo(0.045f * side, 0.05f);
                        canvas.DrawPath(p, Stroke);
                    }
                    break;
            }
            canvas.Restore();
        }
    }

    static void StrokeInk(float width)
    {
        Stroke.Color = EyeInk;
        Stroke.StrokeWidth = width;
        Stroke.Shader = null;
    }

    static void Pill(SKCanvas canvas, float w, float h, float open, bool catchlight, float px)
    {
        var hh = Math.Max(Math.Max(0.03f, 1.3f * px), h * open);
        Fill.Color = EyeInk;
        var r = Math.Min(w, hh) / 2;
        canvas.DrawRoundRect(SKRect.Create(-w / 2, -hh / 2, w, hh), r, r, Fill);
        if (catchlight && open > 0.6f)
        {
            Fill.Color = SKColors.White.WithAlpha(235);
            canvas.DrawCircle(w * 0.12f, -hh * 0.24f, w * 0.24f, Fill);
        }
    }

    // ---- Badge ----

    static void DrawBadge(SKCanvas canvas, CharacterPose pose, float d, float px)
    {
        if (pose.Badge == BadgeKind.None || pose.BadgeScale <= 0.01f) return;
        var dot = pose.Badge is BadgeKind.RedDot or BadgeKind.GreenDot or BadgeKind.OrangeDot;
        var r = (dot ? 0.115f : 0.16f) * pose.BadgeScale;
        var cx = dot ? -0.34f : -0.36f;
        var cy = dot ? -0.34f : -0.36f;
        var ring = Math.Max(0.045f, 1.3f * px);

        canvas.Save();
        Fill.Color = SKColors.Black;
        canvas.DrawCircle(cx, cy, r + ring, Fill);
        var col = ToColor(pose.BadgeColor);
        using (var g = SKShader.CreateLinearGradient(new SKPoint(cx, cy - r), new SKPoint(cx, cy + r),
                   [ToColor(Rgb.Lerp(pose.BadgeColor, new Rgb(1, 1, 1), 0.25f)), col], [0f, 1f], SKShaderTileMode.Clamp))
        {
            Fill.Shader = g;
            canvas.DrawCircle(cx, cy, r, Fill);
            Fill.Shader = null;
        }

        canvas.Translate(cx, cy);
        canvas.Scale(r);
        Fill.Color = SKColors.White;
        Stroke.Color = SKColors.White;
        Stroke.Shader = null;
        var glyphStroke = Math.Max(0.22f, 1.1f * px / Math.Max(r, 0.01f));
        switch (pose.Badge)
        {
            case BadgeKind.Dots:
                var dr = Math.Max(0.15f, 0.7f * px / Math.Max(r, 0.01f));
                for (var i = -1; i <= 1; i++) canvas.DrawCircle(i * 0.42f, 0.04f, dr, Fill);
                break;
            case BadgeKind.Exclamation:
                canvas.DrawRoundRect(SKRect.Create(-0.11f, -0.6f, 0.22f, 0.7f), 0.11f, 0.11f, Fill);
                canvas.DrawCircle(0, 0.38f, 0.13f, Fill);
                break;
            case BadgeKind.Question:
                Stroke.StrokeWidth = glyphStroke;
                using (var p = new SKPath())
                {
                    p.MoveTo(-0.3f, -0.25f);
                    p.CubicTo(-0.3f, -0.7f, 0.33f, -0.72f, 0.33f, -0.3f);
                    p.CubicTo(0.33f, -0.05f, 0f, -0.02f, 0f, 0.14f);
                    canvas.DrawPath(p, Stroke);
                }
                canvas.DrawCircle(0, 0.44f, 0.12f, Fill);
                break;
            default:
                // Status dots: a small specular glint.
                if (d >= 30)
                {
                    Fill.Color = SKColors.White.WithAlpha(150);
                    canvas.DrawCircle(-0.3f, -0.3f, 0.25f, Fill);
                }
                break;
        }
        canvas.Restore();
    }

    // ---- Particles ----

    static void DrawParticle(SKCanvas canvas, Particle p, float px)
    {
        if (p.Alpha <= 0.01f || p.Size <= 0) return;
        canvas.Save();
        canvas.Translate(p.X, p.Y);
        canvas.RotateRadians(p.Rotation);
        var a = A(p.Alpha);
        switch (p.Kind)
        {
            case ParticleKind.Heart:
                canvas.Scale(p.Size * 0.55f);
                Fill.Color = HeartRed.WithAlpha(a);
                canvas.DrawPath(HeartPath, Fill);
                break;
            case ParticleKind.Star:
                canvas.Scale(p.Size * 0.5f);
                Additive.Color = StarGold.WithAlpha(a);
                canvas.DrawPath(StarPath, Additive);
                break;
            case ParticleKind.Spark:
                canvas.Scale(p.Size * 0.5f);
                Additive.Color = new SKColor(0xD8, 0xFF, 0xEC).WithAlpha(a);
                canvas.DrawPath(SparkPath, Additive);
                break;
            case ParticleKind.Sweat:
                canvas.Scale(p.Size * 0.5f);
                Fill.Color = new SKColor(0x9C, 0xD8, 0xFF).WithAlpha(a);
                canvas.DrawPath(DropPath, Fill);
                break;
            case ParticleKind.Z:
                canvas.Scale(p.Size);
                Stroke.Color = new SKColor(0xC8, 0xD3, 0xEA).WithAlpha(a);
                Stroke.StrokeWidth = Math.Max(0.16f, 1.1f * px / p.Size);
                Stroke.Shader = null;
                canvas.DrawPath(ZPath, Stroke);
                break;
        }
        canvas.Restore();
    }

    // ---- Geometry ----

    static SKPath Squircle(float a, float b, float n)
    {
        var path = new SKPath();
        const int steps = 128;
        for (var i = 0; i < steps; i++)
        {
            var t = i * MathF.PI * 2 / steps;
            var c = MathF.Cos(t); var s = MathF.Sin(t);
            var x = a * MathF.Sign(c) * MathF.Pow(MathF.Abs(c), 2 / n);
            var y = b * MathF.Sign(s) * MathF.Pow(MathF.Abs(s), 2 / n);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        path.Close();
        return path;
    }

    /// <summary>Heart fitting roughly in [-1,1]².</summary>
    static SKPath Heart()
    {
        var p = new SKPath();
        p.MoveTo(0, 0.95f);
        p.CubicTo(-0.35f, 0.6f, -1.05f, 0.25f, -1.0f, -0.3f);
        p.CubicTo(-0.95f, -0.85f, -0.25f, -1.0f, 0, -0.5f);
        p.CubicTo(0.25f, -1.0f, 0.95f, -0.85f, 1.0f, -0.3f);
        p.CubicTo(1.05f, 0.25f, 0.35f, 0.6f, 0, 0.95f);
        p.Close();
        return p;
    }

    static SKPath Star(int points, float outer, float inner)
    {
        var p = new SKPath();
        for (var i = 0; i < points * 2; i++)
        {
            var r = i % 2 == 0 ? outer : inner;
            var a = -MathF.PI / 2 + i * MathF.PI / points;
            var x = MathF.Cos(a) * r; var y = MathF.Sin(a) * r;
            if (i == 0) p.MoveTo(x, y); else p.LineTo(x, y);
        }
        p.Close();
        return p;
    }

    static SKPath Spiral()
    {
        var p = new SKPath();
        const int steps = 80;
        const float turns = 2.4f, maxR = 0.085f;
        for (var i = 0; i <= steps; i++)
        {
            var u = i / (float)steps;
            var a = u * turns * MathF.PI * 2;
            var r = maxR * u;
            var x = MathF.Cos(a) * r; var y = MathF.Sin(a) * r;
            if (i == 0) p.MoveTo(x, y); else p.LineTo(x, y);
        }
        return p;
    }

    static SKPath ZGlyph()
    {
        var p = new SKPath();
        p.MoveTo(-0.45f, -0.5f);
        p.LineTo(0.45f, -0.5f);
        p.LineTo(-0.45f, 0.5f);
        p.LineTo(0.45f, 0.5f);
        return p;
    }

    static SKPath Drop()
    {
        var p = new SKPath();
        p.MoveTo(0, -1f);
        p.CubicTo(0.35f, -0.45f, 0.75f, -0.05f, 0.75f, 0.35f);
        p.CubicTo(0.75f, 0.8f, 0.4f, 1f, 0, 1f);
        p.CubicTo(-0.4f, 1f, -0.75f, 0.8f, -0.75f, 0.35f);
        p.CubicTo(-0.75f, -0.05f, -0.35f, -0.45f, 0, -1f);
        p.Close();
        return p;
    }

    static SKColor ToColor(Rgb c) => new(B8(c.R), B8(c.G), B8(c.B));
    static byte B8(float v) => (byte)Math.Clamp(MathF.Round(v * 255), 0, 255);
    static byte A(float a) => (byte)Math.Clamp(MathF.Round(a * 255), 0, 255);
}
