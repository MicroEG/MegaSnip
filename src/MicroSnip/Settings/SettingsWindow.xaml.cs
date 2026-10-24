using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MicroSnip.History;
using MicroSnip.Infrastructure;
using MicroSnip.Ocr;

namespace MicroSnip.Settings;

internal partial class SettingsWindow : Window
{
    private readonly Func<AppSettings> _getCurrentSettings;
    private readonly Func<AppSettings, string?> _applySettings;
    private readonly Func<string, Task<HistoryDirectoryChangeResult>> _changeHistoryDirectory;
    private readonly Action<bool> _setHotkeyCaptureActive;
    private readonly IReadOnlyList<OcrLanguage> _availableLanguages;
    private HotkeyGesture _hotkey;
    private bool _capturingHotkey;
    private bool _isSaving;
    private bool _isChangingHistory;

    internal SettingsWindow(
        AppSettings current,
        IReadOnlyList<OcrLanguage> availableLanguages,
        Func<AppSettings> getCurrentSettings,
        Func<AppSettings, string?> applySettings,
        Action<bool> setHotkeyCaptureActive,
        Func<string, Task<HistoryDirectoryChangeResult>> changeHistoryDirectory)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(availableLanguages);
        ArgumentNullException.ThrowIfNull(getCurrentSettings);
        ArgumentNullException.ThrowIfNull(applySettings);
        ArgumentNullException.ThrowIfNull(setHotkeyCaptureActive);
        ArgumentNullException.ThrowIfNull(changeHistoryDirectory);

        _availableLanguages = availableLanguages;
        _getCurrentSettings = getCurrentSettings;
        _applySettings = applySettings;
        _setHotkeyCaptureActive = setHotkeyCaptureActive;
        _changeHistoryDirectory = changeHistoryDirectory;
        _hotkey = current.Hotkey;

        InitializeComponent();

        HotkeyBox.Text = _hotkey.ToString();
        PopulateLanguages(current.OcrLanguageTag);
        PopulateLineBreakModes(current.LineBreakMode);
        PopulateHistoryActions(current.DefaultHistoryAction);
        AutoPreviewCheck.IsChecked = current.AutoPreview;
        SoundFeedbackCheck.IsChecked = current.SoundFeedback;
        StartWithWindowsCheck.IsChecked = current.StartWithWindows;
        SaveHistoryCheck.IsChecked = current.SaveHistory;
        SaveHistoryImagesCheck.IsChecked = current.SaveHistoryImages;
        AskBeforeDeleteCheck.IsChecked = current.AskBeforeDelete;
        HistoryPathText.Text = HistoryPaths.Resolve(current.HistoryDirectory);

        HistoryRetentionSettings retention =
            current.HistoryRetention ?? new HistoryRetentionSettings();
        AutoCleanupCheck.IsChecked = retention.AutoCleanupEnabled;
        MaxEntriesBox.Text = retention.MaxEntries.ToString(CultureInfo.InvariantCulture);
        MaxAgeDaysBox.Text = retention.MaxAgeDays.ToString(CultureInfo.InvariantCulture);
        MaxDiskMBBox.Text = retention.MaxDiskMB.ToString(CultureInfo.InvariantCulture);

        UpdateHistoryControlStates();
        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isChangingHistory)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_capturingHotkey)
        {
            _capturingHotkey = false;
            _setHotkeyCaptureActive(false);
        }

        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CaptureHotkeyButton.Focus();
    }

    private void PopulateLanguages(string? selectedTag)
    {
        var options = new List<LanguageOption>
        {
            new(null, "System default"),
        };

        options.AddRange(
            _availableLanguages
                .OrderBy(language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(language => new LanguageOption(language.Tag, language.DisplayName)));

        LanguageCombo.ItemsSource = options;
        LanguageCombo.SelectedItem =
            options.FirstOrDefault(option =>
                string.Equals(option.Tag, selectedTag, StringComparison.OrdinalIgnoreCase))
            ?? options[0];
    }

    private void PopulateLineBreakModes(LineBreakMode selectedMode)
    {
        var options = new[]
        {
            new LineBreakOption(LineBreakMode.Preserve, "Preserve line breaks"),
            new LineBreakOption(LineBreakMode.JoinWithSpaces, "Join lines with spaces"),
        };

        LineBreakCombo.ItemsSource = options;
        LineBreakCombo.SelectedItem =
            options.FirstOrDefault(option => option.Mode == selectedMode)
            ?? options[0];
    }

    private void PopulateHistoryActions(HistoryRowAction selectedAction)
    {
        var options = new[]
        {
            new HistoryActionOption(HistoryRowAction.OpenDetail, "Open details"),
            new HistoryActionOption(HistoryRowAction.CopyText, "Copy text"),
            new HistoryActionOption(HistoryRowAction.CopyImage, "Copy image"),
        };

        DefaultHistoryActionCombo.ItemsSource = options;
        DefaultHistoryActionCombo.SelectedItem =
            options.FirstOrDefault(option => option.Action == selectedAction)
            ?? options[0];
    }

    private void OnSaveHistoryChanged(object sender, RoutedEventArgs e) =>
        UpdateHistoryControlStates();

    private void OnAutoCleanupChanged(object sender, RoutedEventArgs e) =>
        UpdateHistoryControlStates();

    private void UpdateHistoryControlStates()
    {
        bool saveHistory = SaveHistoryCheck.IsChecked == true;

        HistoryOptionsPanel.IsEnabled = saveHistory;
        AutoCleanupOptionsPanel.IsEnabled =
            saveHistory && AutoCleanupCheck.IsChecked == true;
    }

    private void OnCaptureHotkeyClicked(object sender, RoutedEventArgs e)
    {
        if (_isSaving || _isChangingHistory)
        {
            return;
        }

        if (_capturingHotkey)
        {
            FinishHotkeyCapture();
            return;
        }

        _capturingHotkey = true;
        HotkeyBox.Text = "Press a shortcut…";
        CaptureHotkeyButton.Content = "Cancel";
        HotkeyBox.Focus();
        Keyboard.Focus(HotkeyBox);
        _setHotkeyCaptureActive(true);
        ClearError();
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        HotkeyModifiers modifiers = GetPressedModifiers();

        if (HotkeyGesture.IsModifierKey(key))
        {
            e.Handled = true;
            return;
        }

        if (key is Key.Tab or Key.Escape && modifiers == HotkeyModifiers.None)
        {
            FinishHotkeyCapture();

            if (key == Key.Escape)
            {
                e.Handled = true;
            }

            return;
        }

        e.Handled = true;

        if (key == Key.None)
        {
            return;
        }

        var gesture = new HotkeyGesture(modifiers, key);
        if (!gesture.IsValid())
        {
            ShowError("Choose a shortcut with Ctrl, Alt, Shift, or Win plus another key.");
            return;
        }

        _hotkey = gesture;
        FinishHotkeyCapture();
        ClearError();
    }

    private void OnHotkeyLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_capturingHotkey && !ReferenceEquals(e.NewFocus, CaptureHotkeyButton))
        {
            FinishHotkeyCapture();
        }
    }

    private void FinishHotkeyCapture()
    {
        _capturingHotkey = false;
        CaptureHotkeyButton.Content = "Change";
        HotkeyBox.Text = _hotkey.ToString();
        _setHotkeyCaptureActive(false);
    }

    private static HotkeyModifiers GetPressedModifiers()
    {
        ModifierKeys pressed = Keyboard.Modifiers;
        HotkeyModifiers modifiers = HotkeyModifiers.None;

        if ((pressed & ModifierKeys.Control) != 0)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if ((pressed & ModifierKeys.Alt) != 0)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if ((pressed & ModifierKeys.Shift) != 0)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if ((pressed & ModifierKeys.Windows) != 0)
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private async void OnChangeHistoryFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_isSaving || _isChangingHistory)
        {
            return;
        }

        string currentPath = HistoryPathText.Text;

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose an empty folder for MicroSnip history",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = currentPath,
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK ||
            string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        string target = dialog.SelectedPath;
        SetHistoryChangeBusy(true);
        SetPathStatus("Moving history…", PathStatusKind.Info);

        try
        {
            HistoryDirectoryChangeResult result =
                await _changeHistoryDirectory(target);

            if (result.Error is not null)
            {
                SetPathStatus(result.Error, PathStatusKind.Error);
                return;
            }

            HistoryPathText.Text = result.Directory;

            bool moved = !HistoryDirectoryMigrator.AreSameDirectory(
                currentPath,
                result.Directory);
            string message = result.Warning ??
                (moved
                    ? "History moved to the new folder."
                    : "History is already stored in this folder.");

            SetPathStatus(
                message,
                result.Warning is null
                    ? PathStatusKind.Success
                    : PathStatusKind.Warning);
        }
        catch (Exception ex)
        {
            SetPathStatus("Could not move history: " + ex.Message, PathStatusKind.Error);
        }
        finally
        {
            SetHistoryChangeBusy(false);
        }
    }

    private void SetHistoryChangeBusy(bool busy)
    {
        _isChangingHistory = busy;
        FormPanel.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : null;

        if (!busy)
        {
            UpdateHistoryControlStates();
        }
    }

    private void SetPathStatus(string message, PathStatusKind kind)
    {
        HistoryPathStatusText.Text = message;
        HistoryPathStatusText.Foreground = (System.Windows.Media.Brush)FindResource(
            kind switch
            {
                PathStatusKind.Success => "PathSuccessBrush",
                PathStatusKind.Warning => "PathWarningBrush",
                PathStatusKind.Error => "PathErrorBrush",
                _ => "PathInfoBrush",
            });
        HistoryPathStatusText.Visibility = Visibility.Visible;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_isSaving || _isChangingHistory)
        {
            return;
        }

        ClearError();

        if (_capturingHotkey)
        {
            FinishHotkeyCapture();
        }

        if (!_hotkey.IsValid())
        {
            ShowError("Choose a hotkey with at least one modifier key and another key.");
            return;
        }

        if (LanguageCombo.SelectedItem is not LanguageOption language)
        {
            ShowError("Choose an OCR language.");
            return;
        }

        if (LineBreakCombo.SelectedItem is not LineBreakOption lineBreak)
        {
            ShowError("Choose how copied text should handle line breaks.");
            return;
        }

        if (DefaultHistoryActionCombo.SelectedItem is not HistoryActionOption historyAction)
        {
            ShowError("Choose what double-clicking a history row should do.");
            return;
        }

        if (!TryReadRetention(
                out int maxEntries,
                out int maxAgeDays,
                out int maxDiskMb))
        {
            return;
        }

        AppSettings candidate = _getCurrentSettings().Clone();
        candidate.Hotkey = _hotkey;
        candidate.OcrLanguageTag = language.Tag;
        candidate.LineBreakMode = lineBreak.Mode;
        candidate.AutoPreview = AutoPreviewCheck.IsChecked == true;
        candidate.SoundFeedback = SoundFeedbackCheck.IsChecked == true;
        candidate.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        candidate.SaveHistory = SaveHistoryCheck.IsChecked == true;
        candidate.SaveHistoryImages = SaveHistoryImagesCheck.IsChecked == true;
        candidate.AskBeforeDelete = AskBeforeDeleteCheck.IsChecked == true;
        candidate.DefaultHistoryAction = historyAction.Action;
        candidate.HistoryRetention ??= new HistoryRetentionSettings();
        candidate.HistoryRetention.AutoCleanupEnabled = AutoCleanupCheck.IsChecked == true;
        candidate.HistoryRetention.MaxEntries = maxEntries;
        candidate.HistoryRetention.MaxAgeDays = maxAgeDays;
        candidate.HistoryRetention.MaxDiskMB = maxDiskMb;

        _isSaving = true;
        SaveButton.IsEnabled = false;

        try
        {
            string? error = _applySettings(candidate);

            if (error is not null)
            {
                ShowError(error);
                return;
            }

            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _isSaving = false;

            if (IsVisible)
            {
                SaveButton.IsEnabled = true;
            }
        }
    }

    private bool TryReadRetention(
        out int maxEntries,
        out int maxAgeDays,
        out int maxDiskMb)
    {
        bool entriesValid = TryParseLimit(MaxEntriesBox.Text, out maxEntries);
        bool ageValid = TryParseLimit(MaxAgeDaysBox.Text, out maxAgeDays);
        bool diskValid = TryParseLimit(MaxDiskMBBox.Text, out maxDiskMb);

        if (!entriesValid || !ageValid || !diskValid)
        {
            ShowError("Retention limits must be whole numbers of 0 or more.");
            return false;
        }

        MaxEntriesBox.Text = maxEntries.ToString(CultureInfo.InvariantCulture);
        MaxAgeDaysBox.Text = maxAgeDays.ToString(CultureInfo.InvariantCulture);
        MaxDiskMBBox.Text = maxDiskMb.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryParseLimit(string text, out int value) =>
        int.TryParse(
            text.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value) &&
        value >= 0;

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private enum PathStatusKind
    {
        Info,
        Success,
        Warning,
        Error,
    }

    private sealed record LanguageOption(string? Tag, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record LineBreakOption(LineBreakMode Mode, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record HistoryActionOption(
        HistoryRowAction Action,
        string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
