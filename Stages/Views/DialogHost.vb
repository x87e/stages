' ============================================================================
'  DialogHost.vb
'  Keeps Windows' own dialogs in front when a Stage opens one.
'
'  THE PROBLEM: Stage windows are pinned to the bottom of the window order
'  (they sit on the desktop). A dialog always appears just above the window
'  that owns it - so a message box, the folder picker, the Change Icon dialog
'  or a delete confirmation owned by a Stage appears above the Stage, but can
'  be BEHIND every other open window, where it's easy to miss while the Stage
'  waits for an answer.
'
'  THE FIX: for the time the dialog is open, create a tiny invisible window
'  that is "always on top", and make IT the dialog's owner. Windows keeps a
'  dialog above its owner, so the dialog is on top of everything too. The
'  host sits over the middle of the Stage, so dialogs that centre themselves
'  on their owner still appear over the Stage.
'
'  (Stages' own dialogs - Rename, Title size, Background picture - don't need
'  this: they are WPF windows marked Topmost themselves.)
'
'  Usage:
'      Using host As New DialogHost(Me)
'          MessageBox.Show(host.OwnerWindow, ...)          ' WPF dialogs
'          SomeWindowsDialog(host.Handle, ...)        ' dialogs that take a window handle
'      End Using
'  If the host can't be created for any reason, OwnerWindow and Handle fall back
'  to the Stage itself, so the dialog still appears (as it did before).
' ============================================================================
Imports System.Windows.Interop
Imports Stages.Services

Friend NotInheritable Class DialogHost
    Implements IDisposable

    ''' <summary>The host is one device-independent pixel square: enough to exist, too small to see (and transparent anyway).</summary>
    Private Const HostSize As Double = 1
    ''' <summary>Shown if the host ever appears in Alt+Tab while a dialog is open.</summary>
    Private Const HostTitle As String = "Stages"

    ''' <summary>The Stage that asked for the dialog (the fallback owner).</summary>
    Private ReadOnly _stageWindow As Window
    ''' <summary>The invisible always-on-top owner, or Nothing if it couldn't be created.</summary>
    Private _hostWindow As Window
    Private _hostHandle As IntPtr = IntPtr.Zero

    ''' <param name="stageWindow">
    ''' The Stage opening a dialog; the host is placed over its centre. Nothing (e.g. at
    ''' startup, before any Stage exists): the host goes in the middle of the main screen.
    ''' </param>
    Public Sub New(stageWindow As Window)
        _stageWindow = stageWindow
        Dim workArea As Rect = SystemParameters.WorkArea
        Dim centreLeft As Double = If(stageWindow Is Nothing, workArea.Left + workArea.Width / 2, stageWindow.Left + stageWindow.ActualWidth / 2)
        Dim centreTop As Double = If(stageWindow Is Nothing, workArea.Top + workArea.Height / 2, stageWindow.Top + stageWindow.ActualHeight / 2)
        Try
            Dim host As New Window() With {
                .Title = HostTitle,
                .WindowStyle = WindowStyle.None,
                .AllowsTransparency = True,
                .Background = Brushes.Transparent,
                .ResizeMode = ResizeMode.NoResize,
                .ShowInTaskbar = False,
                .Topmost = True,
                .MinWidth = 0,
                .MinHeight = 0,
                .Width = HostSize,
                .Height = HostSize,
                .WindowStartupLocation = WindowStartupLocation.Manual,
                .Left = centreLeft,
                .Top = centreTop
            }
            host.Show()
            _hostHandle = New WindowInteropHelper(host).Handle
            _hostWindow = host
        Catch ex As Exception
            ' Not worth failing the user's action over: the dialog will be owned by the Stage instead.
            Logger.Warn("Could not create the on-top owner for a dialog; it may open behind other windows", ex)
            _hostWindow = Nothing
            _hostHandle = IntPtr.Zero
        End Try
    End Sub

    ''' <summary>The owner to give a WPF dialog (MessageBox.Show, Window.Owner).</summary>
    Public ReadOnly Property OwnerWindow As Window
        Get
            Return If(_hostWindow, _stageWindow)
        End Get
    End Property

    ''' <summary>The owner to give a Windows dialog that takes a native window handle.</summary>
    Public ReadOnly Property Handle As IntPtr
        Get
            If _hostHandle <> IntPtr.Zero Then Return _hostHandle
            If _stageWindow Is Nothing Then Return IntPtr.Zero ' no owner: the desktop
            Return New WindowInteropHelper(_stageWindow).Handle
        End Get
    End Property

    ''' <summary>Closes the host (the dialog has finished). Safe to call more than once.</summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        If _hostWindow Is Nothing Then Return
        Try
            _hostWindow.Close()
        Catch ex As Exception
            Logger.Warn("Could not close a dialog's on-top owner window", ex)
        End Try
        _hostWindow = Nothing
        _hostHandle = IntPtr.Zero
    End Sub

End Class
