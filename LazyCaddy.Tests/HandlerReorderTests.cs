using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class HandlerReorderTests
{
    [Fact]
    public void Swap_MovesElementUp()
    {
        var arr = """[{"handler":"a"},{"handler":"b"},{"handler":"c"}]""";
        var result = HandlerReorder.Swap(arr, index: 1, delta: -1);
        Assert.Equal("""[{"handler":"b"},{"handler":"a"},{"handler":"c"}]""", Compact(result));
    }

    [Fact]
    public void Swap_MovesElementDown()
    {
        var arr = """[{"handler":"a"},{"handler":"b"},{"handler":"c"}]""";
        var result = HandlerReorder.Swap(arr, index: 1, delta: +1);
        Assert.Equal("""[{"handler":"a"},{"handler":"c"},{"handler":"b"}]""", Compact(result));
    }

    [Fact]
    public void Swap_OutOfBounds_ReturnsUnchanged()
    {
        var arr = """[{"handler":"a"},{"handler":"b"}]""";
        Assert.Equal(Compact(arr), Compact(HandlerReorder.Swap(arr, 0, -1)));
        Assert.Equal(Compact(arr), Compact(HandlerReorder.Swap(arr, 1, +1)));
    }

    [Fact]
    public void Swap_NonArray_ReturnsUnchanged()
    {
        var s = """{"not":"array"}""";
        Assert.Equal(s, HandlerReorder.Swap(s, 0, 1));
    }

    private static string Compact(string json)
    {
        using var d = System.Text.Json.JsonDocument.Parse(json);
        return System.Text.Json.JsonSerializer.Serialize(d.RootElement);
    }
}
