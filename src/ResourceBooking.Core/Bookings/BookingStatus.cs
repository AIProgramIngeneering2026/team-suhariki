namespace ResourceBooking.Core.Bookings;

/// Состояния Job (брони), docs/job-states.md.
public enum BookingStatus
{
    Requested,
    Confirmed,
    Rejected,
    TimedOut,
    Cancelled,
    Used,
    Expired,

    /// Проверка упала в коде правила maxFailedChecks раз подряд. Конечное, ресурс не занимает, письма нет.
    CheckFailed
}

public static class BookingStatusExtensions
{
    /// Код состояния — значение поля status в API.
    public static string ToApiCode(this BookingStatus status) => status switch
    {
        BookingStatus.Requested => "requested",
        BookingStatus.Confirmed => "confirmed",
        BookingStatus.Rejected => "rejected",
        BookingStatus.TimedOut => "timed_out",
        BookingStatus.Cancelled => "cancelled",
        BookingStatus.Used => "used",
        BookingStatus.Expired => "expired",
        BookingStatus.CheckFailed => "check_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static bool IsFinal(this BookingStatus status) =>
        status is not (BookingStatus.Requested or BookingStatus.Confirmed);
}
