using System;
using System.Windows;
using System.Windows.Controls;
using MegaSnip.Infrastructure;

namespace MegaSnip.History;

internal partial class HistoryConfirmDialog : Window
{
    private readonly string? _requiredPhrase;

    internal HistoryConfirmDialog(
        string title,
        string message,
        string confirmText,
        string? requiredPhrase = null,
        bool showDontAskAgain = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmText);

        _requiredPhrase = requiredPhrase;

        InitializeComponent();

        DialogTitle.Text = title;
        DialogMessage.Text = message;
        ConfirmButton.Content = confirmText;

        if (requiredPhrase is not null)
        {
            PhrasePanel.Visibility = Visibility.Visible;
        }

        if (showDontAskAgain)
        {
            DontAskCheck.Visibility = Visibility.Visible;
        }

        Loaded += OnLoaded;
        UpdateConfirmState();
    }

    internal bool DontAskAgain => DontAskCheck.IsChecked == true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_requiredPhrase is not null)
        {
            PhraseBox.Focus();
        }
        else
        {
            ConfirmButton.Focus();
        }
    }

    private void OnPhraseChanged(object sender, TextChangedEventArgs e) =>
        UpdateConfirmState();

    private void UpdateConfirmState() =>
        ConfirmButton.IsEnabled = _requiredPhrase is null ||
            string.Equals(PhraseBox.Text.Trim(), _requiredPhrase, StringComparison.Ordinal);

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (ConfirmButton.IsEnabled)
        {
            DialogResult = true;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
