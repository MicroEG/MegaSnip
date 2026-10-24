using System;

namespace MicroSnip.Settings;

internal sealed class HistoryRetentionSettings
{
    internal const int DefaultMaxEntries = 500;
    internal const int DefaultMaxAgeDays = 30;
    internal const int DefaultMaxDiskMB = 500;

    public bool AutoCleanupEnabled { get; set; } = true;

    public int MaxEntries { get; set; } = DefaultMaxEntries;

    public int MaxAgeDays { get; set; } = DefaultMaxAgeDays;

    public int MaxDiskMB { get; set; } = DefaultMaxDiskMB;

    internal HistoryRetentionSettings Clone() =>
        (HistoryRetentionSettings)MemberwiseClone();

    internal void Normalize()
    {
        MaxEntries = Math.Max(0, MaxEntries);
        MaxAgeDays = Math.Max(0, MaxAgeDays);
        MaxDiskMB = Math.Max(0, MaxDiskMB);
    }
}
