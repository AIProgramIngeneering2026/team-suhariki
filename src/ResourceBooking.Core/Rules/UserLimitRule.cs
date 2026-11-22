namespace ResourceBooking.Core.Rules;

/// Нет брони confirmed или used того же ресурса на пересекающемся интервале (сценарий E2).
public sealed class OverlapRule : IBookingRule
{
    public const string RuleCode = "overlap";

    public string Code => RuleCode;

    public int Order => 30;

    public async Task<RuleResult> CheckAsync(BookingCheckContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var taken = await context.Reader.HasActiveOverlapAsync(context.ResourceId, context.Start, context.End, ct);

        return taken
            ? RuleResult.Reject(RejectReason.IntervalTaken, "Интервал занят")
            : RuleResult.Pass();
    }
}
