namespace ResourceBooking.Core.Rules;

/// У пользователя меньше трёх броней confirmed или used с End позже текущего времени (сценарии E3, E7).
/// Текущий Job в счёт не входит: он ещё requested.
public sealed class UserLimitRule : IBookingRule
{
    public const string RuleCode = "user_limit";
    public const int DefaultLimit = 3;

    private readonly int _limit;

    public UserLimitRule(int limit = DefaultLimit)
    {
        if (limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Лимит должен быть не меньше 1");
        _limit = limit;
    }

    public string Code => RuleCode;

    public int Order => 40;

    public async Task<RuleResult> CheckAsync(BookingCheckContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var active = await context.Reader.CountActiveBookingsAsync(context.UserId, context.Now, ct);

        return active >= _limit
            ? RuleResult.Reject(RejectReason.LimitExceeded, "Превышен лимит")
            : RuleResult.Pass();
    }
}
