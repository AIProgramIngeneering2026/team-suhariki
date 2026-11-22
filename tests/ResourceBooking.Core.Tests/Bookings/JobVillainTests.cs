using ResourceBooking.Core.Bookings;
using static ResourceBooking.Core.Tests.Bookings.BookingFactory;

namespace ResourceBooking.Core.Tests.Bookings;

/// Злодейства З1–З6 из «Взлома Job» (Zadania/tasks/p3_job/README.md) на уровне Job.
/// Соответствие команд: create → Booking.Create, start → BeginCheck, finish → Confirm/Reject,
/// fail → FailCheck, restart → RecoverAfterRestart, cancel → Cancel. «Готово» — confirmed, рабочее — requested
/// с запущенной проверкой. Те же злодейства через Dispatcher — в Dispatching/DispatcherVillainTests.
public class JobVillainTests
{
    [Fact]
    public void З1_двойная_работа_второй_start_отклоняется()
    {
        // create, start, start
        var booking = Requested();
        booking.BeginCheck();

        Assert.Throws<InvalidTransitionException>(() => booking.BeginCheck());
        Assert.Equal(1, booking.CheckAttempt);
    }

    [Fact]
    public void З1_после_restart_ответ_пропавшего_Tool_не_принимается()
    {
        // create, start(#1), restart, start(#2) — работает только #2; итог #1 отклоняется
        var booking = Requested();
        var lost = booking.BeginCheck();
        booking.RecoverAfterRestart();
        var current = booking.BeginCheck();

        Assert.Throws<InvalidTransitionException>(() => booking.Confirm(lost));
        booking.Confirm(current);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void З2_вечная_работа_restart_снимает_проверку()
    {
        // create, start, restart → requested без работающего Tool, проверку можно запустить снова
        var booking = Requested();
        booking.BeginCheck();

        booking.RecoverAfterRestart();

        Assert.Equal(BookingStatus.Requested, booking.Status);
        Assert.False(booking.IsCheckRunning);
        booking.BeginCheck();
    }

    [Fact]
    public void З2_вечная_работа_дедлайн_завершает_Job_даже_во_время_проверки()
    {
        // create, start, (restart), TimeOut → timed_out; поздний finish отклоняется
        var booking = Requested();
        var attempt = booking.BeginCheck();

        booking.TimeOut();

        Assert.Equal(BookingStatus.TimedOut, booking.Status);
        Assert.Throws<InvalidTransitionException>(() => booking.Confirm(attempt));
        Assert.Equal(BookingStatus.TimedOut, booking.Status);
    }

    [Fact]
    public void З3_отменить_можно_только_confirmed_отмена_во_время_проверки_отклоняется()
    {
        // create, start, cancel → отказ (ADR-0004)
        var booking = Requested();
        booking.BeginCheck();

        Assert.Throws<InvalidTransitionException>(() => booking.Cancel());
        Assert.Equal(BookingStatus.Requested, booking.Status);
    }

    [Fact]
    public void З3_отменённое_не_становится_готовым()
    {
        // create, start, finish, cancel, start / finish / check-in → отказ
        var booking = Confirmed();
        var attempt = booking.CheckAttempt;
        booking.Cancel();

        Assert.Throws<InvalidTransitionException>(() => booking.Confirm(attempt));
        Assert.Throws<InvalidTransitionException>(() => booking.BeginCheck());
        Assert.Throws<InvalidTransitionException>(() => booking.CheckIn());
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public void З4_два_итога_rejected_не_становится_confirmed()
    {
        // create, start, finish(reject), finish(pass) → отказ
        var booking = Requested();
        var attempt = booking.BeginCheck();
        booking.Reject(attempt, "interval_taken");

        Assert.Throws<InvalidTransitionException>(() => booking.Confirm(attempt));
        Assert.Equal(BookingStatus.Rejected, booking.Status);
    }

    [Fact]
    public void З4_два_итога_check_failed_не_становится_confirmed()
    {
        var booking = CheckFailed();

        Assert.Throws<InvalidTransitionException>(() => booking.Confirm(booking.CheckAttempt));
        Assert.Throws<InvalidTransitionException>(() => booking.BeginCheck());
        Assert.Equal(BookingStatus.CheckFailed, booking.Status);
    }

    [Fact]
    public void З4_итог_упавшей_попытки_не_принимается_после_повтора()
    {
        // create, start(#1), fail(#1), start(#2), finish(#1) → отказ: у #1 уже был итог
        var booking = Requested();
        var first = booking.BeginCheck();
        booking.FailCheck(first, "boom");
        booking.BeginCheck();

        Assert.Throws<InvalidTransitionException>(() => booking.Confirm(first));
        Assert.Equal(BookingStatus.Requested, booking.Status);
    }

    [Fact]
    public void З5_успех_без_работы_confirm_без_start_отклоняется()
    {
        // create, finish → отказ
        var booking = Requested();

        Assert.Throws<InvalidTransitionException>(() => booking.Confirm(1));
        Assert.Throws<InvalidTransitionException>(() => booking.Confirm(0));
        Assert.Equal(BookingStatus.Requested, booking.Status);
    }

    [Fact]
    public void З5_успех_без_работы_check_in_из_requested_отклоняется()
    {
        var booking = Requested();

        Assert.Throws<InvalidTransitionException>(() => booking.CheckIn());
        Assert.Equal(BookingStatus.Requested, booking.Status);
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Used)]
    public void З6_повтор_успешного_готовый_Job_не_уходит_в_работу(BookingStatus done)
    {
        // create, start, finish, start → отказ
        var booking = InStatus(done);

        Assert.Throws<InvalidTransitionException>(() => booking.BeginCheck());
        Assert.False(booking.IsCheckRunning);
        Assert.Equal(done, booking.Status);
    }
}
