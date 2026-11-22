using ResourceBooking.Core.Rules;

namespace ResourceBooking.Core.Tests.Rules;

internal sealed class StubReader : IBookingCheckReader
{
    public bool Overlap { get; init; }
    public int ActiveBookings { get; init; }
    public Exception? Failure { get; init; }

    public Task<bool> HasActiveOverlapAsync(Guid resourceId, DateTimeOffset start, DateTimeOffset end,
        CancellationToken ct) => Failure is null ? Task.FromResult(Overlap) : Task.FromException<bool>(Failure);

    public Task<int> CountActiveBookingsAsync(Guid userId, DateTimeOffset now, CancellationToken ct) =>
        Failure is null ? Task.FromResult(ActiveBookings) : Task.FromException<int>(Failure);

    public static BookingCheckContext Context(IBookingCheckReader reader)
    {
        var now = new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
        return new BookingCheckContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            now.AddHours(1), now.AddHours(2), now, reader);
    }
}
