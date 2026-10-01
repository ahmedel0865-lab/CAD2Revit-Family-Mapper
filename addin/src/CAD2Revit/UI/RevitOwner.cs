using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using IWin32Window = System.Windows.Forms.IWin32Window;

namespace CAD2Revit.UI
{
    /// <summary>
    /// Revit's main window as the owner of every CAD2Revit dialog. A modal dialog locks
    /// Revit until it closes; without an owner it can drop behind Revit (or be minimized
    /// with no taskbar button), so Revit looks frozen and the dialog can't be found.
    /// Owned dialogs always stay on top of Revit.
    /// </summary>
    public sealed class RevitOwner : IWin32Window
    {
        public static IntPtr MainHandle { get; set; }

        public IntPtr Handle { get; }
        RevitOwner(IntPtr handle) { Handle = handle; }

        /// <summary>Owner for WinForms ShowDialog, or null when Revit's window is unknown.</summary>
        public static IWin32Window Win32 => MainHandle == IntPtr.Zero ? null : new RevitOwner(MainHandle);

        /// <summary>Owns a WPF window by Revit and removes its Minimize button.</summary>
        public static void Attach(Window win)
        {
            if (MainHandle != IntPtr.Zero) new WindowInteropHelper(win).Owner = MainHandle;
            win.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(win).Handle;
                SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_MINIMIZEBOX);
            };
            // Belt and braces: never leave the window minimized (Alt+Space > Minimize, Win+Down).
            win.StateChanged += (s, e) =>
            {
                if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            };
        }

        const int GWL_STYLE = -16;
        const int WS_MINIMIZEBOX = 0x20000;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
