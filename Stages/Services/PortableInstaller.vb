' ============================================================================
'  PortableInstaller.vb   (v0.8.5)
'  Settings > Install: turns a "portable" copy of Stages (one run straight from
'  wherever it was built or downloaded) into a normally installed Windows
'  program - and the matching uninstaller that Windows' Apps list runs.
'
'  INSTALLING (Install, from the Settings page):
'    1. Copies Stages.exe (and Stages.exe.config) into
'       %LOCALAPPDATA%\Programs\Stages\App - the same folder the Inno Setup
'       installer uses, beside the Stages' own Data folder, which is left
'       exactly as it is.
'    2. Start menu: a "Stages" shortcut in the user's own Start menu (if
'       chosen; the same place the Inno installer puts it).
'    3. Start with Windows: the usual per-user Run entry, now pointing at the
'       installed copy (if chosen) - see StartupRegistration.
'    4. Windows' Apps list ("Installed apps" / "Add or remove programs"): a
'       per-user uninstall entry whose Uninstall button runs
'       "Stages.exe /uninstall". Skipped if the Inno installer's entry is
'       already there (that one already lists Stages and uninstalls it).
'    5. Starts the installed copy with AfterInstallArgument and quits. The new
'       copy waits for this one to exit (Program.vb), then sends the portable
'       copy to the Recycle Bin if that was chosen, and says it's done.
'
'  Everything is per user (the user's own AppData folder and HKEY_CURRENT_USER),
'  so no administrator rights are needed - and none are asked for: run as an
'  administrator, Stages would install into the ADMINISTRATOR's account, not
'  the user's.
'
'  UNINSTALLING (Windows Settings > Apps > Stages > Uninstall, which runs
'  "Stages.exe /uninstall" - Program.Main hands it straight to RunUninstaller,
'  before any window is made):
'    asks to confirm; asks whether to remove the Stages and settings too (to
'    the Recycle Bin; the default is to keep them); asks a running Stages to
'    quit; removes the Start menu shortcut, the Run entry and the Apps entry;
'    then a hidden command prompt removes the App folder a moment after this
'    program has exited (a running program can't delete itself).
' ============================================================================
Imports System.Diagnostics
Imports System.IO
Imports System.Threading
Imports Microsoft.Win32
Imports Stages.Native

Namespace Services

    ''' <summary>What the person chose on the Install page.</summary>
    Public NotInheritable Class InstallChoices
        ''' <summary>Start Stages when signing in to Windows.</summary>
        Public Property StartWithWindows As Boolean = True
        ''' <summary>Put a Stages shortcut in the Start menu.</summary>
        Public Property AddToStartMenu As Boolean = True
        ''' <summary>Send the portable copy (the .exe that's running now) to the Recycle Bin afterwards.</summary>
        Public Property RemovePortableCopy As Boolean = True
    End Class

    ''' <summary>How installing went.</summary>
    Public NotInheritable Class InstallResult
        ''' <summary>True if Stages was copied into its install folder (the part that matters).</summary>
        Public Property ProgramCopied As Boolean
        ''' <summary>What went wrong, in a sentence, or Nothing if everything worked.</summary>
        Public Property Problem As String
    End Class

    Public NotInheritable Class PortableInstaller

#Region "Constants"
        ''' <summary>Start the installed copy with this to finish installing (Program.vb).</summary>
        Public Const AfterInstallArgument As String = "--after-install"
        ''' <summary>...followed by this and a path: the portable copy to send to the Recycle Bin.</summary>
        Public Const RemovePortableArgument As String = "--remove-portable"
        ''' <summary>Windows' Apps list runs Stages.exe with this to uninstall it.</summary>
        Public Const UninstallArgument As String = "/uninstall"

        Private Const InstalledProgramFileName As String = "Stages.exe"
        ''' <summary>A .NET program's settings file sits beside it, named after it.</summary>
        Private Const ProgramConfigSuffix As String = ".config"
        Private Const StartMenuShortcutFileName As String = "Stages.lnk"

        ''' <summary>Where Windows keeps the per-user Apps list entries.</summary>
        Private Const UninstallKeysPath As String = "Software\Microsoft\Windows\CurrentVersion\Uninstall"
        ''' <summary>The Apps list entry this installer writes.</summary>
        Private Const OwnUninstallKeyName As String = "Stages"
        ''' <summary>
        ''' The Inno Setup installer's entry: its AppId (Installer\Stages.iss) followed by "_is1",
        ''' as Inno Setup names them. If it exists, Stages is already listed.
        ''' </summary>
        Private Const InnoSetupUninstallKeyName As String = "{D659C42D-1372-45F1-A400-44FC04B3EECE}_is1"

        ' What the Apps list shows.
        Private Const DisplayNameText As String = "Stages"
        Private Const PublisherText As String = "x87e"
        Private Const ProjectWebsite As String = "https://github.com/x87e/stages"
        ''' <summary>Windows reads 1 as "no Modify / Repair buttons".</summary>
        Private Const RegistryTrue As Integer = 1
        Private Const BytesPerKilobyte As Long = 1024

        ''' <summary>How long the uninstaller waits for a running Stages to quit.</summary>
        Private Shared ReadOnly WaitForRunningCopyToQuit As TimeSpan = TimeSpan.FromSeconds(30)
        ''' <summary>How long the new copy keeps trying to recycle the old one while its file is still in use.</summary>
        Private Shared ReadOnly WaitForPortableFileToBeFree As TimeSpan = TimeSpan.FromSeconds(10)
        Private Const FileFreeCheckIntervalMilliseconds As Integer = 250

        ' Messages.
        Private Const UninstallCaption As String = "Uninstall Stages"
        Private Const ConfirmUninstallText As String = "Remove Stages from this PC?"
        Private Const RemoveDataQuestionText As String =
            "Also remove your Stages, their shortcuts and your settings?" & vbCrLf & vbCrLf &
            "Yes sends them to the Recycle Bin. No keeps them, ready for when Stages is installed again."
        Private Const CouldNotQuitText As String =
            "Stages is still running and didn't close. Quit it (right-click its icon in the notification area > Quit Stages), then try uninstalling again."
        Private Const UninstalledText As String = "Stages has been removed from this PC."
        Private Const UninstalledKeptDataText As String =
            "Stages has been removed from this PC. Your Stages and settings were kept."
#End Region

        Private Sub New()
            ' Shared members only.
        End Sub

#Region "Where things are"

        ''' <summary>The Stages.exe that's running now.</summary>
        Public Shared ReadOnly Property RunningProgramPath As String
            Get
                Return StartupRegistration.CurrentProgramPath
            End Get
        End Property

        ''' <summary>Where the installed Stages.exe lives (or will).</summary>
        Public Shared ReadOnly Property InstalledProgramPath As String
            Get
                Return Path.Combine(AppPaths.AppFolder, InstalledProgramFileName)
            End Get
        End Property

        ''' <summary>The user's own Start menu shortcut for Stages.</summary>
        Private Shared ReadOnly Property StartMenuShortcutPath As String
            Get
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), StartMenuShortcutFileName)
            End Get
        End Property

        ''' <summary>True if this copy is the installed one (it runs from the App folder).</summary>
        Public Shared Function IsRunningInstalledCopy() As Boolean
            Return SameFolder(Path.GetDirectoryName(RunningProgramPath), AppPaths.AppFolder)
        End Function

        ''' <summary>True if an installed Stages.exe is already there (so installing replaces it).</summary>
        Public Shared Function IsAlreadyInstalled() As Boolean
            Return File.Exists(InstalledProgramPath)
        End Function

        ''' <summary>The installed copy's version ("0.8.4.0"), or Nothing if it can't be read.</summary>
        Public Shared Function InstalledVersion() As String
            Try
                Return FileVersionInfo.GetVersionInfo(InstalledProgramPath).FileVersion
            Catch ex As Exception
                Return Nothing
            End Try
        End Function

        ''' <summary>True if two folder paths are the same folder (ignoring case and a trailing "\").</summary>
        Private Shared Function SameFolder(firstFolder As String, secondFolder As String) As Boolean
            If String.IsNullOrEmpty(firstFolder) OrElse String.IsNullOrEmpty(secondFolder) Then Return False
            Dim first As String = Path.GetFullPath(firstFolder).TrimEnd(Path.DirectorySeparatorChar)
            Dim second As String = Path.GetFullPath(secondFolder).TrimEnd(Path.DirectorySeparatorChar)
            Return String.Equals(first, second, StringComparison.OrdinalIgnoreCase)
        End Function

#End Region

#Region "Installing"

        ''' <summary>
        ''' Steps 1-4 (see the top of the file). The log has the details of any problem.
        ''' Doesn't restart - see RestartIntoInstalledCopy.
        ''' </summary>
        Public Shared Function Install(choices As InstallChoices) As InstallResult
            Logger.Info("Installing Stages from " & RunningProgramPath & " to " & AppPaths.AppFolder)
            Try
                CopyProgramFiles()
            Catch ex As Exception
                Logger.Error("Could not copy Stages into its install folder", ex)
                Return New InstallResult() With {
                    .ProgramCopied = False,
                    .Problem = "Stages couldn't be copied into " & AppPaths.AppFolder & ", so it hasn't been installed. (" & ex.Message & ")"
                }
            End Try

            ' The copy is what matters; the rest is convenience, so each part is tried on its own.
            Dim problems As New List(Of String)()
            If Not TryUpdateStartMenuShortcut(choices.AddToStartMenu) Then problems.Add("the Start menu shortcut")
            If Not StartupRegistration.SetEnabled(choices.StartWithWindows, InstalledProgramPath) Then problems.Add("starting with Windows")
            If Not TryWriteAppsListEntry() Then problems.Add("the entry in Windows' Apps list")

            Dim result As New InstallResult() With {.ProgramCopied = True}
            If problems.Count > 0 Then
                result.Problem = "Stages was installed, but these couldn't be set up: " & String.Join(", ", problems) & ". The log file has the details."
            End If
            Return result
        End Function

        ''' <summary>Copies the running program (and its .config file, if there is one) into the App folder.</summary>
        Private Shared Sub CopyProgramFiles()
            Directory.CreateDirectory(AppPaths.AppFolder)
            Const ReplaceExisting As Boolean = True
            File.Copy(RunningProgramPath, InstalledProgramPath, ReplaceExisting)

            ' The running file may have been renamed (e.g. "Stages-0.8.5.exe"); the installed one is
            ' always Stages.exe, so its .config is copied under the matching name.
            Dim runningConfigPath As String = RunningProgramPath & ProgramConfigSuffix
            If File.Exists(runningConfigPath) Then
                File.Copy(runningConfigPath, InstalledProgramPath & ProgramConfigSuffix, ReplaceExisting)
            End If
        End Sub

        ''' <summary>Adds the Start menu shortcut (or removes one if it wasn't wanted). True if that worked.</summary>
        Private Shared Function TryUpdateStartMenuShortcut(isWanted As Boolean) As Boolean
            Try
                If isWanted Then
                    ShellLinks.CreateShortcut(StartMenuShortcutPath, InstalledProgramPath)
                ElseIf File.Exists(StartMenuShortcutPath) Then
                    File.Delete(StartMenuShortcutPath)
                End If
                Return True
            Catch ex As Exception
                Logger.Error("Could not update the Start menu shortcut", ex)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Writes the per-user Apps list entry (HKEY_CURRENT_USER, so no administrator rights).
        ''' Leaves it to the Inno installer's entry if that one exists. True if all is well.
        ''' </summary>
        Private Shared Function TryWriteAppsListEntry() As Boolean
            Try
                If InnoSetupEntryExists() Then
                    Logger.Info("The installer's Apps list entry already exists; not adding another")
                    Return True
                End If
                Dim quotedProgram As String = ControlChars.Quote & InstalledProgramPath & ControlChars.Quote
                Using entryKey As RegistryKey = Registry.CurrentUser.CreateSubKey(UninstallKeysPath & "\" & OwnUninstallKeyName)
                    entryKey.SetValue("DisplayName", DisplayNameText, RegistryValueKind.String)
                    entryKey.SetValue("DisplayVersion", ProgramVersionText(), RegistryValueKind.String)
                    entryKey.SetValue("Publisher", PublisherText, RegistryValueKind.String)
                    entryKey.SetValue("DisplayIcon", InstalledProgramPath & ",0", RegistryValueKind.String) ' its first icon
                    entryKey.SetValue("InstallLocation", AppPaths.AppFolder, RegistryValueKind.String)
                    entryKey.SetValue("UninstallString", quotedProgram & " " & UninstallArgument, RegistryValueKind.String)
                    entryKey.SetValue("URLInfoAbout", ProjectWebsite, RegistryValueKind.String)
                    entryKey.SetValue("InstallDate", Date.Today.ToString("yyyyMMdd"), RegistryValueKind.String)
                    entryKey.SetValue("EstimatedSize", InstalledSizeInKilobytes(), RegistryValueKind.DWord)
                    entryKey.SetValue("NoModify", RegistryTrue, RegistryValueKind.DWord)
                    entryKey.SetValue("NoRepair", RegistryTrue, RegistryValueKind.DWord)
                End Using
                Return True
            Catch ex As Exception
                Logger.Error("Could not add Stages to Windows' Apps list", ex)
                Return False
            End Try
        End Function

        ''' <summary>True if the Inno Setup installer has already listed Stages.</summary>
        Private Shared Function InnoSetupEntryExists() As Boolean
            Using innoKey As RegistryKey = Registry.CurrentUser.OpenSubKey(UninstallKeysPath & "\" & InnoSetupUninstallKeyName)
                Return innoKey IsNot Nothing
            End Using
        End Function

        ''' <summary>"0.8.5" - the version without its last (always 0) part, as the Apps list shows it.</summary>
        Private Shared Function ProgramVersionText() As String
            Const MajorMinorBuildParts As Integer = 3
            Return GetType(PortableInstaller).Assembly.GetName().Version.ToString(MajorMinorBuildParts)
        End Function

        ''' <summary>The App folder's size, for the Apps list (it shows it as the app's size).</summary>
        Private Shared Function InstalledSizeInKilobytes() As Integer
            Dim totalBytes As Long = 0
            For Each installedFile As String In Directory.GetFiles(AppPaths.AppFolder)
                totalBytes += New FileInfo(installedFile).Length
            Next
            Return CInt(Math.Min(Integer.MaxValue, (totalBytes + BytesPerKilobyte - 1) \ BytesPerKilobyte))
        End Function

        ''' <summary>
        ''' Step 5: starts the installed copy (telling it which portable copy to recycle, if any)
        ''' and quits this one. False (and keeps running) if the installed copy couldn't be started.
        ''' </summary>
        Public Shared Function RestartIntoInstalledCopy(removePortableCopy As Boolean) As Boolean
            Dim arguments As String = AfterInstallArgument
            If removePortableCopy Then
                arguments &= " " & RemovePortableArgument & " " & ControlChars.Quote & RunningProgramPath & ControlChars.Quote
            End If
            Try
                Dim startInfo As New ProcessStartInfo(InstalledProgramPath, arguments) With {
                    .UseShellExecute = False,
                    .WorkingDirectory = AppPaths.AppFolder
                }
                Process.Start(startInfo)
            Catch ex As Exception
                Logger.Error("Could not start the installed copy of Stages", ex)
                Return False
            End Try
            Logger.Info("Installed - handing over to the installed copy")
            StageManager.Instance.ShutDown()
            Return True
        End Function

        ''' <summary>
        ''' Run by the installed copy once the portable one has exited: sends the portable
        ''' Stages.exe (and its .config) to the Recycle Bin. Only ever a .exe outside the App
        ''' folder - never the installed copy - and it waits a moment if the file is still in
        ''' use as the old copy finishes closing. Problems are logged and otherwise ignored.
        ''' </summary>
        Public Shared Sub RemovePortableCopy(portableProgramPath As String)
            Try
                If String.IsNullOrEmpty(portableProgramPath) OrElse Not File.Exists(portableProgramPath) Then Return
                If Not String.Equals(Path.GetExtension(portableProgramPath), ".exe", StringComparison.OrdinalIgnoreCase) Then Return
                If SameFolder(Path.GetDirectoryName(portableProgramPath), AppPaths.AppFolder) Then Return

                WaitUntilFileIsFree(portableProgramPath)
                StageFolder.SendToRecycleBin(portableProgramPath)
                Dim portableConfigPath As String = portableProgramPath & ProgramConfigSuffix
                If File.Exists(portableConfigPath) Then StageFolder.SendToRecycleBin(portableConfigPath)
                Logger.Info("Sent the portable copy to the Recycle Bin: " & portableProgramPath)
            Catch ex As Exception
                Logger.Warn("Could not remove the portable copy " & portableProgramPath, ex)
            End Try
        End Sub

        ''' <summary>Waits (up to a few seconds) until nothing has the file open any more.</summary>
        Private Shared Sub WaitUntilFileIsFree(filePath As String)
            Dim giveUpAt As Date = Date.UtcNow.Add(WaitForPortableFileToBeFree)
            Do
                Try
                    Using exclusiveProbe As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None)
                    End Using
                    Return ' opened with no sharing: nobody else is using it
                Catch ex As IOException
                    Thread.Sleep(FileFreeCheckIntervalMilliseconds)
                End Try
            Loop While Date.UtcNow < giveUpAt
        End Sub

#End Region

#Region "Uninstalling"

        ''' <summary>
        ''' The whole uninstall, run instead of the normal app (Program.Main) when Windows' Apps
        ''' list starts Stages with UninstallArgument. Uses plain message boxes: no Stages
        ''' window exists in this mode.
        ''' </summary>
        Public Shared Sub RunUninstaller()
            Logger.Info("Uninstall started")
            If MessageBox.Show(ConfirmUninstallText, UninstallCaption, MessageBoxButton.YesNo,
                               MessageBoxImage.Question, MessageBoxResult.No) <> MessageBoxResult.Yes Then
                Logger.Info("Uninstall cancelled")
                Return
            End If
            Dim removeData As Boolean =
                MessageBox.Show(RemoveDataQuestionText, UninstallCaption, MessageBoxButton.YesNo,
                                MessageBoxImage.Question, MessageBoxResult.No) = MessageBoxResult.Yes

            ' A running Stages would keep its files open and save them again on the way out.
            Using singleInstanceLock As New Mutex(False, Program.SingleInstanceMutexName)
                If Not TakeOverFromRunningCopy(singleInstanceLock) Then
                    MessageBox.Show(CouldNotQuitText, UninstallCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
                    Return
                End If

                TryUpdateStartMenuShortcut(isWanted:=False)
                StartupRegistration.SetEnabled(False)
                TryDeleteAppsListEntry()
                ' Logged now: once the data (with the Logs folder) is in the Recycle Bin, another
                ' log line would make a new Logs folder and leave the Stages folder behind.
                Logger.Info("Uninstalling" & If(removeData, " - sending the Stages and settings to the Recycle Bin", " - keeping the Stages and settings"))
                If removeData Then RecycleStagesData()
            End Using

            MessageBox.Show(If(removeData, UninstalledText, UninstalledKeptDataText), UninstallCaption,
                            MessageBoxButton.OK, MessageBoxImage.Information)
            ' Last of all, so this program has exited by the time the folder is removed - however
            ' long the message above was left open.
            ScheduleAppFolderRemoval(alsoRemoveRootIfEmpty:=removeData)
        End Sub

        ''' <summary>
        ''' Takes the single-instance lock, first asking a running Stages to quit if it has it.
        ''' True once this process holds the lock.
        ''' </summary>
        Private Shared Function TakeOverFromRunningCopy(singleInstanceLock As Mutex) As Boolean
            Try
                If singleInstanceLock.WaitOne(0) Then Return True ' nothing running
                Program.AskRunningCopyToQuit()
                Return singleInstanceLock.WaitOne(WaitForRunningCopyToQuit)
            Catch ex As AbandonedMutexException
                Return True ' the running copy ended without releasing the lock - it's ours now
            End Try
        End Function

        ''' <summary>Removes this installer's Apps list entry (Windows removes the Inno one itself).</summary>
        Private Shared Sub TryDeleteAppsListEntry()
            Try
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKeysPath & "\" & OwnUninstallKeyName, throwOnMissingSubKey:=False)
            Catch ex As Exception
                Logger.Warn("Could not remove the Apps list entry", ex)
            End Try
        End Sub

        ''' <summary>
        ''' Sends everything in the Stages folder EXCEPT the App folder (the Stages, settings,
        ''' logs...) to the Recycle Bin. The App folder is removed after this program exits.
        ''' </summary>
        Private Shared Sub RecycleStagesData()
            If Not Directory.Exists(AppPaths.RootFolder) Then Return
            For Each entryPath As String In Directory.GetFileSystemEntries(AppPaths.RootFolder)
                If SameFolder(entryPath, AppPaths.AppFolder) Then Continue For
                Try
                    StageFolder.SendToRecycleBin(entryPath)
                Catch ex As Exception
                    ' Not logged: the Logs folder may be gone already (see RunUninstaller). Windows
                    ' shows its own message if a file couldn't be moved.
                End Try
            Next
        End Sub

        ''' <summary>
        ''' A running program can't delete its own file, so a hidden command prompt does it: it
        ''' waits about two seconds (ping is the usual way to pause in a hidden prompt - "timeout"
        ''' needs a keyboard), then removes the App folder, and the (now empty) Stages folder if
        ''' the data went too. Only ever the App folder under the user's own AppData.
        ''' </summary>
        Private Shared Sub ScheduleAppFolderRemoval(alsoRemoveRootIfEmpty As Boolean)
            Dim appFolder As String = AppPaths.AppFolder
            ' Belt and braces: the folder must be the one AppPaths names, and hold Stages.exe.
            If Not Directory.Exists(appFolder) OrElse Not File.Exists(InstalledProgramPath) Then Return

            Const PauseCommand As String = "ping -n 3 127.0.0.1 >nul"
            Dim command As String = PauseCommand & " & rmdir /s /q " & ControlChars.Quote & appFolder & ControlChars.Quote
            If alsoRemoveRootIfEmpty Then
                ' Without /s, rmdir only removes a folder that's empty - so nothing else can go.
                command &= " & rmdir " & ControlChars.Quote & AppPaths.RootFolder & ControlChars.Quote
            End If
            Try
                Dim startInfo As New ProcessStartInfo("cmd.exe", "/d /c " & command) With {
                    .UseShellExecute = False,
                    .CreateNoWindow = True,
                    .WindowStyle = ProcessWindowStyle.Hidden,
                    .WorkingDirectory = Path.GetTempPath() ' not inside the folder being removed
                }
                Process.Start(startInfo)
            Catch ex As Exception
                Logger.Error("Could not schedule removing the App folder", ex)
            End Try
        End Sub

#End Region

    End Class

End Namespace
