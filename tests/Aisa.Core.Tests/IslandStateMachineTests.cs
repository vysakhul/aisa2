using Aisa.Core.Animation;
using Aisa.Core.Island;
using Microsoft.Extensions.Time.Testing;

namespace Aisa.Core.Tests;

public class IslandStateMachineTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));

    private IslandStateMachine Create(IslandOptions? options = null)
    {
        SynchronizationContext.SetSynchronizationContext(null); // timer callbacks run inline under FakeTimeProvider
        return new IslandStateMachine(_time, options);
    }

    private void Advance(double seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

    private static IslandStateMachine AfterGreeting(IslandStateMachine sm, FakeTimeProvider time)
    {
        sm.Start();
        sm.GreetingFinished();
        time.Advance(TimeSpan.FromSeconds(0.61));
        return sm;
    }

    [Fact]
    public void Greeting_folds_to_compact_after_600ms_when_not_hovered()
    {
        var sm = Create();
        sm.Start();
        Assert.Equal((IslandMode.Expanded, IslandView.Greeting), (sm.Snapshot.Mode, sm.Snapshot.View));

        sm.GreetingFinished();
        Advance(0.59);
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);
        Advance(0.02);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
        Assert.Equal(IslandView.Chat, sm.Snapshot.View);
    }

    [Fact]
    public void Greeting_waits_10s_when_hovered_and_folds_immediately_on_leave()
    {
        var sm = Create();
        sm.Start();
        sm.PointerEntered();
        sm.GreetingFinished();
        Advance(5);
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);

        sm.PointerLeft();
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
    }

    [Fact]
    public void Compact_hides_to_closed_10s_after_pointer_leaves_and_pointer_on_notch_wakes_it()
    {
        var sm = AfterGreeting(Create(), _time);
        Advance(9.9);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
        Advance(0.2);
        Assert.Equal(IslandMode.Closed, sm.Snapshot.Mode);

        sm.PointerEntered();
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
        Advance(60);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode); // never hides while the pointer is on it
    }

    [Fact]
    public void Face_rest_opens_after_500ms_and_leaving_the_face_cancels()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.PointerEntered();
        sm.FaceEntered();
        Advance(0.3);
        sm.FaceLeft();
        Advance(1);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);

        sm.FaceEntered();
        Advance(0.5);
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);
        Assert.True(sm.Snapshot.HoverOpened);
    }

    [Fact]
    public void Hover_opened_island_folds_600ms_after_leaving_unless_clicked()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.PointerEntered();
        sm.FaceEntered();
        Advance(0.5);
        sm.PointerLeft();
        Assert.Equal(TimeSpan.FromSeconds(0.6), sm.Snapshot.CollapseTotal);
        Advance(0.61);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
    }

    [Fact]
    public void Click_inside_converts_hover_open_into_normal_15s_open()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.PointerEntered();
        sm.FaceEntered();
        Advance(0.5);
        sm.Click();
        Assert.False(sm.Snapshot.HoverOpened);

        sm.PointerLeft();
        Assert.Equal(TimeSpan.FromSeconds(15), sm.Snapshot.CollapseTotal);
        Advance(14.9);
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);
        Advance(0.2);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
    }

    [Fact]
    public void Click_on_compact_opens_and_reentering_cancels_the_countdown()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.PointerEntered();
        sm.Click();
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);
        Assert.False(sm.Snapshot.HoverOpened);

        sm.PointerLeft();
        Assert.NotNull(sm.Snapshot.CollapseDueAt);
        Advance(10);
        sm.PointerEntered();
        Assert.Null(sm.Snapshot.CollapseDueAt);
        Advance(30);
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);
    }

    [Fact]
    public void Activity_restarts_countdown_from_full()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.Open();                // tray open: pointer is outside, so the countdown runs
        Advance(10);
        sm.Activity();
        Advance(10);
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);
        Advance(5.1);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
    }

    [Fact]
    public void Changing_auto_close_restarts_running_countdown_with_new_value()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.Open();
        Advance(10);
        sm.UpdateOptions(sm.Options with { AutoClose = TimeSpan.FromSeconds(30) });
        Advance(29.9);
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);
        Advance(0.2);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
    }

    [Fact]
    public void Close_returns_to_compact()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.Open();
        sm.CloseRequested();
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
    }

    [Fact]
    public void Alert_forces_expanded_pinned_and_survives_leave_and_timers()
    {
        var sm = AfterGreeting(Create(), _time);
        Advance(11);
        Assert.Equal(IslandMode.Closed, sm.Snapshot.Mode);

        sm.RaiseAlert();
        Assert.Equal((IslandMode.Expanded, IslandView.Alert, true), (sm.Snapshot.Mode, sm.Snapshot.View, sm.Snapshot.Pinned));

        sm.PointerEntered();
        sm.PointerLeft();
        Advance(600);
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);
        Assert.Null(sm.Snapshot.CollapseDueAt);
    }

    [Fact]
    public void Closing_an_alert_keeps_it_pinned_in_compact_and_click_reopens_the_alert()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.RaiseAlert();
        sm.CloseRequested();
        Assert.Equal((IslandMode.Compact, true), (sm.Snapshot.Mode, sm.Snapshot.Pinned));

        Advance(600);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode); // pinned: never auto-hides

        sm.PointerEntered();
        sm.Click();
        Assert.Equal((IslandMode.Expanded, IslandView.Alert), (sm.Snapshot.Mode, sm.Snapshot.View));
    }

    [Fact]
    public void Clearing_alerts_unpins_and_folds_to_compact_chat()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.RaiseAlert();
        sm.AlertsCleared();
        Assert.Equal((IslandMode.Compact, IslandView.Chat, false), (sm.Snapshot.Mode, sm.Snapshot.View, sm.Snapshot.Pinned));
        Advance(10.1);
        Assert.Equal(IslandMode.Closed, sm.Snapshot.Mode);
    }

    [Fact]
    public void Alerts_during_greeting_are_held_until_it_ends()
    {
        var sm = Create();
        sm.Start();
        sm.RaiseAlert();
        Assert.Equal(IslandView.Greeting, sm.Snapshot.View);

        sm.GreetingFinished();
        Assert.Equal((IslandMode.Expanded, IslandView.Alert, true), (sm.Snapshot.Mode, sm.Snapshot.View, sm.Snapshot.Pinned));
    }

    [Fact]
    public void Hide_is_ignored_while_pinned()
    {
        var sm = AfterGreeting(Create(), _time);
        sm.RaiseAlert();
        sm.Hide();
        Assert.Equal(IslandMode.Expanded, sm.Snapshot.Mode);
    }

    [Fact]
    public void Face_hover_open_can_be_disabled()
    {
        var sm = AfterGreeting(Create(new IslandOptions { OpenOnFaceHover = false }), _time);
        sm.PointerEntered();
        sm.FaceEntered();
        Advance(2);
        Assert.Equal(IslandMode.Compact, sm.Snapshot.Mode);
    }
}

public class AnimationTests
{
    [Fact]
    public void Grow_spring_overshoots_3_to_4_percent_and_settles_near_half_a_second()
    {
        var v = new TrackedValue(0);
        v.AnimateSpring(100);
        double peak = 0, t = 0, settledAt = 0;
        while (t < 2)
        {
            v.Step(1.0 / 60);
            t += 1.0 / 60;
            peak = Math.Max(peak, v.Value);
            if (Math.Abs(v.Value - 100) >= 1) settledAt = t; // last frame outside ±1 %
        }
        Assert.InRange(peak, 103, 104.5);
        Assert.InRange(settledAt, 0.4, 0.7);
        Assert.False(v.IsMoving);
        Assert.Equal(100, v.Value);
    }

    [Fact]
    public void Shrink_curve_never_overshoots_and_finishes_in_340ms()
    {
        var v = new TrackedValue(100);
        v.AnimateCurve(0, 0.34, CubicBezier.Shrink);
        double min = 100;
        for (int i = 0; i < 22; i++) // 22 frames ≈ 367 ms
        {
            v.Step(1.0 / 60);
            min = Math.Min(min, v.Value);
        }
        Assert.True(min >= 0);
        Assert.False(v.IsMoving);
        Assert.Equal(0, v.Value);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    public void Bezier_endpoints(double x, double y) => Assert.Equal(y, CubicBezier.Shrink.Evaluate(x), 6);

    [Fact]
    public void Bezier_is_monotonic()
    {
        double previous = 0;
        for (int i = 1; i <= 100; i++)
        {
            double y = CubicBezier.Shrink.Evaluate(i / 100.0);
            Assert.True(y >= previous - 1e-9);
            previous = y;
        }
    }
}
