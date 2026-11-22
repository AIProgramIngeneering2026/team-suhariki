using ResourceBooking.Core.Bookings;
using ResourceBooking.Core.Dispatching;
using ResourceBooking.Core.Rules;
using ResourceBooking.Core.Storage;
using ResourceBooking.Core.Tests.Bookings;

namespace ResourceBooking.Core.Tests.Dispatching;

public class DispatcherTests
{
    [Fact]
    public async Task Обычный_путь_создание_проверка_подтверждение_отметка()
    {
        var core = new TestCore();
        var booking = await core.AddRequestedAsync();

        var result = await core.Dispatcher.DispatchAsync(booking.Id);

        Assert.Equal(DispatchOutcome.Confirmed, result.Outcome);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);

        core.Clock.UtcNow = booking.Start.AddMinutes(5);
        await core.Bookings.UpdateAsync(booking.Id, b => { b.CheckIn(); return 0; });
        Assert.Equal(BookingStatus.Used, booking.Status);
    }

    [Fact]
    public async Task Отказ_Tool_переводит_Job_в_rejected_с_причиной()
    {
        var core = new TestCore();
        var resource = Guid.NewGuid();
        await core.AddConfirmedAsync(resourceId: resource);
        var booking = await core.AddRequestedAsync(resourceId: resource,
            start: BookingFactory.Start.AddMinutes(30), end: BookingFactory.End.AddMinutes(30));

        var result = await core.Dispatcher.DispatchAsync(booking.Id);

        Assert.Equal(DispatchOutcome.Rejected, result.Outcome);
        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.Equal("interval_taken", booking.RejectReasonCode);
    }

    [Fact]
    public async Task Соседний_интервал_не_пересекается()
    {
        var core = new TestCore();
        var resource = Guid.NewGuid();
        await core.AddConfirmedAsync(resourceId: resource);
        var booking = await core.AddRequestedAsync(resourceId: resource,
            start: BookingFactory.End, end: BookingFactory.End.AddHours(1));

        var result = await core.Dispatcher.DispatchAsync(booking.Id);

        Assert.Equal(DispatchOutcome.Confirmed, result.Outcome);
    }

    [Fact]
    public async Task Лимит_пользователя_четвёртая_бронь_отклоняется()
    {
        var core = new TestCore();
        var user = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
            await core.AddConfirmedAsync(userId: user);
        var booking = await core.AddRequestedAsync(userId: user);

        await core.Dispatcher.DispatchAsync(booking.Id);

        Assert.Equal("limit_exceeded", booking.RejectReasonCode);
    }

    [Fact]
    public async Task Падение_Tool_переводит_Job_в_check_failed_после_трёх_попыток()
    {
        var crashing = new CrashingRule();
        var core = new TestCore(crashing, new OverlapRule());
        var booking = await core.AddRequestedAsync();

        var first = await core.Dispatcher.DispatchAsync(booking.Id);
        var second = await core.Dispatcher.DispatchAsync(booking.Id);
        var third = await core.Dispatcher.DispatchAsync(booking.Id);

        Assert.Equal(DispatchOutcome.FailedWillRetry, first.Outcome);
        Assert.Equal(DispatchOutcome.FailedWillRetry, second.Outcome);
        Assert.Equal(DispatchOutcome.CheckFailed, third.Outcome);
        Assert.Equal(BookingStatus.CheckFailed, booking.Status);
        Assert.Contains("crashing", booking.LastCheckError);
        Assert.Equal(3, crashing.Calls);
        await Assert.ThrowsAsync<InvalidTransitionException>(() => core.Dispatcher.DispatchAsync(booking.Id));
    }

    [Fact]
    public async Task Временный_сбой_оставляет_Job_requested_без_засчитанной_попытки()
    {
        var core = new TestCore(new UnavailableRule());
        var booking = await core.AddRequestedAsync();

        var result = await core.Dispatcher.DispatchAsync(booking.Id);

        Assert.Equal(DispatchOutcome.Unavailable, result.Outcome);
        Assert.Equal(BookingStatus.Requested, booking.Status);
        Assert.False(booking.IsCheckRunning);
        Assert.Equal(0, booking.FailedChecks);
    }

    [Fact]
    public async Task Новый_Tool_добавляется_регистрацией_без_правки_Dispatcher()
    {
        var core = new TestCore(new OverlapRule(), new MaxDurationRule(TimeSpan.FromMinutes(30)));
        var booking = await core.AddRequestedAsync();

        var result = await core.Dispatcher.DispatchAsync(booking.Id);

        Assert.Equal(DispatchOutcome.Rejected, result.Outcome);
        Assert.Equal("outside_availability", booking.RejectReasonCode);
    }

    [Fact]
    public async Task После_start_проверка_не_запускается()
    {
        var gate = new GateRule();
        var core = new TestCore(gate);
        var booking = await core.AddRequestedAsync();
        core.Clock.UtcNow = booking.Start;

        var result = await core.Dispatcher.DispatchAsync(booking.Id);

        Assert.Equal(DispatchOutcome.DeadlinePassed, result.Outcome);
        Assert.Equal(0, gate.Calls);
        Assert.False(booking.IsCheckRunning);
    }

    [Fact]
    public async Task Неизвестная_бронь_ошибка_booking_not_found()
    {
        var core = new TestCore();

        var error = await Assert.ThrowsAsync<BookingNotFoundException>(
            () => core.Dispatcher.DispatchAsync(Guid.NewGuid()));
        Assert.Equal("booking_not_found", error.Code);
    }

    [Fact]
    public async Task Таймер_переводит_confirmed_в_expired_после_end()
    {
        var core = new TestCore();
        var booking = await core.AddConfirmedAsync();
        core.Clock.UtcNow = booking.End;

        Assert.Equal(1, await core.Timer.RunOnceAsync());
        Assert.Equal(BookingStatus.Expired, booking.Status);
    }
}
