' ============================================================================
'  Program.vb
'  Entry point of Stages.exe.
'
'  Responsibilities:
'    * only ever allow ONE copy of Stages to run,
'    * install safety nets so an unexpected error is logged instead of
'      silently killing every Stage,
'    * load the app-wide settings and the saved Stages (or create a first
'      one), add the tray icon, and run the WPF app,
'    * when Stages is started again while it's already running, open
'      Settings in the running copy (and show the Stages if they're hidden).
'      That is the way back in when the tray icon has been switched off
'      (Settings > General), so nobody can lock themselves out (v0.8.4),
'    * the other jobs Stages.exe can be started for (v0.8.5, see
'      PortableInstaller.vb): "/uninstall" (Windows' Apps list) runs the
'      uninstaller instead of the app; "--after-install" (Settings > Install)
'      waits for the portable copy to exit, then finishes installing. The
'      running copy also listens for a "quit" signal, which the uninstaller
'      sets so the files can be removed.
' ============================================================================
Imports System.Threading
Imports System.Windows.Threading
Imports Stages.Model
Imports Stages.Services

Public Module Program

    ''' <summary>
    ''' Name of the system-wide lock that marks "Stages is already running".
    ''' "Local\" = per logged-in user session.
    ''' </summary>
    Friend Const SingleInstanceMutexName As String = "Local\Stages.SingleInstance"

    ''' <summary>The signal the uninstaller sets to ask the running copy to quit.</summary>
    Private Const QuitSignalName As String = "Local\Stages.Quit"
    ''' <summary>The running copy's end of that signal, and the background wait on it. Kept until exit.</summary>
    Private _quitSignal As EventWaitHandle
    Private _quitWait As RegisteredWaitHandle

    ''' <summary>True if this copy was started by Settings > Install to finish installing.</summary>
    Private _isFinishingInstall As Boolean
    ''' <summary>The portable copy to send to the Recycle Bin once installed, or Nothing.</summary>
    Private _portableCopyToRemove As String

    Private Const InstallFinishedCaption As String = "Install Stages"
    Private Const InstallFinishedText As String =
        "Stages is installed. You'll find it in the Start menu, and it can be uninstalled from Windows Settings > Apps, like any other program."

    ''' <summary>
    ''' Name of the signal a second copy of Stages sets to say "open Settings" to the copy
    ''' that's running. "Local\" = per logged-in user session, like the lock above.
    ''' </summary>
    Private Const OpenSettingsSignalName As String = "Local\Stages.OpenSettings"

    ''' <summary>The running copy's end of that signal, and the background wait on it. Kept until exit.</summary>
    Private _openSettingsSignal As EventWaitHandle
    Private _openSettingsWait As RegisteredWaitHandle

    ''' <summary>
    ''' After an import, the restarted copy waits this long for the old copy to finish
    ''' saving and exit. Normally it takes well under a second.
    ''' </summary>
    Private ReadOnly WaitForPreviousCopyToExit As TimeSpan = TimeSpan.FromSeconds(30)

    ''' <summary>True if this copy was started by Settings > Backup to finish an import.</summary>
    Private _isFinishingImport As Boolean
    ''' <summary>How finishing the import went (shown once the Stages are up).</summary>
    Private _importOutcome As ImportOutcome = ImportOutcome.Succeeded

    Private Const ImportFinishedCaption As String = "Import backup"
    Private Const ImportFinishedText As String = "The backup has been imported. Your previous Stages and settings are in the Recycle Bin, if you need them back."
    Private Const ImportFailedText As String =
        "The backup couldn't be imported, so your Stages and settings haven't been changed. The log file has the details."

    <STAThread()>
    Public Sub Main()
        Dim commandLineArguments As List(Of String) = Environment.GetCommandLineArgs().Skip(1).ToList()

        ' Windows' Apps list > Uninstall: only the uninstaller runs, no Stages appear.
        If HasArgument(commandLineArguments, PortableInstaller.UninstallArgument) Then
            PortableInstaller.RunUninstaller()
            Return
        End If

        _isFinishingImport = HasArgument(commandLineArguments, BackupArchive.FinishImportArgument)
        _isFinishingInstall = HasArgument(commandLineArguments, PortableInstaller.AfterInstallArgument)
        _portableCopyToRemove = ArgumentAfter(commandLineArguments, PortableInstaller.RemovePortableArgument)

        Dim isFirstInstance As Boolean
        ' The Using block holds the mutex for the whole lifetime of the app.
        Using singleInstanceLock As New Mutex(True, SingleInstanceMutexName, isFirstInstance)
            If Not isFirstInstance Then
                ' Another copy is already managing the Stages - quietly leave. Unless this
                ' copy was started to finish an import or an install: then the other copy is
                ' on its way out, so wait for it.
                If Not _isFinishingImport AndAlso Not _isFinishingInstall Then
                    AskRunningCopyToOpenSettings()
                    Return
                End If
                If Not WaitForPreviousCopy(singleInstanceLock) Then
                    Return
                End If
            End If

            Dim stagesApp As New Application()
            ' Stages can be destroyed and re-created (e.g. Explorer restarts), so the app must
            ' NOT quit automatically when its window count briefly drops to zero.
            ' StageManager decides when to quit.
            stagesApp.ShutdownMode = ShutdownMode.OnExplicitShutdown

            InstallGlobalErrorHandlers(stagesApp)
            AddHandler stagesApp.Startup, AddressOf OnApplicationStartup
            AddHandler stagesApp.SessionEnding, AddressOf OnWindowsSessionEnding
            ' [Exit] is bracketed because Exit is also a VB keyword.
            AddHandler stagesApp.[Exit], AddressOf OnApplicationExit

            stagesApp.Run()
        End Using
    End Sub

    ''' <summary>True if <paramref name="wanted"/> is one of the command-line arguments (any letter case).</summary>
    Private Function HasArgument(arguments As List(Of String), wanted As String) As Boolean
        Return arguments.Any(Function(argument) String.Equals(argument, wanted, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>The argument that follows <paramref name="name"/> (e.g. a path), or Nothing.</summary>
    Private Function ArgumentAfter(arguments As List(Of String), name As String) As String
        For argumentIndex As Integer = 0 To arguments.Count - 2
            If String.Equals(arguments(argumentIndex), name, StringComparison.OrdinalIgnoreCase) Then
                Return arguments(argumentIndex + 1)
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' Uninstaller: sets the running copy's "quit" signal. If there's no signal (the running
    ''' copy is too old to have one), nothing happens and the uninstaller says so.
    ''' </summary>
    Friend Sub AskRunningCopyToQuit()
        Try
            Using signal As EventWaitHandle = EventWaitHandle.OpenExisting(QuitSignalName)
                signal.Set()
            End Using
        Catch ex As WaitHandleCannotBeOpenedException
            Logger.Warn("The running copy of Stages has no quit signal", ex)
        Catch ex As Exception
            Logger.Warn("Could not ask the running copy of Stages to quit", ex)
        End Try
    End Sub

    ''' <summary>Running copy: waits (on the thread pool) for the uninstaller's "quit" signal.</summary>
    Private Sub StartListeningForQuit()
        Try
            _quitSignal = New EventWaitHandle(False, EventResetMode.AutoReset, QuitSignalName)
            Const WaitForever As Integer = Timeout.Infinite
            Const ExecuteOnlyOnce As Boolean = True ' quitting happens once
            _quitWait = ThreadPool.RegisterWaitForSingleObject(
                _quitSignal, AddressOf OnQuitSignalled, Nothing, WaitForever, ExecuteOnlyOnce)
        Catch ex As Exception
            Logger.Error("Could not listen for the uninstaller", ex)
        End Try
    End Sub

    ''' <summary>The uninstaller asked: quit cleanly, on the UI thread.</summary>
    Private Sub OnQuitSignalled(state As Object, timedOut As Boolean)
        Dim currentApplication As Application = Application.Current
        If currentApplication Is Nothing Then Return
        currentApplication.Dispatcher.BeginInvoke(New Action(
            Sub()
                Logger.Info("The uninstaller asked Stages to quit")
                StageManager.Instance.ShutDown()
            End Sub))
    End Sub

    ''' <summary>
    ''' Installed copy, started by Settings > Install: once the Stages are up, recycles the
    ''' portable copy (if asked to) and says it's done. Queued, like the import message.
    ''' </summary>
    Private Sub FinishInstalling()
        Dispatcher.CurrentDispatcher.BeginInvoke(New Action(
            Sub()
                PortableInstaller.RemovePortableCopy(_portableCopyToRemove)
                Using host As New DialogHost(Nothing)
                    MessageBox.Show(host.OwnerWindow, InstallFinishedText, InstallFinishedCaption,
                                    MessageBoxButton.OK, MessageBoxImage.Information)
                End Using
            End Sub), DispatcherPriority.ApplicationIdle)
    End Sub

    ''' <summary>
    ''' Waits for the copy of Stages that's quitting to let go of the single-instance lock.
    ''' True once this copy holds it; False if the old copy is still running after the wait.
    ''' </summary>
    Private Function WaitForPreviousCopy(singleInstanceLock As Mutex) As Boolean
        Try
            Return singleInstanceLock.WaitOne(WaitForPreviousCopyToExit)
        Catch ex As AbandonedMutexException
            ' The old copy exited without formally releasing the lock (normal when a process
            ' ends): this copy now holds it.
            Return True
        End Try
    End Function

    ''' <summary>
    ''' Second copy: sets the running copy's "open Settings" signal, after letting it come to
    ''' the front. If the running copy hasn't made its signal yet (it's still starting), there's
    ''' nothing to do - it will show itself anyway.
    ''' </summary>
    Private Sub AskRunningCopyToOpenSettings()
        Try
            Native.NativeMethods.AllowSetForegroundWindow(Native.NativeMethods.ASFW_ANY)
            Using signal As EventWaitHandle = EventWaitHandle.OpenExisting(OpenSettingsSignalName)
                signal.Set()
            End Using
        Catch ex As WaitHandleCannotBeOpenedException
            ' The running copy is still starting up (or is an older version without the signal).
        Catch ex As Exception
            Logger.Warn("Could not ask the running copy of Stages to open Settings", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Running copy: makes the "open Settings" signal and waits for it on a thread-pool thread
    ''' (no thread of its own is kept busy). A failure only loses this convenience.
    ''' </summary>
    Private Sub StartListeningForOpenSettings()
        Try
            _openSettingsSignal = New EventWaitHandle(False, EventResetMode.AutoReset, OpenSettingsSignalName)
            Const WaitForever As Integer = Timeout.Infinite
            Const ExecuteOnlyOnce As Boolean = False ' keep waiting after each signal
            _openSettingsWait = ThreadPool.RegisterWaitForSingleObject(
                _openSettingsSignal, AddressOf OnOpenSettingsSignalled, Nothing, WaitForever, ExecuteOnlyOnce)
        Catch ex As Exception
            Logger.Error("Could not listen for Stages being started again", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Another copy was started: show the Stages if they're hidden and open Settings. Called on
    ''' a thread-pool thread, so the work is handed to the UI thread.
    ''' </summary>
    Private Sub OnOpenSettingsSignalled(state As Object, timedOut As Boolean)
        Dim currentApplication As Application = Application.Current
        If currentApplication Is Nothing Then Return
        currentApplication.Dispatcher.BeginInvoke(New Action(
            Sub()
                Logger.Info("Stages was started again - opening Settings")
                If StageManager.Instance.AreStagesHidden Then
                    StageManager.Instance.SetStagesHidden(False)
                End If
                SettingsWindow.ShowSettings()
            End Sub))
    End Sub

    ''' <summary>Settings > General: shows or hides the tray icon to match the setting.</summary>
    Public Sub ApplyTrayIconSetting()
        If _trayIcon IsNot Nothing Then
            _trayIcon.SetShown(AppSettingsStore.Current.ShowTrayIcon)
        End If
    End Sub

    ''' <summary>The Stages icon in the notification area (hidden if Settings says so).</summary>
    Private _trayIcon As StagesTrayIcon
    ''' <summary>Reloads Stage icons when Windows' icons change. Created at startup, stopped at exit.</summary>
    Private _iconChangeWatcher As IconChangeWatcher

    ''' <summary>Runs once the WPF application is ready: loads settings, shows the saved Stages and the tray icon.</summary>
    Private Sub OnApplicationStartup(sender As Object, e As StartupEventArgs)
        Logger.Info("Stages " & GetType(Program).Assembly.GetName().Version.ToString() & " starting")
        ' Recorded because backdrop effects depend on both (see DesktopIntegration.ApplyBackdrop).
        Logger.Info("Windows " & Environment.OSVersion.Version.ToString() &
                    ", transparency effects " & If(Native.DesktopIntegration.AreTransparencyEffectsEnabled(), "on", "off"))

        ' App-wide settings first: every Stage reads its defaults and colours from them.
        ' Before anything is loaded: move an imported backup into place (Settings > Backup).
        If _isFinishingImport Then
            _importOutcome = BackupArchive.FinishPendingImport()
        End If

        AppSettingsStore.Load()
        ' Then the light/dark theme, before any themed window or menu can appear.
        ThemeManager.Apply()

        Dim stagesShown As Integer = StageManager.Instance.LoadStages()
        If stagesShown = 0 Then
            ' First run (or an empty Data folder) - start with one empty Stage.
            StageManager.Instance.CreateStage(StageDefaults.FirstStageName)
        End If

        ' A tray icon problem must never stop the Stages themselves from working.
        Try
            _trayIcon = New StagesTrayIcon()
        Catch ex As Exception
            Logger.Error("Could not create the tray icon", ex)
        End Try

        ' Starting Stages again opens Settings here (the way back in without a tray icon).
        StartListeningForOpenSettings()
        ' The uninstaller can ask this copy to quit.
        StartListeningForQuit()

        ' The hide/show-all shortcut. If another program owns the combination, it's logged and
        ' Settings > General says so; everything else carries on.
        Try
            GlobalHotkey.Instance.ApplyFromSettings()
        Catch ex As Exception
            Logger.Error("Could not set up the hide/show shortcut", ex)
        End Try

        ' Same for icon-change watching: without it, icons still update after a restart.
        Try
            _iconChangeWatcher = New IconChangeWatcher()
        Catch ex As Exception
            Logger.Error("Could not start watching for icon changes", ex)
        End Try

        If _isFinishingImport Then
            ReportImportOutcome()
        End If
        If _isFinishingInstall Then
            Logger.Info("Finishing installing")
            FinishInstalling()
        End If
    End Sub

    ''' <summary>Says whether the import worked, once the (imported) Stages are on screen.</summary>
    Private Sub ReportImportOutcome()
        Dim succeeded As Boolean = (_importOutcome = ImportOutcome.Succeeded)
        ' Queued, so the Stages finish appearing first. Owned by an on-top host so the
        ' message can't open behind other windows (no Stage owns it).
        Dispatcher.CurrentDispatcher.BeginInvoke(New Action(
            Sub()
                Using host As New DialogHost(Nothing)
                    MessageBox.Show(host.OwnerWindow,
                                    If(succeeded, ImportFinishedText, ImportFailedText),
                                    ImportFinishedCaption, MessageBoxButton.OK,
                                    If(succeeded, MessageBoxImage.Information, MessageBoxImage.Warning))
                End Using
            End Sub), DispatcherPriority.ApplicationIdle)
    End Sub

    ''' <summary>Windows is logging off or shutting down.</summary>
    Private Sub OnWindowsSessionEnding(sender As Object, e As SessionEndingCancelEventArgs)
        Logger.Info("Windows session ending (" & e.ReasonSessionEnding.ToString() & ")")
        StageManager.Instance.NotifyShuttingDown()
    End Sub

    Private Sub OnApplicationExit(sender As Object, e As ExitEventArgs)
        Logger.Info("Stages exiting")
        ' Remove the tray icon now, or it lingers until the mouse passes over it.
        If _trayIcon IsNot Nothing Then
            _trayIcon.Dispose()
            _trayIcon = Nothing
        End If
        If _openSettingsWait IsNot Nothing Then
            _openSettingsWait.Unregister(Nothing)
            _openSettingsWait = Nothing
        End If
        If _openSettingsSignal IsNot Nothing Then
            _openSettingsSignal.Dispose()
            _openSettingsSignal = Nothing
        End If
        If _quitWait IsNot Nothing Then
            _quitWait.Unregister(Nothing)
            _quitWait = Nothing
        End If
        If _quitSignal IsNot Nothing Then
            _quitSignal.Dispose()
            _quitSignal = Nothing
        End If
        Try
            GlobalHotkey.Instance.Dispose()
        Catch ex As Exception
            Logger.Warn("Could not release the hide/show shortcut", ex)
        End Try
        If _iconChangeWatcher IsNot Nothing Then
            _iconChangeWatcher.Dispose()
            _iconChangeWatcher = Nothing
        End If
    End Sub

#Region "Global error handling"

    ''' <summary>
    ''' Catch-alls for errors no other code handled, so an unexpected error is
    ''' logged instead of silently ending the process (as it did in NoFences).
    ''' </summary>
    Private Sub InstallGlobalErrorHandlers(stagesApp As Application)
        ' Errors on the UI thread (event handlers, timers, rendering): log and carry on.
        AddHandler stagesApp.DispatcherUnhandledException, AddressOf OnUiThreadException
        ' Errors on any other thread: can't be survived by .NET, but at least they get logged.
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnBackgroundThreadException
        ' Errors inside Tasks nobody awaited: log and mark as handled.
        AddHandler TaskScheduler.UnobservedTaskException, AddressOf OnUnobservedTaskException
    End Sub

    Private Sub OnUiThreadException(sender As Object, e As DispatcherUnhandledExceptionEventArgs)
        Logger.Error("Unhandled error on the UI thread (recovered, Stages kept running)", e.Exception)
        ' Handled = True keeps the app alive instead of letting WPF terminate it.
        e.Handled = True
    End Sub

    Private Sub OnBackgroundThreadException(sender As Object, e As UnhandledExceptionEventArgs)
        Logger.Error("Fatal unhandled error on a background thread (terminating=" & e.IsTerminating.ToString() & ")",
                     TryCast(e.ExceptionObject, Exception))
    End Sub

    Private Sub OnUnobservedTaskException(sender As Object, e As UnobservedTaskExceptionEventArgs)
        Logger.Error("Unobserved background task error", e.Exception)
        e.SetObserved()
    End Sub

#End Region

End Module
