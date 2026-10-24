using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MicroSnip.Settings;
using Microsoft.Data.Sqlite;

namespace MicroSnip.History;

internal sealed class HistoryRepository : IHistoryRepository
{
    private const string SelectColumns =
        "id, created_at_utc, text, width, height, monitor_id, ocr_language, " +
        "char_count, app_version, has_image, is_orphaned";

    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _historyDirectory;
    private readonly string _databasePath;
    private readonly Func<DateTimeOffset> _utcNow;
    private bool _initialized;

    internal HistoryRepository(string historyDirectory, Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyDirectory);

        _historyDirectory = Path.GetFullPath(historyDirectory);
        _databasePath = Path.Combine(_historyDirectory, HistoryFileNames.Database);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string HistoryDirectory => _historyDirectory;

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_historyDirectory);

                using SqliteConnection connection = OpenConnection();
                EnsureSchema(connection);
                _initialized = true;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or SqliteException)
            {
                throw new HistoryStorageException(
                    "MicroSnip could not open the history database.",
                    ex);
            }
        }
    }

    public HistoryEntry Save(HistorySaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Text);

        if (!HistoryEntryId.IsValid(request.Id))
        {
            throw new ArgumentException("The history entry id is not valid.", nameof(request));
        }

        if (request.Width <= 0 || request.Height <= 0)
        {
            throw new ArgumentException("The history entry dimensions must be positive.", nameof(request));
        }

        if (request.SaveImage && request.Image is null)
        {
            throw new ArgumentException("A capture image is required when saving images.", nameof(request));
        }

        lock (_gate)
        {
            EnsureInitialized();

            string entryDirectory = GetEntryDirectory(request.Id);
            if (Directory.Exists(entryDirectory) ||
                File.Exists(entryDirectory) ||
                GetUnlocked(request.Id) is not null)
            {
                throw new HistoryStorageException("A history entry with this id already exists.");
            }

            bool hasImage = request.SaveImage;
            string stagingDirectory = Path.Combine(
                _historyDirectory,
                "." + request.Id + "-" + Guid.NewGuid().ToString("N") + ".tmp");

            Directory.CreateDirectory(stagingDirectory);

            try
            {
                if (hasImage && request.Image is not null)
                {
                    SavePng(request.Image, Path.Combine(stagingDirectory, HistoryFileNames.Image));

                    using Bitmap thumbnail = ThumbnailGenerator.Create(request.Image);
                    SavePng(thumbnail, Path.Combine(stagingDirectory, HistoryFileNames.Thumbnail));
                }

                var metadata = new HistoryMetadata
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
                    HasImage = hasImage,
                };

                WriteMetadata(stagingDirectory, metadata);
                Directory.Move(stagingDirectory, entryDirectory);
            }
            catch
            {
                TryDeleteDirectory(stagingDirectory);
                throw;
            }

            try
            {
                InsertEntry(request, hasImage);
            }
            catch
            {
                TryDeleteDirectory(entryDirectory);
                throw;
            }

            return CreateEntry(request, hasImage, isOrphaned: false, entryDirectory);
        }
    }

    public HistoryEntry? Get(string id)
    {
        if (!HistoryEntryId.IsValid(id))
        {
            return null;
        }

        lock (_gate)
        {
            EnsureInitialized();
            return GetUnlocked(id);
        }
    }

    public IReadOnlyList<HistoryEntry> LoadAll()
    {
        lock (_gate)
        {
            EnsureInitialized();
            return LoadAllUnlocked();
        }
    }

    public IReadOnlyList<HistoryEntry> Search(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        string query = term.Trim();
        if (query.Length == 0)
        {
            return LoadAll();
        }

        if (!IsAscii(query))
        {
            return LoadAll()
                .Where(entry => entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        lock (_gate)
        {
            EnsureInitialized();

            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + SelectColumns + " FROM history_entries " +
                "WHERE text LIKE @pattern ESCAPE '\\' " +
                "ORDER BY created_at_utc DESC, id DESC";
            command.Parameters.AddWithValue("@pattern", "%" + EscapeLikePattern(query) + "%");

            using SqliteDataReader reader = command.ExecuteReader();
            return ReadEntries(reader);
        }
    }

    public bool UpdateText(string id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!HistoryEntryId.IsValid(id))
        {
            return false;
        }

        lock (_gate)
        {
            EnsureInitialized();

            HistoryEntry? entry = GetUnlocked(id);
            if (entry is null)
            {
                return false;
            }

            WriteMetadata(
                entry.EntryDirectory,
                CreateMetadata(entry, text, entry.HasImage));

            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "UPDATE history_entries SET text = @text, char_count = @charCount WHERE id = @id";
            command.Parameters.AddWithValue("@text", text);
            command.Parameters.AddWithValue("@charCount", text.Length);
            command.Parameters.AddWithValue("@id", id);

            if (command.ExecuteNonQuery() == 0)
            {
                throw new HistoryStorageException("The history entry no longer exists.");
            }

            return true;
        }
    }

    public HistoryDeleteResult Delete(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        string[] distinctIds = ids
            .Where(HistoryEntryId.IsValid)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctIds.Length == 0)
        {
            return HistoryDeleteResult.Empty;
        }

        lock (_gate)
        {
            EnsureInitialized();
            return DeleteCore(distinctIds);
        }
    }

    public HistoryDeleteResult DeleteAll()
    {
        lock (_gate)
        {
            EnsureInitialized();
            string[] ids = LoadAllUnlocked().Select(entry => entry.Id).ToArray();
            return ids.Length == 0 ? HistoryDeleteResult.Empty : DeleteCore(ids);
        }
    }

    public int ReconcileOrphans()
    {
        lock (_gate)
        {
            EnsureInitialized();

            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            IReadOnlyList<HistoryEntry> entries = LoadAllUnlocked(connection, transaction);
            int orphanedCount = 0;

            foreach (HistoryEntry entry in entries)
            {
                bool orphaned = IsOrphaned(entry);
                if (orphaned)
                {
                    orphanedCount++;
                }

                if (orphaned == entry.IsOrphaned)
                {
                    continue;
                }

                using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE history_entries SET is_orphaned = @orphaned WHERE id = @id";
                update.Parameters.AddWithValue("@orphaned", orphaned ? 1 : 0);
                update.Parameters.AddWithValue("@id", entry.Id);
                update.ExecuteNonQuery();
            }

            transaction.Commit();
            return orphanedCount;
        }
    }

    public HistoryDeleteResult DeleteOrphans()
    {
        lock (_gate)
        {
            EnsureInitialized();

            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM history_entries WHERE is_orphaned = 1";

            var ids = new List<string>();
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    ids.Add(reader.GetString(0));
                }
            }

            return ids.Count == 0 ? HistoryDeleteResult.Empty : DeleteCore(ids);
        }
    }

    public HistoryRebuildResult RebuildIndex()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_historyDirectory);
            SqliteConnection.ClearAllPools();
            ArchiveDatabaseFiles();

            int imported = 0;
            int skipped = 0;

            try
            {
                using (SqliteConnection freshConnection = OpenConnection())
                {
                    EnsureSchema(freshConnection);
                }

                _initialized = true;

                foreach (string directory in Directory.EnumerateDirectories(_historyDirectory))
                {
                    string folderName = Path.GetFileName(directory);
                    if (folderName.StartsWith('.') || !HistoryEntryId.IsValid(folderName))
                    {
                        continue;
                    }

                    HistoryMetadata? metadata = ReadMetadata(directory);
                    if (metadata is null)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        ImportMetadata(folderName, directory, metadata);
                        imported++;
                    }
                    catch (Exception ex) when (
                        ex is IOException or UnauthorizedAccessException or SqliteException)
                    {
                        skipped++;
                    }
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or SqliteException)
            {
                throw new HistoryStorageException(
                    "MicroSnip could not rebuild the history index.",
                    ex);
            }

            return new HistoryRebuildResult(imported, skipped);
        }
    }

    public HistoryDeleteResult Prune(
        HistoryRetentionSettings retention,
        string? protectedEntryId)
    {
        ArgumentNullException.ThrowIfNull(retention);

        if (!retention.AutoCleanupEnabled ||
            (retention.MaxEntries <= 0 && retention.MaxAgeDays <= 0 && retention.MaxDiskMB <= 0))
        {
            return HistoryDeleteResult.Empty;
        }

        lock (_gate)
        {
            EnsureInitialized();

            IReadOnlyList<HistoryEntry> entries = LoadAllUnlocked();
            if (entries.Count == 0)
            {
                return HistoryDeleteResult.Empty;
            }

            var toDelete = new HashSet<string>(StringComparer.Ordinal);

            if (retention.MaxAgeDays > 0)
            {
                DateTimeOffset cutoff = _utcNow().AddDays(-retention.MaxAgeDays);
                foreach (HistoryEntry entry in entries)
                {
                    if (entry.Id != protectedEntryId && entry.CreatedAtUtc < cutoff)
                    {
                        toDelete.Add(entry.Id);
                    }
                }
            }

            List<HistoryEntry> remaining = entries
                .Where(entry => !toDelete.Contains(entry.Id) || entry.Id == protectedEntryId)
                .ToList();

            if (retention.MaxEntries > 0 && remaining.Count > retention.MaxEntries)
            {
                var keep = new HashSet<string>(
                    remaining.Take(retention.MaxEntries).Select(entry => entry.Id),
                    StringComparer.Ordinal);

                if (protectedEntryId is not null &&
                    remaining.Any(entry => entry.Id == protectedEntryId) &&
                    !keep.Contains(protectedEntryId))
                {
                    HistoryEntry? victim = remaining
                        .Take(retention.MaxEntries)
                        .LastOrDefault(entry => entry.Id != protectedEntryId);

                    if (victim is not null)
                    {
                        keep.Remove(victim.Id);
                    }

                    keep.Add(protectedEntryId);
                }

                foreach (HistoryEntry entry in remaining)
                {
                    if (!keep.Contains(entry.Id))
                    {
                        toDelete.Add(entry.Id);
                    }
                }
            }

            remaining = entries
                .Where(entry => !toDelete.Contains(entry.Id))
                .ToList();

            if (retention.MaxDiskMB > 0)
            {
                long limitBytes = retention.MaxDiskMB * 1024L * 1024L;
                var sizes = entries.ToDictionary(
                    entry => entry.Id,
                    entry => GetEntrySizeBytes(entry.EntryDirectory),
                    StringComparer.Ordinal);
                long usage = remaining.Sum(entry => sizes[entry.Id]);

                for (int index = remaining.Count - 1; index >= 0 && usage > limitBytes; index--)
                {
                    HistoryEntry entry = remaining[index];
                    if (entry.Id == protectedEntryId)
                    {
                        continue;
                    }

                    toDelete.Add(entry.Id);
                    usage -= sizes[entry.Id];
                }
            }

            return toDelete.Count == 0
                ? HistoryDeleteResult.Empty
                : DeleteCore(toDelete);
        }
    }

    public long GetStorageSizeBytes()
    {
        lock (_gate)
        {
            EnsureInitialized();
            return LoadAllUnlocked().Sum(entry => GetEntrySizeBytes(entry.EntryDirectory));
        }
    }

    private static HistoryMetadata CreateMetadata(
        HistoryEntry entry,
        string text,
        bool hasImage) =>
        new()
        {
            Id = entry.Id,
            CreatedAtUtc = entry.CreatedAtUtc,
            Text = text,
            Width = entry.Width,
            Height = entry.Height,
            MonitorId = entry.MonitorId,
            OcrLanguage = entry.OcrLanguage,
            CharCount = text.Length,
            AppVersion = entry.AppVersion,
            HasImage = hasImage,
        };

    private static HistoryEntry CreateEntry(
        HistorySaveRequest request,
        bool hasImage,
        bool isOrphaned,
        string entryDirectory) =>
        new()
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
            HasImage = hasImage,
            IsOrphaned = isOrphaned,
            EntryDirectory = entryDirectory,
        };

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(
            value,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static long GetEntrySizeBytes(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;

        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return total;
    }

    private static bool IsAscii(string value) =>
        value.All(character => character <= 0x7F);

    private static bool IsOrphaned(HistoryEntry entry)
    {
        if (!File.Exists(entry.MetadataPath))
        {
            return true;
        }

        return entry.HasImage &&
            (entry.ImagePath is null ||
             entry.ThumbnailPath is null ||
             !File.Exists(entry.ImagePath) ||
             !File.Exists(entry.ThumbnailPath));
    }

    private void ArchiveDatabaseFiles()
    {
        bool hasDatabase = File.Exists(_databasePath) ||
            File.Exists(_databasePath + "-wal") ||
            File.Exists(_databasePath + "-shm");

        if (!hasDatabase)
        {
            return;
        }

        string archiveBase = Path.Combine(
            _historyDirectory,
            HistoryFileNames.Database + ".corrupt-" +
            _utcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

        if (File.Exists(archiveBase))
        {
            archiveBase += "-" + Guid.NewGuid().ToString("N")[..8];
        }

        if (File.Exists(_databasePath))
        {
            File.Move(_databasePath, archiveBase);
        }

        if (File.Exists(_databasePath + "-wal"))
        {
            File.Move(_databasePath + "-wal", archiveBase + "-wal");
        }

        if (File.Exists(_databasePath + "-shm"))
        {
            File.Move(_databasePath + "-shm", archiveBase + "-shm");
        }
    }

    private HistoryEntry? GetUnlocked(string id)
    {
        using SqliteConnection connection = OpenConnection();
        return GetUnlocked(connection, transaction: null, id);
    }

    private HistoryEntry? GetUnlocked(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT " + SelectColumns + " FROM history_entries WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    private HistoryDeleteResult DeleteCore(IReadOnlyCollection<string> ids)
    {
        var deleted = new List<string>();
        var failures = new List<HistoryDeleteFailure>();

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (string id in ids)
        {
            if (!HistoryEntryId.IsValid(id))
            {
                failures.Add(new HistoryDeleteFailure(id, "The history entry id is not valid."));
                continue;
            }

            HistoryEntry? entry = GetUnlocked(connection, transaction, id);
            if (entry is null)
            {
                failures.Add(new HistoryDeleteFailure(id, "The history entry was not found."));
                continue;
            }

            string trashDirectory = Path.Combine(
                _historyDirectory,
                "." + id + "-" + Guid.NewGuid().ToString("N") + ".deleting");
            bool moved = false;

            try
            {
                if (Directory.Exists(entry.EntryDirectory))
                {
                    Directory.Move(entry.EntryDirectory, trashDirectory);
                    moved = true;
                }

                if (moved)
                {
                    Directory.Delete(trashDirectory, recursive: true);
                }

                using SqliteCommand delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM history_entries WHERE id = @id";
                delete.Parameters.AddWithValue("@id", id);

                if (delete.ExecuteNonQuery() == 0)
                {
                    throw new HistoryStorageException("The history entry no longer exists.");
                }

                deleted.Add(id);
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or SqliteException
                    or HistoryStorageException)
            {
                if (moved &&
                    Directory.Exists(trashDirectory) &&
                    !Directory.Exists(entry.EntryDirectory))
                {
                    try
                    {
                        Directory.Move(trashDirectory, entry.EntryDirectory);
                    }
                    catch (Exception restoreException) when (
                        restoreException is IOException or UnauthorizedAccessException)
                    {
                    }
                }

                failures.Add(new HistoryDeleteFailure(id, ex.Message));
            }
        }

        transaction.Commit();
        return new HistoryDeleteResult(deleted, failures);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS history_entries (
                id TEXT PRIMARY KEY NOT NULL,
                created_at_utc TEXT NOT NULL,
                text TEXT NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                monitor_id TEXT NOT NULL,
                ocr_language TEXT NULL,
                char_count INTEGER NOT NULL,
                app_version TEXT NOT NULL,
                has_image INTEGER NOT NULL DEFAULT 1,
                is_orphaned INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_history_created
                ON history_entries (created_at_utc DESC, id DESC);

            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
    }

    private string GetEntryDirectory(string id) => Path.Combine(_historyDirectory, id);

    private void ImportMetadata(
        string folderName,
        string entryDirectory,
        HistoryMetadata metadata)
    {
        string text = metadata.Text ?? string.Empty;
        bool hasImage = metadata.HasImage ||
            File.Exists(Path.Combine(entryDirectory, HistoryFileNames.Image));
        bool orphaned = hasImage &&
            (!File.Exists(Path.Combine(entryDirectory, HistoryFileNames.Image)) ||
             !File.Exists(Path.Combine(entryDirectory, HistoryFileNames.Thumbnail)));
        DateTimeOffset createdAt = metadata.CreatedAtUtc;

        if (createdAt == default)
        {
            createdAt = new DateTimeOffset(Directory.GetLastWriteTimeUtc(entryDirectory));
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO history_entries (
                id, created_at_utc, text, width, height, monitor_id, ocr_language,
                char_count, app_version, has_image, is_orphaned)
            VALUES (
                @id, @createdAtUtc, @text, @width, @height, @monitorId, @ocrLanguage,
                @charCount, @appVersion, @hasImage, @isOrphaned)
            """;
        command.Parameters.AddWithValue("@id", folderName);
        command.Parameters.AddWithValue("@createdAtUtc", FormatTimestamp(createdAt));
        command.Parameters.AddWithValue("@text", text);
        command.Parameters.AddWithValue("@width", metadata.Width);
        command.Parameters.AddWithValue("@height", metadata.Height);
        command.Parameters.AddWithValue("@monitorId", metadata.MonitorId ?? string.Empty);
        command.Parameters.AddWithValue(
            "@ocrLanguage",
            metadata.OcrLanguage is null ? DBNull.Value : metadata.OcrLanguage);
        command.Parameters.AddWithValue("@charCount", text.Length);
        command.Parameters.AddWithValue(
            "@appVersion",
            string.IsNullOrWhiteSpace(metadata.AppVersion) ? "unknown" : metadata.AppVersion);
        command.Parameters.AddWithValue("@hasImage", hasImage ? 1 : 0);
        command.Parameters.AddWithValue("@isOrphaned", orphaned ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private void InsertEntry(HistorySaveRequest request, bool hasImage)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO history_entries (
                id, created_at_utc, text, width, height, monitor_id, ocr_language,
                char_count, app_version, has_image, is_orphaned)
            VALUES (
                @id, @createdAtUtc, @text, @width, @height, @monitorId, @ocrLanguage,
                @charCount, @appVersion, @hasImage, 0)
            """;
        command.Parameters.AddWithValue("@id", request.Id);
        command.Parameters.AddWithValue("@createdAtUtc", FormatTimestamp(request.CreatedAtUtc));
        command.Parameters.AddWithValue("@text", request.Text);
        command.Parameters.AddWithValue("@width", request.Width);
        command.Parameters.AddWithValue("@height", request.Height);
        command.Parameters.AddWithValue("@monitorId", request.MonitorId);
        command.Parameters.AddWithValue(
            "@ocrLanguage",
            request.OcrLanguage is null ? DBNull.Value : request.OcrLanguage);
        command.Parameters.AddWithValue("@charCount", request.Text.Length);
        command.Parameters.AddWithValue("@appVersion", request.AppVersion);
        command.Parameters.AddWithValue("@hasImage", hasImage ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private IReadOnlyList<HistoryEntry> LoadAllUnlocked()
    {
        using SqliteConnection connection = OpenConnection();
        return LoadAllUnlocked(connection, transaction: null);
    }

    private IReadOnlyList<HistoryEntry> LoadAllUnlocked(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT " + SelectColumns + " FROM history_entries " +
            "ORDER BY created_at_utc DESC, id DESC";

        using SqliteDataReader reader = command.ExecuteReader();
        return ReadEntries(reader);
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 3,
        };

        var connection = new SqliteConnection(builder.ConnectionString);

        try
        {
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 3000;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            // A failed PRAGMA must not leak a pooled handle to a corrupt database.
            connection.Dispose();
            throw;
        }
    }

    private static HistoryMetadata? ReadMetadata(string entryDirectory)
    {
        string path = Path.Combine(entryDirectory, HistoryFileNames.Metadata);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<HistoryMetadata>(
                File.ReadAllText(path),
                MetadataJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private List<HistoryEntry> ReadEntries(SqliteDataReader reader)
    {
        var entries = new List<HistoryEntry>();

        while (reader.Read())
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    private HistoryEntry ReadEntry(SqliteDataReader reader)
    {
        string id = reader.GetString(0);

        return new HistoryEntry
        {
            Id = id,
            CreatedAtUtc = ParseTimestamp(reader.GetString(1)),
            Text = reader.GetString(2),
            Width = reader.GetInt32(3),
            Height = reader.GetInt32(4),
            MonitorId = reader.GetString(5),
            OcrLanguage = reader.IsDBNull(6) ? null : reader.GetString(6),
            CharCount = reader.GetInt32(7),
            AppVersion = reader.GetString(8),
            HasImage = reader.GetInt32(9) != 0,
            IsOrphaned = reader.GetInt32(10) != 0,
            EntryDirectory = Path.Combine(_historyDirectory, id),
        };
    }

    private static void SavePng(Bitmap bitmap, string path) =>
        bitmap.Save(path, ImageFormat.Png);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteMetadata(string entryDirectory, HistoryMetadata metadata)
    {
        Directory.CreateDirectory(entryDirectory);

        string path = Path.Combine(entryDirectory, HistoryFileNames.Metadata);
        string temporaryPath = path + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(metadata, MetadataJsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class HistoryMetadata
    {
        public string Id { get; set; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; set; }

        public string Text { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public string MonitorId { get; set; } = string.Empty;

        public string? OcrLanguage { get; set; }

        public int CharCount { get; set; }

        public string AppVersion { get; set; } = string.Empty;

        public bool HasImage { get; set; }
    }
}
