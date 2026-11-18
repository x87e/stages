' ============================================================================
'  DesktopIntegration.vb
'  Everything that makes a normal WPF window behave like part of the desktop:
'    * hidden from the taskbar and Alt+Tab
'    * "glued" to the desktop so Win+D / Show Desktop doesn't hide it
'    * always kept at the bottom of the z-order
'    * blurred / acrylic / plain translucent background (BackdropStyle)
'  ...plus hiding/showing the desktop icons (Settings > Desktop) and light/dark
'  menus and title bars (Settings > Appearance > Theme).
'
'  Ported from NoFences' DesktopUtil.cs, WindowUtil.cs and BlurUtil.cs.
' ============================================================================
Imports System.Runtime.InteropServices
Imports Microsoft.Win32
Imports Stages.Model
Imports Stages.Services

Namespace Native

    Friend NotInheritable Class DesktopIntegration

        ''' <summary>
        ''' Window class name of Explorer's "Program Manager" window, which hosts
        ''' the desktop. Stages are owned by it so that Show Desktop leaves them alone.
        ''' </summary>
        Private Const DesktopProgramManagerClassName As String = "Progman"

        ''' <summary>Registry key behind Settings > Personalization > Colors.</summary>
        Private Const PersonalizeRegistryKey As String = "Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
        ''' <summary>The "Transparency effects" switch (DWORD: 1 = on, 0 = off).</summary>
        Private Const TransparencyEffectsValueName As String = "EnableTransparency"
        Private Const TransparencyEffectsOff As Integer = 0
        ''' <summary>AABBGGRR colour with zero opacity, for the Translucent backdrop.</summary>
        Private Const FullyTransparentColor As Integer = &H0
        ''' <summary>SetWindowCompositionAttribute returns 0 when it fails.</summary>
        Private Const CompositionCallFailed As Integer = 0

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Applies all desktop behaviours to a freshly created Stage window.
        ''' Safe to call again later (e.g. after Explorer restarts). The background effect is
        ''' applied separately with ApplyBackdrop.
        ''' </summary>
        Public Shared Sub PrepareStageWindow(windowHandle As IntPtr)
            HideFromTaskbarAndAltTab(windowHandle)
            GlueToDesktop(windowHandle)
            SendToBottom(windowHandle)
        End Sub

        ''' <summary>Finds Explorer's desktop window. Returns IntPtr.Zero while Explorer isn't running.</summary>
        Public Shared Function FindDesktopWindow() As IntPtr
            Return NativeMethods.FindWindow(DesktopProgramManagerClassName, Nothing)
        End Function

        ''' <summary>
        ''' Makes the desktop window the OWNER of our window (not its parent - the Stage
        ''' stays a top-level window). Owned windows survive Win+D / Show Desktop.
        ''' </summary>
        ''' <returns>True if the desktop window was found and the owner set.</returns>
        Public Shared Function GlueToDesktop(windowHandle As IntPtr) As Boolean
            Dim desktopWindow As IntPtr = FindDesktopWindow()
            If desktopWindow = IntPtr.Zero Then
                ' Explorer isn't running (yet) - nothing to glue to right now.
                Return False
            End If

            NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWLP_HWNDPARENT, desktopWindow)
            Return True
        End Function

        ''' <summary>
        ''' True if our window is still owned by the CURRENT desktop window.
        ''' After an Explorer restart the old desktop window is gone and a new one exists,
        ''' so this returns False until we re-glue.
        ''' </summary>
        Public Shared Function IsGluedToDesktop(windowHandle As IntPtr) As Boolean
            Dim desktopWindow As IntPtr = FindDesktopWindow()
            If desktopWindow = IntPtr.Zero Then
                ' No desktop exists at the moment, so there is nothing we could re-glue to.
                Return True
            End If

            Dim currentOwner As IntPtr = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWLP_HWNDPARENT)
            Return currentOwner = desktopWindow
        End Function

        ''' <summary>Pushes the window to the very bottom of the z-order without moving, sizing or activating it.</summary>
        Public Shared Sub SendToBottom(windowHandle As IntPtr)
            Dim keepPositionSizeAndFocus As UInteger =
                NativeMethods.SWP_NOSIZE Or NativeMethods.SWP_NOMOVE Or NativeMethods.SWP_NOACTIVATE
            NativeMethods.SetWindowPos(windowHandle, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0, keepPositionSizeAndFocus)
        End Sub

        ''' <summary>Turns the window into a "tool window" so it appears in neither the taskbar nor Alt+Tab.</summary>
        Public Shared Sub HideFromTaskbarAndAltTab(windowHandle As IntPtr)
            Dim extendedStyle As Long = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE).ToInt64()
            extendedStyle = (extendedStyle Or NativeMethods.WS_EX_TOOLWINDOW) And Not NativeMethods.WS_EX_APPWINDOW
            NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE, New IntPtr(extendedStyle))
        End Sub

        ''' <summary>
        ''' True if Windows' "Transparency effects" setting is on (Settings > Personalization >
        ''' Colors). When it is off, Windows draws every backdrop effect as a solid colour, so
        ''' Stages can only look opaque - StageWindow tells the user why.
        ''' </summary>
        Public Shared Function AreTransparencyEffectsEnabled() As Boolean
            Try
                Using personalizeKey As RegistryKey = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryKey)
                    If personalizeKey Is Nothing Then
                        Return True ' never configured = Windows default, which is on
                    End If
                    Dim storedValue As Object = personalizeKey.GetValue(TransparencyEffectsValueName)
                    If TypeOf storedValue Is Integer Then
                        Return CInt(storedValue) <> TransparencyEffectsOff
                    End If
                    Return True
                End Using
            Catch ex As Exception
                Return True ' can't tell - assume the default
            End Try
        End Function

        ''' <summary>
        ''' Sets what Windows draws behind the window's transparent pixels, using the
        ''' undocumented-but-stable SetWindowCompositionAttribute API (as NoFences did).
        ''' Works the same on Windows 10 and 11 for a normal (non-layered) window whose WPF
        ''' render background is transparent - see StageWindow.OnSourceInitialized.
        '''   Blur        -> blurred desktop
        '''   Acrylic     -> blurred desktop (Stages draws the acrylic look on top)
        '''   Translucent -> desktop shown through, unblurred
        ''' </summary>
        Public Shared Sub ApplyBackdrop(windowHandle As IntPtr, requestedStyle As BackdropStyle)
            Dim accent As New NativeMethods.AccentPolicy()
            Select Case requestedStyle
                Case BackdropStyle.Translucent
                    ' A fully transparent "gradient": Windows composites the window's own alpha
                    ' (our dark tint) over the desktop, with no blur.
                    accent.AccentState = NativeMethods.ACCENT_ENABLE_TRANSPARENTGRADIENT
                    accent.AccentFlags = NativeMethods.ACCENT_FLAG_USE_GRADIENT_COLOR
                    accent.GradientColor = FullyTransparentColor
                Case Else
                    ' Blur - and also Acrylic: Stages' acrylic is an imitation drawn on top of
                    ' this blur by StageWindow. Windows' own acrylic accent renders black on
                    ' Windows 11, so it is no longer used.
                    accent.AccentState = NativeMethods.ACCENT_ENABLE_BLURBEHIND
            End Select

            If Not SetAccentPolicy(windowHandle, accent) Then
                Logger.Warn("Windows refused the '" & requestedStyle.ToString() & "' background effect for a Stage")
            End If
        End Sub

        ''' <summary>Passes an AccentPolicy to SetWindowCompositionAttribute. Returns False if Windows rejected it.</summary>
        Private Shared Function SetAccentPolicy(windowHandle As IntPtr, accent As NativeMethods.AccentPolicy) As Boolean
            ' The API wants a pointer to the AccentPolicy, so copy it into unmanaged memory first.
            Dim accentSize As Integer = Marshal.SizeOf(GetType(NativeMethods.AccentPolicy))
            Dim accentPointer As IntPtr = Marshal.AllocHGlobal(accentSize)
            Try
                Marshal.StructureToPtr(accent, accentPointer, False)

                Dim compositionData As New NativeMethods.WindowCompositionAttributeData With {
                    .Attribute = NativeMethods.WCA_ACCENT_POLICY,
                    .Data = accentPointer,
                    .SizeOfData = accentSize
                }
                Return NativeMethods.SetWindowCompositionAttribute(windowHandle, compositionData) <> CompositionCallFailed
            Finally
                ' Always free unmanaged memory, even if the call above throws.
                Marshal.FreeHGlobal(accentPointer)
            End Try
        End Function

#Region "Light / dark mode (Settings > Appearance > Theme)"

        ''' <summary>Windows' "default app mode" switch, in the same Personalize key (DWORD: 1 = light, 0 = dark).</summary>
        Private Const AppsUseLightThemeValueName As String = "AppsUseLightTheme"
        Private Const AppsUseDarkThemeValue As Integer = 0
        ''' <summary>DwmSetWindowAttribute values for "dark title bar".</summary>
        Private Const DarkTitleBarOn As Integer = 1
        Private Const DarkTitleBarOff As Integer = 0
        ''' <summary>DwmSetWindowAttribute returns S_OK (0) on success.</summary>
        Private Const DwmCallSucceeded As Integer = 0

        ''' <summary>Set once a menu-mode failure has been logged, so it isn't logged on every change.</summary>
        Private Shared _menuModeFailureLogged As Boolean

        ''' <summary>
        ''' True if Windows is set to dark mode for apps (Settings > Personalization > Colors >
        ''' "Choose your default app mode"). Light if the setting can't be read (Windows' default).
        ''' </summary>
        Public Shared Function IsWindowsAppModeDark() As Boolean
            Try
                Using personalizeKey As RegistryKey = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryKey)
                    If personalizeKey Is Nothing Then Return False
                    Dim storedValue As Object = personalizeKey.GetValue(AppsUseLightThemeValueName)
                    If TypeOf storedValue Is Integer Then
                        Return CInt(storedValue) = AppsUseDarkThemeValue
                    End If
                    Return False
                End Using
            Catch ex As Exception
                Return False ' can't tell - assume Windows' default, light
            End Try
        End Function

        ''' <summary>
        ''' Makes every popup menu this process shows from now on light or dark: the Stage menu,
        ''' the tray menu and the Explorer menu on items. Uses undocumented uxtheme functions
        ''' (see NativeMethods), so on Windows 10 before 1903, or if they ever disappear, this
        ''' does nothing and the menus simply stay in Windows' default light style.
        ''' </summary>
        Public Shared Sub SetMenusDark(useDark As Boolean)
            If Environment.OSVersion.Version.Build < NativeMethods.PREFERRED_APP_MODE_MINIMUM_BUILD Then
                LogMenuModeFailureOnce("Windows build " & Environment.OSVersion.Version.Build.ToString() &
                                       " is older than 1903, so menus stay light", Nothing)
                Return
            End If

            Try
                Dim requestedMode As Integer = If(useDark, NativeMethods.PREFERRED_APP_MODE_FORCE_DARK,
                                                           NativeMethods.PREFERRED_APP_MODE_FORCE_LIGHT)
                NativeMethods.SetPreferredAppMode(requestedMode)
                NativeMethods.FlushMenuThemes()
            Catch ex As Exception When TypeOf ex Is EntryPointNotFoundException OrElse TypeOf ex Is DllNotFoundException
                LogMenuModeFailureOnce("Windows doesn't offer the menu light/dark switch here, so menus keep Windows' default style", ex)
            End Try
        End Sub

        ''' <summary>
        ''' True if this Windows version has the (undocumented) menu light/dark switch. The
        ''' Settings window uses it to explain why menus might stay light.
        ''' </summary>
        Public Shared Function CanThemeMenus() As Boolean
            Return Environment.OSVersion.Version.Build >= NativeMethods.PREFERRED_APP_MODE_MINIMUM_BUILD
        End Function

        Private Shared Sub LogMenuModeFailureOnce(message As String, problem As Exception)
            If _menuModeFailureLogged Then Return
            _menuModeFailureLogged = True
            Logger.Warn(message, problem)
        End Sub

        ''' <summary>True on Windows 11, where windows can have rounded corners (Windows 10 can't round them).</summary>
        Public Shared Function CanRoundCorners() As Boolean
            Return Environment.OSVersion.Version.Build >= NativeMethods.WINDOWS_11_FIRST_BUILD
        End Function

        ''' <summary>
        ''' Rounds or squares a window's corners (Windows 11's documented DWM setting).
        ''' Does nothing on Windows 10, whose windows are always square.
        ''' </summary>
        Public Shared Sub SetWindowCorners(windowHandle As IntPtr, rounded As Boolean)
            If windowHandle = IntPtr.Zero OrElse Not CanRoundCorners() Then Return
            Dim cornerPreference As Integer = If(rounded, NativeMethods.DWMWCP_ROUND, NativeMethods.DWMWCP_DONOTROUND)
            Const AttributeSizeInBytes As Integer = 4 ' one 32-bit enum value
            Dim result As Integer = NativeMethods.DwmSetWindowAttribute(windowHandle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                                                                       cornerPreference, AttributeSizeInBytes)
            If result <> DwmCallSucceeded Then
                Logger.Warn("Windows refused the corner shape (DWM result " & result.ToString() & ")")
            End If
        End Sub

        ''' <summary>
        ''' Gives a normal window (Settings, dialogs) a dark or light title bar, using the
        ''' documented DWM attribute (its number changed in Windows 10 20H1, so both are handled).
        ''' Does nothing on Windows older than 10 1809.
        ''' </summary>
        Public Shared Sub SetDarkTitleBar(windowHandle As IntPtr, useDark As Boolean)
            If windowHandle = IntPtr.Zero Then Return
            Dim windowsBuild As Integer = Environment.OSVersion.Version.Build
            If windowsBuild < NativeMethods.IMMERSIVE_DARK_MODE_MINIMUM_BUILD Then Return

            Dim darkModeAttribute As Integer = If(windowsBuild >= NativeMethods.IMMERSIVE_DARK_MODE_ATTRIBUTE_20_BUILD,
                                                  NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                                                  NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1)
            Dim attributeValue As Integer = If(useDark, DarkTitleBarOn, DarkTitleBarOff)
            Const AttributeSizeInBytes As Integer = 4 ' one 32-bit BOOL
            Dim result As Integer = NativeMethods.DwmSetWindowAttribute(windowHandle, darkModeAttribute, attributeValue, AttributeSizeInBytes)
            If result <> DwmCallSucceeded Then
                Logger.Warn("Windows refused the " & If(useDark, "dark", "light") & " title bar (DWM result " & result.ToString() & ")")
                Return
            End If

            ' Windows 10 only repaints the title bar when the frame changes; this nudges it
            ' without moving, resizing, re-ordering or activating the window.
            Dim redrawFrameOnly As UInteger = NativeMethods.SWP_NOMOVE Or NativeMethods.SWP_NOSIZE Or
                                              NativeMethods.SWP_NOZORDER Or NativeMethods.SWP_NOACTIVATE Or
                                              NativeMethods.SWP_FRAMECHANGED
            NativeMethods.SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, redrawFrameOnly)
        End Sub

#End Region

#Region "File name extensions (Settings > Items)"

        ''' <summary>Registry key behind File Explorer's View options.</summary>
        Private Const ExplorerAdvancedRegistryKey As String = "Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
        ''' <summary>"Hide extensions for known file types" (DWORD: 1 = hide, 0 = show).</summary>
        Private Const HideFileExtensionsValueName As String = "HideFileExt"
        Private Const ShowFileExtensionsValue As Integer = 0

        ''' <summary>
        ''' True if File Explorer is hiding extensions of known file types (its default), False
        ''' if "File name extensions" is ticked in Explorer's View menu. Read fresh each time,
        ''' so a change made in Explorer shows on the Stages at their next refresh.
        ''' </summary>
        Public Shared Function IsWindowsHidingFileExtensions() As Boolean
            Try
                Using advancedKey As RegistryKey = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedRegistryKey)
                    If advancedKey Is Nothing Then Return True ' never configured = Windows' default, hidden
                    Dim storedValue As Object = advancedKey.GetValue(HideFileExtensionsValueName)
                    If TypeOf storedValue Is Integer Then
                        Return CInt(storedValue) <> ShowFileExtensionsValue
                    End If
                    Return True
                End Using
            Catch ex As Exception
                Return True ' can't tell - assume the default
            End Try
        End Function

        ''' <summary>
        ''' True if Windows has a file type registered for this extension (e.g. ".docx"), which is
        ''' what "known file type" means in Explorer's "Hide extensions for known file types".
        ''' </summary>
        Public Shared Function IsKnownFileType(extension As String) As Boolean
            Try
                Using fileTypeKey As RegistryKey = Registry.ClassesRoot.OpenSubKey(extension)
                    Return fileTypeKey IsNot Nothing
                End Using
            Catch ex As Exception
                Return False
            End Try
        End Function

#End Region

#Region "Desktop icons (Settings > Desktop)"

        ' The desktop icons are a list view ("SysListView32") inside Explorer's shell view
        ' window ("SHELLDLL_DefView"). That shell view normally sits inside Progman, but
        ' Windows moves it into a "WorkerW" window in some situations (e.g. after a
        ' wallpaper slideshow change), so both places are searched.
        '
        ' Hiding the icons is done exactly as the desktop's own right-click > View >
        ' "Show desktop icons" does it: by sending the shell view that menu's command.
        ' Explorer then remembers the choice itself, just as if the user had used the menu.
        ' Stages are owned by Progman, not the list view, so they stay visible.

        Private Const ShellViewClassName As String = "SHELLDLL_DefView"
        Private Const DesktopIconListClassName As String = "SysListView32"
        Private Const DesktopWorkerClassName As String = "WorkerW"
        ''' <summary>
        ''' Command id of "Show desktop icons" in the desktop's View menu. Not officially
        ''' documented, but unchanged since Windows Vista and widely used.
        ''' </summary>
        Private Const ToggleDesktopIconsCommandId As Integer = &H7402

        ''' <summary>Explorer's desktop shell view window, or IntPtr.Zero if it can't be found.</summary>
        Private Shared Function FindDesktopShellView() As IntPtr
            ' Usual place: directly inside Progman.
            Dim desktopWindow As IntPtr = FindDesktopWindow()
            If desktopWindow <> IntPtr.Zero Then
                Dim shellView As IntPtr = NativeMethods.FindWindowEx(desktopWindow, IntPtr.Zero, ShellViewClassName, Nothing)
                If shellView <> IntPtr.Zero Then Return shellView
            End If

            ' Otherwise: inside one of the top-level WorkerW windows. Walk them in turn.
            Dim worker As IntPtr = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, DesktopWorkerClassName, Nothing)
            While worker <> IntPtr.Zero
                Dim shellView As IntPtr = NativeMethods.FindWindowEx(worker, IntPtr.Zero, ShellViewClassName, Nothing)
                If shellView <> IntPtr.Zero Then Return shellView
                worker = NativeMethods.FindWindowEx(IntPtr.Zero, worker, DesktopWorkerClassName, Nothing)
            End While

            Return IntPtr.Zero
        End Function

        ''' <summary>
        ''' True if the desktop icons are currently hidden; Nothing if that can't be told
        ''' (e.g. Explorer isn't running), so the Settings window can grey the option out.
        ''' </summary>
        Public Shared Function AreDesktopIconsHidden() As Boolean?
            Dim shellView As IntPtr = FindDesktopShellView()
            If shellView = IntPtr.Zero Then Return Nothing

            Dim iconList As IntPtr = NativeMethods.FindWindowEx(shellView, IntPtr.Zero, DesktopIconListClassName, Nothing)
            If iconList = IntPtr.Zero Then Return Nothing

            Return Not NativeMethods.IsWindowVisible(iconList)
        End Function

        ''' <summary>
        ''' Hides or shows the desktop icons. Does nothing if they're already in that state.
        ''' Explorer applies it a moment later (the message is posted, not waited for), so
        ''' callers should re-read AreDesktopIconsHidden shortly afterwards.
        ''' </summary>
        ''' <returns>False if Explorer's desktop couldn't be found.</returns>
        Public Shared Function SetDesktopIconsHidden(shouldHide As Boolean) As Boolean
            Dim currentlyHidden As Boolean? = AreDesktopIconsHidden()
            If Not currentlyHidden.HasValue Then
                Logger.Warn("Could not find Explorer's desktop icons to " & If(shouldHide, "hide", "show"))
                Return False
            End If
            If currentlyHidden.Value = shouldHide Then Return True

            ' The command is a toggle, so it is only sent when the state needs to change.
            Dim shellView As IntPtr = FindDesktopShellView()
            Dim posted As Boolean = NativeMethods.PostMessage(shellView, NativeMethods.WM_COMMAND,
                                                              New IntPtr(ToggleDesktopIconsCommandId), IntPtr.Zero)
            Logger.Info(If(shouldHide, "Hiding", "Showing") & " desktop icons" & If(posted, "", " FAILED"))
            Return posted
        End Function

#End Region

        ''' <summary>Current mouse position in physical screen pixels.</summary>
        Public Shared Function GetCursorScreenPosition() As NativeMethods.NativePoint
            Dim cursorPosition As New NativeMethods.NativePoint()
            NativeMethods.GetCursorPos(cursorPosition)
            Return cursorPosition
        End Function

        ''' <summary>
        ''' True if the mouse pointer is anywhere over the window, including its
        ''' invisible resize borders. Used to decide when a Stage should "minify".
        ''' </summary>
        Public Shared Function IsCursorInsideWindow(windowHandle As IntPtr) As Boolean
            If windowHandle = IntPtr.Zero Then
                Return False
            End If

            Dim windowBounds As New NativeMethods.NativeRect()
            If Not NativeMethods.GetWindowRect(windowHandle, windowBounds) Then
                Return False
            End If

            Dim cursorPosition As NativeMethods.NativePoint = GetCursorScreenPosition()
            Return cursorPosition.X >= windowBounds.Left AndAlso cursorPosition.X < windowBounds.Right AndAlso
                   cursorPosition.Y >= windowBounds.Top AndAlso cursorPosition.Y < windowBounds.Bottom
        End Function

        ''' <summary>
        ''' True while the left mouse button is physically held down - e.g. during a
        ''' drag or an edge resize, when the pointer can briefly stray outside the window.
        ''' </summary>
        Public Shared Function IsLeftMouseButtonDown() As Boolean
            Dim buttonState As Integer = NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON)
            Return (buttonState And NativeMethods.KEY_IS_DOWN_FLAG) <> 0
        End Function

        ''' <summary>
        ''' Makes sure the window is actually showing on screen. Something outside our control
        ''' (Explorer, a shell extension) can hide it; this puts it back without stealing focus.
        ''' </summary>
        ''' <returns>True if the window had been hidden and was re-shown.</returns>
        Public Shared Function EnsureWindowVisible(windowHandle As IntPtr) As Boolean
            If NativeMethods.IsWindowVisible(windowHandle) Then
                Return False
            End If

            NativeMethods.ShowWindow(windowHandle, NativeMethods.SW_SHOWNOACTIVATE)
            Return True
        End Function

    End Class

End Namespace
