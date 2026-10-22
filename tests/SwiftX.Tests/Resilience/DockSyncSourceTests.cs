using System.Text.RegularExpressions;
using Xunit;

namespace SwiftX.Tests.Resilience;

/// <summary>
/// An edit made on the Orbit screen has to reach the dock. Nothing here can be driven from a test:
/// <c>ShellWindow</c> cannot be constructed without a live WinUI runtime, and the dock is a window
/// of its own. What decides whether the dock hears about an edit is one call in the source, so the
/// call is what is pinned.
/// <para>
/// <c>OrbitManager.NotifyItemsChanged</c> writes the config <em>and</em> raises
/// <c>ItemsChanged</c> <em>and</em> reloads the dock if it is open. <c>OrbitManager.Save</c> only
/// writes the file. A group edit that ends in the second leaves the file right and a dock that is
/// open beside the Settings window, which it stays while that window has the focus, showing the
/// group as it was; the Elements list and anything else that hangs off <c>ItemsChanged</c>
/// stays as it was too.
/// </para>
/// <para>
/// That is what dropping an item on a group in the Quadrant preview did: it was the one way of
/// putting an item in a group that ended in a bare <c>Save</c>.
/// </para>
/// </summary>
public class DockSyncSourceTests
{
    /// <summary>How far past a group add its notification may be: the rest of its block.</summary>
    private const int BlockLines = 14;

    private static string ShellSource() =>
        File.ReadAllText(SourceTree.Files().Single(f => Path.GetFileName(f) == "ShellWindow.xaml.cs"));

    /// <summary>The code lines of <paramref name="lines"/> from <paramref name="start"/>, comments left out.</summary>
    private static string CodeAfter(string[] lines, int start, int count)
    {
        var code = lines
            .Skip(start)
            .Take(count)
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal));
        return string.Join("\n", code);
    }

    [Fact]
    public void EveryItemPutInAGroupOnTheOrbitScreen_ReachesTheManagersChangeNotification()
    {
        var lines = ShellSource().Split('\n');
        var problems = new List<string>();
        int found = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("GroupDropRules.TryAdd(", StringComparison.Ordinal))
                continue;
            found++;

            // The block this add sits in, no further than the next add: a notification belonging to
            // the next one must not stand in for this one's.
            int end = Math.Min(lines.Length, i + BlockLines);
            for (int j = i + 1; j < end; j++)
            {
                if (lines[j].Contains("GroupDropRules.TryAdd(", StringComparison.Ordinal))
                {
                    end = j;
                    break;
                }
            }

            var block = CodeAfter(lines, i, end - i);
            bool notifies = block.Contains("NotifyItemsChanged()", StringComparison.Ordinal) ||
                            block.Contains("FinishGroupDrop(", StringComparison.Ordinal);

            if (!notifies)
                problems.Add($"ShellWindow.xaml.cs line {i + 1}: `{lines[i].Trim()}` puts an item in a group, " +
                             "but nothing after it calls _manager.NotifyItemsChanged() (or FinishGroupDrop, which does). " +
                             "A bare _manager.Save() leaves an open dock and the Elements list as they were.");
        }

        // The rule is only worth anything while it is looking at something.
        Assert.True(found >= 4, $"expected to find the group-add call sites in ShellWindow.xaml.cs, found {found}; " +
                                "if TryAdd was renamed, update this test to the new name");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [InlineData("PutInQuadrantSlot")]
    [InlineData("MoveQuadrantItem")]
    [InlineData("RemoveQuadrantItem")]
    public void ACornerArrangementEdit_GoesThroughSaveCornerArrangement(string method)
    {
        var match = Regex.Match(
            ShellSource(), $@"void {method}\([^)]*\)\s*\{{(?<body>.*?)\n    \}}", RegexOptions.Singleline);
        Assert.True(match.Success, $"{method} was not found; if it was renamed, update this test to the new name");

        var body = match.Groups["body"].Value;
        Assert.Contains("SaveCornerArrangement()", body);
        Assert.DoesNotContain("_manager.Save()", body);
    }

    [Fact]
    public void SaveCornerArrangement_IsTheManagersChangeNotification_NotABareSave()
    {
        var match = Regex.Match(ShellSource(), @"void SaveCornerArrangement\(\)\s*=>\s*(?<body>[^;]*);");
        Assert.True(match.Success, "SaveCornerArrangement was not found; if it was renamed, update this test to the new name");

        Assert.Equal("_manager.NotifyItemsChanged()", match.Groups["body"].Value.Trim());
    }

    /// <summary>
    /// The braces body of the member called <paramref name="method"/>, whatever it returns. Its
    /// declaration, not a call to it: a name that follows a dot or another word is skipped.
    /// </summary>
    private static string BodyOf(string text, string method)
    {
        var match = Regex.Match(
            text, $@"(?<![\w.]){method}\([^)]*\)\s*\{{(?<body>.*?)\n    \}}", RegexOptions.Singleline);
        Assert.True(match.Success, $"{method} was not found; if it was renamed, update this test to the new name");
        return match.Groups["body"].Value;
    }

    /// <summary>
    /// The corner stored a group dragged onto it as a copy under an id of its own, and nothing the
    /// sidebar did to the orbit's group reached the copy. <c>HotCornerItems.RelinkOrbitGroups</c>
    /// repairs the copies such a build left behind, and it only repairs what it is asked to: it has
    /// to run on every way a configuration comes in.
    /// </summary>
    [Fact]
    public void ACornerCopyOfAGroup_IsRelinkedWhereverAConfigurationComesIn()
    {
        var manager = File.ReadAllText(SourceTree.Files().Single(f => Path.GetFileName(f) == "OrbitManager.cs"));
        Assert.Contains("HotCornerItems.RelinkOrbitGroups(Config)", BodyOf(manager, "Start"));

        Assert.Contains("HotCornerItems.RelinkOrbitGroups(_manager.Config)", BodyOf(ShellSource(), "Import_Click"));
    }

    /// <summary>
    /// A group the sidebar does not list is a group nothing added there can show up in. The orbit's
    /// groups and the corner's own are both on the dock, so both are offered, by the one method.
    /// </summary>
    [Theory]
    [InlineData("BuildOrbitEditSidebar")]
    [InlineData("BuildEditRowMenu")]
    public void TheSidebar_OffersEveryGroupTheDockShows(string method)
    {
        var body = BodyOf(ShellSource(), method);

        Assert.Contains("SidebarGroups()", body);
        Assert.DoesNotContain("ElementLibrary.UserGroups(", body);
    }

    [Fact]
    public void SidebarGroups_IsTheOrbitsGroupsAndTheCornersOwn()
    {
        var match = Regex.Match(ShellSource(), @"SidebarGroups\(\)\s*=>\s*(?<body>[^;]*);");
        Assert.True(match.Success, "SidebarGroups was not found; if it was renamed, update this test to the new name");

        var body = match.Groups["body"].Value;
        Assert.Contains("ElementLibrary.UserGroups(", body);
        Assert.Contains("HotCornerItems.OwnGroups(", body);
    }
}
