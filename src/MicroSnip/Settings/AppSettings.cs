using System;
using MicroSnip.Infrastructure;

namespace MicroSnip.Settings;

internal sealed class AppSettings
{
    internal const int CurrentSchemaVersion = 2;

    public AppSettings()
    {
    }

    public int SchemaVersion { get; set; }

    public HotkeyGesture Hotkey { get; set; } = HotkeyGesture.Default;

    public string? OcrLanguageTag { get; set; }

    public LineBreakMode LineBreakMode { get; set; } = LineBreakMode.Preserve;

    public bool AutoPreview { get; set; }

    public bool StartWithWindows { get; set; }

    public bool SoundFeedback { get; set; }

    public bool SaveHistory { get; set; } = true;

    public bool SaveHistoryImages { get; set; } = true;

    public bool AskBeforeDelete { get; set; } = true;

    public HistoryRetentionSettings HistoryRetention { get; set; } = new();

    public string? HistoryDirectory { get; set; }

    public HistoryRowAction DefaultHistoryAction { get; set; } = HistoryRowAction.OpenDetail;

    public bool HistoryPrivacyNoticeAccepted { get; set; }

    internal AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.HistoryRetention = HistoryRetention.Clone();
        return clone;
    }

    internal void ApplyMigrations()
    {
        if (SchemaVersion <= 0)
        {
            SchemaVersion = 1;
        }

        if (SchemaVersion < 2)
        {
            // v2 adds the history properties above; their initializers supply the defaults.
            SchemaVersion = 2;
        }

        SchemaVersion = Math.Min(SchemaVersion, CurrentSchemaVersion);
        Normalize();
    }

    internal void Normalize()
    {
        if (!Hotkey.IsValid())
        {
            Hotkey = HotkeyGesture.Default;
        }

        if (!Enum.IsDefined(typeof(LineBreakMode), LineBreakMode))
        {
            LineBreakMode = LineBreakMode.Preserve;
        }

        if (!Enum.IsDefined(typeof(HistoryRowAction), DefaultHistoryAction))
        {
            DefaultHistoryAction = HistoryRowAction.OpenDetail;
        }

        HistoryRetention ??= new HistoryRetentionSettings();
        HistoryRetention.Normalize();

        OcrLanguageTag = string.IsNullOrWhiteSpace(OcrLanguageTag)
            ? null
            : OcrLanguageTag.Trim();

        HistoryDirectory = string.IsNullOrWhiteSpace(HistoryDirectory)
            ? null
            : HistoryDirectory.Trim();
    }
}
