using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SwiftX.Models;

public enum RingItemKind
{
    Application,
    File,
    Folder,
    WebLink,
    Shortcut,
    Group,
    Tool,
}

/// <summary>One entry on the ring. Serializable fields persist; <see cref="IconImage"/> does not.</summary>
public sealed class RingItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public RingItemKind Kind { get; set; } = RingItemKind.Application;

    private string _displayName = "";
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    /// <summary>Path, URL, or tool kind name (Data / Json / Xml / SecureValues).</summary>
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

    public bool Hidden { get; set; }

    /// <summary>
    /// A ring entry the ring itself synthesizes (Settings, Close) rather than one the user owns.
    /// Never persisted: the only instances that carry it are built at render time by
    /// <see cref="SystemRingItems"/>, so writing it would put a meaningless <c>false</c> on every
    /// user item — and let a hand-edited config claim built-in status for an ordinary one.
    /// </summary>
    [JsonIgnore]
    public bool IsSystem { get; set; }

    public List<RingItem> Children { get; set; } = new();

    private ImageSource? _iconImage;

    [JsonIgnore]
    public ImageSource? IconImage
    {
        get => _iconImage;
        set
        {
            _iconImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageVisibility));
            OnPropertyChanged(nameof(GlyphVisibility));
        }
    }

    [JsonIgnore]
    public string Glyph => !string.IsNullOrEmpty(CustomGlyph) ? CustomGlyph! : Kind switch
    {
        RingItemKind.WebLink => "\uE774",
        RingItemKind.Folder => "\uE8B7",
        RingItemKind.Group => "\uE838",
        RingItemKind.Tool => "\uE943",
        RingItemKind.Shortcut => "\uE71B",
        _ => "\uE7C3",
    };

    [JsonIgnore]
    public Visibility ImageVisibility => _iconImage is null ? Visibility.Collapsed : Visibility.Visible;

    [JsonIgnore]
    public Visibility GlyphVisibility => _iconImage is null ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public bool IsGroup => Kind == RingItemKind.Group;

    [JsonIgnore]
    public bool IsTool => Kind == RingItemKind.Tool;

    [JsonIgnore]
    public bool HasCustomIcon =>
        !string.IsNullOrWhiteSpace(CustomIconPath) || !string.IsNullOrEmpty(CustomGlyph);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
