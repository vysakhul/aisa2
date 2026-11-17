namespace Aisa.Character;

[Flags]
public enum StateMotion
{
    None = 0,
    Bounce = 1,
    Scan = 2,
    Breathe = 4,
    Tilt = 8,
    Shake = 16,
    Roll = 32,
    Sweat = 64,
    Zzz = 128,
    Sway = 256,
}

/// <summary>One row of the state catalog (spec 2.3).</summary>
public sealed record StateConfig(
    Rgb Body,
    Rgb Accent,
    float TintAmount,
    EyeShape Eyes,
    BadgeKind Badge,
    Rgb Glow,
    float GlowOpacity,
    StateMotion Motion,
    bool Blinks = true,
    bool TracksCursor = true)
{
    static readonly Rgb SoftWhite = Rgb.FromHex(0xF3F1F7);
    static readonly Rgb IdleTint = Rgb.FromHex(0xB9B4D6);

    static StateConfig Colored(uint hex, EyeShape eyes, BadgeKind badge, StateMotion motion, float glow = 0.65f, float bodyMix = 0.16f)
    {
        var accent = Rgb.FromHex(hex);
        return new StateConfig(Rgb.Lerp(SoftWhite, accent, bodyMix), accent, 0.9f, eyes, badge, accent, glow, motion);
    }

    public static StateConfig For(CharacterState state) => Table[(int)state];

    static readonly StateConfig[] Table = BuildTable();

    static StateConfig[] BuildTable()
    {
        var t = new StateConfig[Enum.GetValues<CharacterState>().Length];
        t[(int)CharacterState.Idle] = new StateConfig(SoftWhite, IdleTint, 0.55f, EyeShape.Pill, BadgeKind.None, new Rgb(1, 1, 1), 0.15f, StateMotion.None);
        t[(int)CharacterState.Working] = Colored(0x3B9EFF, EyeShape.Pill, BadgeKind.Dots, StateMotion.None);
        t[(int)CharacterState.Thinking] = Colored(0xA78BFA, EyeShape.Pill, BadgeKind.Dots, StateMotion.None);
        t[(int)CharacterState.Searching] = Colored(0x6366F1, EyeShape.Pill, BadgeKind.Dots, StateMotion.Scan);
        t[(int)CharacterState.Approval] = Colored(0xF5A524, EyeShape.Wide, BadgeKind.Exclamation, StateMotion.Bounce);
        t[(int)CharacterState.Question] = Colored(0x22D3EE, EyeShape.Pill, BadgeKind.Question, StateMotion.Tilt);
        t[(int)CharacterState.Error] = Colored(0xF4505E, EyeShape.Flat, BadgeKind.RedDot, StateMotion.Shake);
        t[(int)CharacterState.Finished] = Colored(0x34D399, EyeShape.Happy, BadgeKind.GreenDot, StateMotion.Roll);
        t[(int)CharacterState.RateLimited] = Colored(0xFB923C, EyeShape.Tired, BadgeKind.OrangeDot, StateMotion.Sweat);
        t[(int)CharacterState.Sleeping] = Colored(0x8A9BB8, EyeShape.Closed, BadgeKind.None, StateMotion.Breathe | StateMotion.Zzz, glow: 0.15f, bodyMix: 0.4f)
            with { Blinks = false, TracksCursor = false };
        t[(int)CharacterState.Dizzy] = Colored(0xF472B6, EyeShape.Spiral, BadgeKind.None, StateMotion.Roll | StateMotion.Sway, glow: 0f, bodyMix: 0.22f)
            with { Blinks = false, TracksCursor = false };
        return t;
    }
}
