using System;

namespace MicroSnip.History;

internal sealed class HistoryStorageException : Exception
{
    public HistoryStorageException(string message)
        : base(message)
    {
    }

    public HistoryStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
