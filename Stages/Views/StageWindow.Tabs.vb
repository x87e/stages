' ============================================================================
'  StageWindow.Tabs.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  Tabs, and folder views.
'
'  TABS: a Stage can hold several sets of items, shown one at a time. Each
'  extra tab is a sub-folder of the Stage's folder with its own settings file:
'
'      Data\Work\                 <- the Stage, and its first tab ("Work")
'          .stage-settings.xml
'          Report.lnk
'          Projects\              <- a second tab ("Projects")
'              .stage-settings.xml
'              Plan.lnk
'
'  With more than one tab, the title bar shows the tabs instead of the title:
'  click one to open it, double-click it to rename it. The Stage menu's Tabs
'  sub-menu adds, renames, reorders and removes tabs, turns a tab into a Stage
'  of its own, or makes this whole Stage a tab of another one.
'  The window's position, size, lock, minify, background and picture belong to
'  the Stage (the first tab); each tab has its own items, view and sort.
'
'  FOLDER VIEWS: a Stage or tab can show the contents of any folder on the PC
'  instead of shortcuts (StageInfo.SourceFolderPath). Those are the user's own
'  files, so Stages never removes, renames or changes their icons, and nothing
'  can be dropped onto a folder view. Explorer's own right-click menu (minus
'  Delete and Rename, which ShellContextMenu hides on every Stage) still
'  works on them as usual.
' ============================================================================
Imports System.Collections.ObjectModel
Imports System.IO
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports Stages.ViewModels

Partial Public Class StageWindow

#Region "Tab constants"
    Private Const NewTabTitle As String = "New tab"
    Private Const NewTabCaption As String = "New tab"
    Private Const RenameTabCaption As String = "Rename tab"
    Private Const RemoveTabCaption As String = "Remove tab"
    Private Const MoveTabCaption As String = "Move tab to its own Stage"
    Private Const AddAsTabCaption As String = "Add this Stage as a tab"
    Private Const ChooseFolderForStageTitle As String = "Choose the folder this Stage will show"
    Private Const ChooseFolderForTabTitle As String = "Choose the folder this tab will show"

    Private Const TabsMenuText As String = "Tabs"
    Private Const RenameTabMenuText As String = "Rename tab..."
    Private Const MoveTabLeftMenuText As String = "Move tab left"
    Private Const MoveTabRightMenuText As String = "Move tab right"
    Private Const MoveTabToOwnStageMenuText As String = "Move tab to its own Stage"
    Private Const RemoveTabMenuText As String = "Remove tab"
    Private Const AddAsTabToMenuText As String = "Add this Stage as a tab to"
    Private Const NoOtherStagesMenuText As String = "(no other Stages)"
    Private Const NotWithTabsMenuText As String = "(only for a Stage without tabs)"
    ' Delete on a folder Stage. {0} = file name or item count, {1} = line break.
    Private Const DeleteFolderItemText As String = "Delete"
    Private Const DeleteFolderItemsMenuFormat As String = "Delete {0} items"
    Private Const DeleteFolderItemCaption As String = "Delete"
    Private Const RecycleOneFormat As String =
        "Move this to the Recycle Bin?{1}{1}{0}{1}{1}This is the real file or folder, not a shortcut. You can restore it from the Recycle Bin."
    Private Const RecycleManyFormat As String =
        "Move these {0} items to the Recycle Bin?{1}{1}These are the real files and folders, not shortcuts. You can restore them from the Recycle Bin."
    Private Const DeleteOnePermanentlyFormat As String =
        "Permanently delete this?{1}{1}{0}{1}{1}This is the real file or folder, not a shortcut. It will NOT go to the Recycle Bin and can't be brought back."
    Private Const DeleteManyPermanentlyFormat As String =
        "Permanently delete these {0} items?{1}{1}These are the real files and folders, not shortcuts. They will NOT go to the Recycle Bin and can't be brought back."
    Private Const DeleteFolderItemsFailedText As String =
        "Windows couldn't delete everything. Some items may be in use or protected. See the log for details."
    Private Const MenuAmpersand As String = "&"
    Private Const EscapedMenuAmpersand As String = "&&"

    Private Const RemoveTabConfirmationText As String =
        "Remove this tab?" & ControlChars.CrLf & ControlChars.CrLf &
        "Its shortcuts will be moved to the Recycle Bin. Your original files are not affected."
    Private Const RemoveFolderTabConfirmationText As String =
        "Remove this tab?" & ControlChars.CrLf & ControlChars.CrLf &
        "The folder it shows is not affected."
    ''' <summary>{0} = number of real items, {1} = folder path, {2} = line break.</summary>
    Private Const RealFilesInTabFormat As String =
        "This tab's folder contains {0} item(s) that aren't shortcuts:{2}{1}{2}{2}" &
        "Stages never deletes real files, so the tab hasn't been removed. " &
        "Move those items out of the folder, then remove the tab again."
    Private Const TabFolderProblemText As String =
        "The tab's folder couldn't be changed - it may be open in Explorer or another program. See the log for details."
    Private Const MoveIntoTabsFailedText As String =
        "This Stage's folder couldn't be moved - it may be open in Explorer or another program. See the log for details."
#End Region

#Region "Tab fields"
    ''' <summary>Every tab, in order. The first is always the Stage itself (_settings).</summary>
    Private ReadOnly _tabs As New List(Of StageInfo)()
    ''' <summary>What the title bar's tab strip shows (one header per tab).</summary>
    Private ReadOnly _tabHeaders As New ObservableCollection(Of StageTabHeader)()
    ''' <summary>The tab being shown.</summary>
    Private _openTab As StageInfo
#End Region

#Region "The open tab"

    ''' <summary>The tab being shown: its items, view and sort. The Stage itself if it has no other tabs.</summary>
    Private ReadOnly Property ActiveTab As StageInfo
        Get
            Return If(_openTab, _settings)
        End Get
    End Property

    ''' <summary>True while this Stage has more than one tab.</summary>
    Private ReadOnly Property HasTabs As Boolean
        Get
            Return _tabs.Count > 1
        End Get
    End Property

    ''' <summary>
    ''' Saves the open tab's settings now: the Stage's own file for the first tab, the tab's
    ''' file for any other.
    ''' </summary>
    Private Sub SaveActiveTab()
        If ActiveTab Is _settings Then
            SaveNow()
        ElseIf Not IsBeingRemoved Then
            StageManager.Instance.SaveStage(ActiveTab)
        End If
    End Sub

    ''' <summary>
    ''' Saves the open tab's settings a moment later (several quick changes are written once).
    ''' A tab other than the first is saved straight away - it has no timer of its own.
    ''' </summary>
    Private Sub ScheduleActiveTabSave()
        If ActiveTab Is _settings Then
            ScheduleSave()
        Else
            SaveActiveTab()
        End If
    End Sub

#End Region

#Region "Loading tabs and keeping them in step with the folder"

    ''' <summary>
    ''' Finds this Stage's tabs (sub-folders with a settings file) and opens the tab that was
    ''' open last time. Called once, from the constructor.
    ''' </summary>
    Private Sub LoadTabs()
        _tabs.Clear()
        _tabs.Add(_settings)
        _tabs.AddRange(ReadTabsFromDisk(Nothing))

        Dim lastOpenTab As StageInfo = _tabs.FirstOrDefault(
            Function(tab) tab IsNot _settings AndAlso
                          String.Equals(Path.GetFileName(tab.FolderPath), _settings.OpenTabFolderName, StringComparison.OrdinalIgnoreCase))
        _openTab = If(lastOpenTab, _settings)
        TabStrip.ItemsSource = _tabHeaders
        RebuildTabHeaders()
    End Sub

    ''' <summary>
    ''' Reads the tab folders inside the Stage's folder, in the saved tab order (new ones after,
    ''' by name). Tabs already in <paramref name="knownTabs"/> are reused rather than re-read.
    ''' </summary>
    Private Function ReadTabsFromDisk(knownTabs As IEnumerable(Of StageInfo)) As List(Of StageInfo)
        Dim foundTabs As New List(Of StageInfo)()
        If Not Directory.Exists(_settings.FolderPath) Then Return foundTabs

        Dim knownByFolder As New Dictionary(Of String, StageInfo)(StringComparer.OrdinalIgnoreCase)
        If knownTabs IsNot Nothing Then
            For Each knownTab As StageInfo In knownTabs
                knownByFolder(knownTab.FolderPath) = knownTab
            Next
        End If

        Try
            For Each subfolderPath As String In Directory.EnumerateDirectories(_settings.FolderPath)
                If Not StageFolder.IsTabFolder(subfolderPath) Then Continue For
                Dim tabSettings As StageInfo = Nothing
                If Not knownByFolder.TryGetValue(subfolderPath, tabSettings) Then
                    tabSettings = StageManager.Instance.LoadTab(subfolderPath)
                End If
                If tabSettings IsNot Nothing Then foundTabs.Add(tabSettings)
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Logger.Warn("Could not look for tabs in " & _settings.FolderPath, ex)
        End Try

        ' Saved order first; tabs not in it yet (e.g. made in Explorer) after, by name.
        Dim savedOrder As List(Of String) = _settings.TabOrder
        Dim inSavedOrder As IOrderedEnumerable(Of StageInfo) =
            foundTabs.OrderBy(Function(tab) TabOrderPosition(savedOrder, Path.GetFileName(tab.FolderPath)))
        Dim thenByName As IOrderedEnumerable(Of StageInfo) =
            inSavedOrder.ThenBy(Function(tab) tab.Name, StringComparer.CurrentCultureIgnoreCase)
        Return thenByName.ToList()
    End Function

    ''' <summary>Where a tab folder comes in the saved order (anything unlisted comes after every listed one).</summary>
    Private Shared Function TabOrderPosition(savedOrder As List(Of String), tabFolderName As String) As Integer
        Dim savedIndex As Integer = savedOrder.FindIndex(Function(savedName) String.Equals(savedName, tabFolderName, StringComparison.OrdinalIgnoreCase))
        Return If(savedIndex < 0, Integer.MaxValue, savedIndex)
    End Function

    ''' <summary>
    ''' Picks up tabs added, renamed or removed in Explorer (called on every item refresh).
    ''' If the open tab has gone, the first tab opens instead.
    ''' </summary>
    Private Sub SyncTabsWithDisk()
        Dim currentOtherTabs As List(Of StageInfo) = _tabs.Skip(1).ToList()
        Dim tabsOnDisk As List(Of StageInfo) = ReadTabsFromDisk(currentOtherTabs)
        If tabsOnDisk.SequenceEqual(currentOtherTabs) Then Return

        _tabs.RemoveRange(1, _tabs.Count - 1)
        _tabs.AddRange(tabsOnDisk)
        RememberTabOrder()
        If Not _tabs.Contains(ActiveTab) Then
            ShowTab(_settings)
        Else
            RebuildTabHeaders()
        End If
    End Sub

    ''' <summary>
    ''' After the Stage's own folder was renamed (by Stages or in Explorer), points every tab
    ''' at its folder's new location inside it.
    ''' </summary>
    Private Sub FollowTabsToRenamedFolder()
        For Each tab As StageInfo In _tabs.Skip(1)
            tab.FolderPath = Path.Combine(_settings.FolderPath, Path.GetFileName(tab.FolderPath))
        Next
    End Sub

    ''' <summary>Saves the tabs' order (and which is open) in the Stage's settings.</summary>
    Private Sub RememberTabOrder()
        _settings.TabOrder = _tabs.Skip(1).Select(Function(tab) Path.GetFileName(tab.FolderPath)).ToList()
        _settings.OpenTabFolderName = If(ActiveTab Is _settings, String.Empty, Path.GetFileName(ActiveTab.FolderPath))
        ScheduleSave()
    End Sub

    ''' <summary>
    ''' Redraws the tab strip. With one tab the plain title shows instead (the Stage's name);
    ''' with several, the strip replaces it.
    ''' </summary>
    Private Sub RebuildTabHeaders()
        _tabHeaders.Clear()
        For Each tab As StageInfo In _tabs
            _tabHeaders.Add(New StageTabHeader(tab, tab Is ActiveTab))
        Next
        TabStrip.Visibility = If(HasTabs, Visibility.Visible, Visibility.Collapsed)
        If Not _isEditingTitle Then
            TitleText.Visibility = If(HasTabs, Visibility.Hidden, Visibility.Visible)
        End If
        ' The tabs may have changed: listen for copies only while a Clipboard history tab exists.
        UpdateClipboardListening() ' StageWindow.Kinds.vb
    End Sub

#End Region

#Region "Switching tabs"

    ''' <summary>Opens a tab: its items replace the current ones, with its own view and sort.</summary>
    Private Sub ShowTab(tabToShow As StageInfo)
        If Not _tabs.Contains(tabToShow) Then tabToShow = _settings
        Dim isSameTab As Boolean = tabToShow Is ActiveTab

        _openTab = tabToShow
        _settings.OpenTabFolderName = If(tabToShow Is _settings, String.Empty, Path.GetFileName(tabToShow.FolderPath))
        RebuildTabHeaders()
        If isSameTab Then Return

        ScheduleSave() ' remembers which tab is open
        _items.Clear()
        ApplyViewMode()
        StartWatchingFolder() ' the new tab's folder
        UpdateKindTimers()    ' a Running tasks tab reads its windows every few seconds
        RefreshItems()
    End Sub

    ''' <summary>
    ''' A click on the tab strip: one click opens that tab (and a press-and-drag still moves
    ''' the Stage, through the title bar's own handler); a double-click renames it.
    ''' </summary>
    Private Sub TabStrip_PreviewMouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        Dim clickedHeader As StageTabHeader = FindTabHeaderFromElement(TryCast(e.OriginalSource, DependencyObject))
        If clickedHeader Is Nothing Then Return

        If e.ClickCount = DoubleClickCount Then
            e.Handled = True ' don't also start the title's own rename
            RenameTab(clickedHeader.Tab)
            Return
        End If
        ShowTab(clickedHeader.Tab)
    End Sub

    ''' <summary>The tab header under a clicked element, or Nothing.</summary>
    Private Shared Function FindTabHeaderFromElement(clickedElement As DependencyObject) As StageTabHeader
        Dim current As DependencyObject = clickedElement
        While current IsNot Nothing
            Dim element As FrameworkElement = TryCast(current, FrameworkElement)
            Dim header As StageTabHeader = If(element Is Nothing, Nothing, TryCast(element.DataContext, StageTabHeader))
            If header IsNot Nothing Then Return header
            current = If(TypeOf current Is Visual, VisualTreeHelper.GetParent(current), LogicalTreeHelper.GetParent(current))
        End While
        Return Nothing
    End Function

#End Region

#Region "Tabs menu"

    ''' <summary>Adds the Tabs sub-menu to the Stage menu.</summary>
    Private Sub AddTabsSubmenu(stageMenu As NativePopupMenu)
        Dim tabsMenu As NativePopupMenu = stageMenu.AddSubmenu(TabsMenuText)
        Dim openTabIndex As Integer = _tabs.IndexOf(ActiveTab)
        Dim isOtherThanFirstTab As Boolean = openTabIndex > 0

        AddNewTabSubmenu(tabsMenu) ' one entry per kind - StageWindow.Kinds.vb
        tabsMenu.AddSeparator()
        tabsMenu.AddItem(RenameTabMenuText, Sub() RenameTab(ActiveTab), isEnabled:=HasTabs)
        ' The first tab is the Stage itself, so it always stays first.
        tabsMenu.AddItem(MoveTabLeftMenuText, Sub() MoveOpenTab(-1), isEnabled:=openTabIndex > 1)
        tabsMenu.AddItem(MoveTabRightMenuText, Sub() MoveOpenTab(1), isEnabled:=isOtherThanFirstTab AndAlso openTabIndex < _tabs.Count - 1)
        tabsMenu.AddItem(MoveTabToOwnStageMenuText, AddressOf MoveOpenTabToOwnStage, isEnabled:=isOtherThanFirstTab)
        tabsMenu.AddItem(RemoveTabMenuText, AddressOf RemoveOpenTab, isEnabled:=isOtherThanFirstTab)
        tabsMenu.AddSeparator()

        ' Making this Stage a tab of another: only for a Stage without tabs of its own
        ' (tabs inside tabs aren't supported).
        Dim addToMenu As NativePopupMenu = tabsMenu.AddSubmenu(AddAsTabToMenuText)
        Dim otherStages As List(Of StageWindow) = StageManager.Instance.OtherStageWindows(Me)
        If HasTabs Then
            addToMenu.AddItem(NotWithTabsMenuText, Nothing, isEnabled:=False)
        ElseIf otherStages.Count = 0 Then
            addToMenu.AddItem(NoOtherStagesMenuText, Nothing, isEnabled:=False)
        End If
        If Not HasTabs Then
            For Each otherStage As StageWindow In otherStages
                Dim targetWindow As StageWindow = otherStage ' a copy for the lambda
                ' "&" marks a menu shortcut letter, so a real "&" in a name is written "&&".
                addToMenu.AddItem(targetWindow.Settings.Name.Replace(MenuAmpersand, EscapedMenuAmpersand),
                                  Sub() AddThisStageAsTabTo(targetWindow))
            Next
        End If
    End Sub

    ''' <summary>Asks for a name, then adds an empty tab and opens it.</summary>
    Private Sub CreateNewTab()
        Dim tabTitle As String = AskForName(NewTabTitle, NewTabCaption)
        If tabTitle Is Nothing Then Return
        AddAndOpenTab(StageManager.Instance.CreateTab(_settings, tabTitle))
    End Sub

    ''' <summary>Asks for a folder, then adds a tab showing it, named after it.</summary>
    Private Sub CreateNewFolderTab()
        Dim chosenFolder As String = AskForFolder(ChooseFolderForTabTitle)
        If chosenFolder Is Nothing Then Return
        AddAndOpenTab(StageManager.Instance.CreateTab(_settings, StageManager.FolderDisplayName(chosenFolder), chosenFolder))
    End Sub

    ''' <summary>Adds a freshly made tab at the end and opens it (Nothing = it couldn't be made).</summary>
    Private Sub AddAndOpenTab(newTab As StageInfo)
        If newTab Is Nothing Then
            ShowMessage(TabFolderProblemText, NewTabCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End If
        _tabs.Add(newTab)
        ShowTab(newTab)
        RememberTabOrder()
    End Sub

    ''' <summary>
    ''' Called by StageManager after another Stage's folder was moved into this one: shows it
    ''' as a new tab, and opens it.
    ''' </summary>
    Public Sub AdoptTab(tabFolderPath As String)
        Dim adoptedTab As StageInfo = StageManager.Instance.LoadTab(tabFolderPath)
        If adoptedTab Is Nothing Then Return
        _tabs.Add(adoptedTab)
        ShowTab(adoptedTab)
        RememberTabOrder()
    End Sub

    ''' <summary>Renames a tab (and its folder). The first tab's name is the Stage's own name.</summary>
    Private Sub RenameTab(tab As StageInfo)
        If tab Is _settings Then
            RenameStage()
            Return
        End If

        Dim newTitle As String = AskForName(tab.Name, RenameTabCaption)
        If newTitle Is Nothing OrElse String.Equals(newTitle, tab.Name, StringComparison.Ordinal) Then Return

        Dim isOpen As Boolean = tab Is ActiveTab
        If isOpen Then StopWatchingFolder() ' a watched folder can't be renamed
        Dim folderWasRenamed As Boolean = StageManager.Instance.RenameStage(tab, newTitle)
        If isOpen Then
            StartWatchingFolder()
            RefreshItems() ' the open tab's items now live under the folder's new name
        End If

        RememberTabOrder() ' the folder name may have changed
        RebuildTabHeaders()
        If Not folderWasRenamed Then
            ShowMessage(TabFolderProblemText, RenameTabCaption, MessageBoxButton.OK, MessageBoxImage.Information)
        End If
    End Sub

    ''' <summary>Moves the open tab one place left (-1) or right (+1). The first tab never moves.</summary>
    Private Sub MoveOpenTab(direction As Integer)
        Dim currentIndex As Integer = _tabs.IndexOf(ActiveTab)
        Dim newIndex As Integer = currentIndex + direction
        If currentIndex < 1 OrElse newIndex < 1 OrElse newIndex >= _tabs.Count Then Return

        Dim movingTab As StageInfo = _tabs(currentIndex)
        _tabs.RemoveAt(currentIndex)
        _tabs.Insert(newIndex, movingTab)
        RememberTabOrder()
        RebuildTabHeaders()
    End Sub

    ''' <summary>Turns the open tab into a Stage of its own, beside this one.</summary>
    Private Sub MoveOpenTabToOwnStage()
        Dim movingTab As StageInfo = ActiveTab
        If movingTab Is _settings Then Return

        StopWatchingFolder() ' the folder is about to move
        If Not StageManager.Instance.MoveTabToOwnStage(_settings, movingTab) Then
            StartWatchingFolder()
            ShowMessage(TabFolderProblemText, MoveTabCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End If

        _tabs.Remove(movingTab)
        ShowTab(_settings)
        RememberTabOrder()
    End Sub

    ''' <summary>
    ''' Removes the open tab, sending its folder to the Recycle Bin - refused (and explained)
    ''' if the folder holds anything other than shortcuts, so real files are never deleted.
    ''' </summary>
    Private Sub RemoveOpenTab()
        Dim removingTab As StageInfo = ActiveTab
        If removingTab Is _settings Then Return

        Dim realEntries As List(Of String) = StageFolder.FindNonShortcutEntries(removingTab.FolderPath)
        If realEntries.Count > 0 Then
            ShowMessage(String.Format(RealFilesInTabFormat, realEntries.Count, removingTab.FolderPath, Environment.NewLine),
                        RemoveTabCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End If

        Dim confirmationText As String = If(removingTab.IsFolderView, RemoveFolderTabConfirmationText, RemoveTabConfirmationText)
        confirmationText = If(RemoveConfirmationForKind(removingTab), confirmationText) ' StageWindow.Kinds.vb
        If ShowMessage(confirmationText, RemoveTabCaption, MessageBoxButton.YesNo, MessageBoxImage.Question) <> MessageBoxResult.Yes Then
            Return
        End If

        StopWatchingFolder()
        Try
            StageFolder.SendToRecycleBin(removingTab.FolderPath)
        Catch ex As OperationCanceledException
            StartWatchingFolder()
            Return ' the user cancelled a Windows error dialog
        Catch ex As Exception
            Logger.Error("Could not remove tab '" & removingTab.Name & "' of Stage '" & _settings.Name & "'", ex)
            StartWatchingFolder()
            ShowMessage(TabFolderProblemText, RemoveTabCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End Try

        _tabs.Remove(removingTab)
        ShowTab(_settings)
        RememberTabOrder()
    End Sub

    ''' <summary>Makes this whole Stage (it has no tabs of its own) the last tab of another Stage.</summary>
    Private Sub AddThisStageAsTabTo(targetWindow As StageWindow)
        If HasTabs Then Return
        SaveNow() ' its items' order and layout travel with it

        Dim newTabFolder As String = StageManager.Instance.MoveStageIntoTabs(Me, targetWindow.Settings)
        If newTabFolder Is Nothing Then
            ShowMessage(MoveIntoTabsFailedText, AddAsTabCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End If
        targetWindow.AdoptTab(newTabFolder)
    End Sub

#End Region

#Region "Folder views"

    ''' <summary>
    ''' True if Stages may send the open tab's items to the Recycle Bin: never in a folder view
    ''' (real files), nor on Running tasks (its items are windows, not files).
    ''' </summary>
    Private Function CanRemoveItems() As Boolean
        Return Not ActiveTab.IsFolderView AndAlso Not ActiveTab.IsRunningTasks
    End Function

    ''' <summary>
    ''' Delete on a folder Stage: deletes the REAL files/folders behind the given items, in
    ''' the way chosen in Settings > Items > Deleting from folder Stages:
    '''   Recycle Bin ....... Stages asks, then recycles (Windows warns if it can't recycle)
    '''   Same as Windows ... no question from Stages; Windows behaves as in File Explorer
    '''   Permanently ....... Stages asks (default answer No), then deletes for good
    ''' Only ever reachable from a folder view; ordinary Stages use Remove from Stage.
    ''' </summary>
    Private Sub DeleteFolderItems(itemsToDelete As List(Of StageItemViewModel))
        If Not ActiveTab.IsFolderView OrElse itemsToDelete.Count = 0 Then Return

        Dim deleteMode As FolderDeleteMode = AppSettingsStore.Current.FolderDelete
        If deleteMode <> FolderDeleteMode.SameAsWindows AndAlso Not ConfirmFolderDelete(itemsToDelete, deleteMode) Then
            Return
        End If

        Dim pathsToDelete As List(Of String) = itemsToDelete.Select(Function(itemViewModel) itemViewModel.FullPath).ToList()
        Dim outcome As ShellDeleteResult
        _isPopupOpen = True ' Windows may show its own questions and a progress window
        Try
            Using host As New DialogHost(Me) ' keeps Windows' questions and progress window in front
                outcome = ShellFileDelete.DeleteItems(host.Handle, pathsToDelete, deleteMode)
            End Using
        Finally
            _isPopupOpen = False
        End Try

        Logger.Info(String.Format("Delete on folder Stage '{0}': {1} item(s), mode {2}, result {3}",
                                  _settings.Name, pathsToDelete.Count, deleteMode, outcome))
        If outcome = ShellDeleteResult.Failed Then
            ShowMessage(DeleteFolderItemsFailedText, DeleteFolderItemCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
        End If
        RefreshItems() ' shows whatever is left (some items may be gone even after a cancel)
        CheckWhetherMouseHasLeft()
    End Sub

    ''' <summary>
    ''' Stages' own question before a delete. Names the file when there is one, counts them
    ''' when there are several. For a permanent delete the highlighted answer is No, so a
    ''' stray Enter can't destroy anything.
    ''' </summary>
    Private Function ConfirmFolderDelete(itemsToDelete As List(Of StageItemViewModel), deleteMode As FolderDeleteMode) As Boolean
        Dim isPermanent As Boolean = (deleteMode = FolderDeleteMode.Permanent)
        Dim question As String
        If itemsToDelete.Count = 1 Then
            question = String.Format(If(isPermanent, DeleteOnePermanentlyFormat, RecycleOneFormat),
                                     itemsToDelete(0).Item.FileName, Environment.NewLine)
        Else
            question = String.Format(If(isPermanent, DeleteManyPermanentlyFormat, RecycleManyFormat),
                                     itemsToDelete.Count, Environment.NewLine)
        End If

        _isPopupOpen = True
        Try
            ' A delete question must never be hidden behind another window (DialogHost.vb).
            Using host As New DialogHost(Me)
                Dim answer As MessageBoxResult = MessageBox.Show(
                    host.OwnerWindow, question, DeleteFolderItemCaption, MessageBoxButton.YesNo,
                    If(isPermanent, MessageBoxImage.Warning, MessageBoxImage.Question),
                    If(isPermanent, MessageBoxResult.No, MessageBoxResult.Yes))
                Return answer = MessageBoxResult.Yes
            End Using
        Finally
            _isPopupOpen = False
        End Try
    End Function

    ''' <summary>Stage menu > New folder Stage...: asks for a folder and opens a Stage showing it.</summary>
    Private Sub CreateNewFolderStage()
        Dim chosenFolder As String = AskForFolder(ChooseFolderForStageTitle)
        If chosenFolder Is Nothing Then Return
        StageManager.Instance.CreateFolderStage(chosenFolder)
    End Sub

#End Region

#Region "Small dialogs"

    ''' <summary>Shows the name dialog; returns the trimmed name, or Nothing if cancelled or left empty.</summary>
    Private Function AskForName(startingName As String, caption As String) As String
        Dim dialog As New RenameDialog(startingName, caption) With {.Owner = Me}
        Dim wasConfirmed As Boolean
        _isPopupOpen = True
        Try
            wasConfirmed = dialog.ShowDialog().GetValueOrDefault()
        Finally
            _isPopupOpen = False
        End Try
        If Not wasConfirmed Then Return Nothing
        Dim typedName As String = If(dialog.NewName, String.Empty).Trim()
        Return If(typedName.Length = 0, Nothing, typedName)
    End Function

    ''' <summary>Shows Windows' folder picker; returns the chosen folder, or Nothing if cancelled.</summary>
    Private Function AskForFolder(dialogTitle As String) As String
        _isPopupOpen = True
        Try
            Using host As New DialogHost(Me) ' keeps the folder picker in front
                Return FolderPicker.ChooseFolder(host.Handle, dialogTitle)
            End Using
        Finally
            _isPopupOpen = False
            CheckWhetherMouseHasLeft()
        End Try
    End Function

#End Region

End Class
