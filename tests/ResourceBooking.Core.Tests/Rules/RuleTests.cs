using ResourceBooking.Core.Rules;

namespace ResourceBooking.Core.Tests.Rules;

public class RuleTests
{
    [Fact]
    public async Task OverlapRule_свободный_интервал_Pass()
    {
        var result = await new OverlapRule().CheckAsync(StubReader.Context(new StubReader()), default);

        Assert.True(result.Passed);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task OverlapRule_занятый_интервал_Reject_interval_taken()
    {
        var result = await new OverlapRule()
            .CheckAsync(StubReader.Context(new StubReader { Overlap = true }), default);

        Assert.False(result.Passed);
        Assert.Equal(RejectReason.IntervalTaken, result.Reason);
        Assert.Equal("interval_taken", result.Reason!.Value.ToApiCode());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    public async Task UserLimitRule_три_активные_брони_и_больше_Reject(int active, bool passed)
    {
        var result = await new UserLimitRule()
            .CheckAsync(StubReader.Context(new StubReader { ActiveBookings = active }), default);

        Assert.Equal(passed, result.Passed);
        if (!passed)
            Assert.Equal(RejectReason.LimitExceeded, result.Reason);
    }

    [Fact]
    public async Task Временный_сбой_чтения_пробрасывается_как_RuleUnavailableException()
    {
        var reader = new StubReader { Failure = new RuleUnavailableException("db down") };

        await Assert.ThrowsAsync<RuleUnavailableException>(
            () => new OverlapRule().CheckAsync(StubReader.Context(reader), default));
    }

    [Fact]
    public void Оба_Tool_за_одним_контрактом_и_с_разными_кодами()
    {
        IBookingRule[] rules = [new OverlapRule(), new UserLimitRule()];

        Assert.Equal(new[] { "overlap", "user_limit" }, rules.Select(r => r.Code));
        Assert.True(rules[0].Order < rules[1].Order);
    }
}
