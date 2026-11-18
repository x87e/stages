' ============================================================================
'  RunningWindows.vb
'  What a Running tasks Stage shows: the programs' windows that are open right
'  now - roughly the ones the taskbar shows - and what can be done to them.
'
'  Which windows count (the same test the taskbar and Alt+Tab broadly use):
'    * visible, with a title, and not "cloaked" (Windows 10/11 keep windows of
'      other virtual desktops and suspended Store apps alive but invisible);
'    * not a tool window (floating palettes) and not owned by another window
'      (dialogs, pop-ups) - unless it asks for a taskbar button itself;
'    * not one of Stages' own windows.
'
'  Pictures: the window's own icon when it gives one (asked politely, with a
'  short timeout, so a program that has stopped responding can't freeze the
'  Stage), otherwise its program file's icon.
'
'  ListWindows runs on the Stage's scan thread; the actions (switch to,
'  minimise, close) run on the UI thread when the user picks them.
' ============================================================================
Imports System.Collections.Concurrent
Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows.Media.Imaging
Imports Stages.Model
Imports Stages.Native

Namespace Services

    Public NotInheritable Class RunningWindows

        ''' <summary>Start of every Running tasks item's made-up path; a window handle number follows.</summary>
        Private Const WindowItemPathPrefix As String = "stages-window:"
        ''' <summary>The Details view's "Type" column for a window shows its program's name, e.g. "notepad".</summary>
        Private Const ProgramTypeNameFormat As String = "{0}"
        ''' <summary>How long a window gets to answer "what is your icon?" before it is given up on.</summary>
        Private Const IconRequestTimeoutMilliseconds As UInteger = 50UI
        ''' <summary>Longest window title read (titles are cut here; Windows' own limit is far beyond any useful title).</summary>
        Private Const MaximumTitleLength As Integer = 512
        ''' <summary>Longest program path read.</summary>
        Private Const MaximumProgramPathLength As Integer = 1024
        ''' <summary>Window styles: WS_EX_TOOLWINDOW and WS_EX_APPWINDOW (see NativeMethods).</summary>
        Private Const ToolWindowStyle As Long = NativeMethods.WS_EX_TOOLWINDOW
        Private Const AppWindowStyle As Long = NativeMethods.WS_EX_APPWINDOW
        ''' <summary>Longest window class name read (Windows' own limit is 256).</summary>
        Private Const MaximumClassNameLength As Integer = 256
        ''' <summary>
        ''' Explorer's own windows that pass the taskbar test but aren't programs: the desktop
        ''' (Progman / WorkerW) and the taskbars themselves. The taskbar leaves them out too.
        ''' </summary>
        Private Shared ReadOnly ShellWindowClasses As New HashSet(Of String)(
            {"Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"}, StringComparer.Ordinal)
        ''' <summary>The icon cache is emptied when it holds more than this many pictures.</summary>
        Private Const MaximumCachedIcons As Integer = 300

        ''' <summary>"window|icon handle" -> its picture, so icons aren't converted again every few seconds.</summary>
        Private Shared ReadOnly IconCache As New ConcurrentDictionary(Of String, ImageSource)(StringComparer.Ordinal)
        ''' <summary>Process id -> program path; looked up once per process.</summary>
        Private Shared ReadOnly ProgramPathCache As New ConcurrentDictionary(Of UInteger, String)()

        Private Sub New()
        End Sub

        ''' <summary>
        ''' The settings that decide which windows a Running tasks tab shows - a snapshot taken
        ''' on the UI thread for the scan thread (see StageFolder.LiveFolderOptions for why).
        ''' </summary>
        Public NotInheritable Class WindowFilter
            Public ReadOnly Property IncludePatterns As NamePatterns
            Public ReadOnly Property ExcludePatterns As NamePatterns
            Public ReadOnly Property ShowMinimised As Boolean

            Public Sub New(tab As StageInfo)
                IncludePatterns = New NamePatterns(tab.RunningTasksIncludePatterns)
                ExcludePatterns = New NamePatterns(tab.RunningTasksExcludePatterns)
                ShowMinimised = tab.RunningTasksShowMinimised
            End Sub

            ''' <summary>True if a window with this title, from this program, should be listed.</summary>
            Public Function Allows(windowTitle As String, programName As String) As Boolean
                If ExcludePatterns.Matches(windowTitle) OrElse ExcludePatterns.Matches(programName) Then Return False
                If IncludePatterns.IsEmpty Then Return True
                Return IncludePatterns.Matches(windowTitle) OrElse IncludePatterns.Matches(programName)
            End Function
        End Class

        ''' <summary>
        ''' Lists the open windows (front-most first, the order Windows keeps them in) as Stage
        ''' items. Call on a scan thread.
        ''' </summary>
        Public Shared Function ListWindows(filter As WindowFilter) As List(Of StageItem)
            Dim foundItems As New List(Of StageItem)()
            Dim ownProcessId As UInteger = CUInt(Process.GetCurrentProcess().Id)

            ' The callback is kept in a local for the whole call, so the garbage collector
            ' can't take it while Windows is still calling it.
            Dim callback As NativeMethods.EnumWindowsCallback =
                Function(windowHandle As IntPtr, unusedParameter As IntPtr) As Boolean
                    Try
                        Dim windowItem As StageItem = TryDescribeWindow(windowHandle, ownProcessId, filter)
                        If windowItem IsNot Nothing Then foundItems.Add(windowItem)
                    Catch ex As Exception
                        ' One odd window (closed mid-way, access denied) must not stop the list.
                        Logger.Warn("Could not read a window for a Running tasks Stage", ex)
                    End Try
                    Return True ' keep going
                End Function
            NativeMethods.EnumWindows(callback, IntPtr.Zero)
            GC.KeepAlive(callback)

            TrimCachesIfLarge()
            Return foundItems
        End Function

        ''' <summary>Returns the window as an item, or Nothing if it isn't one the taskbar would show (or the filter hides it).</summary>
        Private Shared Function TryDescribeWindow(windowHandle As IntPtr, ownProcessId As UInteger, filter As WindowFilter) As StageItem
            If Not IsTaskbarWindow(windowHandle) Then Return Nothing

            Dim processId As UInteger
            NativeMethods.GetWindowThreadProcessId(windowHandle, processId)
            If processId = ownProcessId Then Return Nothing ' Stages' own windows

            Dim isMinimised As Boolean = NativeMethods.IsIconic(windowHandle)
            If isMinimised AndAlso Not filter.ShowMinimised Then Return Nothing

            Dim windowTitle As String = ReadWindowTitle(windowHandle)
            Dim programPath As String = FindProgramPath(processId)
            Dim programName As String = If(programPath Is Nothing, String.Empty, Path.GetFileNameWithoutExtension(programPath))
            If Not filter.Allows(windowTitle, programName) Then Return Nothing

            Dim windowIcon As ImageSource = ReadWindowIcon(windowHandle)
            ' Made-up but unique path: the same window keeps the same item between updates.
            Dim itemPath As String = WindowItemPathPrefix & windowHandle.ToInt64().ToString()
            Dim typeText As String = String.Format(ProgramTypeNameFormat, programName)

            Return New StageItem(itemPath, windowTitle, String.Empty,
                                 entryIsFolder:=False, entryIsShortcut:=False,
                                 shortcutTarget:=Nothing, targetIsMissing:=False, thumbnailSource:=Nothing,
                                 customIconFile:=Nothing, customIconNumber:=0, entryLastWriteTicks:=0L,
                                 fileTypeName:=typeText, contentModifiedTicks:=0L, opensAFolder:=False,
                                 itemWindowHandle:=windowHandle,
                                 readyMadeIcon:=windowIcon,
                                 iconFilePath:=programPath)
        End Function

        ''' <summary>The taskbar's test (see the top of this file).</summary>
        Private Shared Function IsTaskbarWindow(windowHandle As IntPtr) As Boolean
            If Not NativeMethods.IsWindowVisible(windowHandle) Then Return False
            If NativeMethods.GetWindowTextLength(windowHandle) = 0 Then Return False

            Dim cloakedReason As Integer = 0
            Dim cloakedQueryResult As Integer = NativeMethods.DwmGetWindowAttribute(
                windowHandle, NativeMethods.DWMWA_CLOAKED, cloakedReason, Marshal.SizeOf(GetType(Integer)))
            Const QuerySucceeded As Integer = 0
            If cloakedQueryResult = QuerySucceeded AndAlso cloakedReason <> 0 Then Return False
            If ShellWindowClasses.Contains(ReadClassName(windowHandle)) Then Return False

            Dim extendedStyle As Long = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE).ToInt64()
            Dim asksForTaskbarButton As Boolean = (extendedStyle And AppWindowStyle) = AppWindowStyle
            If asksForTaskbarButton Then Return True

            Dim isToolWindow As Boolean = (extendedStyle And ToolWindowStyle) = ToolWindowStyle
            Dim hasOwner As Boolean = NativeMethods.GetWindow(windowHandle, NativeMethods.GW_OWNER) <> IntPtr.Zero
            Return Not isToolWindow AndAlso Not hasOwner
        End Function

        Private Shared Function ReadClassName(windowHandle As IntPtr) As String
            Dim classBuffer As New StringBuilder(MaximumClassNameLength)
            NativeMethods.GetClassName(windowHandle, classBuffer, classBuffer.Capacity)
            Return classBuffer.ToString()
        End Function

        Private Shared Function ReadWindowTitle(windowHandle As IntPtr) As String
            Dim titleBuffer As New StringBuilder(MaximumTitleLength)
            NativeMethods.GetWindowText(windowHandle, titleBuffer, titleBuffer.Capacity)
            Return titleBuffer.ToString()
        End Function

        ''' <summary>The program file of a process, or Nothing if Windows won't say (e.g. some system processes).</summary>
        Private Shared Function FindProgramPath(processId As UInteger) As String
            Dim knownPath As String = Nothing
            If ProgramPathCache.TryGetValue(processId, knownPath) Then Return knownPath

            Dim foundPath As String = Nothing
            Dim processHandle As IntPtr = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, False, processId)
            If processHandle <> IntPtr.Zero Then
                Try
                    Dim pathBuffer As New StringBuilder(MaximumProgramPathLength)
                    Dim bufferSize As Integer = pathBuffer.Capacity
                    Const Win32PathFormat As UInteger = 0UI ' "C:\..." rather than a device path
                    If NativeMethods.QueryFullProcessImageName(processHandle, Win32PathFormat, pathBuffer, bufferSize) Then
                        foundPath = pathBuffer.ToString()
                    End If
                Finally
                    NativeMethods.CloseHandle(processHandle)
                End Try
            End If
            ProgramPathCache(processId) = foundPath
            Return foundPath
        End Function

        ''' <summary>
        ''' The window's own icon as a frozen picture, or Nothing (the program file's icon is then
        ''' shown instead). The icon belongs to the window, so it is copied, never destroyed.
        ''' </summary>
        Private Shared Function ReadWindowIcon(windowHandle As IntPtr) As ImageSource
            Dim iconHandle As IntPtr = AskWindowForIcon(windowHandle, NativeMethods.ICON_BIG)
            If iconHandle = IntPtr.Zero Then iconHandle = AskWindowForIcon(windowHandle, NativeMethods.ICON_SMALL2)
            If iconHandle = IntPtr.Zero Then iconHandle = AskWindowForIcon(windowHandle, NativeMethods.ICON_SMALL)
            If iconHandle = IntPtr.Zero Then iconHandle = NativeMethods.GetClassLongPtr(windowHandle, NativeMethods.GCLP_HICON)
            If iconHandle = IntPtr.Zero Then iconHandle = NativeMethods.GetClassLongPtr(windowHandle, NativeMethods.GCLP_HICONSM)
            If iconHandle = IntPtr.Zero Then Return Nothing

            Dim cacheKey As String = windowHandle.ToInt64().ToString() & "|" & iconHandle.ToInt64().ToString()
            Dim cachedPicture As ImageSource = Nothing
            If IconCache.TryGetValue(cacheKey, cachedPicture) Then Return cachedPicture

            Try
                Dim picture As BitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(iconHandle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions())
                picture.Freeze() ' frozen pictures can be handed from the scan thread to the UI thread
                IconCache(cacheKey) = picture
                Return picture
            Catch ex As Exception When TypeOf ex Is COMException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is ExternalException
                Return Nothing ' the icon vanished (window closing) or is in a format WPF can't read
            End Try
        End Function

        ''' <summary>WM_GETICON with a short timeout; IntPtr.Zero if the window has none or doesn't answer.</summary>
        Private Shared Function AskWindowForIcon(windowHandle As IntPtr, whichIcon As Integer) As IntPtr
            Dim iconHandle As IntPtr = IntPtr.Zero
            Dim answered As IntPtr = NativeMethods.SendMessageTimeout(windowHandle, NativeMethods.WM_GETICON, New IntPtr(whichIcon), IntPtr.Zero,
                                                                      NativeMethods.SMTO_ABORTIFHUNG, IconRequestTimeoutMilliseconds, iconHandle)
            If answered = IntPtr.Zero Then Return IntPtr.Zero ' timed out, or the window is gone
            Return iconHandle
        End Function

        ''' <summary>Stops the caches growing for as long as Stages runs (windows and processes come and go).</summary>
        Private Shared Sub TrimCachesIfLarge()
            If IconCache.Count > MaximumCachedIcons Then IconCache.Clear()
            If ProgramPathCache.Count > MaximumCachedIcons Then ProgramPathCache.Clear()
        End Sub

#Region "Actions (UI thread)"

        ''' <summary>Brings the window to the front, restoring it first if it is minimised (as clicking its taskbar button does).</summary>
        Public Shared Sub SwitchTo(windowHandle As IntPtr)
            If Not NativeMethods.IsWindow(windowHandle) Then Return
            If NativeMethods.IsIconic(windowHandle) Then
                NativeMethods.ShowWindowAsync(windowHandle, NativeMethods.SW_RESTORE)
            End If
            ' Normally allowed: the user has just clicked the Stage, so Stages received the last
            ' input. If Windows refuses anyway, ask the way Alt+Tab does.
            If Not NativeMethods.SetForegroundWindow(windowHandle) Then
                Const AsAltTabWould As Boolean = True
                NativeMethods.SwitchToThisWindow(windowHandle, AsAltTabWould)
            End If
        End Sub

        Public Shared Sub Minimise(windowHandle As IntPtr)
            If Not NativeMethods.IsWindow(windowHandle) Then Return
            NativeMethods.ShowWindowAsync(windowHandle, NativeMethods.SW_MINIMIZE)
        End Sub

        ''' <summary>Asks the window to close, as its X button would - the program may ask to save first.</summary>
        Public Shared Sub Close(windowHandle As IntPtr)
            If Not NativeMethods.IsWindow(windowHandle) Then Return
            NativeMethods.PostMessage(windowHandle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero)
        End Sub

        ''' <summary>True if the window is minimised right now (to offer Restore or Minimise).</summary>
        Public Shared Function IsMinimised(windowHandle As IntPtr) As Boolean
            Return NativeMethods.IsIconic(windowHandle)
        End Function

#End Region

    End Class

End Namespace
