using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace SwiftX.Controls;

/// <summary>
/// A <see cref="Button"/> that shows the pointing-hand cursor (index finger out, the others
/// closed) while the pointer is over it. WinUI exposes the cursor only to subclasses
/// (<c>UIElement.ProtectedCursor</c>), which is the whole reason these two types exist; the
/// "Add to orbit" sidebar uses them so everything in it that can be clicked or dragged says so.
/// </summary>
public sealed class HandButton : Button
{
    public HandButton()
    {
        // A subclass looks up its own style by type name; without this it would be an unstyled,
        // invisible button rather than the stock one.
        DefaultStyleKey = typeof(Button);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}

/// <summary>
/// A <see cref="Border"/>-like control that shows the pointing-hand cursor; see
/// <see cref="HandButton"/>. Derives from <see cref="ContentControl"/> rather than
/// <see cref="Border"/> because <see cref="Border"/> is sealed — <see cref="ContentControl"/>
/// carries the same Background/BorderBrush/BorderThickness/CornerRadius/Padding set, so it draws
/// identically once templated as one.
/// </summary>
public sealed class HandBorder : ContentControl
{
    public HandBorder()
    {
        DefaultStyleKey = typeof(ContentControl);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}
