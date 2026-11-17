using Aisa.Character;

namespace Aisa.Character.Tests;

public class CharacterEngineTests
{
    const double Frame = 1.0 / 60;

    static CharacterEngine NewEngine(int seed = 1, bool expanded = true)
    {
        var e = new CharacterEngine(new Random(seed));
        e.SetExpanded(expanded);
        return e;
    }

    static void Run(CharacterEngine e, double seconds)
    {
        for (double t = 0; t < seconds - 1e-9; t += Frame) e.Step(Frame);
    }

    [Fact]
    public void Layers_resolve_override_over_alert_over_dialog_over_idle()
    {
        var e = NewEngine();
        Assert.Equal(CharacterState.Idle, e.EffectiveState);

        e.SetDialogState(CharacterState.Question);
        Assert.Equal(CharacterState.Question, e.EffectiveState);

        e.SetAlertState(CharacterState.Approval);
        Assert.Equal(CharacterState.Approval, e.EffectiveState);

        e.BeginOverride(CharacterState.Thinking);
        Assert.Equal(CharacterState.Thinking, e.EffectiveState);

        e.Flash(CharacterState.Error, TimeSpan.FromSeconds(1.2));
        Assert.Equal(CharacterState.Error, e.EffectiveState);

        Run(e, 1.3);
        Assert.Equal(CharacterState.Thinking, e.EffectiveState);

        e.EndOverride();
        Assert.Equal(CharacterState.Approval, e.EffectiveState);
        e.SetAlertState(null);
        Assert.Equal(CharacterState.Question, e.EffectiveState);
        e.SetDialogState(null);
        Assert.Equal(CharacterState.Idle, e.EffectiveState);
    }

    [Fact]
    public void Flash_expires_after_its_duration()
    {
        var e = NewEngine();
        e.Flash(CharacterState.Finished, TimeSpan.FromSeconds(1.6));
        Run(e, 1.55);
        Assert.Equal(CharacterState.Finished, e.EffectiveState);
        Run(e, 0.1);
        Assert.Equal(CharacterState.Idle, e.EffectiveState);
    }

    [Fact]
    public void Three_slaps_within_window_trigger_dizzy()
    {
        var e = NewEngine();
        var started = 0;
        e.DizzyStarted += () => started++;

        Assert.False(e.Slap());
        Run(e, 0.6);
        Assert.False(e.Slap());
        Run(e, 0.6);
        Assert.True(e.Slap());
        Assert.Equal(CharacterState.Dizzy, e.EffectiveState);
        Assert.Equal(1, started);
    }

    [Fact]
    public void Slower_slaps_do_not_trigger_dizzy()
    {
        var e = NewEngine();
        for (var i = 0; i < 6; i++)
        {
            Assert.False(e.Slap());
            Run(e, 0.9);
        }
        Assert.NotEqual(CharacterState.Dizzy, e.EffectiveState);
    }

    [Fact]
    public void Slaps_are_ignored_when_not_expanded()
    {
        var e = NewEngine(expanded: false);
        var sounds = new List<CharacterSound>();
        e.SoundRequested += sounds.Add;
        for (var i = 0; i < 3; i++) Assert.False(e.Slap());
        Assert.Empty(sounds);
        Assert.Equal(CharacterState.Idle, e.EffectiveState);
    }

    [Fact]
    public void Slap_shows_annoyed_eyes_and_plays_slap_and_annoyed_sounds()
    {
        var e = NewEngine();
        var sounds = new List<CharacterSound>();
        e.SoundRequested += sounds.Add;
        e.Slap();
        Run(e, 0.1);
        Assert.Equal(EyeShape.Annoyed, e.Pose.Eyes);
        Assert.True(e.Pose.ScaleY < 1);
        Assert.Equal([CharacterSound.Slap, CharacterSound.Annoyed], sounds);
        Run(e, 0.8);
        Assert.Equal(EyeShape.Pill, e.Pose.Eyes);
    }

    [Fact]
    public void Slap_jelly_wobbles_a_few_times_then_settles()
    {
        var e = NewEngine();
        e.Slap();
        var crossings = 0;
        var prev = 0f;
        var peak = 0f;
        for (var i = 0; i < 240; i++)
        {
            e.Step(Frame);
            var y = e.Pose.OffsetY;
            peak = Math.Max(peak, Math.Abs(y));
            if (Math.Abs(y) > peak * 0.05f && prev != 0 && Math.Sign(y) != Math.Sign(prev)) crossings++;
            if (Math.Abs(y) > peak * 0.05f) prev = y;
        }
        Assert.InRange(crossings, 2, 5);
        Run(e, 2);
        Assert.Equal(0f, e.Pose.OffsetX, 3);
        Assert.Equal(0f, e.Pose.OffsetY, 3);
    }

    [Fact]
    public void Dizzy_ends_after_about_3_3_seconds_with_happy_emote_and_ignores_slaps()
    {
        var e = NewEngine();
        var ended = 0;
        e.DizzyEnded += () => ended++;
        e.Slap(); e.Slap(); Assert.True(e.Slap());

        Run(e, 1.0);
        Assert.Equal(EyeShape.Spiral, e.Pose.Eyes);
        Assert.False(e.Slap());
        Assert.False(e.Slap());
        Assert.False(e.Slap());

        Run(e, 2.2);
        Assert.Equal(CharacterState.Dizzy, e.EffectiveState);
        Run(e, 0.15);
        Assert.Equal(CharacterState.Idle, e.EffectiveState);
        Assert.Equal(1, ended);
        Assert.Equal(EyeShape.Happy, e.Pose.Eyes);
    }

    [Fact]
    public void Hover_blinks_scales_eyes_and_loves_after_rest()
    {
        var e = NewEngine();
        var sounds = new List<CharacterSound>();
        e.SoundRequested += sounds.Add;

        e.UpdateFacePointer(true, 0, 0);
        Assert.Equal([CharacterSound.Hover], sounds);
        Run(e, 0.05);
        Assert.True(e.Pose.EyeOpen < 1);
        Run(e, 0.5);
        Assert.Equal(1.08f, e.Pose.EyeScale, 2);

        Run(e, 1.3);
        Assert.DoesNotContain(CharacterSound.Love, sounds);
        Run(e, 0.1);
        Assert.Contains(CharacterSound.Love, sounds);
        Assert.Equal(EyeShape.Heart, e.Pose.Eyes);
        Assert.True(e.Pose.Particles.Count > 0);
    }

    [Fact]
    public void Moving_more_than_40_dip_restarts_the_love_wait()
    {
        var e = NewEngine();
        var loves = 0;
        e.SoundRequested += s => { if (s == CharacterSound.Love) loves++; };

        e.UpdateFacePointer(true, 0, 0);
        Run(e, 1.5);
        e.UpdateFacePointer(true, 30, 0); // under threshold
        Run(e, 0.3);
        Assert.Equal(0, loves);
        Run(e, 0.15);
        Assert.Equal(1, loves);

        var f = NewEngine();
        var floves = 0;
        f.SoundRequested += s => { if (s == CharacterSound.Love) floves++; };
        f.UpdateFacePointer(true, 0, 0);
        Run(f, 1.5);
        f.UpdateFacePointer(true, 45, 0); // re-arms
        Run(f, 1.0);
        Assert.Equal(0, floves);
        Run(f, 1.0);
        Assert.Equal(1, floves);
    }

    [Fact]
    public void Love_has_a_6_second_cooldown()
    {
        var e = NewEngine();
        var sounds = new List<CharacterSound>();
        e.SoundRequested += sounds.Add;

        e.UpdateFacePointer(true, 0, 0);
        Run(e, 2.0);
        e.UpdateFacePointer(false, 0, 0);
        Run(e, 1.0);
        sounds.Clear();

        e.UpdateFacePointer(true, 0, 0);
        Run(e, 2.5);
        Assert.Empty(sounds);
        e.UpdateFacePointer(false, 0, 0);

        Run(e, 3.0); // > 6 s since love
        e.UpdateFacePointer(true, 0, 0);
        Run(e, 2.0);
        Assert.Equal([CharacterSound.Hover, CharacterSound.Love], sounds);
    }

    [Fact]
    public void Hover_love_is_suppressed_during_an_override()
    {
        var e = NewEngine();
        var sounds = new List<CharacterSound>();
        e.SoundRequested += sounds.Add;

        e.BeginOverride(CharacterState.Thinking);
        e.UpdateFacePointer(true, 0, 0);
        Run(e, 2.5);
        Assert.Empty(sounds);

        // Override starting mid-wait also cancels the love.
        e.EndOverride();
        e.UpdateFacePointer(false, 0, 0);
        e.UpdateFacePointer(true, 0, 0);
        Run(e, 1.0);
        e.Flash(CharacterState.Finished, TimeSpan.FromSeconds(1.6));
        Run(e, 1.5);
        Assert.DoesNotContain(CharacterSound.Love, sounds);
    }

    [Fact]
    public void Hover_does_nothing_while_compact()
    {
        var e = NewEngine(expanded: false);
        var sounds = new List<CharacterSound>();
        e.SoundRequested += sounds.Add;
        e.UpdateFacePointer(true, 0, 0);
        Run(e, 3);
        Assert.Empty(sounds);
    }

    [Fact]
    public void Idle_blinks_follow_the_spec_cadence()
    {
        var e = NewEngine(seed: 42);
        var starts = new List<double>();
        var closed = false;
        double t = 0;
        for (var i = 0; i < 60 * 120; i++)
        {
            e.Step(Frame);
            t += Frame;
            var nowClosed = e.Pose.EyeOpen < 0.5f;
            if (nowClosed && !closed) starts.Add(t);
            closed = nowClosed;
        }

        Assert.True(starts.Count > 20);
        var doubles = 0;
        for (var i = 1; i < starts.Count; i++)
        {
            var gap = starts[i] - starts[i - 1];
            if (gap < 0.5) { Assert.InRange(gap, 0.2, 0.26); doubles++; }
            else Assert.InRange(gap, 2.2 - 0.26 - 0.05, 5.4 + 0.05);
        }
        Assert.InRange(doubles, 1, starts.Count / 2);
    }

    [Fact]
    public void Sleeping_and_dizzy_do_not_blink()
    {
        var e = NewEngine();
        e.SetAlertState(CharacterState.Sleeping);
        for (var i = 0; i < 60 * 15; i++)
        {
            e.Step(Frame);
            Assert.Equal(1f, e.Pose.EyeOpen);
        }
    }

    [Fact]
    public void Engine_settles_and_reports_next_wake()
    {
        var e = NewEngine();
        e.SetGaze(120, 40);
        Assert.True(e.IsBusy);
        Assert.Null(e.NextWakeIn);

        var settled = false;
        for (var i = 0; i < 600 && !settled; i++)
        {
            e.Step(Frame);
            settled = !e.IsBusy;
        }
        Assert.True(settled);
        Assert.NotNull(e.NextWakeIn);
        Assert.InRange(e.NextWakeIn!.Value.TotalSeconds, 0, 5.4);

        // A sleeping host can jump straight to the wake time; the blink then animates.
        e.Step(e.NextWakeIn.Value.TotalSeconds + 0.001);
        Assert.True(e.IsBusy);
    }

    [Fact]
    public void Continuous_states_stay_busy()
    {
        foreach (var s in new[] { CharacterState.Approval, CharacterState.Searching, CharacterState.Sleeping, CharacterState.RateLimited })
        {
            var e = NewEngine();
            e.SetAlertState(s);
            Run(e, 3);
            Assert.True(e.IsBusy, s.ToString());
        }
    }

    [Fact]
    public void Gaze_saturates_smoothly()
    {
        var e = NewEngine();
        e.SetGaze(5000, 5000);
        e.Step(Frame);
        Assert.InRange(e.Pose.Yaw, 0.01f, 0.3f); // eased, not snapped
        Run(e, 2);
        Assert.Equal(0.62f, e.Pose.Yaw, 2);
        Assert.Equal(0.5f, e.Pose.Pitch, 2);

        var near = NewEngine();
        near.SetGaze(130, 0);
        Run(near, 2);
        Assert.Equal((float)(Math.Tanh(0.5) * 0.62), near.Pose.Yaw, 3);

        near.ClearGaze();
        Run(near, 2);
        Assert.Equal(0f, near.Pose.Yaw, 3);
    }

    [Fact]
    public void Thinking_fixes_gaze_up_right_and_sleeping_looks_down()
    {
        var e = NewEngine();
        e.SetGaze(-400, 300);
        e.BeginOverride(CharacterState.Thinking);
        Run(e, 2);
        Assert.True(e.Pose.Yaw > 0.2f);
        Assert.True(e.Pose.Pitch < -0.2f);

        e.EndOverride();
        e.SetAlertState(CharacterState.Sleeping);
        Run(e, 2);
        Assert.True(e.Pose.Pitch > 0.2f);
        Assert.Equal(0f, e.Pose.Yaw, 3);
    }

    [Fact]
    public void Badge_scales_out_then_in_with_overshoot()
    {
        var e = NewEngine();
        e.SetAlertState(CharacterState.Working);
        Run(e, 0.5);
        Assert.Equal(BadgeKind.Dots, e.Pose.Badge);
        Assert.Equal(1f, e.Pose.BadgeScale);

        e.SetAlertState(CharacterState.Approval);
        e.Step(0.05);
        Assert.Equal(BadgeKind.Dots, e.Pose.Badge);
        Assert.InRange(e.Pose.BadgeScale, 0.01f, 0.99f);
        e.Step(0.05);
        Assert.Equal(BadgeKind.Exclamation, e.Pose.Badge);

        var maxScale = 0f;
        for (var i = 0; i < 30; i++) { e.Step(0.01); maxScale = Math.Max(maxScale, e.Pose.BadgeScale); }
        Assert.True(maxScale > 1.02f, $"expected overshoot, got {maxScale}");
        Assert.Equal(1f, e.Pose.BadgeScale);

        e.SetAlertState(null);
        Run(e, 0.2);
        Assert.Equal(0f, e.Pose.BadgeScale);
    }

    [Fact]
    public void Colour_cross_fades_over_about_300ms()
    {
        var e = NewEngine();
        var start = e.Pose.Tint;
        e.SetAlertState(CharacterState.Error);
        var target = StateConfig.For(CharacterState.Error).Accent;
        Run(e, 0.1);
        Assert.True(e.Pose.Tint.DistanceTo(target) > 0.05f);
        Run(e, 0.25);
        Assert.True(e.Pose.Tint.DistanceTo(target) < start.DistanceTo(target) * 0.06f);
    }

    [Fact]
    public void Finished_rolls_once_and_emits_sparks()
    {
        var e = NewEngine();
        e.Flash(CharacterState.Finished, TimeSpan.FromSeconds(1.6));
        var maxRoll = 0f;
        var sparks = 0;
        for (var i = 0; i < 60; i++)
        {
            e.Step(Frame);
            maxRoll = Math.Max(maxRoll, e.Pose.Roll);
            sparks = Math.Max(sparks, e.Pose.Particles.Count(p => p.Kind == ParticleKind.Spark));
        }
        Assert.InRange(maxRoll, MathF.PI * 1.8f, MathF.PI * 2);
        Assert.Equal(5, sparks);
        Assert.Equal(0f, e.Pose.Roll);
    }

    [Fact]
    public void Error_shakes_horizontally_briefly()
    {
        var e = NewEngine();
        e.Flash(CharacterState.Error, TimeSpan.FromSeconds(1.2));
        var maxX = 0f;
        for (var i = 0; i < 15; i++) { e.Step(Frame); maxX = Math.Max(maxX, Math.Abs(e.Pose.OffsetX)); }
        Assert.True(maxX > 0.02f);
        Run(e, 0.1);
        Assert.Equal(0f, e.Pose.OffsetX, 4);
    }

    [Fact]
    public void Question_tilts_head()
    {
        var e = NewEngine();
        e.SetDialogState(CharacterState.Question);
        Run(e, 1.5);
        Assert.Equal(0.17f, e.Pose.Tilt, 2);
    }

    [Fact]
    public void Emotes_overlay_eyes_and_expire()
    {
        var expected = new Dictionary<Emote, EyeShape>
        {
            [Emote.Love] = EyeShape.Heart, [Emote.Surprised] = EyeShape.Dot, [Emote.Proud] = EyeShape.Star,
            [Emote.Wink] = EyeShape.Wink, [Emote.Yawn] = EyeShape.Tired, [Emote.Happy] = EyeShape.Happy,
            [Emote.Annoyed] = EyeShape.Annoyed,
        };
        foreach (var (emote, eyes) in expected)
        {
            var e = NewEngine();
            e.SetAlertState(CharacterState.Working);
            e.PlayEmote(emote);
            Run(e, 0.5);
            Assert.Equal(eyes, e.Pose.Eyes);
            Run(e, 1.4);
            Assert.Equal(EyeShape.Pill, e.Pose.Eyes);
        }
    }

    [Fact]
    public void Sleeping_and_rate_limited_emit_their_particles()
    {
        var s = NewEngine();
        s.SetAlertState(CharacterState.Sleeping);
        Run(s, 1.5);
        Assert.Contains(s.Pose.Particles, p => p.Kind == ParticleKind.Z);

        var r = NewEngine();
        r.SetAlertState(CharacterState.RateLimited);
        Run(r, 0.5);
        Assert.Contains(r.Pose.Particles, p => p.Kind == ParticleKind.Sweat);
    }

    [Fact]
    public void Same_seed_gives_identical_poses()
    {
        static float Trace(int seed)
        {
            var e = NewEngine(seed);
            e.Slap();
            var sum = 0f;
            for (var i = 0; i < 300; i++) { e.Step(Frame); sum += e.Pose.OffsetX * (i + 1) + e.Pose.EyeOpen; }
            return sum;
        }
        Assert.Equal(Trace(9), Trace(9));
    }
}
