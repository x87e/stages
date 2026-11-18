' ============================================================================
'  StageWindow.DetailsColumns.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  The Details view's columns (v0.8.4, at Dan's request):
'
'    * Which columns show: right-click the column headings, or Stage menu >
'      View > Details columns. Name, Type, Date modified and Size; Name is
'      always shown, because it holds the icon and the label.
'    * Their order: drag a heading left or right and let go where it should
'      be. A click without moving still sorts by that column, as before.
'    * Reset columns puts back the default set and order.
'
'  Each tab keeps its own choice (StageInfo.DetailsColumns). The choice is
'  handed to every DetailsColumnsPanel - the headings and each row - by
'  setting one DetailsColumnLayout on this window (ApplyDetailsColumns); see
'  DetailsColumnsPanel.vb for how the columns are laid out.
'
'  Dragging a heading: on press, the heading under the mouse is remembered.
'  Once the mouse has moved further than Windows' drag distance, the heading
'  panel takes the mouse (CaptureMouse) - which also means the heading's
'  button never sees a click, so no sort happens - and the heading slides
'  with the mouse, see-through. On release it moves to wherever its centre
'  is among the other headings.
' ============================================================================
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services

Partial Public Class StageWindow

#Region "Details columns constants"
    Private Const DetailsColumnsSubmenuText As String = "Details columns"
    Private Const NameColumnMenuText As String = "Name"
    Private Const TypeColumnMenuText As String = "Type"
    Private Const DateModifiedColumnMenuText As String = "Date modified"
    Private Const SizeColumnMenuText As String = "Size"
    Private Const ResetColumnsMenuText As String = "Reset columns"
    ''' <summary>How see-through a heading is while it's being dragged (1 = solid).</summary>
    Private Const DraggedHeadingOpacity As Double = 0.6
    ''' <summary>Keeps the dragged heading drawn above the others.</summary>
    Private Const DraggedHeadingZIndex As Integer = 1
#End Region

#Region "Details columns fields"
    ''' <summary>The heading pressed (a possible drag), or Nothing.</summary>
    Private _pressedHeading As Button
    ''' <summary>Where the press happened, relative to the heading panel.</summary>
    Private _headingPressPoint As Point
    ''' <summary>True once the mouse moved far enough for the press to become a drag.</summary>
    Private _isDraggingHeading As Boolean
    ''' <summary>Slides the dragged heading sideways while it's dragged.</summary>
    Private _draggedHeadingShift As TranslateTransform
#End Region

#Region "Details columns: choosing which show"

    ''' <summary>Hands the active tab's columns to every DetailsColumnsPanel in this Stage (headings and rows).</summary>
    Private Sub ApplyDetailsColumns()
        DetailsColumnsPanel.SetColumnLayout(Me, New DetailsColumnLayout(ActiveTab.EffectiveDetailsColumns()))
    End Sub

    ''' <summary>Saves a new column choice for the active tab and shows it.</summary>
    Private Sub UseDetailsColumns(columnsInOrder As List(Of StageDetailsColumn))
        ActiveTab.DetailsColumns = columnsInOrder
        ApplyDetailsColumns()
        SaveActiveTab()
    End Sub

    ''' <summary>
    ''' Shows a hidden column (at the right-hand end) or hides a shown one. Name can't be
    ''' hidden - its menu entry is greyed out - but it's refused here too, just in case.
    ''' </summary>
    Private Sub ToggleDetailsColumn(column As StageDetailsColumn)
        If column = StageDetailsColumn.Name Then Return
        Dim columns As List(Of StageDetailsColumn) = ActiveTab.EffectiveDetailsColumns()
        If columns.Contains(column) Then
            columns.Remove(column)
        Else
            columns.Add(column)
        End If
        UseDetailsColumns(columns)
    End Sub

    ''' <summary>Back to the default columns and order (Nothing = follow StageDefaults.DefaultDetailsColumns).</summary>
    Private Sub ResetDetailsColumns()
        UseDetailsColumns(Nothing)
    End Sub

    ''' <summary>The column switches, ticked when shown, then Reset columns - for both menus that offer them.</summary>
    Private Sub AddDetailsColumnItems(columnsMenu As NativePopupMenu)
        Dim columns As List(Of StageDetailsColumn) = ActiveTab.EffectiveDetailsColumns()
        columnsMenu.AddItem(NameColumnMenuText, Nothing, isChecked:=True, isEnabled:=False) ' always shown
        columnsMenu.AddItem(TypeColumnMenuText, Sub() ToggleDetailsColumn(StageDetailsColumn.Type),
                            isChecked:=columns.Contains(StageDetailsColumn.Type))
        columnsMenu.AddItem(DateModifiedColumnMenuText, Sub() ToggleDetailsColumn(StageDetailsColumn.DateModified),
                            isChecked:=columns.Contains(StageDetailsColumn.DateModified))
        columnsMenu.AddItem(SizeColumnMenuText, Sub() ToggleDetailsColumn(StageDetailsColumn.Size),
                            isChecked:=columns.Contains(StageDetailsColumn.Size))
        columnsMenu.AddSeparator()
        columnsMenu.AddItem(ResetColumnsMenuText, AddressOf ResetDetailsColumns)
    End Sub

    ''' <summary>Stage menu > View > Details columns (only usable in the Details view).</summary>
    Private Sub AddDetailsColumnsSubmenu(viewMenu As NativePopupMenu)
        If ActiveTab.ViewMode <> StageViewMode.Details Then
            viewMenu.AddItem(DetailsColumnsSubmenuText, Nothing, isEnabled:=False)
            Return
        End If
        AddDetailsColumnItems(viewMenu.AddSubmenu(DetailsColumnsSubmenuText))
    End Sub

    ''' <summary>Right-click on the column headings: just the column switches.</summary>
    Private Sub ShowDetailsColumnsMenu()
        Dim chosenAction As Action = Nothing
        Using columnsMenu As New NativePopupMenu()
            AddDetailsColumnItems(columnsMenu)

            ' The menu blocks here until it closes; meanwhile the Stage must not minify.
            Dim cursorPosition As NativeMethods.NativePoint = DesktopIntegration.GetCursorScreenPosition()
            _isPopupOpen = True
            Try
                chosenAction = columnsMenu.ShowAndGetChoice(_windowHandle, cursorPosition.X, cursorPosition.Y)
            Finally
                _isPopupOpen = False
            End Try
        End Using

        CheckWhetherMouseHasLeft()
        If chosenAction IsNot Nothing Then
            Dispatcher.InvokeAsync(chosenAction)
        End If
    End Sub

    ''' <summary>True if <paramref name="clickedElement"/> is one of the column headings (or inside one).</summary>
    Private Function IsInsideDetailsHeader(clickedElement As DependencyObject) As Boolean
        Dim current As DependencyObject = clickedElement
        While current IsNot Nothing
            If current Is DetailsHeader Then Return True
            ' Visual elements have a visual parent; text fragments only have a logical one.
            current = If(TypeOf current Is Visual, VisualTreeHelper.GetParent(current), LogicalTreeHelper.GetParent(current))
        End While
        Return False
    End Function

    ''' <summary>The Size heading sorts by size, like the others (StageWindow.Layout.vb).</summary>
    Private Sub DetailsSizeHeader_Click(sender As Object, e As RoutedEventArgs)
        SortFromHeading(StageSortOrder.Size)
    End Sub

#End Region

#Region "Details columns: dragging a heading to move its column"

    ''' <summary>A press on a heading might become a drag: remember which heading and where. The click itself still goes ahead.</summary>
    Private Sub DetailsHeaderPanel_PreviewMouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        _pressedHeading = FindHeadingButton(TryCast(e.OriginalSource, DependencyObject))
        _headingPressPoint = e.GetPosition(DetailsHeaderPanel)
        _isDraggingHeading = False
    End Sub

    ''' <summary>Starts the drag once the mouse has moved far enough, then slides the heading along with it.</summary>
    Private Sub DetailsHeaderPanel_PreviewMouseMove(sender As Object, e As MouseEventArgs)
        If _pressedHeading Is Nothing OrElse e.LeftButton <> MouseButtonState.Pressed Then Return
        Dim distanceMoved As Double = e.GetPosition(DetailsHeaderPanel).X - _headingPressPoint.X

        If Not _isDraggingHeading Then
            If Math.Abs(distanceMoved) < SystemParameters.MinimumHorizontalDragDistance Then Return
            BeginHeadingDrag()
        End If
        _draggedHeadingShift.X = distanceMoved
        e.Handled = True
    End Sub

    ''' <summary>Let go: move the column to where its heading was dropped.</summary>
    Private Sub DetailsHeaderPanel_PreviewMouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs)
        If Not _isDraggingHeading Then
            _pressedHeading = Nothing ' a plain click: the heading's button sorts, as before
            Return
        End If
        e.Handled = True

        Dim draggedColumn As StageDetailsColumn = DetailsColumnsPanel.GetColumn(_pressedHeading)
        Dim startCell As Rect = DetailsHeaderPanel.ArrangedRectOf(draggedColumn)
        Dim droppedCentreX As Double = startCell.Left + startCell.Width / 2 + _draggedHeadingShift.X
        Dim newIndex As Integer = DetailsHeaderPanel.DropIndexFor(draggedColumn, droppedCentreX)

        EndHeadingDrag() ' before changing the columns, so the heading is drawn in its new place

        Dim columns As List(Of StageDetailsColumn) = ActiveTab.EffectiveDetailsColumns()
        Dim oldIndex As Integer = columns.IndexOf(draggedColumn)
        columns.Remove(draggedColumn)
        columns.Insert(Math.Min(newIndex, columns.Count), draggedColumn)
        If columns.IndexOf(draggedColumn) <> oldIndex Then
            UseDetailsColumns(columns)
        End If
    End Sub

    ''' <summary>The mouse was taken away mid-drag (a menu opened, the Stage hid...): drop the drag, change nothing.</summary>
    Private Sub DetailsHeaderPanel_LostMouseCapture(sender As Object, e As MouseEventArgs)
        If _isDraggingHeading Then EndHeadingDrag()
    End Sub

    ''' <summary>The heading panel takes the mouse, and the pressed heading turns see-through and starts following it.</summary>
    Private Sub BeginHeadingDrag()
        _isDraggingHeading = True
        _draggedHeadingShift = New TranslateTransform()
        _pressedHeading.RenderTransform = _draggedHeadingShift
        _pressedHeading.Opacity = DraggedHeadingOpacity
        Panel.SetZIndex(_pressedHeading, DraggedHeadingZIndex)
        ' Taking the mouse from the button means it never raises Click, so no sort happens.
        DetailsHeaderPanel.CaptureMouse()
    End Sub

    ''' <summary>Puts the dragged heading back to normal and lets go of the mouse.</summary>
    Private Sub EndHeadingDrag()
        _isDraggingHeading = False ' first, so the LostMouseCapture this causes does nothing
        If _pressedHeading IsNot Nothing Then
            _pressedHeading.RenderTransform = Transform.Identity
            _pressedHeading.ClearValue(UIElement.OpacityProperty)
            _pressedHeading.ClearValue(Panel.ZIndexProperty)
        End If
        _pressedHeading = Nothing
        If DetailsHeaderPanel.IsMouseCaptured Then DetailsHeaderPanel.ReleaseMouseCapture()
    End Sub

    ''' <summary>The heading button <paramref name="element"/> is part of, or Nothing.</summary>
    Private Function FindHeadingButton(element As DependencyObject) As Button
        Dim current As DependencyObject = element
        While current IsNot Nothing AndAlso current IsNot DetailsHeaderPanel
            Dim heading As Button = TryCast(current, Button)
            If heading IsNot Nothing Then Return heading
            current = If(TypeOf current Is Visual, VisualTreeHelper.GetParent(current), LogicalTreeHelper.GetParent(current))
        End While
        Return Nothing
    End Function

#End Region

End Class
