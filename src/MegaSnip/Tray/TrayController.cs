using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MegaSnip.Infrastructure;

namespace MegaSnip.Tray;

internal sealed class TrayController : IDisposable
{
    private readonly Icon _icon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _snipItem;
    private readonly ToolStripMenuItem _lastResultItem;
    private readonly ToolStripMenuItem _historyItem;
    private readonly ToolStripMenuItem _pauseHistoryItem;
    private readonly ToolStripMenuItem _openHistoryFolderItem;
    private bool _disposed;

    public TrayController()
    {
        _icon = LoadIcon();

        _snipItem = new ToolStripMenuItem("Snip")
        {
            ShortcutKeyDisplayString = HotkeyGesture.Default.ToString(),
        };
        _snipItem.Click += OnSnipClicked;

        _lastResultItem = new ToolStripMenuItem("Last result")
        {
            Enabled = false,
        };
        _lastResultItem.Click += OnLastResultClicked;

        _historyItem = new ToolStripMenuItem("History");
        _historyItem.Click += OnHistoryClicked;

        _pauseHistoryItem = new ToolStripMenuItem("Pause history for 1 hour");
        _pauseHistoryItem.Click += OnHistoryPauseToggled;

        _openHistoryFolderItem = new ToolStripMenuItem("Open history folder");
        _openHistoryFolderItem.Click += OnOpenHistoryFolderRequested;

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += OnSettingsClicked;

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += OnQuitClicked;

        _contextMenu = new ContextMenuStrip
        {
            ShowImageMargin = false,
        };
        _contextMenu.Items.Add(_snipItem);
        _contextMenu.Items.Add(_lastResultItem);
        _contextMenu.Items.Add(_historyItem);
        _contextMenu.Items.Add(_pauseHistoryItem);
        _contextMenu.Items.Add(_openHistoryFolderItem);
        _contextMenu.Items.Add(settingsItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(quitItem);
        _contextMenu.Opening += OnContextMenuOpening;

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "MegaSnip - offline text snipping",
            ContextMenuStrip = _contextMenu,
            Visible = true,
        };
    }

    public event EventHandler? SnipRequested;

    public event EventHandler? LastResultRequested;

    public event EventHandler? HistoryRequested;

    public event EventHandler? HistoryPauseToggled;

    public event EventHandler? OpenHistoryFolderRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? MenuOpening;

    public void SetHotkeyDisplay(string display)
    {
        if (_disposed)
        {
            return;
        }

        _snipItem.ShortcutKeyDisplayString = display;
    }

    public void SetLastResultAvailable(bool available)
    {
        if (_disposed)
        {
            return;
        }

        _lastResultItem.Enabled = available;
    }

    public void SetHistoryOptions(
        bool savingEnabled,
        bool historyAvailable,
        bool paused)
    {
        if (_disposed)
        {
            return;
        }

        _pauseHistoryItem.Enabled = savingEnabled;
        _pauseHistoryItem.Text = paused
            ? "Resume history"
            : "Pause history for 1 hour";
        _openHistoryFolderItem.Enabled = historyAvailable;
    }

    public void ShowBalloon(string title, string message) =>
        ShowBalloon(title, message, ToolTipIcon.Warning);

    public void ShowInfo(string title, string message) =>
        ShowBalloon(title, message, ToolTipIcon.Info);

    public void ShowError(string title, string message) =>
        ShowBalloon(title, message, ToolTipIcon.Warning);

    private void ShowBalloon(string title, string message, ToolTipIcon icon)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(4000, title, message, icon);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _icon.Dispose();
        _disposed = true;
    }

    private static Icon LoadIcon()
    {
        string? executablePath = Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            try
            {
                Icon? executableIcon = Icon.ExtractAssociatedIcon(executablePath);
                if (executableIcon is not null)
                {
                    return executableIcon;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private void OnSnipClicked(object? sender, EventArgs e) =>
        SnipRequested?.Invoke(this, EventArgs.Empty);

    private void OnLastResultClicked(object? sender, EventArgs e) =>
        LastResultRequested?.Invoke(this, EventArgs.Empty);

    private void OnHistoryClicked(object? sender, EventArgs e) =>
        HistoryRequested?.Invoke(this, EventArgs.Empty);

    private void OnHistoryPauseToggled(object? sender, EventArgs e) =>
        HistoryPauseToggled?.Invoke(this, EventArgs.Empty);

    private void OnOpenHistoryFolderRequested(object? sender, EventArgs e) =>
        OpenHistoryFolderRequested?.Invoke(this, EventArgs.Empty);

    private void OnContextMenuOpening(object? sender, EventArgs e) =>
        MenuOpening?.Invoke(this, EventArgs.Empty);

    private void OnSettingsClicked(object? sender, EventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnQuitClicked(object? sender, EventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);
}
