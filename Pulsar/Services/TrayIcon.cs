using System.Runtime.InteropServices;

namespace Pulsar.Services;

/// <summary>
/// A notification-area (system tray) icon with a right-click menu, implemented directly on Shell_NotifyIcon.
/// Messages arrive through a subclass of the main window, so no extra window or package is needed.
/// </summary>
public sealed partial class TrayIcon : IDisposable
{
    private const int WM_NULL = 0x0000;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int CallbackMessage = 0x8000 + 0x5A; // WM_APP + n

    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;
    private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const int SM_CXSMICON = 49, SM_CYSMICON = 50;

    private readonly nint _hwnd;
    private readonly SubclassProc _proc; // kept alive for the lifetime of the subclass
    private readonly uint _taskbarCreated;
    private readonly List<(string Text, Action? Action)> _menu = [];
    private nint _icon;
    private bool _added;
    private bool _disposed;

    public event Action? Activated;

    public TrayIcon(nint hwnd, string iconPath, string tooltip)
    {
        _hwnd = hwnd;
        _icon = LoadImage(0, iconPath, IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _proc = WndProc;
        SetWindowSubclass(_hwnd, _proc, 0x5055, 0);
        Tooltip = tooltip;
        Add();
    }

    public string Tooltip { get; set; }

    /// <summary>Adds a menu item; pass null text for a separator.</summary>
    public void AddMenuItem(string? text, Action? action = null) => _menu.Add((text ?? "-", action));

    private void Add()
    {
        var data = CreateData();
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = Tooltip.Length > 127 ? Tooltip[..127] : Tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (msg == CallbackMessage)
        {
            int mouse = (int)(lParam.ToInt64() & 0xFFFF);
            switch (mouse)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    Activated?.Invoke();
                    break;
                case WM_RBUTTONUP:
                case WM_CONTEXTMENU:
                    ShowMenu();
                    break;
            }
            return 0;
        }
        if (msg == _taskbarCreated && !_disposed)
        {
            Add(); // Explorer restarted; put the icon back
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        try
        {
            for (int i = 0; i < _menu.Count; i++)
            {
                if (_menu[i].Text == "-") AppendMenu(menu, MF_SEPARATOR, 0, null);
                else AppendMenu(menu, MF_STRING, (nuint)(i + 1), _menu[i].Text);
            }

            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd); // required so the menu closes when you click elsewhere
            int cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, 0);
            PostMessage(_hwnd, WM_NULL, 0, 0);
            if (cmd > 0 && cmd <= _menu.Count) _menu[cmd - 1].Action?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added)
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
        }
        RemoveWindowSubclass(_hwnd, _proc, 0x5055);
        if (_icon != 0) { DestroyIcon(_icon); _icon = 0; }
    }

    // ---------- Win32 ----------

    private delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATAW data);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint id, nuint refData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc proc, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(nint hwnd, int msg, nint wParam, nint lParam);
}
