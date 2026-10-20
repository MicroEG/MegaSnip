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
        _contextMenu.Items.Add(settingsItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(quitItem);

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

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

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

    public void ShowBalloon(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(
            4000,
            title,
            message,
            ToolTipIcon.Warning);
    }

    public void ShowError(string title, string message) =>
        ShowBalloon(title, message);

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

    private void OnSettingsClicked(object? sender, EventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnQuitClicked(object? sender, EventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);
}
