using System;
using System.IO;
using System.Windows.Input;
using MicroSnip.Infrastructure;
using MicroSnip.Settings;
using Xunit;

namespace MicroSnip.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _settingsPath;

    public SettingsStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "MicroSnip.Tests",
            Guid.NewGuid().ToString("N"));
        _settingsPath = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
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
    public void Load_WhenFileIsMissing_ReturnsDefaults()
    {
        var store = new SettingsStore(_settingsPath);

        AppSettings settings = store.Load();

        Assert.Equal(HotkeyGesture.Default, settings.Hotkey);
        Assert.Null(settings.OcrLanguageTag);
        Assert.Equal(LineBreakMode.Preserve, settings.LineBreakMode);
        Assert.False(settings.AutoPreview);
        Assert.False(settings.StartWithWindows);
        Assert.False(settings.SoundFeedback);
        Assert.True(settings.SaveHistory);
        Assert.True(settings.SaveHistoryImages);
        Assert.True(settings.AskBeforeDelete);
        Assert.True(settings.HistoryRetention.AutoCleanupEnabled);
        Assert.Equal(500, settings.HistoryRetention.MaxEntries);
        Assert.Equal(30, settings.HistoryRetention.MaxAgeDays);
        Assert.Equal(500, settings.HistoryRetention.MaxDiskMB);
        Assert.Null(settings.HistoryDirectory);
        Assert.Equal(HistoryRowAction.OpenDetail, settings.DefaultHistoryAction);
        Assert.False(settings.HistoryPrivacyNoticeAccepted);
        Assert.False(File.Exists(_settingsPath));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryValue()
    {
        var store = new SettingsStore(_settingsPath);
        var expected = new AppSettings
        {
            Hotkey = new HotkeyGesture(
                HotkeyModifiers.Control | HotkeyModifiers.Shift,
                Key.F8),
            OcrLanguageTag = "en-US",
            LineBreakMode = LineBreakMode.JoinWithSpaces,
            AutoPreview = true,
            StartWithWindows = true,
            SoundFeedback = true,
            SaveHistory = false,
            SaveHistoryImages = false,
            AskBeforeDelete = false,
            HistoryRetention = new HistoryRetentionSettings
            {
                AutoCleanupEnabled = false,
                MaxEntries = 42,
                MaxAgeDays = 7,
                MaxDiskMB = 64,
            },
            HistoryDirectory = @"C:\MicroSnip History",
            DefaultHistoryAction = HistoryRowAction.CopyImage,
            HistoryPrivacyNoticeAccepted = true,
        };

        store.Save(expected);
        AppSettings actual = store.Load();

        Assert.Equal(expected.Hotkey, actual.Hotkey);
        Assert.Equal(expected.OcrLanguageTag, actual.OcrLanguageTag);
        Assert.Equal(expected.LineBreakMode, actual.LineBreakMode);
        Assert.Equal(expected.AutoPreview, actual.AutoPreview);
        Assert.Equal(expected.StartWithWindows, actual.StartWithWindows);
        Assert.Equal(expected.SoundFeedback, actual.SoundFeedback);
        Assert.Equal(expected.SaveHistory, actual.SaveHistory);
        Assert.Equal(expected.SaveHistoryImages, actual.SaveHistoryImages);
        Assert.Equal(expected.AskBeforeDelete, actual.AskBeforeDelete);
        Assert.Equal(
            expected.HistoryRetention.AutoCleanupEnabled,
            actual.HistoryRetention.AutoCleanupEnabled);
        Assert.Equal(expected.HistoryRetention.MaxEntries, actual.HistoryRetention.MaxEntries);
        Assert.Equal(expected.HistoryRetention.MaxAgeDays, actual.HistoryRetention.MaxAgeDays);
        Assert.Equal(expected.HistoryRetention.MaxDiskMB, actual.HistoryRetention.MaxDiskMB);
        Assert.Equal(expected.HistoryDirectory, actual.HistoryDirectory);
        Assert.Equal(expected.DefaultHistoryAction, actual.DefaultHistoryAction);
        Assert.Equal(expected.HistoryPrivacyNoticeAccepted, actual.HistoryPrivacyNoticeAccepted);
        Assert.Equal(AppSettings.CurrentSchemaVersion, actual.SchemaVersion);
        Assert.False(File.Exists(_settingsPath + ".tmp"));
    }

    [Fact]
    public void Save_WritesCamelCaseJsonWithNamedEnums()
    {
        var store = new SettingsStore(_settingsPath);
        store.Save(new AppSettings { LineBreakMode = LineBreakMode.JoinWithSpaces });

        string json = File.ReadAllText(_settingsPath);

        Assert.Contains("\"schemaVersion\": 2", json);
        Assert.Contains("\"lineBreakMode\": \"JoinWithSpaces\"", json);
        Assert.Contains("\"autoPreview\"", json);
        Assert.Contains("\"ocrLanguageTag\"", json);
        Assert.Contains("\"saveHistory\": true", json);
        Assert.Contains("\"historyRetention\"", json);
        Assert.Contains("\"maxDiskMB\": 500", json);
        Assert.Contains("\"defaultHistoryAction\": \"OpenDetail\"", json);
    }

    [Fact]
    public void Load_WithCorruptJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_settingsPath, "{ this is not valid json");
        var store = new SettingsStore(_settingsPath);

        AppSettings settings = store.Load();

        Assert.Equal(HotkeyGesture.Default, settings.Hotkey);
        Assert.Equal(LineBreakMode.Preserve, settings.LineBreakMode);
    }

    [Fact]
    public void Load_NormalizesInvalidValues()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _settingsPath,
            """
            {
              "hotkey": { "modifiers": "None", "key": "None" },
              "ocrLanguageTag": "  en-US  ",
              "lineBreakMode": 99,
              "autoPreview": true,
              "startWithWindows": false,
              "soundFeedback": true
            }
            """);
        var store = new SettingsStore(_settingsPath);

        AppSettings settings = store.Load();

        Assert.Equal(HotkeyGesture.Default, settings.Hotkey);
        Assert.Equal("en-US", settings.OcrLanguageTag);
        Assert.Equal(LineBreakMode.Preserve, settings.LineBreakMode);
        Assert.True(settings.AutoPreview);
        Assert.True(settings.SoundFeedback);
        Assert.Equal(HistoryRowAction.OpenDetail, settings.DefaultHistoryAction);
        Assert.True(settings.HistoryRetention.AutoCleanupEnabled);
    }

    [Fact]
    public void Load_WithVersionOneSettings_AddsHistoryDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _settingsPath,
            """
            {
              "hotkey": { "modifiers": "Control, Alt", "key": "Y" },
              "ocrLanguageTag": "en-US",
              "lineBreakMode": "JoinWithSpaces",
              "autoPreview": true,
              "startWithWindows": true,
              "soundFeedback": true
            }
            """);
        var store = new SettingsStore(_settingsPath);

        AppSettings settings = store.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal("Ctrl+Alt+Y", settings.Hotkey.ToString());
        Assert.Equal("en-US", settings.OcrLanguageTag);
        Assert.Equal(LineBreakMode.JoinWithSpaces, settings.LineBreakMode);
        Assert.True(settings.AutoPreview);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.SoundFeedback);
        Assert.True(settings.SaveHistory);
        Assert.True(settings.SaveHistoryImages);
        Assert.True(settings.AskBeforeDelete);
        Assert.Equal(500, settings.HistoryRetention.MaxEntries);
        Assert.Equal(30, settings.HistoryRetention.MaxAgeDays);
        Assert.Equal(500, settings.HistoryRetention.MaxDiskMB);
        Assert.Null(settings.HistoryDirectory);
        Assert.Equal(HistoryRowAction.OpenDetail, settings.DefaultHistoryAction);
        Assert.False(settings.HistoryPrivacyNoticeAccepted);
    }

    [Fact]
    public void Load_WithWhitespaceLanguage_ReturnsNullLanguage()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_settingsPath, "{ \"ocrLanguageTag\": \"   \" }");
        var store = new SettingsStore(_settingsPath);

        AppSettings settings = store.Load();

        Assert.Null(settings.OcrLanguageTag);
    }

    [Fact]
    public void Clone_CopiesHistorySettingsIndependently()
    {
        var original = new AppSettings
        {
            SaveHistory = false,
            AskBeforeDelete = false,
            HistoryDirectory = @"C:\History",
            HistoryRetention = new HistoryRetentionSettings
            {
                AutoCleanupEnabled = false,
                MaxEntries = 12,
                MaxAgeDays = 3,
                MaxDiskMB = 8,
            },
        };

        AppSettings clone = original.Clone();
        clone.AskBeforeDelete = true;
        clone.HistoryRetention.MaxEntries = 99;

        Assert.False(original.AskBeforeDelete);
        Assert.Equal(12, original.HistoryRetention.MaxEntries);
        Assert.True(clone.AskBeforeDelete);
        Assert.Equal(99, clone.HistoryRetention.MaxEntries);
        Assert.Equal(original.HistoryDirectory, clone.HistoryDirectory);
        Assert.Equal(original.SaveHistory, clone.SaveHistory);
    }
}
