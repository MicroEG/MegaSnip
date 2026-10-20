using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace MegaSnip.Infrastructure;

internal sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;

    private readonly LowLevelKeyboardProc _hookProcedure;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<uint> _keysDown = new();
    private IntPtr _hook;
    private HotkeyGesture _gesture;
    private int _targetVirtualKey;
    private bool _disposed;

    internal LowLevelKeyboardHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _hookProcedure = OnKeyboardMessage;
    }

    internal event EventHandler? Pressed;

    internal bool Start(HotkeyGesture gesture, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        int virtualKey = KeyInterop.VirtualKeyFromKey(gesture.Key);
        if (virtualKey == 0)
        {
            error = $"{gesture.Key} cannot be used as a global hotkey.";
            return false;
        }

        _gesture = gesture;
        _targetVirtualKey = virtualKey;
        _keysDown.Clear();

        IntPtr module = GetModuleHandle(null);
        _hook = SetWindowsHookEx(WhKeyboardLl, _hookProcedure, module, 0);

        if (_hook == IntPtr.Zero)
        {
            int win32Error = Marshal.GetLastWin32Error();
            error =
                $"Windows could not install the keyboard fallback: " +
                new Win32Exception(win32Error).Message;
            return false;
        }

        error = null;
        return true;
    }

    internal void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _keysDown.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private IntPtr OnKeyboardMessage(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _hook != IntPtr.Zero)
        {
            int message = wParam.ToInt32();
            uint virtualKey = (uint)Marshal.ReadInt32(lParam);

            switch (message)
            {
                case WmKeyDown:
                case WmSysKeyDown:
                    if (_keysDown.Add(virtualKey) &&
                        virtualKey == (uint)_targetVirtualKey &&
                        GetCurrentModifiers() == _gesture.Modifiers)
                    {
                        _dispatcher.BeginInvoke(
                            DispatcherPriority.Input,
                            new Action(RaisePressed));
                    }

                    break;
                case WmKeyUp:
                case WmSysKeyUp:
                    _keysDown.Remove(virtualKey);
                    break;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void RaisePressed()
    {
        if (!_disposed && _hook != IntPtr.Zero)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static HotkeyModifiers GetCurrentModifiers()
    {
        HotkeyModifiers modifiers = HotkeyModifiers.None;

        if (IsKeyDown(VkControl))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsKeyDown(VkAlt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsKeyDown(VkShift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsKeyDown(VkLeftWindows) || IsKeyDown(VkRightWindows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc hookProcedure,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
