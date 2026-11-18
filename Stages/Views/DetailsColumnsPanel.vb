' ============================================================================
'  DetailsColumnsPanel.vb   (v0.8.4)
'  Lays out the Details view's columns: Name, Type, Date modified and Size,
'  in whatever order the tab has chosen, leaving out the hidden ones.
'
'  The same panel is used twice, so the two always line up:
'    * once for the column headings above the items (StageWindow.xaml), and
'    * once inside every item row (StageDetailsTemplate, StagesStyles.xaml).
'  Each child says which column it belongs to with the attached property
'  DetailsColumnsPanel.Column (e.g. local:DetailsColumnsPanel.Column="Size").
'
'  Which columns show, and their order, is a DetailsColumnLayout set ONCE on
'  the Stage window (StageWindow.DetailsColumns.vb). It reaches every panel
'  through WPF property inheritance (the "Inherits" option below), the same
'  way StageItemsPanel gets its settings, so the panels never need to know
'  about the window.
'
'  Widths: Type, Date modified and Size have fixed widths, the Name column
'  takes the rest. On a Stage too narrow for that, the fixed columns shrink
'  (all by the same proportion) so Name keeps at least NameMinimumWidth.
'
'  A hidden column's child is still a child of the panel (it's in the XAML),
'  it is just placed outside the panel, where the panel's clip (ClipToBounds)
'  hides it and it can't be clicked. That avoids switching Visibility on and
'  off in the middle of a layout pass.
' ============================================================================
Imports Stages.Model

''' <summary>
''' Which Details columns show, in order. Immutable: StageWindow makes a new one whenever the
''' choice changes, which is what tells every DetailsColumnsPanel to lay out again.
''' </summary>
Public NotInheritable Class DetailsColumnLayout

    ''' <summary>The columns to show, left to right. Always contains Name.</summary>
    Public ReadOnly Property VisibleColumns As IReadOnlyList(Of StageDetailsColumn)

    ''' <summary>The layout used until a Stage sets its own: the default columns.</summary>
    Public Shared ReadOnly DefaultLayout As New DetailsColumnLayout(StageDefaults.DefaultDetailsColumns)

    Public Sub New(columnsInOrder As IEnumerable(Of StageDetailsColumn))
        VisibleColumns = New List(Of StageDetailsColumn)(columnsInOrder).AsReadOnly()
    End Sub

    ''' <summary>True if <paramref name="column"/> is shown.</summary>
    Public Function IsShown(column As StageDetailsColumn) As Boolean
        Return VisibleColumns.Contains(column)
    End Function
End Class

Public NotInheritable Class DetailsColumnsPanel
    Inherits Panel

#Region "Column widths"
    ''' <summary>Fixed widths, in device-independent pixels, of the columns other than Name.</summary>
    Private Const TypeColumnWidth As Double = 110
    Private Const DateModifiedColumnWidth As Double = 115
    Private Const SizeColumnWidth As Double = 75
    ''' <summary>The Name column never gets narrower than this while the others can still shrink.</summary>
    Private Const NameMinimumWidth As Double = 80
    ''' <summary>Space between one column and the next (left of every column but the first).</summary>
    Private Const ColumnGap As Double = 6
    ''' <summary>Where a hidden column's child is put: well to the right of any Stage, so it's clipped away.</summary>
    Private Const HiddenColumnOffset As Double = 100000
#End Region

#Region "Attached properties"

    ''' <summary>
    ''' The columns to show (a DetailsColumnLayout). Set on the Stage window; inherited by every
    ''' panel inside it. A change makes each panel measure and arrange again.
    ''' </summary>
    Public Shared ReadOnly ColumnLayoutProperty As DependencyProperty =
        DependencyProperty.RegisterAttached("ColumnLayout", GetType(DetailsColumnLayout), GetType(DetailsColumnsPanel),
            New FrameworkPropertyMetadata(DetailsColumnLayout.DefaultLayout,
                FrameworkPropertyMetadataOptions.Inherits Or
                FrameworkPropertyMetadataOptions.AffectsMeasure Or
                FrameworkPropertyMetadataOptions.AffectsArrange,
                AddressOf OnColumnLayoutChanged))

    ''' <summary>Which column a child of the panel belongs to (Name by default).</summary>
    Public Shared ReadOnly ColumnProperty As DependencyProperty =
        DependencyProperty.RegisterAttached("Column", GetType(StageDetailsColumn), GetType(DetailsColumnsPanel),
            New FrameworkPropertyMetadata(StageDetailsColumn.Name,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure Or
                FrameworkPropertyMetadataOptions.AffectsParentArrange))

    Public Shared Function GetColumnLayout(target As DependencyObject) As DetailsColumnLayout
        Return DirectCast(target.GetValue(ColumnLayoutProperty), DetailsColumnLayout)
    End Function
    Public Shared Sub SetColumnLayout(target As DependencyObject, value As DetailsColumnLayout)
        target.SetValue(ColumnLayoutProperty, value)
    End Sub

    Public Shared Function GetColumn(target As DependencyObject) As StageDetailsColumn
        Return DirectCast(target.GetValue(ColumnProperty), StageDetailsColumn)
    End Function
    Public Shared Sub SetColumn(target As DependencyObject, value As StageDetailsColumn)
        target.SetValue(ColumnProperty, value)
    End Sub

    ''' <summary>
    ''' Belt and braces: the AffectsMeasure option above should already re-lay out a panel whose
    ''' inherited layout changed, but asking explicitly costs nothing.
    ''' </summary>
    Private Shared Sub OnColumnLayoutChanged(target As DependencyObject, e As DependencyPropertyChangedEventArgs)
        Dim panel As DetailsColumnsPanel = TryCast(target, DetailsColumnsPanel)
        If panel IsNot Nothing Then
            panel.InvalidateMeasure()
            panel.InvalidateArrange()
        End If
    End Sub

#End Region

#Region "Fields"
    ''' <summary>Where each visible column was put in the last arrange - used to work out drops when a heading is dragged.</summary>
    Private ReadOnly _arrangedColumns As New Dictionary(Of StageDetailsColumn, Rect)()
#End Region

    Public Sub New()
        ' Clips the children placed outside (hidden columns), and anything over-long.
        ClipToBounds = True
    End Sub

#Region "Layout"

    ''' <summary>The fixed width of a column other than Name.</summary>
    Private Shared Function FixedWidthOf(column As StageDetailsColumn) As Double
        Select Case column
            Case StageDetailsColumn.Type : Return TypeColumnWidth
            Case StageDetailsColumn.DateModified : Return DateModifiedColumnWidth
            Case StageDetailsColumn.Size : Return SizeColumnWidth
            Case Else : Return 0 ' Name takes what's left
        End Select
    End Function

    ''' <summary>
    ''' Each visible column's width for a panel <paramref name="totalWidth"/> wide (gaps included
    ''' in the total). With an unlimited width (Double.PositiveInfinity), the fixed columns get
    ''' their normal widths and Name gets its minimum.
    ''' </summary>
    Private Function ColumnWidthsFor(totalWidth As Double) As Dictionary(Of StageDetailsColumn, Double)
        Dim visibleColumns As IReadOnlyList(Of StageDetailsColumn) = GetColumnLayout(Me).VisibleColumns
        Dim widths As New Dictionary(Of StageDetailsColumn, Double)()

        Dim totalGaps As Double = ColumnGap * Math.Max(0, visibleColumns.Count - 1)
        Dim fixedTotal As Double = 0
        For Each column As StageDetailsColumn In visibleColumns
            fixedTotal += FixedWidthOf(column)
        Next

        ' Space the fixed columns may use: all of it, unless Name would drop below its minimum.
        Dim shrinkFactor As Double = 1
        If Not Double.IsInfinity(totalWidth) AndAlso fixedTotal > 0 Then
            Dim roomForFixedColumns As Double = Math.Max(0, totalWidth - totalGaps - NameMinimumWidth)
            If roomForFixedColumns < fixedTotal Then
                shrinkFactor = roomForFixedColumns / fixedTotal
            End If
        End If

        Dim usedByFixedColumns As Double = 0
        For Each column As StageDetailsColumn In visibleColumns
            If column <> StageDetailsColumn.Name Then
                widths(column) = FixedWidthOf(column) * shrinkFactor
                usedByFixedColumns += widths(column)
            End If
        Next

        If Double.IsInfinity(totalWidth) Then
            widths(StageDetailsColumn.Name) = NameMinimumWidth
        Else
            widths(StageDetailsColumn.Name) = Math.Max(0, totalWidth - totalGaps - usedByFixedColumns)
        End If
        Return widths
    End Function

    Protected Overrides Function MeasureOverride(availableSize As Size) As Size
        Dim layout As DetailsColumnLayout = GetColumnLayout(Me)
        Dim widths As Dictionary(Of StageDetailsColumn, Double) = ColumnWidthsFor(availableSize.Width)
        Dim tallestChild As Double = 0
        Dim usedWidth As Double = 0

        For Each child As UIElement In InternalChildren
            If child Is Nothing Then Continue For
            Dim column As StageDetailsColumn = GetColumn(child)
            If layout.IsShown(column) Then
                child.Measure(New Size(widths(column), availableSize.Height))
                tallestChild = Math.Max(tallestChild, child.DesiredSize.Height)
            Else
                ' A hidden column is measured with no room, so it asks for nothing.
                child.Measure(New Size(0, 0))
            End If
        Next

        For Each column As StageDetailsColumn In layout.VisibleColumns
            usedWidth += widths(column)
        Next
        usedWidth += ColumnGap * Math.Max(0, layout.VisibleColumns.Count - 1)

        ' With a limited width the panel fills it; otherwise it asks for what the columns need.
        Dim desiredWidth As Double = If(Double.IsInfinity(availableSize.Width), usedWidth, availableSize.Width)
        Return New Size(desiredWidth, tallestChild)
    End Function

    Protected Overrides Function ArrangeOverride(finalSize As Size) As Size
        Dim layout As DetailsColumnLayout = GetColumnLayout(Me)
        Dim widths As Dictionary(Of StageDetailsColumn, Double) = ColumnWidthsFor(finalSize.Width)

        ' Where each visible column starts, left to right.
        _arrangedColumns.Clear()
        Dim nextLeft As Double = 0
        For Each column As StageDetailsColumn In layout.VisibleColumns
            _arrangedColumns(column) = New Rect(nextLeft, 0, widths(column), finalSize.Height)
            nextLeft += widths(column) + ColumnGap
        Next

        For Each child As UIElement In InternalChildren
            If child Is Nothing Then Continue For
            Dim cell As Rect = Nothing
            If _arrangedColumns.TryGetValue(GetColumn(child), cell) Then
                child.Arrange(cell)
            Else
                child.Arrange(New Rect(HiddenColumnOffset, 0, 0, finalSize.Height)) ' clipped away
            End If
        Next
        Return finalSize
    End Function

#End Region

#Region "Dragging a heading (used by StageWindow.DetailsColumns.vb)"

    ''' <summary>Where <paramref name="column"/> was placed in the last layout, or Rect.Empty if it isn't shown.</summary>
    Public Function ArrangedRectOf(column As StageDetailsColumn) As Rect
        Dim cell As Rect = Nothing
        If _arrangedColumns.TryGetValue(column, cell) Then Return cell
        Return Rect.Empty
    End Function

    ''' <summary>
    ''' The position <paramref name="draggedColumn"/> should move to if dropped with its centre
    ''' at <paramref name="draggedCentreX"/>: the number of OTHER visible columns whose centre
    ''' is to the left of it. That index is into the list of visible columns without the dragged one.
    ''' </summary>
    Public Function DropIndexFor(draggedColumn As StageDetailsColumn, draggedCentreX As Double) As Integer
        Dim columnsToTheLeft As Integer = 0
        For Each column As StageDetailsColumn In GetColumnLayout(Me).VisibleColumns
            If column = draggedColumn Then Continue For
            Dim cell As Rect = ArrangedRectOf(column)
            If Not cell.IsEmpty AndAlso cell.Left + cell.Width / 2 < draggedCentreX Then
                columnsToTheLeft += 1
            End If
        Next
        Return columnsToTheLeft
    End Function

#End Region

End Class
