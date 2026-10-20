using System.Collections.Generic;

namespace MegaSnip.History;

internal sealed class HistoryDeleteResult
{
    internal HistoryDeleteResult(
        IReadOnlyList<string> deletedIds,
        IReadOnlyList<HistoryDeleteFailure> failures)
    {
        DeletedIds = deletedIds;
        Failures = failures;
    }

    public IReadOnlyList<string> DeletedIds { get; }

    public IReadOnlyList<HistoryDeleteFailure> Failures { get; }

    public bool Success => Failures.Count == 0;

    internal static HistoryDeleteResult Empty { get; } =
        new([], []);
}

internal sealed record HistoryDeleteFailure(string Id, string Message);
