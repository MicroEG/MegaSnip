using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MegaSnip.Settings;
using Microsoft.Data.Sqlite;

namespace MegaSnip.History;

internal sealed class HistoryService : IDisposable
{
    private readonly IHistoryRepository _repository;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private bool _disposed;

    internal HistoryService(IHistoryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public event EventHandler? Changed;

    public string HistoryDirectory => _repository.HistoryDirectory;

    public string DatabasePath => _repository.DatabasePath;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunMutationAsync(
            () =>
            {
                _repository.Initialize();
                _repository.ReconcileOrphans();
                return true;
            },
            cancellationToken);

    public Task<HistoryEntry> SaveAsync(
        HistorySaveRequest request,
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(
            () => _repository.Save(request),
            cancellationToken);

    public Task<HistoryEntry?> GetAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        RunReadAsync(() => _repository.Get(id), cancellationToken);

    public Task<IReadOnlyList<HistoryEntry>> LoadAllAsync(
        CancellationToken cancellationToken = default) =>
        RunReadAsync(_repository.LoadAll, cancellationToken);

    public Task<IReadOnlyList<HistoryEntry>> SearchAsync(
        string term,
        CancellationToken cancellationToken = default) =>
        RunReadAsync(() => _repository.Search(term), cancellationToken);

    public Task<bool> UpdateTextAsync(
        string id,
        string text,
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(
            () => _repository.UpdateText(id, text),
            cancellationToken);

    public Task<HistoryDeleteResult> DeleteAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(
            () => _repository.Delete(ids),
            cancellationToken);

    public Task<HistoryDeleteResult> DeleteAllAsync(
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(_repository.DeleteAll, cancellationToken);

    public Task<int> ReconcileOrphansAsync(CancellationToken cancellationToken = default) =>
        RunMutationAsync(_repository.ReconcileOrphans, cancellationToken);

    public Task<HistoryDeleteResult> DeleteOrphansAsync(
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(_repository.DeleteOrphans, cancellationToken);

    public Task<HistoryRebuildResult> RebuildIndexAsync(
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(_repository.RebuildIndex, cancellationToken);

    public Task<HistoryDeleteResult> PruneAsync(
        HistoryRetentionSettings retention,
        string? protectedEntryId,
        CancellationToken cancellationToken = default) =>
        RunMutationAsync(
            () => _repository.Prune(retention, protectedEntryId),
            cancellationToken);

    public Task<long> GetStorageSizeBytesAsync(
        CancellationToken cancellationToken = default) =>
        RunReadAsync(_repository.GetStorageSizeBytes, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _mutationGate.Dispose();
        _disposed = true;
    }

    private async Task<T> RunMutationAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            T result = await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return ExecuteStorageOperation(operation);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            Changed?.Invoke(this, EventArgs.Empty);
            return result;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<T> RunReadAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ExecuteStorageOperation(operation);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static T ExecuteStorageOperation<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or SqliteException)
        {
            throw new HistoryStorageException(
                "MegaSnip could not access its history storage.",
                ex);
        }
    }
}
