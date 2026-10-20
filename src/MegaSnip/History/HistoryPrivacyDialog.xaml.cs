using System;
using System.Windows;
using MegaSnip.Infrastructure;

namespace MegaSnip.History;

internal partial class HistoryPrivacyDialog : Window
{
    internal HistoryPrivacyDialog(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);

        InitializeComponent();
        StorageText.Text = storageDirectory;
        Loaded += OnLoaded;
    }

    internal bool OpenSettingsRequested { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AcceptButton.Focus();
    }

    private void OnAcceptClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        OpenSettingsRequested = true;
        DialogResult = false;
    }
}
