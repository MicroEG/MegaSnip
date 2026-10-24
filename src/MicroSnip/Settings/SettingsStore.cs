using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicroSnip.Infrastructure;

namespace MicroSnip.Settings;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly string _settingsPath;

    public SettingsStore()
        : this(Path.Combine(AppDataPaths.RootDirectory, "settings.json"))
    {
    }

    internal SettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = settingsPath;
    }

    internal string SettingsPath => _settingsPath;

    internal AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(_settingsPath);
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);

            if (settings is null)
            {
                return new AppSettings();
            }

            settings.ApplyMigrations();
            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (NotSupportedException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    internal void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.ApplyMigrations();
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;

        string? directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, SerializerOptions);
        string temporaryPath = _settingsPath + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
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

    private static JsonSerializerOptions CreateSerializerOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
}
