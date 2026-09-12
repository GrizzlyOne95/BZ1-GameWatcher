using BZAPI.Configuration;
using BZAPI.Websocket;
using Xunit;

namespace API.Tests;

public sealed class ChatObserverConnectionBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BudgetIsSharedAcrossAllSocketCreationsAndThenOpensCircuit()
    {
        var budget = CreateBudget();

        Assert.True(budget.TryReserve(Start, out _));
        Assert.True(budget.TryReserve(Start.AddMinutes(1), out _));
        Assert.True(budget.TryReserve(Start.AddMinutes(2), out _));

        Assert.False(budget.TryReserve(Start.AddMinutes(3), out var wait));
        Assert.Equal(TimeSpan.FromHours(1), wait);
        Assert.False(budget.TryReserve(Start.AddMinutes(20), out _));
    }

    [Fact]
    public void ExpiredWindowAllowsNewAttemptsWithoutLobbySpecificState()
    {
        var budget = CreateBudget();

        Assert.True(budget.TryReserve(Start, out _));
        Assert.True(budget.TryReserve(Start.AddMinutes(1), out _));
        Assert.True(budget.TryReserve(Start.AddMinutes(2), out _));

        Assert.True(budget.TryReserve(Start.AddMinutes(31), out var wait));
        Assert.Equal(TimeSpan.Zero, wait);
    }

    [Fact]
    public void ProtocolFailureImmediatelyOpensCircuit()
    {
        var budget = CreateBudget();
        Assert.True(budget.TryReserve(Start, out _));

        budget.OpenCircuit(Start.AddMinutes(1));

        Assert.False(budget.TryReserve(Start.AddMinutes(2), out var wait));
        Assert.Equal(TimeSpan.FromMinutes(59), wait);
    }

    private static ChatObserverConnectionBudget CreateBudget() => new(new ChatObserverOptions
    {
        MaxConnectionAttemptsPerWindow = 3,
        ConnectionAttemptWindow = TimeSpan.FromMinutes(30),
        CircuitOpenDuration = TimeSpan.FromHours(1)
    });
}
