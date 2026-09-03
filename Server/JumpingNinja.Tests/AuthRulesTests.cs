using JumpingNinja.Api.Auth;
using Xunit;

namespace JumpingNinja.Tests;

public sealed class AuthRulesTests
{
    [Theory]
    [InlineData("ninja")]
    [InlineData("Ninja_01")]
    [InlineData("abc1234567890123")]
    public void AcceptsValidUsernames(string username)
    {
        Assert.Null(AuthRules.ValidateUsername(username));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("this_username_is_too_long")]
    [InlineData("bad-name")]
    [InlineData("玩家")]
    public void RejectsInvalidUsernames(string username)
    {
        Assert.NotNull(AuthRules.ValidateUsername(username));
    }

    [Fact]
    public void UsernameNormalizationTrimsAndIgnoresCase()
    {
        Assert.Equal("ninja_01", AuthRules.NormalizeUsername("  Ninja_01 "));
    }

    [Theory]
    [InlineData("password1")]
    [InlineData("Ninja123")]
    public void AcceptsPasswordsWithLettersAndNumbers(string password)
    {
        Assert.Null(AuthRules.ValidatePassword(password));
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("password")]
    [InlineData("12345678")]
    public void RejectsWeakPasswords(string password)
    {
        Assert.NotNull(AuthRules.ValidatePassword(password));
    }
}
