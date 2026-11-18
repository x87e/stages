' ============================================================================
'  BackgroundPictureDialog.xaml.vb
'  Edits one Stage's background picture. Usage:
'      Dim dialog As New BackgroundPictureDialog(startingChoice, AddressOf PreviewOnStage) With {.Owner = stageWindow}
'      If dialog.ShowDialog().GetValueOrDefault() Then save dialog.Result
'  Every change is passed straight to the preview callback (a copy each time),
'  so the Stage shows it while the dialog is open. The dialog itself never
'  writes anything: the Stage saves Result after OK, or undoes the preview
'  after Cancel.
' ============================================================================
Imports System.IO
Imports System.Windows.Media.Imaging
Imports Stages.Model
Imports Stages.Services

Partial Public Class BackgroundPictureDialog

    Private Const PercentLabelFormat As String = "{0}%"
    Private Const PixelLabelFormat As String = "{0}px"
    Private Const NoPictureNote As String = "No picture: the Stage shows its usual background."
    Private Const UnreadablePictureNote As String = "This picture couldn't be opened. It may be damaged, or a type Windows can't read."
    Private Const ChoosePictureTitle As String = "Choose a background picture"

    ''' <summary>The settings as edited so far.</summary>
    Private ReadOnly _choice As BackgroundPictureChoice
    ''' <summary>Shows a choice on the Stage (called after every change).</summary>
    Private ReadOnly _showPreview As Action(Of BackgroundPictureChoice)
    ''' <summary>True while controls are being set from code (their change events are ignored).</summary>
    Private _isSettingControls As Boolean

    ''' <param name="startingChoice">The Stage's current picture settings.</param>
    ''' <param name="showPreview">Shows a choice on the Stage, without saving it.</param>
    Public Sub New(startingChoice As BackgroundPictureChoice, showPreview As Action(Of BackgroundPictureChoice))
        InitializeComponent()
        _choice = startingChoice.Copy()
        _showPreview = showPreview

        _isSettingControls = True
        Try
            OpacitySlider.Minimum = StageDefaults.MinimumPercent
            OpacitySlider.Maximum = StageDefaults.MaximumPercent
            ShadeSlider.Minimum = StageDefaults.MinimumPercent
            ShadeSlider.Maximum = StageDefaults.MaximumPercent
            BlurSlider.Minimum = 0
            BlurSlider.Maximum = StageDefaults.MaximumPictureBlurRadius

            OpacitySlider.Value = _choice.OpacityPercent
            ShadeSlider.Value = _choice.ShadePercent
            BlurSlider.Value = _choice.BlurRadius
            Select Case _choice.Fit
                Case BackgroundPictureFit.Fit
                    FitWholeRadio.IsChecked = True
                Case BackgroundPictureFit.Stretch
                    FitStretchRadio.IsChecked = True
                Case Else
                    FitFillRadio.IsChecked = True
            End Select
        Finally
            _isSettingControls = False
        End Try

        UpdateValueLabels()
        ShowPreviewThumbnail()
    End Sub

    ''' <summary>The native window now exists: give it a title bar matching the theme.</summary>
    Protected Overrides Sub OnSourceInitialized(e As EventArgs)
        MyBase.OnSourceInitialized(e)
        ThemeManager.ApplyTitleBar(Me)
    End Sub

    ''' <summary>The settings chosen (read after OK).</summary>
    Public ReadOnly Property Result As BackgroundPictureChoice
        Get
            Return _choice.Copy()
        End Get
    End Property

    ''' <summary>The "45%" / "8px" labels beside the sliders.</summary>
    Private Sub UpdateValueLabels()
        ' ValueChanged can fire while XAML is still being loaded, before the labels exist.
        If OpacityValueText Is Nothing OrElse ShadeValueText Is Nothing OrElse BlurValueText Is Nothing Then Return
        OpacityValueText.Text = String.Format(PercentLabelFormat, _choice.OpacityPercent)
        ShadeValueText.Text = String.Format(PercentLabelFormat, _choice.ShadePercent)
        BlurValueText.Text = String.Format(PixelLabelFormat, _choice.BlurRadius)
    End Sub

    ''' <summary>Shows the chosen picture in the dialog's own preview box (or a note).</summary>
    Private Sub ShowPreviewThumbnail()
        PreviewImage.Source = Nothing
        If Not _choice.HasPicture Then
            PreviewNoteText.Text = NoPictureNote
            Return
        End If

        Dim picture As BitmapSource = PictureLoader.Load(_choice.PictureFilePath)
        PreviewImage.Source = picture
        PreviewNoteText.Text = If(picture Is Nothing, UnreadablePictureNote, String.Empty)
    End Sub

    ''' <summary>Hands a copy of the current settings to the Stage to show.</summary>
    Private Sub SendPreview()
        If _showPreview IsNot Nothing Then _showPreview.Invoke(_choice.Copy())
    End Sub

    Private Sub ChoosePictureButton_Click(sender As Object, e As RoutedEventArgs)
        Dim picker As New Microsoft.Win32.OpenFileDialog() With {
            .Title = ChoosePictureTitle,
            .Filter = PictureLoader.PictureFileFilter,
            .CheckFileExists = True,
            .Multiselect = False
        }
        ' Start in the folder of the current picture, if it's somewhere other than the Stage.
        If _choice.HasPicture AndAlso File.Exists(_choice.PictureFilePath) Then
            picker.InitialDirectory = Path.GetDirectoryName(_choice.PictureFilePath)
        End If
        If Not picker.ShowDialog(Me).GetValueOrDefault() Then Return

        _choice.PictureFilePath = picker.FileName
        ShowPreviewThumbnail()
        SendPreview()
    End Sub

    Private Sub NoPictureButton_Click(sender As Object, e As RoutedEventArgs)
        _choice.PictureFilePath = Nothing
        ShowPreviewThumbnail()
        SendPreview()
    End Sub

    Private Sub FitRadio_Checked(sender As Object, e As RoutedEventArgs)
        If _isSettingControls OrElse Not IsInitialized Then Return
        If sender Is FitWholeRadio Then
            _choice.Fit = BackgroundPictureFit.Fit
        ElseIf sender Is FitStretchRadio Then
            _choice.Fit = BackgroundPictureFit.Stretch
        Else
            _choice.Fit = BackgroundPictureFit.Fill
        End If
        SendPreview()
    End Sub

    Private Sub PictureSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        If _isSettingControls OrElse _choice Is Nothing Then Return
        _choice.OpacityPercent = CInt(Math.Round(OpacitySlider.Value))
        _choice.ShadePercent = CInt(Math.Round(ShadeSlider.Value))
        _choice.BlurRadius = CInt(Math.Round(BlurSlider.Value))
        UpdateValueLabels()
        SendPreview()
    End Sub

    Private Sub OkButton_Click(sender As Object, e As RoutedEventArgs)
        ' Closes the dialog; ShowDialog() returns True.
        DialogResult = True
    End Sub

End Class
