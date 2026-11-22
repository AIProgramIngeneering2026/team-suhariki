using ResourceBooking.Core.Bookings;
using ResourceBooking.Core.Storage;

namespace ResourceBooking.Core.Dispatching;

/// TimerWorker: наступил start — requested → timed_out; наступил end — confirmed → expired.
/// Гарантирует, что Job не остаётся в работе вечно, даже если проверка пропала (З2).
public sealed class BookingDeadlineTimer
{
    private readonly IBookingRepository _bookings;
    private readonly IClock _clock;

    public BookingDeadlineTimer(IBookingRepository bookings, IClock clock)
    {
        _bookings = bookings;
        _clock = clock;
    }

    /// Один проход таймера. Возвращает число изменённых броней.
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var due = await _bookings.FindAsync(b =>
            (b.Status == BookingStatus.Requested && b.Start <= now)
            || (b.Status == BookingStatus.Confirmed && b.End <= now), ct);

        var changed = 0;
        foreach (var booking in due)
        {
            // Условие перепроверяется внутри атомарного изменения: состояние могло смениться после выборки.
            var applied = await _bookings.UpdateAsync(booking.Id, b =>
            {
                if (b.Status == BookingStatus.Requested && b.Start <= now)
                {
                    b.TimeOut();
                    return true;
                }

                if (b.Status == BookingStatus.Confirmed && b.End <= now)
                {
                    b.Expire();
                    return true;
                }

                return false;
            }, ct);

            if (applied)
                changed++;
        }

        return changed;
    }
}
