using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Zaldaryon.Pharos.Platform;

/// <summary>
/// Minimal Win32 user32.dll interop for configuring hidden window styles on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Win32WindowInterop
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_APPWINDOW = 0x00040000L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;
    public const int SW_HIDE = 0;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
        {
            return GetWindowLongPtr64(hWnd, nIndex);
        }

        return new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
        {
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        }

        return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    /// <summary>
    /// Suppresses taskbar presence and focus stealing for the given window handle.
    /// </summary>
    public static void SuppressTaskbarAndFocus(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

        // Remove WS_EX_APPWINDOW so Windows does not force a taskbar button
        exStyle &= ~WS_EX_APPWINDOW;

        // Add WS_EX_TOOLWINDOW and WS_EX_NOACTIVATE to completely hide from taskbar and Alt+Tab
        exStyle |= WS_EX_TOOLWINDOW;
        exStyle |= WS_EX_NOACTIVATE;

        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        ShowWindow(hwnd, SW_HIDE);
    }
}
