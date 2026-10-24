using System;
using System.IO;
using MicroSnip.History;
using Xunit;

namespace MicroSnip.Tests.History;

public sealed class HistoryDirectoryMigratorTests : IDisposable
{
    private readonly string _root;

    public HistoryDirectoryMigratorTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "MicroSnip.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Copy_CopiesEveryFileAndKeepsTheSource()
    {
        string source = Path.Combine(_root, "old-history");
        string target = Path.Combine(_root, "new-history");
        Directory.CreateDirectory(Path.Combine(source, "20261021-120000-abcd1234"));
        File.WriteAllText(Path.Combine(source, "history.db"), "database");
        File.WriteAllText(
            Path.Combine(source, "20261021-120000-abcd1234", "meta.json"),
            "{\"id\":\"20261021-120000-abcd1234\"}");
        File.WriteAllBytes(
            Path.Combine(source, "20261021-120000-abcd1234", "image.png"),
            [1, 2, 3, 4, 5]);

        HistoryMigrationResult result = HistoryDirectoryMigrator.Copy(source, target);

        Assert.Equal(source, result.SourceDirectory);
        Assert.Equal(target, result.TargetDirectory);
        Assert.Equal(3, result.CopiedFileCount);
        Assert.Equal(
            new FileInfo(Path.Combine(source, "history.db")).Length +
            new FileInfo(Path.Combine(source, "20261021-120000-abcd1234", "meta.json")).Length +
            5,
            result.CopiedByteCount);
        Assert.True(File.Exists(Path.Combine(source, "history.db")));
        Assert.Equal(
            "database",
            File.ReadAllText(Path.Combine(target, "history.db")));
        Assert.Equal(
            new byte[] { 1, 2, 3, 4, 5 },
            File.ReadAllBytes(
                Path.Combine(target, "20261021-120000-abcd1234", "image.png")));
    }

    [Fact]
    public void Copy_AllowsAnExistingEmptyTarget()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "history.db"), "database");

        HistoryDirectoryMigrator.Copy(source, target);

        Assert.True(File.Exists(Path.Combine(target, "history.db")));
    }

    [Fact]
    public void Copy_RejectsANonEmptyTarget()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "history.db"), "database");
        File.WriteAllText(Path.Combine(target, "notes.txt"), "keep me");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => HistoryDirectoryMigrator.Copy(source, target));

        Assert.Contains("empty folder", exception.Message);
        Assert.True(File.Exists(Path.Combine(target, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(target, "history.db")));
    }

    [Fact]
    public void Copy_RejectsTheSameFolder()
    {
        string source = Path.Combine(_root, "history");
        Directory.CreateDirectory(source);

        Assert.Throws<InvalidOperationException>(
            () => HistoryDirectoryMigrator.Copy(source, source + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Copy_RejectsATargetInsideTheSource()
    {
        string source = Path.Combine(_root, "history");
        string target = Path.Combine(source, "nested");
        Directory.CreateDirectory(source);

        Assert.Throws<InvalidOperationException>(
            () => HistoryDirectoryMigrator.Copy(source, target));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Copy_RejectsAFilePathAsTarget()
    {
        string source = Path.Combine(_root, "history");
        string target = Path.Combine(_root, "file.txt");
        Directory.CreateDirectory(source);
        File.WriteAllText(target, "not a folder");

        Assert.Throws<IOException>(
            () => HistoryDirectoryMigrator.Copy(source, target));
    }

    [Fact]
    public void Copy_WithMissingSource_CreatesAnEmptyTarget()
    {
        string source = Path.Combine(_root, "missing");
        string target = Path.Combine(_root, "created");

        HistoryMigrationResult result =
            HistoryDirectoryMigrator.Copy(source, target);

        Assert.Equal(0, result.CopiedFileCount);
        Assert.Equal(0, result.CopiedByteCount);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void TryDeleteSource_RemovesTheFolderAndReportsSuccess()
    {
        string source = Path.Combine(_root, "history");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "history.db"), "database");

        bool removed = HistoryDirectoryMigrator.TryDeleteSource(source, out string? warning);

        Assert.True(removed);
        Assert.Null(warning);
        Assert.False(Directory.Exists(source));
    }

    [Fact]
    public void TryDeleteSource_WithMissingFolder_ReportsSuccess()
    {
        bool removed = HistoryDirectoryMigrator.TryDeleteSource(
            Path.Combine(_root, "missing"),
            out string? warning);

        Assert.True(removed);
        Assert.Null(warning);
    }

    [Fact]
    public void DeleteCopiedTarget_LeavesTheFolderEmpty()
    {
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "entry"));
        File.WriteAllText(Path.Combine(target, "history.db"), "database");

        HistoryDirectoryMigrator.DeleteCopiedTarget(target);

        Assert.True(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }
}
