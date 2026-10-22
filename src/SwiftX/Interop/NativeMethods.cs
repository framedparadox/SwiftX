using System.Runtime.InteropServices;
using System.Text;

namespace SwiftX.Interop;

/// <summary>
/// Thin Win32 / DWM interop surface. Kept small and focused on what the orbit needs:
/// rounded window corners, tool-window styling, transparent click-through chrome, and
/// cursor probing for placement at the pointer.
/// </summary>
internal static partial class NativeMethods
{
    // ---- DWM window attributes --------------------------------------------
    public const int DWMWA_NCRENDERING_POLICY = 2;
    public const int DWMNCRP_USEWINDOWSTYLE = 0;
    public const int DWMNCRP_DISABLED = 1;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_CAPTION_COLOR = 35;

    /// <summary>
    /// DWM "no custom border color" sentinel for <see cref="DWMWA_BORDER_COLOR"/>.
    /// Pass this instead of a solid COLORREF when a translucent window must not paint a custom rim.
    /// </summary>
    public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    /// <summary>DWM default border-color sentinel for <see cref="DWMWA_BORDER_COLOR"/>.</summary>
    public const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);

    // DWM_WINDOW_CORNER_PREFERENCE
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;       // full ~8px Win11 window radius
    public const int DWMWCP_ROUNDSMALL = 3;  // subtle ~4px radius (menus/tooltips)

    // ---- Background erase suppression (see WindowChrome.MakeTransparentOverlay) ----
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_NCCALCSIZE = 0x0083;
    public const uint WM_NCPAINT = 0x0085;

    public delegate nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nint data);

    [DllImport("comctl32.dll")]
    public static extern nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);

    // ---- GDI regions (round hit-testing region for the orbit) --------------
    [DllImport("gdi32.dll")]
    public static extern nint CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    public static extern nint CreateEllipticRgn(int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    public static extern int CombineRgn(nint dest, nint src1, nint src2, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(nint obj);

    public const int RGN_OR = 2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowRgn(nint hwnd, nint hrgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    // ---- Window Z-order / positioning -------------------------------------
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint hwndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    // ---- Extended window styles (tool window: off taskbar & alt-tab) ------
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_DLGMODALFRAME = 0x00000001;
    public const long WS_EX_WINDOWEDGE = 0x00000100;
    public const long WS_EX_CLIENTEDGE = 0x00000200;
    public const long WS_EX_STATICEDGE = 0x00020000;

    // ---- Window styles (strip the non-client frame / resize border) -------
    public const int GWL_STYLE = -16;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_BORDER = 0x00800000;
    public const long WS_DLGFRAME = 0x00400000;

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_FRAMECHANGED = 0x0020;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint newLong);

    // ---- Cursor position (summon-at-pointer placement) ---------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    // ---- DPI (DIP -> physical pixel conversion for exact placement) -------
    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    // Mouse buttons, read through the same GetAsyncKeyState below. The hot-corner watcher needs
    // the first three: a pointer arriving at a corner with a button held is a drag — a window
    // being thrown at a corner for Windows' own snap, or a selection — not a request to summon
    // the orbit. All five are what MouseTriggerWatcher polls for its own long-press summon.
    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;
    public const int VK_MBUTTON = 0x04;
    public const int VK_XBUTTON1 = 0x05;
    public const int VK_XBUTTON2 = 0x06;

    // Modifier keys, read the same way while capturing a hotkey: XAML's KeyDown reports the
    // pressed key but not which modifiers are held with it, and the Windows key never reaches
    // the app as a KeyDown at all.
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12; // Alt
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    // ---- Shell icon extraction (SHGetFileInfo) -----------------------------
    // A fallback icon source for anything the filesystem says exists. Needed because the
    // WinRT Storage thumbnail pipeline (IconService's primary path, for higher-res icons)
    // flatly refuses to open .lnk shortcuts ("UNABLE_TO_MASK_PATH") and can deny arbitrary
    // paths for an unpackaged app; SHGetFileInfo has neither limitation and, for a .lnk,
    // naturally resolves to the shortcut target's icon with the arrow overlay, matching
    // Explorer. Classic DllImport here (not LibraryImport) since the fixed-size string
    // fields in SHFILEINFO need ByValTStr marshalling.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_LARGEICON = 0x0;
    public const uint SHGFI_SYSICONINDEX = 0x4000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern nint SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint hIcon);

    // ---- Direct 32bpp pixel extraction from an HICON ------------------------
    //
    // GDI+'s System.Drawing.Icon.ToBitmap() mishandles the alpha channel on PNG-compressed icon
    // frames — the format every "jumbo" 256x256 icon resource uses — producing black boxes/opaque
    // halos instead of a clean transparent icon. Pulling the color bitmap's raw BGRA pixels
    // directly via GetIconInfo + GetDIBits sidesteps that conversion entirely (see
    // IconService.ShellIconAsync).

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [DllImport("gdi32.dll")]
    public static extern int GetObject(nint hgdiobj, int cbBuffer, ref BITMAP lpvObject);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    public const uint DIB_RGB_COLORS = 0;
    public const uint BI_RGB = 0;

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(
        nint hdc, nint hbmp, uint uStartScan, uint cScanLines,
        [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hDC);

    // ---- Jumbo (256x256) shell icons (SHGetImageList) ----------------------
    // The crisp, Explorer-matching icon source for IconService: SHGetFileInfo with
    // SHGFI_SYSICONINDEX resolves a path to its index in the shell's system image list, and
    // SHGetImageList(SHIL_JUMBO, ...) hands back that list at its largest (256x256) resolution.
    // This is pure Win32 — no WinRT Storage broker involved — so it works for any path a
    // full-trust process can see, without needing the broadFileSystemAccess capability.
    public const int SHIL_JUMBO = 0x4;
    public const int ILD_TRANSPARENT = 0x1;

    public static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    // The image list is used through its raw COM pointer, not as a managed [ComImport] interface.
    // Declaring it as `out IImageList ppv` makes the CLR build a managed wrapper around the returned
    // pointer (System.StubHelpers.InterfaceMarshaler.ConvertToManaged), and in this app that step
    // fails fast with a stowed E_UNEXPECTED (0xc000027b, exception code 8000ffff): every crash dump
    // from it ends in that call. Nothing about the wrapper is needed, since the only thing ever
    // done with the list is one GetIcon call, so the pointer is taken as-is, GetIcon is called
    // through the interface's vtable, and the reference is released by hand.
    [DllImport("shell32.dll", EntryPoint = "SHGetImageList")]
    public static extern int SHGetImageList(int iImageList, ref Guid riid, out nint ppv);

    /// <summary>
    /// Position of <c>IImageList::GetIcon</c> in the interface's vtable: the three IUnknown methods,
    /// then Add, ReplaceIcon, SetOverlayImage, Replace, AddMasked, Draw and Remove, in the order
    /// commoncontrols.h declares them.
    /// </summary>
    private const int ImageListGetIconSlot = 10;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ImageListGetIconFn(nint self, int index, int flags, out nint hIcon);

    /// <summary>
    /// <c>IImageList::GetIcon</c> on a raw image-list pointer from <see cref="SHGetImageList"/>.
    /// Returns the HRESULT; on success <paramref name="hIcon"/> is an icon the caller must destroy.
    /// </summary>
    public static int ImageListGetIcon(nint imageList, int index, int flags, out nint hIcon)
    {
        nint vtable = Marshal.ReadIntPtr(imageList);
        nint method = Marshal.ReadIntPtr(vtable, ImageListGetIconSlot * nint.Size);
        return Marshal.GetDelegateForFunctionPointer<ImageListGetIconFn>(method)(imageList, index, flags, out hIcon);
    }

    // ---- Hidden helper window (tray callbacks + global hotkeys) ------------
    //
    // Both the notification-area icon and RegisterHotKey deliver their events as window messages,
    // and a WinUI 3 Window exposes no WndProc to receive them. So SwiftX owns one tiny hidden
    // HWND of its own (see Services/MessageWindow.cs) purely as a message sink. It is a real
    // (never-shown) popup rather than an HWND_MESSAGE child, because TrackPopupMenuEx needs an
    // owner that can be made the foreground window or the tray menu won't light-dismiss.

    public delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    public const long WS_POPUP = 0x80000000L;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(
        int exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string message);

    public const uint WM_APP = 0x8000;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_NULL = 0x0000;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_CONTEXTMENU = 0x007B;

    // ---- Notification-area (tray) icon -------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
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
        public nint hBalloonIcon;
    }

    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_SHOWTIP = 0x00000080;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    // ---- Icon loading (the exe's own icon, at the shell's small-icon size) --

    public const uint IMAGE_ICON = 1;
    public const uint LR_DEFAULTCOLOR = 0x0000;
    public const uint LR_SHARED = 0x8000;
    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    /// <summary>Resource id the .NET SDK gives an <c>&lt;ApplicationIcon&gt;</c> (IDI_APPLICATION).</summary>
    public static readonly nint IDI_APPLICATION = 32512;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint LoadImage(nint instance, nint name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(string file, int iconIndex, out nint large, out nint small, uint icons);

    // ---- Native popup menu (the tray context menu) -------------------------
    //
    // A native menu rather than a XAML MenuFlyout: a flyout needs a XamlRoot to hang off, and the
    // tray icon's owner is a plain HWND with no XAML content at all.

    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;

    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenu(nint menu, uint flags, nuint itemId, string? item);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    /// <summary>
    /// Pseudo-handle for <see cref="PostMessage"/> that delivers to every top-level window in the
    /// session. Used by <see cref="Services.SingleInstanceSignal"/> to reach an already-running
    /// SwiftX: the hidden sink's window class name deliberately carries the owning process id
    /// (see <see cref="Services.MessageWindow"/>), so a second instance has no class or title to
    /// hand <c>FindWindow</c> and a broadcast is the only route left. Safe because the message id
    /// itself comes from <see cref="RegisterWindowMessage"/>, so no window that isn't ours can
    /// mistake it for one of its own.
    /// </summary>
    public static readonly nint HWND_BROADCAST = 0xFFFF;

    // ---- Foreground / show (bring the dock to the front) -------------------

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hwnd);

    /// <summary>Hands the foreground right to any process, rather than one named pid.</summary>
    public static readonly uint ASFW_ANY = 0xFFFFFFFF;

    /// <summary>
    /// Lets this process give away its own right to steal the foreground.
    /// <para>
    /// Needed by the second-instance handoff: Windows grants foreground rights to the process the
    /// user just launched from the shell, and denies them to the long-running instance sitting in
    /// the tray. Without this call the running instance's <c>SetForegroundWindow</c> is silently
    /// downgraded to a taskbar flash, and Settings comes up behind whatever the user was looking
    /// at. Calling it here transfers the right the new process legitimately holds to the one that
    /// is actually going to show the window, which is exactly what the API is for.
    /// </para>
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(uint processId);

    // A plain SetForegroundWindow is honored unconditionally only when Windows can see it was
    // triggered by a real input event on the calling thread — a keypress, a mouse click. The hot
    // corner's summon is neither: it fires from a WM_TIMER on the message window's own thread, so
    // Windows' foreground-lock can silently downgrade the request to a flashing taskbar icon
    // instead of true activation. That matters here specifically because "click anywhere to
    // dismiss" (OrbitWindow_Activated) fires on losing activation — a window that was never truly
    // activated can never fire that transition, so a corner-summoned dock would stay stuck on
    // screen through a click elsewhere. AttachThreadInput temporarily shares input state with
    // whichever thread currently owns the foreground, which is the documented, widely-used way to
    // borrow its standing long enough for SetForegroundWindow to actually take.
    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    // ---- Global hotkeys ----------------------------------------------------

    /// <summary>Ask Windows not to auto-repeat a held-down hotkey.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hwnd, int id);

    // ---- Window timers (hot-corner pointer polling) ------------------------
    //
    // WM_TIMER on the same hidden MessageWindow the tray icon and hotkeys already use, rather than
    // a DispatcherQueueTimer: the tick then arrives through the app's own message pump on the UI
    // thread, in the same place as every other shell-integration message.

    public const uint WM_TIMER = 0x0113;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nuint SetTimer(nint hwnd, nuint id, uint intervalMs, nint callback);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool KillTimer(nint hwnd, nuint id);

    // ---- Package identity --------------------------------------------------
    //
    // The one reliable way to ask "am I running from an MSIX package?" without throwing: the
    // WinRT alternative (Package.Current) raises an exception when there is no package, and an
    // exception is a poor answer to a question this is asked at startup. Called with a zero
    // length and no buffer it does no work at all — it only reports which of the two error codes
    // below applies.

    /// <summary>The process has no package identity — i.e. it is a plain unpackaged exe.</summary>
    public const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int GetCurrentPackageFullName(
        ref uint packageFullNameLength, [Out] char[]? packageFullName);

    // ---- Writing a shortcut (.lnk) -----------------------------------------
    //
    // The desktop-shortcut option in Settings (see Services/DesktopShortcutService.cs) has to
    // author a .lnk, and there has never been a managed API for that: the shell's own
    // IShellLinkW + IPersistFile is the only way, the same pair Explorer uses when you drag an
    // app out of Start.
    //
    // Services/ShortcutResolver.cs already declares a cut-down, *read-only* slice of the same two
    // interfaces privately (GetPath and Load, nothing more). That was deliberate there and is
    // left alone: this file is the app's P/Invoke surface, so the full, writable declaration
    // belongs here, and duplicating three vtable slots is cheaper than making the read path
    // depend on the write path. If the two ever need to converge, ShortcutResolver is the one
    // that should move.

    /// <summary>The shell's shortcut object (CLSID_ShellLink). Cast to
    /// <see cref="IShellLinkW"/> to describe the shortcut and <see cref="IPersistFile"/> to write
    /// it out.</summary>
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    public class ShellLink
    {
    }

    /// <summary>
    /// <c>IShellLinkW</c>, declared in full.
    /// <para>
    /// Every slot is present even though only SetPath / SetArguments / SetDescription /
    /// SetWorkingDirectory / SetIconLocation and GetDescription are ever called: COM dispatches by
    /// vtable index, so an omitted or reordered member silently calls the wrong function. The
    /// order below is <c>ShObjIdl_core.h</c>'s, and SetPath really is last.
    /// </para>
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    public interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int fileLength,
            nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int nameLength);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int dirLength);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int argsLength);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
        void Resolve(nint hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    /// <summary>
    /// <c>IPersistFile</c>, far enough down the vtable to reach <c>Save</c>. The first two members
    /// are <c>IPersist</c>'s and <c>IPersistFile</c>'s own, and precede it in the vtable, so they
    /// have to be here even though nothing calls them.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    public interface IPersistFile
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

        /// <param name="remember">Passing true would make this the object's new "current" file.
        /// SwiftX writes once and releases, so false — there is nothing left to remember.</param>
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [MarshalAs(UnmanagedType.Bool)] bool remember);
    }
}
