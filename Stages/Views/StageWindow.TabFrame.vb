' ============================================================================
'  StageWindow.TabFrame.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  The "Outlined" tab look's frame (v0.8.7, at Dan's request): like a tabbed
'  window in Windows or Visual Studio, the open tab's outline carries on along
'  the bottom of the title bar to both sides and then round the rest of the
'  Stage, so the open tab and the Stage below it make one outlined shape,
'  open only where the tab joins the Stage.
'
'  The open tab draws its own top and sides (its Border, StageTabOutlinedStyle
'  in StagesStyles.xaml, 2 pixels wide). TabFrameOutline (a Path laid over the
'  whole Stage in StageWindow.xaml, ignoring the mouse) draws the rest, in the
'  same colour (the "StageTabAccentBrush" resource) and width:
'
'        +-------+
'   +----+  tab  +---------------+   <- along the bottom of the title bar,
'   |                            |      stopping at the tab's two sides
'   |                            |
'   +----------------------------+   <- bottom corners rounded like the window's
'
'  Its shape is worked out again whenever the layout changes (the Stage is
'  resized, another tab opens, a tab is renamed, the title moves...) - only
'  when something actually moved, so redrawing can't set off another layout
'  pass forever. It's hidden unless the Outlined look is chosen, the Stage has
'  tabs, and the Stage is open (not minified or rolled up) the usual way - not
'  while it's open upwards with its title bar at the bottom.
' ============================================================================
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports Stages.ViewModels

Partial Public Class StageWindow

#Region "Tab frame constants"
    ''' <summary>Width of the frame line - the same as the open tab's outline (StageTabOutlinedStyle).</summary>
    Private Const TabFrameThickness As Double = 2
    ''' <summary>Below this much open Stage under the title bar (e.g. mid-minify), no frame is drawn.</summary>
    Private Const TabFrameMinimumBodyHeight As Double = 12
    ''' <summary>How far apart two positions must be (in pixels) to count as "moved".</summary>
    Private Const TabFrameMoveTolerance As Double = 0.5
#End Region

#Region "Tab frame fields"
    ''' <summary>Where things were when the frame was last drawn: open tab left / right, title bottom, Stage width / height, rounded.</summary>
    Private _lastTabFrameLayout As Double() = Nothing
#End Region

    ''' <summary>Window.LayoutUpdated (hooked in Window_Loaded, StageWindow.xaml.vb).</summary>
    Private Sub StageWindow_LayoutUpdated(sender As Object, e As EventArgs)
        UpdateTabFrame()
    End Sub

    ''' <summary>
    ''' Called after every layout pass (Window.LayoutUpdated, hooked in StageWindow.xaml.vb):
    ''' shows, hides or reshapes the frame round the Stage for the Outlined tab look.
    ''' </summary>
    Private Sub UpdateTabFrame()
        ' WPF raises LayoutUpdated after every layout pass anywhere in the app, so the cheap
        ' checks come first and most calls stop here.
        Dim isWanted As Boolean = AppSettingsStore.Current.TabStyle = StageTabStyle.Outlined AndAlso
                                  HasTabs AndAlso Not _isMinified AndAlso Not _isOpenedUpward AndAlso
                                  TabFrameOutline.IsLoaded
        Dim openTabBorder As Border = If(isWanted, FindOpenTabBorder(), Nothing)
        If openTabBorder Is Nothing OrElse Not openTabBorder.IsVisible Then
            HideTabFrame()
            Return
        End If

        ' Everything in the frame overlay's own coordinates (it covers the whole Stage).
        Dim tabTopLeft As Point
        Dim titleBarBottom As Double
        Try
            tabTopLeft = openTabBorder.TransformToVisual(TabFrameOutline).Transform(New Point(0, 0))
            titleBarBottom = TitleBar.TransformToVisual(TabFrameOutline).Transform(New Point(0, TitleBar.ActualHeight)).Y
        Catch ex As InvalidOperationException
            ' The tab is being rebuilt and isn't connected to the window yet; the next pass will do.
            Return
        End Try
        Dim stageWidth As Double = TabFrameOutline.ActualWidth
        Dim stageHeight As Double = TabFrameOutline.ActualHeight
        If stageHeight - titleBarBottom < TabFrameMinimumBodyHeight OrElse stageWidth <= 0 Then
            HideTabFrame() ' rolling up, or too small to frame
            Return
        End If

        Dim isRounded As Boolean = AppSettingsStore.Current.CornerStyle = StageCornerStyle.Rounded AndAlso
                                   DesktopIntegration.CanRoundCorners()
        Dim layoutNow As Double() = {tabTopLeft.X, tabTopLeft.X + openTabBorder.ActualWidth,
                                     titleBarBottom, stageWidth, stageHeight, If(isRounded, 1, 0)}
        If TabFrameOutline.Visibility = Visibility.Visible AndAlso IsSameLayout(layoutNow, _lastTabFrameLayout) Then
            Return ' nothing moved: leave it, so this doesn't cause another layout pass
        End If
        _lastTabFrameLayout = layoutNow

        TabFrameOutline.Data = BuildTabFrameShape(tabTopLeft.X, tabTopLeft.X + openTabBorder.ActualWidth,
                                                  titleBarBottom, stageWidth, stageHeight, isRounded)
        TabFrameOutline.StrokeThickness = TabFrameThickness
        TabFrameOutline.Visibility = Visibility.Visible
    End Sub

    ''' <summary>Hides the frame (and forgets the last shape, so it's drawn afresh next time).</summary>
    Private Sub HideTabFrame()
        If TabFrameOutline.Visibility <> Visibility.Collapsed Then
            TabFrameOutline.Visibility = Visibility.Collapsed
        End If
        _lastTabFrameLayout = Nothing
    End Sub

    ''' <summary>
    ''' The frame's line: from the open tab's left side along the bottom of the title bar to
    ''' the Stage's left edge, down, along the bottom, up the right edge, and back along the
    ''' title bar to the tab's right side. Left open over the tab. WPF draws a line centred
    ''' on its path, so every edge is set in by half the line's width to stay inside the Stage.
    ''' </summary>
    Private Shared Function BuildTabFrameShape(tabLeft As Double, tabRight As Double, titleBarBottom As Double,
                                               stageWidth As Double, stageHeight As Double,
                                               isRounded As Boolean) As Geometry
        Dim halfLine As Double = TabFrameThickness / 2
        Dim topLine As Double = titleBarBottom - halfLine        ' the last 2 pixels of the title bar
        Dim leftLine As Double = halfLine
        Dim rightLine As Double = stageWidth - halfLine
        Dim bottomLine As Double = stageHeight - halfLine
        ' Bottom corners follow the window's own rounding (8 pixels), measured to the line's middle.
        Dim cornerRadius As Double = If(isRounded, Math.Max(0, RoundedCornerRadius - halfLine), 0)
        Dim cornerSize As New Size(cornerRadius, cornerRadius)

        ' The tab's own outline is 2 pixels inside each side of it; the frame meets those lines.
        Dim tabLeftLine As Double = tabLeft + halfLine
        Dim tabRightLine As Double = tabRight - halfLine

        Dim shape As New StreamGeometry()
        Using pen As StreamGeometryContext = shape.Open()
            Const IsFilled As Boolean = False
            Const IsClosed As Boolean = False    ' open over the tab
            Const IsStroked As Boolean = True
            Const IsSmoothJoin As Boolean = False
            Const SmallArc As Boolean = False
            pen.BeginFigure(New Point(tabLeftLine, topLine), IsFilled, IsClosed)
            pen.LineTo(New Point(leftLine, topLine), IsStroked, IsSmoothJoin)
            pen.LineTo(New Point(leftLine, bottomLine - cornerRadius), IsStroked, IsSmoothJoin)
            If cornerRadius > 0 Then
                pen.ArcTo(New Point(leftLine + cornerRadius, bottomLine), cornerSize, 0, SmallArc,
                          SweepDirection.Counterclockwise, IsStroked, IsSmoothJoin)
            End If
            pen.LineTo(New Point(rightLine - cornerRadius, bottomLine), IsStroked, IsSmoothJoin)
            If cornerRadius > 0 Then
                pen.ArcTo(New Point(rightLine, bottomLine - cornerRadius), cornerSize, 0, SmallArc,
                          SweepDirection.Counterclockwise, IsStroked, IsSmoothJoin)
            End If
            pen.LineTo(New Point(rightLine, topLine), IsStroked, IsSmoothJoin)
            pen.LineTo(New Point(tabRightLine, topLine), IsStroked, IsSmoothJoin)
        End Using
        shape.Freeze()
        Return shape
    End Function

    ''' <summary>The open tab's Border in the tab strip, or Nothing (no tabs, or not laid out yet).</summary>
    Private Function FindOpenTabBorder() As Border
        For Each header As StageTabHeader In _tabHeaders
            If Not header.IsActive Then Continue For
            ' Each tab is a ContentPresenter (made by the ItemsControl) holding the template's Border.
            Dim tabContainer As DependencyObject = TabStrip.ItemContainerGenerator.ContainerFromItem(header)
            If tabContainer Is Nothing OrElse VisualTreeHelper.GetChildrenCount(tabContainer) = 0 Then Return Nothing
            Return TryCast(VisualTreeHelper.GetChild(tabContainer, 0), Border)
        Next
        Return Nothing
    End Function

    ''' <summary>True if every position in the two lists is within TabFrameMoveTolerance of the other.</summary>
    Private Shared Function IsSameLayout(current As Double(), previous As Double()) As Boolean
        If previous Is Nothing OrElse previous.Length <> current.Length Then Return False
        For position As Integer = 0 To current.Length - 1
            If Math.Abs(current(position) - previous(position)) > TabFrameMoveTolerance Then Return False
        Next
        Return True
    End Function

End Class
