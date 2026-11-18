' ============================================================================
'  TitleHeightDialog.xaml.vb
'  Lets the user pick a Stage's title bar height in pixels. Usage:
'      Dim dialog As New TitleHeightDialog(currentHeight, defaultHeight) With {.Owner = someWindow}
'      If dialog.ShowDialog().GetValueOrDefault() Then use dialog.SelectedTitleHeight
'  "Restore default" moves the slider to the app-wide default (Settings > General).
' ============================================================================
Imports Stages.Model

Partial Public Class TitleHeightDialog

    ''' <summary>Format for the value label, e.g. "35px".</summary>
    Private Const PixelLabelFormat As String = "{0}px"

    ''' <summary>Where "Restore default" moves the slider to.</summary>
    Private ReadOnly _defaultHeight As Integer

    ''' <param name="currentHeight">The Stage's current title height, shown as the starting value.</param>
    ''' <param name="defaultHeight">The app-wide default title height (Settings > General).</param>
    Public Sub New(currentHeight As Integer, defaultHeight As Integer)
        InitializeComponent()

        _defaultHeight = StageDefaults.SanitizeTitleHeight(defaultHeight)
        HeightSlider.Minimum = StageDefaults.MinimumTitleHeight
        HeightSlider.Maximum = StageDefaults.MaximumTitleHeight
        HeightSlider.Value = StageDefaults.SanitizeTitleHeight(currentHeight)
        UpdateValueLabel()
    End Sub

    ''' <summary>The native window now exists: give it a title bar matching the theme.</summary>
    Protected Overrides Sub OnSourceInitialized(e As EventArgs)
        MyBase.OnSourceInitialized(e)
        Services.ThemeManager.ApplyTitleBar(Me)
    End Sub

    ''' <summary>The height chosen on the slider.</summary>
    Public ReadOnly Property SelectedTitleHeight As Integer
        Get
            Return CInt(Math.Round(HeightSlider.Value))
        End Get
    End Property

    Private Sub UpdateValueLabel()
        ' ValueChanged can fire while XAML is still being loaded, before the label exists.
        If HeightValueText Is Nothing Then Return
        HeightValueText.Text = String.Format(PixelLabelFormat, SelectedTitleHeight)
    End Sub

    Private Sub HeightSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        UpdateValueLabel()
    End Sub

    Private Sub RestoreDefaultButton_Click(sender As Object, e As RoutedEventArgs)
        HeightSlider.Value = _defaultHeight
    End Sub

    Private Sub OkButton_Click(sender As Object, e As RoutedEventArgs)
        ' Closes the dialog; ShowDialog() returns True.
        DialogResult = True
    End Sub

End Class
