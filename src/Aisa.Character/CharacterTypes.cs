namespace Aisa.Character;

public enum CharacterState { Idle, Working, Thinking, Searching, Approval, Question, Error, Finished, RateLimited, Sleeping, Dizzy }

public enum Emote { Love, Surprised, Proud, Wink, Yawn, Happy, Annoyed }

public enum EyeShape { Pill, Wide, Flat, Happy, Tired, Closed, Spiral, Heart, Dot, Star, Wink, Annoyed }

public enum BadgeKind { None, Dots, Exclamation, Question, RedDot, GreenDot, OrangeDot }

public enum CharacterSound { Hover, Love, Slap, Annoyed, Dizzy, Happy }

public enum ParticleKind { Heart, Star, Spark, Sweat, Z }

/// <summary>Linear colour, components 0..1.</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    public static Rgb FromHex(uint rgb) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    public static Rgb Lerp(Rgb a, Rgb b, float t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    internal float DistanceTo(Rgb o) => MathF.Max(MathF.Abs(R - o.R), MathF.Max(MathF.Abs(G - o.G), MathF.Abs(B - o.B)));
}

/// <summary>X/Y are relative to the face centre in face-diameter units (+Y is down).</summary>
public readonly record struct Particle(ParticleKind Kind, float X, float Y, float Size, float Rotation, float Alpha);

/// <summary>
/// Everything the renderer needs for one frame. Lengths are in face-diameter units (diameter = 1),
/// angles in radians, +X right and +Y down. The engine reuses one instance and rewrites it every Step.
/// </summary>
public sealed class CharacterPose
{
    public Rgb Body { get; set; } = new(0.95f, 0.94f, 0.93f);
    /// <summary>Underside gradient colour; also the state accent.</summary>
    public Rgb Tint { get; set; } = new(0.8f, 0.8f, 0.85f);
    public float TintAmount { get; set; }
    public Rgb Glow { get; set; } = new(1, 1, 1);
    public float GlowOpacity { get; set; }

    public EyeShape Eyes { get; set; } = EyeShape.Pill;
    public float EyeScale { get; set; } = 1;
    /// <summary>0 closed .. 1 open (blinks).</summary>
    public float EyeOpen { get; set; } = 1;

    /// <summary>-1..1, +1 looks right.</summary>
    public float Yaw { get; set; }
    /// <summary>-1..1, +1 looks down.</summary>
    public float Pitch { get; set; }
    public float Tilt { get; set; }
    public float Roll { get; set; }

    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }

    public BadgeKind Badge { get; set; }
    public float BadgeScale { get; set; }
    public Rgb BadgeColor { get; set; } = new(0.23f, 0.62f, 1f);

    public float Blush { get; set; }
    public float SpiralPhase { get; set; }

    public IReadOnlyList<Particle> Particles { get; set; } = [];
}
