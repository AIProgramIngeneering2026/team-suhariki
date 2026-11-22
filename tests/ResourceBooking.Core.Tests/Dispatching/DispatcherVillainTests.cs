using ResourceBooking.Core.Bookings;
using ResourceBooking.Core.Dispatching;
using ResourceBooking.Core.Rules;

namespace ResourceBooking.Core.Tests.Dispatching;

/// Злодейства З1–З6 сквозь Dispatcher: те же цепочки, но команды подаёт не тест, а ядро целиком.
public class DispatcherVillainTests
{
    [Fact]
    public async Task З1_двойная_работа_второй_Dispatcher_не_запускает_Tool_пока_идёт_первый()
    {
        var gate = new GateRule();
        var core = new TestCore(gate);
        var booking = await core.AddRequestedAsync();

        var first = core.Dispatcher.DispatchAsync(booking.Id);
        await gate.Entered.Task;

        await Assert.ThrowsAsync<InvalidTransitionException>(() => core.Dispatcher.DispatchAsync(booking.Id));
        Assert.Equal(1, gate.Calls);

        gate.Answer(RuleResult.Pass());
        Assert.Equal(DispatchOutcome.Confirmed, (await first).Outcome);
    }

    [Fact]
    public async Task З2_вечная_работа_после_перезапуска_Job_проверяется_заново_а_старый_итог_отбрасывается()
    {
        var gate = new GateRule();
        var core = new TestCore(gate);
        var booking = await core.AddRequestedAsync();

        var lostCheck = core.Dispatcher.DispatchAsync(booking.Id);   // Tool работает...
        await gate.Entered.Task;

        // restart: новый процесс — новый Dispatcher над тем же хранилищем.
        var restarted = core.NewDispatcher(new OverlapRule());
        Assert.Equal(1, await restarted.RecoverAfterRestartAsync());
        var results = await restarted.DispatchPendingAsync();

        Assert.Equal(DispatchOutcome.Confirmed, Assert.Single(results).Outcome);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);

        // «Пропавший» Tool всё-таки ответил — Job итог не принимает.
        gate.Answer(RuleResult.Reject(RejectReason.IntervalTaken, "late"));
        Assert.Equal(DispatchOutcome.ResultDiscarded, (await lostCheck).Outcome);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task З2_вечная_работа_таймер_переводит_зависший_Job_в_timed_out()
    {
        var gate = new GateRule();
        var core = new TestCore(gate);
        var booking = await core.AddRequestedAsync();
        var lostCheck = core.Dispatcher.DispatchAsync(booking.Id);
        await gate.Entered.Task;

        core.Clock.UtcNow = booking.Start;
        Assert.Equal(1, await core.Timer.RunOnceAsync());
        Assert.Equal(BookingStatus.TimedOut, booking.Status);

        gate.Answer(RuleResult.Pass());
        var late = await lostCheck;
        Assert.NotEqual(DispatchOutcome.Confirmed, late.Outcome);
        Assert.Equal(BookingStatus.TimedOut, booking.Status);
    }

    [Fact]
    public async Task З3_отменённое_не_становится_готовым_повторная_проверка_отклоняется()
    {
        var core = new TestCore();
        var booking = await core.AddRequestedAsync();
        await core.Dispatcher.DispatchAsync(booking.Id);
        await core.Bookings.UpdateAsync(booking.Id, b => { b.Cancel(); return 0; });

        await Assert.ThrowsAsync<InvalidTransitionException>(() => core.Dispatcher.DispatchAsync(booking.Id));
        Assert.Empty(await core.Dispatcher.DispatchPendingAsync());
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public async Task З4_два_итога_отклонённая_бронь_не_проверяется_повторно()
    {
        var core = new TestCore(new MaxDurationRule(TimeSpan.FromMinutes(1)));
        var booking = await core.AddRequestedAsync();
        await core.Dispatcher.DispatchAsync(booking.Id);
        Assert.Equal(BookingStatus.Rejected, booking.Status);

        await Assert.ThrowsAsync<InvalidTransitionException>(() => core.Dispatcher.DispatchAsync(booking.Id));
        Assert.Equal(BookingStatus.Rejected, booking.Status);
    }

    [Fact]
    public async Task З5_успех_без_работы_confirmed_только_после_ответа_Tool()
    {
        var gate = new GateRule();
        var core = new TestCore(gate);
        var booking = await core.AddRequestedAsync();

        var check = core.Dispatcher.DispatchAsync(booking.Id);
        await gate.Entered.Task;
        Assert.Equal(BookingStatus.Requested, booking.Status);   // Tool ещё работает — не готово

        gate.Answer(RuleResult.Pass());
        await check;
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(1, gate.Calls);
    }

    [Fact]
    public async Task З6_повтор_успешного_confirmed_не_уходит_в_работу()
    {
        var core = new TestCore();
        var booking = await core.AddRequestedAsync();
        await core.Dispatcher.DispatchAsync(booking.Id);

        await Assert.ThrowsAsync<InvalidTransitionException>(() => core.Dispatcher.DispatchAsync(booking.Id));
        Assert.Empty(await core.Dispatcher.DispatchPendingAsync());
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(1, booking.CheckAttempt);
    }
}
