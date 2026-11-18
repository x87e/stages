' ============================================================================
'  SnapGuideOverlay.vb
'  Draws the coloured guide lines that show which Stages are in line while a
'  Stage is being moved or resized (Settings > Desktop > Arranging Stages).
'
'  Each line is its own tiny borderless window, filled with the guide colour
'  and placed in physical screen pixels - exactly the numbers StageSnapper
'  works in, so there is no scaling to get wrong on mixed-DPI setups. The
'  windows float above everything, can't be clicked or focused, and never
'  appear in the taskbar or Alt+Tab. They are made the first time they're
'  needed and then reused (hidden between moves).
'
'  Windows Forms is used for these windows (as for the tray icon) because a
'  plain Form can be placed by pixel and doesn't rescale itself when it lands
'  on a screen with different scaling, unlike a WPF window.
' ============================================================================
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports WinForms = System.Windows.Forms
Imports GdiDrawing = System.Drawing

Friend NotInheritable Class SnapGuideOverlay

    ''' <summary>SetWindowPos: keep the line above all ordinary windows.</summary>
    Private Shared ReadOnly AboveEverything As New IntPtr(-1) ' HWND_TOPMOST
    ''' <summary>SetWindowPos flags: place the line without activating it (Show() then makes it visible).</summary>
    Private Const PlaceWithoutActivatingFlags As UInteger = NativeMethods.SWP_NOACTIVATE

    Private Shared _instance As SnapGuideOverlay

    ''' <summary>Line windows made so far; the first N are used for N guides, the rest stay hidden.</summary>
    Private ReadOnly _lineWindows As New List(Of GuideLineWindow)()

    ''' <summary>The single shared overlay (UI thread only).</summary>
    Public Shared ReadOnly Property Instance As SnapGuideOverlay
        Get
            If _instance Is Nothing Then _instance = New SnapGuideOverlay()
            Return _instance
        End Get
    End Property

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Shows exactly these guide lines (hiding any others) in the colour "#RRGGBB", each
    ''' <paramref name="thicknessPixels"/> thick and centred on its edge.
    ''' </summary>
    Public Sub ShowGuides(guides As IReadOnlyList(Of StageSnapper.GuideLine), guideColorText As String, thicknessPixels As Integer)
        Dim lineColor As Color
        If Not ColorText.TryParseRgb(guideColorText, lineColor) Then
            ColorText.TryParseRgb(StageDefaults.DefaultSnapGuideColor, lineColor)
        End If
        Dim fillColor As GdiDrawing.Color = GdiDrawing.Color.FromArgb(lineColor.R, lineColor.G, lineColor.B)
        Dim halfThickness As Integer = thicknessPixels \ 2

        For guideIndex As Integer = 0 To guides.Count - 1
            If guideIndex >= _lineWindows.Count Then _lineWindows.Add(New GuideLineWindow())
            Dim lineWindow As GuideLineWindow = _lineWindows(guideIndex)
            Dim guide As StageSnapper.GuideLine = guides(guideIndex)
            Dim lineLength As Integer = Math.Max(1, guide.ToCoordinate - guide.FromCoordinate)

            Dim lineLeft, lineTop, lineWidth, lineHeight As Integer
            If guide.IsHorizontal Then
                lineLeft = guide.FromCoordinate
                lineTop = guide.Position - halfThickness
                lineWidth = lineLength
                lineHeight = thicknessPixels
            Else
                lineLeft = guide.Position - halfThickness
                lineTop = guide.FromCoordinate
                lineWidth = thicknessPixels
                lineHeight = lineLength
            End If

            If lineWindow.BackColor <> fillColor Then lineWindow.BackColor = fillColor
            NativeMethods.SetWindowPos(lineWindow.Handle, AboveEverything, lineLeft, lineTop, lineWidth, lineHeight, PlaceWithoutActivatingFlags)
            If Not lineWindow.Visible Then lineWindow.Show()
        Next

        ' Lines not needed this time.
        For spareIndex As Integer = guides.Count To _lineWindows.Count - 1
            If _lineWindows(spareIndex).Visible Then _lineWindows(spareIndex).Hide()
        Next
    End Sub

    ''' <summary>Hides every guide line (the move or resize has ended).</summary>
    Public Sub HideAll()
        For Each lineWindow As GuideLineWindow In _lineWindows
            If lineWindow.Visible Then lineWindow.Hide()
        Next
    End Sub

    ''' <summary>One guide line: a borderless, unclickable, never-focused coloured window.</summary>
    Private NotInheritable Class GuideLineWindow
        Inherits WinForms.Form

        Public Sub New()
            FormBorderStyle = WinForms.FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = WinForms.FormStartPosition.Manual
            ControlBox = False
            Text = String.Empty
            ' Sizes are set in pixels by SetWindowPos; Windows Forms must not scale anything.
            AutoScaleMode = WinForms.AutoScaleMode.None
            MinimumSize = GdiDrawing.Size.Empty
        End Sub

        ''' <summary>Showing a line must never take focus from the Stage being moved.</summary>
        Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
            Get
                Return True
            End Get
        End Property

        ''' <summary>Extended window styles: never activated, clicks pass through, topmost, not in Alt+Tab.</summary>
        Protected Overrides ReadOnly Property CreateParams As WinForms.CreateParams
            Get
                Dim windowParameters As WinForms.CreateParams = MyBase.CreateParams
                windowParameters.ExStyle = windowParameters.ExStyle Or
                                           NativeMethods.WS_EX_NOACTIVATE_STYLE Or
                                           NativeMethods.WS_EX_TRANSPARENT_STYLE Or
                                           NativeMethods.WS_EX_TOPMOST_STYLE Or
                                           NativeMethods.WS_EX_TOOLWINDOW_STYLE
                Return windowParameters
            End Get
        End Property
    End Class

End Class
