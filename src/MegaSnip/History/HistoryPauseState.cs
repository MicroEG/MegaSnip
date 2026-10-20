using System;

namespace MegaSnip.History;

internal sealed class HistoryPauseState
{
    private static readonly TimeSpan PauseDuration = TimeSpan.FromHours(1);

    private DateTimeOffset? _pausedUntil;

    internal DateTimeOffset? PausedUntil => _pausedUntil;

    internal bool IsPaused(DateTimeOffset now) =>
        _pausedUntil is { } pausedUntil && now < pausedUntil;

    internal void Pause(DateTimeOffset now) =>
        _pausedUntil = now + PauseDuration;

    internal void Resume() =>
        _pausedUntil = null;
}
