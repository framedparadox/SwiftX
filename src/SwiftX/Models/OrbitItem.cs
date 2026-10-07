using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SwiftX.Models;

public enum OrbitItemKind
{
    Application,
    File,
    Folder,
    WebLink,
    Shortcut,
    Group,
    Tool,

    /// <summary>
    /// A hole in the orbit: the place an item used to be, kept so the items after it do not slide
    /// into it. Never launched or listed — see <see cref="OrbitSlots"/>.
    /// </summary>
    Empty,
}

/// <summary>One entry on the orbit. Serializable fields persist; <see cref="IconImage"/> does not.</summary>
public sealed class OrbitItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public OrbitItemKind Kind { get; set; } = OrbitItemKind.Application;

    private string _displayName = "";
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    /// <summary>Path, URL, or tool kind name (Data / SecureValues / Base64 / Certificate).</summary>
    public string Target { get; set; } = "";

    public string? Arguments { get; set; }
    public string? CustomIconPath { get; set; }

    private string? _customGlyph;
    public string? CustomGlyph
    {
        get => _customGlyph;
        set
        {
            if (_customGlyph == value) return;
            _customGlyph = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(HasCustomIcon));
        }
    }

    private string? _customIconColor;

    /// <summary>
    /// The colour the item's glyph is drawn in, "#RRGGBB", or null for the theme's own foreground.
    /// Only a glyph is tinted: a picture (an app's shell icon, a site's favicon, a picked image) has
    /// its own colours. Not part of <see cref="IconKey"/> for that reason — changing it never needs
    /// a picture re-resolved. Hover states that fill the bubble (Invert, Tint, Close) still draw
    /// their own contrast colour over it, since this one was chosen against the resting glass.
    /// </summary>
    public string? CustomIconColor
    {
        get => _customIconColor;
        set
        {
            if (_customIconColor == value) return;
            _customIconColor = value;
            OnPropertyChanged();
        }
    }

    public bool Hidden { get; set; }

    /// <summary>
    /// An orbit entry the orbit itself synthesizes (Settings, Close) rather than one the user owns.
    /// Never persisted: the only instances that carry it are built at render time by
    /// <see cref="SystemOrbitItems"/>, so writing it would put a meaningless <c>false</c> on every
    /// user item — and let a hand-edited config claim built-in status for an ordinary one.
    /// </summary>
    [JsonIgnore]
    public bool IsSystem { get; set; }

    public List<OrbitItem> Children { get; set; } = new();

    private ImageSource? _iconImage;

    /// <summary>
    /// Everything <see cref="Services.IconService.LoadIconAsync"/> reads when it resolves this
    /// item's icon, as one comparable string. Two items with the same key resolve to the same
    /// picture, and a change to this key is exactly the set of edits that invalidate a resolved
    /// one.
    /// <para>
    /// The separator is U+001F (unit separator) rather than a printable character so no
    /// combination of a path and a target can spell another item's key.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public string IconKey => $"{Kind}\u001f{Target}\u001f{CustomIconPath}\u001f{CustomGlyph}";

    /// <summary>
    /// The <see cref="IconKey"/> that <see cref="IconImage"/> was resolved from, or null when
    /// nothing has been resolved yet. Lets a render skip an item whose icon is already correct —
    /// see <c>OrbitWindow.ResolveIconAsync</c>, which is what reads it.
    /// </summary>
    [JsonIgnore]
    public string? IconSignature { get; set; }

    [JsonIgnore]
    public ImageSource? IconImage
    {
        get => _iconImage;
        set
        {
            // Idempotent, like CustomGlyph above. A render that finds the icon already correct
            // still assigns the same value back (see OrbitWindow.ResolveIconAsync), and three
            // change notifications per item per summon for a picture nobody repainted is noise
            // every bubble's handler then has to do nothing about.
            if (ReferenceEquals(_iconImage, value)) return;
            _iconImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageVisibility));
            OnPropertyChanged(nameof(GlyphVisibility));
        }
    }

    [JsonIgnore]
    public string Glyph => !string.IsNullOrEmpty(CustomGlyph) ? CustomGlyph! : Kind switch
    {
        OrbitItemKind.WebLink => "\uE774",
        OrbitItemKind.Folder => "\uE8B7",
        OrbitItemKind.Group => "\uE838",
        OrbitItemKind.Tool => "\uE943",
        OrbitItemKind.Shortcut => "\uE71B",
        _ => "\uE7C3",
    };

    [JsonIgnore]
    public Visibility ImageVisibility => _iconImage is null ? Visibility.Collapsed : Visibility.Visible;

    [JsonIgnore]
    public Visibility GlyphVisibility => _iconImage is null ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public bool IsGroup => Kind == OrbitItemKind.Group;

    [JsonIgnore]
    public bool IsTool => Kind == OrbitItemKind.Tool;

    [JsonIgnore]
    public bool IsEmpty => Kind == OrbitItemKind.Empty;

    [JsonIgnore]
    public bool HasCustomIcon =>
        !string.IsNullOrWhiteSpace(CustomIconPath) || !string.IsNullOrEmpty(CustomGlyph);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
