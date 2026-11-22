using ResourceBooking.Core.Bookings;
using ResourceBooking.Core.Dispatching;
using ResourceBooking.Core.Rules;
using ResourceBooking.Core.Storage;
using ResourceBooking.Core.Tests.Bookings;

namespace ResourceBooking.Core.Tests.Dispatching;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; set; }
}

/// Tool, который падает с ошибкой в коде.
internal sealed class CrashingRule : IBookingRule
{
    public string Code => "crashing";
    public int Order => 5;
    public int Calls { get; private set; }

    public Task<RuleResult> CheckAsync(BookingCheckContext context, CancellationToken ct)
    {
        Calls++;
        throw new NullReferenceException("bug in rule");
    }
}

/// Tool с временным сбоем чтения.
internal sealed class UnavailableRule : IBookingRule
{
    public string Code => "unavailable";
    public int Order => 5;

    public Task<RuleResult> CheckAsync(BookingCheckContext context, CancellationToken ct) =>
        throw new RuleUnavailableException("db down");
}

/// Tool, который отвечает, только когда тест разрешит: имитирует «работающий» Tool.
internal sealed class GateRule : IBookingRule
{
    private readonly TaskCompletionSource<RuleResult> _answer =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Code => "gate";
    public int Order => 1;
    public int Calls { get; private set; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<RuleResult> CheckAsync(BookingCheckContext context, CancellationToken ct)
    {
        Calls++;
        Entered.TrySetResult();
        return _answer.Task;
    }

    public void Answer(RuleResult result) => _answer.SetResult(result);
}

/// Новый Tool, о котором Dispatcher ничего не знает: отклоняет брони длиннее заданной.
internal sealed class MaxDurationRule : IBookingRule
{
    private readonly TimeSpan _max;

    public MaxDurationRule(TimeSpan max) => _max = max;

    public string Code => "max_duration";
    public int Order => 15;

    public Task<RuleResult> CheckAsync(BookingCheckContext context, CancellationToken ct) =>
        Task.FromResult(context.End - context.Start > _max
            ? RuleResult.Reject(RejectReason.OutsideAvailability, "Слишком длинная бронь")
            : RuleResult.Pass());
}

/// Собранное ядро в памяти.
internal sealed class TestCore
{
    public TestCore(params IBookingRule[] rules)
    {
        Clock = new FakeClock(BookingFactory.Now);
        Bookings = new InMemoryBookingRepository();
        Reader = new InMemoryBookingCheckReader(Bookings);
        Dispatcher = NewDispatcher(rules.Length > 0 ? rules : new IBookingRule[] { new OverlapRule(), new UserLimitRule() });
        Timer = new BookingDeadlineTimer(Bookings, Clock);
    }

    public FakeClock Clock { get; }
    public InMemoryBookingRepository Bookings { get; }
    public InMemoryBookingCheckReader Reader { get; }
    public BookingCheckDispatcher Dispatcher { get; }
    public BookingDeadlineTimer Timer { get; }

    /// Новый экземпляр Dispatcher над тем же хранилищем — например, «после перезапуска».
    public BookingCheckDispatcher NewDispatcher(params IBookingRule[] rules) =>
        new(Bookings, new BookingRuleRegistry(rules), Reader, Clock);

    public async Task<Booking> AddRequestedAsync(Guid? userId = null, Guid? resourceId = null,
        DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var booking = BookingFactory.Requested(userId, resourceId, start, end);
        await Bookings.AddAsync(booking);
        return booking;
    }

    public async Task<Booking> AddConfirmedAsync(Guid? userId = null, Guid? resourceId = null,
        DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var booking = BookingFactory.Requested(userId, resourceId, start, end);
        booking.Confirm(booking.BeginCheck());
        await Bookings.AddAsync(booking);
        return booking;
    }
}
