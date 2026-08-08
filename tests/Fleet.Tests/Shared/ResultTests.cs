using Fleet.Shared;

namespace Fleet.Tests.Shared;

public class ResultTests
{
    [Fact]
    public void Ok_carries_a_value_and_no_error()
    {
        var r = Result<int>.Ok(42);

        Assert.True(r.Succeeded);
        Assert.Equal(42, r.Value);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Fail_carries_an_error_and_throws_on_Value()
    {
        var r = Result<int>.Fail("nope");

        Assert.False(r.Succeeded);
        Assert.Equal("nope", r.Error);
        Assert.Throws<InvalidOperationException>(() => r.Value);
    }

    [Fact]
    public void Fail_rejects_an_empty_reason()
        => Assert.Throws<ArgumentException>(() => Result<int>.Fail("  "));

    [Fact]
    public void Unit_result_works_without_a_payload()
    {
        Assert.True(Result.Ok().Succeeded);
        Assert.Equal("bad", Result.Fail("bad").Error);
    }
}
