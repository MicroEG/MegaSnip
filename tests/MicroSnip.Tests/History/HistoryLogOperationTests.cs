using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MicroSnip.History;
using MicroSnip.Settings;
using Xunit;

namespace MicroSnip.Tests.History;

public sealed class HistoryLogOperationTests
{
    [Fact]
    public async Task DiscardAfterSave_DeletesEntry()
    {
        var repository = new FakeHistoryRepository();
        using var service = new HistoryService(repository);
        var operation = new HistoryLogOperation(service, CreateRequest());

        operation.Start();
        await operation.Completion;

        Assert.Single(repository.Entries);

        await operation.DiscardAsync();

        Assert.Empty(repository.Entries);
    }

    [Fact]
    public async Task DiscardWhileSaveIsPending_LeavesNoEntry()
    {
        var repository = new FakeHistoryRepository();
        var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.SaveGate = saveGate;
        using var service = new HistoryService(repository);
        var operation = new HistoryLogOperation(service, CreateRequest());

        operation.Start();
        Task discard = operation.DiscardAsync();
        saveGate.SetResult();
        await discard;
        await operation.Completion;

        Assert.Empty(repository.Entries);
    }

    [Fact]
    public async Task DiscardBeforeStart_DoesNotSave()
    {
        var repository = new FakeHistoryRepository();
        using var service = new HistoryService(repository);
        var operation = new HistoryLogOperation(service, CreateRequest());

        await operation.DiscardAsync();
        operation.Start();
        await operation.Completion;

        Assert.Empty(repository.Entries);
    }

    [Fact]
    public async Task SaveFailure_RaisesFailureAndCompletesQuietly()
    {
        var repository = new FakeHistoryRepository { FailSaves = true };
        using var service = new HistoryService(repository);
        var operation = new HistoryLogOperation(service, CreateRequest());
        var failure = new TaskCompletionSource<HistoryStorageException>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Failed += (_, exception) => failure.TrySetResult(exception);

        operation.Start();
        await operation.Completion;

        HistoryStorageException reported = await failure.Task;
        Assert.Equal("The disk is full.", reported.Message);
        Assert.Empty(repository.Entries);
    }

    [Fact]
    public async Task DiscardFailure_RaisesFailureAndKeepsEntry()
    {
        var repository = new FakeHistoryRepository();
        using var service = new HistoryService(repository);
        var operation = new HistoryLogOperation(service, CreateRequest());
        var failure = new TaskCompletionSource<HistoryStorageException>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Failed += (_, exception) => failure.TrySetResult(exception);

        operation.Start();
        await operation.Completion;
        Assert.Single(repository.Entries);

        repository.FailDeletes = true;
        await operation.DiscardAsync();

        HistoryStorageException reported = await failure.Task;
        Assert.Equal("The entry is locked.", reported.Message);
        Assert.Single(repository.Entries);
    }

    private static HistorySaveRequest CreateRequest() =>
        new()
        {
            Id = "20261020-120000-deadbeef",
            CreatedAtUtc = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
            Text = "hello",
            Width = 10,
            Height = 10,
            MonitorId = @"\\.\DISPLAY1",
            OcrLanguage = "en-US",
            AppVersion = "0.1.0",
            SaveImage = false,
        };

    private sealed class FakeHistoryRepository : IHistoryRepository
    {
        internal Dictionary<string, HistoryEntry> Entries { get; } =
            new(StringComparer.Ordinal);

        internal TaskCompletionSource? SaveGate { get; set; }

        internal bool FailSaves { get; set; }

        internal bool FailDeletes { get; set; }

        public string HistoryDirectory => @"C:\MicroSnip.Tests\fake-history";

        public string DatabasePath => HistoryDirectory + @"\history.db";

        public void Initialize()
        {
        }

        public HistoryEntry Save(HistorySaveRequest request)
        {
            SaveGate?.Task.GetAwaiter().GetResult();

            if (FailSaves)
            {
                throw new HistoryStorageException("The disk is full.");
            }

            var entry = new HistoryEntry
            {
                Id = request.Id,
                CreatedAtUtc = request.CreatedAtUtc,
                Text = request.Text,
                Width = request.Width,
                Height = request.Height,
                MonitorId = request.MonitorId,
                OcrLanguage = request.OcrLanguage,
                CharCount = request.Text.Length,
                AppVersion = request.AppVersion,
                HasImage = request.SaveImage,
                IsOrphaned = false,
                EntryDirectory = HistoryDirectory + @"\" + request.Id,
            };

            Entries[request.Id] = entry;
            return entry;
        }

        public HistoryEntry? Get(string id) => Entries.GetValueOrDefault(id);

        public IReadOnlyList<HistoryEntry> LoadAll() =>
            Entries.Values
                .OrderByDescending(entry => entry.CreatedAtUtc)
                .ToList();

        public IReadOnlyList<HistoryEntry> Search(string term) =>
            LoadAll()
                .Where(entry => entry.Text.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();

        public bool UpdateText(string id, string text) => false;

        public HistoryDeleteResult Delete(IEnumerable<string> ids)
        {
            if (FailDeletes)
            {
                throw new HistoryStorageException("The entry is locked.");
            }

            var deleted = new List<string>();
            var failures = new List<HistoryDeleteFailure>();

            foreach (string id in ids)
            {
                if (Entries.Remove(id))
                {
                    deleted.Add(id);
                }
                else
                {
                    failures.Add(new HistoryDeleteFailure(id, "The history entry was not found."));
                }
            }

            return new HistoryDeleteResult(deleted, failures);
        }

        public HistoryDeleteResult DeleteAll() => Delete(Entries.Keys.ToArray());

        public int ReconcileOrphans() => 0;

        public HistoryDeleteResult DeleteOrphans() => HistoryDeleteResult.Empty;

        public HistoryRebuildResult RebuildIndex() => new(0, 0);

        public HistoryDeleteResult Prune(
            HistoryRetentionSettings retention,
            string? protectedEntryId) => HistoryDeleteResult.Empty;

        public long GetStorageSizeBytes() => 0;
    }
}
