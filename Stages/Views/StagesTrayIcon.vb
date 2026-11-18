' ============================================================================
'  StagesTrayIcon.vb
'  The Stages icon in the notification area (system tray).
'
'    Right-click menu, top to bottom:
'        New Stage  >  Standard Stage / Live folder... / Running tasks / Clipboard history
'        ----------
'        Hide Stages   (reads "Show Stages" while they are hidden)
'        Open Settings (bold: it's also what double-click does, as in Windows)
'        ----------
'        Quit Stages
'    Double-click: opens the Settings window.
'
'  WPF has no tray icon of its own, so the icon is Windows Forms' NotifyIcon,
'  which is part of .NET Framework itself (no download / NuGet package).
'  The MENU is a native Windows menu (NativePopupMenu) rather than a Windows
'  Forms one, so it looks like the Stage and Explorer menus and follows
'  Settings > Appearance > Theme in the same way.
'
'  Windows Forms is imported under the alias "WinForms" (and System.Drawing as
'  "GdiDrawing") so its classes - which share names with WPF's, such as
'  Application, MessageBox and Color - can never be picked up by accident.
'
'  NotifyIcon also re-adds itself automatically when Explorer restarts.
'
'  Settings > General > "Show the Stages icon in the notification area"
'  (v0.8.4) can hide the icon. Stages keeps running without it; starting
'  Stages again then opens Settings in the running copy (Program.vb).
' ============================================================================
Imports System.IO
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports WinForms = System.Windows.Forms
Imports GdiDrawing = System.Drawing

Public NotInheritable Class StagesTrayIcon
    Implements IDisposable

#Region "Constants"
    ''' <summary>Shown when hovering over the tray icon.</summary>
    Private Const TooltipText As String = "Stages"
    Private Const HideStagesText As String = "Hide Stages"
    Private Const ShowStagesText As String = "Show Stages"
    Private Const OpenSettingsText As String = "Open Settings"
    Private Const QuitStagesText As String = "Quit Stages"
    ' "New Stage" sub-menu (the same choices as a Stage's own menu).
    Private Const NewStageText As String = "New Stage"
    Private Const NewStandardStageText As String = "Standard Stage"
    Private Const NewLiveFolderStageText As String = "Live folder..."
    Private Const NewRunningTasksStageText As String = "Running tasks"
    Private Const NewClipboardStageText As String = "Clipboard history"
    Private Const ChooseLiveFolderTitle As String = "Choose the folder this Stage will show"

    ''' <summary>The app icon, compiled into Stages.exe as a WPF resource (Resources\Stages.ico).</summary>
    Public Const AppIconResourceUri As String = "pack://application:,,,/Resources/Stages.ico"
#End Region

#Region "Fields"
    Private ReadOnly _notifyIcon As WinForms.NotifyIcon
    ''' <summary>The picture in the tray. Kept so it can be released on Dispose.</summary>
    Private ReadOnly _trayImage As GdiDrawing.Icon
    ''' <summary>
    ''' An invisible window that owns the tray menu. A native menu needs an owner window
    ''' to send its messages to and to be "foreground" while it's open.
    ''' </summary>
    Private ReadOnly _menuOwnerWindow As WinForms.NativeWindow
    Private _isDisposed As Boolean
#End Region

    ''' <summary>Creates the tray icon and shows it straight away.</summary>
    Public Sub New()
        _trayImage = LoadTrayImage()

        ' A plain hidden window: CreateParams with nothing set = no caption, not visible.
        _menuOwnerWindow = New WinForms.NativeWindow()
        _menuOwnerWindow.CreateHandle(New WinForms.CreateParams())

        _notifyIcon = New WinForms.NotifyIcon() With {
            .Icon = _trayImage,
            .Text = TooltipText
        }
        AddHandler _notifyIcon.MouseUp, AddressOf NotifyIcon_MouseUp
        AddHandler _notifyIcon.MouseDoubleClick, AddressOf NotifyIcon_MouseDoubleClick
        _notifyIcon.Visible = AppSettingsStore.Current.ShowTrayIcon
    End Sub

    ''' <summary>Shows or hides the icon (Settings > General). The icon object stays, so showing it again is instant.</summary>
    Public Sub SetShown(shouldShow As Boolean)
        If _isDisposed Then Return
        _notifyIcon.Visible = shouldShow
    End Sub

    ''' <summary>
    ''' Loads the app icon at the size Windows uses for tray icons (16x16 at 100% scaling,
    ''' larger on high-DPI screens). Falls back to Windows' generic program icon if the
    ''' resource can't be read, so a missing icon never stops Stages starting.
    ''' </summary>
    Private Shared Function LoadTrayImage() As GdiDrawing.Icon
        Try
            Dim iconResource As System.Windows.Resources.StreamResourceInfo =
                Application.GetResourceStream(New Uri(AppIconResourceUri))
            ' The Icon constructor copies the data, so the stream can be closed afterwards.
            Using iconStream As Stream = iconResource.Stream
                Return New GdiDrawing.Icon(iconStream, WinForms.SystemInformation.SmallIconSize)
            End Using
        Catch ex As Exception
            Logger.Warn("Could not load the tray icon image - using Windows' default", ex)
            Return New GdiDrawing.Icon(GdiDrawing.SystemIcons.Application, WinForms.SystemInformation.SmallIconSize)
        End Try
    End Function

#Region "Menu and mouse handlers"

    ''' <summary>Right-click (on release, as Windows does it): show the tray menu.</summary>
    Private Sub NotifyIcon_MouseUp(sender As Object, e As WinForms.MouseEventArgs)
        If e.Button <> WinForms.MouseButtons.Right Then Return
        ShowTrayMenu()
    End Sub

    Private Sub NotifyIcon_MouseDoubleClick(sender As Object, e As WinForms.MouseEventArgs)
        If e.Button = WinForms.MouseButtons.Left Then
            RunAfterMenuCloses(AddressOf SettingsWindow.ShowSettings)
        End If
    End Sub

    ''' <summary>Builds the menu (so "Hide/Show Stages" is current), shows it at the mouse, and acts on the choice.</summary>
    Private Sub ShowTrayMenu()
        Dim chosenAction As Action = Nothing
        Dim hideOrShowText As String = If(StageManager.Instance.AreStagesHidden, ShowStagesText, HideStagesText)

        Try
            Using trayMenu As New NativePopupMenu()
                Dim newStageMenu As NativePopupMenu = trayMenu.AddSubmenu(NewStageText)
                newStageMenu.AddItem(NewStandardStageText, Sub() StageManager.Instance.CreateStageOfKind(StageKind.Standard))
                newStageMenu.AddItem(NewLiveFolderStageText, AddressOf CreateLiveFolderStage)
                newStageMenu.AddItem(NewRunningTasksStageText, Sub() StageManager.Instance.CreateStageOfKind(StageKind.RunningTasks))
                newStageMenu.AddItem(NewClipboardStageText, Sub() StageManager.Instance.CreateStageOfKind(StageKind.ClipboardHistory))
                trayMenu.AddSeparator()

                trayMenu.AddItem(hideOrShowText,
                                 Sub() StageManager.Instance.SetStagesHidden(Not StageManager.Instance.AreStagesHidden))
                trayMenu.AddItem(OpenSettingsText, AddressOf SettingsWindow.ShowSettings, isDefault:=True)
                trayMenu.AddSeparator()
                trayMenu.AddItem(QuitStagesText, AddressOf QuitStages)

                Dim cursorPosition As NativeMethods.NativePoint = DesktopIntegration.GetCursorScreenPosition()
                chosenAction = trayMenu.ShowAndGetChoice(_menuOwnerWindow.Handle, cursorPosition.X, cursorPosition.Y)
            End Using
        Catch ex As Exception
            Logger.Error("Could not show the tray menu", ex)
        End Try

        If chosenAction IsNot Nothing Then
            RunAfterMenuCloses(chosenAction)
        End If
    End Sub

    ''' <summary>
    ''' Asks for a folder (Windows' folder picker, kept in front by a DialogHost, as there is
    ''' no Stage to own it), then makes a Live folder Stage showing it.
    ''' </summary>
    Private Shared Sub CreateLiveFolderStage()
        Dim chosenFolder As String
        Using host As New DialogHost(Nothing)
            chosenFolder = FolderPicker.ChooseFolder(host.Handle, ChooseLiveFolderTitle)
        End Using
        If chosenFolder Is Nothing Then Return ' cancelled
        StageManager.Instance.CreateFolderStage(chosenFolder)
    End Sub

    Private Shared Sub QuitStages()
        Logger.Info("Quit chosen from the tray menu")
        StageManager.Instance.ShutDown()
    End Sub

    ''' <summary>
    ''' Queues the action on the WPF UI thread instead of running it straight away,
    ''' so the menu has fully closed before any window opens or the app shuts down.
    ''' </summary>
    Private Shared Sub RunAfterMenuCloses(work As Action)
        Dim currentApplication As Application = Application.Current
        If currentApplication IsNot Nothing Then
            currentApplication.Dispatcher.BeginInvoke(work)
        End If
    End Sub

#End Region

    ''' <summary>
    ''' Removes the icon from the tray and releases its Windows resources. Must be called
    ''' when Stages exits, or a "ghost" icon stays in the tray until the mouse passes over it.
    ''' </summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        If _isDisposed Then Return
        _isDisposed = True

        _notifyIcon.Visible = False
        _notifyIcon.Dispose()
        _trayImage.Dispose()
        _menuOwnerWindow.DestroyHandle()
    End Sub

End Class
