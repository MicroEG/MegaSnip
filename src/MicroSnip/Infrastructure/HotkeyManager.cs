using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;

namespace MicroSnip.Infrastructure;

internal sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x4D53;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly HotkeyMessageWindow _messageWindow;
    private readonly LowLevelKeyboardHook _fallbackHook;
    private bool _registered;
    private bool _usingFallback;
    private bool _disposed;

    public HotkeyManager()
    {
        _messageWindow = new HotkeyMessageWindow(HandleHotkeyMessage);
        _fallbackHook = new LowLevelKeyboardHook(Dispatcher.CurrentDispatcher);
        _fallbackHook.Pressed += OnFallbackPressed;
    }

    public event EventHandler? Pressed;

    internal bool IsUsingFallback => _usingFallback;

    internal void Suspend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UnregisterCurrent();
    }

    public bool TryRegister(HotkeyGesture gesture, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UnregisterCurrent();

        if (!gesture.IsValid())
        {
            error = "Choose a shortcut with at least one modifier key and another key.";
            return false;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(gesture.Key);
        if (virtualKey == 0)
        {
            error = $"{gesture.Key} cannot be used as a global hotkey.";
            return false;
        }

        uint modifiers = (uint)gesture.Modifiers | NativeMethods.ModNoRepeat;
        if (NativeMethods.RegisterHotKey(
                _messageWindow.Handle,
                HotkeyId,
                modifiers,
                (uint)virtualKey))
        {
            _registered = true;
            error = null;
            return true;
        }

        int win32Error = Marshal.GetLastWin32Error();
        string nativeError = win32Error == ErrorHotkeyAlreadyRegistered
            ? $"{gesture} is already used by another application."
            : $"Windows could not register {gesture}: {new Win32Exception(win32Error).Message}";

        if (_fallbackHook.Start(gesture, out string? fallbackError))
        {
            _usingFallback = true;
            error = null;
            return true;
        }

        error = $"{nativeError} {fallbackError}";
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterCurrent();
        _fallbackHook.Pressed -= OnFallbackPressed;
        _fallbackHook.Dispose();
        _messageWindow.DestroyHandle();
        _disposed = true;
    }

    private void UnregisterCurrent()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, HotkeyId);
            _registered = false;
        }

        if (_usingFallback)
        {
            _fallbackHook.Stop();
            _usingFallback = false;
        }
    }

    private void HandleHotkeyMessage(int id)
    {
        if (id == HotkeyId && _registered)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnFallbackPressed(object? sender, EventArgs e)
    {
        if (!_disposed && _usingFallback)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class HotkeyMessageWindow : NativeWindow
    {
        private readonly Action<int> _onHotkey;

        public HotkeyMessageWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;

            var createParams = new CreateParams
            {
                Caption = "MicroSnip.HotkeyWindow",
                Parent = NativeMethods.HwndMessage,
                Style = 0,
                ExStyle = 0,
            };

            CreateHandle(createParams);

            if (Handle == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not create the global hotkey message window.");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WmHotkey)
            {
                _onHotkey(m.WParam.ToInt32());
            }

            base.WndProc(ref m);
        }
    }
}
