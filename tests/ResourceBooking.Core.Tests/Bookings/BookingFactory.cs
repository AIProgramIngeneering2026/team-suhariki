using ResourceBooking.Core.Bookings;

namespace ResourceBooking.Core.Tests.Bookings;

internal static class BookingFactory
{
    public static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Start = Now.AddHours(2);
    public static readonly DateTimeOffset End = Start.AddHours(1);

    public static Booking Requested(Guid? userId = null, Guid? resourceId = null,
        DateTimeOffset? start = null, DateTimeOffset? end = null) =>
        Booking.Create(Guid.NewGuid(), userId ?? Guid.NewGuid(), resourceId ?? Guid.NewGuid(),
            start ?? Start, end ?? End, Now);

    public static Booking Confirmed()
    {
        var booking = Requested();
        booking.Confirm(booking.BeginCheck());
        return booking;
    }

    public static Booking Rejected()
    {
        var booking = Requested();
        booking.Reject(booking.BeginCheck(), "interval_taken");
        return booking;
    }

    public static Booking CheckFailed()
    {
        var booking = Requested();
        for (var i = 0; i < Booking.DefaultMaxFailedChecks; i++)
            booking.FailCheck(booking.BeginCheck(), "boom");
        return booking;
    }

    public static Booking InStatus(BookingStatus status)
    {
        switch (status)
        {
            case BookingStatus.Requested: return Requested();
            case BookingStatus.Confirmed: return Confirmed();
            case BookingStatus.Rejected: return Rejected();
            case BookingStatus.CheckFailed: return CheckFailed();
            case BookingStatus.TimedOut:
            {
                var b = Requested();
                b.TimeOut();
                return b;
            }
            case BookingStatus.Cancelled:
            {
                var b = Confirmed();
                b.Cancel();
                return b;
            }
            case BookingStatus.Used:
            {
                var b = Confirmed();
                b.CheckIn();
                return b;
            }
            case BookingStatus.Expired:
            {
                var b = Confirmed();
                b.Expire();
                return b;
            }
            default: throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }
}
