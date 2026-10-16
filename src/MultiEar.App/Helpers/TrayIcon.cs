using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace MultiEar_App.Helpers
{
    public class TrayIcon : IDisposable
    {
        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        private const int WM_USER = 0x0400;
        public const int WM_TRAYICON = WM_USER + 101;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersionOrTimeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_HIDE = 0;
        private const int SW_RESTORE = 9;

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

        private readonly IntPtr _hwnd;
        private readonly SubclassProc _subclassProc;
        private NOTIFYICONDATA _nid;
        private bool _isCreated;

        public event Action? TrayClicked;

        public TrayIcon(Window window, string tooltip = "MultiEar — Bluetooth Audio Sync")
        {
            _hwnd = WindowNative.GetWindowHandle(window);
            _subclassProc = WindowSubclassHandler;

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = LoadDefaultIcon(),
                szTip = tooltip
            };

            _isCreated = Shell_NotifyIcon(NIM_ADD, ref _nid);
            SetWindowSubclass(_hwnd, _subclassProc, 1001, IntPtr.Zero);
        }

        private IntPtr WindowSubclassHandler(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_TRAYICON)
            {
                int msg = lParam.ToInt32();
                if (msg == WM_LBUTTONUP || msg == WM_RBUTTONUP)
                {
                    TrayClicked?.Invoke();
                    ShowWindow();
                }
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private static IntPtr LoadDefaultIcon()
        {
            const int IDI_APPLICATION = 32512;
            return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        }

        public void ShowWindow()
        {
            ShowWindow(_hwnd, SW_RESTORE);
            SetForegroundWindow(_hwnd);
        }

        public void HideWindow()
        {
            ShowWindow(_hwnd, SW_HIDE);
        }

        public void UpdateTooltip(string text)
        {
            if (!_isCreated) return;
            _nid.szTip = text.Length > 127 ? text.Substring(0, 127) : text;
            _nid.uFlags = NIF_TIP;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }

        public void Dispose()
        {
            if (_isCreated)
            {
                RemoveWindowSubclass(_hwnd, _subclassProc, 1001);
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _isCreated = false;
            }
        }
    }
}
