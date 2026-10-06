using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace SwiftX.Services;

/// <summary>
/// Plays a <see cref="MotionSpec"/> on an element's composition visual. Shared by the orbit, where
/// it animates a bubble under the pointer, and by Settings' picker, where each option plays its own
/// motion when hovered so the choice can be seen instead of guessed from a name.
/// <para>
/// Travel goes through <c>Translation</c>, never <c>Offset</c>: Offset is how XAML carries an
/// element's <c>Canvas.Left</c>/<c>Top</c>, so writing it would hand the element's position to
/// composition and strand it wherever the animation ended. The orbit's entrance also rides on
/// Translation (its trip out from the hub); a hover that lands mid-entrance replaces that trip, and
/// <see cref="Stop"/> brings Translation back to zero, which is the bubble's resting place either
/// way. Rotation is otherwise unused, and Scale is only ever animated here by Pulse, which replaces
/// whatever scale animation was running.
/// </para>
/// </summary>
public static class HoverMotionPlayer
{
    /// <summary>
    /// Starts <paramref name="spec"/> on <paramref name="host"/>. <paramref name="size"/> is the
    /// element's diameter: travel is a fraction of it, and it is centred on <c>size / 2</c> so a
    /// rotation or scale turns about the middle. <paramref name="restScale"/> is what the element
    /// settles at when idle (an open group rests a little enlarged); a pulse swells about that and is
    /// clamped to <paramref name="maxScale"/> (or to the rest scale, if that is already larger).
    /// Returns false, having changed nothing, if composition would not take the animation.
    /// </summary>
    public static bool Start(UIElement host, double size, MotionSpec spec, float restScale, float maxScale)
    {
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(host, true);
            var visual = ElementCompositionPreview.GetElementVisual(host);
            var compositor = visual.Compositor;
            visual.CenterPoint = new Vector3((float)(size / 2), (float)(size / 2), 0);

            // Ease in and out, so a swing reverses gently instead of bouncing off a wall.
            var ease = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
            float extent = (float)size;

            switch (spec.Axis)
            {
                case MotionAxis.Scale:
                {
                    var animate = compositor.CreateVector3KeyFrameAnimation();
                    float ceiling = Math.Max(maxScale, restScale);
                    foreach (var key in spec.Keys)
                    {
                        float scale = Math.Min(restScale * key.Value, ceiling);
                        animate.InsertKeyFrame(key.At, new Vector3(scale, scale, 1), ease);
                    }
                    Run(animate, "Scale");
                    break;
                }

                case MotionAxis.TranslateX:
                case MotionAxis.TranslateY:
                {
                    bool horizontal = spec.Axis == MotionAxis.TranslateX;
                    var animate = compositor.CreateVector3KeyFrameAnimation();
                    foreach (var key in spec.Keys)
                    {
                        float travel = key.Value * extent;
                        animate.InsertKeyFrame(
                            key.At, horizontal ? new Vector3(travel, 0, 0) : new Vector3(0, travel, 0), ease);
                    }
                    Run(animate, "Translation");
                    break;
                }

                case MotionAxis.Rotate:
                {
                    var animate = compositor.CreateScalarKeyFrameAnimation();
                    foreach (var key in spec.Keys)
                        animate.InsertKeyFrame(key.At, key.Value, ease);
                    Run(animate, "RotationAngleInDegrees");
                    break;
                }
            }

            return true;

            void Run(KeyFrameAnimation animate, string property)
            {
                animate.Duration = TimeSpan.FromMilliseconds(spec.DurationMs);
                if (spec.Loops)
                    animate.IterationBehavior = AnimationIterationBehavior.Forever;
                visual.StartAnimation(property, animate);
            }
        }
        catch
        {
            // Motion is decoration; an element that will not animate is still a working element.
            return false;
        }
    }

    /// <summary>
    /// Brings <paramref name="host"/> back to rest after <see cref="Start"/>. A replacement
    /// animation with a single keyframe starts from wherever the running one has got to, so the
    /// element glides home from mid-swing over <paramref name="returnMs"/> instead of snapping.
    /// <para>
    /// Scale needs nothing here: whoever owns the element's scale animates it back to rest, and that
    /// replaces the pulse. A single turn is left to finish, since unwinding it would run the turn
    /// backwards and 360 degrees is where it stops.
    /// </para>
    /// </summary>
    public static void Stop(UIElement host, MotionSpec spec, int returnMs, CompositionEasingFunction? ease = null)
    {
        if (spec.Axis == MotionAxis.Scale || !spec.Loops)
            return;

        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(host);
            var compositor = visual.Compositor;

            // The overload without an easing function is a distinct call, not a null argument.
            if (spec.Axis == MotionAxis.Rotate)
            {
                var home = compositor.CreateScalarKeyFrameAnimation();
                if (ease is null)
                    home.InsertKeyFrame(1, 0f);
                else
                    home.InsertKeyFrame(1, 0f, ease);
                home.Duration = TimeSpan.FromMilliseconds(returnMs);
                visual.StartAnimation("RotationAngleInDegrees", home);
            }
            else
            {
                var home = compositor.CreateVector3KeyFrameAnimation();
                if (ease is null)
                    home.InsertKeyFrame(1, Vector3.Zero);
                else
                    home.InsertKeyFrame(1, Vector3.Zero, ease);
                home.Duration = TimeSpan.FromMilliseconds(returnMs);
                visual.StartAnimation("Translation", home);
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Puts <paramref name="host"/> back to its resting scale over <paramref name="returnMs"/> and
    /// leaves any travel and rotation to <see cref="Stop"/>. For a host, like a picker option, that
    /// has no scale of its own to animate back.
    /// </summary>
    public static void RestScale(UIElement host, float restScale, int returnMs)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(host);
            var scale = visual.Compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, new Vector3(restScale, restScale, 1));
            scale.Duration = TimeSpan.FromMilliseconds(returnMs);
            visual.StartAnimation("Scale", scale);
        }
        catch
        {
            // ignore
        }
    }
}
