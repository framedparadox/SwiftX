using System.Text.RegularExpressions;
using Xunit;

namespace SwiftX.Tests.Resilience;

/// <summary>
/// The app window must still be on screen once its first <c>Activate()</c> returns. Nothing here can
/// be driven from a test - a <c>Window</c> cannot be constructed without a live WinUI runtime - so the
/// rule is pinned in the source.
/// <para>
/// <c>Window.Activated</c> is raised <em>inside</em> the first <c>Activate()</c>, and the app window
/// repaints its chrome from that event. When that repaint flipped the presenter's <c>IsResizable</c>
/// (the window is built fixed-size, and the shared frame code forced it back to <c>true</c> every
/// time), the window Windows had just been asked to show was hidden again about 30 ms later: it
/// flashed up as a blank frame and then was gone. Every <em>new</em> window failed its first
/// activation and succeeded on the second, which from the user's chair was a blank window "every
/// other time the app is opened". The second activation changes nothing (already <c>true</c>),
/// which is why it worked.
/// </para>
/// <para>
/// Found by skipping the chrome calls one at a time on the real window: skipping only this assignment
/// kept it on screen. With the fix, new windows stayed visible and rendered in every theme, opened
/// from the dock and from a second launch; before it, none did.
/// </para>
/// </summary>
public class WindowActivationFrameTests
{
    private static string Source(string fileName) =>
        File.ReadAllText(SourceTree.Files().Single(f => Path.GetFileName(f) == fileName));

    /// <summary>The code lines of <paramref name="text"/>, comments left out.</summary>
    private static string CodeOnly(string text) =>
        string.Join("\n", text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void TheSharedFrameCode_NeverForcesIsResizableTrue_AndOnlyWritesItWhenItDiffers()
    {
        var method = Regex.Match(
            Source("WindowChrome.cs"),
            @"public static void ApplyShellFrame\((?<signature>[^)]*)\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);
        Assert.True(method.Success, "WindowChrome.ApplyShellFrame not found");

        var body = CodeOnly(method.Groups["body"].Value);

        Assert.DoesNotMatch(@"IsResizable\s*=\s*true", body);
        Assert.Contains("if (presenter.IsResizable != resizable)", body);
        Assert.Contains("presenter.IsResizable = resizable;", body);

        // Tool windows and onboarding are resizable and say nothing, so the default stays true.
        Assert.Matches(@"bool\s+resizable\s*=\s*true", method.Groups["signature"].Value);
    }

    [Fact]
    public void EveryWindowThatBuildsItselfFixedSize_TellsTheSharedFrameSo()
    {
        var problems = new List<string>();
        int callers = 0;

        foreach (var path in SourceTree.Files())
        {
            var text = File.ReadAllText(path);
            if (Path.GetFileName(path) == "WindowChrome.cs" || !text.Contains("WindowChrome.ApplyShellFrame(", StringComparison.Ordinal))
                continue;
            callers++;

            if (!Regex.IsMatch(CodeOnly(text), @"\bIsResizable\s*=\s*false\b"))
                continue;

            foreach (Match call in Regex.Matches(text, @"WindowChrome\.ApplyShellFrame\((?<args>[^;]*);"))
            {
                if (!Regex.IsMatch(call.Groups["args"].Value, @"\bresizable\s*:\s*false\b"))
                    problems.Add($"{Path.GetFileName(path)} sets IsResizable = false but calls ApplyShellFrame without " +
                                 "`resizable: false`; the call flips it back to true from Window.Activated and the new window " +
                                 "is hidden again right after it is shown.");
            }
        }

        Assert.True(callers >= 3, "expected the shell, onboarding and tool windows to call ApplyShellFrame");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
