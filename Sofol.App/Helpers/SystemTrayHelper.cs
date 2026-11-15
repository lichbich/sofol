using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sofol.App.Helpers;

/// <summary>
/// Quản lý biểu tượng khay hệ thống Windows 11 (System Tray) và bắt sự kiện thu nhỏ cửa sổ bằng Win32 Subclassing.
/// </summary>
public sealed class SystemTrayHelper : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmTrayIcon = WmApp + 101;
    private const uint WmSysCommand = 0x0112;
    private const uint ScMinimize = 0xF020;

    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const int NinSelect = 0x0400;
    private const int NinKeySelect = 0x0401;

    private const uint WmNull = 0x0000;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmNonotify = 0x0080;
    private const uint TpmReturnCmd = 0x0100;
    private const uint IdCmdOpenApp = 1001;
    private const uint IdCmdExitApp = 1002;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private const uint IdTrayIcon = 1001;
    private static readonly UIntPtr SubclassId = new(4201);

    private readonly IntPtr _hwnd;
    private readonly SUBCLASSPROC _subclassDelegate;
    private readonly uint _taskbarCreatedMsg;

    private IntPtr _hIcon;
    private bool _iconAdded;
    private bool _disposed;

    public event Action<int, int>? TrayIconClicked;
    public event Action? TrayIconDoubleClicked;
    public event Action? MinimizeRequested;
    public event Action? OpenAppRequested;
    public event Action? ExitAppRequested;
    public event Action? ContextMenuOpening;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetMessagePos();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, [MarshalAs(UnmanagedType.LPWStr)] string? lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, uint fByPos);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    public SystemTrayHelper(IntPtr hwnd, string iconFilePath, string tooltip)
    {
        _hwnd = hwnd;
        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");
        _subclassDelegate = SubclassWindowProc;

        try
        {
            SetPreferredAppMode(1); // AllowDark để menu ngữ cảnh Win32 tự đồng bộ theme Windows 11
            FlushMenuThemes();
        }
        catch
        {
            // Bỏ qua nếu bản Windows không hỗ trợ API không chính thức của uxtheme
        }

        SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, UIntPtr.Zero);
        LoadIcon(iconFilePath);
        AddOrUpdateTrayIcon(tooltip);
    }

    private void LoadIcon(string iconFilePath)
    {
        if (!File.Exists(iconFilePath))
        {
            return;
        }

        _hIcon = LoadImageW(IntPtr.Zero, iconFilePath, ImageIcon, 32, 32, LrLoadFromFile | LrDefaultSize);
    }

    public void UpdateTooltip(string tooltip)
    {
        if (_disposed || !_iconAdded)
        {
            return;
        }

        NOTIFYICONDATA nid = CreateNotifyData(tooltip);
        nid.uFlags = NifTip;
        Shell_NotifyIconW(NimModify, ref nid);
    }

    private void AddOrUpdateTrayIcon(string tooltip)
    {
        if (_disposed)
        {
            return;
        }

        NOTIFYICONDATA nid = CreateNotifyData(tooltip);
        nid.uFlags = NifMessage | NifTip | (_hIcon != IntPtr.Zero ? NifIcon : 0u);
        nid.uCallbackMessage = WmTrayIcon;
        nid.hIcon = _hIcon;

        _iconAdded = Shell_NotifyIconW(NimAdd, ref nid);
        if (_iconAdded)
        {
            nid.uTimeoutOrVersion = 0;
            Shell_NotifyIconW(NimSetVersion, ref nid);
        }
    }

    private NOTIFYICONDATA CreateNotifyData(string tooltip)
    {
        string safeTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = IdTrayIcon,
            szTip = safeTip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private IntPtr SubclassWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScMinimize)
        {
            MinimizeRequested?.Invoke();
            return IntPtr.Zero;
        }

        if (uMsg == WmTrayIcon)
        {
            int mouseMsg = unchecked((int)(lParam.ToInt64() & 0xFFFF));
            if (mouseMsg == WmLButtonUp || mouseMsg == NinSelect || mouseMsg == NinKeySelect)
            {
                POINT pt = GetPointerScreenPoint();
                TrayIconClicked?.Invoke(pt.X, pt.Y);
            }
            else if (mouseMsg == WmRButtonUp || mouseMsg == WmContextMenu)
            {
                POINT pt = GetPointerScreenPoint();
                ShowContextMenu(pt.X, pt.Y);
            }
            else if (mouseMsg == WmLButtonDblClk)
            {
                TrayIconDoubleClicked?.Invoke();
            }

            return IntPtr.Zero;
        }

        if (_taskbarCreatedMsg != 0 && uMsg == _taskbarCreatedMsg)
        {
            AddOrUpdateTrayIcon("Sofol — Tự động sắp xếp tệp thông minh");
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private static POINT GetPointerScreenPoint()
    {
        if (GetCursorPos(out POINT pt))
        {
            return pt;
        }

        uint msgPos = GetMessagePos();
        return new POINT
        {
            X = unchecked((short)(msgPos & 0xFFFF)),
            Y = unchecked((short)((msgPos >> 16) & 0xFFFF))
        };
    }

    private void ShowContextMenu(int x, int y)
    {
        ContextMenuOpening?.Invoke();

        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenuW(hMenu, MfString, new UIntPtr(IdCmdOpenApp), "Mở ứng dụng");
            SetMenuDefaultItem(hMenu, IdCmdOpenApp, 0);
            AppendMenuW(hMenu, MfSeparator, UIntPtr.Zero, null);
            AppendMenuW(hMenu, MfString, new UIntPtr(IdCmdExitApp), "Đóng ứng dụng");

            SetForegroundWindow(_hwnd);
            uint cmd = TrackPopupMenuEx(
                hMenu,
                TpmReturnCmd | TpmNonotify | TpmRightButton | TpmBottomAlign,
                x,
                y,
                _hwnd,
                IntPtr.Zero);
            PostMessageW(_hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (cmd == IdCmdOpenApp)
            {
                OpenAppRequested?.Invoke();
            }
            else if (cmd == IdCmdExitApp)
            {
                ExitAppRequested?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId);

        if (_iconAdded)
        {
            NOTIFYICONDATA nid = CreateNotifyData(string.Empty);
            Shell_NotifyIconW(NimDelete, ref nid);
            _iconAdded = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
