' ============================================================================
'  StageWindow.OpenDirection.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  Which way a minified (or rolled-up) Stage opens (v0.8.9, at Dan's request):
'  Settings > General > Minified Stages > Open: Downwards (the default),
'  Upwards, or Automatic - upwards when there isn't room below on its screen
'  (and if it fits neither way, towards whichever side has more room).
'
'  Opening upwards works like a drop-down list that opens upwards: the title
'  bar stays exactly where it is, under the mouse, and the Stage's contents
'  appear ABOVE it. So while a Stage is open upwards:
'    * its title bar is docked at the BOTTOM of the window (the window's
'      layout is a DockPanel - StageWindow.xaml - so this is one property);
'    * it grows and shrinks with its bottom edge fixed (SetHeightKeepingBottom
'      sets the top and height in one go, so nothing jitters);
'    * the edge to resize it by is the top one (StageWindow.Resizing.vb);
'    * its saved position is still where the title bar sits ("the anchor"),
'      so it shuts back exactly where it was (Window_LocationChanged).
'  Once shut, the title bar goes back to the top, which looks the same as only
'  the title bar is showing.
'
'  The direction is chosen when a fully shut Stage starts to open. A roll that
'  turns round half-way (the mouse leaves while it's opening) keeps going the
'  same way. StageInfo.IsOpenedUpward remembers it across a restart for a Stage
'  that stays open (one rolled down by a title-bar click, with Minify off).
' ============================================================================
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports WinForms = System.Windows.Forms

Partial Public Class StageWindow

#Region "Open direction fields"
    ''' <summary>True while the Stage is open (or opening / shutting) upwards, its title bar at the bottom.</summary>
    Private _isOpenedUpward As Boolean
#End Region

#Region "Open direction: choosing"

    ''' <summary>
    ''' Which way this Stage should open from where its title bar is now: Settings' choice,
    ''' and for Automatic, whether its open height fits below the title bar on its screen.
    ''' </summary>
    Private Function ShouldOpenUpward() As Boolean
        Select Case AppSettingsStore.Current.MinifyOpenDirection
            Case StageOpenDirection.Up
                Return True
            Case StageOpenDirection.Automatic
                Return NeedsToOpenUpward()
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>
    ''' Automatic: True if opening downwards would run off the bottom of the screen's work area
    ''' (the desktop without the taskbar) and there's more room above. All in physical pixels:
    ''' this app is per-monitor DPI aware, so Windows Forms reports screens in physical pixels.
    ''' </summary>
    Private Function NeedsToOpenUpward() As Boolean
        If _windowHandle = IntPtr.Zero Then Return False
        Dim titleBarRect As NativeMethods.NativeRect ' the shut Stage is just its title bar
        If Not NativeMethods.GetWindowRect(_windowHandle, titleBarRect) Then Return False

        Dim workArea As System.Drawing.Rectangle = WinForms.Screen.FromHandle(_windowHandle).WorkingArea
        Dim openHeight As Integer = ToPhysicalPixels(_settings.Height)
        Dim roomBelow As Integer = workArea.Bottom - titleBarRect.Top   ' opening downwards: from the title's top
        Dim roomAbove As Integer = titleBarRect.Bottom - workArea.Top   ' opening upwards: up from the title's bottom

        If openHeight <= roomBelow Then Return False ' fits below: open the usual way
        If openHeight <= roomAbove Then Return True  ' fits above only
        Return roomAbove > roomBelow                  ' fits neither: the side with more room
    End Function

    ''' <summary>
    ''' For snapping and overlap (GetSnapBounds): the way this Stage will open, or is opening -
    ''' already chosen if it's rolling, otherwise as it would be chosen now.
    ''' </summary>
    Private Function OpensUpwardFromHere() As Boolean
        If _isAnimatingMinify Then Return _isOpenedUpward
        Return ShouldOpenUpward()
    End Function

#End Region

#Region "Open direction: the layout and the size"

    ''' <summary>
    ''' Starts or stops "open upwards": the title bar docks at the bottom or top, and the choice
    ''' is remembered (StageInfo.IsOpenedUpward) for a Stage that stays open over a restart.
    ''' </summary>
    Private Sub SetOpenedUpward(isUpward As Boolean)
        _isOpenedUpward = isUpward
        DockPanel.SetDock(TitleBar, If(isUpward, Dock.Bottom, Dock.Top))
        If _settings.IsOpenedUpward <> isUpward Then
            _settings.IsOpenedUpward = isUpward
            ScheduleSave()
        End If
    End Sub

    ''' <summary>
    ''' Sets the Stage's height for minify: from the top as usual, or - when opening upwards -
    ''' with the bottom edge (where the title bar is) staying put.
    ''' </summary>
    Private Sub ApplyMinifyHeight(heightInDips As Double)
        If _isOpenedUpward AndAlso _windowHandle <> IntPtr.Zero Then
            SetHeightKeepingBottom(heightInDips)
        Else
            Height = heightInDips
        End If
    End Sub

    ''' <summary>
    ''' Changes the height with the bottom edge fixed: top and height set together in one call
    ''' (setting WPF's Top and Height one after the other would show an in-between frame).
    ''' </summary>
    Private Sub SetHeightKeepingBottom(heightInDips As Double)
        Dim currentRect As NativeMethods.NativeRect
        If Not NativeMethods.GetWindowRect(_windowHandle, currentRect) Then Return
        Dim newHeight As Integer = ToPhysicalPixels(CInt(Math.Round(heightInDips)))
        Const KeepZOrderAndFocus As UInteger = NativeMethods.SWP_NOZORDER Or NativeMethods.SWP_NOACTIVATE
        NativeMethods.SetWindowPos(_windowHandle, IntPtr.Zero,
                                   currentRect.Left, currentRect.Bottom - newHeight,
                                   currentRect.Right - currentRect.Left, newHeight,
                                   KeepZOrderAndFocus)
    End Sub

    ''' <summary>
    ''' Called once a roll has reached its height (StageWindow.xaml.vb): a Stage that has shut
    ''' after opening upwards puts its title bar back at the top. Only the title bar shows, so
    ''' nothing visibly moves.
    ''' </summary>
    Private Sub OnMinifyHeightReached()
        If _isMinified AndAlso _isOpenedUpward Then
            SetOpenedUpward(False)
        End If
    End Sub

    ''' <summary>
    ''' A Stage that's open upwards and stops being one that shuts (Minify switched off): it
    ''' keeps its place and size, but becomes a normal Stage with its title bar on top.
    ''' </summary>
    Private Sub SettleAsNormalStage()
        If Not _isOpenedUpward Then Return
        SetOpenedUpward(False)
        _settings.PosY = CInt(Math.Round(Top)) ' the window's top is its position again
        ScheduleSave()
    End Sub

    ''' <summary>
    ''' Start-up, for a Stage that was left open upwards and doesn't start shut: opens it the
    ''' same way again above its title bar's saved position.
    ''' </summary>
    Private Sub RestoreUpwardOpening()
        SetOpenedUpward(True)
        Top = _settings.PosY + _titleHeight - Height
    End Sub

    ''' <summary>
    ''' The position to save, in DIPs: the window's top - or, while open upwards, where the
    ''' title bar sits (the window's bottom minus the title bar), so it shuts back there. The
    ''' window's current height is read from Windows, as WPF's ActualHeight can lag a move.
    ''' </summary>
    Private Function SavedTopPosition() As Double
        If Not _isOpenedUpward OrElse _windowHandle = IntPtr.Zero Then Return Top
        Dim currentRect As NativeMethods.NativeRect
        If Not NativeMethods.GetWindowRect(_windowHandle, currentRect) Then Return Top
        Dim screenScale As Double = VisualTreeHelper.GetDpi(Me).DpiScaleY
        Dim windowHeightInDips As Double = (currentRect.Bottom - currentRect.Top) / screenScale
        Return Top + windowHeightInDips - _titleHeight
    End Function

#End Region

End Class
