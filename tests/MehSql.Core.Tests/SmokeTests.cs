using Xunit;

namespace MehSql.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Solution_Builds_Successfully()
    {
        Assert.True(true, "Basic smoke test to verify test infrastructure works");
    }

    [Fact]
    public void Core_Assembly_Loads()
    {
        var assembly = typeof(object).Assembly;
        Assert.NotNull(assembly);
    }
}
