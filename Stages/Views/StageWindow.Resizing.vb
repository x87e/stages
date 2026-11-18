' ============================================================================
'  StageWindow.Resizing.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  Resizing a Stage by dragging its edges and corners.
'
'    * Near the left, right or bottom edge, the pointer becomes the matching
'      resize arrow; in a corner (all four, the top ones included), the
'      diagonal arrow. Press and drag to resize. The top edge on its own is left
'      for the title bar, which moves the Stage.
'    * Not while the Stage is locked, minified (rolled shut) or rolling.
'    * While dragging, the new size snaps into line with other Stages and keeps
'      the gap, exactly as moving does (Settings > Desktop > Arranging Stages);
'      hold Alt to size freely.
'
'  Why Stages does this itself (v0.8.1): before, it told Windows "this is a
'  resize edge" (WM_NCHITTEST) and let Windows do the resizing. But Windows
'  only honours that for windows with a resizable frame (WS_THICKFRAME), and a
'  Stage deliberately has none (ResizeMode="NoResize" in StageWindow.xaml), so
'  that it has no frame to draw and Aero Snap can't maximise it or stretch it
'  to half the screen. Windows therefore ignored the edges: no arrow, no
'  resize. Now the edges are ordinary window content, and this file does the
'  pointer, the dragging and the sizing in physical screen pixels.
' ============================================================================
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services

Partial Public Class StageWindow

#Region "Resizing constants"
    ''' <summary>
    ''' How far along an edge a corner reaches, in device-independent pixels. Bigger than the
    ''' edge's own thickness (ResizeBorderThickness), so corners are easy to grab.
    ''' </summary>
    Private Const ResizeCornerReach As Double = 16
    ''' <summary>SetWindowPos flags while resizing: keep the z-order (the Stage stays at the bottom), don't activate.</summary>
    Private Const ResizeWindowPosFlags As UInteger = NativeMethods.SWP_NOZORDER Or NativeMethods.SWP_NOACTIVATE
#End Region

#Region "Resizing fields"
    ''' <summary>True from pressing on an edge until the button is released.</summary>
    Private _isResizingByMouse As Boolean
    ''' <summary>Which edges the drag moves (two for a corner).</summary>
    Private _resizeEdges As StageSnapper.MovingEdges
    ''' <summary>The Stage's rectangle and the pointer's position when the drag began, in physical screen pixels.</summary>
    Private _resizeStartRect As NativeMethods.NativeRect
    Private _resizeStartPointer As NativeMethods.NativePoint
    ''' <summary>The smallest the Stage may become, in physical pixels (from MinWidth / MinHeight).</summary>
    Private _resizeMinimumWidth As Integer
    Private _resizeMinimumHeight As Integer
#End Region

#Region "Resizing"

    ''' <summary>
    ''' Which edges a point (in the Stage's own coordinates) is on: none, one edge, or a corner.
    ''' None while the Stage can't be resized. <paramref name="elementUnderPointer"/> is what the
    ''' mouse is over: the slim scrollbar runs down the right edge, so there the right edge is
    ''' left to the scrollbar (the corners still resize).
    ''' </summary>
    Private Function ResizeEdgesAt(pointInStage As Point, elementUnderPointer As DependencyObject) As StageSnapper.MovingEdges
        ' Not while rolling open or shut either: a resize would fight the animation.
        If _settings.Locked OrElse _isMinified OrElse _isAnimatingMinify OrElse _isEditingTitle Then
            Return StageSnapper.MovingEdges.None
        End If

        Dim onLeftEdge As Boolean = pointInStage.X < ResizeBorderThickness
        Dim onRightEdge As Boolean = pointInStage.X > ActualWidth - ResizeBorderThickness
        Dim onTopEdge As Boolean = pointInStage.Y < ResizeBorderThickness
        Dim onBottomEdge As Boolean = pointInStage.Y > ActualHeight - ResizeBorderThickness
        ' Within a corner's reach along an edge (wider than the edge itself).
        Dim nearLeft As Boolean = pointInStage.X < ResizeCornerReach
        Dim nearRight As Boolean = pointInStage.X > ActualWidth - ResizeCornerReach
        Dim nearTop As Boolean = pointInStage.Y < ResizeCornerReach
        Dim nearBottom As Boolean = pointInStage.Y > ActualHeight - ResizeCornerReach

        ' Corners first: on either edge, within the corner's reach of the other.
        If (onTopEdge AndAlso nearLeft) OrElse (onLeftEdge AndAlso nearTop) Then
            Return StageSnapper.MovingEdges.Top Or StageSnapper.MovingEdges.Left
        End If
        If (onTopEdge AndAlso nearRight) OrElse (onRightEdge AndAlso nearTop) Then
            Return StageSnapper.MovingEdges.Top Or StageSnapper.MovingEdges.Right
        End If
        If (onBottomEdge AndAlso nearLeft) OrElse (onLeftEdge AndAlso nearBottom) Then
            Return StageSnapper.MovingEdges.Bottom Or StageSnapper.MovingEdges.Left
        End If
        If (onBottomEdge AndAlso nearRight) OrElse (onRightEdge AndAlso nearBottom) Then
            Return StageSnapper.MovingEdges.Bottom Or StageSnapper.MovingEdges.Right
        End If
        ' Then single edges. The edge along the title bar alone belongs to it (moving the
        ' Stage): normally the top; for a Stage open upwards (title bar at the bottom,
        ' StageWindow.OpenDirection.vb) the bottom, so there the top edge resizes instead.
        If onBottomEdge AndAlso Not _isOpenedUpward Then Return StageSnapper.MovingEdges.Bottom
        If onTopEdge AndAlso _isOpenedUpward Then Return StageSnapper.MovingEdges.Top
        If onLeftEdge Then Return StageSnapper.MovingEdges.Left
        If onRightEdge AndAlso Not IsInsideScrollBar(elementUnderPointer) Then Return StageSnapper.MovingEdges.Right
        Return StageSnapper.MovingEdges.None
    End Function

    ''' <summary>The resize arrow for a set of edges (Nothing = the normal pointer).</summary>
    Private Shared Function ResizeCursorFor(edges As StageSnapper.MovingEdges) As Cursor
        Dim isTopLeftOrBottomRight As Boolean =
            edges = (StageSnapper.MovingEdges.Top Or StageSnapper.MovingEdges.Left) OrElse
            edges = (StageSnapper.MovingEdges.Bottom Or StageSnapper.MovingEdges.Right)
        Dim isTopRightOrBottomLeft As Boolean =
            edges = (StageSnapper.MovingEdges.Top Or StageSnapper.MovingEdges.Right) OrElse
            edges = (StageSnapper.MovingEdges.Bottom Or StageSnapper.MovingEdges.Left)

        If isTopLeftOrBottomRight Then Return Cursors.SizeNWSE ' the "\" diagonal arrow
        If isTopRightOrBottomLeft Then Return Cursors.SizeNESW ' the "/" diagonal arrow
        If edges = StageSnapper.MovingEdges.Left OrElse edges = StageSnapper.MovingEdges.Right Then Return Cursors.SizeWE
        If edges = StageSnapper.MovingEdges.Bottom Then Return Cursors.SizeNS
        Return Nothing
    End Function

    ''' <summary>
    ''' Mouse moving anywhere over the Stage (seen before any part of it): shows the resize
    ''' arrow on an edge, or resizes while a drag is under way.
    ''' </summary>
    Protected Overrides Sub OnPreviewMouseMove(e As MouseEventArgs)
        MyBase.OnPreviewMouseMove(e)

        If _isResizingByMouse Then
            ResizeToPointer()
            e.Handled = True ' items mustn't react (no rubber band, no item drag)
            Return
        End If

        ' Show the arrow on an edge; back to normal anywhere else. While a button is held
        ' (dragging items, drawing a selection) the pointer is left alone.
        If e.LeftButton = MouseButtonState.Released Then
            Dim wantedCursor As Cursor = ResizeCursorFor(ResizeEdgesAt(e.GetPosition(Me), TryCast(e.OriginalSource, DependencyObject)))
            If Cursor IsNot wantedCursor Then Cursor = wantedCursor
        End If
    End Sub

    ''' <summary>A press on an edge or corner starts resizing (before the title bar or items see it).</summary>
    Protected Overrides Sub OnPreviewMouseLeftButtonDown(e As MouseButtonEventArgs)
        MyBase.OnPreviewMouseLeftButtonDown(e)
        If e.Handled Then Return

        Dim edges As StageSnapper.MovingEdges = ResizeEdgesAt(e.GetPosition(Me), TryCast(e.OriginalSource, DependencyObject))
        If edges = StageSnapper.MovingEdges.None Then Return

        If Not NativeMethods.GetWindowRect(_windowHandle, _resizeStartRect) Then Return
        If Not NativeMethods.GetCursorPos(_resizeStartPointer) Then Return

        _resizeEdges = edges
        _resizeMinimumWidth = ToPhysicalPixels(CInt(Math.Ceiling(MinWidth)))
        _resizeMinimumHeight = ToPhysicalPixels(CInt(Math.Ceiling(MinHeight)))
        _isResizingByMouse = True
        _isPopupOpen = True ' a minify-enabled Stage must stay open while it's resized
        Cursor = ResizeCursorFor(edges)

        ' Lining up with other Stages, as when moving - StageWindow.Snapping.vb.
        BeginSnapping()
        ' Keep receiving the mouse even when it goes outside the Stage (growing it).
        CaptureMouse()
        e.Handled = True
    End Sub

    ''' <summary>Releasing the button ends the resize; the new size is saved by Window_SizeChanged.</summary>
    Protected Overrides Sub OnPreviewMouseLeftButtonUp(e As MouseButtonEventArgs)
        MyBase.OnPreviewMouseLeftButtonUp(e)
        If Not _isResizingByMouse Then Return
        e.Handled = True
        FinishMouseResize()
    End Sub

    ''' <summary>Something else took the mouse mid-resize (a window popped up): stop where it is.</summary>
    Protected Overrides Sub OnLostMouseCapture(e As MouseEventArgs)
        MyBase.OnLostMouseCapture(e)
        If _isResizingByMouse Then FinishMouseResize()
    End Sub

    ''' <summary>Leaving the Stage puts the normal pointer back (unless a resize is holding the mouse).</summary>
    Protected Overrides Sub OnMouseLeave(e As MouseEventArgs)
        MyBase.OnMouseLeave(e)
        If Not _isResizingByMouse Then Me.ClearValue(CursorProperty)
    End Sub

    ''' <summary>
    ''' Works out the new rectangle from how far the pointer has moved since the press, keeps
    ''' it at least the minimum size, lines it up with other Stages, and applies it.
    ''' </summary>
    Private Sub ResizeToPointer()
        Dim pointerNow As NativeMethods.NativePoint
        If Not NativeMethods.GetCursorPos(pointerNow) Then Return
        Dim movedSideways As Integer = pointerNow.X - _resizeStartPointer.X
        Dim movedUpOrDown As Integer = pointerNow.Y - _resizeStartPointer.Y

        ' Only the dragged edges move; each stops where the Stage would get too small.
        Dim proposed As NativeMethods.NativeRect = _resizeStartRect
        If _resizeEdges.HasFlag(StageSnapper.MovingEdges.Left) Then
            proposed.Left = Math.Min(_resizeStartRect.Left + movedSideways, _resizeStartRect.Right - _resizeMinimumWidth)
        End If
        If _resizeEdges.HasFlag(StageSnapper.MovingEdges.Right) Then
            proposed.Right = Math.Max(_resizeStartRect.Right + movedSideways, _resizeStartRect.Left + _resizeMinimumWidth)
        End If
        If _resizeEdges.HasFlag(StageSnapper.MovingEdges.Top) Then
            proposed.Top = Math.Min(_resizeStartRect.Top + movedUpOrDown, _resizeStartRect.Bottom - _resizeMinimumHeight)
        End If
        If _resizeEdges.HasFlag(StageSnapper.MovingEdges.Bottom) Then
            proposed.Bottom = Math.Max(_resizeStartRect.Bottom + movedUpOrDown, _resizeStartRect.Top + _resizeMinimumHeight)
        End If

        ' Snapping and the gap (Alt held = none) - the same rules as moving.
        Dim finalRect As NativeMethods.NativeRect = SnapRectangle(proposed, Function(rect) _snapper.AdjustResize(rect, _resizeEdges))

        NativeMethods.SetWindowPos(_windowHandle, IntPtr.Zero, finalRect.Left, finalRect.Top,
                                   finalRect.Right - finalRect.Left, finalRect.Bottom - finalRect.Top,
                                   ResizeWindowPosFlags)
    End Sub

    ''' <summary>Ends a resize: lets go of the mouse, removes the guide lines, puts the pointer back.</summary>
    Private Sub FinishMouseResize()
        _isResizingByMouse = False
        If IsMouseCaptured Then ReleaseMouseCapture()
        EndSnapping()
        _isPopupOpen = False
        Me.ClearValue(CursorProperty)
        CheckWhetherMouseHasLeft()
    End Sub

#End Region

End Class
