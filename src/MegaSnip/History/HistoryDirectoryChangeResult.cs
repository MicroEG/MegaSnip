namespace MegaSnip.History;

internal sealed record HistoryDirectoryChangeResult(
    string Directory,
    string? Error,
    string? Warning)
{
    internal static HistoryDirectoryChangeResult Success(
        string directory,
        string? warning = null) =>
        new(directory, null, warning);

    internal static HistoryDirectoryChangeResult Failed(
        string directory,
        string error) =>
        new(directory, error, null);
}
