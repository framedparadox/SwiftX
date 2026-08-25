using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

public class Base64CodecTests
{
    [Fact]
    public void Encode_RoundTripsThroughDecode()
    {
        var encoded = Base64Codec.Encode("hello world", urlSafe: false);
        Assert.True(encoded.Success);
        Assert.Equal("aGVsbG8gd29ybGQ=", encoded.Text);

        var decoded = Base64Codec.Decode(encoded.Text);
        Assert.True(decoded.Success);
        Assert.Equal("hello world", decoded.Text);
    }

    [Fact]
    public void Encode_UrlSafe_ReplacesAlphabetAndDropsPadding()
    {
        // Chosen so the standard alphabet actually contains '+' and '/' and needs padding.
        var encoded = Base64Codec.Encode(">>>???", urlSafe: true);
        Assert.True(encoded.Success);
        Assert.DoesNotContain('+', encoded.Text);
        Assert.DoesNotContain('/', encoded.Text);
        Assert.DoesNotContain('=', encoded.Text);
    }

    [Fact]
    public void Decode_AcceptsUrlSafeAlphabetRegardlessOfHowItWasEncoded()
    {
        var standard = Base64Codec.Encode(">>>???", urlSafe: false).Text;
        var urlSafe = standard.Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var decoded = Base64Codec.Decode(urlSafe);
        Assert.True(decoded.Success);
        Assert.Equal(">>>???", decoded.Text);
    }

    [Fact]
    public void Decode_TolerantOfWhitespaceAndMissingPadding()
    {
        var encoded = Base64Codec.Encode("hello world", urlSafe: false).Text; // "aGVsbG8gd29ybGQ="
        var wrapped = encoded.TrimEnd('=').Insert(4, "\n ").Insert(0, "  ");

        var decoded = Base64Codec.Decode(wrapped);
        Assert.True(decoded.Success);
        Assert.Equal("hello world", decoded.Text);
    }

    [Fact]
    public void Decode_InvalidBase64_Fails()
    {
        var result = Base64Codec.Decode("not base64!!!");
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void Decode_ValidBase64ButNotUtf8_Fails()
    {
        // 0xFF 0xFE is not a valid UTF-8 sequence.
        var result = Base64Codec.Decode(Convert.ToBase64String([0xFF, 0xFE]));
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void Encode_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", Base64Codec.Encode("", urlSafe: false).Text);
        Assert.Equal("", Base64Codec.Decode("").Text);
    }

    [Theory]
    [InlineData("aGVsbG8gd29ybGQ=", true)] // padded
    [InlineData("aGVsbG8td29ybGQ", true)] // unpadded, valid length class (15 % 4 == 3)
    [InlineData("hello, world!", false)] // punctuation outside the base64 alphabet
    [InlineData("short", false)] // below the minimum length
    [InlineData("not*base64*text*", false)] // '*' outside the base64 alphabet
    public void IsLikelyBase64_DetectsCharsetAndLength(string text, bool expected)
    {
        Assert.Equal(expected, Base64Codec.IsLikelyBase64(text));
    }
}
