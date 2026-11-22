using ResourceBooking.Core.Bookings;
using ResourceBooking.Core.Rules;

namespace ResourceBooking.Core.Storage;

/// Чтение для правил поверх IBookingRepository.
public sealed class InMemoryBookingCheckReader : IBookingCheckReader
{
    private readonly IBookingRepository _bookings;

    public InMemoryBookingCheckReader(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task<bool> HasActiveOverlapAsync(Guid resourceId, DateTimeOffset start, DateTimeOffset end,
        CancellationToken ct)
    {
        var overlapping = await _bookings.FindAsync(b =>
            b.ResourceId == resourceId
            && IsActive(b.Status)
            && b.Start < end
            && start < b.End, ct);

        return overlapping.Count > 0;
    }

    public async Task<int> CountActiveBookingsAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await _bookings.FindAsync(b =>
            b.UserId == userId
            && IsActive(b.Status)
            && b.End > now, ct);

        return active.Count;
    }

    private static bool IsActive(BookingStatus status) =>
        status is BookingStatus.Confirmed or BookingStatus.Used;
}
