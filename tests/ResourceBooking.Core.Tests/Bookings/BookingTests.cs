using ResourceBooking.Core.Bookings;
using ResourceBooking.Core.Errors;
using static ResourceBooking.Core.Tests.Bookings.BookingFactory;

namespace ResourceBooking.Core.Tests.Bookings;

public class BookingTests
{
    [Fact]
    public void Обычный_путь_от_создания_до_использования()
    {
        var booking = Requested();
        Assert.Equal(BookingStatus.Requested, booking.Status);

        var attempt = booking.BeginCheck();
        Assert.True(booking.IsCheckRunning);

        booking.Confirm(attempt);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.False(booking.IsCheckRunning);

        booking.CheckIn();
        Assert.Equal(BookingStatus.Used, booking.Status);
    }

    [Fact]
    public void Отклонение_сохраняет_код_причины()
    {
        var booking = Requested();
        booking.Reject(booking.BeginCheck(), "limit_exceeded");

        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.Equal("limit_exceeded", booking.RejectReasonCode);
    }

    [Fact]
    public void Падение_правила_повторяется_и_после_третьего_раза_Job_в_check_failed()
    {
        var booking = Requested();

        booking.FailCheck(booking.BeginCheck(), "boom 1");
        Assert.Equal(BookingStatus.Requested, booking.Status);
        booking.FailCheck(booking.BeginCheck(), "boom 2");
        Assert.Equal(BookingStatus.Requested, booking.Status);
        booking.FailCheck(booking.BeginCheck(), "boom 3");

        Assert.Equal(BookingStatus.CheckFailed, booking.Status);
        Assert.Equal(3, booking.FailedChecks);
        Assert.Equal("boom 3", booking.LastCheckError);
        Assert.True(booking.Status.IsFinal());
    }

    [Fact]
    public void Временный_сбой_снимает_проверку_без_засчитанной_попытки()
    {
        var booking = Requested();
        booking.ReleaseCheck(booking.BeginCheck());

        Assert.Equal(BookingStatus.Requested, booking.Status);
        Assert.False(booking.IsCheckRunning);
        Assert.Equal(0, booking.FailedChecks);
    }

    [Theory]
    [InlineData(BookingStatus.Requested, BookingCommand.Cancel)]
    [InlineData(BookingStatus.Requested, BookingCommand.CheckIn)]
    [InlineData(BookingStatus.Requested, BookingCommand.Expire)]
    [InlineData(BookingStatus.Confirmed, BookingCommand.TimeOut)]
    [InlineData(BookingStatus.Rejected, BookingCommand.Cancel)]
    [InlineData(BookingStatus.TimedOut, BookingCommand.CheckIn)]
    [InlineData(BookingStatus.Used, BookingCommand.Expire)]
    [InlineData(BookingStatus.Cancelled, BookingCommand.Expire)]
    [InlineData(BookingStatus.Cancelled, BookingCommand.Cancel)]
    [InlineData(BookingStatus.Expired, BookingCommand.CheckIn)]
    [InlineData(BookingStatus.CheckFailed, BookingCommand.TimeOut)]
    public void Недопустимый_переход_отклоняется_явной_ошибкой_и_не_меняет_состояние(
        BookingStatus from, BookingCommand command)
    {
        var booking = InStatus(from);

        var error = Assert.Throws<InvalidTransitionException>(() => Apply(booking, command));

        Assert.Equal("invalid_state", error.Code);
        Assert.Equal(from, error.From);
        Assert.Equal(command, error.Command);
        Assert.Equal(from, booking.Status);
    }

    [Theory]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.TimedOut)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Used)]
    [InlineData(BookingStatus.Expired)]
    [InlineData(BookingStatus.CheckFailed)]
    public void Из_конечного_состояния_нет_ни_одного_перехода(BookingStatus final)
    {
        foreach (var command in Enum.GetValues<BookingCommand>())
        {
            var booking = InStatus(final);
            Assert.Throws<InvalidTransitionException>(() => Apply(booking, command));
            Assert.Equal(final, booking.Status);
        }
    }

    [Fact]
    public void Таблица_не_содержит_переходов_из_конечных_состояний()
    {
        Assert.All(BookingTransitions.All, t => Assert.False(t.From.IsFinal(), $"{t.Command} из {t.From}"));
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("resourceId")]
    [InlineData("end")]
    [InlineData("start")]
    public void Неверные_параметры_дают_ошибку_с_указанием_поля(string field)
    {
        var user = field == "userId" ? Guid.Empty : Guid.NewGuid();
        var resource = field == "resourceId" ? Guid.Empty : Guid.NewGuid();
        var start = field == "start" ? Now.AddMinutes(-1) : Start;
        var end = field == "end" ? start : End;

        var error = Assert.Throws<InvalidParamsException>(
            () => Booking.Create(Guid.NewGuid(), user, resource, start, end, Now));

        Assert.Equal("invalid_params", error.Code);
        Assert.Equal(field, error.Field);
    }

    /// Подаёт команду с «правильным» номером проверки, чтобы отказ шёл именно от таблицы переходов.
    internal static void Apply(Booking booking, BookingCommand command)
    {
        switch (command)
        {
            case BookingCommand.BeginCheck: booking.BeginCheck(); break;
            case BookingCommand.Confirm: booking.Confirm(booking.CheckAttempt); break;
            case BookingCommand.Reject: booking.Reject(booking.CheckAttempt, "interval_taken"); break;
            case BookingCommand.FailCheck: booking.FailCheck(booking.CheckAttempt, "boom"); break;
            case BookingCommand.ReleaseCheck: booking.ReleaseCheck(booking.CheckAttempt); break;
            case BookingCommand.RecoverAfterRestart: booking.RecoverAfterRestart(); break;
            case BookingCommand.TimeOut: booking.TimeOut(); break;
            case BookingCommand.Cancel: booking.Cancel(); break;
            case BookingCommand.CheckIn: booking.CheckIn(); break;
            case BookingCommand.Expire: booking.Expire(); break;
            default: throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }
}
