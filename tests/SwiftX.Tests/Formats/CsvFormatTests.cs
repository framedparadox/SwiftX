using SwiftX.Services.Formats;
using Xunit;

namespace SwiftX.Tests.Formats;

public class CsvFormatTests
{
    [Fact]
    public void Detect_PrefersTabularCommaData()
    {
        const string csv = "name,age\nAda,36\nGrace,45";
        Assert.True(CsvFormat.Instance.DetectConfidence(csv) >= 70);
        Assert.True(CsvFormat.Instance.DetectConfidence(csv) > JsonFormat.Instance.DetectConfidence(csv));
    }

    [Fact]
    public void Format_NormalizesQuoting()
    {
        var result = CsvFormat.Instance.Format("a,b\n1,\"x,y\"", FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Contains("\"x,y\"", result.Text);
    }

    [Fact]
    public void Validate_ReportsUnterminatedQuote()
    {
        var diagnostics = CsvFormat.Instance.Validate("a,\"b");
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void AutoDetect_PicksCsvOverJsonForPlainTable()
    {
        const string csv = "id,name\n1,Ada\n2,Grace";
        Assert.Equal("csv", FormatRegistry.DetectBest(csv).Id);
    }
}
