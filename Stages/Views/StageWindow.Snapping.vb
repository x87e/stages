' ============================================================================
'  StageWindow.Snapping.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  Lining Stages up while one is moved or resized (Settings > Desktop >
'  Arranging Stages):
'
'    * edges snap into line: by default only when the Stage is brought up
'      beside another one (sitting the gap away, flush with its top or
'      bottom); in Full mode also with any Stage's edges and the screen's;
'    * Stages are kept from overlapping (at least the gap apart): a moved Stage
'      let go of over another moves to the nearest free spot;
'    * coloured guide lines show which Stages this one is in line with;
'    * holding Alt while moving or resizing switches all of that off.
'
'  Moving: Windows runs a "move loop" while a Stage is dragged by its title bar
'  (DragMove). During it, Windows sends WM_MOVING with the rectangle it is about
'  to use, and lets us change that rectangle - which is all snapping is.
'  Resizing: Stages does that itself (StageWindow.Resizing.vb) and passes each
'  new rectangle through SnapRectangle the same way. (WM_SIZING is still
'  handled, in case Windows ever resizes a Stage through its own loop.)
'  The arithmetic lives in Services\StageSnapper.vb.
'
'  A minified Stage counts at its full open height, so a Stage placed below
'  one won't be covered when that one rolls open.
' ============================================================================
Imports System.Runtime.InteropServices
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports WinForms = System.Windows.Forms

Partial Public Class StageWindow

#Region "Snapping fields"
    ''' <summary>Does the snapping for the move or resize in progress; Nothing when none is (or it's all switched off).</summary>
    Private _snapper As StageSnapper
    ''' <summary>The guide colour and thickness for the move in progress (read from Settings when it starts).</summary>
    Private _snapGuideColor As String
    Private _snapGuideThickness As Integer
    Private _isShowingSnapGuides As Boolean
    ''' <summary>True once the Stage has actually been dragged in this move loop (not just clicked).</summary>
    Private _wasMovedInThisLoop As Boolean
    ''' <summary>SetWindowPos flags for moving to a free spot: keep size and z-order, don't activate.</summary>
    Private Const FreeSpotWindowPosFlags As UInteger = NativeMethods.SWP_NOSIZE Or NativeMethods.SWP_NOZORDER Or NativeMethods.SWP_NOACTIVATE
#End Region

#Region "Snapping"

    ''' <summary>
    ''' This Stage's rectangle for other Stages to line up with, in physical screen pixels, or
    ''' Nothing if it isn't on screen (hidden, or not created yet). A minified (or rolled-up)
    ''' Stage reports its full open height, so nothing is put where it will open - unless
    ''' <paramref name="useOpenHeight"/> is False ("Only snap side by side"): then just its
    ''' title bar counts, and it opens over whatever is below it.
    ''' </summary>
    Friend Function GetSnapBounds(useOpenHeight As Boolean) As NativeMethods.NativeRect?
        If _windowHandle = IntPtr.Zero OrElse Not IsVisible Then Return Nothing
        Dim bounds As NativeMethods.NativeRect
        If Not NativeMethods.GetWindowRect(_windowHandle, bounds) Then Return Nothing

        If useOpenHeight AndAlso (_isMinified OrElse _isAnimatingMinify) Then
            ' Where it will be when open: below its title bar, or above it for a Stage that
            ' opens upwards (StageWindow.OpenDirection.vb).
            Dim openHeight As Integer = ToPhysicalPixels(_settings.Height)
            If OpensUpwardFromHere() Then
                bounds.Top = bounds.Bottom - openHeight
            Else
                bounds.Bottom = bounds.Top + openHeight
            End If
        End If
        Return bounds
    End Function

    ''' <summary>
    ''' Handles the move/size loop messages (called from WindowMessageHook). Returns True if
    ''' the message was one of them and has been dealt with.
    ''' </summary>
    Private Function HandleSnappingMessage(msg As Integer, wParam As IntPtr, lParam As IntPtr) As Boolean
        Select Case msg
            Case NativeMethods.WM_ENTERSIZEMOVE
                BeginSnapping()
                Return False ' Windows and WPF still need to see this one

            Case NativeMethods.WM_EXITSIZEMOVE
                ' Let go: if it was dropped over (or too close to) another Stage, move it clear.
                If _wasMovedInThisLoop Then MoveToFreeSpotIfOverlapping()
                EndSnapping()
                Return False

            Case NativeMethods.WM_MOVING
                _wasMovedInThisLoop = True
                If _snapper Is Nothing Then Return False
                AdjustProposedRect(lParam, Function(proposed) _snapper.AdjustMove(proposed))
                Return True

            Case NativeMethods.WM_SIZING
                If _snapper Is Nothing Then Return False
                Dim edges As StageSnapper.MovingEdges = EdgesFromSizingCode(wParam.ToInt32())
                AdjustProposedRect(lParam, Function(proposed) _snapper.AdjustResize(proposed, edges))
                Return True
        End Select
        Return False
    End Function

    ''' <summary>
    ''' A move or resize has started: note where every other Stage is and read the settings.
    ''' If snapping, overlap prevention and guides are all switched off, nothing is set up.
    ''' </summary>
    Private Sub BeginSnapping()
        _snapper = Nothing
        _wasMovedInThisLoop = False
        Dim currentSettings As AppSettings = AppSettingsStore.Current
        Dim isSnapping As Boolean = currentSettings.StageSnapping <> StageSnapMode.Off
        Dim anythingToDo As Boolean = isSnapping OrElse currentSettings.PreventStageOverlap OrElse currentSettings.ShowSnapGuides
        If Not anythingToDo Then Return

        Dim startRect As NativeMethods.NativeRect
        If Not NativeMethods.GetWindowRect(_windowHandle, startRect) Then Return

        ' "Only snap side by side": a minified Stage counts only as its title bar (see GetSnapBounds).
        Dim useOpenHeight As Boolean = Not currentSettings.SnapSideBySideOnly
        Dim otherRects As New List(Of NativeMethods.NativeRect)()
        For Each otherStage As StageWindow In StageManager.Instance.OtherStageWindows(Me)
            Dim otherBounds As NativeMethods.NativeRect? = otherStage.GetSnapBounds(useOpenHeight)
            If otherBounds.HasValue Then otherRects.Add(otherBounds.Value)
        Next

        ' Screens' work areas (the desktop without the taskbar). This app is per-monitor DPI
        ' aware, so Windows Forms reports them in physical pixels, matching the rectangles above.
        Dim workAreas As New List(Of NativeMethods.NativeRect)()
        For Each attachedScreen As WinForms.Screen In WinForms.Screen.AllScreens
            Dim area As System.Drawing.Rectangle = attachedScreen.WorkingArea
            workAreas.Add(New NativeMethods.NativeRect With {.Left = area.Left, .Top = area.Top, .Right = area.Right, .Bottom = area.Bottom})
        Next

        _snapper = New StageSnapper(startRect, otherRects, workAreas,
                                    snapDistancePixels:=ToPhysicalPixels(StageDefaults.SnapDistancePixels),
                                    gapPixels:=ToPhysicalPixels(currentSettings.StageGapPixels),
                                    snapMode:=currentSettings.StageSnapping,
                                    preventOverlapOn:=currentSettings.PreventStageOverlap,
                                    minimumWidthPixels:=ToPhysicalPixels(CInt(Math.Ceiling(MinWidth))),
                                    minimumHeightPixels:=ToPhysicalPixels(CInt(Math.Ceiling(MinHeight))),
                                    sideBySideOnly:=currentSettings.SnapSideBySideOnly)
        _isShowingSnapGuides = currentSettings.ShowSnapGuides
        _snapGuideColor = currentSettings.SnapGuideColor
        _snapGuideThickness = Math.Max(1, ToPhysicalPixels(StageDefaults.SnapGuideThicknessPixels))
    End Sub

    ''' <summary>
    ''' After a move: if the Stage was let go of on top of (or closer than the gap to) another
    ''' Stage, moves it to the nearest free spot. Not while Alt is held (placing freely).
    ''' </summary>
    Private Sub MoveToFreeSpotIfOverlapping()
        If _snapper Is Nothing OrElse IsAltKeyHeld() Then Return
        Dim droppedRect As NativeMethods.NativeRect
        If Not NativeMethods.GetWindowRect(_windowHandle, droppedRect) Then Return

        Dim freeRect As NativeMethods.NativeRect = _snapper.FindFreeSpotAfterMove(droppedRect)
        If freeRect.Left = droppedRect.Left AndAlso freeRect.Top = droppedRect.Top Then Return
        ' Window_LocationChanged then saves the new place, as after any move.
        NativeMethods.SetWindowPos(_windowHandle, IntPtr.Zero, freeRect.Left, freeRect.Top, 0, 0, FreeSpotWindowPosFlags)
    End Sub

    ''' <summary>The title bar's "hold Alt" tip makes no sense on a locked Stage (it can't be moved) or while renaming.</summary>
    Private Sub TitleBar_ToolTipOpening(sender As Object, e As ToolTipEventArgs)
        If _settings.Locked OrElse _isEditingTitle Then e.Handled = True ' Handled = don't show it
    End Sub

    ''' <summary>True while either Alt key is held down (= place the Stage freely).</summary>
    Private Shared Function IsAltKeyHeld() As Boolean
        Return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) And NativeMethods.KEY_IS_DOWN_FLAG) <> 0
    End Function

    ''' <summary>The move or resize has ended (or was cancelled with Esc): remove the guides.</summary>
    Private Sub EndSnapping()
        If _snapper IsNot Nothing Then SnapGuideOverlay.Instance.HideAll()
        _snapper = Nothing
    End Sub

    ''' <summary>
    ''' Reads the rectangle Windows proposes (lParam points at it), lets <paramref name="adjust"/>
    ''' change it - unless Alt is held - writes the result back, and updates the guide lines.
    ''' </summary>
    Private Sub AdjustProposedRect(lParam As IntPtr, adjust As Func(Of NativeMethods.NativeRect, NativeMethods.NativeRect))
        Dim proposed As NativeMethods.NativeRect = Marshal.PtrToStructure(Of NativeMethods.NativeRect)(lParam)
        Dim finalRect As NativeMethods.NativeRect = SnapRectangle(proposed, adjust)
        Marshal.StructureToPtr(finalRect, lParam, False)
    End Sub

    ''' <summary>
    ''' The rectangle to use instead of <paramref name="proposed"/>: <paramref name="adjust"/>'s
    ''' answer (snapped, kept apart from other Stages), or <paramref name="proposed"/> itself
    ''' while Alt is held or when nothing is switched on. Also updates the guide lines.
    ''' Shared by moving (WM_MOVING) and resizing (StageWindow.Resizing.vb).
    ''' </summary>
    Private Function SnapRectangle(proposed As NativeMethods.NativeRect,
                                   adjust As Func(Of NativeMethods.NativeRect, NativeMethods.NativeRect)) As NativeMethods.NativeRect
        If _snapper Is Nothing Then Return proposed ' every arranging option is switched off

        Dim finalRect As NativeMethods.NativeRect = If(IsAltKeyHeld(), proposed, adjust(proposed))
        If _isShowingSnapGuides Then
            SnapGuideOverlay.Instance.ShowGuides(_snapper.FindGuides(finalRect), _snapGuideColor, _snapGuideThickness)
        End If
        Return finalRect
    End Function

    ''' <summary>WM_SIZING's edge code -> which edges are being dragged.</summary>
    Private Shared Function EdgesFromSizingCode(sizingCode As Integer) As StageSnapper.MovingEdges
        Select Case sizingCode
            Case NativeMethods.WMSZ_LEFT
                Return StageSnapper.MovingEdges.Left
            Case NativeMethods.WMSZ_RIGHT
                Return StageSnapper.MovingEdges.Right
            Case NativeMethods.WMSZ_TOP
                Return StageSnapper.MovingEdges.Top
            Case NativeMethods.WMSZ_BOTTOM
                Return StageSnapper.MovingEdges.Bottom
            Case NativeMethods.WMSZ_TOPLEFT
                Return StageSnapper.MovingEdges.Top Or StageSnapper.MovingEdges.Left
            Case NativeMethods.WMSZ_TOPRIGHT
                Return StageSnapper.MovingEdges.Top Or StageSnapper.MovingEdges.Right
            Case NativeMethods.WMSZ_BOTTOMLEFT
                Return StageSnapper.MovingEdges.Bottom Or StageSnapper.MovingEdges.Left
            Case NativeMethods.WMSZ_BOTTOMRIGHT
                Return StageSnapper.MovingEdges.Bottom Or StageSnapper.MovingEdges.Right
            Case Else
                Return StageSnapper.MovingEdges.None
        End Select
    End Function

    ''' <summary>A distance in device-independent pixels, in this Stage's screen's physical pixels.</summary>
    Private Function ToPhysicalPixels(deviceIndependentPixels As Integer) As Integer
        Dim screenScale As Double = VisualTreeHelper.GetDpi(Me).DpiScaleX
        Return CInt(Math.Round(deviceIndependentPixels * screenScale))
    End Function

#End Region

End Class
