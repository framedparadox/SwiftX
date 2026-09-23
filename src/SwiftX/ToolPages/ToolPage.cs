using SwiftX;
using SwiftX.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwiftX.ToolPages;

/// <summary>
/// The base of every tool. Splitting the page from its host window (<c>ToolWindowBase</c>) costs
/// one indirection today and means a future tabbed host is a host change, not a rewrite of every
/// tool (design doc §9.1).
/// </summary>
public abstract class ToolPage : UserControl
{
    /// <summary>
    /// Every concrete page passes its <see cref="OrbitManager"/> straight through to here before
    /// building a single control — that ordering is what lets <see cref="EditorToolPage"/>'s
    /// shared chrome (panes, hover-accent buttons, the privacy bubble) read the live theme and
    /// accent colour off <see cref="HostManager"/> from its very first line, rather than being
    /// built with some placeholder colour and repainted later once a host got around to handing
    /// the manager over.
    /// </summary>
    protected ToolPage(OrbitManager manager)
    {
        HostManager = manager;
    }

    public abstract ToolKind Kind { get; }

    /// <summary>True while the page holds content the user has not saved or copied out. Drives
    /// the close-confirmation.</summary>
    public abstract bool IsDirty { get; }

    /// <summary>Commands surfaced in the host's command bar and bound to accelerators.</summary>
    public abstract IReadOnlyList<ToolCommand> Commands { get; }

    /// <summary>
    /// Optional content seated in the title bar itself, beside the minimize/maximize/close
    /// buttons — for the handful of actions important enough to stay visible without opening
    /// the command bar (Formatter's Advanced toggle, Secure values' Add/Save/Clear). Null for
    /// every page that doesn't need this.
    /// </summary>
    public virtual FrameworkElement? HeaderActions => null;

    /// <summary>The hosting <c>ToolWindowBase</c>'s window handle, set once at construction — the
    /// one thing a page needs to show a WinRT file picker (an unpackaged app must tell WinRT which
    /// window owns it; see <c>Services.FilePickers</c>).</summary>
    public nint HostHwnd { get; internal set; }

    /// <summary>
    /// The <see cref="OrbitManager"/> that owns this page's hosting window — set by the base
    /// constructor, so it is real (never null) for the whole of a page's life, construction
    /// included. Lets a page open another built-in tool the same way the orbit itself does (see
    /// <see cref="OrbitManager.OpenTool"/>; Quick Format's "open Data Formatter" button is the
    /// first user of this) and read the live theme/accent it is meant to draw itself in.
    /// </summary>
    public OrbitManager HostManager { get; }

    /// <summary>
    /// Re-reads the app theme (light/dark/Midnight/Frosted glass) and repaints whatever the page
    /// built with it — called by <c>ToolWindowBase.ApplyTheme</c> after it has repainted its own
    /// chrome, and fanned out to every open tool window by
    /// <see cref="Services.ToolWindowManager.ApplyThemeToAll"/> whenever the theme changes anywhere
    /// in the app, this window included or not.
    /// <para>
    /// The base implementation repaints <see cref="EditorToolPage"/>'s own shared chrome; a page
    /// with content built from a live re-render (<c>CertificatePage</c>'s last-read certificate,
    /// <c>SecureValuesPage</c>'s row list) overrides this to re-run that render, which is what
    /// carries the new theme onto content this class has no way to reach on its own.
    /// </para>
    /// </summary>
    public virtual void ApplyTheme() { }

    /// <summary>
    /// The lighter counterpart to <see cref="ApplyTheme"/>, for a live accent-colour change alone
    /// (Settings' colour picker fires continuously while the user drags it — see
    /// <c>AddItemWindow.ApplyAccent</c>'s own note on the same trade-off). Repaints only what
    /// reads the accent; unlike <see cref="ApplyTheme"/> it must never re-run a page's own render,
    /// which is exactly the per-frame cost this exists to avoid.
    /// </summary>
    public virtual void ApplyAccent() { }

    /// <summary>Seed the page from a dropped file or a picker.</summary>
    public virtual Task LoadFileAsync(string path) => Task.CompletedTask;

    /// <summary>Text the page would contribute to the clipboard-aware launch chip check.</summary>
    public virtual bool AcceptsClipboardText(string text) => false;

    /// <summary>Loads clipboard text into the page in response to the "Paste clipboard" chip.</summary>
    public virtual void PasteClipboardText(string text) { }

    /// <summary>Whether this page can save its own content directly — adds a Save option beside
    /// Discard/Cancel on the close-without-saving prompt (<c>ToolWindowBase.OnClosing</c>). False
    /// for pages with nothing file-shaped to save (Certificate is read-only; Secure values
    /// persists every row as it's edited, not as a document).</summary>
    public virtual bool CanSave => false;

    /// <summary>Saves the page's content. Only called when <see cref="CanSave"/> is true. Returns
    /// false if the save didn't happen — a picker the user cancelled, or a write that failed —
    /// so the close prompt knows to leave the window open rather than discard unsaved work.</summary>
    public virtual Task<bool> SaveAsync() => Task.FromResult(false);

    private readonly CancellationTokenSource _pageClosing = new();

    /// <summary>Cancelled when the host window closes — every background parse ties to this.</summary>
    protected CancellationToken PageClosing => _pageClosing.Token;

    /// <summary>
    /// Called by <c>ToolWindowBase</c> when the host window closes. Cancelling is what runs the
    /// registered callbacks — the pages that own a <c>DispatcherQueueTimer</c> stop it here (see
    /// <c>DiffPage</c>, <c>TimestampPage</c>), which is what stops the dispatcher holding a closed
    /// window's whole visual tree.
    /// <para>
    /// <b>Not disposed, deliberately.</b> A <see cref="CancellationTokenSource"/> only holds an
    /// unmanaged resource once something arms its timer (<c>CancelAfter</c>) or asks for its
    /// <c>WaitHandle</c>; this one does neither, so it is plain managed memory collected with the
    /// page that owns it — there is nothing for a Dispose to release. Calling one anyway would be
    /// actively harmful: <c>RegexPage</c> reads <see cref="PageClosing"/> after awaiting its match,
    /// which is precisely the "the window closed underneath us" case, and reading <c>.Token</c> on
    /// a disposed source throws <see cref="ObjectDisposedException"/> — from an <c>async void</c>
    /// handler, where an exception is an unhandled one.
    /// </para>
    /// </summary>
    internal void NotifyClosing()
    {
        try { _pageClosing.Cancel(); } catch { /* a registered callback threw; the window is going anyway */ }
    }
}
