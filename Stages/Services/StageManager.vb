' ============================================================================
'  StageManager.vb
'  Owns the set of Stages: loads them at startup, creates/renames/removes
'  them, saves their settings, and brings them back if Windows destroys them.
'
'  On disk (see AppPaths): every sub-folder of Data is one Stage. Its title,
'  position etc. live in a hidden ".stage-settings.xml" inside it; its items
'  are the shortcut files in it. A folder with no settings file (e.g. one you
'  create by hand in Explorer) still loads, as a Stage named after the folder.
'
'  The Data folder is watched while Stages runs, so Explorer and the desktop
'  stay in step both ways:
'    * folder renamed in Explorer  -> the Stage's title changes to match
'    * folder deleted/moved away   -> the Stage closes
'    * new folder created          -> a new Stage appears
'  (Renaming a Stage on the desktop renames its folder - see RenameStage.)
'  If a folder was renamed while Stages wasn't running, the folder name wins
'  at the next start.
'
'  It also hides/shows every Stage at once (tray icon) and pushes changes made
'  in the Settings window out to every Stage.
'
'  Robustness carried over from the NoFences rewrite:
'    * saves are atomic (temp file + swap) with a .bak of the previous version
'    * an unreadable settings file never stops the app starting
'    * windows destroyed by an Explorer restart are re-created
' ============================================================================
Imports System.Diagnostics
Imports System.IO
Imports System.Windows.Threading
Imports System.Xml.Serialization
Imports Stages.Model
Imports Stages.Native

Namespace Services

    Public NotInheritable Class StageManager

        ''' <summary>The single shared manager.</summary>
        Public Shared ReadOnly Property Instance As StageManager = New StageManager()

        ' --- Recovery timing ---------------------------------------------------------
        ''' <summary>How often to check whether Explorer is back after it vanished.</summary>
        Private Shared ReadOnly RecoveryPollInterval As TimeSpan = TimeSpan.FromSeconds(1)
        ''' <summary>Explorer must be seen on this many polls in a row before we trust it is fully back.</summary>
        Private Const RequiredConsecutiveDesktopSightings As Integer = 2
        ''' <summary>Give up waiting for Explorer after this many polls (~10 minutes).</summary>
        Private Const MaximumRecoveryPolls As Integer = 600

        ''' <summary>Re-used for every load/save (creating XmlSerializers is slow).</summary>
        Private ReadOnly _serializer As New XmlSerializer(GetType(StageInfo))
        Private ReadOnly _saveLock As New Object()

        ''' <summary>Stage id -> its window, for every Stage currently on screen.</summary>
        Private ReadOnly _openWindows As New Dictionary(Of Guid, StageWindow)()
        ''' <summary>Stages whose windows were destroyed by Windows and are waiting to be re-created.</summary>
        Private ReadOnly _stagesAwaitingRecovery As New List(Of StageInfo)()

        ''' <summary>Reports Stage folders created, renamed or deleted in Data (by Explorer or anything else).</summary>
        Private _dataFolderWatcher As FileSystemWatcher

        Private _recoveryTimer As DispatcherTimer
        Private _recoveryPollCount As Integer
        Private _consecutiveDesktopSightings As Integer
        Private _isShuttingDown As Boolean
        ''' <summary>True while every Stage is hidden from the tray menu ("Hide Stages").</summary>
        Private _areStagesHidden As Boolean

        Private Sub New()
        End Sub

        ''' <summary>True once Stages (or Windows, at logoff) has started closing the app.</summary>
        Public ReadOnly Property IsShuttingDown As Boolean
            Get
                Return _isShuttingDown
            End Get
        End Property

        ''' <summary>
        ''' True while the user has hidden all Stages from the tray menu. Not saved:
        ''' Stages always start visible. The watchdog in each StageWindow checks this so it
        ''' doesn't "helpfully" re-show a Stage the user hid.
        ''' </summary>
        Public ReadOnly Property AreStagesHidden As Boolean
            Get
                Return _areStagesHidden
            End Get
        End Property

#Region "Loading and creating Stages"

        ''' <summary>
        ''' Shows one Stage per folder in Data, then starts watching Data for changes made
        ''' in Explorer. Returns how many Stages were shown.
        ''' </summary>
        Public Function LoadStages() As Integer
            Directory.CreateDirectory(AppPaths.DataFolder)

            Dim shownCount As Integer = 0
            For Each stageFolderPath As String In Directory.EnumerateDirectories(AppPaths.DataFolder)
                If LoadStageFromFolder(stageFolderPath) Then
                    shownCount += 1
                End If
            Next

            Logger.Info(String.Format("Loaded {0} Stage(s) from {1}", shownCount, AppPaths.DataFolder))
            StartWatchingDataFolder()
            Return shownCount
        End Function

        ''' <summary>
        ''' Shows the Stage stored in one folder. A folder without (readable) settings becomes
        ''' a fresh Stage named after the folder. If the folder was renamed while Stages wasn't
        ''' running, the title is updated to the new folder name.
        ''' </summary>
        Private Function LoadStageFromFolder(stageFolderPath As String) As Boolean
            Dim folderName As String = Path.GetFileName(stageFolderPath)
            Dim settingsNeedSaving As Boolean = False

            Dim stageSettings As StageInfo = TryReadSettings(stageFolderPath)
            If stageSettings Is Nothing Then
                ' A brand new Stage as far as Stages is concerned: use the "new Stage" defaults.
                stageSettings = New StageInfo() With {
                    .Name = folderName,
                    .CanMinify = AppSettingsStore.Current.NewStagesMinify
                }
                PlaceAtFreeSpot(stageSettings)
                settingsNeedSaving = True
            End If
            stageSettings.FolderPath = stageFolderPath

            Dim titleMatchesFolder As Boolean =
                Not String.IsNullOrWhiteSpace(stageSettings.Name) AndAlso
                StageFolder.FolderNameMatchesTitle(folderName, stageSettings.Name)
            If Not titleMatchesFolder Then
                Logger.Info("Folder '" & folderName & "' was renamed outside Stages - using it as the Stage title")
                stageSettings.Name = folderName
                settingsNeedSaving = True
            End If

            ' A copied folder carries a duplicate id - every open Stage needs its own.
            If stageSettings.Id = Guid.Empty OrElse _openWindows.ContainsKey(stageSettings.Id) Then
                stageSettings.Id = Guid.NewGuid()
            End If

            If settingsNeedSaving Then
                SaveStage(stageSettings)
            End If
            Return ShowStage(stageSettings)
        End Function

        ''' <summary>
        ''' Creates a new empty Stage (folder + settings), placed so it doesn't sit exactly on top
        ''' of another. <paramref name="kind"/>: Standard, Running tasks or Clipboard history (a
        ''' Live folder needs its folder - see CreateFolderStage).
        ''' </summary>
        Public Sub CreateStage(stageTitle As String, Optional kind As StageKind = StageKind.Standard)
            ' Background and title height are left empty, so the new Stage follows the
            ' app-wide defaults (Settings > General) until it is given its own.
            Dim newStage As New StageInfo(Guid.NewGuid()) With {
                .Name = stageTitle,
                .FolderPath = StageFolder.MakeNewStageFolderPath(stageTitle),
                .CanMinify = AppSettingsStore.Current.NewStagesMinify,
                .Kind = kind
            }
            newStage.ApplyKindDefaults()
            Directory.CreateDirectory(newStage.FolderPath)
            If kind = StageKind.ClipboardHistory Then ClipboardHistoryStore.EnsureMarker(newStage.FolderPath)
            PlaceAtFreeSpot(newStage)

            SaveStage(newStage)
            ShowStage(newStage)
            Logger.Info("Created " & kind.ToString() & " Stage '" & stageTitle & "' in " & newStage.FolderPath)
        End Sub

        ''' <summary>
        ''' Creates a Standard, Running tasks or Clipboard history Stage with its kind's usual
        ''' title (Stage menu and tray menu > New Stage). Live folders go through CreateFolderStage.
        ''' </summary>
        Public Sub CreateStageOfKind(kind As StageKind)
            Select Case kind
                Case StageKind.RunningTasks
                    CreateStage(StageDefaults.NewRunningTasksStageName, kind)
                Case StageKind.ClipboardHistory
                    CreateStage(StageDefaults.NewClipboardStageName, kind)
                Case Else
                    CreateStage(StageDefaults.NewStageName, StageKind.Standard)
            End Select
        End Sub

        ''' <summary>Starts at the default position and steps diagonally while another Stage already occupies the spot.</summary>
        Private Sub PlaceAtFreeSpot(stage As StageInfo)
            stage.PosX = StageDefaults.NewStageLeft
            stage.PosY = StageDefaults.NewStageTop

            For cascadeStep As Integer = 1 To StageDefaults.MaximumCascadeSteps
                Dim spotIsTaken As Boolean = _openWindows.Values.Any(
                    Function(window) window.Settings.PosX = stage.PosX AndAlso window.Settings.PosY = stage.PosY)
                If Not spotIsTaken Then
                    Return
                End If
                stage.PosX += StageDefaults.NewStageCascadeOffset
                stage.PosY += StageDefaults.NewStageCascadeOffset
            Next
        End Sub

        ''' <summary>
        ''' Creates and shows the window for a Stage. Returns False (and logs) on failure.
        ''' While Stages are hidden (tray menu > Hide Stages) the window is created but not
        ''' shown; SetStagesHidden(False) shows it along with the others.
        ''' </summary>
        Private Function ShowStage(stageSettings As StageInfo) As Boolean
            Try
                Dim window As New StageWindow(stageSettings)
                _openWindows(stageSettings.Id) = window
                If Not _areStagesHidden Then
                    window.Show()
                End If
                Return True
            Catch ex As Exception
                Logger.Error("Could not show Stage '" & stageSettings.Name & "'", ex)
                _openWindows.Remove(stageSettings.Id)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Reads a Stage's settings file, falling back to the .bak copy if the main file is
        ''' missing or damaged. Returns Nothing if neither can be read.
        ''' </summary>
        Private Function TryReadSettings(stageFolderPath As String) As StageInfo
            Dim settingsPath As String = Path.Combine(stageFolderPath, StageFolder.SettingsFileName)
            Dim candidatePaths As String() = {settingsPath, settingsPath & AtomicFile.BackupFileSuffix}

            For Each candidatePath As String In candidatePaths
                If Not File.Exists(candidatePath) Then
                    Continue For
                End If

                Try
                    Using reader As FileStream = File.OpenRead(candidatePath)
                        Dim stageSettings As StageInfo = TryCast(_serializer.Deserialize(reader), StageInfo)
                        If stageSettings IsNot Nothing Then
                            stageSettings.Sanitize()
                            If candidatePath <> settingsPath Then
                                Logger.Warn("Main settings file was unreadable; recovered Stage from backup: " & candidatePath)
                            End If
                            Return stageSettings
                        End If
                    End Using
                Catch ex As Exception
                    Logger.Warn("Could not read Stage settings file " & candidatePath, ex)
                End Try
            Next

            Return Nothing
        End Function

        ''' <summary>
        ''' Creates a FOLDER VIEW Stage: a Stage showing the contents of
        ''' <paramref name="sourceFolder"/> (any folder on the PC), named after that folder.
        ''' Its own Data folder only holds its settings.
        ''' </summary>
        Public Sub CreateFolderStage(sourceFolder As String)
            Dim folderTitle As String = FolderDisplayName(sourceFolder)
            Dim newStage As New StageInfo(Guid.NewGuid()) With {
                .Name = folderTitle,
                .FolderPath = StageFolder.MakeNewStageFolderPath(folderTitle),
                .SourceFolderPath = sourceFolder,
                .Kind = StageKind.LiveFolder,
                .CanMinify = AppSettingsStore.Current.NewStagesMinify
            }
            Directory.CreateDirectory(newStage.FolderPath)
            PlaceAtFreeSpot(newStage)

            SaveStage(newStage)
            ShowStage(newStage)
            Logger.Info("Created folder Stage '" & folderTitle & "' showing " & sourceFolder)
        End Sub

        ''' <summary>A folder's name for a title; a whole drive ("D:\") becomes "Drive D".</summary>
        Public Shared Function FolderDisplayName(folderPath As String) As String
            Dim trimmedPath As String = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            Dim folderName As String = Path.GetFileName(trimmedPath)
            If String.IsNullOrEmpty(folderName) Then
                ' A drive root: Path.GetFileName("D:") is empty.
                Return StageFolder.DriveLabelPrefix & trimmedPath.TrimEnd(":"c)
            End If
            Return folderName
        End Function

#End Region

#Region "Tabs"

        ''' <summary>
        ''' Reads one tab's settings from its folder (a sub-folder of a Stage's folder). A tab
        ''' whose folder was renamed in Explorer takes the folder's name as its title.
        ''' Returns Nothing if the folder isn't a tab (it has no settings file).
        ''' </summary>
        Public Function LoadTab(tabFolderPath As String) As StageInfo
            Dim tabSettings As StageInfo = TryReadSettings(tabFolderPath)
            If tabSettings Is Nothing Then Return Nothing
            tabSettings.FolderPath = tabFolderPath

            Dim folderName As String = Path.GetFileName(tabFolderPath)
            Dim titleMatchesFolder As Boolean = Not String.IsNullOrWhiteSpace(tabSettings.Name) AndAlso
                                                StageFolder.FolderNameMatchesTitle(folderName, tabSettings.Name)
            If Not titleMatchesFolder Then
                tabSettings.Name = folderName
                SaveStage(tabSettings)
            End If
            Return tabSettings
        End Function

        ''' <summary>
        ''' Makes a new tab in a Stage: a sub-folder of its folder with its own settings file.
        ''' With <paramref name="sourceFolder"/>, the tab is a folder view of that folder.
        ''' Returns the new tab's settings, or Nothing (logged) if the folder couldn't be made.
        ''' </summary>
        Public Function CreateTab(hostStage As StageInfo, tabTitle As String, Optional sourceFolder As String = Nothing,
                                  Optional kind As StageKind = StageKind.Standard) As StageInfo
            Try
                ' A tab showing a folder is always a Live folder.
                Dim tabKind As StageKind = If(String.IsNullOrWhiteSpace(sourceFolder), kind, StageKind.LiveFolder)
                Dim newTab As New StageInfo(Guid.NewGuid()) With {
                    .Name = tabTitle,
                    .FolderPath = StageFolder.MakeNewTabFolderPath(hostStage.FolderPath, tabTitle),
                    .SourceFolderPath = sourceFolder,
                    .Kind = tabKind
                }
                newTab.ApplyKindDefaults()
                Directory.CreateDirectory(newTab.FolderPath)
                If tabKind = StageKind.ClipboardHistory Then ClipboardHistoryStore.EnsureMarker(newTab.FolderPath)
                If Not SaveStage(newTab) Then Return Nothing
                Logger.Info("Created tab '" & tabTitle & "' in Stage '" & hostStage.Name & "'")
                Return newTab
            Catch ex As Exception
                Logger.Error("Could not create a tab in Stage '" & hostStage.Name & "'", ex)
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Turns a tab into a Stage of its own: its folder moves from inside the Stage's folder
        ''' into Data, and a window opens for it a little below and right of the old Stage.
        ''' Returns False (tab left where it was) if the folder couldn't be moved.
        ''' </summary>
        Public Function MoveTabToOwnStage(hostStage As StageInfo, movingTab As StageInfo) As Boolean
            Dim oldFolder As String = movingTab.FolderPath
            Dim newFolder As String = StageFolder.MakeNewStageFolderPath(movingTab.Name)
            Try
                Directory.Move(oldFolder, newFolder)
            Catch ex As Exception
                Logger.Error("Could not move tab folder " & oldFolder & " out to " & newFolder, ex)
                Return False
            End Try

            movingTab.FolderPath = newFolder
            movingTab.Id = Guid.NewGuid()
            movingTab.PosX = hostStage.PosX + StageDefaults.NewStageCascadeOffset
            movingTab.PosY = hostStage.PosY + StageDefaults.NewStageCascadeOffset
            movingTab.Width = hostStage.Width
            movingTab.Height = hostStage.Height
            movingTab.CanMinify = hostStage.CanMinify
            SaveStage(movingTab)
            ShowStage(movingTab)
            Logger.Info("Tab '" & movingTab.Name & "' is now a Stage of its own")
            Return True
        End Function

        ''' <summary>
        ''' Makes a whole Stage a tab of another: its folder moves inside the other Stage's
        ''' folder and its window closes. The target window then shows it as a tab.
        ''' Only Stages without tabs of their own can be moved (the menu only offers those).
        ''' Returns the tab's new folder, or Nothing (Stage left as it was) on failure.
        ''' </summary>
        Public Function MoveStageIntoTabs(movingWindow As StageWindow, targetStage As StageInfo) As String
            Dim movingStage As StageInfo = movingWindow.Settings
            Dim oldFolder As String = movingStage.FolderPath
            Dim newFolder As String = StageFolder.MakeNewTabFolderPath(targetStage.FolderPath, Path.GetFileName(oldFolder))

            ' The moving window must stop watching and saving into the folder first.
            movingWindow.IsBeingRemoved = True
            movingWindow.StopWatchingFolder()
            Try
                Directory.Move(oldFolder, newFolder)
            Catch ex As Exception
                Logger.Error("Could not move Stage folder " & oldFolder & " into " & targetStage.FolderPath, ex)
                movingWindow.IsBeingRemoved = False
                movingWindow.StartWatchingFolder()
                Return Nothing
            End Try

            Logger.Info("Stage '" & movingStage.Name & "' is now a tab of Stage '" & targetStage.Name & "'")
            movingWindow.Close()
            Return newFolder
        End Function

        ''' <summary>Every other open Stage (for "Add this Stage as a tab to").</summary>
        Public Function OtherStageWindows(exceptWindow As StageWindow) As List(Of StageWindow)
            Dim otherWindows As IEnumerable(Of StageWindow) = _openWindows.Values.Where(Function(window) window IsNot exceptWindow)
            Dim byName As IOrderedEnumerable(Of StageWindow) =
                otherWindows.OrderBy(Function(window) window.Settings.Name, StringComparer.CurrentCultureIgnoreCase)
            Return byName.ToList()
        End Function

#End Region

#Region "Saving, renaming and removing"

        ''' <summary>
        ''' Saves a Stage's settings safely:
        '''   1. serialise to memory (a serialisation error never touches the disk),
        '''   2. write a .tmp file and flush it to disk,          } AtomicFile
        '''   3. swap it in atomically, keeping the previous version as .bak, }
        '''   4. mark the settings files hidden so they don't clutter the folder.
        ''' Never throws; returns False and logs on failure.
        ''' </summary>
        Public Function SaveStage(stageSettings As StageInfo) As Boolean
            SyncLock _saveLock
                Try
                    If Not Directory.Exists(stageSettings.FolderPath) Then
                        ' Never re-create a missing Stage folder. It has almost certainly just been
                        ' renamed or deleted in Explorer (the Data watcher will catch up a moment
                        ' later); re-creating it is what used to leave a stray duplicate folder behind.
                        Logger.Warn("Not saving Stage '" & stageSettings.Name & "': its folder no longer exists at " & stageSettings.FolderPath)
                        Return False
                    End If

                    Dim settingsPath As String = Path.Combine(stageSettings.FolderPath, StageFolder.SettingsFileName)
                    Dim backupPath As String = settingsPath & AtomicFile.BackupFileSuffix

                    ' Step 1: serialise in memory
                    Dim xmlBytes As Byte()
                    Using buffer As New MemoryStream()
                        _serializer.Serialize(buffer, stageSettings)
                        xmlBytes = buffer.ToArray()
                    End Using

                    ' Steps 2 and 3: write a temp file and swap it in atomically (see AtomicFile).
                    ' Hidden files can refuse to be overwritten, so unhide them first.
                    SetHidden(settingsPath, False)
                    SetHidden(backupPath, False)
                    AtomicFile.WriteAllBytes(settingsPath, xmlBytes)

                    ' Step 4: hide them again
                    SetHidden(settingsPath, True)
                    SetHidden(backupPath, True)
                    Return True
                Catch ex As Exception
                    Logger.Error("Could not save Stage '" & stageSettings.Name & "' - will retry on the next change", ex)
                    Return False
                End Try
            End SyncLock
        End Function

        ''' <summary>Adds or removes the Hidden attribute on a file, if it exists.</summary>
        Private Shared Sub SetHidden(filePath As String, shouldBeHidden As Boolean)
            If Not File.Exists(filePath) Then
                Return
            End If

            Dim attributes As FileAttributes = File.GetAttributes(filePath)
            Dim updatedAttributes As FileAttributes
            If shouldBeHidden Then
                updatedAttributes = attributes Or FileAttributes.Hidden
            Else
                updatedAttributes = attributes And Not FileAttributes.Hidden
            End If

            If updatedAttributes <> attributes Then
                File.SetAttributes(filePath, updatedAttributes)
            End If
        End Sub

        ''' <summary>
        ''' Changes a Stage's title and renames its folder to match, then saves.
        ''' The title always changes. Returns False if the FOLDER couldn't be renamed
        ''' (usually because something has it open) - it then keeps its old name.
        ''' </summary>
        Public Function RenameStage(stageSettings As StageInfo, newTitle As String) As Boolean
            stageSettings.Name = newTitle
            Dim folderWasRenamed As Boolean = StageFolder.TryRenameFolder(stageSettings, newTitle)
            SaveStage(stageSettings)
            Return folderWasRenamed
        End Function

        ''' <summary>
        ''' Moves a Stage's folder (its shortcuts and settings) to the Recycle Bin and closes
        ''' its window. The caller must already have checked the folder contains only
        ''' shortcuts. Returns False (Stage left as it was) if the folder couldn't be removed.
        ''' </summary>
        Public Function RemoveStage(window As StageWindow) As Boolean
            Dim stageSettings As StageInfo = window.Settings
            ' Tell the window this close is deliberate (so it isn't "recovered") and stop
            ' it watching / saving into the folder that is about to go.
            window.IsBeingRemoved = True
            window.StopWatchingFolder()

            Try
                StageFolder.SendToRecycleBin(stageSettings.FolderPath)
            Catch ex As Exception
                Logger.Error("Could not move the folder of Stage '" & stageSettings.Name & "' to the Recycle Bin", ex)
                window.IsBeingRemoved = False
                window.StartWatchingFolder()
                Return False
            End Try

            Logger.Info("Removed Stage '" & stageSettings.Name & "' (folder sent to the Recycle Bin)")
            window.Close()
            Return True
        End Function

#End Region

#Region "Keeping in step with Explorer (Data folder watcher)"

        ''' <summary>Starts reporting folder changes in Data. Failure is logged; Stages still work, just without live sync.</summary>
        Private Sub StartWatchingDataFolder()
            Try
                Dim watcher As New FileSystemWatcher(AppPaths.DataFolder) With {
                    .IncludeSubdirectories = False,
                    .NotifyFilter = NotifyFilters.DirectoryName
                }
                AddHandler watcher.Created, AddressOf DataFolderWatcher_Created
                AddHandler watcher.Deleted, AddressOf DataFolderWatcher_Deleted
                AddHandler watcher.Renamed, AddressOf DataFolderWatcher_Renamed
                AddHandler watcher.Error, AddressOf DataFolderWatcher_Error
                watcher.EnableRaisingEvents = True
                _dataFolderWatcher = watcher
            Catch ex As Exception
                Logger.Warn("Could not watch the Data folder for changes made in Explorer", ex)
            End Try
        End Sub

        Private Sub StopWatchingDataFolder()
            If _dataFolderWatcher Is Nothing Then Return
            _dataFolderWatcher.EnableRaisingEvents = False
            _dataFolderWatcher.Dispose()
            _dataFolderWatcher = Nothing
        End Sub

        ' The watcher's events arrive on a background thread; each one copies what it needs
        ' and hands the real work to the UI thread, where all Stage windows live.

        Private Sub DataFolderWatcher_Created(sender As Object, e As FileSystemEventArgs)
            Dim createdFolder As String = e.FullPath
            RunOnUiThread(Sub() OnDataSubfolderCreated(createdFolder))
        End Sub

        Private Sub DataFolderWatcher_Deleted(sender As Object, e As FileSystemEventArgs)
            Dim deletedFolder As String = e.FullPath
            RunOnUiThread(Sub() OnDataSubfolderDeleted(deletedFolder))
        End Sub

        Private Sub DataFolderWatcher_Renamed(sender As Object, e As RenamedEventArgs)
            Dim oldFolder As String = e.OldFullPath
            Dim newFolder As String = e.FullPath
            RunOnUiThread(Sub() OnDataSubfolderRenamed(oldFolder, newFolder))
        End Sub

        Private Sub DataFolderWatcher_Error(sender As Object, e As ErrorEventArgs)
            Logger.Warn("Data folder watcher reported an error", e.GetException())
        End Sub

        Private Shared Sub RunOnUiThread(work As Action)
            Dim currentApplication As Application = Application.Current
            If currentApplication IsNot Nothing Then
                currentApplication.Dispatcher.BeginInvoke(work)
            End If
        End Sub

        ''' <summary>
        ''' A Stage folder was renamed. If it was renamed in Explorer, the Stage's title follows.
        ''' (When Stages renames a folder itself, the Stage already has the new path, so no
        ''' Stage matches the old path and nothing happens here.)
        ''' </summary>
        Private Sub OnDataSubfolderRenamed(oldFolderPath As String, newFolderPath As String)
            Try
                If _isShuttingDown Then Return
                Dim window As StageWindow = FindWindowByFolder(oldFolderPath)
                If window Is Nothing Then Return

                Logger.Info("Stage folder renamed in Explorer: '" & Path.GetFileName(oldFolderPath) &
                            "' -> '" & Path.GetFileName(newFolderPath) & "'")
                window.OnFolderRenamedExternally(newFolderPath)
            Catch ex As Exception
                Logger.Error("Could not follow the rename of " & oldFolderPath, ex)
            End Try
        End Sub

        ''' <summary>A Stage folder was deleted or moved out of Data - close its Stage.</summary>
        Private Sub OnDataSubfolderDeleted(folderPath As String)
            Try
                If _isShuttingDown Then Return
                Dim window As StageWindow = FindWindowByFolder(folderPath)
                If window Is Nothing OrElse Directory.Exists(folderPath) Then Return

                Logger.Info("Stage folder removed outside Stages - closing Stage '" & window.Settings.Name & "'")
                window.OnFolderDeletedExternally()
            Catch ex As Exception
                Logger.Error("Could not handle the removal of " & folderPath, ex)
            End Try
        End Sub

        ''' <summary>A new folder appeared in Data - show it as a new Stage (unless Stages created it).</summary>
        Private Sub OnDataSubfolderCreated(folderPath As String)
            Try
                If _isShuttingDown OrElse Not Directory.Exists(folderPath) Then Return
                If FindWindowByFolder(folderPath) IsNot Nothing Then Return ' made by CreateStage
                If _stagesAwaitingRecovery.Any(Function(waiting) StageFolder.IsSamePath(waiting.FolderPath, folderPath)) Then Return

                Logger.Info("New folder in Data - showing it as a Stage: " & folderPath)
                LoadStageFromFolder(folderPath)
            Catch ex As Exception
                Logger.Error("Could not show a Stage for new folder " & folderPath, ex)
            End Try
        End Sub

        ''' <summary>The open Stage whose folder is at this path, or Nothing.</summary>
        Private Function FindWindowByFolder(folderPath As String) As StageWindow
            Return _openWindows.Values.FirstOrDefault(
                Function(window) StageFolder.IsSamePath(window.Settings.FolderPath, folderPath))
        End Function

#End Region

#Region "Hiding all Stages and applying app-wide settings"

        ''' <summary>
        ''' Hides or shows every Stage (tray menu > Hide Stages / Show Stages). Hidden Stages
        ''' keep running - their folders are still watched - they just aren't on screen.
        ''' </summary>
        Public Sub SetStagesHidden(shouldHide As Boolean)
            If shouldHide = _areStagesHidden Then Return
            _areStagesHidden = shouldHide

            ' ToList(): a copy, in case showing a window somehow changes the collection.
            For Each window As StageWindow In _openWindows.Values.ToList()
                Try
                    If shouldHide Then
                        window.Hide()
                    Else
                        ' Also shows windows created while hidden (never shown until now).
                        window.Show()
                    End If
                Catch ex As Exception
                    Logger.Warn("Could not " & If(shouldHide, "hide", "show") & " Stage '" & window.Settings.Name & "'", ex)
                End Try
            Next

            Logger.Info(If(shouldHide, "All Stages hidden", "All Stages shown"))
        End Sub

        ''' <summary>
        ''' Re-applies the app-wide settings (colours, default background, default title height)
        ''' to every Stage. Called by the Settings window after each change, so changes show live.
        ''' </summary>
        Public Sub ApplyAppSettingsToAllStages()
            For Each window As StageWindow In _openWindows.Values.ToList()
                Try
                    window.ApplyAppSettings()
                Catch ex As Exception
                    Logger.Warn("Could not apply settings to Stage '" & window.Settings.Name & "'", ex)
                End Try
            Next
        End Sub

        ''' <summary>Reloads every Stage's icons (after Windows' icons changed - see IconChangeWatcher).</summary>
        Public Sub ReloadAllIcons()
            For Each window As StageWindow In _openWindows.Values.ToList()
                window.ReloadIcons()
            Next
        End Sub

        ''' <summary>Clears every Stage's own background choice, so they all follow the app default.</summary>
        Public Sub UseDefaultBackdropOnAllStages()
            For Each window As StageWindow In _openWindows.Values.ToList()
                window.UseDefaultBackdrop()
            Next
            Logger.Info("Every Stage now follows the default background")
        End Sub

        ''' <summary>Clears every Stage's own title height, so they all follow the app default.</summary>
        Public Sub UseDefaultTitleHeightOnAllStages()
            For Each window As StageWindow In _openWindows.Values.ToList()
                window.UseDefaultTitleHeight()
            Next
            Logger.Info("Every Stage now follows the default title height")
        End Sub

        ' Counting with a condition: Where(...) first, then Count(). Calling .Count(condition)
        ' directly on _openWindows.Values doesn't compile in VB, because the collection's own
        ' Count property hides LINQ's Count method.

        ''' <summary>How many Stages currently have their own background (shown in Settings).</summary>
        Public Function CountStagesWithOwnBackdrop() As Integer
            Return _openWindows.Values.Where(Function(window) window.Settings.Backdrop.HasValue).Count()
        End Function

        ''' <summary>How many Stages currently have their own title height (shown in Settings).</summary>
        Public Function CountStagesWithOwnTitleHeight() As Integer
            Return _openWindows.Values.Where(Function(window) window.Settings.TitleHeight.HasValue).Count()
        End Function

#End Region

#Region "Window lifetime and recovery"

        ''' <summary>
        ''' Called by every StageWindow when it closes. Decides whether this was a
        ''' deliberate removal or Windows destroying the window behind our back.
        ''' </summary>
        Public Sub OnStageWindowClosed(window As StageWindow)
            Dim stageSettings As StageInfo = window.Settings

            ' Only forget the window if it is still the one registered for this Stage.
            Dim registeredWindow As StageWindow = Nothing
            If _openWindows.TryGetValue(stageSettings.Id, registeredWindow) AndAlso registeredWindow Is window Then
                _openWindows.Remove(stageSettings.Id)
            End If

            If _isShuttingDown Then
                Return
            End If

            If window.IsBeingRemoved Then
                ' Removing the last Stage quits the app: "New Stage" is only offered from a
                ' Stage's own menu, so with none left there'd be nothing to do.
                If _openWindows.Count = 0 AndAlso _stagesAwaitingRecovery.Count = 0 Then
                    Logger.Info("Last Stage removed - exiting")
                    ShutDown()
                End If
                Return
            End If

            ' Not removed by the user => Windows destroyed it (almost always an Explorer restart).
            Logger.Warn("Stage '" & stageSettings.Name & "' was closed unexpectedly (Explorer restart?). It will be restored.")
            _stagesAwaitingRecovery.Add(stageSettings)
            StartRecoveryTimer()
        End Sub

        ''' <summary>Starts (or keeps running) the timer that waits for Explorer to return.</summary>
        Private Sub StartRecoveryTimer()
            If _recoveryTimer Is Nothing Then
                _recoveryTimer = New DispatcherTimer(DispatcherPriority.Background) With {.Interval = RecoveryPollInterval}
                AddHandler _recoveryTimer.Tick, AddressOf RecoveryTimer_Tick
            End If

            If Not _recoveryTimer.IsEnabled Then
                _recoveryPollCount = 0
                _consecutiveDesktopSightings = 0
                _recoveryTimer.Start()
            End If
        End Sub

        ''' <summary>Each second: once Explorer's desktop is reliably back, re-create the lost Stages.</summary>
        Private Sub RecoveryTimer_Tick(sender As Object, e As EventArgs)
            Try
                _recoveryPollCount += 1

                If DesktopIntegration.FindDesktopWindow() = IntPtr.Zero Then
                    _consecutiveDesktopSightings = 0
                Else
                    _consecutiveDesktopSightings += 1
                End If

                Dim desktopIsBack As Boolean = _consecutiveDesktopSightings >= RequiredConsecutiveDesktopSightings
                Dim waitedTooLong As Boolean = _recoveryPollCount >= MaximumRecoveryPolls
                If Not desktopIsBack AndAlso Not waitedTooLong Then
                    Return ' keep waiting
                End If

                _recoveryTimer.Stop()
                If waitedTooLong AndAlso Not desktopIsBack Then
                    Logger.Warn("Explorer's desktop did not come back in time; restoring Stages anyway")
                End If

                ' Copy then clear the list first, in case a Stage closes again while being shown.
                Dim stagesToRestore As List(Of StageInfo) = _stagesAwaitingRecovery.ToList()
                _stagesAwaitingRecovery.Clear()
                For Each stageSettings As StageInfo In stagesToRestore
                    If ShowStage(stageSettings) Then
                        Logger.Info("Restored Stage '" & stageSettings.Name & "'")
                    End If
                Next
            Catch ex As Exception
                Logger.Error("Stage recovery failed", ex)
            End Try
        End Sub

        ''' <summary>Windows is logging off/shutting down - don't try to "recover" closing Stages.</summary>
        Public Sub NotifyShuttingDown()
            _isShuttingDown = True
        End Sub

        ''' <summary>Writes every open Stage's waiting changes, so a backup holds the latest of everything.</summary>
        Public Sub SaveAllStagesNow()
            For Each window As StageWindow In _openWindows.Values.ToList()
                window.SavePendingChanges()
            Next
        End Sub

        ''' <summary>
        ''' Finishes an import (Settings > Backup): starts a new copy of Stages with
        ''' BackupArchive.FinishImportArgument, then quits. The new copy waits until this one
        ''' has fully exited, moves the imported files into place, then starts as normal.
        ''' Returns False (and keeps running) if the new copy couldn't be started.
        ''' </summary>
        Public Function RestartToFinishImport() As Boolean
            Try
                Dim programPath As String = Process.GetCurrentProcess().MainModule.FileName
                Dim restartInfo As New ProcessStartInfo(programPath, BackupArchive.FinishImportArgument) With {
                    .UseShellExecute = False,
                    .WorkingDirectory = Path.GetDirectoryName(programPath)
                }
                Process.Start(restartInfo)
            Catch ex As Exception
                Logger.Error("Could not restart Stages to finish the import", ex)
                Return False
            End Try
            Logger.Info("Restarting to finish importing a backup")
            ShutDown()
            Return True
        End Function

        ''' <summary>Quits Stages cleanly.</summary>
        Public Sub ShutDown()
            _isShuttingDown = True
            StopWatchingDataFolder()
            If _recoveryTimer IsNot Nothing Then
                _recoveryTimer.Stop()
            End If
            Application.Current.Shutdown()
        End Sub

#End Region

    End Class

End Namespace
