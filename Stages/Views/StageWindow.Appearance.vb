' ============================================================================
'  StageWindow.Appearance.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  How the Stage looks, beyond its background effect (ApplyBackdropNow):
'
'    Stage text and frame ... font, label and title sizes, title bar / text /
'                             label colours, outline, corner shape - the same
'                             on every Stage (Settings > Appearance)
'    Background picture ..... this Stage's own picture, with fit, strength,
'                             shade and blur (Stage menu > Background > Picture...)
'
'  The text and frame values are put into this window's own resources, which
'  take priority over the starting values in StagesStyles.xaml; the styles use
'  {DynamicResource} for them, so everything updates live as Settings change.
' ============================================================================
Imports System.IO
Imports System.Windows.Media.Effects
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services

Partial Public Class StageWindow

#Region "Appearance constants"
    ' Resource keys in StagesStyles.xaml that Settings > Appearance overrides.
    ' (StageLabelFontSize and StageLabelMaxHeight are declared in StageWindow.Layout.vb.)
    Private Const TitleBackgroundResourceKey As String = "StageTitleBackgroundBrush"
    Private Const TitleForegroundResourceKey As String = "StageTitleForegroundBrush"
    Private Const FontFamilyResourceKey As String = "StageFontFamily"
    Private Const LabelBrushResourceKey As String = "ItemLabelBrush"
    Private Const BorderBrushResourceKey As String = "StageBorderBrush"
    ''' <summary>The key the tab template looks its Style up by (StagesStyles.xaml).</summary>
    Private Const TabHeaderStyleResourceKey As String = "StageTabHeaderStyle"
    Private Const TabAccentBrushResourceKey As String = "StageTabAccentBrush"
    ''' <summary>The four tab looks in StagesStyles.xaml, one per StageTabStyle.</summary>
    Private Const TabUnderlineStyleKey As String = "StageTabUnderlineStyle"
    Private Const TabShadedStyleKey As String = "StageTabShadedStyle"
    Private Const TabOutlinedStyleKey As String = "StageTabOutlinedStyle"
    Private Const TabClearStyleKey As String = "StageTabClearStyle"

    ''' <summary>Two lines of label text are about this many times the font size tall.</summary>
    Private Const LabelTwoLineHeightFactor As Double = 2.9
    ''' <summary>Windows 11 rounds window corners with this radius; the outline follows it.</summary>
    Private Const RoundedCornerRadius As Double = 8
    Private Const FullOpacityPercent As Double = 100.0

    Private Const PictureMenuText As String = "Picture..."
    Private Const RemovePictureMenuText As String = "Remove picture"
    Private Const PictureCaption As String = "Background picture"
    Private Const PictureCopyFailedText As String =
        "The picture couldn't be copied into the Stage's folder, so it wasn't changed. See the log for details."
    ''' <summary>Name used while a new picture is copied in, before it replaces the old one (".stage-" files are never shown).</summary>
    Private Const IncomingPictureSuffix As String = ".incoming"
#End Region

#Region "Appearance fields"
    ''' <summary>Counts picture loads, so a slow load that finishes after a newer one is ignored.</summary>
    Private _pictureLoadNumber As Integer
    ''' <summary>The picture file last decoded, and the result - so preview slider changes don't re-read it.</summary>
    Private _decodedPicturePath As String
    Private _decodedPicture As ImageSource
#End Region

#Region "Stage text and frame (Settings > Appearance)"

    ''' <summary>Applies Settings > Appearance's text and frame choices to this Stage.</summary>
    Private Sub ApplyStageAppearance()
        Dim currentSettings As AppSettings = AppSettingsStore.Current

        Resources(TitleBackgroundResourceKey) = currentSettings.CreateTitleBarBrush()
        Resources(TitleForegroundResourceKey) = currentSettings.CreateTitleTextBrush()
        Resources(LabelBrushResourceKey) = currentSettings.CreateLabelTextBrush()
        Resources(BorderBrushResourceKey) = currentSettings.CreateBorderBrush()
        Resources(FontFamilyResourceKey) = New FontFamily(currentSettings.StageFontFamily)

        ' Sizes are chosen in points; WPF measures in 1/96 inch.
        Dim labelFontSize As Double = currentSettings.LabelFontSizePoints * PointsToDeviceIndependentPixels
        Resources(LabelFontSizeResourceKey) = labelFontSize
        Resources(LabelMaxHeightResourceKey) = Math.Ceiling(labelFontSize * LabelTwoLineHeightFactor)

        StageOutline.BorderThickness = New Thickness(currentSettings.BorderThickness)
        ApplyCornerStyle()
        ApplyTitleAndTabStyle()
    End Sub

    ''' <summary>
    ''' Settings > Appearance > Title and tabs: where the title (or the row of tabs) sits, and
    ''' how the open tab is marked. The tab template picks the Style up by its resource key,
    ''' so swapping the resource is enough to restyle every tab.
    ''' </summary>
    Private Sub ApplyTitleAndTabStyle()
        Dim currentSettings As AppSettings = AppSettingsStore.Current

        Dim position As HorizontalAlignment
        Select Case currentSettings.TitleAlignment
            Case StageTitleAlignment.Left : position = HorizontalAlignment.Left
            Case StageTitleAlignment.Right : position = HorizontalAlignment.Right
            Case Else : position = HorizontalAlignment.Center
        End Select
        TitleText.HorizontalAlignment = position
        TabStrip.HorizontalAlignment = position

        Dim chosenStyleKey As String
        Select Case currentSettings.TabStyle
            Case StageTabStyle.Shaded : chosenStyleKey = TabShadedStyleKey
            Case StageTabStyle.Outlined : chosenStyleKey = TabOutlinedStyleKey
            Case StageTabStyle.Clear : chosenStyleKey = TabClearStyleKey
            Case Else : chosenStyleKey = TabUnderlineStyleKey
        End Select
        Dim chosenStyle As Style = TryCast(TryFindResource(chosenStyleKey), Style)
        If chosenStyle IsNot Nothing Then
            Resources(TabHeaderStyleResourceKey) = chosenStyle
        End If
        RefreshTabAccentBrush()
    End Sub

    ''' <summary>
    ''' The open tab's underline / outline colour. Automatic (the default) follows this Stage's
    ''' own background colour for the Outlined look (AppSettings.EffectiveTabAccentColor), so
    ''' it's refreshed whenever the background changes too (ApplyBackdropNow).
    ''' </summary>
    Private Sub RefreshTabAccentBrush()
        Resources(TabAccentBrushResourceKey) = AppSettingsStore.Current.CreateTabAccentBrush(EffectiveBackdrop())
    End Sub

    ''' <summary>
    ''' Rounds or squares the Stage's corners (Windows 11), and gives the outline the same
    ''' shape. On Windows 10 the corners are always square.
    ''' </summary>
    Private Sub ApplyCornerStyle()
        Dim wantsRounded As Boolean = AppSettingsStore.Current.CornerStyle = StageCornerStyle.Rounded
        Dim isRounded As Boolean = wantsRounded AndAlso DesktopIntegration.CanRoundCorners()
        StageOutline.CornerRadius = New CornerRadius(If(isRounded, RoundedCornerRadius, 0))
        DesktopIntegration.SetWindowCorners(_windowHandle, wantsRounded) ' does nothing until the window exists
    End Sub

#End Region

#Region "Background picture"

    ''' <summary>Shows this Stage's saved picture (or none).</summary>
    Private Sub ApplyBackgroundPicture()
        ShowBackgroundPicture(BackgroundPictureChoice.FromStage(_settings))
    End Sub

    ''' <summary>
    ''' Shows a picture choice on the Stage - the saved one, or a preview from the picture
    ''' dialog. The file is decoded on a background thread, so a big photo can't freeze the
    ''' Stage. "Async Sub" = fire-and-forget; problems are logged and leave no picture.
    ''' </summary>
    Private Async Sub ShowBackgroundPicture(choice As BackgroundPictureChoice)
        _pictureLoadNumber += 1
        Dim thisLoadNumber As Integer = _pictureLoadNumber

        If Not choice.HasPicture OrElse Not File.Exists(choice.PictureFilePath) Then
            HideBackgroundPicture()
            Return
        End If

        Dim picture As ImageSource = Nothing
        If String.Equals(choice.PictureFilePath, _decodedPicturePath, StringComparison.OrdinalIgnoreCase) Then
            picture = _decodedPicture
        Else
            Dim fileToLoad As String = choice.PictureFilePath
            picture = Await Task.Run(Function() DirectCast(PictureLoader.Load(fileToLoad), ImageSource))
            ' Only remember pictures that loaded, so a failed one is tried again next time.
            If picture IsNot Nothing Then
                _decodedPicturePath = fileToLoad
                _decodedPicture = picture
            End If
        End If

        ' A newer choice arrived while this one loaded, or the Stage has gone: drop this one.
        If thisLoadNumber <> _pictureLoadNumber OrElse _isClosed Then Return
        If picture Is Nothing Then
            HideBackgroundPicture()
            Return
        End If

        BackgroundPicture.Source = picture
        BackgroundPicture.Stretch = StretchFor(choice.Fit)
        BackgroundPicture.Opacity = choice.OpacityPercent / FullOpacityPercent
        If choice.BlurRadius > 0 Then
            BackgroundPicture.Effect = New BlurEffect() With {
                .Radius = choice.BlurRadius,
                .KernelType = KernelType.Gaussian,
                .RenderingBias = RenderingBias.Performance
            }
            ' Blur fades the picture's edges to transparent; making it a little bigger than
            ' the Stage keeps those faded edges out of sight.
            BackgroundPicture.Margin = New Thickness(-choice.BlurRadius)
        Else
            BackgroundPicture.Effect = Nothing
            BackgroundPicture.Margin = New Thickness(0)
        End If
        BackgroundPicture.Visibility = Visibility.Visible

        BackgroundShade.Opacity = choice.ShadePercent / FullOpacityPercent
        BackgroundShade.Visibility = If(choice.ShadePercent > 0, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Sub HideBackgroundPicture()
        BackgroundPicture.Source = Nothing
        BackgroundPicture.Effect = Nothing
        BackgroundPicture.Visibility = Visibility.Collapsed
        BackgroundShade.Visibility = Visibility.Collapsed
    End Sub

    Private Shared Function StretchFor(pictureFit As BackgroundPictureFit) As Stretch
        Select Case pictureFit
            Case BackgroundPictureFit.Fit : Return Stretch.Uniform
            Case BackgroundPictureFit.Stretch : Return Stretch.Fill
            Case Else : Return Stretch.UniformToFill
        End Select
    End Function

    ''' <summary>Adds Picture... (and Remove picture, if it has one) to the Background sub-menu.</summary>
    Private Sub AddPictureMenuItems(backgroundMenu As NativePopupMenu)
        backgroundMenu.AddItem(PictureMenuText, AddressOf ChooseBackgroundPicture)
        If Not String.IsNullOrEmpty(_settings.BackgroundPictureFile) Then
            backgroundMenu.AddItem(RemovePictureMenuText, AddressOf RemoveBackgroundPicture)
        End If
    End Sub

    ''' <summary>Opens the picture dialog; the Stage previews every change, and keeps it only after OK.</summary>
    Private Sub ChooseBackgroundPicture()
        Dim savedChoice As BackgroundPictureChoice = BackgroundPictureChoice.FromStage(_settings)
        Dim dialog As New BackgroundPictureDialog(savedChoice, AddressOf ShowBackgroundPicture) With {.Owner = Me}

        Dim wasConfirmed As Boolean
        _isPopupOpen = True
        Try
            wasConfirmed = dialog.ShowDialog().GetValueOrDefault()
        Finally
            _isPopupOpen = False
        End Try

        If wasConfirmed Then
            SaveBackgroundPicture(dialog.Result)
        Else
            ShowBackgroundPicture(savedChoice) ' undo the preview
        End If
        CheckWhetherMouseHasLeft()
    End Sub

    ''' <summary>
    ''' Keeps the dialog's choice: copies a newly chosen picture into the Stage's folder (as a
    ''' hidden ".stage-background" file, replacing any earlier one), then saves the settings.
    ''' </summary>
    Private Sub SaveBackgroundPicture(chosen As BackgroundPictureChoice)
        Dim savedPicturePath As String = BackgroundPictureChoice.FromStage(_settings).PictureFilePath

        If Not chosen.HasPicture Then
            DeleteOwnPictureFile()
            _settings.BackgroundPictureFile = Nothing
        ElseIf Not StageFolder.IsSamePath(chosen.PictureFilePath, savedPicturePath) Then
            Dim copiedFileName As String = CopyPictureIntoStage(chosen.PictureFilePath)
            If copiedFileName Is Nothing Then
                ShowMessage(PictureCopyFailedText, PictureCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
                ApplyBackgroundPicture() ' back to the saved picture
                Return
            End If
            _settings.BackgroundPictureFile = copiedFileName
        End If

        _settings.BackgroundPictureOpacityPercent = chosen.OpacityPercent
        _settings.BackgroundPictureShadePercent = chosen.ShadePercent
        _settings.BackgroundPictureBlurRadius = chosen.BlurRadius
        _settings.BackgroundPictureFit = chosen.Fit
        SaveNow()
        ApplyBackgroundPicture()
    End Sub

    ''' <summary>Stage menu > Background > Remove picture.</summary>
    Private Sub RemoveBackgroundPicture()
        DeleteOwnPictureFile()
        _settings.BackgroundPictureFile = Nothing
        SaveNow()
        ApplyBackgroundPicture()
    End Sub

    ''' <summary>
    ''' Copies a picture into the Stage's folder as ".stage-background" plus its extension,
    ''' hidden. Copied under a temporary name first, so the old picture is only replaced once
    ''' the copy is complete. Returns the new file name, or Nothing (logged) on failure.
    ''' </summary>
    Private Function CopyPictureIntoStage(sourcePicturePath As String) As String
        Dim pictureExtension As String = Path.GetExtension(sourcePicturePath).ToLowerInvariant()
        Dim finalFileName As String = StageFolder.BackgroundPictureBaseName & pictureExtension
        Dim finalPath As String = Path.Combine(_settings.FolderPath, finalFileName)
        Dim incomingPath As String = Path.Combine(_settings.FolderPath, StageFolder.BackgroundPictureBaseName & IncomingPictureSuffix & pictureExtension)

        ' The old copy is only deleted once the new one is safely in place, so a failure
        ' part-way never leaves the Stage with no picture file.
        Dim oldFileName As String = _settings.BackgroundPictureFile
        Dim isSameNameAsOld As Boolean = String.Equals(oldFileName, finalFileName, StringComparison.OrdinalIgnoreCase)
        Try
            File.Copy(sourcePicturePath, incomingPath, overwrite:=True)
            If File.Exists(finalPath) Then
                ' Same name (same extension): swap the new copy in for the old in one step.
                File.SetAttributes(finalPath, FileAttributes.Normal)
                File.Replace(incomingPath, finalPath, Nothing)
            Else
                File.Move(incomingPath, finalPath)
            End If
            File.SetAttributes(finalPath, File.GetAttributes(finalPath) Or FileAttributes.Hidden)
            ' A different extension: the old copy is still there under its own name.
            If Not isSameNameAsOld Then DeleteOwnPictureFile()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Logger.Error("Could not copy the picture " & sourcePicturePath & " into Stage '" & _settings.Name & "'", ex)
            Try
                If File.Exists(incomingPath) Then File.Delete(incomingPath)
            Catch cleanupProblem As Exception When TypeOf cleanupProblem Is IOException OrElse TypeOf cleanupProblem Is UnauthorizedAccessException
                ' Harmless: ".stage-" files are never shown, and the next copy overwrites it.
            End Try
            Return Nothing
        End Try

        ' Same name as before (same extension): forget the decoded old picture.
        _decodedPicturePath = Nothing
        _decodedPicture = Nothing
        Return finalFileName
    End Function

    ''' <summary>
    ''' Deletes the Stage's own copy of its picture. Only ever this file: it's Stages' copy,
    ''' not the user's original, which is never touched.
    ''' </summary>
    Private Sub DeleteOwnPictureFile()
        If String.IsNullOrEmpty(_settings.BackgroundPictureFile) Then Return
        ' Only a ".stage-" name is ours; anything else in the settings is ignored, not deleted.
        If Not StageFolder.IsStageOwnFile(_settings.BackgroundPictureFile) Then Return

        Dim ownCopyPath As String = Path.Combine(_settings.FolderPath, Path.GetFileName(_settings.BackgroundPictureFile))
        Try
            If File.Exists(ownCopyPath) Then
                File.SetAttributes(ownCopyPath, FileAttributes.Normal)
                File.Delete(ownCopyPath)
            End If
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Logger.Warn("Could not delete the old background picture " & ownCopyPath, ex)
        End Try
        _decodedPicturePath = Nothing
        _decodedPicture = Nothing
    End Sub

#End Region

End Class
