' ============================================================================
'  StageItemsPanel.vb
'  Lays out the items on a Stage. It is the ItemsPanel of the Stage's ListBox
'  (StagesStyles.xaml), and works in one of three ways (ItemsPanelMode):
'
'    Flow  - icons left to right in sort order, wrapping onto new rows.
'            Used when "Auto arrange icons" is on.
'    Free  - each icon where the user dropped it (its LayoutPosition); icons
'            without a position fill the first free grid cells. Used when
'            "Auto arrange icons" is off, like the Windows desktop.
'    Rows  - one item per row, full width. Used by the List and Details views.
'
'  Every icon gets the same "cell" (CellWidth x CellHeight), which is what makes
'  the grid. The mode and sizes are set by StageWindow on the ListBox and reach
'  this panel through WPF property inheritance (the "Inherits" option below), so
'  the panel never needs to know about the window.
'
'  StageWindow also asks the panel where it put each item (GetArrangedRect),
'  to work out drops when items are dragged.
' ============================================================================

''' <summary>An item that may have been placed by hand. StageItemViewModel implements it.</summary>
Public Interface IPositionedItem
    ''' <summary>Top-left of the item in the panel, or Nothing to let the panel place it.</summary>
    Property LayoutPosition As Point?
End Interface

''' <summary>How StageItemsPanel lays out its items (see the file header).</summary>
Public Enum ItemsPanelMode
    Flow
    Free
    Rows
End Enum

Public Class StageItemsPanel
    Inherits Panel

#Region "Settings (inherited from the ListBox)"

    ''' <summary>Flow, Free or Rows.</summary>
    Public Shared ReadOnly ModeProperty As DependencyProperty =
        DependencyProperty.RegisterAttached("Mode", GetType(ItemsPanelMode), GetType(StageItemsPanel),
            New FrameworkPropertyMetadata(ItemsPanelMode.Flow,
                FrameworkPropertyMetadataOptions.Inherits Or FrameworkPropertyMetadataOptions.AffectsMeasure))

    ''' <summary>Width of one icon cell (Flow and Free).</summary>
    Public Shared ReadOnly CellWidthProperty As DependencyProperty =
        DependencyProperty.RegisterAttached("CellWidth", GetType(Double), GetType(StageItemsPanel),
            New FrameworkPropertyMetadata(DefaultCellWidth,
                FrameworkPropertyMetadataOptions.Inherits Or FrameworkPropertyMetadataOptions.AffectsMeasure))

    ''' <summary>Height of one icon cell (Flow and Free), or of one row (Rows).</summary>
    Public Shared ReadOnly CellHeightProperty As DependencyProperty =
        DependencyProperty.RegisterAttached("CellHeight", GetType(Double), GetType(StageItemsPanel),
            New FrameworkPropertyMetadata(DefaultCellHeight,
                FrameworkPropertyMetadataOptions.Inherits Or FrameworkPropertyMetadataOptions.AffectsMeasure))

    ''' <summary>Cell size used until StageWindow sets the real one (the Medium icons view).</summary>
    Private Const DefaultCellWidth As Double = 87
    Private Const DefaultCellHeight As Double = 88

    Public Shared Function GetMode(target As DependencyObject) As ItemsPanelMode
        Return DirectCast(target.GetValue(ModeProperty), ItemsPanelMode)
    End Function
    Public Shared Sub SetMode(target As DependencyObject, value As ItemsPanelMode)
        target.SetValue(ModeProperty, value)
    End Sub

    Public Shared Function GetCellWidth(target As DependencyObject) As Double
        Return CDbl(target.GetValue(CellWidthProperty))
    End Function
    Public Shared Sub SetCellWidth(target As DependencyObject, value As Double)
        target.SetValue(CellWidthProperty, value)
    End Sub

    Public Shared Function GetCellHeight(target As DependencyObject) As Double
        Return CDbl(target.GetValue(CellHeightProperty))
    End Function
    Public Shared Sub SetCellHeight(target As DependencyObject, value As Double)
        target.SetValue(CellHeightProperty, value)
    End Sub

#End Region

#Region "Layout"

    ''' <summary>Where each child goes, worked out in MeasureOverride and used by ArrangeOverride.</summary>
    Private ReadOnly _plannedRects As New Dictionary(Of UIElement, Rect)()
    ''' <summary>Where each child actually went at the last arrange (for StageWindow's drag and drop).</summary>
    Private ReadOnly _arrangedRects As New Dictionary(Of UIElement, Rect)()

    ''' <summary>Used if the panel is ever measured without a width limit (it normally has one).</summary>
    Private Const FallbackColumns As Integer = 3

    Protected Overrides Function MeasureOverride(availableSize As Size) As Size
        Dim cellWidth As Double = GetCellWidth(Me)
        Dim cellHeight As Double = GetCellHeight(Me)
        Dim panelWidth As Double = If(Double.IsInfinity(availableSize.Width), cellWidth * FallbackColumns, availableSize.Width)

        _plannedRects.Clear()
        Select Case GetMode(Me)
            Case ItemsPanelMode.Rows
                PlanRows(panelWidth, cellHeight)
            Case ItemsPanelMode.Free
                PlanFreePlacement(panelWidth, cellWidth, cellHeight)
            Case Else
                PlanFlow(panelWidth, cellWidth, cellHeight)
        End Select

        ' Each child is measured at the size it will get; the panel is as tall as its lowest item.
        Dim contentHeight As Double = 0
        For Each child As UIElement In InternalChildren
            Dim plannedRect As Rect = _plannedRects(child)
            child.Measure(plannedRect.Size)
            contentHeight = Math.Max(contentHeight, plannedRect.Bottom)
        Next
        Return New Size(panelWidth, contentHeight)
    End Function

    Protected Overrides Function ArrangeOverride(finalSize As Size) As Size
        _arrangedRects.Clear()
        For Each child As UIElement In InternalChildren
            Dim plannedRect As Rect = Nothing
            If Not _plannedRects.TryGetValue(child, plannedRect) Then
                Continue For ' added since the last measure; a new measure is already on its way
            End If
            child.Arrange(plannedRect)
            _arrangedRects(child) = plannedRect
        Next
        Return finalSize
    End Function

    ''' <summary>List / Details: one full-width row per item.</summary>
    Private Sub PlanRows(panelWidth As Double, rowHeight As Double)
        Dim top As Double = 0
        For Each child As UIElement In InternalChildren
            _plannedRects(child) = New Rect(0, top, panelWidth, rowHeight)
            top += rowHeight
        Next
    End Sub

    ''' <summary>Auto arrange: cells left to right, then the next row.</summary>
    Private Sub PlanFlow(panelWidth As Double, cellWidth As Double, cellHeight As Double)
        Dim columnCount As Integer = ColumnsThatFit(panelWidth, cellWidth)
        Dim position As Integer = 0
        For Each child As UIElement In InternalChildren
            Dim column As Integer = position Mod columnCount
            Dim row As Integer = position \ columnCount
            _plannedRects(child) = New Rect(column * cellWidth, row * cellHeight, cellWidth, cellHeight)
            position += 1
        Next
    End Sub

    ''' <summary>
    ''' Auto arrange off: items with a position go there (kept inside the panel's width);
    ''' the rest take the first grid cells nobody is using, left to right, top to bottom.
    ''' </summary>
    Private Sub PlanFreePlacement(panelWidth As Double, cellWidth As Double, cellHeight As Double)
        Dim columnCount As Integer = ColumnsThatFit(panelWidth, cellWidth)
        Dim usedCells As New HashSet(Of Long)()
        Dim unplacedChildren As New List(Of UIElement)()
        Dim rightmostLeft As Double = Math.Max(0, panelWidth - cellWidth)

        ' 1. Hand-placed items.
        For Each child As UIElement In InternalChildren
            Dim positioned As IPositionedItem = TryCast(TryCast(child, FrameworkElement)?.DataContext, IPositionedItem)
            If positioned Is Nothing OrElse Not positioned.LayoutPosition.HasValue Then
                unplacedChildren.Add(child)
                Continue For
            End If

            Dim wanted As Point = positioned.LayoutPosition.Value
            Dim left As Double = Math.Max(0, Math.Min(wanted.X, rightmostLeft))
            Dim top As Double = Math.Max(0, wanted.Y)
            Dim wantedCell As Long = CellKeyAtPoint(left + cellWidth / 2, top + cellHeight / 2, cellWidth, cellHeight)
            If usedCells.Contains(wantedCell) Then
                ' Another icon is already there (e.g. both were pushed into the last column
                ' when the Stage was made narrower): show this one in the nearest free cell.
                ' Only the display moves; the saved position is kept, so widening the Stage
                ' again puts it back.
                Dim freeTopLeft As Point = FindFreeCellNear(New Point(left, top), usedCells, columnCount, cellWidth, cellHeight)
                left = freeTopLeft.X
                top = freeTopLeft.Y
                wantedCell = CellKeyAtPoint(left + cellWidth / 2, top + cellHeight / 2, cellWidth, cellHeight)
            End If
            _plannedRects(child) = New Rect(left, top, cellWidth, cellHeight)
            usedCells.Add(wantedCell)
        Next

        ' 2. Everything else: next free cell.
        Dim candidateCell As Integer = 0
        For Each child As UIElement In unplacedChildren
            Do While usedCells.Contains(CellKey(candidateCell Mod columnCount, candidateCell \ columnCount))
                candidateCell += 1
            Loop
            Dim column As Integer = candidateCell Mod columnCount
            Dim row As Integer = candidateCell \ columnCount
            _plannedRects(child) = New Rect(column * cellWidth, row * cellHeight, cellWidth, cellHeight)
            usedCells.Add(CellKey(column, row))
            candidateCell += 1
        Next
    End Sub

    Private Shared Function ColumnsThatFit(panelWidth As Double, cellWidth As Double) As Integer
        Return Math.Max(1, CInt(Math.Floor(panelWidth / cellWidth)))
    End Function

#End Region

#Region "Grid cells (used by drag and drop)"

    ''' <summary>A single number for a (column, row) pair, for the "used cells" set.</summary>
    Private Shared Function CellKey(column As Integer, row As Integer) As Long
        Const RowMultiplier As Long = 100000L
        Return row * RowMultiplier + column
    End Function

    Private Shared Function CellKeyAtPoint(x As Double, y As Double, cellWidth As Double, cellHeight As Double) As Long
        Return CellKey(CInt(Math.Floor(x / cellWidth)), CInt(Math.Floor(y / cellHeight)))
    End Function

    ''' <summary>Where a child was last arranged, in panel coordinates (Nothing if it hasn't been).</summary>
    Public Function GetArrangedRect(child As UIElement) As Rect?
        Dim arrangedRect As Rect = Nothing
        If child IsNot Nothing AndAlso _arrangedRects.TryGetValue(child, arrangedRect) Then
            Return arrangedRect
        End If
        Return Nothing
    End Function

    ''' <summary>The top-left of the grid cell nearest to <paramref name="wantedTopLeft"/>.</summary>
    Public Function SnapToGrid(wantedTopLeft As Point) As Point
        Dim cellWidth As Double = GetCellWidth(Me)
        Dim cellHeight As Double = GetCellHeight(Me)
        Dim column As Double = Math.Max(0, Math.Round(wantedTopLeft.X / cellWidth))
        Dim row As Double = Math.Max(0, Math.Round(wantedTopLeft.Y / cellHeight))
        Return New Point(column * cellWidth, row * cellHeight)
    End Function

    ''' <summary>
    ''' The free grid cell nearest to <paramref name="wantedTopLeft"/>, not counting
    ''' <paramref name="ignoredChildren"/> (the items being dragged). Searches outwards
    ''' ring by ring, so a drop onto a taken cell lands right beside it.
    ''' <paramref name="alreadyChosen"/>: cells just given to other dragged items (top-left
    ''' corners), which count as taken even though nothing has been laid out there yet.
    ''' </summary>
    Public Function NearestFreeCell(wantedTopLeft As Point, ignoredChildren As ICollection(Of UIElement),
                                    alreadyChosen As IEnumerable(Of Point)) As Point
        Dim cellWidth As Double = GetCellWidth(Me)
        Dim cellHeight As Double = GetCellHeight(Me)
        Dim columnCount As Integer = ColumnsThatFit(ActualWidth, cellWidth)

        Dim usedCells As New HashSet(Of Long)()
        For Each placed As KeyValuePair(Of UIElement, Rect) In _arrangedRects
            If ignoredChildren.Contains(placed.Key) Then Continue For
            usedCells.Add(CellKeyAtPoint(placed.Value.X + cellWidth / 2, placed.Value.Y + cellHeight / 2, cellWidth, cellHeight))
        Next
        For Each chosenTopLeft As Point In alreadyChosen
            usedCells.Add(CellKeyAtPoint(chosenTopLeft.X + cellWidth / 2, chosenTopLeft.Y + cellHeight / 2, cellWidth, cellHeight))
        Next

        Return FindFreeCellNear(wantedTopLeft, usedCells, columnCount, cellWidth, cellHeight)
    End Function

    ''' <summary>
    ''' The free grid cell nearest to <paramref name="wantedTopLeft"/>, counting only the cells
    ''' in <paramref name="takenTopLefts"/> as taken (not the items on screen). For placing
    ''' several icons in one go, e.g. snapping them all to the grid.
    ''' </summary>
    Public Function FreeCellAmong(wantedTopLeft As Point, takenTopLefts As IEnumerable(Of Point)) As Point
        Dim cellWidth As Double = GetCellWidth(Me)
        Dim cellHeight As Double = GetCellHeight(Me)
        Dim usedCells As New HashSet(Of Long)()
        For Each takenTopLeft As Point In takenTopLefts
            usedCells.Add(CellKeyAtPoint(takenTopLeft.X + cellWidth / 2, takenTopLeft.Y + cellHeight / 2, cellWidth, cellHeight))
        Next
        Return FindFreeCellNear(wantedTopLeft, usedCells, ColumnsThatFit(ActualWidth, cellWidth), cellWidth, cellHeight)
    End Function

    ''' <summary>
    ''' The search shared by the functions above: from the grid cell nearest the wanted spot,
    ''' outwards ring by ring, the first cell not in <paramref name="usedCells"/>.
    ''' </summary>
    Private Shared Function FindFreeCellNear(wantedTopLeft As Point, usedCells As HashSet(Of Long), columnCount As Integer,
                                             cellWidth As Double, cellHeight As Double) As Point
        Dim snappedColumn As Integer = CInt(Math.Max(0, Math.Round(wantedTopLeft.X / cellWidth)))
        Dim snappedRow As Integer = CInt(Math.Max(0, Math.Round(wantedTopLeft.Y / cellHeight)))
        Dim snapped As New Point(snappedColumn * cellWidth, snappedRow * cellHeight)
        Dim startColumn As Integer = Math.Min(columnCount - 1, snappedColumn)
        Dim startRow As Integer = snappedRow
        Const MaximumSearchDistance As Integer = 200

        For distance As Integer = 0 To MaximumSearchDistance
            For rowOffset As Integer = -distance To distance
                For columnOffset As Integer = -distance To distance
                    ' Only the outer ring of this distance - the inner ones were tried already.
                    If Math.Max(Math.Abs(rowOffset), Math.Abs(columnOffset)) <> distance Then Continue For
                    Dim column As Integer = startColumn + columnOffset
                    Dim row As Integer = startRow + rowOffset
                    If column < 0 OrElse column >= columnCount OrElse row < 0 Then Continue For
                    If Not usedCells.Contains(CellKey(column, row)) Then
                        Return New Point(column * cellWidth, row * cellHeight)
                    End If
                Next
            Next
        Next
        Return snapped ' everything nearby is full: overlap rather than fail
    End Function

#End Region

End Class
