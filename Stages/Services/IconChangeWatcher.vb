' ============================================================================
'  IconChangeWatcher.vb
'  Keeps Stage icons in step with Windows' own icons while Stages runs.
'
'  Stages always asks Windows for icons (ShellIcons), so custom file-type
'  icons, custom folder icons and icon packs already appear. But Stages keeps
'  the icons it has loaded (IconProvider's cache), so a change made while it
'  runs would otherwise only show after a restart.
'
'  This class asks the shell to tell us when icons change:
'      SHCNE_ASSOCCHANGED - a file type's association changed (icon packs and
'                           "Default apps" changes announce this)
'      SHCNE_UPDATEIMAGE  - an icon in Windows' icon cache was replaced
'  Several usually arrive together, so it waits for a quiet moment, then
'  clears the cache and reloads every Stage's icons (RefreshAllIcons).
'  Settings > Items > "Refresh icons now" does the same on demand, for any
'  change Windows doesn't announce.
'
'  The shell's notifications are window messages, so this owns a small hidden
'  window (a Windows Forms NativeWindow, part of .NET Framework).
' ============================================================================
Imports System.Runtime.InteropServices
Imports System.Windows.Threading
Imports Stages.Native
Imports WinForms = System.Windows.Forms

Namespace Services

    Public NotInheritable Class IconChangeWatcher
        Implements IDisposable

        ''' <summary>The message number the shell posts to our hidden window for each change.</summary>
        Private Const IconsChangedMessage As Integer = NativeMethods.WM_APP + 1
        ''' <summary>Wait this long after the last notification before refreshing (they come in bursts).</summary>
        Private Shared ReadOnly QuietPeriodBeforeRefresh As TimeSpan = TimeSpan.FromSeconds(1.5)
        ''' <summary>SHChangeNotifyRegister returns 0 when it fails.</summary>
        Private Const RegistrationFailed As UInteger = 0UI
        Private Const SingleEntry As Integer = 1

        Private ReadOnly _notificationWindow As NotificationWindow
        Private ReadOnly _refreshTimer As DispatcherTimer
        ''' <summary>The shell's id for the desktop (the top of everything), which is what we watch.</summary>
        Private _desktopItemId As IntPtr = IntPtr.Zero
        Private _registrationId As UInteger = RegistrationFailed
        Private _isDisposed As Boolean

        ''' <summary>Starts listening. Failure is only logged: icons then update on restart or via the Settings button.</summary>
        Public Sub New()
            _refreshTimer = New DispatcherTimer() With {.Interval = QuietPeriodBeforeRefresh}
            AddHandler _refreshTimer.Tick, AddressOf RefreshTimer_Tick

            _notificationWindow = New NotificationWindow(Sub() OnIconsChanged())
            _notificationWindow.CreateHandle(New WinForms.CreateParams())

            Try
                NativeMethods.SHGetSpecialFolderLocation(IntPtr.Zero, NativeMethods.CSIDL_DESKTOP, _desktopItemId)
                Dim watchEverything As New NativeMethods.SHChangeNotifyEntry With {
                    .pidl = _desktopItemId,
                    .fRecursive = True
                }
                _registrationId = NativeMethods.SHChangeNotifyRegister(
                    _notificationWindow.Handle,
                    NativeMethods.SHCNRF_INTERRUPT_LEVEL Or NativeMethods.SHCNRF_SHELL_LEVEL,
                    NativeMethods.SHCNE_ASSOCCHANGED Or NativeMethods.SHCNE_UPDATEIMAGE,
                    IconsChangedMessage, SingleEntry, watchEverything)

                If _registrationId = RegistrationFailed Then
                    Logger.Warn("Windows wouldn't report icon changes; icons will update after a restart or via Settings > Items")
                End If
            Catch ex As Exception
                Logger.Warn("Could not start watching for icon changes", ex)
            End Try
        End Sub

        ''' <summary>
        ''' Forgets every loaded icon and reloads all Stages' icons from Windows. Also used by
        ''' the "Refresh icons now" button. Call on the UI thread.
        ''' </summary>
        Public Shared Sub RefreshAllIcons()
            IconProvider.Instance.ForgetIcons()
            ItemLabels.ForgetKnownFileTypes() ' associations may have changed too
            StageManager.Instance.ReloadAllIcons()
            Logger.Info("Reloaded all icons")
        End Sub

        ''' <summary>A notification arrived: (re)start the quiet-period countdown.</summary>
        Private Sub OnIconsChanged()
            _refreshTimer.Stop()
            _refreshTimer.Start()
        End Sub

        Private Sub RefreshTimer_Tick(sender As Object, e As EventArgs)
            _refreshTimer.Stop()
            Logger.Info("Windows reported that icons changed")
            RefreshAllIcons()
        End Sub

        ''' <summary>Stops listening and releases the hidden window and the desktop id.</summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            If _isDisposed Then Return
            _isDisposed = True

            _refreshTimer.Stop()
            If _registrationId <> RegistrationFailed Then
                NativeMethods.SHChangeNotifyDeregister(_registrationId)
            End If
            If _desktopItemId <> IntPtr.Zero Then
                Marshal.FreeCoTaskMem(_desktopItemId)
            End If
            _notificationWindow.DestroyHandle()
        End Sub

        ''' <summary>The hidden window the shell posts its notifications to.</summary>
        Private NotInheritable Class NotificationWindow
            Inherits WinForms.NativeWindow

            Private ReadOnly _onIconsChanged As Action

            Public Sub New(onIconsChanged As Action)
                _onIconsChanged = onIconsChanged
            End Sub

            Protected Overrides Sub WndProc(ByRef m As WinForms.Message)
                If m.Msg = IconsChangedMessage Then
                    _onIconsChanged.Invoke()
                    Return
                End If
                MyBase.WndProc(m)
            End Sub
        End Class

    End Class

End Namespace
