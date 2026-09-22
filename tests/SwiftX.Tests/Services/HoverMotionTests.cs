using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The Animate hover style's motions as data. The window turns each <see cref="MotionSpec"/> into a
/// composition animation and cannot be run here, so what is guarded is the part a wrong number would
/// only reveal by hovering the right item for long enough: a loop that jumps every time round, a
/// swing that carries an item onto its neighbour, a symbol that is missing or that two motions share.
/// </summary>
public class HoverMotionTests
{
    public static TheoryData<HoverAnimation> Every => new(Enum.GetValues<HoverAnimation>());

    [Fact]
    public void The_picker_lists_every_animation_exactly_once()
    {
        Assert.Equal(
            Enum.GetValues<HoverAnimation>().OrderBy(a => a),
            HoverMotion.All.OrderBy(a => a));
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Every_animation_has_a_name_and_a_symbol(HoverAnimation animation)
    {
        Assert.False(string.IsNullOrWhiteSpace(HoverMotion.Label(animation)));
        Assert.False(string.IsNullOrEmpty(HoverMotion.Symbol(animation)));
    }

    /// <summary>
    /// Two motions sharing a symbol would leave the side pill unable to say which one is chosen.
    /// </summary>
    [Fact]
    public void No_two_animations_share_a_symbol_or_a_name()
    {
        Assert.Equal(HoverMotion.All.Count, HoverMotion.All.Select(HoverMotion.Symbol).Distinct().Count());
        Assert.Equal(HoverMotion.All.Count, HoverMotion.All.Select(HoverMotion.Label).Distinct().Count());
    }

    /// <summary>
    /// The symbols are drawn in Segoe UI Symbol, which has ordinary Unicode symbols and not the icon
    /// font's private-use glyphs: one of those would draw as a blank box there.
    /// </summary>
    [Theory]
    [MemberData(nameof(Every))]
    public void Symbols_are_ordinary_unicode_and_not_private_use(HoverAnimation animation)
    {
        foreach (char c in HoverMotion.Symbol(animation))
            Assert.False(c is >= (char)0xE000 and <= (char)0xF8FF, $"{animation}'s symbol is U+{(int)c:X4}, a private-use codepoint.");
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Keyframes_run_from_the_start_to_the_end_in_order(HoverAnimation animation)
    {
        var keys = HoverMotion.For(animation).Keys;

        Assert.True(keys.Count >= 2);
        Assert.Equal(0f, keys[0].At);
        Assert.Equal(1f, keys[^1].At);
        for (int i = 1; i < keys.Count; i++)
            Assert.True(keys[i].At > keys[i - 1].At, $"{animation}: key {i} does not come after key {i - 1}.");
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void A_motion_has_a_sensible_duration(HoverAnimation animation)
    {
        int ms = HoverMotion.For(animation).DurationMs;

        Assert.InRange(ms, 300, 3000);
    }

    /// <summary>
    /// A loop that ends somewhere other than where it started would jump on every pass. Scale is
    /// measured against a resting value of 1, the others against no travel and no rotation.
    /// </summary>
    [Theory]
    [MemberData(nameof(Every))]
    public void A_looping_motion_ends_where_it_begins(HoverAnimation animation)
    {
        var spec = HoverMotion.For(animation);
        if (!spec.Loops)
            return;

        Assert.Equal(spec.Keys[0].Value, spec.Keys[^1].Value);
        Assert.Equal(spec.Axis == MotionAxis.Scale ? 1f : 0f, spec.Keys[0].Value);
    }

    /// <summary>
    /// A single turn is the exception, and only Spin: it is not a loop precisely because a turn that
    /// repeated would leave no resting angle to settle back to, and 360 degrees is where it stops.
    /// </summary>
    [Fact]
    public void Spin_is_one_full_turn_and_the_only_motion_that_does_not_repeat()
    {
        var spin = HoverMotion.For(HoverAnimation.Spin);

        Assert.False(spin.Loops);
        Assert.Equal(MotionAxis.Rotate, spin.Axis);
        Assert.Equal(0f, spin.Keys[0].Value);
        Assert.Equal(360f, spin.Keys[^1].Value);
        Assert.Equal(
            [HoverAnimation.Spin],
            HoverMotion.All.Where(a => !HoverMotion.For(a).Loops).ToArray());
    }

    /// <summary>
    /// Travel is a fraction of the item's diameter, and the orbit only leaves room for so much of it
    /// before an item sits on its neighbour.
    /// </summary>
    [Theory]
    [MemberData(nameof(Every))]
    public void No_motion_carries_an_item_further_than_the_layout_allows(HoverAnimation animation)
    {
        var spec = HoverMotion.For(animation);
        if (spec.Axis is not (MotionAxis.TranslateX or MotionAxis.TranslateY))
            return;

        foreach (var key in spec.Keys)
            Assert.True(Math.Abs(key.Value) <= HoverMotion.MaxTravel, $"{animation} travels {key.Value}.");
    }

    /// <summary>
    /// The layout budgets <see cref="OrbitMetrics.HoverScale"/> of growth. A pulse resting at 1 must
    /// stay inside it, so the window's clamp only ever has to act on an item that already rests
    /// enlarged.
    /// </summary>
    [Fact]
    public void The_pulse_swells_but_stays_inside_the_layouts_growth_budget()
    {
        var pulse = HoverMotion.For(HoverAnimation.Pulse);

        Assert.Equal(MotionAxis.Scale, pulse.Axis);
        Assert.Contains(pulse.Keys, k => k.Value > 1f);
        Assert.All(pulse.Keys, k => Assert.True(k.Value <= OrbitMetrics.HoverScale));
    }

    /// <summary>A rotation big enough to look like a different item is a wobble that has become a spin.</summary>
    [Fact]
    public void The_wobble_tilts_by_a_few_degrees_only()
    {
        var wobble = HoverMotion.For(HoverAnimation.Wobble);

        Assert.Equal(MotionAxis.Rotate, wobble.Axis);
        Assert.All(wobble.Keys, k => Assert.InRange(k.Value, -20f, 20f));
        Assert.Contains(wobble.Keys, k => k.Value > 0f);
        Assert.Contains(wobble.Keys, k => k.Value < 0f);
    }

    [Fact]
    public void Bounce_and_float_move_vertically_and_shake_moves_horizontally()
    {
        Assert.Equal(MotionAxis.TranslateY, HoverMotion.For(HoverAnimation.Bounce).Axis);
        Assert.Equal(MotionAxis.TranslateY, HoverMotion.For(HoverAnimation.Float).Axis);
        Assert.Equal(MotionAxis.TranslateX, HoverMotion.For(HoverAnimation.Shake).Axis);
    }

    /// <summary>Bounce goes up (negative), never below where it started: it lands, it does not sink.</summary>
    [Theory]
    [InlineData(HoverAnimation.Bounce)]
    [InlineData(HoverAnimation.Float)]
    public void Vertical_motions_only_rise(HoverAnimation animation)
    {
        Assert.All(HoverMotion.For(animation).Keys, k => Assert.True(k.Value <= 0f));
    }

    /// <summary>
    /// The value the converter falls back to for an unknown name is <c>default(T)</c>, and the setting's
    /// default is Pulse, so it has to be the first member.
    /// </summary>
    [Fact]
    public void Pulse_is_the_first_member_because_it_is_the_default_and_the_fallback()
    {
        Assert.Equal(default(HoverAnimation), HoverAnimation.Pulse);
        Assert.Equal(HoverAnimation.Pulse, new AppConfig().HoverAnimation);
    }
}
