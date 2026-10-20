using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MegaSnip.Infrastructure;
using MegaSnip.Ocr;

namespace MegaSnip.Settings;

internal partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, string?> _applySettings;
    private readonly Action<bool> _setHotkeyCaptureActive;
    private readonly IReadOnlyList<OcrLanguage> _availableLanguages;
    private readonly AppSettings _currentSettings;
    private HotkeyGesture _hotkey;
    private bool _capturingHotkey;
    private bool _isSaving;

    internal SettingsWindow(
        AppSettings current,
        IReadOnlyList<OcrLanguage> availableLanguages,
        Func<AppSettings, string?> applySettings,
        Action<bool> setHotkeyCaptureActive)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(availableLanguages);
        ArgumentNullException.ThrowIfNull(applySettings);
        ArgumentNullException.ThrowIfNull(setHotkeyCaptureActive);

        _availableLanguages = availableLanguages;
        _applySettings = applySettings;
        _setHotkeyCaptureActive = setHotkeyCaptureActive;
        _currentSettings = current;
        _hotkey = current.Hotkey;

        InitializeComponent();

        HotkeyBox.Text = _hotkey.ToString();
        PopulateLanguages(current.OcrLanguageTag);
        PopulateLineBreakModes(current.LineBreakMode);
        AutoPreviewCheck.IsChecked = current.AutoPreview;
        SoundFeedbackCheck.IsChecked = current.SoundFeedback;
        StartWithWindowsCheck.IsChecked = current.StartWithWindows;

        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
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

    private void OnCaptureHotkeyClicked(object sender, RoutedEventArgs e)
    {
        if (_isSaving)
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

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_isSaving)
        {
            return;
        }

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

        AppSettings candidate = _currentSettings.Clone();
        candidate.Hotkey = _hotkey;
        candidate.OcrLanguageTag = language.Tag;
        candidate.LineBreakMode = lineBreak.Mode;
        candidate.AutoPreview = AutoPreviewCheck.IsChecked == true;
        candidate.SoundFeedback = SoundFeedbackCheck.IsChecked == true;
        candidate.StartWithWindows = StartWithWindowsCheck.IsChecked == true;

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

    private sealed record LanguageOption(string? Tag, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record LineBreakOption(LineBreakMode Mode, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
