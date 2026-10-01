using System.Text.RegularExpressions;
using Xunit;

namespace SwiftX.Tests.Resilience;

/// <summary>
/// Closed windows must be collectable. Two source rules decide that, and neither can be observed
/// from a unit test, because a <c>Window</c> cannot be constructed without a live WinUI runtime.
/// <para>
/// <b>Rule 1 - a window must not leave a handler of its own on its own events.</b> <c>Window</c> is
/// not a <c>DependencyObject</c>, so it does not take part in the XAML reference tracking that lets
/// the garbage collector see through a managed-to-native cycle. A delegate on the window's own
/// <c>Activated</c> or <c>Closed</c> that targets the window is a strong reference the native
/// window holds for as long as it lives, and it lives for as long as that reference is there.
/// Measured on a minimal WinUI 3 app (Windows App SDK 2.4, .NET 10; 30 windows opened and closed,
/// full GC afterwards): <b>30 of 30 windows still alive</b> with <c>Activated</c> or <c>Closed</c>
/// subscribed to a method or lambda of the window, 30 of 30 with <c>Activated</c> left subscribed
/// while <c>Closed</c> unsubscribed itself, and <b>0 of 30</b> with no subscription, with a
/// weak-reference delegate target, or with every handler - <c>Closed</c> included - removed inside
/// <c>Closed</c>. Handlers on child elements (XAML <c>Click=""</c>, <c>Loaded</c>) and on
/// <c>AppWindow</c> do not matter.
/// </para>
/// <para>
/// In SwiftX that was Settings: ~5 MB, ~35 handles and ~30 GDI objects leaked per open, with a
/// 10 000-object GDI quota per process. And it was every tool window: a closed Formatter kept its
/// editors and document text, and a closed Secure values window kept whatever the user had
/// revealed.
/// </para>
/// <para>
/// <b>Rule 2 - subscriptions to an app-lifetime publisher need their unsubscribe.</b>
/// <c>OrbitManager</c> lives for the whole run and the windows are built afresh per open, so a
/// <c>_manager.ItemsChanged += () =&gt; ...</c> in a window pins the window for the run; a lambda
/// cannot be unsubscribed at all.
/// </para>
/// <para>
/// Both are pinned here by reading the source, and the consequence is measured by
/// <c>measure-app.ps1 -Phase settings</c> (private bytes, handles and GDI per open/close cycle).
/// </para>
/// </summary>
public class EventSubscriptionLifetimeTests
{
    /// <summary>
    /// The windows that are closed and rebuilt during a run. <c>OrbitWindow</c> is deliberately not
    /// here: it is created once and lives until the app quits, so it has nothing to be collected.
    /// A new closeable window belongs in this list.
    /// </summary>
    private static readonly string[] CloseableWindows =
    [
        "ShellWindow.xaml.cs",
        "OnboardingWindow.xaml.cs",
        "AddItemWindow.xaml.cs",
        "ToolWindowBase.cs",
    ];

    /// <summary>The window's own events whose handlers must not outlive <c>Closed</c>.</summary>
    private static readonly string[] OwnEvents = ["Activated", "Closed", "SizeChanged", "VisibilityChanged"];

    private static string Source(string fileName)
    {
        var path = SourceTree.Files().Single(f => Path.GetFileName(f) == fileName);
        return File.ReadAllText(path);
    }

    [Fact]
    public void ACloseableWindow_NeverLeavesAHandlerOfItsOwnOnItsOwnEvents()
    {
        var problems = new List<string>();

        foreach (var file in CloseableWindows)
        {
            var text = Source(file);

            foreach (var evt in OwnEvents)
            {
                // `Activated += X;` on the window itself (no receiver), not `Root.Loaded +=` and not
                // `_flyout.Closed +=`: those hang off other objects.
                var subscribe = new Regex($@"(?<![\w.]){evt}\s*\+=\s*(?<handler>[^;]*);");
                var unsubscribe = new Regex($@"(?<![\w.]){evt}\s*-=\s*(?<handler>\w+)\s*;");

                foreach (Match sub in subscribe.Matches(text))
                {
                    var handler = sub.Groups["handler"].Value.Trim();

                    if (!Regex.IsMatch(handler, @"^\w+$"))
                    {
                        problems.Add($"{file}: `{evt} += {Shorten(handler)}` is a lambda; it cannot be unsubscribed, " +
                                     "so the window can never be collected. Use a named method and remove it in the Closed handler.");
                        continue;
                    }

                    var removed = unsubscribe.Matches(text).Any(u => u.Groups["handler"].Value == handler);
                    if (!removed)
                        problems.Add($"{file}: `{evt} += {handler}` is never removed. Remove it (`{evt} -= {handler};`) inside " +
                                     "the Closed handler, or the closed window stays in memory with everything it holds.");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void ACloseableWindows_ClosedHandler_RemovesItself()
    {
        // `Closed -= OnWindowClosed;` inside OnWindowClosed: leaving the Closed handler itself
        // subscribed leaks the window exactly as leaving any other one does.
        foreach (var file in CloseableWindows)
        {
            var text = Source(file);
            var handler = Regex.Match(text, @"void (?<name>OnWindowClosed)\([^)]*\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);

            Assert.True(handler.Success, $"{file}: no OnWindowClosed method; a closeable window needs one to unsubscribe in");
            Assert.Contains("Closed -= OnWindowClosed", handler.Groups["body"].Value);
        }
    }

    [Fact]
    public void ShellWindow_StopsItsTimersAndAnimations_WhenItCloses()
    {
        var text = Source("ShellWindow.xaml.cs");

        var stop = Regex.Match(text, @"void StopHomeOrbitMark\(\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(stop.Success, "StopHomeOrbitMark was not found; if it was renamed, update this test to the new name");
        Assert.Contains("_homeOrbitMark?.Reset()", stop.Groups["body"].Value);
        Assert.Contains("_homeToolsMark?.Reset()", stop.Groups["body"].Value);

        var closed = Regex.Match(text, @"void OnWindowClosed\([^)]*\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(closed.Success);
        Assert.Contains("StopHomeOrbitMark()", closed.Groups["body"].Value);
    }

    /// <summary>
    /// (publisher expression, event) pairs for the events whose publisher outlives any window.
    /// Add an entry when an app-lifetime object grows a new event; <c>OrbitManager</c> is reached as
    /// <c>_manager</c> inside windows, and <c>OrbitMetrics.Changed</c> is static.
    /// </summary>
    private static readonly (string Publisher, string Event)[] LongLived =
    [
        ("_manager", "ItemsChanged"),
        ("manager", "ItemsChanged"),
        ("OrbitMetrics", "Changed"),
        ("ToolWindows", "OpenSetChanged"),
    ];

    [Fact]
    public void EverySubscriptionToAnAppLifetimeEvent_IsANamedHandlerWithAMatchingUnsubscribe()
    {
        var problems = new List<string>();

        foreach (var file in SourceTree.Files())
        {
            var text = File.ReadAllText(file);

            foreach (var (publisher, evt) in LongLived)
            {
                var subscribe = new Regex($@"\b{Regex.Escape(publisher)}\.{Regex.Escape(evt)}\s*\+=\s*(?<handler>[^;]*)");
                var unsubscribe = new Regex($@"\b{Regex.Escape(publisher)}\.{Regex.Escape(evt)}\s*-=");

                var subs = subscribe.Matches(text);
                if (subs.Count == 0)
                    continue;

                var name = Path.GetFileName(file);
                foreach (Match sub in subs)
                {
                    var handler = sub.Groups["handler"].Value.TrimStart();
                    if (handler.StartsWith('(') || handler.StartsWith("async", StringComparison.Ordinal) ||
                        handler.StartsWith("delegate", StringComparison.Ordinal) || handler.Contains("=>"))
                    {
                        problems.Add($"{name}: {publisher}.{evt} is subscribed with a lambda, which cannot be unsubscribed " +
                                     "- the publisher then keeps this object alive for the whole run");
                    }
                }

                if (unsubscribe.Matches(text).Count < subs.Count)
                    problems.Add($"{name}: {publisher}.{evt} is subscribed {subs.Count}x but unsubscribed " +
                                 $"{unsubscribe.Matches(text).Count}x - unsubscribe in the window's Closed handler");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheLongLivedEventList_StillNamesRealEvents()
    {
        // A rename would silently turn the test above into a no-op: it would look for a publisher
        // and event that no longer exist and find nothing wrong. Pin that each is still declared.
        var declarations = string.Join("\n", SourceTree.Files().Select(File.ReadAllText));

        foreach (var evt in LongLived.Select(l => l.Event).Distinct())
        {
            Assert.True(
                Regex.IsMatch(declarations, $@"\bevent\s+[A-Za-z<>?,\s]+\b{evt}\b"),
                $"no 'event ... {evt}' declaration found: update LongLived in this test to the renamed event");
        }
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + "...";
}
