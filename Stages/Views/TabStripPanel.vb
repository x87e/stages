' ============================================================================
'  TabStripPanel.vb
'  Lays out the tabs in a Stage's title bar (StageWindow.xaml, TabStrip).
'
'  Tabs sit side by side at their natural width. When they don't all fit,
'  they shrink, fairly: tabs already narrower than an equal share keep their
'  width, and the wider ones share what's left equally. Their titles end in
'  "..." (TextTrimming in the tab template), so every tab stays reachable,
'  however narrow the Stage. A plain StackPanel would just cut the last tabs
'  off.
' ============================================================================
Public Class TabStripPanel
    Inherits Panel

    ''' <summary>The width each tab gets, worked out in MeasureOverride, used by ArrangeOverride.</summary>
    Private ReadOnly _plannedWidths As New List(Of Double)()

    Protected Overrides Function MeasureOverride(availableSize As Size) As Size
        ' 1. Every tab's natural width.
        Dim naturalWidths As New List(Of Double)()
        Dim tallest As Double = 0
        For Each child As UIElement In InternalChildren
            child.Measure(New Size(Double.PositiveInfinity, availableSize.Height))
            naturalWidths.Add(child.DesiredSize.Width)
            tallest = Math.Max(tallest, child.DesiredSize.Height)
        Next

        ' 2. Shrink them if they don't fit.
        _plannedWidths.Clear()
        _plannedWidths.AddRange(FairWidths(naturalWidths, availableSize.Width))

        ' 3. Measure each again at the width it will really get (so its title trims to fit).
        Dim totalWidth As Double = 0
        For childIndex As Integer = 0 To InternalChildren.Count - 1
            InternalChildren(childIndex).Measure(New Size(_plannedWidths(childIndex), availableSize.Height))
            totalWidth += _plannedWidths(childIndex)
        Next
        Return New Size(totalWidth, tallest)
    End Function

    Protected Overrides Function ArrangeOverride(finalSize As Size) As Size
        Dim left As Double = 0
        For childIndex As Integer = 0 To InternalChildren.Count - 1
            ' A tab added since the last measure gets its natural width until the next one.
            Dim tabWidth As Double = If(childIndex < _plannedWidths.Count,
                                        _plannedWidths(childIndex),
                                        InternalChildren(childIndex).DesiredSize.Width)
            InternalChildren(childIndex).Arrange(New Rect(left, 0, tabWidth, finalSize.Height))
            left += tabWidth
        Next
        Return finalSize
    End Function

    ''' <summary>
    ''' Widths for tabs whose natural widths are <paramref name="naturalWidths"/>, fitting in
    ''' <paramref name="availableWidth"/>. If they fit already, they're unchanged. Otherwise
    ''' the narrowest tabs keep their width as long as it's below an equal share of what's
    ''' left, and all the rest get that equal share.
    ''' </summary>
    Public Shared Function FairWidths(naturalWidths As IList(Of Double), availableWidth As Double) As List(Of Double)
        Dim result As New List(Of Double)(naturalWidths)
        Dim naturalTotal As Double = naturalWidths.Sum()
        If Double.IsInfinity(availableWidth) OrElse naturalTotal <= availableWidth OrElse naturalWidths.Count = 0 Then
            Return result
        End If

        ' Go through the tabs from narrowest to widest.
        Dim tabIndexes As IEnumerable(Of Integer) = Enumerable.Range(0, naturalWidths.Count)
        Dim narrowestFirst As List(Of Integer) = tabIndexes.OrderBy(Function(tabIndex) naturalWidths(tabIndex)).ToList()
        Dim widthLeft As Double = Math.Max(0, availableWidth)
        Dim tabsLeft As Integer = naturalWidths.Count
        For Each tabIndex As Integer In narrowestFirst
            Dim equalShare As Double = widthLeft / tabsLeft
            If naturalWidths(tabIndex) <= equalShare Then
                ' Narrow enough already: keeps its own width, leaving more for the others.
                widthLeft -= naturalWidths(tabIndex)
            Else
                result(tabIndex) = equalShare
                widthLeft -= equalShare
            End If
            tabsLeft -= 1
        Next
        Return result
    End Function

End Class
