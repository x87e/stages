' ============================================================================
'  StageKindOptionsDialog.xaml.vb
'  Options for a Live folder, Running tasks or Clipboard history Stage or tab.
'  Usage (see StageWindow.Kinds.vb):
'      Dim dialog As New StageKindOptionsDialog(tab) With {.Owner = someStage}
'      If dialog.ShowDialog().GetValueOrDefault() Then dialog.ApplyTo(tab)
'  Nothing changes until OK; the caller then saves the tab and refreshes it.
' ============================================================================
Imports Stages.Model
Imports Stages.Native

Partial Public Class StageKindOptionsDialog

    ' --- Titles and value labels ---
    Private Const LiveFolderTitle As String = "Live folder options"
    Private Const RunningTasksTitle As String = "Running tasks options"
    Private Const ClipboardTitle As String = "Clipboard history options"
    Private Const ChooseFolderTitle As String = "Choose the folder this Stage will show"
    Private Const OnlyThisFolderText As String = "This folder only"
    Private Const OneLevelText As String = "1 level down"
    ''' <summary>{0} = number of levels.</summary>
    Private Const LevelsFormat As String = "{0} levels down"
    Private Const ItemsFormat As String = "{0} items"
    Private Const EntriesFormat As String = "{0} entries"
    Private Const SecondsFormat As String = "{0} s"
    Private Const MegabytesFormat As String = "{0} MB"

    ' --- Slider steps ---
    Private Const MaximumItemsStep As Double = 50
    Private Const ClipboardEntriesStep As Double = 5
    Private Const ClipboardMegabytesStep As Double = 5

    ''' <summary>Which kind's options are shown.</summary>
    Private ReadOnly _kind As StageKind
    ''' <summary>The folder a Live folder will show (changed with "Change...").</summary>
    Private _chosenFolder As String

    ''' <param name="tab">The Stage or tab whose options to show; it is not changed until ApplyTo.</param>
    Public Sub New(tab As StageInfo)
        InitializeComponent()
        _kind = tab.Kind
        SetSliderLimits()

        Select Case _kind
            Case StageKind.LiveFolder
                Title = LiveFolderTitle
                LiveFolderPanel.Visibility = Visibility.Visible
                _chosenFolder = tab.SourceFolderPath
                FolderPathText.Text = _chosenFolder
                FolderPathText.ToolTip = _chosenFolder
                DepthSlider.Value = tab.LiveFolderDepth
                ShowFoldersCheckBox.IsChecked = tab.LiveFolderShowFolders
                LiveIncludeBox.Text = tab.LiveFolderIncludePatterns
                LiveExcludeBox.Text = tab.LiveFolderExcludePatterns
                MaximumItemsSlider.Value = tab.LiveFolderMaximumItems

            Case StageKind.RunningTasks
                Title = RunningTasksTitle
                RunningTasksPanel.Visibility = Visibility.Visible
                TasksIncludeBox.Text = tab.RunningTasksIncludePatterns
                TasksExcludeBox.Text = tab.RunningTasksExcludePatterns
                ShowMinimisedCheckBox.IsChecked = tab.RunningTasksShowMinimised
                IntervalSlider.Value = tab.RunningTasksIntervalSeconds

            Case StageKind.ClipboardHistory
                Title = ClipboardTitle
                ClipboardPanel.Visibility = Visibility.Visible
                CaptureTextCheckBox.IsChecked = tab.ClipboardCaptureText
                CaptureImagesCheckBox.IsChecked = tab.ClipboardCaptureImages
                CaptureFilesCheckBox.IsChecked = tab.ClipboardCaptureFiles
                MaximumEntriesSlider.Value = tab.ClipboardMaximumEntries
                MaximumMegabytesSlider.Value = tab.ClipboardMaximumMegabytes
        End Select
        UpdateValueLabels()
    End Sub

    ''' <summary>The native window now exists: give it a title bar matching the theme.</summary>
    Protected Overrides Sub OnSourceInitialized(e As EventArgs)
        MyBase.OnSourceInitialized(e)
        Services.ThemeManager.ApplyTitleBar(Me)
    End Sub

    ''' <summary>Writes the chosen options into <paramref name="tab"/> (call after OK), keeping them in range.</summary>
    Public Sub ApplyTo(tab As StageInfo)
        Select Case _kind
            Case StageKind.LiveFolder
                If Not String.IsNullOrWhiteSpace(_chosenFolder) Then tab.SourceFolderPath = _chosenFolder
                tab.LiveFolderDepth = SliderValue(DepthSlider)
                tab.LiveFolderShowFolders = ShowFoldersCheckBox.IsChecked.GetValueOrDefault()
                tab.LiveFolderIncludePatterns = LiveIncludeBox.Text.Trim()
                tab.LiveFolderExcludePatterns = LiveExcludeBox.Text.Trim()
                tab.LiveFolderMaximumItems = SliderValue(MaximumItemsSlider)

            Case StageKind.RunningTasks
                tab.RunningTasksIncludePatterns = TasksIncludeBox.Text.Trim()
                tab.RunningTasksExcludePatterns = TasksExcludeBox.Text.Trim()
                tab.RunningTasksShowMinimised = ShowMinimisedCheckBox.IsChecked.GetValueOrDefault()
                tab.RunningTasksIntervalSeconds = SliderValue(IntervalSlider)

            Case StageKind.ClipboardHistory
                tab.ClipboardCaptureText = CaptureTextCheckBox.IsChecked.GetValueOrDefault()
                tab.ClipboardCaptureImages = CaptureImagesCheckBox.IsChecked.GetValueOrDefault()
                tab.ClipboardCaptureFiles = CaptureFilesCheckBox.IsChecked.GetValueOrDefault()
                tab.ClipboardMaximumEntries = SliderValue(MaximumEntriesSlider)
                tab.ClipboardMaximumMegabytes = SliderValue(MaximumMegabytesSlider)
        End Select
        tab.Sanitize() ' belt and braces: every value back inside its limits
    End Sub

    ''' <summary>Each slider's range and step, from StageDefaults (one place for every limit).</summary>
    Private Sub SetSliderLimits()
        DepthSlider.Minimum = 0
        DepthSlider.Maximum = StageDefaults.MaximumLiveFolderDepth

        MaximumItemsSlider.Minimum = StageDefaults.MinimumLiveFolderMaximumItems
        MaximumItemsSlider.Maximum = StageDefaults.MaximumLiveFolderMaximumItems
        MaximumItemsSlider.TickFrequency = MaximumItemsStep

        IntervalSlider.Minimum = StageDefaults.MinimumRunningTasksIntervalSeconds
        IntervalSlider.Maximum = StageDefaults.MaximumRunningTasksIntervalSeconds

        MaximumEntriesSlider.Minimum = StageDefaults.MinimumClipboardMaximumEntries
        MaximumEntriesSlider.Maximum = StageDefaults.MaximumClipboardMaximumEntries
        MaximumEntriesSlider.TickFrequency = ClipboardEntriesStep

        MaximumMegabytesSlider.Minimum = StageDefaults.MinimumClipboardMaximumMegabytes
        MaximumMegabytesSlider.Maximum = StageDefaults.MaximumClipboardMaximumMegabytes
        MaximumMegabytesSlider.TickFrequency = ClipboardMegabytesStep
    End Sub

    ''' <summary>The text beside each slider ("2 levels down", "1000 items", "2 s"...).</summary>
    Private Sub UpdateValueLabels()
        ' ValueChanged can fire while the XAML is still being loaded, before the labels exist.
        If DepthValueText Is Nothing OrElse MaximumMegabytesValueText Is Nothing Then Return

        Dim depth As Integer = SliderValue(DepthSlider)
        Select Case depth
            Case 0
                DepthValueText.Text = OnlyThisFolderText
            Case 1
                DepthValueText.Text = OneLevelText
            Case Else
                DepthValueText.Text = String.Format(LevelsFormat, depth)
        End Select
        MaximumItemsValueText.Text = String.Format(ItemsFormat, SliderValue(MaximumItemsSlider))
        IntervalValueText.Text = String.Format(SecondsFormat, SliderValue(IntervalSlider))
        MaximumEntriesValueText.Text = String.Format(EntriesFormat, SliderValue(MaximumEntriesSlider))
        MaximumMegabytesValueText.Text = String.Format(MegabytesFormat, SliderValue(MaximumMegabytesSlider))
    End Sub

    Private Sub AnySlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        UpdateValueLabels()
    End Sub

    ''' <summary>A slider's value as a whole number (sliders snap to whole steps, but store Doubles).</summary>
    Private Shared Function SliderValue(sourceSlider As Slider) As Integer
        Return CInt(Math.Round(sourceSlider.Value))
    End Function

    ''' <summary>Live folder: pick a different folder to show (the Stage keeps its title).</summary>
    Private Sub ChangeFolderButton_Click(sender As Object, e As RoutedEventArgs)
        Dim pickedFolder As String = FolderPicker.ChooseFolder(New System.Windows.Interop.WindowInteropHelper(Me).Handle, ChooseFolderTitle)
        If pickedFolder Is Nothing Then Return ' cancelled
        _chosenFolder = pickedFolder
        FolderPathText.Text = pickedFolder
        FolderPathText.ToolTip = pickedFolder
    End Sub

    Private Sub OkButton_Click(sender As Object, e As RoutedEventArgs)
        ' Closes the dialog; ShowDialog() returns True.
        DialogResult = True
    End Sub

End Class
