using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>What a <see cref="MotionSpec"/> moves.</summary>
public enum MotionAxis
{
    /// <summary>Uniform scale. Values multiply the item's resting scale.</summary>
    Scale,

    /// <summary>Horizontal travel. Values are a fraction of the item's diameter.</summary>
    TranslateX,

    /// <summary>Vertical travel; negative is up. Values are a fraction of the item's diameter.</summary>
    TranslateY,

    /// <summary>Rotation about the item's centre. Values are degrees.</summary>
    Rotate,
}

/// <summary>One point on a motion's curve: <paramref name="At"/> is 0 to 1 through its duration.</summary>
public readonly record struct MotionKey(float At, float Value);

/// <summary>
/// A hover animation as data: one <see cref="Axis"/>, the keyframes it passes through, how long one
/// pass takes, and whether it repeats for as long as the pointer stays.
/// </summary>
public sealed record MotionSpec(MotionAxis Axis, IReadOnlyList<MotionKey> Keys, int DurationMs, bool Loops);

/// <summary>
/// The motions behind <see cref="OrbitHoverStyle.Animate"/>, and how each is named and drawn in the
/// picker.
/// <para>
/// Kept as plain data rather than composition calls so the rules that matter can be tested without a
/// window: every loop has to end where it began (or the item would jump each time round), and no
/// motion may carry an item further than the room the orbit's layout leaves it. The window turns a
/// spec into a keyframe animation and is the only thing that does.
/// </para>
/// </summary>
public static class HoverMotion
{
    /// <summary>Every animation, in the order the picker lists them.</summary>
    public static IReadOnlyList<HoverAnimation> All { get; } =
    [
        HoverAnimation.Pulse,
        HoverAnimation.Bounce,
        HoverAnimation.Wobble,
        HoverAnimation.Shake,
        HoverAnimation.Spin,
        HoverAnimation.Float,
    ];

    /// <summary>
    /// The face the picker draws <see cref="Symbol"/> in. Ordinary Unicode symbols rather than the
    /// icon font's private-use glyphs: Segoe UI Symbol carries all of them, and what each one is
    /// (a sine wave, a clockwise arrow) is fixed by the standard rather than by whichever icon-font
    /// release is installed.
    /// </summary>
    public const string SymbolFontFamily = "Segoe UI Symbol";

    public static string Label(HoverAnimation animation) => animation switch
    {
        HoverAnimation.Pulse => "Pulse",
        HoverAnimation.Bounce => "Bounce",
        HoverAnimation.Wobble => "Wobble",
        HoverAnimation.Shake => "Shake",
        HoverAnimation.Spin => "Spin",
        HoverAnimation.Float => "Float",
        _ => animation.ToString(),
    };

    /// <summary>
    /// The symbol that stands for <paramref name="animation"/>: it says which motion this is, on the
    /// side pill and in the picker. Built from codepoints so the source stays plain ASCII.
    /// </summary>
    public static string Symbol(HoverAnimation animation) => char.ConvertFromUtf32(animation switch
    {
        HoverAnimation.Pulse => 0x25CE,   // bullseye: rings of a pulse
        HoverAnimation.Bounce => 0x2195,  // up-down arrow
        HoverAnimation.Wobble => 0x223F,  // sine wave
        HoverAnimation.Shake => 0x2194,   // left-right arrow
        HoverAnimation.Spin => 0x21BB,    // clockwise open circle arrow
        HoverAnimation.Float => 0x2601,   // cloud
        _ => 0x25CE,
    });

    /// <summary>
    /// The largest fraction of an item's own diameter any motion may carry it. The orbit budgets
    /// <see cref="OrbitMetrics.HoverScale"/> of growth around each item, which is that fraction of
    /// diameter on the far edge; travelling further than it starts to sit on a neighbour.
    /// </summary>
    public const float MaxTravel = 0.2f;

    /// <summary>The motion for <paramref name="animation"/>.</summary>
    public static MotionSpec For(HoverAnimation animation) => animation switch
    {
        // A swell that peaks inside the layout's growth budget (the window also clamps it against
        // the budget, for an item that already rests a little enlarged).
        HoverAnimation.Pulse => new(MotionAxis.Scale,
            [new(0f, 1f), new(0.5f, 1.10f), new(1f, 1f)],
            DurationMs: 900, Loops: true),

        HoverAnimation.Bounce => new(MotionAxis.TranslateY,
            [new(0f, 0f), new(0.28f, -0.16f), new(0.52f, 0f), new(0.68f, -0.06f), new(0.82f, 0f), new(1f, 0f)],
            DurationMs: 1100, Loops: true),

        HoverAnimation.Wobble => new(MotionAxis.Rotate,
            [new(0f, 0f), new(0.15f, -10f), new(0.35f, 8f), new(0.55f, -5f), new(0.75f, 2f), new(0.9f, 0f), new(1f, 0f)],
            DurationMs: 1000, Loops: true),

        HoverAnimation.Shake => new(MotionAxis.TranslateX,
            [new(0f, 0f), new(0.08f, -0.06f), new(0.18f, 0.06f), new(0.28f, -0.05f), new(0.38f, 0.05f),
             new(0.48f, -0.025f), new(0.56f, 0.025f), new(0.64f, 0f), new(1f, 0f)],
            DurationMs: 900, Loops: true),

        // One turn, not a spinner: an item left turning for as long as the pointer rests on it has no
        // resting angle to settle back to when the pointer leaves, and 360 degrees is where it
        // already is.
        HoverAnimation.Spin => new(MotionAxis.Rotate,
            [new(0f, 0f), new(1f, 360f)],
            DurationMs: 700, Loops: false),

        HoverAnimation.Float => new(MotionAxis.TranslateY,
            [new(0f, 0f), new(0.5f, -0.08f), new(1f, 0f)],
            DurationMs: 1600, Loops: true),

        _ => For(HoverAnimation.Pulse),
    };
}

/// <summary>
/// Whether Windows has animations switched off ("Show animations in Windows" under Accessibility >
/// Visual effects). A motion that repeats for as long as the pointer rests on an item is exactly what
/// that setting is for, so <see cref="OrbitHoverStyle.Animate"/> gives way to a plain grow when it is
/// off. Same shape as <see cref="HighContrast"/>: guarded, and false when the setting cannot be read.
/// </summary>
public static class ReducedMotion
{
    public static bool IsActive()
    {
        try { return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return false; }
    }
}
