namespace ResourceBooking.Core.Rules;

/// Коды причин совпадают с ReasonCode в docs/api/openapi.yaml.
public enum RejectReason
{
    NoPermission,          // no_permission — «Нет права на бронирование»
    OutsideAvailability,   // outside_availability — «Вне часов доступности ресурса»
    IntervalTaken,         // interval_taken — «Интервал занят»
    LimitExceeded          // limit_exceeded — «Превышен лимит»
}

public static class RejectReasonExtensions
{
    public static string ToApiCode(this RejectReason reason) => reason switch
    {
        RejectReason.NoPermission => "no_permission",
        RejectReason.OutsideAvailability => "outside_availability",
        RejectReason.IntervalTaken => "interval_taken",
        RejectReason.LimitExceeded => "limit_exceeded",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };
}
