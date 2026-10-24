using System;
using System.Drawing;
using System.Threading.Tasks;

namespace MicroSnip.History;

internal sealed class HistoryLogOperation
{
    private readonly HistoryService _historyService;
    private readonly HistorySaveRequest _request;
    private readonly object _gate = new();
    private Task<HistoryEntry?>? _saveTask;
    private bool _started;
    private bool _discardRequested;
    private bool _discardStarted;

    internal HistoryLogOperation(HistoryService historyService, HistorySaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(request);

        _historyService = historyService;
        _request = request;
    }

    internal event EventHandler<HistoryStorageException>? Failed;

    internal Task Completion
    {
        get
        {
            lock (_gate)
            {
                return (Task?)_saveTask ?? Task.CompletedTask;
            }
        }
    }

    internal void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _saveTask = SaveCoreAsync();
        }
    }

    internal async Task DiscardAsync()
    {
        Task<HistoryEntry?> task;

        lock (_gate)
        {
            if (_discardStarted)
            {
                return;
            }

            _discardStarted = true;
            _discardRequested = true;
            task = _saveTask ?? Task.FromResult<HistoryEntry?>(null);
        }

        HistoryEntry? entry = await task.ConfigureAwait(false);

        if (entry is not null)
        {
            await DeleteEntryAsync().ConfigureAwait(false);
        }
    }

    private async Task<HistoryEntry?> SaveCoreAsync()
    {
        try
        {
            if (IsDiscardRequested())
            {
                return null;
            }

            HistoryEntry entry = await _historyService
                .SaveAsync(_request)
                .ConfigureAwait(false);

            if (!IsDiscardRequested())
            {
                return entry;
            }

            await DeleteEntryAsync().ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HistoryStorageException ex)
        {
            RaiseFailed(ex);
            return null;
        }
        catch (Exception ex)
        {
            RaiseFailed(new HistoryStorageException(
                "MicroSnip could not save this snip to history.",
                ex));
            return null;
        }
        finally
        {
            Bitmap? image = _request.Image;
            image?.Dispose();
        }
    }

    private async Task DeleteEntryAsync()
    {
        try
        {
            HistoryDeleteResult result = await _historyService
                .DeleteAsync(new[] { _request.Id })
                .ConfigureAwait(false);

            if (!result.Success)
            {
                string message = result.Failures.Count > 0
                    ? result.Failures[0].Message
                    : "The history entry could not be deleted.";
                RaiseFailed(new HistoryStorageException(message));
            }
        }
        catch (HistoryStorageException ex)
        {
            RaiseFailed(ex);
        }
        catch (Exception ex)
        {
            RaiseFailed(new HistoryStorageException(
                "MicroSnip could not remove this snip from history.",
                ex));
        }
    }

    private bool IsDiscardRequested()
    {
        lock (_gate)
        {
            return _discardRequested;
        }
    }

    private void RaiseFailed(HistoryStorageException exception)
    {
        try
        {
            Failed?.Invoke(this, exception);
        }
        catch (Exception)
        {
        }
    }
}
