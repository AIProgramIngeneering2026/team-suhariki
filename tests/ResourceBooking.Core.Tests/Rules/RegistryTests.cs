using ResourceBooking.Core.Rules;

namespace ResourceBooking.Core.Tests.Rules;

public class RegistryTests
{
    private static BookingRuleRegistry Registry() => new([new UserLimitRule(), new OverlapRule()]);

    [Theory]
    [InlineData("overlap", typeof(OverlapRule))]
    [InlineData("user_limit", typeof(UserLimitRule))]
    public void По_имени_операции_выдаёт_Tool(string code, Type expected)
    {
        var rule = Registry().Get(code);

        Assert.IsType(expected, rule);
        Assert.Equal(code, rule.Code);
    }

    [Theory]
    [InlineData("send_sms")]
    [InlineData("OVERLAP")]
    [InlineData("")]
    public void Неизвестная_операция_ошибка_unknown_operation(string code)
    {
        var error = Assert.Throws<UnknownOperationException>(() => Registry().Get(code));

        Assert.Equal("unknown_operation", error.Code);
        Assert.Equal(code, error.Operation);
    }

    [Fact]
    public void Правила_отдаются_в_порядке_Order_независимо_от_порядка_регистрации()
    {
        Assert.Equal(new[] { "overlap", "user_limit" }, Registry().GetRules().Select(r => r.Code));
    }

    [Fact]
    public void Пустой_список_правил_ошибка_конфигурации()
    {
        Assert.Throws<InvalidOperationException>(() => new BookingRuleRegistry([]));
    }

    [Fact]
    public void Повтор_кода_правила_ошибка_конфигурации()
    {
        Assert.Throws<InvalidOperationException>(() => new BookingRuleRegistry([new OverlapRule(), new OverlapRule()]));
    }
}
