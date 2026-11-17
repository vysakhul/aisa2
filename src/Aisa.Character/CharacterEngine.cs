namespace Aisa.Character;

/// <summary>
/// Headless character simulation. The host calls input methods as events arrive, then <see cref="Step"/>
/// once per frame and draws <see cref="Pose"/>. Deterministic for a given dt sequence and <see cref="Random"/>.
/// </summary>
public sealed class CharacterEngine
{
    // Timings and tuning (spec 2.3–2.5).
    internal const double MaxDt = 0.05;
    internal const double DefaultEmoteSeconds = 1.8;
    internal const double SlapWindow = 1.7;
    internal const double DizzySeconds = 3.3;
    internal const double AnnoyedSeconds = 0.8;
    internal const double LoveWait = 1.9;
    internal const double LoveCooldown = 6.0;
    internal const double HoverRearmDip = 40;
    internal const double BlinkMin = 2.2, BlinkMax = 5.4, DoubleBlinkChance = 0.22, DoubleBlinkDelay = 0.23;
    internal const double BlinkDuration = 0.15;
    internal const double SquashDuration = 0.37;
    internal const double ShakeDuration = 0.28;
    internal const double FinishedRollSeconds = 0.95, DizzyRollSeconds = 1.3;
    internal const double BadgeOutSeconds = 0.09, BadgeInSeconds = 0.28;
    internal const double ColorTau = 0.1;   // ≈95% after 0.3 s
    internal const double GazeTau = 0.12;
    internal const double ScanSpeed = 2.6, SwaySpeed = 9.0;
    internal const double JellyStiffness = 60, JellyDamping = 5;
    const double SpringSubstep = 1.0 / 240;

    readonly Random _rng;
    readonly CharacterPose _pose = new();

    CharacterState? _alert, _dialog, _held, _flash;
    double _flashRemaining;
    bool _dizzy;
    double _dizzyRemaining;
    CharacterState _effective = CharacterState.Idle;
    StateConfig _cfg = StateConfig.For(CharacterState.Idle);

    Emote? _emote;
    double _emoteRemaining, _emoteAge;

    bool _expanded;

    // Gaze
    double _cursorYaw, _cursorPitch;
    float _yaw, _pitch;

    // Hover
    bool _pointerInside, _hoverActive;
    double? _loveWait;
    double _anchorX, _anchorY;
    double _lastLoveTime = double.NegativeInfinity;
    double _time;

    // One-shots: progress in seconds, negative = inactive
    double _blinkT = -1, _blinkTimer, _doubleBlinkTimer = -1;
    double _squashT = -1;
    double _rollT = -1, _rollDuration, _rollTurns;
    double _shakeT = -1;
    double _hopT = -1, _hopDuration, _hopHeight;
    double _stretchT = -1;

    // Jelly spring
    double _jx, _jy, _jvx, _jvy;

    readonly List<double> _slaps = [];

    // Continuous motions
    double _bouncePhase, _scanPhase, _breathPhase, _swayPhase;
    float _bounceAmp, _breathAmp, _swayAmp, _scanAmp;

    float _tilt, _eyeScale = 1, _blush, _spiral;
    Rgb _body, _tint, _glow, _badgeColor;
    float _tintAmount, _glowOpacity;

    // Badge
    enum BadgePhase { Idle, Out, In }
    BadgeKind _badgeShown, _badgeTarget;
    BadgePhase _badgePhase;
    double _badgeT;
    float _badgeScale, _badgeOutFrom;

    // Particles
    struct Sim
    {
        public ParticleKind Kind;
        public float X, Y, Vx, Vy, Size, Rot, Spin, Age, Life, Gravity, Grow;
    }
    readonly List<Sim> _sims = [];
    readonly List<Particle> _particles = [];
    double _zTimer, _sweatTimer;

    bool _dirtyFlag = true, _animating;
    TimeSpan? _nextWake;

    // Every input path marks the engine dirty; the false→true edge tells a sleeping host to resume frames.
    bool _dirty
    {
        get => _dirtyFlag;
        set
        {
            bool wake = value && !_dirtyFlag;
            _dirtyFlag = value;
            if (wake) Invalidated?.Invoke();
        }
    }

    public CharacterEngine(Random? random = null)
    {
        _rng = random ?? new Random();
        _body = _cfg.Body; _tint = _cfg.Accent; _tintAmount = _cfg.TintAmount;
        _glow = _cfg.Glow; _glowOpacity = _cfg.GlowOpacity; _badgeColor = _cfg.Accent;
        ScheduleBlink();
        Step(0);
    }

    public CharacterPose Pose => _pose;
    public CharacterState EffectiveState => _effective;

    /// <summary>True while anything is animating or input arrived since the last Step.</summary>
    public bool IsBusy => _dirty || _animating;

    /// <summary>When settled: time until the next scheduled event (blink, love, flash/emote expiry). Null while busy.</summary>
    public TimeSpan? NextWakeIn => IsBusy ? null : _nextWake;

    public event Action<CharacterSound>? SoundRequested;
    public event Action? DizzyStarted;
    public event Action? DizzyEnded;
    /// <summary>Raised when the engine needs frames again after settling (state, emote, input).</summary>
    public event Action? Invalidated;

    bool OverrideActive => _dizzy || _flash.HasValue || _held.HasValue;

    // ---- Layered state ----

    public void SetAlertState(CharacterState? state) { _alert = state; Refresh(); }
    public void SetDialogState(CharacterState? state) { _dialog = state; Refresh(); }

    public void Flash(CharacterState state, TimeSpan duration)
    {
        _flash = state;
        _flashRemaining = Math.Max(0, duration.TotalSeconds);
        Refresh();
    }

    public void BeginOverride(CharacterState state) { _held = state; Refresh(); }
    public void EndOverride() { _held = null; Refresh(); }

    public void PlayEmote(Emote emote, TimeSpan? duration = null)
    {
        _emote = emote;
        _emoteRemaining = duration?.TotalSeconds ?? DefaultEmoteSeconds;
        _emoteAge = 0;
        switch (emote)
        {
            case Emote.Love:
                StartHop(0.06, 0.34);
                for (var i = 0; i < 4; i++)
                {
                    var side = i % 2 == 0 ? -1 : 1;
                    Emit(ParticleKind.Heart, side * (0.3f + 0.12f * Rand()), -0.25f - 0.1f * Rand(),
                         side * (0.12f + 0.12f * Rand()), -(0.45f + 0.25f * Rand()), 0.15f + 0.05f * Rand(),
                         life: 1.2f + 0.3f * Rand(), delay: i * 0.12f, spin: side * 0.6f);
                }
                break;
            case Emote.Surprised:
                StartHop(0.11, 0.42);
                break;
            case Emote.Proud:
                for (var i = 0; i < 3; i++)
                {
                    var a = -MathF.PI / 2 + (i - 1) * 0.8f;
                    Emit(ParticleKind.Star, MathF.Cos(a) * 0.5f, MathF.Sin(a) * 0.5f, MathF.Cos(a) * 0.25f, MathF.Sin(a) * 0.25f,
                         0.13f, life: 0.9f, delay: i * 0.1f, spin: 2f);
                }
                break;
            case Emote.Yawn:
                _stretchT = 0;
                break;
        }
        _dirty = true;
    }

    // ---- Pointer ----

    public void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        if (!expanded) EndHover();
        _dirty = true;
    }

    /// <summary>Cursor offset from the face centre in DIPs (host only calls this within 420 px).</summary>
    public void SetGaze(double dxDip, double dyDip)
    {
        var yaw = Math.Tanh(dxDip / 260) * 0.62;
        var pitch = Math.Tanh(dyDip / 200) * 0.5;
        if (Math.Abs(yaw - _cursorYaw) < 1e-4 && Math.Abs(pitch - _cursorPitch) < 1e-4) return;
        _cursorYaw = yaw; _cursorPitch = pitch;
        _dirty = true;
    }

    public void ClearGaze() => SetGaze(0, 0);

    public void UpdateFacePointer(bool insideFace, double xDip, double yDip)
    {
        if (!insideFace || !_expanded)
        {
            if (_pointerInside) EndHover();
            _pointerInside = false;
            return;
        }

        if (!_pointerInside)
        {
            _pointerInside = true;
            _anchorX = xDip; _anchorY = yDip;
            if (!OverrideActive && LoveReady)
            {
                _hoverActive = true;
                _loveWait = LoveWait;
                StartBlink();
                SoundRequested?.Invoke(CharacterSound.Hover);
                _dirty = true;
            }
            return;
        }

        var dx = xDip - _anchorX; var dy = yDip - _anchorY;
        if (dx * dx + dy * dy > HoverRearmDip * HoverRearmDip)
        {
            _anchorX = xDip; _anchorY = yDip;
            if (!OverrideActive && LoveReady)
            {
                if (!_hoverActive) { _hoverActive = true; _dirty = true; }
                _loveWait = LoveWait;
            }
        }
    }

    bool LoveReady => _time - _lastLoveTime >= LoveCooldown;

    void EndHover()
    {
        _pointerInside = false;
        _hoverActive = false;
        _loveWait = null;
        _dirty = true;
    }

    /// <summary>Returns true when this slap triggered dizzy (3 within 1.7 s). Ignored while dizzy or not expanded.</summary>
    public bool Slap()
    {
        if (_dizzy || !_expanded) return false;

        _slaps.RemoveAll(t => _time - t > SlapWindow + 1e-9);
        _slaps.Add(_time);
        _squashT = 0;
        var side = _rng.NextDouble() < 0.5 ? -1 : 1;
        _jvx += side * (0.5 + 0.8 * _rng.NextDouble());
        _jvy -= 1.3;
        _loveWait = null;
        _dirty = true;
        SoundRequested?.Invoke(CharacterSound.Slap);

        if (_slaps.Count >= 3)
        {
            _slaps.Clear();
            _dizzy = true;
            _dizzyRemaining = DizzySeconds;
            _emote = null;
            Refresh();
            SoundRequested?.Invoke(CharacterSound.Dizzy);
            DizzyStarted?.Invoke();
            return true;
        }

        PlayEmote(Emote.Annoyed, TimeSpan.FromSeconds(AnnoyedSeconds));
        SoundRequested?.Invoke(CharacterSound.Annoyed);
        return false;
    }

    public void Appear()
    {
        _squashT = 0;
        _jvy -= 0.7;
        _dirty = true;
    }

    // ---- Simulation ----

    public void Step(double dt)
    {
        if (double.IsNaN(dt) || dt < 0) dt = 0;
        var timerDt = dt;            // timers use real time so a sleeping host can wake straight into the next event
        dt = Math.Min(dt, MaxDt);    // animation uses the capped frame time
        _time += timerDt;

        AdvanceTimers(timerDt);
        Refresh();

        var animating = false;
        var cfg = _cfg;
        var f = (float)dt;

        // Colours
        animating |= Approach(ref _body, cfg.Body, dt, ColorTau);
        animating |= Approach(ref _tint, cfg.Accent, dt, ColorTau);
        animating |= Approach(ref _tintAmount, cfg.TintAmount, dt, ColorTau, 0.002f);
        var glow = cfg.Glow; var glowOpacity = cfg.GlowOpacity;
        if (_emote == Emote.Annoyed && !_dizzy) { glow = Rgb.FromHex(0xC4A1FF); glowOpacity = Math.Max(glowOpacity, 0.55f); }
        animating |= Approach(ref _glow, glow, dt, ColorTau);
        animating |= Approach(ref _glowOpacity, glowOpacity, dt, ColorTau, 0.002f);
        if (_badgeShown == _badgeTarget) animating |= Approach(ref _badgeColor, BadgeColorFor(_badgeShown, cfg), dt, ColorTau);

        // Continuous motion amplitudes
        var m = cfg.Motion;
        animating |= Approach(ref _bounceAmp, m.HasFlag(StateMotion.Bounce) ? 0.05f : 0, dt, 0.12, 0.0005f);
        animating |= Approach(ref _breathAmp, m.HasFlag(StateMotion.Breathe) ? 0.035f : 0, dt, 0.3, 0.0005f);
        animating |= Approach(ref _swayAmp, m.HasFlag(StateMotion.Sway) ? 1f : 0, dt, 0.15, 0.002f);
        animating |= Approach(ref _scanAmp, m.HasFlag(StateMotion.Scan) ? 1f : 0, dt, 0.15, 0.002f);
        if (_bounceAmp > 0) { _bouncePhase += dt * Math.PI * 2.2; animating = true; } else _bouncePhase = 0;
        if (_breathAmp > 0) { _breathPhase += dt * Math.PI * 2 / 3.2; animating = true; } else _breathPhase = 0;
        if (_swayAmp > 0) { _swayPhase += dt * SwaySpeed; animating = true; } else _swayPhase = 0;
        if (_scanAmp > 0) { _scanPhase += dt * ScanSpeed; animating = true; } else _scanPhase = 0;
        if (_effective == CharacterState.Dizzy) { _spiral += f * 7f; animating = true; }

        // Gaze
        double gy, gp;
        if (_effective == CharacterState.Thinking) { gy = 0.42 + 0.15 * _cursorYaw; gp = -0.38 + 0.1 * _cursorPitch; }
        else if (_effective == CharacterState.Sleeping) { gy = 0; gp = 0.35; }
        else if (_effective == CharacterState.Dizzy) { gy = 0; gp = 0; }
        else
        {
            gy = _cursorYaw * (1 - _scanAmp) + Math.Sin(_scanPhase) * 0.6 * _scanAmp;
            gp = _cursorPitch;
        }
        animating |= Approach(ref _yaw, (float)gy, dt, GazeTau, 0.0005f);
        animating |= Approach(ref _pitch, (float)gp, dt, GazeTau, 0.0005f);

        // Tilt, eye scale, blush
        animating |= Approach(ref _tilt, m.HasFlag(StateMotion.Tilt) ? 0.17f : 0, dt, 0.12, 0.0005f);
        var eyeScale = _hoverActive && !_dizzy ? 1.08f : 1f;
        if (_emote == Emote.Surprised && !_dizzy) eyeScale *= 1.15f;
        animating |= Approach(ref _eyeScale, eyeScale, dt, 0.07, 0.001f);
        animating |= Approach(ref _blush, _emote == Emote.Love && !_dizzy ? 1f : 0, dt, 0.12, 0.003f);

        // One-shots
        animating |= Advance(ref _blinkT, dt, BlinkDuration);
        animating |= Advance(ref _squashT, dt, SquashDuration);
        animating |= Advance(ref _shakeT, dt, ShakeDuration);
        animating |= Advance(ref _hopT, dt, _hopDuration);
        animating |= Advance(ref _stretchT, dt, 1.3);
        var rollActive = Advance(ref _rollT, dt, _rollDuration);
        animating |= rollActive;
        animating |= StepBadge(dt);
        animating |= StepSpring(dt);
        animating |= StepParticles(f, cfg);

        _animating = animating;
        _dirty = false;
        ComposePose();
        _nextWake = ComputeNextWake();
    }

    void AdvanceTimers(double dt)
    {
        if (dt <= 0) return;

        if (_flash.HasValue && (_flashRemaining -= dt) <= 0) _flash = null;

        if (_dizzy && (_dizzyRemaining -= dt) <= 0)
        {
            _dizzy = false;
            Refresh();
            DizzyEnded?.Invoke();
            PlayEmote(Emote.Happy);
            SoundRequested?.Invoke(CharacterSound.Happy);
        }

        if (_emote.HasValue)
        {
            _emoteAge += dt;
            if ((_emoteRemaining -= dt) <= 0) _emote = null;
        }

        if (_loveWait.HasValue)
        {
            if (OverrideActive) _loveWait = null;
            else if ((_loveWait -= dt) <= 0)
            {
                _loveWait = null;
                _lastLoveTime = _time;
                PlayEmote(Emote.Love);
                SoundRequested?.Invoke(CharacterSound.Love);
            }
        }

        if (_doubleBlinkTimer >= 0 && (_doubleBlinkTimer -= dt) < 0)
        {
            if (_cfg.Blinks) StartBlink();
        }

        if ((_blinkTimer -= dt) <= 0)
        {
            if (_cfg.Blinks)
            {
                StartBlink();
                if (_rng.NextDouble() < DoubleBlinkChance) _doubleBlinkTimer = DoubleBlinkDelay;
            }
            ScheduleBlink();
        }
    }

    void ScheduleBlink() => _blinkTimer = BlinkMin + _rng.NextDouble() * (BlinkMax - BlinkMin);

    void StartBlink()
    {
        if (!_cfg.Blinks) return;
        _blinkT = 0;
        _dirty = true;
    }

    void StartHop(double height, double duration)
    {
        _hopT = 0; _hopHeight = height; _hopDuration = duration;
    }

    void StartRoll(double turns, double duration)
    {
        _rollT = 0; _rollTurns = turns; _rollDuration = duration;
    }

    /// <summary>Recomputes the effective state and fires entry one-shots on change.</summary>
    void Refresh()
    {
        _dirty = true;
        var next = _dizzy ? CharacterState.Dizzy : _flash ?? _held ?? _alert ?? _dialog ?? CharacterState.Idle;
        var cfg = StateConfig.For(next);
        SetBadgeTarget(cfg.Badge);
        if (next == _effective) return;

        _effective = next;
        _cfg = cfg;
        switch (next)
        {
            case CharacterState.Approval:
                StartHop(0.14, 0.42);
                break;
            case CharacterState.Question:
                _blinkT = 0;
                break;
            case CharacterState.Error:
                _shakeT = 0;
                break;
            case CharacterState.Finished:
                StartRoll(1, FinishedRollSeconds);
                for (var i = 0; i < 5; i++)
                {
                    var a = -MathF.PI / 2 + (i - 2) * 0.62f + (Rand() - 0.5f) * 0.2f;
                    Emit(ParticleKind.Spark, MathF.Cos(a) * 0.42f, MathF.Sin(a) * 0.42f, MathF.Cos(a) * 0.7f, MathF.Sin(a) * 0.7f,
                         0.12f + 0.04f * Rand(), life: 0.75f, delay: 0.25f + i * 0.04f, spin: 3f);
                }
                break;
            case CharacterState.Dizzy:
                StartRoll(2, DizzyRollSeconds);
                _blinkT = -1;
                _doubleBlinkTimer = -1;
                break;
            case CharacterState.Sleeping:
                _zTimer = 0.4;
                break;
            case CharacterState.RateLimited:
                _sweatTimer = 0.2;
                break;
        }
    }

    // ---- Badge ----

    void SetBadgeTarget(BadgeKind target)
    {
        if (target == _badgeTarget) return;
        _badgeTarget = target;
        if (_badgePhase == BadgePhase.Out) return;
        if (_badgeShown == BadgeKind.None)
        {
            _badgeShown = target;
            _badgeColor = BadgeColorFor(target, StateConfig.For(_effective));
            _badgePhase = BadgePhase.In; _badgeT = 0;
        }
        else if (_badgeShown != target || _badgePhase == BadgePhase.In)
        {
            _badgePhase = BadgePhase.Out; _badgeT = 0; _badgeOutFrom = _badgeScale;
        }
    }

    bool StepBadge(double dt)
    {
        switch (_badgePhase)
        {
            case BadgePhase.Out:
                _badgeT += dt;
                var u = Math.Min(1, _badgeT / BadgeOutSeconds);
                _badgeScale = _badgeOutFrom * (float)(1 - u * u);
                if (u >= 1)
                {
                    _badgeShown = _badgeTarget;
                    _badgeScale = 0;
                    if (_badgeShown == BadgeKind.None) _badgePhase = BadgePhase.Idle;
                    else
                    {
                        _badgeColor = BadgeColorFor(_badgeShown, _cfg);
                        _badgePhase = BadgePhase.In; _badgeT = 0;
                    }
                }
                return true;
            case BadgePhase.In:
                _badgeT += dt;
                var v = Math.Min(1, _badgeT / BadgeInSeconds);
                _badgeScale = (float)Ease.OutBack(v);
                if (v >= 1) { _badgeScale = 1; _badgePhase = BadgePhase.Idle; }
                return true;
            default:
                _badgeScale = _badgeShown == BadgeKind.None ? 0 : 1;
                return false;
        }
    }

    static Rgb BadgeColorFor(BadgeKind badge, StateConfig cfg) => badge switch
    {
        BadgeKind.Exclamation => Rgb.FromHex(0xF5A524),
        BadgeKind.Question => Rgb.FromHex(0x22D3EE),
        BadgeKind.RedDot => Rgb.FromHex(0xF4505E),
        BadgeKind.GreenDot => Rgb.FromHex(0x34D399),
        BadgeKind.OrangeDot => Rgb.FromHex(0xFB923C),
        BadgeKind.Dots => cfg.Badge == BadgeKind.Dots ? cfg.Accent : Rgb.FromHex(0x3B9EFF),
        _ => cfg.Accent,
    };

    // ---- Physics ----

    bool StepSpring(double dt)
    {
        if (Math.Abs(_jx) < 1e-4 && Math.Abs(_jy) < 1e-4 && Math.Abs(_jvx) < 1e-3 && Math.Abs(_jvy) < 1e-3)
        {
            _jx = _jy = _jvx = _jvy = 0;
            return false;
        }
        var remaining = dt;
        while (remaining > 1e-9)
        {
            var h = Math.Min(SpringSubstep, remaining);
            _jvx += (-JellyStiffness * _jx - JellyDamping * _jvx) * h;
            _jvy += (-JellyStiffness * _jy - JellyDamping * _jvy) * h;
            _jx += _jvx * h;
            _jy += _jvy * h;
            remaining -= h;
        }
        return true;
    }

    float Rand() => (float)_rng.NextDouble();

    void Emit(ParticleKind kind, float x, float y, float vx, float vy, float size, float life, float delay = 0, float spin = 0, float gravity = 0, float grow = 0)
    {
        _sims.Add(new Sim
        {
            Kind = kind, X = x, Y = y, Vx = vx, Vy = vy, Size = size, Life = life, Age = -delay,
            Rot = (Rand() - 0.5f) * 0.6f, Spin = spin * (Rand() + 0.5f), Gravity = gravity, Grow = grow,
        });
        _dirty = true;
    }

    bool StepParticles(float dt, StateConfig cfg)
    {
        if (cfg.Motion.HasFlag(StateMotion.Zzz) && (_zTimer -= dt) <= 0)
        {
            _zTimer = 1.15;
            Emit(ParticleKind.Z, 0.3f, -0.42f, 0.12f, -0.22f, 0.11f, life: 2.2f, spin: 0, grow: 0.06f);
        }
        if (cfg.Motion.HasFlag(StateMotion.Sweat) && (_sweatTimer -= dt) <= 0)
        {
            _sweatTimer = 1.0;
            // Right side only: the badge sits top-left.
            Emit(ParticleKind.Sweat, 0.44f + 0.04f * Rand(), -0.22f - 0.1f * Rand(), 0.22f, -0.2f, 0.16f, life: 0.9f, gravity: 1.1f);
        }

        _particles.Clear();
        for (var i = _sims.Count - 1; i >= 0; i--)
        {
            var s = _sims[i];
            s.Age += dt;
            if (s.Age >= s.Life) { _sims.RemoveAt(i); continue; }
            if (s.Age > 0)
            {
                s.Vy += s.Gravity * dt;
                s.X += s.Vx * dt; s.Y += s.Vy * dt;
                s.Rot += s.Spin * dt;
                s.Size += s.Grow * dt;
                if (s.Kind == ParticleKind.Spark) { s.Vx *= 1 - 3 * dt; s.Vy *= 1 - 3 * dt; }
            }
            _sims[i] = s;
        }
        for (var i = 0; i < _sims.Count; i++)
        {
            var s = _sims[i];
            if (s.Age <= 0) continue;
            var u = s.Age / s.Life;
            var alpha = Math.Clamp(MathF.Min(u / 0.12f, (1 - u) / 0.35f), 0, 1);
            var size = s.Size;
            if (s.Kind is ParticleKind.Heart or ParticleKind.Star or ParticleKind.Spark)
                size *= (float)Ease.OutBack(Math.Min(1, u / 0.25)) * (s.Kind == ParticleKind.Spark ? 1 - 0.5f * u : 1);
            var wobble = s.Kind == ParticleKind.Z ? MathF.Sin(s.Age * 3f) * 0.03f : 0;
            _particles.Add(new Particle(s.Kind, s.X + wobble, s.Y, size, s.Rot, alpha));
        }
        return _sims.Count > 0;
    }

    // ---- Output ----

    void ComposePose()
    {
        var p = _pose;
        p.Body = _body; p.Tint = _tint; p.TintAmount = _tintAmount;
        p.Glow = _glow; p.GlowOpacity = _glowOpacity;

        p.Eyes = _dizzy ? EyeShape.Spiral : _emote switch
        {
            Emote.Love => EyeShape.Heart,
            Emote.Surprised => EyeShape.Dot,
            Emote.Proud => EyeShape.Star,
            Emote.Wink => EyeShape.Wink,
            Emote.Yawn => EyeShape.Tired,
            Emote.Happy => EyeShape.Happy,
            Emote.Annoyed => EyeShape.Annoyed,
            _ => _cfg.Eyes,
        };
        p.EyeScale = _eyeScale;
        p.EyeOpen = _blinkT >= 0 ? BlinkOpen(_blinkT) : 1f;
        p.SpiralPhase = _spiral;

        var sway = (float)Math.Sin(_swayPhase) * _swayAmp;
        p.Yaw = Math.Clamp(_yaw + sway * 0.35f, -1, 1);
        p.Pitch = Math.Clamp(_pitch, -1, 1);
        p.Tilt = _tilt + sway * 0.2f;

        p.Roll = _rollT >= 0 ? (float)(Ease.InOutCubic(_rollT / _rollDuration) * _rollTurns * Math.PI * 2) : 0;

        float sx = 1, sy = 1;
        if (_squashT >= 0) (sx, sy) = Squash(_squashT);
        if (_breathAmp > 0)
        {
            var b = (float)Math.Sin(_breathPhase) * _breathAmp;
            sy *= 1 + b; sx *= 1 + b * 0.55f;
        }
        if (_stretchT >= 0)
        {
            var s = (float)Math.Sin(Math.PI * Math.Min(1, _stretchT / 1.3)) * 0.06f;
            sy *= 1 + s; sx *= 1 - s * 0.5f;
        }

        var bounce = (float)Math.Abs(Math.Sin(_bouncePhase)) * _bounceAmp;
        if (_bounceAmp > 0)
        {
            // Brief squash as the bounce touches down.
            var contact = MathF.Pow(1 - MathF.Min(1, bounce / (_bounceAmp * 0.35f)), 2) * (_bounceAmp / 0.05f);
            sy *= 1 - 0.06f * contact; sx *= 1 + 0.05f * contact;
        }
        p.ScaleX = sx; p.ScaleY = sy;

        var hop = _hopT >= 0 ? (float)(Math.Sin(Math.PI * Math.Min(1, _hopT / _hopDuration)) * _hopHeight) : 0;
        var shake = _shakeT >= 0 ? (float)(Math.Sin(_shakeT / ShakeDuration * Math.PI * 2 * 4) * 0.06 * (1 - _shakeT / ShakeDuration)) : 0;
        p.OffsetX = (float)_jx + shake + sway * 0.035f;
        p.OffsetY = (float)_jy - hop - bounce;

        p.Badge = _badgeShown;
        p.BadgeScale = _badgeScale;
        p.BadgeColor = _badgeColor;
        p.Blush = _blush;
        p.Particles = _particles;
    }

    TimeSpan? ComputeNextWake()
    {
        var next = double.PositiveInfinity;
        if (_cfg.Blinks) next = Math.Min(next, Math.Max(0, _blinkTimer));
        if (_doubleBlinkTimer >= 0) next = Math.Min(next, _doubleBlinkTimer);
        if (_loveWait.HasValue) next = Math.Min(next, _loveWait.Value);
        if (_flash.HasValue) next = Math.Min(next, _flashRemaining);
        if (_dizzy) next = Math.Min(next, _dizzyRemaining);
        if (_emote.HasValue) next = Math.Min(next, _emoteRemaining);
        return double.IsPositiveInfinity(next) ? null : TimeSpan.FromSeconds(Math.Max(0, next));
    }

    static float BlinkOpen(double t)
    {
        const double close = 0.06, hold = 0.02;
        if (t < close) return (float)(1 - t / close * 0.95);
        if (t < close + hold) return 0.05f;
        return (float)Math.Min(1, 0.05 + (t - close - hold) / (BlinkDuration - close - hold) * 0.95);
    }

    /// <summary>Height 1→0.78→1.10→1.0, width 1→1.16→0.95→1.0 over ≈370 ms.</summary>
    static (float, float) Squash(double t)
    {
        ReadOnlySpan<double> times = [0, 0.06, 0.19, SquashDuration];
        ReadOnlySpan<double> w = [1, 1.16, 0.95, 1];
        ReadOnlySpan<double> h = [1, 0.78, 1.10, 1];
        for (var i = 0; i < times.Length - 1; i++)
        {
            if (t <= times[i + 1])
            {
                var u = Ease.InOutSine((t - times[i]) / (times[i + 1] - times[i]));
                return ((float)(w[i] + (w[i + 1] - w[i]) * u), (float)(h[i] + (h[i + 1] - h[i]) * u));
            }
        }
        return (1, 1);
    }

    static bool Advance(ref double t, double dt, double duration)
    {
        if (t < 0) return false;
        t += dt;
        if (t >= duration) { t = -1; return false; }
        return true;
    }

    static bool Approach(ref float value, float target, double dt, double tau, float eps)
    {
        if (Math.Abs(value - target) <= eps) { value = target; return false; }
        value += (target - value) * (float)(1 - Math.Exp(-dt / tau));
        if (Math.Abs(value - target) <= eps) { value = target; return false; }
        return true;
    }

    static bool Approach(ref Rgb value, Rgb target, double dt, double tau)
    {
        if (value.DistanceTo(target) <= 0.002f) { value = target; return false; }
        value = Rgb.Lerp(value, target, (float)(1 - Math.Exp(-dt / tau)));
        if (value.DistanceTo(target) <= 0.002f) { value = target; return false; }
        return true;
    }
}

internal static class Ease
{
    public static double InOutCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    public static double InOutSine(double t) => -(Math.Cos(Math.PI * Math.Clamp(t, 0, 1)) - 1) / 2;

    public static double OutBack(double t)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        t = Math.Clamp(t, 0, 1);
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}
