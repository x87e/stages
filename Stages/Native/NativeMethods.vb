' ============================================================================
'  NativeMethods.vb
'  Every raw Win32 declaration Stages uses, gathered in one place.
'
'  VB note for readers new to P/Invoke:
'    <DllImport("user32.dll")> on a "Shared Function ... End Function" with an
'    EMPTY body tells .NET "this function lives in that Windows DLL - call it
'    there". The parameter types must line up with the C signature.
' ============================================================================
Imports System.Runtime.InteropServices

Namespace Native

    ''' <summary>
    ''' Win32 constants, structures and function imports.
    ''' "Friend" = visible anywhere inside Stages.exe but not outside it.
    ''' </summary>
    Friend NotInheritable Class NativeMethods

        ' A class of only Shared members - prevent anyone creating an instance.
        Private Sub New()
        End Sub

#Region "Window messages (WM_*)"
        ''' <summary>Sent when a window's size/position/z-order is ABOUT to change.</summary>
        Public Const WM_WINDOWPOSCHANGING As Integer = &H46
        ''' <summary>Asks the window "what part of you is under the mouse?" (caption, border, client...).</summary>
        Public Const WM_NCHITTEST As Integer = &H84
        ''' <summary>System menu / window-command message (maximise, minimise, close, move...).</summary>
        Public Const WM_SYSCOMMAND As Integer = &H112
        ''' <summary>A system setting changed (e.g. transparency effects switched on/off).</summary>
        Public Const WM_SETTINGCHANGE As Integer = &H1A
        ''' <summary>The mouse left the non-client (border) area of the window.</summary>
        Public Const WM_NCMOUSELEAVE As Integer = &H2A2
        ''' <summary>Owner-drawn menu item needs painting (shell context menu sub-menus).</summary>
        Public Const WM_DRAWITEM As Integer = &H2B
        ''' <summary>Owner-drawn menu item needs measuring (shell context menu sub-menus).</summary>
        Public Const WM_MEASUREITEM As Integer = &H2C
        ''' <summary>A popup sub-menu is about to open (lets "Send to"/"Open with" fill themselves in).</summary>
        Public Const WM_INITMENUPOPUP As Integer = &H117
        ''' <summary>User pressed a key that doesn't match a menu mnemonic.</summary>
        Public Const WM_MENUCHAR As Integer = &H120
        ''' <summary>A menu or toolbar command; used to send Explorer's "Show desktop icons" command.</summary>
        Public Const WM_COMMAND As Integer = &H111
        ''' <summary>"Do nothing" message. Posted after a tray menu closes so it dismisses properly next time (Microsoft KB135788).</summary>
        Public Const WM_NULL As Integer = &H0

        ''' <summary>Name of the message Explorer broadcasts every time it (re)creates the taskbar.</summary>
        Public Const TaskbarCreatedMessageName As String = "TaskbarCreated"
#End Region

#Region "WM_SYSCOMMAND command ids (SC_*)"
        ''' <summary>The low 4 bits of an SC_ value are used internally by Windows; mask them off before comparing.</summary>
        Public Const SC_COMMAND_MASK As Integer = &HFFF0
        Public Const SC_MINIMIZE As Integer = &HF020
        Public Const SC_MAXIMIZE As Integer = &HF030
#End Region

#Region "Hit-test results (HT*) - returned from WM_NCHITTEST"
        Public Const HTLEFT As Integer = 10
        Public Const HTRIGHT As Integer = 11
        Public Const HTTOP As Integer = 12
        Public Const HTTOPLEFT As Integer = 13
        Public Const HTTOPRIGHT As Integer = 14
        Public Const HTBOTTOM As Integer = 15
        Public Const HTBOTTOMLEFT As Integer = 16
        Public Const HTBOTTOMRIGHT As Integer = 17
#End Region

#Region "SetWindowPos flags and special z-order handles"
        Public Const SWP_NOSIZE As UInteger = &H1UI
        Public Const SWP_NOMOVE As UInteger = &H2UI
        Public Const SWP_NOZORDER As UInteger = &H4UI
        Public Const SWP_NOACTIVATE As UInteger = &H10UI

        ''' <summary>"Put this window at the very bottom of the z-order".</summary>
        Public Shared ReadOnly HWND_BOTTOM As New IntPtr(1)
#End Region

#Region "Window long indexes and extended styles"
        ''' <summary>GetWindowLongPtr index for the extended window style bits.</summary>
        Public Const GWL_EXSTYLE As Integer = -20
        ''' <summary>GetWindowLongPtr index for a top-level window's OWNER window.</summary>
        Public Const GWLP_HWNDPARENT As Integer = -8

        ''' <summary>Tool windows don't appear in the taskbar or the Alt+Tab list.</summary>
        Public Const WS_EX_TOOLWINDOW As Long = &H80L
        ''' <summary>Forces a window INTO the taskbar - we make sure it is off.</summary>
        Public Const WS_EX_APPWINDOW As Long = &H40000L
#End Region

#Region "Virtual key codes"
        ''' <summary>Virtual-key code of the left mouse button.</summary>
        Public Const VK_LBUTTON As Integer = &H1
        ''' <summary>GetAsyncKeyState sets this bit when the key/button is currently held down.</summary>
        Public Const KEY_IS_DOWN_FLAG As Integer = &H8000
#End Region

#Region "ShowWindow commands"
        ''' <summary>Show the window in its normal state without stealing focus.</summary>
        Public Const SW_SHOWNOACTIVATE As Integer = 4
        ''' <summary>Normal "show" used when invoking shell verbs.</summary>
        Public Const SW_SHOWNORMAL As Integer = 1
#End Region

#Region "Blur-behind (undocumented but long-stable Windows 10/11 API)"
        ''' <summary>SetWindowCompositionAttribute attribute id for the accent (blur) policy.</summary>
        Public Const WCA_ACCENT_POLICY As Integer = 19
        ''' <summary>Accent state: show the window's own transparency over a (here fully clear) colour, no blur.</summary>
        Public Const ACCENT_ENABLE_TRANSPARENTGRADIENT As Integer = 2
        ''' <summary>Accent state: blur whatever is behind the window (the classic NoFences look).</summary>
        Public Const ACCENT_ENABLE_BLURBEHIND As Integer = 3
        ' (ACCENT_ENABLE_ACRYLICBLURBEHIND = 4 exists too, but renders black on Windows 11,
        '  so Stages imitates acrylic itself - see AcrylicNoise.vb.)
        ''' <summary>AccentFlags bit: use our GradientColor instead of the Windows accent colour.</summary>
        Public Const ACCENT_FLAG_USE_GRADIENT_COLOR As Integer = 2
#End Region

#Region "Shell icon flags (SHGetFileInfo)"
        Public Const SHGFI_ICON As UInteger = &H100UI
        Public Const SHGFI_LARGEICON As UInteger = &H0UI
        ''' <summary>Ask for the 16x16 icon instead of the 32x32 one.</summary>
        Public Const SHGFI_SMALLICON As UInteger = &H1UI
        ''' <summary>Fill in szTypeName ("Microsoft Word Document", "File folder"...).</summary>
        Public Const SHGFI_TYPENAME As UInteger = &H400UI
        ''' <summary>Fill in iIcon: the icon's position in Windows' system icon list (used for 48px icons).</summary>
        Public Const SHGFI_SYSICONINDEX As UInteger = &H4000UI
        ''' <summary>Pixel size of an "extra large" shell icon (Large icons view).</summary>
        Public Const EXTRA_LARGE_ICON_SIZE As Integer = 48
        ''' <summary>SHGetImageList: the list holding the 48x48 icons.</summary>
        Public Const SHIL_EXTRALARGE As Integer = 2
        ''' <summary>IImageList.GetIcon: keep the icon's transparency.</summary>
        Public Const ILD_TRANSPARENT As Integer = &H1
        Public Shared ReadOnly IID_IImageList As New Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")
        ''' <summary>Don't look at the actual file: give the generic icon for this name/extension and the attributes passed in.</summary>
        Public Const SHGFI_USEFILEATTRIBUTES As UInteger = &H10UI
        Public Const FILE_ATTRIBUTE_DIRECTORY As UInteger = &H10UI
        Public Const FILE_ATTRIBUTE_NORMAL As UInteger = &H80UI
        ''' <summary>Pixel size of a "large" shell icon.</summary>
        Public Const LARGE_ICON_SIZE As Integer = 32
        ''' <summary>Pixel size of a "small" shell icon.</summary>
        Public Const SMALL_ICON_SIZE As Integer = 16
        ' Note: SHGFI_ADDOVERLAYS (&H20) / SHGFI_LINKOVERLAY (&H8000) would add the
        ' shortcut arrow. Stages deliberately passes neither, so Stage items show
        ' clean icons.
        Public Const MAX_PATH As Integer = 260
        Public Const SHFILEINFO_TYPENAME_LENGTH As Integer = 80
#End Region

#Region "Shell context menu constants"
        Public Const S_OK As Integer = 0

        ''' <summary>First command id we let the shell use in its menu (0 means "nothing chosen").</summary>
        Public Const CONTEXT_MENU_FIRST_COMMAND_ID As UInteger = 1UI
        ''' <summary>Last command id we let the shell use.</summary>
        Public Const CONTEXT_MENU_LAST_COMMAND_ID As UInteger = &H7FFFUI

        ' IContextMenu::QueryContextMenu flags
        Public Const CMF_NORMAL As UInteger = &H0UI
        Public Const CMF_EXPLORE As UInteger = &H4UI
        ''' <summary>Shift+right-click "extended" verbs (e.g. "Copy as path").</summary>
        Public Const CMF_EXTENDEDVERBS As UInteger = &H100UI

        ' CMINVOKECOMMANDINFOEX.fMask flags
        Public Const CMIC_MASK_UNICODE As Integer = &H4000
        Public Const CMIC_MASK_SHIFT_DOWN As Integer = &H10000000
        Public Const CMIC_MASK_PTINVOKE As Integer = &H20000000
        Public Const CMIC_MASK_CONTROL_DOWN As Integer = &H40000000

        ' TrackPopupMenuEx flags
        ''' <summary>Return the chosen command id instead of posting WM_COMMAND.</summary>
        Public Const TPM_RETURNCMD As UInteger = &H100UI
        ''' <summary>Allow the right mouse button to pick items too.</summary>
        Public Const TPM_RIGHTBUTTON As UInteger = &H2UI

        ''' <summary>Command ids from here up are Stages' own items added to the Explorer menu.</summary>
        Public Const CUSTOM_COMMAND_FIRST_ID As UInteger = &H8000UI

        ''' <summary>IContextMenu::GetCommandString flag: return the canonical verb ("delete", "open"...) as Unicode.</summary>
        Public Const GCS_VERBW As UInteger = &H4UI

        ' Menu item flags (AppendMenu / GetMenuState / DeleteMenu)
        Public Const MF_STRING As Integer = &H0
        Public Const MF_POPUP As Integer = &H10
        Public Const MF_SEPARATOR As Integer = &H800
        ''' <summary>Greyed out and can't be chosen (normal, themed - unlike owner-drawn items).</summary>
        Public Const MF_GRAYED As Integer = &H1
        ''' <summary>Show a tick next to the menu item.</summary>
        Public Const MF_CHECKED As Integer = &H8
        ''' <summary>SetMenuDefaultItem: the item is given by its command id, not its position.</summary>
        Public Const MENU_ITEM_BY_COMMAND As UInteger = 0UI
        ''' <summary>The position argument is a 0-based index, not a command id.</summary>
        Public Const MF_BYPOSITION As UInteger = &H400UI
        ''' <summary>GetMenuState / GetMenuItemID return -1 on failure or for sub-menus.</summary>
        Public Const MENU_LOOKUP_FAILED As Integer = -1

        ' COM interface ids
        Public Shared ReadOnly IID_IShellFolder As New Guid("000214E6-0000-0000-C000-000000000046")
        Public Shared ReadOnly IID_IContextMenu As New Guid("000214E4-0000-0000-C000-000000000046")
#End Region

#Region "Structures"
        ''' <summary>A point in physical screen pixels (Win32 POINT).</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure NativePoint
            Public X As Integer
            Public Y As Integer
        End Structure

        ''' <summary>A rectangle in physical screen pixels (Win32 RECT).</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure NativeRect
            Public Left As Integer
            Public Top As Integer
            Public Right As Integer
            Public Bottom As Integer
        End Structure

        ''' <summary>Payload of WM_WINDOWPOSCHANGING - we edit it to keep Stages at the bottom.</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure WINDOWPOS
            Public hwnd As IntPtr
            Public hwndInsertAfter As IntPtr
            Public x As Integer
            Public y As Integer
            Public cx As Integer
            Public cy As Integer
            Public flags As UInteger
        End Structure

        ''' <summary>Describes the blur/accent effect for SetWindowCompositionAttribute.</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure AccentPolicy
            Public AccentState As Integer
            Public AccentFlags As Integer
            Public GradientColor As Integer
            Public AnimationId As Integer
        End Structure

        ''' <summary>Wrapper passed to SetWindowCompositionAttribute.</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure WindowCompositionAttributeData
            Public Attribute As Integer
            Public Data As IntPtr
            Public SizeOfData As Integer
        End Structure

        ''' <summary>Result of SHGetFileInfo - we only use hIcon.</summary>
        <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
        Public Structure SHFILEINFO
            Public hIcon As IntPtr
            Public iIcon As Integer
            Public dwAttributes As UInteger
            <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=MAX_PATH)>
            Public szDisplayName As String
            <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=SHFILEINFO_TYPENAME_LENGTH)>
            Public szTypeName As String
        End Structure

        ''' <summary>Tells a shell context menu which command to run and how.</summary>
        <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
        Public Structure CMINVOKECOMMANDINFOEX
            Public cbSize As Integer
            Public fMask As Integer
            Public hwnd As IntPtr
            Public lpVerb As IntPtr
            <MarshalAs(UnmanagedType.LPStr)> Public lpParameters As String
            <MarshalAs(UnmanagedType.LPStr)> Public lpDirectory As String
            Public nShow As Integer
            Public dwHotKey As Integer
            Public hIcon As IntPtr
            <MarshalAs(UnmanagedType.LPStr)> Public lpTitle As String
            Public lpVerbW As IntPtr
            <MarshalAs(UnmanagedType.LPWStr)> Public lpParametersW As String
            <MarshalAs(UnmanagedType.LPWStr)> Public lpDirectoryW As String
            <MarshalAs(UnmanagedType.LPWStr)> Public lpTitleW As String
            Public ptInvoke As NativePoint
        End Structure
#End Region

#Region "user32.dll - windows"
        <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
        Public Shared Function FindWindow(lpClassName As String, lpWindowName As String) As IntPtr
        End Function

        ''' <summary>
        ''' Finds a window by class name, either a child of <paramref name="hwndParent"/> or (with
        ''' IntPtr.Zero) a top-level window. Pass the previous result as <paramref name="hwndChildAfter"/>
        ''' to continue the search after it.
        ''' </summary>
        <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
        Public Shared Function FindWindowEx(hwndParent As IntPtr, hwndChildAfter As IntPtr,
                                            lpszClass As String, lpszWindow As String) As IntPtr
        End Function

        ''' <summary>Puts a message in another window's queue and returns at once (never waits for Explorer).</summary>
        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function PostMessage(hWnd As IntPtr, Msg As Integer, wParam As IntPtr, lParam As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function SetWindowPos(hWnd As IntPtr, hWndInsertAfter As IntPtr, x As Integer, y As Integer,
                                            cx As Integer, cy As Integer, uFlags As UInteger) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function IsWindow(hWnd As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function IsWindowVisible(hWnd As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
        End Function

        ''' <summary>
        ''' Lets another process bring its window to the front. Windows normally only lets the
        ''' program the user is working with do that; a second copy of Stages (just started by
        ''' the user, so allowed) passes the permission on to the copy already running, so its
        ''' Settings window opens in front rather than flashing on the taskbar.
        ''' </summary>
        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function AllowSetForegroundWindow(dwProcessId As Integer) As Boolean
        End Function

        ''' <summary>AllowSetForegroundWindow: any process may take the foreground (we don't know the other copy's id).</summary>
        Public Const ASFW_ANY As Integer = -1

        <DllImport("user32.dll")>
        Public Shared Function GetCursorPos(ByRef lpPoint As NativePoint) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function GetWindowRect(hWnd As IntPtr, ByRef lpRect As NativeRect) As Boolean
        End Function

        ''' <summary>Physical state of a key or mouse button right now (high bit set = held down).</summary>
        <DllImport("user32.dll")>
        Public Shared Function GetAsyncKeyState(vKey As Integer) As Short
        End Function

        <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
        Public Shared Function RegisterWindowMessage(lpString As String) As UInteger
        End Function

        <DllImport("user32.dll")>
        Public Shared Function SetWindowCompositionAttribute(hwnd As IntPtr, ByRef data As WindowCompositionAttributeData) As Integer
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function DestroyIcon(hIcon As IntPtr) As Boolean
        End Function

        ' --- GetWindowLongPtr / SetWindowLongPtr ---------------------------------
        ' On 64-bit Windows these are real exports; on 32-bit only the
        ' non-"Ptr" versions exist. The wrappers below pick the right one.
        ' (The original NoFences always called the 32-bit version, which truncates
        '  values on 64-bit Windows.)

        <DllImport("user32.dll", EntryPoint:="GetWindowLongPtrW", SetLastError:=True)>
        Private Shared Function GetWindowLongPtr64(hWnd As IntPtr, nIndex As Integer) As IntPtr
        End Function

        <DllImport("user32.dll", EntryPoint:="GetWindowLongW", SetLastError:=True)>
        Private Shared Function GetWindowLong32(hWnd As IntPtr, nIndex As Integer) As Integer
        End Function

        <DllImport("user32.dll", EntryPoint:="SetWindowLongPtrW", SetLastError:=True)>
        Private Shared Function SetWindowLongPtr64(hWnd As IntPtr, nIndex As Integer, dwNewLong As IntPtr) As IntPtr
        End Function

        <DllImport("user32.dll", EntryPoint:="SetWindowLongW", SetLastError:=True)>
        Private Shared Function SetWindowLong32(hWnd As IntPtr, nIndex As Integer, dwNewLong As Integer) As Integer
        End Function

        ''' <summary>Reads a window "long" value, correctly on both 32- and 64-bit Windows.</summary>
        Public Shared Function GetWindowLongPtr(hWnd As IntPtr, nIndex As Integer) As IntPtr
            Const SixtyFourBitPointerSize As Integer = 8
            If IntPtr.Size = SixtyFourBitPointerSize Then
                Return GetWindowLongPtr64(hWnd, nIndex)
            End If
            Return New IntPtr(GetWindowLong32(hWnd, nIndex))
        End Function

        ''' <summary>Writes a window "long" value, correctly on both 32- and 64-bit Windows.</summary>
        Public Shared Function SetWindowLongPtr(hWnd As IntPtr, nIndex As Integer, dwNewLong As IntPtr) As IntPtr
            Const SixtyFourBitPointerSize As Integer = 8
            If IntPtr.Size = SixtyFourBitPointerSize Then
                Return SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            End If
            Return New IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()))
        End Function
#End Region

#Region "user32.dll - menus"
        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function CreatePopupMenu() As IntPtr
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function DestroyMenu(hMenu As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
        Public Shared Function AppendMenu(hMenu As IntPtr, uFlags As UInteger, uIDNewItem As UIntPtr, lpNewItem As String) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function DeleteMenu(hMenu As IntPtr, uPosition As UInteger, uFlags As UInteger) As Boolean
        End Function

        <DllImport("user32.dll")>
        Public Shared Function GetMenuItemCount(hMenu As IntPtr) As Integer
        End Function

        ''' <summary>Command id of the item at a position (-1 for sub-menus, 0 for separators).</summary>
        <DllImport("user32.dll")>
        Public Shared Function GetMenuItemID(hMenu As IntPtr, nPos As Integer) As Integer
        End Function

        ''' <summary>Flags of a menu item (MF_SEPARATOR, MF_POPUP...), or -1 if it doesn't exist.</summary>
        <DllImport("user32.dll")>
        Public Shared Function GetMenuState(hMenu As IntPtr, uId As UInteger, uFlags As UInteger) As Integer
        End Function

        ''' <summary>Shows a popup menu and (with TPM_RETURNCMD) returns the chosen command id, or 0.</summary>
        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function TrackPopupMenuEx(hMenu As IntPtr, uFlags As UInteger, x As Integer, y As Integer,
                                                hWnd As IntPtr, lptpm As IntPtr) As Integer
        End Function

        ''' <summary>Makes one menu item the bold "default" entry (what double-click does, by Windows convention).</summary>
        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function SetMenuDefaultItem(hMenu As IntPtr, uItem As UInteger, fByPos As UInteger) As Boolean
        End Function
#End Region

#Region "Light / dark mode (uxtheme.dll, dwmapi.dll)"
        ' --- Menus ------------------------------------------------------------------
        ' Windows has no documented way for an ordinary desktop program to ask for dark
        ' popup menus. These two functions are exported from uxtheme.dll by NUMBER only
        ' ("#135"), have existed since Windows 10 1903 (build 18362), and are what Notepad++,
        ' Windows Terminal and others use. They affect every menu this process shows,
        ' including the Explorer menu on an item. Callers must check the Windows build first
        ' and treat any failure as "menus stay light".

        ''' <summary>SetPreferredAppMode values.</summary>
        Public Const PREFERRED_APP_MODE_FORCE_DARK As Integer = 2
        Public Const PREFERRED_APP_MODE_FORCE_LIGHT As Integer = 3
        ''' <summary>First Windows build where uxtheme ordinal 135 is SetPreferredAppMode (Windows 10 1903).</summary>
        Public Const PREFERRED_APP_MODE_MINIMUM_BUILD As Integer = 18362

        ''' <summary>Undocumented: chooses light or dark for this process's menus. Returns the previous mode.</summary>
        <DllImport("uxtheme.dll", EntryPoint:="#135", SetLastError:=False)>
        Public Shared Function SetPreferredAppMode(appMode As Integer) As Integer
        End Function

        ''' <summary>Undocumented: makes the next menus shown pick up the mode set above.</summary>
        <DllImport("uxtheme.dll", EntryPoint:="#136", SetLastError:=False)>
        Public Shared Sub FlushMenuThemes()
        End Sub

        ' --- Title bars (documented) ---------------------------------------------------
        ''' <summary>DwmSetWindowAttribute: dark title bar on/off (Windows 10 20H1 / build 18985 and later, and Windows 11).</summary>
        Public Const DWMWA_USE_IMMERSIVE_DARK_MODE As Integer = 20
        ''' <summary>The same attribute's number on Windows 10 1809-1909, before it was made official.</summary>
        Public Const DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 As Integer = 19
        ''' <summary>First build that uses attribute 20 rather than 19.</summary>
        Public Const IMMERSIVE_DARK_MODE_ATTRIBUTE_20_BUILD As Integer = 18985
        ''' <summary>First build with a dark title bar at all (Windows 10 1809).</summary>
        Public Const IMMERSIVE_DARK_MODE_MINIMUM_BUILD As Integer = 17763
        ''' <summary>SetWindowPos: redraw the frame (used to repaint the title bar after switching).</summary>
        Public Const SWP_FRAMECHANGED As UInteger = &H20UI

        ''' <summary>DwmSetWindowAttribute: a window's corner shape (Windows 11 only).</summary>
        Public Const DWMWA_WINDOW_CORNER_PREFERENCE As Integer = 33
        ''' <summary>Corner preference values: square, or rounded like other Windows 11 windows.</summary>
        Public Const DWMWCP_DONOTROUND As Integer = 1
        Public Const DWMWCP_ROUND As Integer = 2
        ''' <summary>The first Windows 11 build (corner preferences exist from here).</summary>
        Public Const WINDOWS_11_FIRST_BUILD As Integer = 22000

        <DllImport("dwmapi.dll")>
        Public Shared Function DwmSetWindowAttribute(hwnd As IntPtr, dwAttribute As Integer, ByRef pvAttribute As Integer,
                                                     cbAttribute As Integer) As Integer
        End Function
#End Region

#Region "Sorting like Explorer (shlwapi.dll)"
        ''' <summary>
        ''' Compares two names the way Explorer sorts them: numbers by value, so "File 2" comes
        ''' before "File 10". Returns negative, zero or positive, like String.Compare.
        ''' </summary>
        <DllImport("shlwapi.dll", CharSet:=CharSet.Unicode)>
        Public Shared Function StrCmpLogicalW(psz1 As String, psz2 As String) As Integer
        End Function
#End Region

#Region "Global keyboard shortcut (user32.dll)"
        ''' <summary>Sent to the window that registered a hotkey when it is pressed anywhere in Windows.</summary>
        Public Const WM_HOTKEY As Integer = &H312
        ''' <summary>RegisterHotKey modifier flags.</summary>
        Public Const MOD_ALT As UInteger = &H1UI
        Public Const MOD_CONTROL As UInteger = &H2UI
        Public Const MOD_SHIFT As UInteger = &H4UI
        Public Const MOD_WIN As UInteger = &H8UI
        ''' <summary>Holding the keys down doesn't repeat the hotkey (Windows 7 and later).</summary>
        Public Const MOD_NOREPEAT As UInteger = &H4000UI

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function RegisterHotKey(hWnd As IntPtr, id As Integer, fsModifiers As UInteger, vk As UInteger) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function UnregisterHotKey(hWnd As IntPtr, id As Integer) As Boolean
        End Function
#End Region

#Region "Shortcut icons and icon-change notifications (shell32.dll, kernel32.dll)"
        ''' <summary>
        ''' Windows' own "Change Icon" dialog (the one in a shortcut's Properties). On entry the
        ''' buffer holds the file to list icons from and piIconIndex the icon to pre-select; on OK
        ''' they hold the user's choice. Returns non-zero for OK, 0 for Cancel. Documented; exported
        ''' by name on Windows 10/11. (On very old Windows it was only exported as number 62 -
        ''' PickIconDlgByNumber below is the fallback for that.)
        ''' </summary>
        <DllImport("shell32.dll", EntryPoint:="PickIconDlg", CharSet:=CharSet.Unicode)>
        Public Shared Function PickIconDlg(hwnd As IntPtr, pszIconPath As System.Text.StringBuilder, cchIconPath As UInteger,
                                           ByRef piIconIndex As Integer) As Integer
        End Function

        <DllImport("shell32.dll", EntryPoint:="#62", CharSet:=CharSet.Unicode)>
        Public Shared Function PickIconDlgByNumber(hwnd As IntPtr, pszIconPath As System.Text.StringBuilder, cchIconPath As UInteger,
                                                   ByRef piIconIndex As Integer) As Integer
        End Function

        ''' <summary>Writes (or, with a Nothing value, deletes) one "key=value" line of an INI-style file such as a .url.</summary>
        <DllImport("kernel32.dll", EntryPoint:="WritePrivateProfileStringW", CharSet:=CharSet.Unicode, SetLastError:=True)>
        Public Shared Function WritePrivateProfileString(lpAppName As String, lpKeyName As String, lpString As String,
                                                         lpFileName As String) As Boolean
        End Function

        ' --- Being told when Windows' icons change ----------------------------------------
        ''' <summary>SHChangeNotifyRegister sources: changes made by the file system and by the shell.</summary>
        Public Const SHCNRF_INTERRUPT_LEVEL As Integer = &H1
        Public Const SHCNRF_SHELL_LEVEL As Integer = &H2
        ''' <summary>Event: a file type's association (and so possibly its icon) changed. Icon packs send this.</summary>
        Public Const SHCNE_ASSOCCHANGED As Integer = &H8000000
        ''' <summary>Event: an icon in Windows' system icon cache was replaced.</summary>
        Public Const SHCNE_UPDATEIMAGE As Integer = &H8000
        ''' <summary>Folder number of the desktop (the top of the shell namespace) for SHGetSpecialFolderLocation.</summary>
        Public Const CSIDL_DESKTOP As Integer = &H0
        ''' <summary>Start of the message numbers a program may use for its own purposes.</summary>
        Public Const WM_APP As Integer = &H8000

        ''' <summary>What to watch: one shell location, and whether to include everything beneath it.</summary>
        <StructLayout(LayoutKind.Sequential)>
        Public Structure SHChangeNotifyEntry
            Public pidl As IntPtr
            <MarshalAs(UnmanagedType.Bool)> Public fRecursive As Boolean
        End Structure

        ''' <summary>
        ''' Asks the shell to post message <paramref name="wMsg"/> to <paramref name="hwnd"/> whenever one
        ''' of <paramref name="fEvents"/> happens. Returns a registration number (0 = failed).
        ''' </summary>
        <DllImport("shell32.dll", SetLastError:=True)>
        Public Shared Function SHChangeNotifyRegister(hwnd As IntPtr, fSources As Integer, fEvents As Integer, wMsg As Integer,
                                                      cEntries As Integer, ByRef pshcne As SHChangeNotifyEntry) As UInteger
        End Function

        <DllImport("shell32.dll")>
        Public Shared Function SHChangeNotifyDeregister(ulID As UInteger) As Boolean
        End Function

        ''' <summary>
        ''' Converts a shell id (PIDL) into a file system path. Returns False for things that
        ''' aren't files or folders (e.g. a Store app). The buffer must hold MAX_PATH characters.
        ''' </summary>
        <DllImport("shell32.dll", EntryPoint:="SHGetPathFromIDListW", CharSet:=CharSet.Unicode)>
        Public Shared Function SHGetPathFromIDList(pidl As IntPtr, pszPath As System.Text.StringBuilder) As Boolean
        End Function

        ''' <summary>Gets the shell id (PIDL) of a special folder; free it with Marshal.FreeCoTaskMem.</summary>
        <DllImport("shell32.dll")>
        Public Shared Function SHGetSpecialFolderLocation(hwndOwner As IntPtr, nFolder As Integer, ByRef ppidl As IntPtr) As Integer
        End Function
#End Region

#Region "shell32.dll"
        ''' <summary>
        ''' Gets one of Windows' system icon lists (e.g. SHIL_EXTRALARGE, the 48px icons) as an
        ''' IImageList COM object. Used with SHGetFileInfo's SHGFI_SYSICONINDEX.
        ''' </summary>
        <DllImport("shell32.dll", EntryPoint:="SHGetImageList")>
        Public Shared Function SHGetImageList(iImageList As Integer, ByRef riid As Guid, ByRef ppv As IntPtr) As Integer
        End Function

        ''' <summary>
        ''' Extracts an icon from a file by index (or by resource id when negative) - the same
        ''' way Explorer reads a shortcut's custom icon. nIconSize packs both sizes:
        ''' low 16 bits = large size, high 16 bits = small size. Returns S_OK on success.
        ''' </summary>
        <DllImport("shell32.dll", CharSet:=CharSet.Unicode)>
        Public Shared Function SHDefExtractIcon(pszIconFile As String, iIndex As Integer, uFlags As UInteger,
                                                ByRef phiconLarge As IntPtr, ByRef phiconSmall As IntPtr,
                                                nIconSize As UInteger) As Integer
        End Function

        <DllImport("shell32.dll", CharSet:=CharSet.Unicode)>
        Public Shared Function SHGetFileInfo(pszPath As String, dwFileAttributes As UInteger, ByRef psfi As SHFILEINFO,
                                             cbFileInfo As UInteger, uFlags As UInteger) As IntPtr
        End Function

        ''' <summary>Turns a full path into a shell item id list (PIDL). Free the result with Marshal.FreeCoTaskMem.</summary>
        <DllImport("shell32.dll", CharSet:=CharSet.Unicode, ExactSpelling:=True)>
        Public Shared Function SHParseDisplayName(pszName As String, pbc As IntPtr, ByRef ppidl As IntPtr,
                                                  sfgaoIn As UInteger, ByRef psfgaoOut As UInteger) As Integer
        End Function

        ''' <summary>
        ''' Given a full PIDL, returns the IShellFolder of its parent folder and a pointer
        ''' to the last (child) part of the PIDL. The child pointer points INSIDE the
        ''' full PIDL, so it must not be freed separately.
        ''' </summary>
        <DllImport("shell32.dll", ExactSpelling:=True)>
        Public Shared Function SHBindToParent(pidl As IntPtr, ByRef riid As Guid, ByRef ppv As IntPtr,
                                              ByRef ppidlLast As IntPtr) As Integer
        End Function
#End Region

#Region "Moving and sizing a Stage (used for snapping Stages into line)"
        ''' <summary>Windows has started its move or resize loop (the user pressed on the title bar or an edge).</summary>
        Public Const WM_ENTERSIZEMOVE As Integer = &H231
        ''' <summary>The move or resize loop has ended (the mouse was released, or Esc was pressed).</summary>
        Public Const WM_EXITSIZEMOVE As Integer = &H232
        ''' <summary>The window is being dragged. lParam points at the proposed RECT, which may be changed.</summary>
        Public Const WM_MOVING As Integer = &H216
        ''' <summary>The window is being resized. wParam says which edge; lParam points at the proposed RECT.</summary>
        Public Const WM_SIZING As Integer = &H214

        ' WM_SIZING's wParam: which edge or corner the user is dragging.
        Public Const WMSZ_LEFT As Integer = 1
        Public Const WMSZ_RIGHT As Integer = 2
        Public Const WMSZ_TOP As Integer = 3
        Public Const WMSZ_TOPLEFT As Integer = 4
        Public Const WMSZ_TOPRIGHT As Integer = 5
        Public Const WMSZ_BOTTOM As Integer = 6
        Public Const WMSZ_BOTTOMLEFT As Integer = 7
        Public Const WMSZ_BOTTOMRIGHT As Integer = 8

        ''' <summary>Virtual-key code of the Alt key (either one). Held while moving = no snapping.</summary>
        Public Const VK_MENU As Integer = &H12

        ' Extended styles for the snap guide lines (see SnapGuideOverlay.vb). Integers, because
        ' that's the type Windows Forms' CreateParams.ExStyle uses.
        ''' <summary>The window is never activated (it can't take focus from the Stage being moved).</summary>
        Public Const WS_EX_NOACTIVATE_STYLE As Integer = &H8000000
        ''' <summary>Mouse clicks pass straight through the window.</summary>
        Public Const WS_EX_TRANSPARENT_STYLE As Integer = &H20
        ''' <summary>The window stays above ordinary windows.</summary>
        Public Const WS_EX_TOPMOST_STYLE As Integer = &H8
        ''' <summary>The window stays out of the taskbar and Alt+Tab (same bit as WS_EX_TOOLWINDOW).</summary>
        Public Const WS_EX_TOOLWINDOW_STYLE As Integer = &H80
#End Region

#Region "Other programs' windows (Running tasks Stages - see RunningWindows.vb)"
        ''' <summary>Called once for every top-level window by EnumWindows. Return True to keep going.</summary>
        Public Delegate Function EnumWindowsCallback(hWnd As IntPtr, lParam As IntPtr) As Boolean

        ''' <summary>Calls <paramref name="callback"/> for every top-level window, front to back.</summary>
        <DllImport("user32.dll")>
        Public Shared Function EnumWindows(callback As EnumWindowsCallback, lParam As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
        Public Shared Function GetWindowTextLength(hWnd As IntPtr) As Integer
        End Function

        <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
        Public Shared Function GetWindowText(hWnd As IntPtr, windowText As System.Text.StringBuilder, maximumCount As Integer) As Integer
        End Function

        ''' <summary>The window's class name (what kind of window Windows made it as), e.g. "Progman" for the desktop.</summary>
        <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
        Public Shared Function GetClassName(hWnd As IntPtr, className As System.Text.StringBuilder, maximumCount As Integer) As Integer
        End Function

        ''' <summary>GetWindow command: the window's owner (dialogs and tool windows are owned; main windows usually aren't).</summary>
        Public Const GW_OWNER As UInteger = 4UI

        <DllImport("user32.dll")>
        Public Shared Function GetWindow(hWnd As IntPtr, command As UInteger) As IntPtr
        End Function

        ''' <summary>True if the window is minimised to the taskbar.</summary>
        <DllImport("user32.dll")>
        Public Shared Function IsIconic(hWnd As IntPtr) As Boolean
        End Function

        ''' <summary>Which process owns a window (its id is written to <paramref name="processId"/>).</summary>
        <DllImport("user32.dll")>
        Public Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef processId As UInteger) As UInteger
        End Function

        ''' <summary>OpenProcess access right: enough to ask for the program's file name, nothing more.</summary>
        Public Const PROCESS_QUERY_LIMITED_INFORMATION As UInteger = &H1000UI

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function OpenProcess(desiredAccess As UInteger, inheritHandle As Boolean, processId As UInteger) As IntPtr
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Public Shared Function CloseHandle(objectHandle As IntPtr) As Boolean
        End Function

        ''' <summary>Full path of the program a process is running (works for most processes, even elevated ones).</summary>
        <DllImport("kernel32.dll", EntryPoint:="QueryFullProcessImageNameW", CharSet:=CharSet.Unicode, SetLastError:=True)>
        Public Shared Function QueryFullProcessImageName(processHandle As IntPtr, flags As UInteger,
                                                         exeName As System.Text.StringBuilder, ByRef bufferLength As Integer) As Boolean
        End Function

        ''' <summary>
        ''' DwmGetWindowAttribute: is the window "cloaked"? Windows 10/11 keep some windows alive
        ''' but invisible (apps on other virtual desktops, suspended Store apps); those are skipped.
        ''' </summary>
        Public Const DWMWA_CLOAKED As Integer = 14

        <DllImport("dwmapi.dll")>
        Public Shared Function DwmGetWindowAttribute(hwnd As IntPtr, dwAttribute As Integer, ByRef pvAttribute As Integer,
                                                     cbAttribute As Integer) As Integer
        End Function

        ''' <summary>Asks a window for its icon. wParam picks which one (ICON_*).</summary>
        Public Const WM_GETICON As Integer = &H7F
        Public Const ICON_SMALL As Integer = 0
        Public Const ICON_BIG As Integer = 1
        ''' <summary>The small icon Windows itself would show for the window (Windows XP and later).</summary>
        Public Const ICON_SMALL2 As Integer = 2
        ''' <summary>SendMessageTimeout: give up straight away if the window's program is not responding.</summary>
        Public Const SMTO_ABORTIFHUNG As UInteger = &H2UI

        ''' <summary>Sends a message but waits at most <paramref name="timeoutMilliseconds"/> (a hung program can't freeze Stages).</summary>
        <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
        Public Shared Function SendMessageTimeout(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr,
                                                  flags As UInteger, timeoutMilliseconds As UInteger, ByRef result As IntPtr) As IntPtr
        End Function

        ''' <summary>GetClassLongPtr indexes: the window class's big and small icons (fallback when WM_GETICON gives none).</summary>
        Public Const GCLP_HICON As Integer = -14
        Public Const GCLP_HICONSM As Integer = -34

        <DllImport("user32.dll", EntryPoint:="GetClassLongPtrW")>
        Private Shared Function GetClassLongPtr64(hWnd As IntPtr, nIndex As Integer) As IntPtr
        End Function

        <DllImport("user32.dll", EntryPoint:="GetClassLongW")>
        Private Shared Function GetClassLong32(hWnd As IntPtr, nIndex As Integer) As Integer
        End Function

        ''' <summary>GetClassLongPtr exists only in 64-bit Windows' user32; 32-bit Stages calls GetClassLong instead.</summary>
        Public Shared Function GetClassLongPtr(hWnd As IntPtr, nIndex As Integer) As IntPtr
            Const BytesInA64BitPointer As Integer = 8
            If IntPtr.Size = BytesInA64BitPointer Then
                Return GetClassLongPtr64(hWnd, nIndex)
            End If
            Return New IntPtr(GetClassLong32(hWnd, nIndex))
        End Function

        ''' <summary>Like ShowWindow, but doesn't wait for the other program (which may be busy).</summary>
        <DllImport("user32.dll")>
        Public Shared Function ShowWindowAsync(hWnd As IntPtr, nCmdShow As Integer) As Boolean
        End Function

        ''' <summary>ShowWindow commands for another program's window.</summary>
        Public Const SW_MINIMIZE As Integer = 6
        Public Const SW_RESTORE As Integer = 9

        ''' <summary>
        ''' Brings a window to the front the way Alt+Tab does. Not officially documented, but
        ''' exported by user32 on every Windows from XP to 11; used only when SetForegroundWindow
        ''' is refused (Windows sometimes blocks a background program from changing the front window).
        ''' </summary>
        <DllImport("user32.dll")>
        Public Shared Sub SwitchToThisWindow(hWnd As IntPtr, fAltTab As Boolean)
        End Sub

        ''' <summary>Asks a window to close, exactly as its own X button would (it may ask to save first).</summary>
        Public Const WM_CLOSE As Integer = &H10
#End Region

#Region "Clipboard (Clipboard history Stages - see ClipboardMonitor.vb)"
        ''' <summary>Sent to every listener after anything new is put on the clipboard (Windows Vista and later).</summary>
        Public Const WM_CLIPBOARDUPDATE As Integer = &H31D

        ''' <summary>Parent handle that makes a window "message-only": never shown, it only receives messages.</summary>
        Public Shared ReadOnly HWND_MESSAGE As New IntPtr(-3)

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function AddClipboardFormatListener(hwnd As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Public Shared Function RemoveClipboardFormatListener(hwnd As IntPtr) As Boolean
        End Function
#End Region

    End Class

#Region "Shell COM interfaces"
    ' These describe COM interfaces implemented by Windows Explorer.
    ' The ORDER of the methods matters (it must match the vtable in the
    ' Windows SDK headers) - that's why methods we never call are still listed.

    ''' <summary>IShellFolder - a folder in the shell namespace. Declared up to GetUIObjectOf, the last method we need.</summary>
    <ComImport(), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E6-0000-0000-C000-000000000046")>
    Friend Interface IShellFolder
        <PreserveSig()>
        Function ParseDisplayName(hwnd As IntPtr, pbc As IntPtr, <MarshalAs(UnmanagedType.LPWStr)> pszDisplayName As String,
                                  ByRef pchEaten As UInteger, ByRef ppidl As IntPtr, ByRef pdwAttributes As UInteger) As Integer
        <PreserveSig()>
        Function EnumObjects(hwnd As IntPtr, grfFlags As Integer, ByRef ppenumIDList As IntPtr) As Integer
        <PreserveSig()>
        Function BindToObject(pidl As IntPtr, pbc As IntPtr, ByRef riid As Guid, ByRef ppv As IntPtr) As Integer
        <PreserveSig()>
        Function BindToStorage(pidl As IntPtr, pbc As IntPtr, ByRef riid As Guid, ByRef ppv As IntPtr) As Integer
        <PreserveSig()>
        Function CompareIDs(lParam As IntPtr, pidl1 As IntPtr, pidl2 As IntPtr) As Integer
        <PreserveSig()>
        Function CreateViewObject(hwndOwner As IntPtr, ByRef riid As Guid, ByRef ppv As IntPtr) As Integer
        <PreserveSig()>
        Function GetAttributesOf(cidl As UInteger, <MarshalAs(UnmanagedType.LPArray)> apidl As IntPtr(),
                                 ByRef rgfInOut As UInteger) As Integer
        <PreserveSig()>
        Function GetUIObjectOf(hwndOwner As IntPtr, cidl As UInteger, <MarshalAs(UnmanagedType.LPArray)> apidl As IntPtr(),
                               ByRef riid As Guid, rgfReserved As IntPtr, ByRef ppv As IntPtr) As Integer
    End Interface

    ''' <summary>IContextMenu - the right-click menu for one or more shell items.</summary>
    <ComImport(), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E4-0000-0000-C000-000000000046")>
    Friend Interface IContextMenu
        <PreserveSig()>
        Function QueryContextMenu(hmenu As IntPtr, iMenu As UInteger, idCmdFirst As UInteger, idCmdLast As UInteger,
                                  uFlags As UInteger) As Integer
        <PreserveSig()>
        Function InvokeCommand(ByRef info As NativeMethods.CMINVOKECOMMANDINFOEX) As Integer
        <PreserveSig()>
        Function GetCommandString(idcmd As UIntPtr, uflags As UInteger, reserved As IntPtr,
                                  commandString As IntPtr, cch As Integer) As Integer
    End Interface

    ''' <summary>IContextMenu2 - adds owner-drawn sub-menu support ("Send to", "Open with").</summary>
    <ComImport(), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F4-0000-0000-C000-000000000046")>
    Friend Interface IContextMenu2
        <PreserveSig()>
        Function QueryContextMenu(hmenu As IntPtr, iMenu As UInteger, idCmdFirst As UInteger, idCmdLast As UInteger,
                                  uFlags As UInteger) As Integer
        <PreserveSig()>
        Function InvokeCommand(ByRef info As NativeMethods.CMINVOKECOMMANDINFOEX) As Integer
        <PreserveSig()>
        Function GetCommandString(idcmd As UIntPtr, uflags As UInteger, reserved As IntPtr,
                                  commandString As IntPtr, cch As Integer) As Integer
        <PreserveSig()>
        Function HandleMenuMsg(uMsg As UInteger, wParam As IntPtr, lParam As IntPtr) As Integer
    End Interface

    ''' <summary>IContextMenu3 - adds keyboard handling and a result value for menu messages.</summary>
    <ComImport(), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")>
    Friend Interface IContextMenu3
        <PreserveSig()>
        Function QueryContextMenu(hmenu As IntPtr, iMenu As UInteger, idCmdFirst As UInteger, idCmdLast As UInteger,
                                  uFlags As UInteger) As Integer
        <PreserveSig()>
        Function InvokeCommand(ByRef info As NativeMethods.CMINVOKECOMMANDINFOEX) As Integer
        <PreserveSig()>
        Function GetCommandString(idcmd As UIntPtr, uflags As UInteger, reserved As IntPtr,
                                  commandString As IntPtr, cch As Integer) As Integer
        <PreserveSig()>
        Function HandleMenuMsg(uMsg As UInteger, wParam As IntPtr, lParam As IntPtr) As Integer
        <PreserveSig()>
        Function HandleMenuMsg2(uMsg As UInteger, wParam As IntPtr, lParam As IntPtr, ByRef plResult As IntPtr) As Integer
    End Interface

    ''' <summary>
    ''' IImageList - one of Windows' icon lists. Declared up to GetIcon, the only method Stages
    ''' calls; the ones before it are listed only because COM needs them in vtable order.
    ''' </summary>
    <ComImport(), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")>
    Friend Interface IImageList
        <PreserveSig()>
        Function Add(hbmImage As IntPtr, hbmMask As IntPtr, ByRef pi As Integer) As Integer
        <PreserveSig()>
        Function ReplaceIcon(i As Integer, hicon As IntPtr, ByRef pi As Integer) As Integer
        <PreserveSig()>
        Function SetOverlayImage(iImage As Integer, iOverlay As Integer) As Integer
        <PreserveSig()>
        Function Replace(i As Integer, hbmImage As IntPtr, hbmMask As IntPtr) As Integer
        <PreserveSig()>
        Function AddMasked(hbmImage As IntPtr, crMask As Integer, ByRef pi As Integer) As Integer
        <PreserveSig()>
        Function Draw(pimldp As IntPtr) As Integer
        <PreserveSig()>
        Function Remove(i As Integer) As Integer
        ''' <summary>Creates an icon (HICON - destroy it after use) from image number i.</summary>
        <PreserveSig()>
        Function GetIcon(i As Integer, flags As Integer, ByRef picon As IntPtr) As Integer
    End Interface
#End Region

End Namespace
