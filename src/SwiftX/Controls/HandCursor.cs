using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace SwiftX.Controls;

/// <summary>
/// A <see cref="Grid"/> that exposes <see cref="UIElement.ProtectedCursor"/> through public
/// methods, for a tree where which cursor to show changes dynamically (on a pointer entering and
/// leaving a plain <see cref="Border"/>, say) rather than being fixed for one element's whole
/// lifetime the way <see cref="HandButton"/>/<see cref="HandBorder"/> are. <see cref="Grid"/> takes
/// a subclass cleanly — unlike <see cref="Border"/>, nothing here needs a template workaround — so
/// this only ever needs to be the one element high enough in the tree to sit over everything that
/// wants to borrow its cursor, typically a window's own root panel.
/// </summary>
public class CursorHost : Grid
{
    public void SetCursor(InputSystemCursorShape shape) =>
        ProtectedCursor = InputSystemCursor.Create(shape);

    /// <summary>Back to whatever the pointer would otherwise show — the OS default, or another
    /// element's own cursor if the pointer has moved onto one that sets its own.</summary>
    public void ClearCursor() => ProtectedCursor = null;
}

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
    // An explicit template, because the bare ContentControl one is only a ContentPresenter that
    // takes none of Background / BorderBrush / BorderThickness / CornerRadius / Padding from the
    // control: the row had no surface to hit-test, so it never saw a press, a drag or a drop, and
    // drew none of its drag feedback either.
    private const string TemplateXaml =
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContentControl'>" +
        "<ContentPresenter Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' " +
        "BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}' " +
        "Padding='{TemplateBinding Padding}' " +
        "HorizontalContentAlignment='{TemplateBinding HorizontalContentAlignment}' " +
        "VerticalContentAlignment='{TemplateBinding VerticalContentAlignment}'/>" +
        "</ControlTemplate>";

    public HandBorder()
    {
        DefaultStyleKey = typeof(ContentControl);
        Template = (ControlTemplate)XamlReader.Load(TemplateXaml);
        // Stretched like a Border's child, so a row's trailing column sits at its right edge.
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = false;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}
