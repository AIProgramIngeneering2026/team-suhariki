namespace ResourceBooking.Core.Rules;

/// Результат Tool (ToolResult).
public sealed record RuleResult
{
    private static readonly RuleResult PassResult = new(true, null, null);

    private RuleResult(bool passed, RejectReason? reason, string? message)
    {
        Passed = passed;
        Reason = reason;
        Message = message;
    }

    public bool Passed { get; }

    /// null, если Passed.
    public RejectReason? Reason { get; }

    public string? Message { get; }

    public static RuleResult Pass() => PassResult;

    public static RuleResult Reject(RejectReason reason, string message) => new(false, reason, message);
}
