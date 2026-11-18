' ============================================================================
'  StageWindow.Kinds.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  What is different about each kind of Stage or tab (Model\StageKind.vb):
'
'    Standard ........... shortcuts dropped on it - the behaviour everywhere else
'                         in StageWindow is for this kind
'    Live folder ........ a folder's real files, optionally with sub-folders,
'                         name filters and an item limit (StageFolder.ScanLiveFolder)
'    Running tasks ...... the open windows (Services\RunningWindows.vb), read again
'                         every few seconds; opening an item switches to that window
'    Clipboard history .. recent copies kept as files (Services\ClipboardHistoryStore.vb);
'                         opening an entry copies it back to the clipboard
'
'  Each tab of a Stage has its own kind, so one Stage can hold, say, a
'  Standard tab, a Running tasks tab and a Clipboard history tab. A Clipboard
'  history tab keeps capturing even while another tab is open.
'
'  Also here: the "New Stage" and "New tab" sub-menus, which offer every kind,
'  and the kind-specific entries in the Stage menu (options, pause, clear).
' ============================================================================
Imports System.IO
Imports System.Windows.Threading
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports Stages.ViewModels

Partial Public Class StageWindow

#Region "Kind constants"
    ' --- "New Stage" / "New tab" sub-menus ---
    Private Const NewStageSubmenuText As String = "New Stage"
    Private Const NewStandardStageMenuText As String = "Standard Stage"
    Private Const NewLiveFolderStageMenuText As String = "Live folder..."
    Private Const NewRunningTasksStageMenuText As String = "Running tasks"
    Private Const NewClipboardStageMenuText As String = "Clipboard history"
    Private Const NewTabSubmenuText As String = "New tab"
    Private Const NewStandardTabMenuText As String = "Standard tab..."
    Private Const NewLiveFolderTabMenuText As String = "Live folder tab..."
    Private Const NewRunningTasksTabMenuText As String = "Running tasks tab"
    Private Const NewClipboardTabMenuText As String = "Clipboard history tab"

    ' --- Kind entries in the Stage menu ---
    Private Const LiveFolderOptionsMenuText As String = "Live folder options..."
    Private Const RunningTasksOptionsMenuText As String = "Running tasks options..."
    Private Const ClipboardOptionsMenuText As String = "Clipboard history options..."
    Private Const PauseClipboardMenuText As String = "Pause clipboard history"
    Private Const ClearClipboardMenuText As String = "Clear clipboard history..."

    ' --- Running tasks item commands ---
    Private Const SwitchToWindowText As String = "Switch to"
    Private Const MinimiseWindowText As String = "Minimise"
    Private Const RestoreWindowText As String = "Restore"
    Private Const CloseWindowText As String = "Close window"
    ''' <summary>{0} = number of selected windows.</summary>
    Private Const MinimiseWindowsMenuFormat As String = "Minimise {0} windows"
    Private Const CloseWindowsMenuFormat As String = "Close {0} windows"

    ' --- Clipboard history item commands and messages ---
    Private Const CopyToClipboardText As String = "Copy to clipboard"
    Private Const ClipboardCaption As String = "Clipboard history"
    Private Const CopyBackFailedText As String =
        "That entry couldn't be put on the clipboard. Another program may be using the clipboard, or the entry's file has gone. See the log for details."
    Private Const ClearClipboardQuestionText As String =
        "Delete every entry in this Clipboard history?" & ControlChars.CrLf & ControlChars.CrLf &
        "The saved text and pictures are deleted for good (they are Stages' own copies). Shortcuts to copied files are deleted too; the files themselves are not affected."

    ' --- Removing a Stage or tab of another kind ---
    Private Const RemoveRunningTasksConfirmationText As String =
        "Remove this Running tasks Stage?" & ControlChars.CrLf & ControlChars.CrLf &
        "The programs and windows it shows are not affected; only the Stage's own settings are moved to the Recycle Bin."
    Private Const RemoveClipboardConfirmationText As String =
        "Remove this Clipboard history?" & ControlChars.CrLf & ControlChars.CrLf &
        "Its saved entries are moved to the Recycle Bin with it. Files that were copied are not affected."
#End Region

#Region "Kind fields"
    ''' <summary>Reads the window list again on a Running tasks tab; created the first time one is shown.</summary>
    Private _runningTasksTimer As DispatcherTimer
    ''' <summary>This Stage's handler for new copies (kept, so the same one can be removed again).</summary>
    Private _clipboardListener As Action(Of ClipboardSnapshot)
    Private _isListeningToClipboard As Boolean
    ''' <summary>
    ''' Running tasks: when each window was first seen (a rising number), so windows keep their
    ''' places instead of reshuffling every time another one is brought to the front.
    ''' </summary>
    Private ReadOnly _windowFirstSeen As New Dictionary(Of IntPtr, Long)()
    Private _nextWindowSequenceNumber As Long
#End Region

#Region "Scanning each kind"

    ''' <summary>
    ''' The work that lists the open tab's items, to run on the scan thread. Everything it needs
    ''' is copied now, on the UI thread. Nothing from the job = the folder isn't there right now.
    ''' </summary>
    Private Function BuildScanJob(scannedTab As StageInfo) As Func(Of List(Of StageItem))
        Dim savedOrder As List(Of String) = scannedTab.ItemOrder.ToList()
        Dim folderToScan As String = scannedTab.ContentFolderPath

        Select Case scannedTab.Kind
            Case StageKind.RunningTasks
                Dim windowFilter As New RunningWindows.WindowFilter(scannedTab)
                Return Function() RunningWindows.ListWindows(windowFilter)

            Case StageKind.LiveFolder
                Dim folderOptions As New StageFolder.LiveFolderOptions(scannedTab)
                Return Function() If(Directory.Exists(folderToScan),
                                     StageFolder.ScanLiveFolder(folderToScan, folderOptions, savedOrder),
                                     Nothing)

            Case Else ' Standard and Clipboard history: the files in the folder
                Return Function() If(Directory.Exists(folderToScan), StageFolder.ScanItems(folderToScan, savedOrder), Nothing)
        End Select
    End Function

    ''' <summary>A Running tasks tab's windows come and go: nothing about their order is worth saving.</summary>
    Private Function ShouldRememberScannedOrder(scannedTab As StageInfo) As Boolean
        Return Not scannedTab.IsRunningTasks
    End Function

    ''' <summary>
    ''' Puts the windows in a steady order: the order they were first seen in (the first time,
    ''' that is Windows' own front-to-back order), with new windows at the end - like taskbar
    ''' buttons. Windows that have closed are forgotten.
    ''' </summary>
    Private Function InTaskbarOrder(windowItems As List(Of StageItem)) As List(Of StageItem)
        For Each windowItem As StageItem In windowItems
            If Not _windowFirstSeen.ContainsKey(windowItem.WindowHandle) Then
                _windowFirstSeen.Add(windowItem.WindowHandle, _nextWindowSequenceNumber)
                _nextWindowSequenceNumber += 1
            End If
        Next

        Dim stillOpen As New HashSet(Of IntPtr)(windowItems.Select(Function(windowItem) windowItem.WindowHandle))
        Dim closedWindows As List(Of IntPtr) = _windowFirstSeen.Keys.Where(Function(handle) Not stillOpen.Contains(handle)).ToList()
        For Each closedWindow As IntPtr In closedWindows
            _windowFirstSeen.Remove(closedWindow)
        Next

        Return windowItems.OrderBy(Function(windowItem) _windowFirstSeen(windowItem.WindowHandle)).ToList()
    End Function

    ''' <summary>Whether the open tab's folder should be watched for changes (a Running tasks tab has nothing to watch).</summary>
    Private Function ShouldWatchActiveFolder() As Boolean
        Return Not ActiveTab.IsRunningTasks
    End Function

    ''' <summary>A Live folder that looks inside sub-folders needs its watcher to see inside them too.</summary>
    Private Function ShouldWatchSubfolders() As Boolean
        Return ActiveTab.Kind = StageKind.LiveFolder AndAlso ActiveTab.LiveFolderDepth > 0
    End Function

    ''' <summary>
    ''' Starts or stops the Running tasks timer to match the open tab (called when a tab opens,
    ''' when the Stage loads and after its options change).
    ''' </summary>
    Private Sub UpdateKindTimers()
        If _isClosed OrElse Not ActiveTab.IsRunningTasks Then
            If _runningTasksTimer IsNot Nothing Then _runningTasksTimer.Stop()
            Return
        End If

        If _runningTasksTimer Is Nothing Then
            _runningTasksTimer = New DispatcherTimer()
            AddHandler _runningTasksTimer.Tick, AddressOf RunningTasksTimer_Tick
        End If
        _runningTasksTimer.Interval = TimeSpan.FromSeconds(ActiveTab.RunningTasksIntervalSeconds)
        _runningTasksTimer.Start()
    End Sub

    ''' <summary>Reads the window list again (not while all Stages are hidden - nobody would see it).</summary>
    Private Sub RunningTasksTimer_Tick(sender As Object, e As EventArgs)
        If Not IsVisible OrElse Not ActiveTab.IsRunningTasks Then Return
        RefreshItems()
    End Sub

#End Region

#Region "Opening an item of each kind"

    ''' <summary>
    ''' Double-click or Enter on an item: a window is switched to, a clipboard entry is copied
    ''' back to the clipboard, and anything else opens as Explorer would open it.
    ''' </summary>
    Private Sub OpenItem(itemToOpen As StageItemViewModel)
        If itemToOpen.Item.IsWindow Then
            RunningWindows.SwitchTo(itemToOpen.Item.WindowHandle)
        ElseIf ActiveTab.IsClipboardHistory Then
            CopyClipboardEntry(itemToOpen)
        Else
            itemToOpen.Item.Open()
        End If
    End Sub

    ''' <summary>Puts a Clipboard history entry back on the clipboard (it moves to the front).</summary>
    Private Sub CopyClipboardEntry(entry As StageItemViewModel)
        If ClipboardHistoryStore.CopyBack(ActiveTab, entry.Item) Then
            SaveActiveTab() ' its new place at the front
            RefreshItems()
        Else
            ShowMessage(CopyBackFailedText, ClipboardCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
        End If
    End Sub

#End Region

#Region "Running tasks: the window menu"

    ''' <summary>Right-click on a window: Switch to, Minimise or Restore, Close window (and the Stage's own commands via Shift).</summary>
    Private Sub ShowWindowItemMenu(item As StageItemViewModel)
        Dim chosenAction As Action = Nothing
        Using windowMenu As New NativePopupMenu()
            Dim commandTexts As New List(Of String)()
            Dim commandActions As New List(Of Action)()
            AddWindowCommands(item, commandTexts, commandActions)
            For commandIndex As Integer = 0 To commandTexts.Count - 1
                ' The first command (Switch to) is what a double-click does, so it is shown in bold.
                windowMenu.AddItem(commandTexts(commandIndex), commandActions(commandIndex), isDefault:=(commandIndex = 0))
            Next

            Dim cursorPosition As NativeMethods.NativePoint = DesktopIntegration.GetCursorScreenPosition()
            _isPopupOpen = True
            Try
                chosenAction = windowMenu.ShowAndGetChoice(_windowHandle, cursorPosition.X, cursorPosition.Y)
            Finally
                _isPopupOpen = False
            End Try
        End Using

        CheckWhetherMouseHasLeft()
        If chosenAction IsNot Nothing Then Dispatcher.InvokeAsync(chosenAction)
    End Sub

    ''' <summary>The commands for one window, shared by its own menu and the Stage menu.</summary>
    Private Sub AddWindowCommands(item As StageItemViewModel, commandTexts As List(Of String), commandActions As List(Of Action))
        Dim windowHandle As IntPtr = item.Item.WindowHandle
        commandTexts.Add(SwitchToWindowText)
        commandActions.Add(Sub() RunningWindows.SwitchTo(windowHandle))

        If RunningWindows.IsMinimised(windowHandle) Then
            commandTexts.Add(RestoreWindowText)
            commandActions.Add(Sub() RunningWindows.SwitchTo(windowHandle))
        Else
            commandTexts.Add(MinimiseWindowText)
            commandActions.Add(Sub() ActOnWindowsThenRefresh({windowHandle}, AddressOf RunningWindows.Minimise))
        End If

        commandTexts.Add(CloseWindowText)
        commandActions.Add(Sub() ActOnWindowsThenRefresh({windowHandle}, AddressOf RunningWindows.Close))
    End Sub

    ''' <summary>Several windows selected: Minimise or Close them all (added to the selection menu).</summary>
    Private Sub AddSelectedWindowsCommands(selectionMenu As NativePopupMenu, selectedItems As List(Of StageItemViewModel))
        Dim selectedWindows As IEnumerable(Of StageItemViewModel) = selectedItems.Where(Function(selected) selected.Item.IsWindow)
        Dim windowHandles As IntPtr() = selectedWindows.Select(Function(selected) selected.Item.WindowHandle).ToArray()
        If windowHandles.Length = 0 Then Return
        selectionMenu.AddItem(String.Format(MinimiseWindowsMenuFormat, windowHandles.Length),
                              Sub() ActOnWindowsThenRefresh(windowHandles, AddressOf RunningWindows.Minimise))
        selectionMenu.AddItem(String.Format(CloseWindowsMenuFormat, windowHandles.Length),
                              Sub() ActOnWindowsThenRefresh(windowHandles, AddressOf RunningWindows.Close))
    End Sub

    ''' <summary>Does something to each window, then reads the list again so the Stage shows the result.</summary>
    Private Sub ActOnWindowsThenRefresh(windowHandles As IEnumerable(Of IntPtr), windowAction As Action(Of IntPtr))
        For Each windowHandle As IntPtr In windowHandles
            windowAction.Invoke(windowHandle)
        Next
        RefreshItems()
    End Sub

#End Region

#Region "Kind entries in the Stage menu"

    ''' <summary>Adds the open tab's own entries (options, and for Clipboard history: pause and clear) to the Stage menu.</summary>
    Private Sub AddKindMenuItems(stageMenu As NativePopupMenu)
        Select Case ActiveTab.Kind
            Case StageKind.LiveFolder
                stageMenu.AddItem(LiveFolderOptionsMenuText, AddressOf ShowKindOptions)
            Case StageKind.RunningTasks
                stageMenu.AddItem(RunningTasksOptionsMenuText, AddressOf ShowKindOptions)
            Case StageKind.ClipboardHistory
                stageMenu.AddItem(PauseClipboardMenuText, AddressOf ToggleClipboardPaused, isChecked:=ActiveTab.ClipboardPaused)
                stageMenu.AddItem(ClearClipboardMenuText, AddressOf ClearClipboardHistory)
                stageMenu.AddItem(ClipboardOptionsMenuText, AddressOf ShowKindOptions)
        End Select
    End Sub

    ''' <summary>Shows the open tab's options dialog; on OK applies and saves them, and shows the result.</summary>
    Private Sub ShowKindOptions()
        Dim optionsTab As StageInfo = ActiveTab
        Dim dialog As New StageKindOptionsDialog(optionsTab) With {.Owner = Me}
        Dim wasConfirmed As Boolean
        _isPopupOpen = True
        Try
            wasConfirmed = dialog.ShowDialog().GetValueOrDefault()
        Finally
            _isPopupOpen = False
        End Try
        CheckWhetherMouseHasLeft()
        If Not wasConfirmed OrElse optionsTab IsNot ActiveTab Then Return

        dialog.ApplyTo(optionsTab)
        If optionsTab.IsClipboardHistory Then ClipboardHistoryStore.ApplyLimits(optionsTab) ' smaller limits apply straight away
        SaveActiveTab()
        StartWatchingFolder() ' a Live folder's folder or depth may have changed
        UpdateKindTimers()    ' a Running tasks interval may have changed
        RefreshItems()
    End Sub

    Private Sub ToggleClipboardPaused()
        ActiveTab.ClipboardPaused = Not ActiveTab.ClipboardPaused
        SaveActiveTab()
    End Sub

    ''' <summary>Asks, then deletes every entry of the open Clipboard history tab.</summary>
    Private Sub ClearClipboardHistory()
        If ShowMessage(ClearClipboardQuestionText, ClipboardCaption, MessageBoxButton.YesNo, MessageBoxImage.Warning) <> MessageBoxResult.Yes Then
            Return
        End If
        ClipboardHistoryStore.ClearAll(ActiveTab)
        SaveActiveTab()
        RefreshItems()
    End Sub

    ''' <summary>The question asked before removing a Stage or tab, by its kind (Nothing = the caller's usual text).</summary>
    Private Shared Function RemoveConfirmationForKind(tab As StageInfo) As String
        Select Case tab.Kind
            Case StageKind.RunningTasks
                Return RemoveRunningTasksConfirmationText
            Case StageKind.ClipboardHistory
                Return RemoveClipboardConfirmationText
            Case Else
                Return Nothing
        End Select
    End Function

#End Region

#Region "New Stage and New tab sub-menus"

    ''' <summary>Stage menu: "New Stage" with one entry per kind.</summary>
    Private Sub AddNewStageSubmenu(stageMenu As NativePopupMenu)
        Dim newStageMenu As NativePopupMenu = stageMenu.AddSubmenu(NewStageSubmenuText)
        newStageMenu.AddItem(NewStandardStageMenuText, AddressOf CreateNewStage)
        newStageMenu.AddItem(NewLiveFolderStageMenuText, AddressOf CreateNewFolderStage)
        newStageMenu.AddItem(NewRunningTasksStageMenuText, Sub() StageManager.Instance.CreateStageOfKind(StageKind.RunningTasks))
        newStageMenu.AddItem(NewClipboardStageMenuText, Sub() StageManager.Instance.CreateStageOfKind(StageKind.ClipboardHistory))
    End Sub

    ''' <summary>Tabs menu: "New tab" with one entry per kind.</summary>
    Private Sub AddNewTabSubmenu(tabsMenu As NativePopupMenu)
        Dim newTabMenu As NativePopupMenu = tabsMenu.AddSubmenu(NewTabSubmenuText)
        newTabMenu.AddItem(NewStandardTabMenuText, AddressOf CreateNewTab)
        newTabMenu.AddItem(NewLiveFolderTabMenuText, AddressOf CreateNewFolderTab)
        newTabMenu.AddItem(NewRunningTasksTabMenuText, Sub() CreateNewTabOfKind(StageKind.RunningTasks, StageDefaults.NewRunningTasksStageName))
        newTabMenu.AddItem(NewClipboardTabMenuText, Sub() CreateNewTabOfKind(StageKind.ClipboardHistory, StageDefaults.NewClipboardStageName))
    End Sub

    ''' <summary>Adds a Running tasks or Clipboard history tab (named after its kind; rename it like any tab) and opens it.</summary>
    Private Sub CreateNewTabOfKind(kind As StageKind, tabTitle As String)
        AddAndOpenTab(StageManager.Instance.CreateTab(_settings, tabTitle, kind:=kind))
    End Sub

#End Region

#Region "Clipboard history: capturing"

    ''' <summary>
    ''' Listens for copies while this Stage has at least one Clipboard history tab, and stops
    ''' otherwise (called whenever the tabs change, and when the Stage closes).
    ''' </summary>
    Private Sub UpdateClipboardListening()
        Dim shouldListen As Boolean = Not _isClosed AndAlso _tabs.Any(Function(tab) tab.IsClipboardHistory)
        If shouldListen = _isListeningToClipboard Then Return

        If _clipboardListener Is Nothing Then _clipboardListener = AddressOf OnClipboardCopied
        If shouldListen Then
            ClipboardMonitor.Instance.AddListener(_clipboardListener)
        Else
            ClipboardMonitor.Instance.RemoveListener(_clipboardListener)
        End If
        _isListeningToClipboard = shouldListen
    End Sub

    ''' <summary>Something was copied: every Clipboard history tab of this Stage keeps it (each by its own options).</summary>
    Private Sub OnClipboardCopied(snapshot As ClipboardSnapshot)
        If _isClosed Then Return
        For Each clipboardTab As StageInfo In _tabs.Where(Function(tab) tab.IsClipboardHistory).ToList()
            If Not ClipboardHistoryStore.Capture(clipboardTab, snapshot) Then Continue For
            ' Save its order (newest first) - the Stage's own file for the first tab.
            If clipboardTab Is _settings Then
                SaveNow()
            Else
                StageManager.Instance.SaveStage(clipboardTab)
            End If
            If clipboardTab Is ActiveTab Then RefreshItems()
        Next
    End Sub

#End Region

End Class
