using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using MicroSnip.History;
using MicroSnip.Settings;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MicroSnip.Tests.History;

public sealed class HistoryRepositoryTests : IDisposable
{
    private readonly string _root;
    private readonly string _historyDirectory;
    private readonly List<Bitmap> _bitmaps = [];

    public HistoryRepositoryTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "MicroSnip.Tests",
            Guid.NewGuid().ToString("N"));
        _historyDirectory = Path.Combine(_root, "history");
    }

    public void Dispose()
    {
        foreach (Bitmap bitmap in _bitmaps)
        {
            bitmap.Dispose();
        }

        SqliteConnection.ClearAllPools();

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
    public void SaveThenLoad_RoundTripsTextImageAndMetadata()
    {
        HistoryRepository repository = CreateRepository();
        var createdAt = new DateTimeOffset(2026, 10, 20, 9, 30, 15, TimeSpan.Zero);
        Bitmap image = CreateBitmap(320, 200, Color.CadetBlue);
        const string text = "Hello, Wörld!\r\nEmoji: 🎉";

        HistoryEntry saved = repository.Save(CreateRequest(text, createdAt, image));

        Assert.True(HistoryEntryId.IsValid(saved.Id));
        Assert.StartsWith("20261020-093015-", saved.Id);
        Assert.Equal(createdAt, saved.CreatedAtUtc);
        Assert.Equal(text, saved.Text);
        Assert.Equal(text.Length, saved.CharCount);
        Assert.Equal(320, saved.Width);
        Assert.Equal(200, saved.Height);
        Assert.Equal(@"\\.\DISPLAY1", saved.MonitorId);
        Assert.Equal("en-US", saved.OcrLanguage);
        Assert.Equal("0.1.0-test", saved.AppVersion);
        Assert.True(saved.HasImage);
        Assert.False(saved.IsOrphaned);
        Assert.True(File.Exists(repository.DatabasePath));
        Assert.True(File.Exists(saved.ImagePath));
        Assert.True(File.Exists(saved.ThumbnailPath));
        Assert.True(File.Exists(saved.MetadataPath));

        byte[] imageBytes = File.ReadAllBytes(saved.ImagePath!);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, imageBytes[..8]);

        using (var imageStream = new MemoryStream(imageBytes))
        using (var storedImage = new Bitmap(imageStream))
        {
            Assert.Equal(320, storedImage.Width);
            Assert.Equal(200, storedImage.Height);
        }

        using (var thumbnailStream = new MemoryStream(File.ReadAllBytes(saved.ThumbnailPath!)))
        using (var thumbnail = new Bitmap(thumbnailStream))
        {
            Assert.Equal(160, thumbnail.Width);
            Assert.Equal(100, thumbnail.Height);
            Assert.True(thumbnail.Width <= ThumbnailGenerator.MaxDimension);
            Assert.True(thumbnail.Height <= ThumbnailGenerator.MaxDimension);
        }

        using (JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(saved.MetadataPath)))
        {
            JsonElement root = metadata.RootElement;
            Assert.Equal(saved.Id, root.GetProperty("id").GetString());
            Assert.Equal(text, root.GetProperty("text").GetString());
            Assert.Equal(320, root.GetProperty("width").GetInt32());
            Assert.Equal(200, root.GetProperty("height").GetInt32());
            Assert.Equal(text.Length, root.GetProperty("charCount").GetInt32());
            Assert.True(root.GetProperty("hasImage").GetBoolean());
        }

        HistoryEntry loaded = Assert.IsType<HistoryEntry>(repository.Get(saved.Id));
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(saved.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(saved.Text, loaded.Text);
        Assert.Equal(saved.CharCount, loaded.CharCount);
        Assert.Equal(saved.Width, loaded.Width);
        Assert.Equal(saved.Height, loaded.Height);
        Assert.Equal(saved.MonitorId, loaded.MonitorId);
        Assert.Equal(saved.OcrLanguage, loaded.OcrLanguage);
        Assert.Equal(saved.AppVersion, loaded.AppVersion);
        Assert.Equal(saved.HasImage, loaded.HasImage);
        Assert.False(loaded.IsOrphaned);
        Assert.Equal(saved.EntryDirectory, loaded.EntryDirectory);

        HistoryEntry listed = Assert.Single(repository.LoadAll());
        Assert.Equal(saved.Id, listed.Id);
        Assert.Equal(imageBytes, File.ReadAllBytes(saved.ImagePath!));
        Assert.True(repository.GetStorageSizeBytes() > imageBytes.Length);
    }

    [Fact]
    public void LoadAll_ReturnsNewestFirst()
    {
        HistoryRepository repository = CreateRepository();
        var baseline = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        HistoryEntry oldest = repository.Save(CreateRequest("oldest", baseline.AddMinutes(-2)));
        HistoryEntry newest = repository.Save(CreateRequest("newest", baseline));
        HistoryEntry middle = repository.Save(CreateRequest("middle", baseline.AddMinutes(-1)));

        IReadOnlyList<HistoryEntry> entries = repository.LoadAll();

        Assert.Equal(
            new[] { newest.Id, middle.Id, oldest.Id },
            entries.Select(entry => entry.Id));
    }

    [Fact]
    public void Save_WithoutImage_StoresTextOnlyEntry()
    {
        HistoryRepository repository = CreateRepository();

        HistoryEntry entry = repository.Save(CreateRequest(
            "plain text",
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
            image: null,
            saveImage: false));

        Assert.False(entry.HasImage);
        Assert.Null(entry.ImagePath);
        Assert.Null(entry.ThumbnailPath);
        Assert.False(File.Exists(Path.Combine(entry.EntryDirectory, HistoryFileNames.Image)));
        Assert.False(File.Exists(Path.Combine(entry.EntryDirectory, HistoryFileNames.Thumbnail)));
        Assert.True(File.Exists(entry.MetadataPath));
        Assert.True(repository.GetStorageSizeBytes() > 0);
        Assert.Equal(0, repository.ReconcileOrphans());

        HistoryEntry reloaded = Assert.IsType<HistoryEntry>(repository.Get(entry.Id));
        Assert.False(reloaded.IsOrphaned);
        Assert.False(reloaded.HasImage);
    }

    [Fact]
    public void Save_RejectsDuplicateAndInvalidIds()
    {
        HistoryRepository repository = CreateRepository();
        var createdAt = new DateTimeOffset(2026, 10, 20, 11, 0, 0, TimeSpan.Zero);
        HistorySaveRequest request = CreateRequest("first", createdAt);
        repository.Save(request);

        Assert.Throws<HistoryStorageException>(
            () => repository.Save(CreateRequest(
                "duplicate",
                createdAt.AddSeconds(1),
                id: request.Id)));
        Assert.Throws<ArgumentException>(
            () => repository.Save(CreateRequest(
                "escape attempt",
                createdAt.AddSeconds(2),
                id: "../escape")));
        Assert.Null(repository.Get("../escape"));
        Assert.Single(repository.LoadAll());
    }

    [Fact]
    public void UpdateText_PersistsTextCharacterCountAndMetadata()
    {
        HistoryRepository repository = CreateRepository();
        HistoryEntry entry = repository.Save(CreateRequest(
            "first draft",
            new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero)));
        const string updatedText = "Second draft — Привет";

        Assert.True(repository.UpdateText(entry.Id, updatedText));
        Assert.False(repository.UpdateText("missing-id", "ignored"));

        HistoryEntry updated = Assert.IsType<HistoryEntry>(repository.Get(entry.Id));
        Assert.Equal(updatedText, updated.Text);
        Assert.Equal(updatedText.Length, updated.CharCount);
        Assert.Empty(repository.Search("first draft"));
        Assert.Single(repository.Search("second"));
        Assert.Single(repository.Search("привет"));

        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(entry.MetadataPath));
        Assert.Equal(updatedText, metadata.RootElement.GetProperty("text").GetString());
        Assert.Equal(updatedText.Length, metadata.RootElement.GetProperty("charCount").GetInt32());
    }

    [Fact]
    public void Delete_RemovesRowsAndFoldersAndReportsMissingEntries()
    {
        HistoryRepository repository = CreateRepository();
        var baseline = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        HistoryEntry first = repository.Save(
            CreateRequest("first", baseline, CreateBitmap(100, 100)));
        HistoryEntry second = repository.Save(
            CreateRequest("second", baseline.AddMinutes(1)));
        HistoryEntry third = repository.Save(
            CreateRequest("third", baseline.AddMinutes(2)));

        HistoryDeleteResult single = repository.Delete(new[] { first.Id });

        Assert.True(single.Success);
        Assert.Equal(new[] { first.Id }, single.DeletedIds);
        Assert.Empty(single.Failures);
        Assert.Null(repository.Get(first.Id));
        Assert.False(Directory.Exists(first.EntryDirectory));

        HistoryDeleteResult bulk = repository.Delete(new[] { second.Id, third.Id });

        Assert.True(bulk.Success);
        Assert.Equal(2, bulk.DeletedIds.Count);
        Assert.Empty(repository.LoadAll());

        HistoryDeleteResult missing = repository.Delete(new[] { second.Id });

        Assert.False(missing.Success);
        HistoryDeleteFailure failure = Assert.Single(missing.Failures);
        Assert.Equal(second.Id, failure.Id);
    }

    [Fact]
    public void DeleteAll_RemovesEveryEntryFolder()
    {
        HistoryRepository repository = CreateRepository();
        var baseline = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        HistoryEntry first = repository.Save(
            CreateRequest("first", baseline, CreateBitmap(90, 60)));
        HistoryEntry second = repository.Save(
            CreateRequest("second", baseline.AddMinutes(1)));

        HistoryDeleteResult result = repository.DeleteAll();

        Assert.True(result.Success);
        Assert.Equal(2, result.DeletedIds.Count);
        Assert.Empty(repository.LoadAll());
        Assert.False(Directory.Exists(first.EntryDirectory));
        Assert.False(Directory.Exists(second.EntryDirectory));
        Assert.True(Directory.Exists(_historyDirectory));
    }

    [Fact]
    public void Delete_WhenFileIsLocked_KeepsRowAndFolder()
    {
        HistoryRepository repository = CreateRepository();
        HistoryEntry entry = repository.Save(CreateRequest(
            "locked",
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
            CreateBitmap(80, 60)));

        using (var stream = new FileStream(
            entry.ImagePath!,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            HistoryDeleteResult result = repository.Delete(new[] { entry.Id });

            Assert.False(result.Success);
            Assert.Single(result.Failures);
            Assert.NotNull(repository.Get(entry.Id));
            Assert.True(Directory.Exists(entry.EntryDirectory));
            Assert.True(File.Exists(entry.ImagePath));
        }

        HistoryDeleteResult retry = repository.Delete(new[] { entry.Id });

        Assert.True(retry.Success);
        Assert.Null(repository.Get(entry.Id));
        Assert.False(Directory.Exists(entry.EntryDirectory));
    }

    [Fact]
    public void ReconcileOrphans_DetectsMissingFilesAndDeleteOrphansRemovesThem()
    {
        HistoryRepository repository = CreateRepository();
        var baseline = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        HistoryEntry missingImage = repository.Save(
            CreateRequest("missing image", baseline, CreateBitmap(100, 80)));
        HistoryEntry missingMetadata = repository.Save(
            CreateRequest("missing metadata", baseline.AddMinutes(1)));
        HistoryEntry healthy = repository.Save(
            CreateRequest("healthy", baseline.AddMinutes(2), CreateBitmap(90, 70)));

        File.Delete(missingImage.ImagePath!);
        File.Delete(missingMetadata.MetadataPath);

        Assert.Equal(2, repository.ReconcileOrphans());
        Assert.True(Assert.IsType<HistoryEntry>(repository.Get(missingImage.Id)).IsOrphaned);
        Assert.True(Assert.IsType<HistoryEntry>(repository.Get(missingMetadata.Id)).IsOrphaned);
        Assert.False(Assert.IsType<HistoryEntry>(repository.Get(healthy.Id)).IsOrphaned);
        Assert.Equal(2, repository.ReconcileOrphans());

        HistoryDeleteResult result = repository.DeleteOrphans();

        Assert.True(result.Success);
        Assert.Equal(2, result.DeletedIds.Count);
        Assert.Contains(missingImage.Id, result.DeletedIds);
        Assert.Contains(missingMetadata.Id, result.DeletedIds);
        Assert.Null(repository.Get(missingImage.Id));
        Assert.Null(repository.Get(missingMetadata.Id));
        HistoryEntry remaining = Assert.Single(repository.LoadAll());
        Assert.Equal(healthy.Id, remaining.Id);
        Assert.Equal(0, repository.ReconcileOrphans());
    }

    [Fact]
    public void Search_IsCaseInsensitiveAndTreatsWildcardsLiterally()
    {
        HistoryRepository repository = CreateRepository();
        var baseline = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        repository.Save(CreateRequest("Alpha Beta", baseline));
        repository.Save(CreateRequest("gamma delta", baseline.AddMinutes(1)));
        repository.Save(CreateRequest("Привет Мир", baseline.AddMinutes(2)));
        repository.Save(CreateRequest("привет земля", baseline.AddMinutes(3)));
        repository.Save(CreateRequest("Emoji 😀 Rocket 🚀", baseline.AddMinutes(4)));
        repository.Save(CreateRequest("100% done_ok", baseline.AddMinutes(5)));
        repository.Save(CreateRequest(@"back\slash", baseline.AddMinutes(6)));

        Assert.Single(repository.Search("alpha"));
        Assert.Single(repository.Search("ALPHA"));
        Assert.Equal(2, repository.Search("привет").Count);
        Assert.Single(repository.Search("😀"));
        Assert.Single(repository.Search("%"));
        Assert.Single(repository.Search("_"));
        Assert.Single(repository.Search("\\"));
        Assert.Equal(7, repository.Search("   ").Count);
        Assert.Empty(repository.Search("absent"));
    }

    [Fact]
    public void Prune_EnforcesCountAndProtectsEntry()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        HistoryRepository repository = CreateRepository(() => now);
        HistoryEntry oldest = repository.Save(CreateRequest("one", now.AddHours(-4)));
        HistoryEntry secondOldest = repository.Save(CreateRequest("two", now.AddHours(-3)));
        HistoryEntry thirdOldest = repository.Save(CreateRequest("three", now.AddHours(-2)));
        HistoryEntry newest = repository.Save(CreateRequest("four", now.AddHours(-1)));
        var retention = new HistoryRetentionSettings
        {
            AutoCleanupEnabled = true,
            MaxEntries = 2,
            MaxAgeDays = 0,
            MaxDiskMB = 0,
        };

        HistoryDeleteResult result = repository.Prune(retention, oldest.Id);

        Assert.True(result.Success);
        Assert.Equal(2, result.DeletedIds.Count);
        Assert.Contains(secondOldest.Id, result.DeletedIds);
        Assert.Contains(thirdOldest.Id, result.DeletedIds);
        Assert.Equal(
            new[] { newest.Id, oldest.Id },
            repository.LoadAll().Select(entry => entry.Id));
    }

    [Fact]
    public void Prune_EnforcesAge()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        HistoryRepository repository = CreateRepository(() => now);
        HistoryEntry expired = repository.Save(CreateRequest("expired", now.AddDays(-40)));
        HistoryEntry justExpired = repository.Save(CreateRequest("just expired", now.AddDays(-31)));
        HistoryEntry recent = repository.Save(CreateRequest("recent", now.AddDays(-29)));
        HistoryEntry newest = repository.Save(CreateRequest("newest", now.AddDays(-1)));
        var retention = new HistoryRetentionSettings
        {
            AutoCleanupEnabled = true,
            MaxEntries = 0,
            MaxAgeDays = 30,
            MaxDiskMB = 0,
        };

        HistoryDeleteResult result = repository.Prune(retention, protectedEntryId: null);

        Assert.True(result.Success);
        Assert.Equal(2, result.DeletedIds.Count);
        Assert.Contains(expired.Id, result.DeletedIds);
        Assert.Contains(justExpired.Id, result.DeletedIds);
        Assert.Equal(
            new[] { newest.Id, recent.Id },
            repository.LoadAll().Select(entry => entry.Id));
    }

    [Fact]
    public void Prune_EnforcesDiskLimitAndKeepsNewestEntries()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        HistoryRepository repository = CreateRepository(() => now);
        var entries = new List<HistoryEntry>();

        for (int index = 0; index < 4; index++)
        {
            entries.Add(repository.Save(CreateRequest(
                "noise " + index,
                now.AddMinutes(index),
                CreateNoiseBitmap(400, 400))));
        }

        var retention = new HistoryRetentionSettings
        {
            AutoCleanupEnabled = true,
            MaxEntries = 0,
            MaxAgeDays = 0,
            MaxDiskMB = 1,
        };

        HistoryDeleteResult result = repository.Prune(retention, protectedEntryId: null);

        Assert.True(result.Success);
        Assert.NotEmpty(result.DeletedIds);
        Assert.True(repository.GetStorageSizeBytes() <= 1024L * 1024L);

        IReadOnlyList<HistoryEntry> remaining = repository.LoadAll();
        Assert.Equal(
            entries.TakeLast(remaining.Count).Reverse().Select(entry => entry.Id),
            remaining.Select(entry => entry.Id));
    }

    [Fact]
    public void Prune_WhenDisabledOrAllLimitsZero_DoesNothing()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        HistoryRepository repository = CreateRepository(() => now);
        repository.Save(CreateRequest("one", now.AddDays(-400)));
        repository.Save(CreateRequest("two", now.AddDays(-200)));
        repository.Save(CreateRequest("three", now));

        HistoryDeleteResult disabled = repository.Prune(
            new HistoryRetentionSettings { AutoCleanupEnabled = false, MaxEntries = 1 },
            protectedEntryId: null);
        HistoryDeleteResult zeroLimits = repository.Prune(
            new HistoryRetentionSettings
            {
                AutoCleanupEnabled = true,
                MaxEntries = 0,
                MaxAgeDays = 0,
                MaxDiskMB = 0,
            },
            protectedEntryId: null);

        Assert.Empty(disabled.DeletedIds);
        Assert.Empty(zeroLimits.DeletedIds);
        Assert.Equal(3, repository.LoadAll().Count);
    }

    [Fact]
    public void RebuildIndex_WhenDatabaseIsCorrupt_ReimportsEntryFolders()
    {
        HistoryRepository repository = CreateRepository();
        var baseline = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        HistoryEntry withImage = repository.Save(
            CreateRequest("image entry", baseline, CreateBitmap(120, 90)));
        HistoryEntry textOnly = repository.Save(
            CreateRequest("text entry", baseline.AddMinutes(1), image: null, saveImage: false));
        string skippedId = "20260101-000000-deadbeef";
        Directory.CreateDirectory(Path.Combine(_historyDirectory, skippedId));

        SqliteConnection.ClearAllPools();
        File.Delete(repository.DatabasePath + "-wal");
        File.Delete(repository.DatabasePath + "-shm");
        File.WriteAllText(repository.DatabasePath, "this is not a sqlite database");

        var reopened = CreateRepository();
        Assert.Throws<HistoryStorageException>(() => reopened.Initialize());

        HistoryRebuildResult result = reopened.RebuildIndex();

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.SkippedFolderCount);
        IReadOnlyList<HistoryEntry> entries = reopened.LoadAll();
        Assert.Equal(2, entries.Count);
        HistoryEntry imageEntry = Assert.Single(
            entries,
            entry => entry.Id == withImage.Id);
        Assert.Equal("image entry", imageEntry.Text);
        Assert.True(imageEntry.HasImage);
        Assert.False(imageEntry.IsOrphaned);
        HistoryEntry textEntry = Assert.Single(
            entries,
            entry => entry.Id == textOnly.Id);
        Assert.Equal("text entry", textEntry.Text);
        Assert.False(textEntry.HasImage);
        Assert.NotEmpty(Directory.GetFiles(_historyDirectory, "history.db.corrupt-*"));
    }

    [Fact]
    public void Save_ThumbnailNeverUpscalesSmallImages()
    {
        HistoryRepository repository = CreateRepository();
        HistoryEntry entry = repository.Save(CreateRequest(
            "small",
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
            CreateBitmap(64, 48)));

        using var stream = new MemoryStream(File.ReadAllBytes(entry.ThumbnailPath!));
        using var thumbnail = new Bitmap(stream);

        Assert.Equal(64, thumbnail.Width);
        Assert.Equal(48, thumbnail.Height);
    }

    private HistoryRepository CreateRepository(Func<DateTimeOffset>? utcNow = null) =>
        new(_historyDirectory, utcNow);

    private static HistorySaveRequest CreateRequest(
        string text,
        DateTimeOffset createdAt,
        Bitmap? image = null,
        bool saveImage = true,
        string? id = null) =>
        new()
        {
            Id = id ?? HistoryEntryId.Create(createdAt),
            CreatedAtUtc = createdAt,
            Text = text,
            Width = image?.Width ?? 100,
            Height = image?.Height ?? 50,
            MonitorId = @"\\.\DISPLAY1",
            OcrLanguage = "en-US",
            AppVersion = "0.1.0-test",
            Image = image,
            SaveImage = saveImage && image is not null,
        };

    private Bitmap CreateBitmap(int width, int height, Color? color = null)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color ?? Color.CadetBlue);
        }

        _bitmaps.Add(bitmap);
        return bitmap;
    }

    private Bitmap CreateNoiseBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            byte[] buffer = new byte[data.Stride * height];
            Random.Shared.NextBytes(buffer);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        _bitmaps.Add(bitmap);
        return bitmap;
    }
}
