' ============================================================================
'  StageWindow.Layout.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  How a Stage shows and arranges its items, and the mouse work on them:
'
'    View ............... large / medium / small icons, list, details
'    Sort by ............ custom (dragged) order, name, type, date modified, size
'    Auto arrange ....... on: icons flow in order; off: icons stay where dropped,
'                         optionally snapped to the grid (like the desktop)
'    Selecting .......... click, Ctrl/Shift+click, or drag a rubber band across
'                         empty space
'    Rearranging ........ drag selected items to a new place (not while Locked)
'    Opening ............ double-click, or Enter on the selection
'
'  The actual placing of items on screen is StageItemsPanel's job; this file
'  tells it which mode and cell size to use, and saves what the user changes.
' ============================================================================
Imports System.Windows.Controls.Primitives
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports Stages.ViewModels

Partial Public Class StageWindow

#Region "Layout constants"
    ' Icon sizes in pixels for each view.
    Private Const LargeIconSize As Integer = 48
    Private Const MediumIconSize As Integer = 32
    Private Const SmallIconSize As Integer = 16

    ' Label widths (DIPs) - the same as the DataTemplates in StagesStyles.xaml.
    Private Const LargeIconLabelWidth As Double = 86
    Private Const MediumIconLabelWidth As Double = 75
    Private Const SmallIconLabelWidth As Double = 70

    ' Space between icon and label (DIPs) - also matching the DataTemplates.
    Private Const LargeIconLabelGap As Double = 5
    Private Const MediumIconLabelGap As Double = 5
    Private Const SmallIconLabelGap As Double = 3

    ''' <summary>
    ''' Extra space round each item: margin 3 + border 1 + padding 2 on both sides
    ''' (StageIconContainerStyle) = 12.
    ''' </summary>
    Private Const ItemBoxOverhead As Double = 12

    ''' <summary>List / Details row height = label font size x this, plus RowExtraHeight.</summary>
    Private Const RowHeightPerFontUnit As Double = 1.6
    Private Const RowExtraHeight As Double = 6
    Private Const MinimumRowHeight As Double = 22

    ''' <summary>Used if the label resource can't be read (matches StagesStyles.xaml).</summary>
    Private Const FallbackLabelMaxHeight As Double = 35
    Private Const FallbackLabelFontSize As Double = 12
    Private Const LabelMaxHeightResourceKey As String = "StageLabelMaxHeight"
    Private Const LabelFontSizeResourceKey As String = "StageLabelFontSize"

    ' Resource keys of the templates and container styles (StagesStyles.xaml).
    Private Const LargeIconTemplateKey As String = "StageLargeIconTemplate"
    Private Const MediumIconTemplateKey As String = "StageMediumIconTemplate"
    Private Const SmallIconTemplateKey As String = "StageSmallIconTemplate"
    Private Const ListTemplateKey As String = "StageListTemplate"
    Private Const DetailsTemplateKey As String = "StageDetailsTemplate"
    Private Const IconContainerStyleKey As String = "StageIconContainerStyle"
    Private Const RowContainerStyleKey As String = "StageRowContainerStyle"

    ''' <summary>Dragged items are drawn above the others while moving.</summary>
    Private Const DraggedItemLayer As Integer = 1000
    Private Const NormalItemLayer As Integer = 0

    ' Menu texts.
    Private Const ViewMenuText As String = "View"
    Private Const SortByMenuText As String = "Sort by"
    Private Const LargeIconsMenuText As String = "Large icons"
    Private Const MediumIconsMenuText As String = "Medium icons"
    Private Const SmallIconsMenuText As String = "Small icons"
    Private Const ListMenuText As String = "List"
    Private Const DetailsMenuText As String = "Details"
    Private Const AutoArrangeMenuText As String = "Auto arrange icons"
    Private Const AlignToGridMenuText As String = "Align icons to grid"
    Private Const SortByNameMenuText As String = "Name"
    Private Const SortByTypeMenuText As String = "Type"
    Private Const SortByDateMenuText As String = "Date modified"
    Private Const SortBySizeMenuText As String = "Size"
    Private Const SortByCustomMenuText As String = "Custom order (drag to arrange)"
    Private Const SortDescendingMenuText As String = "Descending"
    Private Const OpenItemsMenuFormat As String = "Open {0} items"
    ''' <summary>Opening more items than this at once asks first (Explorer asks above 15 too).</summary>
    Private Const ManyItemsToOpenWithoutAsking As Integer = 15
    Private Const OpenManyItemsCaption As String = "Open items"
    Private Const OpenManyItemsQuestionFormat As String = "Open all {0} items? Each one opens in its own program or window."

    ' Scrolling while dragging items near the top or bottom edge of the item area.
    ''' <summary>How close to the edge (DIPs) the mouse must be to scroll.</summary>
    Private Const DragScrollEdgeZone As Double = 24
    ''' <summary>How far each step scrolls (DIPs), and how often a step happens.</summary>
    Private Const DragScrollStep As Double = 14
    Private Shared ReadOnly DragScrollInterval As TimeSpan = TimeSpan.FromMilliseconds(40)
    Private Const RemoveItemsMenuFormat As String = "Remove {0} items from Stage"
#End Region

#Region "Layout fields"
    ''' <summary>The item that the left mouse button went down on (Nothing for empty space).</summary>
    Private _pressedItem As StageItemViewModel
    ''' <summary>Where the button went down, in the items panel's coordinates.</summary>
    Private _pressPointInPanel As Point
    ''' <summary>
    ''' The press was on an item that was already selected (with no Ctrl/Shift). The selection
    ''' is kept so several items can be dragged together; if the mouse is released without a
    ''' drag, it becomes a plain click that selects only that item.
    ''' </summary>
    Private _isClickOnSelectedPending As Boolean
    Private _isDraggingItems As Boolean
    ''' <summary>The items being dragged, with the box each is drawn in.</summary>
    Private ReadOnly _draggedItems As New List(Of StageItemViewModel)()
    Private ReadOnly _draggedContainers As New List(Of ListBoxItem)()

    ''' <summary>Scrolls the items while a drag is held near the top or bottom edge (created on first drag).</summary>
    Private _dragScrollTimer As System.Windows.Threading.DispatcherTimer

    Private _isBandSelecting As Boolean
    ''' <summary>Where the rubber band started, in SelectionOverlay coordinates.</summary>
    Private _bandStartPoint As Point
    ''' <summary>What was selected before the band started (kept when Ctrl is held).</summary>
    Private ReadOnly _selectionBeforeBand As New List(Of Object)()
#End Region

#Region "View: templates, cell sizes, icon sizes"

    ''' <summary>
    ''' Applies the active tab's View: item template, item box style, the panel's mode and
    ''' cell size, the Details headings, and the icon size each item loads.
    ''' </summary>
    Private Sub ApplyViewMode()
        Dim viewMode As StageViewMode = ActiveTab.ViewMode
        Dim isRowView As Boolean = (viewMode = StageViewMode.List OrElse viewMode = StageViewMode.Details)

        ItemsList.ItemTemplate = DirectCast(FindResource(TemplateKeyFor(viewMode)), DataTemplate)
        ItemsList.ItemContainerStyle = DirectCast(FindResource(If(isRowView, RowContainerStyleKey, IconContainerStyleKey)), Style)
        DetailsHeader.Visibility = If(viewMode = StageViewMode.Details, Visibility.Visible, Visibility.Collapsed)
        ApplyDetailsColumns() ' which Details columns show, in what order - StageWindow.DetailsColumns.vb

        ApplyPanelLayout()

        Dim iconSize As Integer = IconSizeFor(viewMode)
        For Each itemViewModel As StageItemViewModel In _items
            itemViewModel.ChangeIconSize(iconSize)
        Next
    End Sub

    ''' <summary>Tells StageItemsPanel (through the ListBox) how to lay out: mode and cell size.</summary>
    Private Sub ApplyPanelLayout()
        Dim viewMode As StageViewMode = ActiveTab.ViewMode
        Dim labelMaxHeight As Double = ReadDoubleResource(LabelMaxHeightResourceKey, FallbackLabelMaxHeight)
        Dim labelFontSize As Double = ReadDoubleResource(LabelFontSizeResourceKey, FallbackLabelFontSize)

        Dim cellWidth As Double
        Dim cellHeight As Double
        Select Case viewMode
            Case StageViewMode.LargeIcons
                cellWidth = LargeIconLabelWidth + ItemBoxOverhead
                cellHeight = LargeIconSize + LargeIconLabelGap + labelMaxHeight + ItemBoxOverhead
            Case StageViewMode.SmallIcons
                cellWidth = SmallIconLabelWidth + ItemBoxOverhead
                cellHeight = SmallIconSize + SmallIconLabelGap + labelMaxHeight + ItemBoxOverhead
            Case StageViewMode.List, StageViewMode.Details
                cellWidth = 0 ' rows are full width
                cellHeight = Math.Max(MinimumRowHeight, Math.Round(labelFontSize * RowHeightPerFontUnit + RowExtraHeight))
            Case Else
                cellWidth = MediumIconLabelWidth + ItemBoxOverhead
                cellHeight = MediumIconSize + MediumIconLabelGap + labelMaxHeight + ItemBoxOverhead
        End Select

        If cellWidth > 0 Then
            StageItemsPanel.SetCellWidth(ItemsList, cellWidth)
        End If
        StageItemsPanel.SetCellHeight(ItemsList, cellHeight)
        StageItemsPanel.SetMode(ItemsList, CurrentPanelMode())
    End Sub

    ''' <summary>The panel mode for the active tab's view and arrangement settings.</summary>
    Private Function CurrentPanelMode() As ItemsPanelMode
        Select Case ActiveTab.ViewMode
            Case StageViewMode.List, StageViewMode.Details
                Return ItemsPanelMode.Rows
            Case Else
                Return If(ActiveTab.AutoArrange, ItemsPanelMode.Flow, ItemsPanelMode.Free)
        End Select
    End Function

    Private Shared Function TemplateKeyFor(viewMode As StageViewMode) As String
        Select Case viewMode
            Case StageViewMode.LargeIcons : Return LargeIconTemplateKey
            Case StageViewMode.SmallIcons : Return SmallIconTemplateKey
            Case StageViewMode.List : Return ListTemplateKey
            Case StageViewMode.Details : Return DetailsTemplateKey
            Case Else : Return MediumIconTemplateKey
        End Select
    End Function

    ''' <summary>Pixel size of the icons each view shows.</summary>
    Private Shared Function IconSizeFor(viewMode As StageViewMode) As Integer
        Select Case viewMode
            Case StageViewMode.LargeIcons : Return LargeIconSize
            Case StageViewMode.MediumIcons : Return MediumIconSize
            Case Else : Return SmallIconSize
        End Select
    End Function

    ''' <summary>The icon size items of the active tab should load at.</summary>
    Private Function CurrentIconSize() As Integer
        Return IconSizeFor(ActiveTab.ViewMode)
    End Function

    Private Function ReadDoubleResource(resourceKey As String, fallbackValue As Double) As Double
        Dim foundValue As Object = TryFindResource(resourceKey)
        If TypeOf foundValue Is Double Then Return CDbl(foundValue)
        Return fallbackValue
    End Function

    ''' <summary>The StageItemsPanel inside the ListBox (Nothing until the list has been drawn once).</summary>
    Private Function FindItemsPanel() As StageItemsPanel
        Return FindDescendant(Of StageItemsPanel)(ItemsList)
    End Function

    ''' <summary>The first element of type T below <paramref name="root"/> in the visual tree.</summary>
    Private Shared Function FindDescendant(Of T As DependencyObject)(root As DependencyObject) As T
        Dim childCount As Integer = VisualTreeHelper.GetChildrenCount(root)
        For childIndex As Integer = 0 To childCount - 1
            Dim child As DependencyObject = VisualTreeHelper.GetChild(root, childIndex)
            Dim match As T = TryCast(child, T)
            If match IsNot Nothing Then Return match
            Dim deeperMatch As T = FindDescendant(Of T)(child)
            If deeperMatch IsNot Nothing Then Return deeperMatch
        Next
        Return Nothing
    End Function

#End Region

#Region "Sorting and hand-placed positions"

    ''' <summary>
    ''' The items in the order to show them. A scan already returns them in Custom order;
    ''' other sorts put folders first and compare names the way Explorer does ("2" before "10").
    ''' </summary>
    Private Function SortForDisplay(scannedItems As List(Of StageItem)) As List(Of StageItem)
        Dim sortOrder As StageSortOrder = ActiveTab.SortBy
        If sortOrder = StageSortOrder.Custom Then Return scannedItems

        Dim explorerOrder As New ExplorerNameComparer()
        Dim foldersFirst As IOrderedEnumerable(Of StageItem) = scannedItems.OrderBy(Function(item) If(item.OpensFolder, 0, 1))
        Dim sorted As IOrderedEnumerable(Of StageItem)
        Select Case sortOrder
            Case StageSortOrder.Type
                sorted = If(ActiveTab.SortDescending,
                            foldersFirst.ThenByDescending(Function(item) item.TypeName, explorerOrder),
                            foldersFirst.ThenBy(Function(item) item.TypeName, explorerOrder))
                sorted = sorted.ThenBy(Function(item) item.LabelWithoutExtension, explorerOrder)
            Case StageSortOrder.DateModified
                sorted = If(ActiveTab.SortDescending,
                            foldersFirst.ThenByDescending(Function(item) item.ModifiedTicks),
                            foldersFirst.ThenBy(Function(item) item.ModifiedTicks))
                sorted = sorted.ThenBy(Function(item) item.LabelWithoutExtension, explorerOrder)
            Case StageSortOrder.Size
                ' Items without a size (StageItem.NoSize = -1: folders, web links, windows) count
                ' as the smallest; folders are already grouped first anyway.
                sorted = If(ActiveTab.SortDescending,
                            foldersFirst.ThenByDescending(Function(item) item.SizeBytes),
                            foldersFirst.ThenBy(Function(item) item.SizeBytes))
                sorted = sorted.ThenBy(Function(item) item.LabelWithoutExtension, explorerOrder)
            Case Else ' Name
                sorted = If(ActiveTab.SortDescending,
                            foldersFirst.ThenByDescending(Function(item) item.LabelWithoutExtension, explorerOrder),
                            foldersFirst.ThenBy(Function(item) item.LabelWithoutExtension, explorerOrder))
        End Select
        Return sorted.ToList()
    End Function

    ''' <summary>Compares names like Explorer does (Windows' own StrCmpLogicalW).</summary>
    Private NotInheritable Class ExplorerNameComparer
        Implements IComparer(Of String)

        Public Function Compare(first As String, second As String) As Integer Implements IComparer(Of String).Compare
            Return NativeMethods.StrCmpLogicalW(If(first, String.Empty), If(second, String.Empty))
        End Function
    End Class

    ''' <summary>
    ''' Gives every displayed item its saved hand-placed position (Auto arrange off), or none
    ''' (Auto arrange on, List, Details), then asks the panel to lay out again.
    ''' </summary>
    Private Sub ApplyItemPositions()
        Dim useSavedPositions As Boolean = (CurrentPanelMode() = ItemsPanelMode.Free)
        Dim positionsByName As New Dictionary(Of String, ItemPosition)(StringComparer.OrdinalIgnoreCase)
        If useSavedPositions Then
            For Each placed As ItemPosition In ActiveTab.ItemPositions
                If placed.FileName IsNot Nothing Then positionsByName(placed.FileName) = placed
            Next
        End If

        For Each itemViewModel As StageItemViewModel In _items
            Dim placed As ItemPosition = Nothing
            If useSavedPositions AndAlso positionsByName.TryGetValue(itemViewModel.Item.FileName, placed) Then
                itemViewModel.LayoutPosition = New Point(placed.X, placed.Y)
            Else
                itemViewModel.LayoutPosition = Nothing
            End If
        Next

        Dim itemsPanel As StageItemsPanel = FindItemsPanel()
        If itemsPanel IsNot Nothing Then itemsPanel.InvalidateMeasure()
    End Sub

    ''' <summary>Records (or replaces) one item's hand-placed position in the active tab's settings.</summary>
    Private Sub RememberItemPosition(itemFileName As String, topLeft As Point)
        ActiveTab.ItemPositions.RemoveAll(Function(placed) String.Equals(placed.FileName, itemFileName, StringComparison.OrdinalIgnoreCase))
        ActiveTab.ItemPositions.Add(New ItemPosition(itemFileName, topLeft.X, topLeft.Y))
    End Sub

    ''' <summary>Forgets hand-placed positions of items that no longer exist.</summary>
    Private Sub ForgetPositionsOfMissingItems(currentFileNames As IEnumerable(Of String))
        Dim stillThere As New HashSet(Of String)(currentFileNames, StringComparer.OrdinalIgnoreCase)
        Dim removedCount As Integer = ActiveTab.ItemPositions.RemoveAll(Function(placed) Not stillThere.Contains(If(placed.FileName, String.Empty)))
        ' The positions belong to the open tab, so it's the tab's settings that need saving.
        If removedCount > 0 Then ScheduleActiveTabSave()
    End Sub

#End Region

#Region "View and Sort by menus"

    ''' <summary>Adds View and Sort by to the Stage menu.</summary>
    Private Sub AddLayoutSubmenus(stageMenu As NativePopupMenu)
        Dim tab As StageInfo = ActiveTab

        Dim viewMenu As NativePopupMenu = stageMenu.AddSubmenu(ViewMenuText)
        viewMenu.AddItem(LargeIconsMenuText, Sub() SetViewMode(StageViewMode.LargeIcons), isChecked:=tab.ViewMode = StageViewMode.LargeIcons)
        viewMenu.AddItem(MediumIconsMenuText, Sub() SetViewMode(StageViewMode.MediumIcons), isChecked:=tab.ViewMode = StageViewMode.MediumIcons)
        viewMenu.AddItem(SmallIconsMenuText, Sub() SetViewMode(StageViewMode.SmallIcons), isChecked:=tab.ViewMode = StageViewMode.SmallIcons)
        viewMenu.AddItem(ListMenuText, Sub() SetViewMode(StageViewMode.List), isChecked:=tab.ViewMode = StageViewMode.List)
        viewMenu.AddItem(DetailsMenuText, Sub() SetViewMode(StageViewMode.Details), isChecked:=tab.ViewMode = StageViewMode.Details)
        AddDetailsColumnsSubmenu(viewMenu) ' which columns Details shows - StageWindow.DetailsColumns.vb
        viewMenu.AddSeparator()
        ' Arranging by hand only exists in the icon views, as on the desktop.
        Dim isIconView As Boolean = (CurrentPanelMode() <> ItemsPanelMode.Rows)
        viewMenu.AddItem(AutoArrangeMenuText, AddressOf ToggleAutoArrange, isChecked:=tab.AutoArrange, isEnabled:=isIconView)
        viewMenu.AddItem(AlignToGridMenuText, AddressOf ToggleAlignToGrid, isChecked:=tab.AlignToGrid,
                         isEnabled:=isIconView AndAlso Not tab.AutoArrange)

        Dim sortMenu As NativePopupMenu = stageMenu.AddSubmenu(SortByMenuText)
        sortMenu.AddItem(SortByNameMenuText, Sub() SetSortOrder(StageSortOrder.Name), isChecked:=tab.SortBy = StageSortOrder.Name)
        sortMenu.AddItem(SortByTypeMenuText, Sub() SetSortOrder(StageSortOrder.Type), isChecked:=tab.SortBy = StageSortOrder.Type)
        sortMenu.AddItem(SortByDateMenuText, Sub() SetSortOrder(StageSortOrder.DateModified), isChecked:=tab.SortBy = StageSortOrder.DateModified)
        sortMenu.AddItem(SortBySizeMenuText, Sub() SetSortOrder(StageSortOrder.Size), isChecked:=tab.SortBy = StageSortOrder.Size)
        sortMenu.AddItem(SortByCustomMenuText, Sub() SetSortOrder(StageSortOrder.Custom), isChecked:=tab.SortBy = StageSortOrder.Custom)
        sortMenu.AddSeparator()
        sortMenu.AddItem(SortDescendingMenuText, AddressOf ToggleSortDescending, isChecked:=tab.SortDescending,
                         isEnabled:=tab.SortBy <> StageSortOrder.Custom)
    End Sub

    Private Sub SetViewMode(newViewMode As StageViewMode)
        ActiveTab.ViewMode = newViewMode
        ApplyViewMode()
        ApplyItemPositions()
        SaveActiveTab()
    End Sub

    ''' <summary>
    ''' Sorts the items. As on the desktop, choosing a sort also tidies away hand-placed
    ''' positions, so the icons line up in the new order.
    ''' </summary>
    Private Sub SetSortOrder(newSortOrder As StageSortOrder)
        ActiveTab.SortBy = newSortOrder
        If newSortOrder <> StageSortOrder.Custom Then
            ActiveTab.ItemPositions.Clear()
        End If
        SaveActiveTab()
        RefreshItems() ' re-orders the items
    End Sub

    Private Sub ToggleSortDescending()
        ActiveTab.SortDescending = Not ActiveTab.SortDescending
        SaveActiveTab()
        RefreshItems()
    End Sub

    Private Sub ToggleAutoArrange()
        ActiveTab.AutoArrange = Not ActiveTab.AutoArrange
        ApplyPanelLayout()
        ApplyItemPositions()
        SaveActiveTab()
    End Sub

    Private Sub ToggleAlignToGrid()
        ActiveTab.AlignToGrid = Not ActiveTab.AlignToGrid
        If ActiveTab.AlignToGrid Then
            SnapAllPositionsToGrid()
        End If
        SaveActiveTab()
    End Sub

    ''' <summary>
    ''' Turning Align to grid on moves every hand-placed icon onto the grid. Two icons that
    ''' would snap into the same cell don't: the second goes to the nearest free one.
    ''' </summary>
    Private Sub SnapAllPositionsToGrid()
        Dim itemsPanel As StageItemsPanel = FindItemsPanel()
        If itemsPanel Is Nothing Then Return
        Dim takenCells As New List(Of Point)()
        For Each placed As ItemPosition In ActiveTab.ItemPositions
            Dim snapped As Point = itemsPanel.FreeCellAmong(New Point(placed.X, placed.Y), takenCells)
            placed.X = snapped.X
            placed.Y = snapped.Y
            takenCells.Add(snapped)
        Next
        ApplyItemPositions()
    End Sub

    ' Details view: clicking a heading sorts by it; clicking the same heading again reverses it.
    Private Sub DetailsNameHeader_Click(sender As Object, e As RoutedEventArgs)
        SortFromHeading(StageSortOrder.Name)
    End Sub

    Private Sub DetailsTypeHeader_Click(sender As Object, e As RoutedEventArgs)
        SortFromHeading(StageSortOrder.Type)
    End Sub

    Private Sub DetailsDateHeader_Click(sender As Object, e As RoutedEventArgs)
        SortFromHeading(StageSortOrder.DateModified)
    End Sub

    Private Sub SortFromHeading(clickedSort As StageSortOrder)
        If ActiveTab.SortBy = clickedSort Then
            ToggleSortDescending()
        Else
            ActiveTab.SortDescending = False
            SetSortOrder(clickedSort)
        End If
    End Sub

#End Region

#Region "Selecting, opening and dragging items"

    ''' <summary>
    ''' Left button down in the item area:
    '''   double-click on an item -> open it;
    '''   on empty space          -> start a rubber-band selection;
    '''   on an item              -> remember it, in case this becomes a drag.
    ''' </summary>
    Private Sub ItemsList_PreviewMouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        Dim pressedElement As DependencyObject = TryCast(e.OriginalSource, DependencyObject)
        If IsInsideScrollBar(pressedElement) Then Return ' let the scrollbar work normally

        Dim itemsPanel As StageItemsPanel = FindItemsPanel()
        If itemsPanel Is Nothing Then Return
        Dim clickedItem As StageItemViewModel = FindItemFromElement(pressedElement)

        If clickedItem IsNot Nothing AndAlso e.ClickCount = DoubleClickCount Then
            e.Handled = True
            OpenItem(clickedItem) ' opens, switches to the window, or copies back - StageWindow.Kinds.vb
            Return
        End If

        ' Taking keyboard focus lets Enter and Ctrl+A work on the items.
        ItemsList.Focus()

        If clickedItem Is Nothing Then
            BeginBandSelection(e)
            Return
        End If

        _pressedItem = clickedItem
        _pressPointInPanel = e.GetPosition(itemsPanel)
        Dim noModifierKeys As Boolean = (Keyboard.Modifiers = ModifierKeys.None)
        If ItemsList.SelectedItems.Contains(clickedItem) AndAlso noModifierKeys Then
            ' Keep the current (possibly multiple) selection for a drag; see _isClickOnSelectedPending.
            ItemsList.CaptureMouse()
            _isClickOnSelectedPending = True
            e.Handled = True
        End If
        ' Otherwise the ListBox selects the item as usual, and we still watch for a drag.
    End Sub

    Private Sub ItemsList_PreviewMouseMove(sender As Object, e As MouseEventArgs)
        If _isBandSelecting Then
            UpdateBandSelection(e.GetPosition(SelectionOverlay))
            ' Handled: the ListBox's own drag-to-select would otherwise change the selection too.
            e.Handled = True
            Return
        End If
        If _pressedItem Is Nothing OrElse e.LeftButton <> MouseButtonState.Pressed Then Return

        Dim itemsPanel As StageItemsPanel = FindItemsPanel()
        If itemsPanel Is Nothing Then Return
        Dim offset As Vector = e.GetPosition(itemsPanel) - _pressPointInPanel

        If Not _isDraggingItems Then
            Dim movedFarEnough As Boolean = Math.Abs(offset.X) >= SystemParameters.MinimumHorizontalDragDistance OrElse
                                            Math.Abs(offset.Y) >= SystemParameters.MinimumVerticalDragDistance
            ' A locked Stage keeps its arrangement; open windows (Running tasks) can't be
            ' rearranged, as their order is the order Windows keeps them in.
            If Not movedFarEnough OrElse _settings.Locked OrElse ActiveTab.IsRunningTasks Then Return
            BeginItemDrag()
        End If

        MoveDraggedItemsWithMouse(itemsPanel)
        ' Handled: stops the ListBox selecting the items the mouse passes over.
        e.Handled = True
    End Sub

    ''' <summary>
    ''' Draws the dragged items under the mouse (they are only really placed on release).
    ''' Measured in the panel's own coordinates, so it stays right while the list scrolls.
    ''' </summary>
    Private Sub MoveDraggedItemsWithMouse(itemsPanel As StageItemsPanel)
        Dim offset As Vector = Mouse.GetPosition(itemsPanel) - _pressPointInPanel
        For Each draggedContainer As ListBoxItem In _draggedContainers
            Dim moveTransform As TranslateTransform = TryCast(draggedContainer.RenderTransform, TranslateTransform)
            If moveTransform IsNot Nothing Then
                moveTransform.X = offset.X
                moveTransform.Y = offset.Y
            End If
        Next
    End Sub

    ''' <summary>
    ''' While items are dragged: if the mouse is held near the top or bottom of the item area,
    ''' scroll a step that way, so items can be dragged to places scrolled out of view.
    ''' </summary>
    Private Sub DragScrollTimer_Tick(sender As Object, e As EventArgs)
        If Not _isDraggingItems Then
            _dragScrollTimer.Stop()
            Return
        End If
        Dim itemsScroller As ScrollViewer = FindDescendant(Of ScrollViewer)(ItemsList)
        Dim itemsPanel As StageItemsPanel = FindItemsPanel()
        If itemsScroller Is Nothing OrElse itemsPanel Is Nothing Then Return

        Dim mouseY As Double = Mouse.GetPosition(itemsScroller).Y
        If mouseY < DragScrollEdgeZone Then
            itemsScroller.ScrollToVerticalOffset(itemsScroller.VerticalOffset - DragScrollStep)
        ElseIf mouseY > itemsScroller.ActualHeight - DragScrollEdgeZone Then
            itemsScroller.ScrollToVerticalOffset(itemsScroller.VerticalOffset + DragScrollStep)
        Else
            Return
        End If
        ' The content moved under a still mouse: keep the dragged items under it.
        itemsScroller.UpdateLayout()
        MoveDraggedItemsWithMouse(itemsPanel)
    End Sub

    Private Sub ItemsList_PreviewMouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs)
        If _isBandSelecting Then
            EndBandSelection()
            e.Handled = True
            Return
        End If

        If _isDraggingItems Then
            Dim itemsPanel As StageItemsPanel = FindItemsPanel()
            If itemsPanel IsNot Nothing Then
                FinishItemDrag(itemsPanel, e.GetPosition(itemsPanel))
            End If
            e.Handled = True
        ElseIf _isClickOnSelectedPending AndAlso _pressedItem IsNot Nothing Then
            ' No drag happened: it was a plain click, which selects just this item.
            ItemsList.SelectedItems.Clear()
            ItemsList.SelectedItem = _pressedItem
            e.Handled = True
        End If
        ResetPointerState()
    End Sub

    ''' <summary>Something else took the mouse mid-drag (e.g. a window popped up): put things back.</summary>
    Private Sub ItemsList_LostMouseCapture(sender As Object, e As MouseEventArgs)
        If _isDraggingItems Then
            ClearDragVisuals()
            ResetPointerState()
        End If
        If _isBandSelecting Then
            EndBandSelection()
        End If
    End Sub

    ''' <summary>Enter opens every selected item.</summary>
    Private Sub ItemsList_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then
            e.Handled = True
            OpenItems(SelectedItemsInDisplayOrder())
        End If
    End Sub

    ''' <summary>
    ''' Opens items. Above ManyItemsToOpenWithoutAsking it asks first (as Explorer does), so
    ''' Ctrl+A then Enter on a big folder Stage can't start hundreds of programs by accident.
    ''' </summary>
    Private Sub OpenItems(itemsToOpen As List(Of StageItemViewModel))
        If itemsToOpen.Count = 0 Then Return
        ' The clipboard holds one thing at a time: copying several entries back would leave
        ' only the last, so only the first is copied.
        If ActiveTab.IsClipboardHistory Then
            OpenItem(itemsToOpen(0))
            Return
        End If
        If itemsToOpen.Count > ManyItemsToOpenWithoutAsking Then
            Dim question As String = String.Format(OpenManyItemsQuestionFormat, itemsToOpen.Count)
            If ShowMessage(question, OpenManyItemsCaption, MessageBoxButton.YesNo, MessageBoxImage.Question) <> MessageBoxResult.Yes Then
                Return
            End If
        End If
        For Each itemToOpen As StageItemViewModel In itemsToOpen
            OpenItem(itemToOpen)
        Next
    End Sub

    Private Shared Function IsInsideScrollBar(element As DependencyObject) As Boolean
        Dim current As DependencyObject = element
        While current IsNot Nothing
            If TypeOf current Is ScrollBar Then Return True
            current = If(TypeOf current Is Visual, VisualTreeHelper.GetParent(current), LogicalTreeHelper.GetParent(current))
        End While
        Return False
    End Function

    ''' <summary>The selected items, in the order they're shown.</summary>
    Private Function SelectedItemsInDisplayOrder() As List(Of StageItemViewModel)
        Return _items.Where(Function(itemViewModel) ItemsList.SelectedItems.Contains(itemViewModel)).ToList()
    End Function

    ''' <summary>The drag has started: lift every selected item so it follows the mouse.</summary>
    Private Sub BeginItemDrag()
        If Not ItemsList.SelectedItems.Contains(_pressedItem) Then
            ItemsList.SelectedItems.Clear()
            ItemsList.SelectedItem = _pressedItem
        End If

        ' Take the mouse first: if the ListBox held it (it does after selecting an item),
        ' the hand-over raises LostMouseCapture, which must not look like a cancelled drag.
        ItemsList.CaptureMouse()
        _isDraggingItems = True
        _isClickOnSelectedPending = False
        _isPopupOpen = True ' a minified Stage stays open while dragging
        _draggedItems.Clear()
        _draggedContainers.Clear()

        For Each selectedItem As StageItemViewModel In SelectedItemsInDisplayOrder()
            Dim itemContainer As ListBoxItem = TryCast(ItemsList.ItemContainerGenerator.ContainerFromItem(selectedItem), ListBoxItem)
            If itemContainer Is Nothing Then Continue For
            _draggedItems.Add(selectedItem)
            _draggedContainers.Add(itemContainer)
            itemContainer.RenderTransform = New TranslateTransform()
            Panel.SetZIndex(itemContainer, DraggedItemLayer)
        Next

        If _dragScrollTimer Is Nothing Then
            _dragScrollTimer = New System.Windows.Threading.DispatcherTimer() With {.Interval = DragScrollInterval}
            AddHandler _dragScrollTimer.Tick, AddressOf DragScrollTimer_Tick
        End If
        _dragScrollTimer.Start()
    End Sub

    ''' <summary>
    ''' The dragged items were released at <paramref name="dropPoint"/> (panel coordinates).
    ''' Auto arrange off: each item moves by the distance dragged (onto the grid, if on).
    ''' Otherwise: the items are moved in the order to where they were dropped, and the
    ''' Stage switches to Custom order.
    ''' </summary>
    Private Sub FinishItemDrag(itemsPanel As StageItemsPanel, dropPoint As Point)
        Dim offset As Vector = dropPoint - _pressPointInPanel

        If CurrentPanelMode() = ItemsPanelMode.Free Then
            PlaceDraggedItemsFreely(itemsPanel, offset)
        Else
            InsertDraggedItemsInOrder(itemsPanel, dropPoint)
        End If

        ClearDragVisuals()
        SaveActiveTab()
        itemsPanel.InvalidateMeasure()
    End Sub

    ''' <summary>Auto arrange off: move each dragged item by <paramref name="offset"/>.</summary>
    Private Sub PlaceDraggedItemsFreely(itemsPanel As StageItemsPanel, offset As Vector)
        ' Pin every item where it is now first, so items the panel placed automatically
        ' don't shuffle around when the dragged ones land - the desktop behaves the same.
        For Each itemViewModel As StageItemViewModel In _items
            If itemViewModel.LayoutPosition.HasValue Then Continue For
            Dim itemContainer As UIElement = TryCast(ItemsList.ItemContainerGenerator.ContainerFromItem(itemViewModel), UIElement)
            Dim currentRect As Rect? = itemsPanel.GetArrangedRect(itemContainer)
            If currentRect.HasValue Then
                itemViewModel.LayoutPosition = currentRect.Value.TopLeft
                RememberItemPosition(itemViewModel.Item.FileName, currentRect.Value.TopLeft)
            End If
        Next

        ' The dragged items' old places don't count as taken; the cells given to dragged
        ' items earlier in this loop do, so two of them never land in the same cell.
        Dim draggedElements As New List(Of UIElement)(_draggedContainers)
        Dim chosenCells As New List(Of Point)()
        For draggedIndex As Integer = 0 To _draggedItems.Count - 1
            Dim draggedItem As StageItemViewModel = _draggedItems(draggedIndex)
            Dim startRect As Rect? = itemsPanel.GetArrangedRect(_draggedContainers(draggedIndex))
            If Not startRect.HasValue Then Continue For

            Dim wanted As Point = startRect.Value.TopLeft + offset
            wanted = New Point(Math.Max(0, wanted.X), Math.Max(0, wanted.Y))
            If ActiveTab.AlignToGrid Then
                wanted = itemsPanel.NearestFreeCell(wanted, draggedElements, chosenCells)
                chosenCells.Add(wanted)
            End If
            draggedItem.LayoutPosition = wanted
            RememberItemPosition(draggedItem.Item.FileName, wanted)
        Next
    End Sub

    ''' <summary>
    ''' Auto arrange / List / Details: put the dragged items, in their current order, before
    ''' the first other item that comes after the drop point in reading order.
    ''' </summary>
    Private Sub InsertDraggedItemsInOrder(itemsPanel As StageItemsPanel, dropPoint As Point)
        Dim isRowView As Boolean = (CurrentPanelMode() = ItemsPanelMode.Rows)
        Dim remainingItems As List(Of StageItemViewModel) = _items.Where(Function(itemViewModel) Not _draggedItems.Contains(itemViewModel)).ToList()

        Dim insertIndex As Integer = remainingItems.Count ' default: at the end
        For candidateIndex As Integer = 0 To remainingItems.Count - 1
            Dim candidateContainer As UIElement = TryCast(ItemsList.ItemContainerGenerator.ContainerFromItem(remainingItems(candidateIndex)), UIElement)
            Dim candidateRect As Rect? = itemsPanel.GetArrangedRect(candidateContainer)
            If Not candidateRect.HasValue Then Continue For

            Dim box As Rect = candidateRect.Value
            Dim centre As Point = New Point(box.X + box.Width / 2, box.Y + box.Height / 2)
            Dim comesAfterDrop As Boolean
            If isRowView Then
                comesAfterDrop = dropPoint.Y < centre.Y
            Else
                Dim isOnLowerRow As Boolean = box.Top > dropPoint.Y
                Dim isSameRowFurtherRight As Boolean = dropPoint.Y >= box.Top AndAlso dropPoint.Y < box.Bottom AndAlso dropPoint.X < centre.X
                comesAfterDrop = isOnLowerRow OrElse isSameRowFurtherRight
            End If
            If comesAfterDrop Then
                insertIndex = candidateIndex
                Exit For
            End If
        Next

        Dim newOrder As New List(Of StageItemViewModel)(remainingItems)
        newOrder.InsertRange(insertIndex, _draggedItems)

        ' Show the new order straight away, keeping the same item objects (and selection).
        For targetIndex As Integer = 0 To newOrder.Count - 1
            Dim currentIndex As Integer = _items.IndexOf(newOrder(targetIndex))
            If currentIndex <> targetIndex Then _items.Move(currentIndex, targetIndex)
        Next

        ActiveTab.ItemOrder = newOrder.Select(Function(itemViewModel) itemViewModel.Item.FileName).ToList()
        ActiveTab.SortBy = StageSortOrder.Custom ' dragging defines a custom order
    End Sub

    ''' <summary>Puts the dragged items' drawing back to normal.</summary>
    Private Sub ClearDragVisuals()
        For Each draggedContainer As ListBoxItem In _draggedContainers
            draggedContainer.RenderTransform = Transform.Identity
            Panel.SetZIndex(draggedContainer, NormalItemLayer)
        Next
        _draggedContainers.Clear()
        _draggedItems.Clear()
    End Sub

    Private Sub ResetPointerState()
        Dim wasDragging As Boolean = _isDraggingItems
        If _dragScrollTimer IsNot Nothing Then _dragScrollTimer.Stop()
        _pressedItem = Nothing
        _isClickOnSelectedPending = False
        _isDraggingItems = False
        If ItemsList.IsMouseCaptured Then ItemsList.ReleaseMouseCapture()
        If wasDragging Then
            _isPopupOpen = False
            CheckWhetherMouseHasLeft()
        End If
    End Sub

    ' --- Rubber band ---------------------------------------------------------------

    Private Sub BeginBandSelection(e As MouseButtonEventArgs)
        ItemsList.CaptureMouse() ' first, for the same reason as in BeginItemDrag
        _isBandSelecting = True
        _bandStartPoint = e.GetPosition(SelectionOverlay)
        _selectionBeforeBand.Clear()

        Dim isAddingToSelection As Boolean = (Keyboard.Modifiers And ModifierKeys.Control) = ModifierKeys.Control
        If isAddingToSelection Then
            _selectionBeforeBand.AddRange(ItemsList.SelectedItems.Cast(Of Object)())
        Else
            ItemsList.SelectedItems.Clear()
        End If

        Canvas.SetLeft(SelectionBand, _bandStartPoint.X)
        Canvas.SetTop(SelectionBand, _bandStartPoint.Y)
        SelectionBand.Width = 0
        SelectionBand.Height = 0
        SelectionBand.Visibility = Visibility.Visible
        _isPopupOpen = True
        e.Handled = True
    End Sub

    ''' <summary>Redraws the band to the mouse and selects every item it touches.</summary>
    Private Sub UpdateBandSelection(currentPoint As Point)
        Dim band As New Rect(_bandStartPoint, currentPoint)
        Canvas.SetLeft(SelectionBand, band.X)
        Canvas.SetTop(SelectionBand, band.Y)
        SelectionBand.Width = band.Width
        SelectionBand.Height = band.Height

        Dim touchedItems As New List(Of Object)(_selectionBeforeBand)
        For Each itemViewModel As StageItemViewModel In _items
            Dim itemContainer As FrameworkElement = TryCast(ItemsList.ItemContainerGenerator.ContainerFromItem(itemViewModel), FrameworkElement)
            If itemContainer Is Nothing OrElse Not itemContainer.IsVisible Then Continue For
            Dim itemTopLeft As Point = itemContainer.TranslatePoint(New Point(0, 0), SelectionOverlay)
            Dim itemBox As New Rect(itemTopLeft, New Size(itemContainer.ActualWidth, itemContainer.ActualHeight))
            If band.IntersectsWith(itemBox) AndAlso Not touchedItems.Contains(itemViewModel) Then
                touchedItems.Add(itemViewModel)
            End If
        Next

        ' Change the selection only where it differs, so the highlight doesn't flicker.
        For Each selectedItem As Object In ItemsList.SelectedItems.Cast(Of Object)().ToList()
            If Not touchedItems.Contains(selectedItem) Then ItemsList.SelectedItems.Remove(selectedItem)
        Next
        For Each touchedItem As Object In touchedItems
            If Not ItemsList.SelectedItems.Contains(touchedItem) Then ItemsList.SelectedItems.Add(touchedItem)
        Next
    End Sub

    Private Sub EndBandSelection()
        If Not _isBandSelecting Then Return
        _isBandSelecting = False
        SelectionBand.Visibility = Visibility.Collapsed
        _selectionBeforeBand.Clear()
        If ItemsList.IsMouseCaptured Then ItemsList.ReleaseMouseCapture()
        _isPopupOpen = False
        CheckWhetherMouseHasLeft()
    End Sub

#End Region

#Region "Several selected items"

    ''' <summary>Right-click with several items selected: open them all, or remove them all.</summary>
    Private Sub ShowSelectionMenu(selectedItems As List(Of StageItemViewModel))
        Dim chosenAction As Action = Nothing
        Using selectionMenu As New NativePopupMenu()
            selectionMenu.AddItem(String.Format(OpenItemsMenuFormat, selectedItems.Count),
                                  Sub() OpenItems(selectedItems), isDefault:=True)
            ' Removing is offered only where it can't touch real files (see CanRemoveItems).
            If ActiveTab.IsFolderView Then
                ' Real files: Delete, following Settings > Items > Deleting from folder Stages.
                selectionMenu.AddItem(String.Format(DeleteFolderItemsMenuFormat, selectedItems.Count),
                                      Sub() DeleteFolderItems(selectedItems))
            End If
            If CanRemoveItems() Then
                selectionMenu.AddItem(String.Format(RemoveItemsMenuFormat, selectedItems.Count),
                                      Sub() RemoveItemsFromStage(selectedItems))
            End If
            ' Open windows: Minimise / Close them all - StageWindow.Kinds.vb.
            If ActiveTab.IsRunningTasks Then
                AddSelectedWindowsCommands(selectionMenu, selectedItems)
            End If

            Dim cursorPosition As NativeMethods.NativePoint = DesktopIntegration.GetCursorScreenPosition()
            _isPopupOpen = True
            Try
                chosenAction = selectionMenu.ShowAndGetChoice(_windowHandle, cursorPosition.X, cursorPosition.Y)
            Finally
                _isPopupOpen = False
            End Try
        End Using

        CheckWhetherMouseHasLeft()
        If chosenAction IsNot Nothing Then
            Dispatcher.InvokeAsync(chosenAction)
        End If
    End Sub

#End Region

End Class
