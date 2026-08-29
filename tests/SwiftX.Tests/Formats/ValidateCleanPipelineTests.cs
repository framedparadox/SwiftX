using SwiftX.Services.Formats;
using Xunit;

namespace SwiftX.Tests.Formats;

/// <summary>Mirrors the Validate button: sanitize messy paste, then pretty-print at 2 spaces.</summary>
public class ValidateCleanPipelineTests
{
    [Fact]
    public void CleanThenFormat_JsonFromEscapedLogLine()
    {
        const string pasted = "  \"{\\\"name\\\":\\\"Ada\\\",\\\"n\\\":1}\"  ";
        var cleaned = InputSanitizer.Clean(pasted);
        var result = JsonFormat.Instance.Format(cleaned, new FormatOptions { IndentWidth = 2 });
        Assert.True(result.Success);
        Assert.Equal("""
            {
              "name": "Ada",
              "n": 1
            }
            """.Replace("\r\n", "\n"), result.Text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void DetectBest_AfterClean_StillJson()
    {
        var cleaned = InputSanitizer.Clean("\"{\\\"ok\\\":true}\"");
        Assert.Equal("json", FormatRegistry.DetectBest(cleaned).Id);
    }
}
