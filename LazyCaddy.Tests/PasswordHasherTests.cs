using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesBcryptString_ThatVerifies()
    {
        var hash = PasswordHasher.Hash("hunter2");
        Assert.StartsWith("$2", hash);
        Assert.True(BCrypt.Net.BCrypt.Verify("hunter2", hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("wrong", hash));
    }

    [Fact]
    public void Hash_UsesCost14()
    {
        var hash = PasswordHasher.Hash("x");
        var parts = hash.Split('$');
        Assert.Equal("14", parts[2]);
    }

    [Fact]
    public void Hash_DifferentSaltsEachCall()
        => Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
}
