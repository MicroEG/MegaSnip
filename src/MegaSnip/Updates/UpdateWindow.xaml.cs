using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MegaSnip.Infrastructure;

namespace MegaSnip.Updates;

internal partial class UpdateWindow : Window
{
    private readonly Func<CancellationToken, Task<UpdateCheckResult>> _checkForUpdates;
    private readonly CancellationTokenSource _cancellation = new();
    private string? _releaseUrl;
    private bool _checkStarted;
    private bool _isClosed;

    internal UpdateWindow(
        Version currentVersion,
        Func<CancellationToken, Task<UpdateCheckResult>> checkForUpdates)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(checkForUpdates);

        _checkForUpdates = checkForUpdates;

        InitializeComponent();
        CurrentVersionText.Text = UpdateService.FormatVersion(currentVersion);
        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _cancellation.Cancel();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CloseButton.Focus();

        if (_checkStarted)
        {
            return;
        }

        _checkStarted = true;
        _ = RunCheckAsync();
    }

    private async Task RunCheckAsync()
    {
        try
        {
            UpdateCheckResult result = await _checkForUpdates(_cancellation.Token);

            if (!_isClosed)
            {
                ShowResult(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (UpdateCheckException ex)
        {
            if (!_isClosed)
            {
                ShowFailure(ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (!_isClosed)
            {
                ShowFailure("Could not check for updates: " + ex.Message);
            }
        }
    }

    private void ShowResult(UpdateCheckResult result)
    {
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                StatusTitleText.Text = $"MegaSnip {result.LatestVersion} is available";
                StatusDetailText.Text =
                    $"You have {result.CurrentVersion}. Open the release page to download the new build.";
                ShowRelease(result.Release.ReleaseUrl);

                if (!string.IsNullOrWhiteSpace(result.Release.Notes))
                {
                    NotesText.Text = result.Release.Notes;
                    ReleaseNotesPanel.Visibility = Visibility.Visible;
                }

                break;
            case UpdateCheckStatus.UnknownVersion:
                StatusTitleText.Text = "A newer release could not be compared automatically";
                StatusDetailText.Text =
                    $"The latest published release is {result.LatestVersion}. Open it on GitHub to review the version.";
                ShowRelease(result.Release.ReleaseUrl);
                break;
            default:
                StatusTitleText.Text = "MegaSnip is up to date";
                StatusDetailText.Text =
                    $"Version {result.CurrentVersion} is the latest published release.";
                CloseButton.IsDefault = true;
                CloseButton.Focus();
                break;
        }
    }

    private void ShowRelease(string releaseUrl)
    {
        _releaseUrl = releaseUrl;
        OpenReleaseButton.Visibility = Visibility.Visible;
        OpenReleaseButton.IsDefault = true;
        CloseButton.IsDefault = false;
        OpenReleaseButton.Focus();
    }

    private void ShowFailure(string message)
    {
        StatusTitleText.Text = "Could not check for updates";
        StatusDetailText.Text = message;
        OpenReleaseButton.Visibility = Visibility.Collapsed;
        CloseButton.IsDefault = true;
        CloseButton.Focus();
    }

    private void OnOpenReleaseClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_releaseUrl))
        {
            OpenUrl(_releaseUrl);
        }
    }

    private void OnOpenReleasesClicked(object sender, RoutedEventArgs e) =>
        OpenUrl(UpdateService.ReleasesUrl);

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
        }
        catch (Exception ex) when (
            ex is Win32Exception
                or IOException
                or InvalidOperationException)
        {
            StatusTitleText.Text = "Could not open GitHub";
            StatusDetailText.Text =
                "Open this address in a browser instead: " + url;
        }
    }
}
