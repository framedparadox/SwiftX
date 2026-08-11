using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

public class ValueMaskingTests
{
    [Theory]
    [InlineData("ab", "ab")]
    [InlineData("abc", "abc")]
    [InlineData("abcd", "abc•")]
    [InlineData("abcde", "abc•e")]
    [InlineData("abcdef", "abc••f")]
    [InlineData("abcdefg", "abc•••g")]
    [InlineData("abcdefgh", "abc••fgh")]
    [InlineData("abcdefghij", "abc••••hij")]
    public void Mask_FollowsLengthRules(string value, string expected)
    {
        Assert.Equal(expected, ValueMasking.Mask(value));
    }
}
