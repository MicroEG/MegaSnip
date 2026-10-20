using System;
using MegaSnip.History;
using Xunit;

namespace MegaSnip.Tests.History;

public sealed class HistoryPauseStateTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewState_IsNotPaused()
    {
        var pause = new HistoryPauseState();

        Assert.False(pause.IsPaused(Start));
        Assert.Null(pause.PausedUntil);
    }

    [Fact]
    public void Pause_BlocksForOneHour()
    {
        var pause = new HistoryPauseState();

        pause.Pause(Start);

        Assert.True(pause.IsPaused(Start));
        Assert.True(pause.IsPaused(Start.AddMinutes(59)));
        Assert.True(pause.IsPaused(Start.AddHours(1).AddTicks(-1)));
        Assert.False(pause.IsPaused(Start.AddHours(1)));
        Assert.Equal(Start.AddHours(1), pause.PausedUntil);
    }

    [Fact]
    public void Pause_AgainAfterExpiry_RestartsTheHour()
    {
        var pause = new HistoryPauseState();
        DateTimeOffset later = Start.AddHours(2);

        pause.Pause(Start);
        pause.Pause(later);

        Assert.True(pause.IsPaused(later.AddMinutes(30)));
        Assert.False(pause.IsPaused(later.AddHours(1)));
    }

    [Fact]
    public void Resume_ClearsPause()
    {
        var pause = new HistoryPauseState();
        pause.Pause(Start);

        pause.Resume();

        Assert.False(pause.IsPaused(Start));
        Assert.Null(pause.PausedUntil);
    }
}
