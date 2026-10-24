using System.Collections.Generic;
using MicroSnip.Settings;

namespace MicroSnip.History;

internal interface IHistoryRepository
{
    string HistoryDirectory { get; }

    string DatabasePath { get; }

    void Initialize();

    HistoryEntry Save(HistorySaveRequest request);

    HistoryEntry? Get(string id);

    IReadOnlyList<HistoryEntry> LoadAll();

    IReadOnlyList<HistoryEntry> Search(string term);

    bool UpdateText(string id, string text);

    HistoryDeleteResult Delete(IEnumerable<string> ids);

    HistoryDeleteResult DeleteAll();

    int ReconcileOrphans();

    HistoryDeleteResult DeleteOrphans();

    HistoryRebuildResult RebuildIndex();

    HistoryDeleteResult Prune(HistoryRetentionSettings retention, string? protectedEntryId);

    long GetStorageSizeBytes();
}
