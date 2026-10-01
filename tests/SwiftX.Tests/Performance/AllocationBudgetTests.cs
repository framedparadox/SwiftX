using System.Text;
using System.Xml.Linq;
using SwiftX.Services.Formats;
using Xunit;
using Xunit.Abstractions;

namespace SwiftX.Tests.Performance;

/// <summary>
/// Allocation budgets: how much garbage an operation makes, which — unlike how long it takes — does
/// not depend on what else the machine is doing.
/// <para>
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> counts only the calling thread's own
/// allocations, so a parallel test class cannot move the number and the assertion needs neither a
/// quiet machine nor <c>TimingSensitiveCollection</c>. That makes it the right instrument for a
/// regression that wall-clock budgets keep missing: an operation that got 10x more wasteful but
/// still finishes inside a budget set an order of magnitude above its measured cost.
/// </para>
/// </summary>
public class AllocationBudgetTests(ITestOutputHelper output)
{
    private static string RecordsXml(int rows)
    {
        var sb = new StringBuilder(rows * 80);
        sb.Append("<records>");
        for (int i = 0; i < rows; i++)
        {
            sb.Append("<record id=\"").Append(i).Append("\">")
              .Append("<name>Record ").Append(i).Append("</name>")
              .Append("<score>").Append(i * 1.5).Append("</score>")
              .Append("</record>");
        }
        sb.Append("</records>");
        return sb.ToString();
    }

    private static long Allocated(Action operation)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        operation();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// <c>Validate</c> only has to say whether the text is well formed, and it used to answer by
    /// building the whole <see cref="XDocument"/> and throwing it away — on every debounced keystroke
    /// in the Formatter and every clipboard read in Quick Format. The tree is the allocation being
    /// ruled out, so it is measured here in the same units: validating must cost a small fraction of
    /// what building the tree costs, and less than one byte per input character.
    /// </summary>
    [Fact]
    public void XmlValidate_DoesNotBuildADocumentTree()
    {
        var xml = RecordsXml(50_000);

        // Untimed warm-up: JIT output and first-use statics are allocations too, and not what is
        // being pinned.
        XmlFormat.Instance.Validate(xml);
        _ = XDocument.Parse(xml);

        IReadOnlyList<Diagnostic> diagnostics = [];
        long validate = Allocated(() => diagnostics = XmlFormat.Instance.Validate(xml));

        XDocument? tree = null;
        long treeBytes = Allocated(() => tree = XDocument.Parse(xml));
        GC.KeepAlive(tree);

        output.WriteLine(
            $"{xml.Length:N0} chars: validate allocated {validate:N0} bytes; " +
            $"building the tree allocates {treeBytes:N0} bytes (x{(double)treeBytes / Math.Max(validate, 1):N0})");

        Assert.Empty(diagnostics);
        Assert.True(validate < xml.Length,
            $"Validate allocated {validate:N0} bytes for {xml.Length:N0} characters; a token walk needs far less than one byte per character");
        Assert.True(validate * 20 < treeBytes,
            $"Validate allocated {validate:N0} bytes against {treeBytes:N0} for the tree it is supposed not to build");
    }

    /// <summary>Same refusals as the tree-building version produced: malformed text and over-deep
    /// nesting are still reported, with a position, rather than accepted by the cheaper walk.</summary>
    [Theory]
    [InlineData("<a><b></a>")]
    [InlineData("<a></a><b></b>")]
    [InlineData("")]
    [InlineData("<a attr=\"1\" attr=\"2\"/>")]
    [InlineData("<!DOCTYPE a [<!ENTITY x \"y\">]><a>&x;</a>")]
    public void XmlValidate_StillRefusesWhatTheTreeBuildingVersionRefused(string text)
    {
        var diagnostics = XmlFormat.Instance.Validate(text);

        Assert.Single(diagnostics);
    }

    [Fact]
    public void XmlValidate_StillRefusesNestingPastTheCeiling_AndAcceptsAtIt()
    {
        static string Nested(int depth) =>
            string.Concat(Enumerable.Repeat("<a>", depth)) + "leaf" +
            string.Concat(Enumerable.Repeat("</a>", depth));

        Assert.Empty(XmlFormat.Instance.Validate(Nested(XmlFormat.MaxDepth)));
        Assert.Single(XmlFormat.Instance.Validate(Nested(XmlFormat.MaxDepth + 1)));
    }
}
