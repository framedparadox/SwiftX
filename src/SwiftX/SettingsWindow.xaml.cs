using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using Windows.Foundation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SwiftX;

/// <summary>
/// Settings shell patterned after Anchor: left NavigationView with Home (ring customize),
/// General (including the invoke shortcut), Apps &amp; links, Tools, and About. Solid
/// surfaces only — no Mica / acrylic.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly RingManager _manager;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private RingItem? _editing;
    private List<RingItem> _editList;
    private bool _loading = true;
    private Border? _dragBorder;
    private RingItem? _dragItem;
    private Point _dragStart;
    private bool _dragMoved;
    private int _dragSlot = -1;

    // Preview geometry and slot occupancy, recorded by RenderPreview so the canvas-level pointer
    // handlers can work out which slot an event landed in without walking the visual tree.
    private readonly Border?[] _slotBorders = new Border?[RingMetrics.SlotCount];
    private Point _previewCenter;
    private double _previewRadius;
    private double _previewBubble;

    public SettingsWindow(RingManager manager)
    {
        _manager = manager;
        _editList = manager.Config.Primary.Items;
        InitializeComponent();

        _toolCards.CollectionChanged += ToolCards_CollectionChanged;

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        Title = "SwiftX Settings";

        var titleBar = new AppTitleBar("", "SwiftX Settings");
        Grid.SetRow(titleBar, 0);
        Root.Children.Insert(0, titleBar);

        // Extend content under caption (solid surface, not glass). Must precede HideWindowBorder
        // or ExtendsContentIntoTitleBar clobbers the rim color.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);

        PreviewCanvas.PointerPressed += Preview_PointerPressed;
        PreviewCanvas.PointerMoved += Preview_PointerMoved;
        PreviewCanvas.PointerReleased += Preview_PointerReleased;
        PreviewCanvas.PointerCaptureLost += (_, _) => EndPreviewDrag();

        ApplyTheme();
        Root.ActualThemeChanged += (_, _) =>
        {
            ApplyChromeTheme();
            RenderPreview();
        };
        Activated += (_, _) => ApplyChromeTheme();

        if (_appWindow.Presenter is OverlappedPresenter p)
            p.IsMaximizable = false;
        _appWindow.IsShownInSwitchers = true;

        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, 1000, 700);
        WindowChrome.SetRoundedCorners(_hwnd);
        WindowChrome.CenterOnCursor(_appWindow, _appWindow.Id);

        Root.Loaded += async (_, _) =>
        {
            _loading = true;
            SelectTheme(_manager.Config.Theme);
            SelectItemSize(_manager.Config.ItemSize);
            RadiusSlider.Value = _manager.Config.RingRadius ?? RingMetrics.DefaultRadius;
            HotkeyEnabledToggle.IsOn = _manager.Config.HotkeyEnabled;
            if (HotkeyGesture.TryParse(_manager.Config.Hotkey, out var g))
                HotkeyCapture.Gesture = g;
            HotkeyCapture.Assigned += OnHotkeyAssigned;
            // Previously unwired: without this, pressing an unregistrable combo (e.g. a bare "A")
            // during capture left the button silently armed with no feedback.
            HotkeyCapture.NeedsModifier += () =>
                HotkeyStatus.Text = "Hold Ctrl, Alt, or Win with that key.";
            RebuildToolsList();
            StartupToggle.IsOn = await StartupService.IsEnabledAsync();
            VersionText.Text = "Version " + (typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0");
            _loading = false;
            RenderPreview();
            RebuildItemsList();
            UpdateHotkeyStatus();
        };

        _manager.ItemsChanged += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RenderPreview();
                RebuildItemsList();
            });
        };
    }

    public void ApplyTheme()
    {
        Root.RequestedTheme = ThemeHelper.Resolve(_manager.Config.Theme);
        ApplyChromeTheme();
        RenderPreview();
    }

    private void ApplyChromeTheme()
    {
        bool dark = ThemeHelper.IsDark(Root);
        WindowChrome.SetTitleBarTheme(_appWindow, dark);
        WindowChrome.HideWindowBorder(_hwnd, dark);
    }

    private Style SecondaryCaptionStyle => (Style)Root.Resources["SecondaryCaption"];

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "home";
        HomePanel.Visibility = tag == "home" ? Visibility.Visible : Visibility.Collapsed;
        GeneralPanel.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        AppsPanel.Visibility = tag == "apps" ? Visibility.Visible : Visibility.Collapsed;
        ToolsPanel.Visibility = tag == "tools" ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "home") RenderPreview();
        if (tag == "apps") RebuildItemsList();
        if (tag == "tools") RebuildToolsList();
    }

    private void RenderPreview()
    {
        if (PreviewCanvas is null) return;
        PreviewCanvas.Children.Clear();
        const double size = 360;
        double cx = size / 2, cy = size / 2;

        // Scale the live ring geometry into the preview canvas rather than drawing one fixed
        // picture of it, so Ring size / Item size / Ring radius visibly change what this shows.
        double scale = size * 0.74 / (2 * (RingMetrics.Radius + RingMetrics.Bubble / 2));
        double radius = RingMetrics.Radius * scale;
        double bubble = RingMetrics.Bubble * scale;
        double glyphSize = Math.Max(10, RingMetrics.Glyph * scale);

        _previewCenter = new Point(cx, cy);
        _previewRadius = radius;
        _previewBubble = bubble;
        Array.Clear(_slotBorders);

        // Resolve against this window's theme — not Application.Current.Resources.
        var borderBrush = ThemeHelper.Brush(Root, "SwiftXBorderBrush");
        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");

        // Match the ring: the center is the app mark alone, painted on nothing. A filled plate
        // here read as a big empty hole that does not exist on the real thing.
        double logo = RingMetrics.HubLogo * scale;
        var hub = AppLogo.Create(logo);
        Canvas.SetLeft(hub, cx - logo / 2);
        Canvas.SetTop(hub, cy - logo / 2);
        PreviewCanvas.Children.Add(hub);

        var items = _editList.Where(i => !i.Hidden).Take(RingMetrics.UserSlotCount).ToList();
        for (int i = 0; i < RingMetrics.SlotCount; i++)
        {
            double angle = (-90 + i * 45) * Math.PI / 180.0;
            double x = cx + radius * Math.Cos(angle) - bubble / 2;
            double y = cy + radius * Math.Sin(angle) - bubble / 2;

            // The ring synthesizes Settings and Close into the last two slots, so the preview does
            // the same rather than reading them out of the profile — they are not in it.
            Border? edit = null;
            bool pinned = i >= RingMetrics.UserSlotCount;
            RingItem? item = pinned
                ? (i == RingMetrics.UserSlotCount ? SystemRingItems.CreateSettings() : SystemRingItems.CreateClose())
                : i < items.Count ? items[i] : null;

            var border = new Border
            {
                Width = bubble,
                Height = bubble,
                CornerRadius = new CornerRadius(bubble / 2),
                Background = ThemeHelper.Brush(Root, "RingBubbleBackground"),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Tag = item,
            };

            if (pinned)
            {
                border.Child = PreviewGlyph(item!.Glyph, glyphSize, muted);
                ToolTipService.SetToolTip(border, $"{item.DisplayName} — always on the ring");
            }
            else if (item is null)
            {
                border.Child = PreviewGlyph("\uE710", glyphSize, muted);
            }
            else
            {
                border.Child = PreviewGlyph(item.Glyph, glyphSize, foreground);
                _slotBorders[i] = border;
                edit = BuildEditBadge(item, bubble, x, y, borderBrush, foreground);
            }

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            PreviewCanvas.Children.Add(border);

            // Added after its slot's bubble, and z-lifted, so a later slot's bubble can't paint
            // over a badge that overhangs into it.
            if (edit is not null)
            {
                Canvas.SetZIndex(edit, 5);
                PreviewCanvas.Children.Add(edit);
            }
        }
    }

    /// <summary>
    /// Preview press/drag/release, handled once on the canvas instead of per bubble.
    /// <para>
    /// Every bubble used to capture the pointer itself and carry its own Tapped handler. That is
    /// why repositioning never worked: the per-item capture and the tap gesture competed on the
    /// same element, and a drop never reached <see cref="ReorderItem"/>. One capture on the
    /// canvas, with the slot derived from geometry, settles tap and drag in the same place.
    /// </para>
    /// </summary>
    private void Preview_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(PreviewCanvas).Position;
        _dragSlot = PreviewSlotAt(point);
        _dragBorder = _dragSlot >= 0 ? _slotBorders[_dragSlot] : null;
        _dragItem = _dragBorder?.Tag as RingItem;
        _dragStart = point;
        _dragMoved = false;
        if (_dragSlot >= 0)
            PreviewCanvas.CapturePointer(e.Pointer);
    }

    private void Preview_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragBorder is null) return;

        var point = e.GetCurrentPoint(PreviewCanvas).Position;
        double dx = point.X - _dragStart.X;
        double dy = point.Y - _dragStart.Y;
        if (!_dragMoved && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4))
            _dragMoved = true;
        if (_dragMoved)
            _dragBorder.RenderTransform = new TranslateTransform { X = dx, Y = dy };
    }

    private void Preview_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(PreviewCanvas).Position;
        int slot = _dragSlot;
        var item = _dragItem;
        bool moved = _dragMoved;

        PreviewCanvas.ReleasePointerCapture(e.Pointer);
        EndPreviewDrag();

        if (slot < 0)
            return;

        if (moved)
        {
            // A drop lands wherever the pointer is, even out past the bubbles — the nearest slot
            // is the intent, so this lookup deliberately doesn't require a direct hit.
            if (item is not null)
                ReorderItem(item, PreviewSlotAt(point, requireHit: false));
            return;
        }

        if (item is not null)
        {
            BeginEdit(item);
        }
        else if (slot < RingMetrics.UserSlotCount)
        {
            _manager.OpenAddItem(added =>
            {
                _editList.Add(added);
                _manager.NotifyItemsChanged();
                RenderPreview();
            }, _editList);
        }
    }

    private void EndPreviewDrag()
    {
        if (_dragBorder is not null)
            _dragBorder.RenderTransform = null;
        _dragBorder = null;
        _dragItem = null;
        _dragSlot = -1;
        _dragMoved = false;
    }

    /// <summary>
    /// Which preview slot a canvas point is in, or -1. With <paramref name="requireHit"/> the
    /// point must actually be on that slot's bubble, so a press in the empty middle or out past
    /// the ring doesn't pick one up.
    /// </summary>
    private int PreviewSlotAt(Point point, bool requireHit = true)
    {
        double dx = point.X - _previewCenter.X;
        double dy = point.Y - _previewCenter.Y;
        int slot = RingMetrics.NearestSlotIndex(dx, dy);
        if (!requireHit)
            return slot;

        double angle = RingMetrics.SlotAngleRad(slot);
        double sx = _previewRadius * Math.Cos(angle);
        double sy = _previewRadius * Math.Sin(angle);
        double gap = Math.Sqrt((dx - sx) * (dx - sx) + (dy - sy) * (dy - sy));
        return gap <= _previewBubble / 2 + 4 ? slot : -1;
    }

    /// <summary>
    /// The pencil badge on a preview item. The bubble itself is now a drag handle for
    /// repositioning, so "edit this one" needs a target of its own rather than relying on a tap
    /// the drag gesture has to keep second-guessing. It sits outside the bubble's Border — a
    /// child would be clipped by the corner radius — and stops its tap from reaching the bubble.
    /// </summary>
    private Border BuildEditBadge(
        RingItem item, double bubble, double x, double y, Brush stroke, Brush foreground)
    {
        double badge = Math.Max(18, bubble * 0.4);
        var edit = new Border
        {
            Width = badge,
            Height = badge,
            CornerRadius = new CornerRadius(badge / 2),
            Background = ThemeHelper.Brush(Root, "SwiftXCardBackgroundBrush"),
            BorderBrush = stroke,
            BorderThickness = new Thickness(1),
            Child = new FontIcon
            {
                Glyph = "\uE70F",
                FontSize = badge * 0.5,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTipService.SetToolTip(edit, $"Edit {item.DisplayName}");
        edit.Tapped += (_, e) =>
        {
            e.Handled = true;
            BeginEdit(item);
        };
        Canvas.SetLeft(edit, x + bubble - badge * 0.8);
        Canvas.SetTop(edit, y - badge * 0.2);
        return edit;
    }

    /// <summary>
    /// A preview slot's glyph. Mirrors the ring's own handling of non-Fluent glyphs such as
    /// "&lt;/&gt;", which render as tofu boxes in the icon font and need a text family instead.
    /// </summary>
    private static FontIcon PreviewGlyph(string glyph, double fontSize, Brush foreground)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = fontSize,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (GlyphFonts.IsTextGlyph(glyph))
            icon.FontFamily = new FontFamily("Segoe UI");
        return icon;
    }

    private void ReorderItem(RingItem dragged, int targetSlot)
    {
        // Slots past the user range hold the pinned Settings/Close bubbles; a drop there is
        // ignored rather than clamped, so the item stays where the user last put it.
        if (targetSlot < 0 || targetSlot >= RingMetrics.UserSlotCount) return;

        var visible = _editList.Where(i => !i.Hidden).Take(RingMetrics.UserSlotCount).ToList();
        int from = visible.IndexOf(dragged);
        if (from < 0) return;

        // Clamp onto the last occupied slot: dropping past the end of a part-filled ring means
        // "put it last", not "leave a hole".
        int to = Math.Min(targetSlot, visible.Count - 1);
        if (to == from) return;

        // Move, don't insert-before-the-occupant. That older form removed the item first and so
        // landed a forward drag one slot short of where it was dropped — dragging an item to the
        // right appeared to do nothing at all.
        visible.RemoveAt(from);
        visible.Insert(to, dragged);

        // Write the new order back over the positions the visible items already occupy, so
        // hidden entries keep their place in the list instead of being shuffled by an index
        // computed from a filtered view of it.
        var positions = new List<int>();
        for (int i = 0; i < _editList.Count && positions.Count < visible.Count; i++)
        {
            if (!_editList[i].Hidden)
                positions.Add(i);
        }
        for (int i = 0; i < positions.Count; i++)
            _editList[positions[i]] = visible[i];

        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    private void BeginEdit(RingItem item)
    {
        _editing = item;
        EditPanel.Visibility = Visibility.Visible;
        EditTitle.Text = $"Edit · {item.Kind}";
        EditName.Text = item.DisplayName;
        EditTarget.Text = item.Target;
        OpenGroupBtn.Visibility = item.IsGroup ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        _editing.DisplayName = EditName.Text.Trim();
        string newTarget = EditTarget.Text.Trim();
        // Target changed: drop the old icon rather than let it linger if the new target's icon
        // fails to resolve (IconImage is otherwise never cleared once set).
        if (!string.Equals(_editing.Target, newTarget, StringComparison.Ordinal))
            _editing.IconImage = null;
        _editing.Target = newTarget;
        EditPanel.Visibility = Visibility.Collapsed;
        _editing = null;
        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    private void RemoveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        _editList.Remove(_editing);
        EditPanel.Visibility = Visibility.Collapsed;
        _editing = null;
        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        int i = _editList.IndexOf(_editing);
        if (i <= 0) return;
        _editList.RemoveAt(i);
        _editList.Insert(i - 1, _editing);
        _manager.NotifyItemsChanged();
        RenderPreview();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        int i = _editList.IndexOf(_editing);
        if (i < 0 || i >= _editList.Count - 1) return;
        _editList.RemoveAt(i);
        _editList.Insert(i + 1, _editing);
        _manager.NotifyItemsChanged();
        RenderPreview();
    }

    private void OpenGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null || !_editing.IsGroup) return;
        _editList = _editing.Children;
        EditPanel.Visibility = Visibility.Collapsed;
        _editing = null;
        RenderPreview();
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        EditPanel.Visibility = Visibility.Collapsed;
        _editing = null;
        if (!ReferenceEquals(_editList, _manager.Config.Primary.Items))
        {
            _editList = _manager.Config.Primary.Items;
            RenderPreview();
        }
    }

    private void AddItem_Click(object sender, RoutedEventArgs e) =>
        _manager.OpenAddItem(added =>
        {
            _editList.Add(added);
            _manager.NotifyItemsChanged();
            RenderPreview();
            RebuildItemsList();
        }, _editList);

    private void AddAppItem_Click(object sender, RoutedEventArgs e) =>
        _manager.OpenAddItem(added =>
        {
            _manager.Config.Primary.Items.Add(added);
            _manager.NotifyItemsChanged();
            RenderPreview();
            RebuildItemsList();
        }, _manager.Config.Primary.Items);

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        _manager.SeedDefaults(_manager.Config.Primary);
        _editList = _manager.Config.Primary.Items;
        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    private void SelectTheme(AppTheme theme)
    {
        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if ((item.Tag as string) == theme.ToString())
            {
                ThemeBox.SelectedItem = item;
                break;
            }
        }
    }

    private void SelectItemSize(RingItemSize size)
    {
        foreach (ComboBoxItem item in ItemSizeBox.Items)
        {
            if ((item.Tag as string) == size.ToString())
            {
                ItemSizeBox.SelectedItem = item;
                break;
            }
        }
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<AppTheme>(item.Tag as string, out var theme))
        {
            _manager.Config.Theme = theme;
            _manager.Save();
            _manager.ApplyTheme();
            ApplyTheme();
        }
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        await StartupService.SetEnabledAsync(StartupToggle.IsOn);
        _manager.Config.LaunchAtStartup = StartupToggle.IsOn;
        _manager.Save();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedFileName = "ring";
        picker.FileTypeChoices.Add("JSON", [".json"]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            RingStore.ExportTo(_manager.Config, file.Path);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var imported = RingStore.ImportFrom(file.Path);
        if (imported is null) return;
        _manager.Config.CopyAppSettingsFrom(imported);
        _manager.Config.Rings.Clear();
        _manager.Config.Rings.AddRange(imported.Rings);
        _manager.Config.EnsurePrimary();
        _editList = _manager.Config.Primary.Items;
        _manager.NotifyItemsChanged();
        _manager.ApplyTheme();
        ApplyTheme();
        RenderPreview();
        RebuildItemsList();
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _manager.Config.Theme = AppTheme.Dark;
        _manager.Config.Hotkey = HotkeyGesture.Default.ToString();
        _manager.Config.HotkeyEnabled = true;
        _manager.Config.ShowDataTool = true;
        _manager.Config.ShowSecureValuesTool = true;
        _manager.Config.ShowBase64Tool = true;
        _manager.Config.ShowCertificateTool = true;
        _manager.SeedDefaults(_manager.Config.Primary);
        // Through the manager, not a raw Config assignment: RingMetrics holds the geometry the
        // ring actually draws with.
        _manager.SetItemSize(RingItemSize.Medium);
        _manager.SetRadius(null);
        _manager.NotifyItemsChanged();
        _manager.ApplyTheme();
        _manager.ApplyHotkey();
        ApplyTheme();
        _loading = true;
        SelectTheme(AppTheme.Dark);
        SelectItemSize(RingItemSize.Medium);
        RadiusSlider.Value = RingMetrics.DefaultRadius;
        HotkeyEnabledToggle.IsOn = true;
        HotkeyCapture.Gesture = HotkeyGesture.Default;
        RebuildToolsList();
        _loading = false;
        RenderPreview();
        RebuildItemsList();
        UpdateHotkeyStatus();
    }

    private void ItemSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ItemSizeBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<RingItemSize>(item.Tag as string, out var size))
            _manager.SetItemSize(size);
    }

    private void RadiusSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        _manager.SetRadius(e.NewValue);
    }

    private void OnHotkeyAssigned(HotkeyGesture? gesture)
    {
        if (_loading || gesture is null) return;
        _manager.Config.Hotkey = gesture.ToString();
        _manager.Save();
        UpdateHotkeyStatus();
    }

    private void HotkeyEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _manager.Config.HotkeyEnabled = HotkeyEnabledToggle.IsOn;
        _manager.Save();
        UpdateHotkeyStatus();
    }

    private void UpdateHotkeyStatus()
    {
        bool ok = _manager.ApplyHotkey();
        HotkeyStatus.Text = ok
            ? $"Registered: {_manager.Config.Hotkey}"
            : "Could not register that hotkey (maybe another app owns it).";
    }

    private void RebuildItemsList()
    {
        if (ItemsList is null) return;
        ItemsList.Children.Clear();
        ItemsList.RowDefinitions.Clear();
        var cards = new List<Border>();
        void AddRows(IEnumerable<RingItem> items, string ring)
        {
            foreach (var item in items)
            {
                // Tools are managed from the Tools tab's own toggles, not listed here too.
                if (item.IsTool)
                    continue;

                // Groups (e.g. "Dev tools") are structure, not a flat show/hide/delete target —
                // this list isn't the source of truth for what's arranged on the ring or how it's
                // grouped, the Home tab's ring preview is. Recurse into a group's children (in
                // case it holds a plain item worth listing) without listing the group itself.
                if (item.IsGroup)
                {
                    AddRows(item.Children, item.DisplayName);
                    continue;
                }

                var card = new Border { Style = (Style)Root.Resources["Card"] };
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var labels = new StackPanel();
                labels.Children.Add(new TextBlock
                {
                    Text = item.DisplayName,
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    VerticalAlignment = VerticalAlignment.Center,
                });
                labels.Children.Add(new TextBlock
                {
                    Text = $"{ring} · {item.Kind}",
                    Style = SecondaryCaptionStyle,
                });

                var toggle = new ToggleSwitch
                {
                    IsOn = !item.Hidden,
                    OnContent = "",
                    OffContent = "",
                    MinWidth = 0,
                    Margin = new Thickness(12, 0, 0, 0),
                };
                var captured = item;
                toggle.Toggled += (_, _) =>
                {
                    captured.Hidden = !toggle.IsOn;
                    _manager.NotifyItemsChanged();
                    RenderPreview();
                };
                Grid.SetColumn(toggle, 1);
                row.Children.Add(labels);
                row.Children.Add(toggle);

                var remove = new Button
                {
                    Content = new FontIcon { Glyph = char.ConvertFromUtf32(0xE74D), FontSize = 14 },
                    Width = 40,
                    Padding = new Thickness(0),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                };
                ToolTipService.SetToolTip(remove, "Delete");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "Delete");
                remove.Click += (_, _) =>
                {
                    _manager.Config.Primary.Items.Remove(captured);
                    foreach (var group in _manager.Config.Primary.Items.Where(i => i.IsGroup))
                        group.Children.Remove(captured);
                    _manager.NotifyItemsChanged();
                    RebuildItemsList();
                    RenderPreview();
                };
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                card.Child = row;
                cards.Add(card);

                if (item.IsGroup)
                    AddRows(item.Children, item.DisplayName);
            }
        }
        AddRows(_manager.Config.Primary.Items, "Home");

        int rowCount = (cards.Count + 1) / 2;
        for (int r = 0; r < rowCount; r++)
            ItemsList.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < cards.Count; i++)
        {
            Grid.SetRow(cards[i], i / 2);
            Grid.SetColumn(cards[i], i % 2);
            ItemsList.Children.Add(cards[i]);
        }
    }

    private sealed record ToolRow(
        string Target, ToolKind Kind, string Title, string Description, string Glyph, bool TextGlyph,
        Func<AppConfig, bool> GetShown, Action<AppConfig, bool> SetShown);

    // Built from the codepoint, not a literal character in source, so the glyph can't be
    // silently swapped for a different (or invisible) private-use character by a stray
    // copy/paste. See the memory note on this exact failure mode.
    private static readonly string DataGlyph = char.ConvertFromUtf32(0xE943);
    private static readonly string SecureValuesGlyph = char.ConvertFromUtf32(0xE72E);
    private static readonly string CertificateGlyph = char.ConvertFromUtf32(0xEB95);

    private static readonly ToolRow[] ToolRows =
    [
        new("Data", ToolKind.Data, "Data formatter", "Auto-detect JSON, XML, or CSV and format it.", DataGlyph, false,
            c => c.ShowDataTool, (c, v) => c.ShowDataTool = v),
        new("SecureValues", ToolKind.SecureValues, "Secure values", "Local key/secret store with masked values.", SecureValuesGlyph, false,
            c => c.ShowSecureValuesTool, (c, v) => c.ShowSecureValuesTool = v),
        new("Base64", ToolKind.Base64, "Base64", "Encode and decode Base64 text.", "B64", true,
            c => c.ShowBase64Tool, (c, v) => c.ShowBase64Tool = v),
        new("Certificate", ToolKind.Certificate, "Certificate", "Inspect certificate, PKCS#12, and Java KeyStore files.", CertificateGlyph, false,
            c => c.ShowCertificateTool, (c, v) => c.ShowCertificateTool = v),
    ];

    // Bound as ToolsList's ItemsSource: WinUI's CanReorderItems drag pipeline mutates whichever
    // IList backs the list, so this is what actually moves when the user drags a card — observing
    // it (see ToolCards_CollectionChanged, wired in the constructor) is how a drag becomes a ring
    // reorder.
    private readonly ObservableCollection<Border> _toolCards = new();

    // Guards ToolCards_CollectionChanged against RebuildToolsList's own Clear+Add below — a
    // reorder drag removes and reinserts a card (not a single Move — ObservableCollection only
    // raises that action for an explicit Move() call), so a rebuild and a drag look identical
    // from the event args alone; this flag is what tells them apart.
    private bool _rebuildingToolCards;

    private void RebuildToolsList()
    {
        _rebuildingToolCards = true;
        try
        {
            _toolCards.Clear();
            foreach (var row in ToolRows)
                _toolCards.Add(BuildToolCard(row));
        }
        finally
        {
            _rebuildingToolCards = false;
        }
        ToolsList.ItemsSource = _toolCards;
    }

    private Border BuildToolCard(ToolRow row)
    {
        // The drag affordance — grabbing anywhere on the card works (this is WinUI's own
        // CanReorderItems drag, not a hand-rolled one), but a dedicated grip at the leading edge
        // is what tells the user this row moves at all, replacing the old per-row up/down
        // buttons. Six dots rather than a font glyph: no Segoe Fluent Icons codepoint draws that
        // pattern, and hand-placed dots can't silently turn into a different (or invisible)
        // private-use character the way a glyph literal could (see the memory note on that
        // failure mode).
        var grip = BuildDragGrip(row.Title);
        Grid.SetColumn(grip, 0);

        FrameworkElement icon = row.TextGlyph
            ? new TextBlock
            {
                Text = row.Glyph,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Width = 28,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new FontIcon
            {
                Glyph = row.Glyph,
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
            };
        Grid.SetColumn(icon, 1);

        var labels = new StackPanel();
        labels.Children.Add(new TextBlock
        {
            Text = row.Title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        labels.Children.Add(new TextBlock
        {
            Text = row.Description,
            Style = SecondaryCaptionStyle,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(labels, 2);

        var openButton = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(0xE8A7), FontSize = 14 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(openButton, "Open");
        var kind = row.Kind;
        openButton.Click += (_, _) => _manager.OpenTool(kind);
        Grid.SetColumn(openButton, 3);

        var toggle = new ToggleSwitch
        {
            IsOn = row.GetShown(_manager.Config),
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        toggle.Toggled += (_, _) =>
        {
            if (_loading) return;
            row.SetShown(_manager.Config, toggle.IsOn);
            ApplyToolVisibility(_manager.Config.Primary.Items);
            _manager.NotifyItemsChanged();
            RenderPreview();
        };
        Grid.SetColumn(toggle, 4);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(grip);
        grid.Children.Add(icon);
        grid.Children.Add(labels);
        grid.Children.Add(openButton);
        grid.Children.Add(toggle);

        return new Border { Style = (Style)Root.Resources["Card"], Child = grid, Tag = row.Target };
    }

    /// <summary>
    /// A <see cref="Grid"/> that exists solely to reach <see cref="UIElement.ProtectedCursor"/> —
    /// WinUI only exposes that setter to subclasses, so a plain <see cref="Grid"/> field can't
    /// change its own hover cursor without one.
    /// </summary>
    private sealed class CursorGrid : Grid
    {
        public void SetCursor(Microsoft.UI.Input.InputCursor? cursor) => ProtectedCursor = cursor;
    }

    /// <summary>
    /// A six-dot grip (two columns of three) at the leading edge of a tool card — the same
    /// affordance shape most reorderable lists use, so it reads as "drag me" without a tooltip
    /// even before the cursor changes over it. The cursor itself becomes a hand on hover, the
    /// closest built-in <see cref="Microsoft.UI.Input.InputSystemCursorShape"/> to a gripping hand.
    /// </summary>
    private static FrameworkElement BuildDragGrip(string title)
    {
        var grid = new CursorGrid
        {
            Width = 8,
            Height = 16,
            ColumnSpacing = 2,
            RowSpacing = 2,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var dotBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                var dot = new Ellipse
                {
                    Width = 2,
                    Height = 2,
                    Fill = dotBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(dot, row);
                Grid.SetColumn(dot, column);
                grid.Children.Add(dot);
            }
        }

        ToolTipService.SetToolTip(grid, "Drag to reorder");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grid, "Drag to reorder " + title);

        var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        grid.PointerEntered += (_, _) => grid.SetCursor(handCursor);
        grid.PointerExited += (_, _) => grid.SetCursor(null);

        return grid;
    }

    /// <summary>
    /// A reorder drag mutates <see cref="_toolCards"/> directly (WinUI's CanReorderItems drives
    /// the bound ItemsSource itself — as a remove-then-reinsert, not a single Move, so the action
    /// kind alone can't tell a drag apart from <see cref="RebuildToolsList"/>'s own Clear+Add;
    /// <see cref="_rebuildingToolCards"/> is what actually distinguishes them). Any change that
    /// gets here is therefore the user's own drag finishing.
    /// </summary>
    private void ToolCards_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildingToolCards) return;
        ApplyToolOrder(_toolCards.Select(c => (string)c.Tag).ToList());
    }

    /// <summary>
    /// Wherever two or more tool-kind items share a list (the top level, or inside a group like
    /// "Dev tools"), re-sorts just those items to match <paramref name="orderedTargets"/> — every
    /// other item in the list keeps its position. A tool kind can have more than one ring instance
    /// at once (the default seed puts Data both inside "Dev tools" and at the top level, see
    /// <see cref="SeedDefaults"/>), so this walks the whole tree rather than assuming one.
    /// </summary>
    private void ApplyToolOrder(List<string> orderedTargets)
    {
        void Walk(List<RingItem> list)
        {
            var toolPositions = new List<int>();
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsTool) toolPositions.Add(i);

            if (toolPositions.Count > 1)
            {
                var sorted = toolPositions
                    .Select(i => list[i])
                    .OrderBy(item => orderedTargets.FindIndex(
                        t => string.Equals(t, item.Target, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                for (int k = 0; k < toolPositions.Count; k++)
                    list[toolPositions[k]] = sorted[k];
            }

            foreach (var item in list.Where(x => x.IsGroup))
                Walk(item.Children);
        }
        Walk(_manager.Config.Primary.Items);

        _manager.NotifyItemsChanged();
        RenderPreview();
    }

    private void ApplyToolVisibility(IEnumerable<RingItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsTool)
            {
                var row = ToolRows.FirstOrDefault(r => string.Equals(r.Target, item.Target, StringComparison.OrdinalIgnoreCase));
                if (row is not null)
                    item.Hidden = !row.GetShown(_manager.Config);
            }
            if (item.Children.Count > 0)
                ApplyToolVisibility(item.Children);
        }
    }
}
