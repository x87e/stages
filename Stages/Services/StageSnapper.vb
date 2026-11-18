' ============================================================================
'  StageSnapper.vb
'  The arithmetic behind lining Stages up (Settings > Desktop > Arranging
'  Stages). Pure calculation on rectangles - no windows are touched here - so
'  it is easy to follow and to check by hand. StageWindow.Snapping.vb feeds it
'  the rectangle Windows proposes while a Stage is dragged (and
'  StageWindow.Resizing.vb the one it works out while a Stage is resized), and
'  uses the rectangle this returns.
'
'  All rectangles are in physical screen pixels (what Windows' move loop
'  uses). Distances given in device-independent pixels are converted by the
'  caller for the Stage's screen scaling.
'
'  SNAPPING (while dragging or resizing) - an edge within SnapDistance of a
'  line-up position jumps to it. Which positions count depends on the mode:
'    * Beside other Stages (default): only a Stage the moving one is being
'      brought up next to - level with it, its facing edge about the gap away.
'      Then: sit exactly the gap away from it, and line up flush with ITS
'      top/bottom (side by side) or left/right (stacked). Nothing else pulls,
'      so a Stage moved away from its neighbours moves completely freely.
'    * Full (v0.8.0's behaviour): also the same edge of any Stage anywhere on
'      screen, and the screen's work-area edges.
'    * Off: none.
'  "Only snap side by side" (v0.8.5, either mode): Stages above or below
'  this one are ignored altogether - it lines up only with Stages in the same
'  row (level with it), so a row can be built under, say, a row of minified
'  Stages without being pulled up against them. Screen edges still count in
'  Full mode. Overlaps are still sorted out on letting go.
'
'  NO OVERLAP - kept apart by at least the gap:
'    * Moving: NOT while dragging (v0.8.0 pushed the Stage around mid-drag,
'      which felt like fighting it). When the Stage is let go of on top of or
'      too close to another, FindFreeSpotAfterMove gives the nearest place
'      that is clear of every Stage, and the Stage slides there.
'    * Resizing: the dragged edge stops the gap's width short of the next
'      Stage, as you'd expect an edge to.
'
'  GUIDES - FindGuides lists a line for each edge that is exactly in line with
'  the same edge of other Stages (in Beside mode: only the Stages it sits
'  beside) - the coloured guide lines.
'
'  One StageSnapper is made when a move or resize starts and dropped when it
'  ends, because it remembers the other Stages' rectangles (they don't move
'  meanwhile).
' ============================================================================
Imports Stages.Model
Imports Stages.Native

Namespace Services

    Friend NotInheritable Class StageSnapper

        ''' <summary>Which edges of the Stage the user is dragging while resizing (several for a corner).</summary>
        <Flags>
        Public Enum MovingEdges
            None = 0
            Left = 1
            Top = 2
            Right = 4
            Bottom = 8
        End Enum

        ''' <summary>One guide line to draw: a horizontal or vertical stretch, in screen pixels.</summary>
        Public NotInheritable Class GuideLine
            ''' <summary>True = runs left to right at Y = Position; False = runs top to bottom at X = Position.</summary>
            Public ReadOnly Property IsHorizontal As Boolean
            ''' <summary>The Y (horizontal line) or X (vertical line) it sits on.</summary>
            Public ReadOnly Property Position As Integer
            ''' <summary>Where it starts and ends along its length (X for a horizontal line, Y for a vertical one).</summary>
            Public ReadOnly Property FromCoordinate As Integer
            Public ReadOnly Property ToCoordinate As Integer

            Public Sub New(horizontal As Boolean, linePosition As Integer, startCoordinate As Integer, endCoordinate As Integer)
                IsHorizontal = horizontal
                Position = linePosition
                FromCoordinate = startCoordinate
                ToCoordinate = endCoordinate
            End Sub
        End Class

        ''' <summary>Two edges count as "in line" for a guide if they are at most this far apart (rounding of scaled sizes).</summary>
        Private Const InLineTolerance As Integer = 1
        ''' <summary>
        ''' Finding a free spot: a spot clear of one Stage may land on another, so up to this many
        ''' steps of "move out of the Stage in the way" are tried from where it was let go.
        ''' </summary>
        Private Const FreeSpotSearchSteps As Integer = 3
        ''' <summary>Stop looking after this many candidate spots (plenty for any real desktop).</summary>
        Private Const MaximumFreeSpotCandidates As Integer = 400

        Private ReadOnly _otherStages As List(Of NativeMethods.NativeRect)
        Private ReadOnly _workAreas As List(Of NativeMethods.NativeRect)
        ''' <summary>Other Stages that already overlapped this one when the resize began: ignored while resizing.</summary>
        Private ReadOnly _overlappedAtStart As HashSet(Of Integer)
        Private ReadOnly _snapDistance As Integer
        Private ReadOnly _gap As Integer
        Private ReadOnly _snapMode As StageSnapMode
        ''' <summary>Settings > "Only snap side by side": Stages above or below are ignored for snapping.</summary>
        Private ReadOnly _isSideBySideOnly As Boolean
        Private ReadOnly _isPreventingOverlap As Boolean
        Private ReadOnly _minimumWidth As Integer
        Private ReadOnly _minimumHeight As Integer
        ''' <summary>Resizing: the last rectangle that overlapped nothing; Nothing until there has been one.</summary>
        Private _lastGoodResizeRect As NativeMethods.NativeRect?

        ''' <param name="startRect">The Stage's rectangle as the move or resize begins.</param>
        ''' <param name="otherStageRects">Every other visible Stage.</param>
        ''' <param name="screenWorkAreas">Each screen's area not covered by the taskbar.</param>
        ''' <param name="snapDistancePixels">How close an edge must come to snap.</param>
        ''' <param name="gapPixels">The minimum space kept between two Stages.</param>
        ''' <param name="snapMode">Settings: Off, Beside other Stages, or Full.</param>
        ''' <param name="preventOverlapOn">Settings: keep Stages apart.</param>
        ''' <param name="minimumWidthPixels">The Stage can't be resized narrower than this...</param>
        ''' <param name="minimumHeightPixels">...or shorter than this.</param>
        ''' <param name="sideBySideOnly">Settings: only line up with Stages in the same row.</param>
        Public Sub New(startRect As NativeMethods.NativeRect,
                       otherStageRects As IEnumerable(Of NativeMethods.NativeRect),
                       screenWorkAreas As IEnumerable(Of NativeMethods.NativeRect),
                       snapDistancePixels As Integer, gapPixels As Integer,
                       snapMode As StageSnapMode, preventOverlapOn As Boolean,
                       minimumWidthPixels As Integer, minimumHeightPixels As Integer,
                       sideBySideOnly As Boolean)
            _otherStages = otherStageRects.ToList()
            _isSideBySideOnly = sideBySideOnly
            _workAreas = screenWorkAreas.ToList()
            _snapDistance = snapDistancePixels
            _gap = gapPixels
            _snapMode = snapMode
            _isPreventingOverlap = preventOverlapOn
            _minimumWidth = minimumWidthPixels
            _minimumHeight = minimumHeightPixels

            _overlappedAtStart = New HashSet(Of Integer)()
            For otherIndex As Integer = 0 To _otherStages.Count - 1
                If ComesTooClose(startRect, _otherStages(otherIndex)) Then _overlappedAtStart.Add(otherIndex)
            Next
            If Not OverlapsAnyOther(startRect, ignoreStartOverlaps:=False) Then _lastGoodResizeRect = startRect
        End Sub

#Region "Moving"

        ''' <summary>
        ''' The rectangle to use instead of <paramref name="proposed"/> while the Stage is dragged:
        ''' snapped (as the mode allows) but never pushed about - overlaps are dealt with when the
        ''' Stage is let go of (FindFreeSpotAfterMove).
        ''' </summary>
        Public Function AdjustMove(proposed As NativeMethods.NativeRect) As NativeMethods.NativeRect
            If _snapMode = StageSnapMode.Off Then Return proposed
            Dim horizontalShift As Integer = BestShift(HorizontalTargetsForMove(proposed))
            Dim verticalShift As Integer = BestShift(VerticalTargetsForMove(proposed))
            Return Shifted(proposed, horizontalShift, verticalShift)
        End Function

        ''' <summary>
        ''' For a move: how far left/right the Stage would have to shift for each line-up
        ''' position of its left or right edge.
        ''' </summary>
        Private Function HorizontalTargetsForMove(stage As NativeMethods.NativeRect) As List(Of Integer)
            Dim shifts As New List(Of Integer)()
            For Each other As NativeMethods.NativeRect In _otherStages
                If Not IsSnapPartner(stage, other) Then Continue For ' above or below, with "side by side only"
                ' Side by side (both modes): just to the right of it, or just to the left.
                If RangesAreLevel(stage.Top, stage.Bottom, other.Top, other.Bottom) Then
                    shifts.Add(other.Right + _gap - stage.Left)
                    shifts.Add(other.Left - _gap - stage.Right)
                End If
                ' Lefts / rights in line (a column): in Full mode with any Stage; in Beside mode
                ' only with the Stage this one is stacked directly above or below. Never with
                ' "side by side only", which ignores columns.
                If Not _isSideBySideOnly AndAlso (_snapMode = StageSnapMode.Full OrElse IsStackedWith(stage, other)) Then
                    shifts.Add(other.Left - stage.Left)
                    shifts.Add(other.Right - stage.Right)
                End If
            Next
            If _snapMode = StageSnapMode.Full Then
                For Each workArea As NativeMethods.NativeRect In _workAreas
                    shifts.Add(workArea.Left - stage.Left)
                    shifts.Add(workArea.Right - stage.Right)
                Next
            End If
            Return shifts
        End Function

        ''' <summary>The same for up/down and the top and bottom edges.</summary>
        Private Function VerticalTargetsForMove(stage As NativeMethods.NativeRect) As List(Of Integer)
            Dim shifts As New List(Of Integer)()
            For Each other As NativeMethods.NativeRect In _otherStages
                If Not IsSnapPartner(stage, other) Then Continue For
                ' Stacked (both modes, unless "side by side only"): just below it, or just above.
                If Not _isSideBySideOnly AndAlso RangesAreLevel(stage.Left, stage.Right, other.Left, other.Right) Then
                    shifts.Add(other.Bottom + _gap - stage.Top)
                    shifts.Add(other.Top - _gap - stage.Bottom)
                End If
                ' Tops / bottoms in line - Stages side by side in a row. In Beside mode only with
                ' the Stage this one sits directly beside.
                If _snapMode = StageSnapMode.Full OrElse IsSideBySideWith(stage, other) Then
                    shifts.Add(other.Top - stage.Top)
                    shifts.Add(other.Bottom - stage.Bottom)
                End If
            Next
            If _snapMode = StageSnapMode.Full Then
                For Each workArea As NativeMethods.NativeRect In _workAreas
                    shifts.Add(workArea.Top - stage.Top)
                    shifts.Add(workArea.Bottom - stage.Bottom)
                Next
            End If
            Return shifts
        End Function

        ''' <summary>
        ''' Where a Stage let go of at <paramref name="dropped"/> should end up: right there if it
        ''' is clear of every other Stage (or overlap prevention is off); otherwise the nearest spot
        ''' that is clear, found by moving it out of the Stage in the way (each of the four ways,
        ''' a few steps deep) and keeping the spot closest to where it was let go. A spot is only
        ''' used if its centre is on a screen. If none is found, it stays where it was let go.
        ''' </summary>
        Public Function FindFreeSpotAfterMove(dropped As NativeMethods.NativeRect) As NativeMethods.NativeRect
            If Not _isPreventingOverlap OrElse Not OverlapsAnyOther(dropped, ignoreStartOverlaps:=False) Then Return dropped

            Dim candidates As New List(Of NativeMethods.NativeRect) From {dropped}
            Dim currentStep As New List(Of NativeMethods.NativeRect) From {dropped}
            Dim bestSpot As NativeMethods.NativeRect? = Nothing
            Dim bestDistance As Long = Long.MaxValue

            For searchStep As Integer = 1 To FreeSpotSearchSteps
                Dim nextStep As New List(Of NativeMethods.NativeRect)()
                For Each spot As NativeMethods.NativeRect In currentStep
                    For Each other As NativeMethods.NativeRect In _otherStages
                        If Not ComesTooClose(spot, other) Then Continue For
                        For Each wayOut As NativeMethods.NativeRect In WaysOutOf(spot, other)
                            If candidates.Count >= MaximumFreeSpotCandidates Then Exit For
                            Dim movedOut As NativeMethods.NativeRect = wayOut ' a copy, so the check below can't be confused by the loop
                            If candidates.Any(Function(known) SameRect(known, movedOut)) Then Continue For
                            candidates.Add(movedOut)
                            nextStep.Add(movedOut)

                            If OverlapsAnyOther(movedOut, ignoreStartOverlaps:=False) OrElse Not IsOnAScreen(movedOut) Then Continue For
                            Dim distance As Long = SquaredDistance(dropped, movedOut)
                            If distance < bestDistance Then
                                bestDistance = distance
                                bestSpot = movedOut
                            End If
                        Next
                    Next
                Next
                currentStep = nextStep
            Next

            Return If(bestSpot, dropped)
        End Function

        ''' <summary>The four places just outside <paramref name="other"/> (gap included) that <paramref name="spot"/> could move to, keeping its other coordinate.</summary>
        Private Function WaysOutOf(spot As NativeMethods.NativeRect, other As NativeMethods.NativeRect) As List(Of NativeMethods.NativeRect)
            Return New List(Of NativeMethods.NativeRect) From {
                Shifted(spot, other.Left - _gap - spot.Right, 0),  ' to its left
                Shifted(spot, other.Right + _gap - spot.Left, 0),  ' to its right
                Shifted(spot, 0, other.Top - _gap - spot.Bottom),  ' above it
                Shifted(spot, 0, other.Bottom + _gap - spot.Top)   ' below it
            }
        End Function

#End Region

#Region "Resizing"

        ''' <summary>The rectangle to use instead of <paramref name="proposed"/> while the Stage is resized.</summary>
        Public Function AdjustResize(proposed As NativeMethods.NativeRect, edges As MovingEdges) As NativeMethods.NativeRect
            Dim adjusted As NativeMethods.NativeRect = proposed

            If _snapMode <> StageSnapMode.Off Then
                If edges.HasFlag(MovingEdges.Left) Then
                    adjusted.Left += BestShift(EdgeTargets(adjusted.Left, isVerticalEdge:=True, isLowEdge:=True, stage:=adjusted))
                End If
                If edges.HasFlag(MovingEdges.Right) Then
                    adjusted.Right += BestShift(EdgeTargets(adjusted.Right, isVerticalEdge:=True, isLowEdge:=False, stage:=adjusted))
                End If
                If edges.HasFlag(MovingEdges.Top) Then
                    adjusted.Top += BestShift(EdgeTargets(adjusted.Top, isVerticalEdge:=False, isLowEdge:=True, stage:=adjusted))
                End If
                If edges.HasFlag(MovingEdges.Bottom) Then
                    adjusted.Bottom += BestShift(EdgeTargets(adjusted.Bottom, isVerticalEdge:=False, isLowEdge:=False, stage:=adjusted))
                End If
                adjusted = KeepMinimumSize(adjusted, proposed, edges)
            End If

            If _isPreventingOverlap Then
                adjusted = StopEdgesShortOfOtherStages(adjusted, edges)
                If OverlapsAnyOther(adjusted, ignoreStartOverlaps:=True) Then
                    If _lastGoodResizeRect.HasValue Then adjusted = _lastGoodResizeRect.Value
                Else
                    _lastGoodResizeRect = adjusted
                End If
            End If
            Return adjusted
        End Function

        ''' <summary>
        ''' How far one dragged edge would move for each of its line-up positions.
        ''' <paramref name="isVerticalEdge"/>: a left/right edge (else top/bottom).
        ''' <paramref name="isLowEdge"/>: left or top (else right or bottom).
        ''' </summary>
        Private Function EdgeTargets(edgePosition As Integer, isVerticalEdge As Boolean, isLowEdge As Boolean,
                                     stage As NativeMethods.NativeRect) As List(Of Integer)
            Dim shifts As New List(Of Integer)()
            For Each other As NativeMethods.NativeRect In _otherStages
                If Not IsSnapPartner(stage, other) Then Continue For
                If isVerticalEdge Then
                    ' Stopping just beside it (side by side, level with it).
                    If RangesAreLevel(stage.Top, stage.Bottom, other.Top, other.Bottom) Then
                        Dim besideIt As Integer = If(isLowEdge, other.Right + _gap, other.Left - _gap)
                        shifts.Add(besideIt - edgePosition)
                    End If
                    ' In line with its same edge (Full: any Stage; Beside: the one stacked above/below;
                    ' never with "side by side only").
                    If Not _isSideBySideOnly AndAlso (_snapMode = StageSnapMode.Full OrElse IsStackedWith(stage, other)) Then
                        shifts.Add(If(isLowEdge, other.Left, other.Right) - edgePosition)
                    End If
                Else
                    ' Stopping just above or below it (not with "side by side only").
                    If Not _isSideBySideOnly AndAlso RangesAreLevel(stage.Left, stage.Right, other.Left, other.Right) Then
                        Dim besideIt As Integer = If(isLowEdge, other.Bottom + _gap, other.Top - _gap)
                        shifts.Add(besideIt - edgePosition)
                    End If
                    If _snapMode = StageSnapMode.Full OrElse IsSideBySideWith(stage, other) Then
                        shifts.Add(If(isLowEdge, other.Top, other.Bottom) - edgePosition)
                    End If
                End If
            Next
            If _snapMode = StageSnapMode.Full Then
                For Each workArea As NativeMethods.NativeRect In _workAreas
                    Dim screenEdge As Integer
                    If isVerticalEdge Then
                        screenEdge = If(isLowEdge, workArea.Left, workArea.Right)
                    Else
                        screenEdge = If(isLowEdge, workArea.Top, workArea.Bottom)
                    End If
                    shifts.Add(screenEdge - edgePosition)
                Next
            End If
            Return shifts
        End Function

        ''' <summary>
        ''' A dragged edge that ran into another Stage stops the gap's width short of it. Of the
        ''' dragged edges, the one that has to give way least is the one pulled back.
        ''' </summary>
        Private Function StopEdgesShortOfOtherStages(stage As NativeMethods.NativeRect, edges As MovingEdges) As NativeMethods.NativeRect
            Dim stopped As NativeMethods.NativeRect = stage
            For otherIndex As Integer = 0 To _otherStages.Count - 1
                If _overlappedAtStart.Contains(otherIndex) Then Continue For
                Dim other As NativeMethods.NativeRect = _otherStages(otherIndex)
                If Not ComesTooClose(stopped, other) Then Continue For

                Dim bestChange As Integer = Integer.MaxValue
                Dim bestRect As NativeMethods.NativeRect = stopped
                If edges.HasFlag(MovingEdges.Right) Then
                    Dim candidate As NativeMethods.NativeRect = stopped
                    candidate.Right = other.Left - _gap
                    ConsiderFix(candidate, Math.Abs(stopped.Right - candidate.Right), bestChange, bestRect)
                End If
                If edges.HasFlag(MovingEdges.Left) Then
                    Dim candidate As NativeMethods.NativeRect = stopped
                    candidate.Left = other.Right + _gap
                    ConsiderFix(candidate, Math.Abs(candidate.Left - stopped.Left), bestChange, bestRect)
                End If
                If edges.HasFlag(MovingEdges.Bottom) Then
                    Dim candidate As NativeMethods.NativeRect = stopped
                    candidate.Bottom = other.Top - _gap
                    ConsiderFix(candidate, Math.Abs(stopped.Bottom - candidate.Bottom), bestChange, bestRect)
                End If
                If edges.HasFlag(MovingEdges.Top) Then
                    Dim candidate As NativeMethods.NativeRect = stopped
                    candidate.Top = other.Bottom + _gap
                    ConsiderFix(candidate, Math.Abs(candidate.Top - stopped.Top), bestChange, bestRect)
                End If
                stopped = bestRect
            Next
            Return stopped
        End Function

        ''' <summary>Keeps <paramref name="candidate"/> if it is still big enough and changes less than the best so far.</summary>
        Private Sub ConsiderFix(candidate As NativeMethods.NativeRect, change As Integer,
                                ByRef bestChange As Integer, ByRef bestRect As NativeMethods.NativeRect)
            Dim isBigEnough As Boolean = Width(candidate) >= _minimumWidth AndAlso Height(candidate) >= _minimumHeight
            If isBigEnough AndAlso change < bestChange Then
                bestChange = change
                bestRect = candidate
            End If
        End Sub

        ''' <summary>A snap must never make the Stage smaller than its minimum: then that edge stays where the mouse put it.</summary>
        Private Function KeepMinimumSize(snapped As NativeMethods.NativeRect, proposed As NativeMethods.NativeRect,
                                         edges As MovingEdges) As NativeMethods.NativeRect
            Dim result As NativeMethods.NativeRect = snapped
            If Width(result) < _minimumWidth Then
                If edges.HasFlag(MovingEdges.Left) Then result.Left = proposed.Left
                If edges.HasFlag(MovingEdges.Right) Then result.Right = proposed.Right
            End If
            If Height(result) < _minimumHeight Then
                If edges.HasFlag(MovingEdges.Top) Then result.Top = proposed.Top
                If edges.HasFlag(MovingEdges.Bottom) Then result.Bottom = proposed.Bottom
            End If
            Return result
        End Function

#End Region

#Region "Guide lines"

        ''' <summary>
        ''' One line for each edge of <paramref name="stage"/> that is in line with the same edge of
        ''' at least one other Stage, long enough to reach from this Stage to the furthest of them.
        ''' In Beside mode only the Stages it sits right beside (or above / below) count, so lines
        ''' appear only for the Stage it has docked to.
        ''' </summary>
        Public Function FindGuides(stage As NativeMethods.NativeRect) As List(Of GuideLine)
            Dim countedStages As List(Of NativeMethods.NativeRect)
            If _snapMode = StageSnapMode.BesideStages Then
                countedStages = _otherStages.Where(
                    Function(other) IsSideBySideWith(stage, other) OrElse (Not _isSideBySideOnly AndAlso IsStackedWith(stage, other))).ToList()
            Else
                countedStages = _otherStages.Where(Function(other) IsSnapPartner(stage, other)).ToList()
            End If

            Dim guides As New List(Of GuideLine)()
            AddGuide(guides, stage, countedStages, Function(rect) rect.Top, isHorizontal:=True)
            AddGuide(guides, stage, countedStages, Function(rect) rect.Bottom, isHorizontal:=True)
            AddGuide(guides, stage, countedStages, Function(rect) rect.Left, isHorizontal:=False)
            AddGuide(guides, stage, countedStages, Function(rect) rect.Right, isHorizontal:=False)
            Return guides
        End Function

        ''' <param name="edgeOf">Picks the edge being compared (e.g. the top) from a rectangle.</param>
        Private Shared Sub AddGuide(guides As List(Of GuideLine), stage As NativeMethods.NativeRect,
                                    countedStages As List(Of NativeMethods.NativeRect),
                                    edgeOf As Func(Of NativeMethods.NativeRect, Integer), isHorizontal As Boolean)
            Dim edgePosition As Integer = edgeOf(stage)
            Dim inLine As List(Of NativeMethods.NativeRect) =
                countedStages.Where(Function(other) Math.Abs(edgeOf(other) - edgePosition) <= InLineTolerance).ToList()
            If inLine.Count = 0 Then Return

            ' The line runs across this Stage and every Stage in line with it.
            inLine.Add(stage)
            If isHorizontal Then
                guides.Add(New GuideLine(True, edgePosition, inLine.Min(Function(rect) rect.Left), inLine.Max(Function(rect) rect.Right)))
            Else
                guides.Add(New GuideLine(False, edgePosition, inLine.Min(Function(rect) rect.Top), inLine.Max(Function(rect) rect.Bottom)))
            End If
        End Sub

#End Region

#Region "Neighbours"

        ''' <summary>
        ''' True if <paramref name="other"/> is directly to the left or right of the Stage: level
        ''' with it, and the space between them is within snapping distance of the gap.
        ''' </summary>
        Private Function IsSideBySideWith(stage As NativeMethods.NativeRect, other As NativeMethods.NativeRect) As Boolean
            If Not RangesAreLevel(stage.Top, stage.Bottom, other.Top, other.Bottom) Then Return False
            Dim spaceBetween As Integer = Math.Max(other.Left - stage.Right, stage.Left - other.Right)
            Return Math.Abs(spaceBetween - _gap) <= _snapDistance
        End Function

        ''' <summary>
        ''' True if <paramref name="other"/> may be snapped to at all: always, unless "Only snap
        ''' side by side" is on - then only a Stage level with this one (in the same row).
        ''' </summary>
        Private Function IsSnapPartner(stage As NativeMethods.NativeRect, other As NativeMethods.NativeRect) As Boolean
            Return Not _isSideBySideOnly OrElse RangesAreLevel(stage.Top, stage.Bottom, other.Top, other.Bottom)
        End Function

        ''' <summary>The same for a Stage directly above or below.</summary>
        Private Function IsStackedWith(stage As NativeMethods.NativeRect, other As NativeMethods.NativeRect) As Boolean
            If Not RangesAreLevel(stage.Left, stage.Right, other.Left, other.Right) Then Return False
            Dim spaceBetween As Integer = Math.Max(other.Top - stage.Bottom, stage.Top - other.Bottom)
            Return Math.Abs(spaceBetween - _gap) <= _snapDistance
        End Function

#End Region

#Region "Small helpers"

        ''' <summary>The shift with the smallest size, if it is within the snap distance; otherwise 0 (no snap).</summary>
        Private Function BestShift(possibleShifts As List(Of Integer)) As Integer
            Dim best As Integer = 0
            Dim bestSize As Integer = Integer.MaxValue
            For Each possibleShift As Integer In possibleShifts
                Dim shiftSize As Integer = Math.Abs(possibleShift)
                If shiftSize <= _snapDistance AndAlso shiftSize < bestSize Then
                    best = possibleShift
                    bestSize = shiftSize
                End If
            Next
            Return best
        End Function

        ''' <summary>
        ''' True if two spans (e.g. two Stages' top-to-bottom) overlap or nearly meet, so the
        ''' Stages are level enough to sit side by side.
        ''' </summary>
        Private Function RangesAreLevel(firstStart As Integer, firstEnd As Integer, secondStart As Integer, secondEnd As Integer) As Boolean
            Return firstStart < secondEnd + _snapDistance AndAlso secondStart < firstEnd + _snapDistance
        End Function

        ''' <summary>True if the two rectangles overlap, or are closer than the gap.</summary>
        Private Function ComesTooClose(stage As NativeMethods.NativeRect, other As NativeMethods.NativeRect) As Boolean
            Return stage.Left < other.Right + _gap AndAlso stage.Right > other.Left - _gap AndAlso
                   stage.Top < other.Bottom + _gap AndAlso stage.Bottom > other.Top - _gap
        End Function

        ''' <summary>True if the rectangle comes too close to any other Stage (optionally not counting those it overlapped at the start).</summary>
        Private Function OverlapsAnyOther(stage As NativeMethods.NativeRect, ignoreStartOverlaps As Boolean) As Boolean
            For otherIndex As Integer = 0 To _otherStages.Count - 1
                If ignoreStartOverlaps AndAlso _overlappedAtStart.Contains(otherIndex) Then Continue For
                If ComesTooClose(stage, _otherStages(otherIndex)) Then Return True
            Next
            Return False
        End Function

        ''' <summary>True if the rectangle's centre is on one of the screens (or nothing is known about the screens).</summary>
        Private Function IsOnAScreen(spot As NativeMethods.NativeRect) As Boolean
            If _workAreas.Count = 0 Then Return True
            Dim centreX As Integer = (spot.Left + spot.Right) \ 2
            Dim centreY As Integer = (spot.Top + spot.Bottom) \ 2
            Return _workAreas.Any(Function(area) centreX >= area.Left AndAlso centreX < area.Right AndAlso
                                                 centreY >= area.Top AndAlso centreY < area.Bottom)
        End Function

        ''' <summary>How far apart two rectangles' top-left corners are (squared - only used for comparing).</summary>
        Private Shared Function SquaredDistance(first As NativeMethods.NativeRect, second As NativeMethods.NativeRect) As Long
            Dim sideways As Long = second.Left - first.Left
            Dim upOrDown As Long = second.Top - first.Top
            Return sideways * sideways + upOrDown * upOrDown
        End Function

        Private Shared Function SameRect(first As NativeMethods.NativeRect, second As NativeMethods.NativeRect) As Boolean
            Return first.Left = second.Left AndAlso first.Top = second.Top AndAlso
                   first.Right = second.Right AndAlso first.Bottom = second.Bottom
        End Function

        Private Shared Function Shifted(rect As NativeMethods.NativeRect, sideways As Integer, upOrDown As Integer) As NativeMethods.NativeRect
            Dim result As NativeMethods.NativeRect = rect
            result.Left += sideways
            result.Right += sideways
            result.Top += upOrDown
            result.Bottom += upOrDown
            Return result
        End Function

        Private Shared Function Width(rect As NativeMethods.NativeRect) As Integer
            Return rect.Right - rect.Left
        End Function

        Private Shared Function Height(rect As NativeMethods.NativeRect) As Integer
            Return rect.Bottom - rect.Top
        End Function

#End Region

    End Class

End Namespace
