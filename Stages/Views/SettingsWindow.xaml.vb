' ============================================================================
'  SettingsWindow.xaml.vb
'  Behaviour of the Settings window (layout in SettingsWindow.xaml).
'
'  How it works:
'    * The window edits AppSettingsStore.Current directly. After every change
'      it asks StageManager to re-apply the settings to every Stage (so the
'      change shows immediately), and starts a short countdown that writes
'      settings.xml once the user stops fiddling (sliders fire many changes
'      per second; the disk is written once).
'    * Only one Settings window exists at a time: ShowSettings() opens it, or
'      brings the open one to the front.
'    * While the controls are being filled in from the settings, their change
'      events are ignored (_isLoadingValues), so loading never counts as a change.
'
'  Map of this file:
'    Opening ......................... ShowSettings, constructor, page switching
'    Filling in the controls ......... LoadValuesIntoControls and helpers
'    General page .................... startup, hide/show shortcut, default background /
'                                      title height / minify
'    Appearance page ................. theme, colours and strengths
'    Items page ...................... file name extensions, icons
'    Desktop page .................... arranging Stages (snapping, gap, guides), hide desktop icons
'    About page ...................... folder buttons
'    Saving and closing .............. debounced save, Close
' ============================================================================
Imports System.Windows.Interop
Imports System.Windows.Threading
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports WinForms = System.Windows.Forms
Imports GdiDrawing = System.Drawing

Partial Public Class SettingsWindow

#Region "Constants"
    ''' <summary>Value labels next to sliders, e.g. "39%" and "35px".</summary>
    Private Const PercentLabelFormat As String = "{0}%"
    Private Const PixelLabelFormat As String = "{0}px"
    Private Const PointLabelFormat As String = "{0} pt"
    Private Const MillisecondLabelFormat As String = "{0} ms"
    ''' <summary>The duration slider moves in steps of this many milliseconds.</summary>
    Private Const MinifyAnimationSliderStep As Double = 25

    ' --- Stage font texts ---
    Private Const FontCaption As String = "Stage font"
    Private Const FontNotUsableText As String =
        "Windows can't use that font for Stages (only TrueType and OpenType fonts work). Please choose another."
    ''' <summary>The size shown in the font picker's preview (only the font family is used).</summary>
    Private Const FontPickerPreviewPoints As Single = 9.0F
    Private Const VersionLabelFormat As String = "Version {0}"

    ''' <summary>{0} = number of Stages that have their own background / title height.</summary>
    Private Const OwnBackdropCountFormat As String = "{0} Stage(s) use their own background."
    Private Const OwnTitleHeightCountFormat As String = "{0} Stage(s) use their own height."
    Private Const AllStagesFollowDefaultText As String = "Every Stage follows this default."

    ''' <summary>Text of the first extensions option; {0} = what Windows is doing right now.</summary>
    Private Const ExtensionsSameAsWindowsFormat As String = "Same as Windows (currently: {0})"
    Private Const WindowsHidesExtensionsText As String = "hidden"
    Private Const WindowsShowsExtensionsText As String = "shown"

    ''' <summary>Shown under "Start Stages when I sign in"; {0} = the Stages.exe Windows will start.</summary>
    Private Const StartupProgramFormat As String = "Starts: {0}"
    Private Const StartupProgramCurrentFormat As String = "Would start this copy: {0}"
    Private Const StartupChangeFailedText As String = "Windows' startup list couldn't be changed. See the log for details."
    Private Const StartupCaption As String = "Startup"

    ' --- Hide / show shortcut texts ---
    Private Const HotkeyOffText As String = "(no shortcut)"
    Private Const HotkeyRecordingText As String = "Press the keys..."
    Private Const HotkeyWorkingStatus As String = "Working: press it anywhere to hide or show all Stages."
    Private Const HotkeyOffStatus As String = "Off. The tray menu can still hide and show the Stages."
    Private Const HotkeyTakenStatus As String = "Another program is already using this shortcut, so it doesn't work. Choose a different one."
    Private Const HotkeyNeedsModifierStatus As String = "Add Ctrl, Alt or Win to the key, so ordinary typing can't trigger it."

    Private Const UseEverywhereCaption As String = "Use on every Stage"
    Private Const UseBackdropEverywhereQuestion As String =
        "Make every Stage use the default background?" & ControlChars.CrLf & ControlChars.CrLf &
        "Stages that picked their own background will switch to the default and follow it from now on."
    Private Const UseTitleHeightEverywhereQuestion As String =
        "Make every Stage use the default title bar height?" & ControlChars.CrLf & ControlChars.CrLf &
        "Stages that picked their own height will switch to the default and follow it from now on."

    ''' <summary>Wait this long after the last change before writing settings.xml.</summary>
    Private Shared ReadOnly SaveDebounceDelay As TimeSpan = TimeSpan.FromMilliseconds(600)
    ''' <summary>Explorer hides/shows the desktop icons a moment after being asked; re-read the state after this.</summary>
    Private Shared ReadOnly DesktopIconsRecheckDelay As TimeSpan = TimeSpan.FromMilliseconds(500)

    ' Positions of the pages in the navigation list (SettingsWindow.xaml).
    Private Const GeneralPageIndex As Integer = 0
#End Region

#Region "Fields"
    ''' <summary>The open Settings window, or Nothing. Ensures there is only ever one.</summary>
    Private Shared _openInstance As SettingsWindow

    ''' <summary>The page panels, in the same order as the entries in SectionList.</summary>
    Private ReadOnly _pages As StackPanel()
    Private ReadOnly _saveTimer As DispatcherTimer
    Private ReadOnly _desktopIconsRecheckTimer As DispatcherTimer
    ''' <summary>True while controls are being filled in from the settings (their events are ignored).</summary>
    Private _isLoadingValues As Boolean
#End Region

#Region "Opening and page switching"

    ''' <summary>
    ''' Opens the Settings window, or brings it to the front if it's already open.
    ''' Used by the tray icon and by the Stage menu.
    ''' </summary>
    Public Shared Sub ShowSettings()
        If _openInstance Is Nothing Then
            _openInstance = New SettingsWindow()
            _openInstance.Show()
        ElseIf _openInstance.WindowState = WindowState.Minimized Then
            _openInstance.WindowState = WindowState.Normal
        End If
        _openInstance.Activate()
    End Sub

    ''' <summary>Private: always open it through ShowSettings, so there's only one.</summary>
    Private Sub New()
        ' Builds the controls declared in SettingsWindow.xaml. Their change events can fire
        ' during this; they are ignored until the window is initialised (see IsReactingToChanges).
        InitializeComponent()

        ' Same order as the entries in SectionList (SettingsWindow.xaml).
        _pages = {GeneralPage, AppearancePage, ItemsPage, DesktopPage, BackupPage, InstallPage, AboutPage}
        ShowInstallState() ' Install page: portable or installed
        _saveTimer = New DispatcherTimer() With {.Interval = SaveDebounceDelay}
        AddHandler _saveTimer.Tick, AddressOf SaveTimer_Tick
        _desktopIconsRecheckTimer = New DispatcherTimer() With {.Interval = DesktopIconsRecheckDelay}
        AddHandler _desktopIconsRecheckTimer.Tick, AddressOf DesktopIconsRecheckTimer_Tick

        SetSliderLimits()
        LoadValuesIntoControls()
        SectionList.SelectedIndex = GeneralPageIndex
    End Sub

    ''' <summary>
    ''' The native window now exists: give it a light or dark title bar to match the theme.
    ''' </summary>
    Protected Overrides Sub OnSourceInitialized(e As EventArgs)
        MyBase.OnSourceInitialized(e)
        ThemeManager.ApplyTitleBar(Me)
    End Sub

    ''' <summary>Slider ranges come from StageDefaults, so they always match what Sanitize() allows.</summary>
    Private Sub SetSliderLimits()
        _isLoadingValues = True
        Try
            DefaultTitleHeightSlider.Minimum = StageDefaults.MinimumTitleHeight
            DefaultTitleHeightSlider.Maximum = StageDefaults.MaximumTitleHeight

            For Each fullRangeSlider As Slider In {StageTintOpacitySlider, AcrylicTintOpacitySlider}
                fullRangeSlider.Minimum = StageDefaults.MinimumPercent
                fullRangeSlider.Maximum = StageDefaults.MaximumPercent
            Next
            For Each detailSlider As Slider In {AcrylicSheenSlider, AcrylicGrainSlider}
                detailSlider.Minimum = StageDefaults.MinimumPercent
                detailSlider.Maximum = StageDefaults.MaximumAcrylicDetailPercent
            Next
            For Each frameStrengthSlider As Slider In {TitleBarOpacitySlider, BorderOpacitySlider}
                frameStrengthSlider.Minimum = StageDefaults.MinimumPercent
                frameStrengthSlider.Maximum = StageDefaults.MaximumPercent
            Next
            MinifyAnimationSlider.Minimum = StageDefaults.MinimumMinifyAnimationMilliseconds
            MinifyAnimationSlider.Maximum = StageDefaults.MaximumMinifyAnimationMilliseconds
            MinifyAnimationSlider.TickFrequency = MinifyAnimationSliderStep
            MinifyAnimationSlider.IsSnapToTickEnabled = True
            LabelFontSizeSlider.Minimum = StageDefaults.MinimumLabelFontSizePoints
            LabelFontSizeSlider.Maximum = StageDefaults.MaximumLabelFontSizePoints
            TitleFontSizeSlider.Minimum = StageDefaults.MinimumTitleFontSizePoints
            TitleFontSizeSlider.Maximum = StageDefaults.MaximumTitleFontSizePoints
            BorderThicknessSlider.Minimum = 0
            BorderThicknessSlider.Maximum = StageDefaults.MaximumBorderThickness
            StageGapSlider.Minimum = StageDefaults.MinimumStageGapPixels
            StageGapSlider.Maximum = StageDefaults.MaximumStageGapPixels
        Finally
            _isLoadingValues = False
        End Try
    End Sub

    ''' <summary>Shows the page matching the selected navigation entry and scrolls back to its top.</summary>
    Private Sub SectionList_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If _pages Is Nothing Then Return ' still being built
        For pageIndex As Integer = 0 To _pages.Length - 1
            _pages(pageIndex).Visibility = If(pageIndex = SectionList.SelectedIndex, Visibility.Visible, Visibility.Collapsed)
        Next
        PageScroller.ScrollToTop()
    End Sub

    ''' <summary>
    ''' Coming back to the window: refresh things that can change elsewhere (desktop icons
    ''' toggled from the desktop, Stages given their own background from their menus...).
    ''' </summary>
    Private Sub Window_Activated(sender As Object, e As EventArgs)
        RefreshStatusTexts()
    End Sub

    ''' <summary>
    ''' True when a control's change event should be acted on: not while the XAML is still
    ''' being built (some controls don't exist yet) and not while values are being loaded.
    ''' </summary>
    Private Function IsReactingToChanges() As Boolean
        Return IsInitialized AndAlso _pages IsNot Nothing AndAlso Not _isLoadingValues
    End Function

#End Region

#Region "Filling in the controls"

    ''' <summary>Sets every control from AppSettingsStore.Current, without treating it as a change.</summary>
    Private Sub LoadValuesIntoControls()
        Dim currentSettings As AppSettings = AppSettingsStore.Current
        _isLoadingValues = True
        Try
            ' General
            Select Case currentSettings.DefaultBackdrop
                Case BackdropStyle.Acrylic
                    AcrylicRadio.IsChecked = True
                Case BackdropStyle.Translucent
                    TranslucentRadio.IsChecked = True
                Case Else
                    BlurRadio.IsChecked = True
            End Select
            DefaultTitleHeightSlider.Value = currentSettings.DefaultTitleHeight
            NewStagesMinifyCheckBox.IsChecked = currentSettings.NewStagesMinify
            TitleClickRollsUpCheckBox.IsChecked = currentSettings.TitleClickRollsUp
            ShowTrayIconCheckBox.IsChecked = currentSettings.ShowTrayIcon
            AnimateMinifyCheckBox.IsChecked = currentSettings.AnimateMinify
            Select Case currentSettings.MinifyOpenDirection
                Case StageOpenDirection.Up : OpenUpRadio.IsChecked = True
                Case StageOpenDirection.Automatic : OpenAutomaticRadio.IsChecked = True
                Case Else : OpenDownRadio.IsChecked = True
            End Select
            MinifyAnimationSlider.Value = currentSettings.MinifyAnimationMilliseconds
            MinifyAnimationSlider.IsEnabled = currentSettings.AnimateMinify
            ' Windows' own "Animation effects" switch wins over ours; say so when it's off.
            WindowsAnimationsOffText.Visibility =
                If(SystemParameters.ClientAreaAnimation, Visibility.Collapsed, Visibility.Visible)

            ' Desktop: arranging Stages
            Select Case currentSettings.StageSnapping
                Case StageSnapMode.Full
                    SnapFullRadio.IsChecked = True
                Case StageSnapMode.Off
                    SnapOffRadio.IsChecked = True
                Case Else
                    SnapBesideRadio.IsChecked = True
            End Select
            PreventOverlapCheckBox.IsChecked = currentSettings.PreventStageOverlap
            SnapSideBySideOnlyCheckBox.IsChecked = currentSettings.SnapSideBySideOnly
            ShowSnapGuidesCheckBox.IsChecked = currentSettings.ShowSnapGuides
            StageGapSlider.Value = currentSettings.StageGapPixels

            ' Items
            Select Case currentSettings.FileExtensions
                Case FileExtensionDisplay.AlwaysShow
                    ExtensionsShowRadio.IsChecked = True
                Case FileExtensionDisplay.AlwaysHide
                    ExtensionsHideRadio.IsChecked = True
                Case Else
                    ExtensionsSameAsWindowsRadio.IsChecked = True
            End Select
            Select Case currentSettings.FolderDelete
                Case FolderDeleteMode.SameAsWindows
                    FolderDeleteWindowsRadio.IsChecked = True
                Case FolderDeleteMode.Permanent
                    FolderDeletePermanentRadio.IsChecked = True
                Case Else
                    FolderDeleteRecycleRadio.IsChecked = True
            End Select

            ' Appearance
            Select Case currentSettings.Theme
                Case AppTheme.Light
                    ThemeLightRadio.IsChecked = True
                Case AppTheme.Dark
                    ThemeDarkRadio.IsChecked = True
                Case Else
                    ThemeSystemRadio.IsChecked = True
            End Select
            MenuThemeUnavailableText.Visibility =
                If(DesktopIntegration.CanThemeMenus(), Visibility.Collapsed, Visibility.Visible)
            StageTintOpacitySlider.Value = currentSettings.StageTintOpacityPercent
            AcrylicTintOpacitySlider.Value = currentSettings.AcrylicTintOpacityPercent
            AcrylicSheenSlider.Value = currentSettings.AcrylicSheenOpacityPercent
            AcrylicGrainSlider.Value = currentSettings.AcrylicGrainOpacityPercent

            ' Appearance: Stage text and frame
            StageFontNameText.Text = currentSettings.StageFontFamily
            StageFontNameText.FontFamily = New FontFamily(currentSettings.StageFontFamily)
            LabelFontSizeSlider.Value = currentSettings.LabelFontSizePoints
            Dim isAutomaticTitleSize As Boolean = (currentSettings.TitleFontSizePoints = StageDefaults.AutomaticTitleFontSize)
            AutomaticTitleSizeCheckBox.IsChecked = isAutomaticTitleSize
            ' With Automatic on, the slider shows the default height's automatic size, greyed out.
            TitleFontSizeSlider.Value = If(isAutomaticTitleSize,
                                           Math.Floor(currentSettings.DefaultTitleHeight / 2.0),
                                           currentSettings.TitleFontSizePoints)
            TitleFontSizeSlider.IsEnabled = Not isAutomaticTitleSize
            TitleBarOpacitySlider.Value = currentSettings.TitleBarOpacityPercent
            BorderThicknessSlider.Value = currentSettings.BorderThickness
            BorderOpacitySlider.Value = currentSettings.BorderOpacityPercent
            If currentSettings.CornerStyle = StageCornerStyle.Square Then
                SquareCornersRadio.IsChecked = True
            Else
                RoundedCornersRadio.IsChecked = True
            End If
            Select Case currentSettings.TitleAlignment
                Case StageTitleAlignment.Left : TitleLeftRadio.IsChecked = True
                Case StageTitleAlignment.Right : TitleRightRadio.IsChecked = True
                Case Else : TitleCentreRadio.IsChecked = True
            End Select
            Select Case currentSettings.TabStyle
                Case StageTabStyle.Shaded : TabShadedRadio.IsChecked = True
                Case StageTabStyle.Outlined : TabOutlinedRadio.IsChecked = True
                Case StageTabStyle.Clear : TabClearRadio.IsChecked = True
                Case Else : TabUnderlineRadio.IsChecked = True
            End Select
            CornersUnavailableText.Visibility =
                If(DesktopIntegration.CanRoundCorners(), Visibility.Collapsed, Visibility.Visible)

            ' About
            VersionText.Text = String.Format(VersionLabelFormat, GetType(SettingsWindow).Assembly.GetName().Version.ToString())
            DataFolderText.Text = AppPaths.DataFolder
            LogsFolderText.Text = AppPaths.LogsFolder
            SettingsFileText.Text = AppPaths.SettingsFilePath
        Finally
            _isLoadingValues = False
        End Try

        UpdateValueLabels()
        UpdateColorButtons()
        RefreshStatusTexts()
    End Sub

    ''' <summary>The "39%" / "35px" labels beside the sliders.</summary>
    Private Sub UpdateValueLabels()
        DefaultTitleHeightValueText.Text = String.Format(PixelLabelFormat, SliderValue(DefaultTitleHeightSlider))
        StageTintOpacityValueText.Text = String.Format(PercentLabelFormat, SliderValue(StageTintOpacitySlider))
        AcrylicTintOpacityValueText.Text = String.Format(PercentLabelFormat, SliderValue(AcrylicTintOpacitySlider))
        AcrylicSheenValueText.Text = String.Format(PercentLabelFormat, SliderValue(AcrylicSheenSlider))
        AcrylicGrainValueText.Text = String.Format(PercentLabelFormat, SliderValue(AcrylicGrainSlider))
        LabelFontSizeValueText.Text = String.Format(PointLabelFormat, SliderValue(LabelFontSizeSlider))
        TitleFontSizeValueText.Text = String.Format(PointLabelFormat, SliderValue(TitleFontSizeSlider))
        TitleBarOpacityValueText.Text = String.Format(PercentLabelFormat, SliderValue(TitleBarOpacitySlider))
        BorderThicknessValueText.Text = String.Format(PixelLabelFormat, SliderValue(BorderThicknessSlider))
        BorderOpacityValueText.Text = String.Format(PercentLabelFormat, SliderValue(BorderOpacitySlider))
        MinifyAnimationValueText.Text = String.Format(MillisecondLabelFormat, SliderValue(MinifyAnimationSlider))
        StageGapValueText.Text = String.Format(PixelLabelFormat, SliderValue(StageGapSlider))
    End Sub

    ''' <summary>The colour swatches and "#RRGGBB" texts on the colour buttons.</summary>
    Private Sub UpdateColorButtons()
        Dim currentSettings As AppSettings = AppSettingsStore.Current
        ShowColorOnButton(currentSettings.StageTintColor, StageTintSwatch, StageTintColorText)
        ShowColorOnButton(currentSettings.AcrylicTintColor, AcrylicTintSwatch, AcrylicTintColorText)
        ShowColorOnButton(currentSettings.TitleTextColor, TitleTextSwatch, TitleTextColorText)
        ' Automatic shows the colour it works out to for the default background, labelled "Automatic".
        ShowColorOnButton(currentSettings.EffectiveTabAccentColor(currentSettings.DefaultBackdrop), TabAccentSwatch, TabAccentColorText)
        If currentSettings.IsTabAccentAutomatic() Then TabAccentColorText.Text = AutomaticColorText
        TabAccentAutomaticButton.IsEnabled = Not currentSettings.IsTabAccentAutomatic()
        ShowColorOnButton(currentSettings.LabelTextColor, LabelTextSwatch, LabelTextColorText)
        ShowColorOnButton(currentSettings.TitleBarColor, TitleBarSwatch, TitleBarColorText)
        ShowColorOnButton(currentSettings.BorderColor, BorderSwatch, BorderColorText)
        ShowColorOnButton(currentSettings.SnapGuideColor, SnapGuideSwatch, SnapGuideColorText)
    End Sub

    ''' <summary>Paints a swatch in the (fully opaque) colour and writes its "#RRGGBB" next to it.</summary>
    Private Shared Sub ShowColorOnButton(rgbText As String, swatch As Border, colorLabel As TextBlock)
        Const SolidPercent As Integer = 100
        swatch.Background = New SolidColorBrush(ColorText.ToColor(rgbText, SolidPercent))
        colorLabel.Text = rgbText
    End Sub

    ''' <summary>
    ''' Texts that depend on things outside this window: how many Stages have their own
    ''' choices, whether transparency effects are on, and whether desktop icons are hidden.
    ''' </summary>
    Private Sub RefreshStatusTexts()
        Dim ownBackdropCount As Integer = StageManager.Instance.CountStagesWithOwnBackdrop()
        OwnBackdropCountText.Text = If(ownBackdropCount = 0, AllStagesFollowDefaultText,
                                       String.Format(OwnBackdropCountFormat, ownBackdropCount))
        UseBackdropEverywhereButton.IsEnabled = ownBackdropCount > 0

        Dim ownTitleHeightCount As Integer = StageManager.Instance.CountStagesWithOwnTitleHeight()
        OwnTitleHeightCountText.Text = If(ownTitleHeightCount = 0, AllStagesFollowDefaultText,
                                          String.Format(OwnTitleHeightCountFormat, ownTitleHeightCount))
        UseTitleHeightEverywhereButton.IsEnabled = ownTitleHeightCount > 0

        TransparencyOffWarning.Visibility =
            If(DesktopIntegration.AreTransparencyEffectsEnabled(), Visibility.Collapsed, Visibility.Visible)

        ' Explorer's own extensions setting may have been changed while this window was away.
        Dim windowsState As String = If(DesktopIntegration.IsWindowsHidingFileExtensions(),
                                        WindowsHidesExtensionsText, WindowsShowsExtensionsText)
        ExtensionsSameAsWindowsRadio.Content = String.Format(ExtensionsSameAsWindowsFormat, windowsState)

        RefreshDesktopIconsCheckBox()
        RefreshStartupStatus()
        ShowCurrentHotkey()
    End Sub

    ''' <summary>Shows the saved shortcut in its box, and whether Windows is delivering it.</summary>
    Private Sub ShowCurrentHotkey()
        Dim currentSettings As AppSettings = AppSettingsStore.Current
        Dim isOff As Boolean = (currentSettings.ToggleStagesHotkeyKey = Key.None)
        HotkeyBox.Text = If(isOff, HotkeyOffText,
                            GlobalHotkey.DescribeShortcut(currentSettings.ToggleStagesHotkeyModifiers, currentSettings.ToggleStagesHotkeyKey))
        If isOff Then
            HotkeyStatusText.Text = HotkeyOffStatus
        ElseIf GlobalHotkey.Instance.IsRegistered OrElse HotkeyBox.IsKeyboardFocused Then
            HotkeyStatusText.Text = HotkeyWorkingStatus
        Else
            HotkeyStatusText.Text = HotkeyTakenStatus
        End If
    End Sub

    ''' <summary>
    ''' Ticks "Start Stages when I sign in" to match Windows (it can also be changed in
    ''' Windows' Startup apps list), and shows which Stages.exe would be started.
    ''' </summary>
    Private Sub RefreshStartupStatus()
        Dim startsWithWindows As Boolean = StartupRegistration.IsEnabled()
        Dim registeredPath As String = StartupRegistration.RegisteredProgramPath()
        Dim currentPath As String = StartupRegistration.CurrentProgramPath

        _isLoadingValues = True
        Try
            StartWithWindowsCheckBox.IsChecked = startsWithWindows
        Finally
            _isLoadingValues = False
        End Try

        If startsWithWindows AndAlso registeredPath IsNot Nothing Then
            StartupProgramText.Text = String.Format(StartupProgramFormat, registeredPath)
        Else
            StartupProgramText.Text = String.Format(StartupProgramCurrentFormat, currentPath)
        End If

        Dim pointsElsewhere As Boolean = startsWithWindows AndAlso registeredPath IsNot Nothing AndAlso
            Not String.Equals(registeredPath, currentPath, StringComparison.OrdinalIgnoreCase)
        StartupPathMismatchWarning.Visibility = If(pointsElsewhere, Visibility.Visible, Visibility.Collapsed)
    End Sub

    ''' <summary>Ticks "Hide desktop icons" to match the desktop, or greys it out if Explorer can't be found.</summary>
    Private Sub RefreshDesktopIconsCheckBox()
        Dim iconsHidden As Boolean? = DesktopIntegration.AreDesktopIconsHidden()
        _isLoadingValues = True
        Try
            HideDesktopIconsCheckBox.IsEnabled = iconsHidden.HasValue
            HideDesktopIconsCheckBox.IsChecked = iconsHidden.GetValueOrDefault()
        Finally
            _isLoadingValues = False
        End Try
        DesktopIconsUnavailableText.Visibility = If(iconsHidden.HasValue, Visibility.Collapsed, Visibility.Visible)
    End Sub

    ''' <summary>A slider's value as a whole number (sliders snap to whole steps, but store Doubles).</summary>
    Private Shared Function SliderValue(sourceSlider As Slider) As Integer
        Return CInt(Math.Round(sourceSlider.Value))
    End Function

#End Region

#Region "General page"

    ''' <summary>One of the three "Default background" options was chosen.</summary>
    Private Sub DefaultBackdropRadio_Checked(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return

        Dim chosenBackdrop As BackdropStyle = BackdropStyle.Blur
        If sender Is AcrylicRadio Then
            chosenBackdrop = BackdropStyle.Acrylic
        ElseIf sender Is TranslucentRadio Then
            chosenBackdrop = BackdropStyle.Translucent
        End If

        AppSettingsStore.Current.DefaultBackdrop = chosenBackdrop
        OnSettingChanged()
    End Sub

    Private Sub DefaultTitleHeightSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        If Not IsReactingToChanges() Then Return
        AppSettingsStore.Current.DefaultTitleHeight = SliderValue(DefaultTitleHeightSlider)
        UpdateValueLabels()
        OnSettingChanged()
    End Sub

    ''' <summary>Animate minified Stages or not. Read each time a Stage opens or closes, so nothing needs re-applying.</summary>
    ''' <summary>
    ''' Minified Stages > Open: downwards, upwards or Automatic. Read each time a Stage opens,
    ''' so nothing needs re-applying; a Stage that's open right now keeps its way until it shuts.
    ''' </summary>
    Private Sub OpenDirectionRadio_Checked(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        Dim chosenDirection As StageOpenDirection = StageOpenDirection.Down
        If sender Is OpenUpRadio Then chosenDirection = StageOpenDirection.Up
        If sender Is OpenAutomaticRadio Then chosenDirection = StageOpenDirection.Automatic
        AppSettingsStore.Current.MinifyOpenDirection = chosenDirection
        ScheduleSave()
    End Sub

    Private Sub AnimateMinifyCheckBox_Click(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        Dim isAnimated As Boolean = AnimateMinifyCheckBox.IsChecked.GetValueOrDefault()
        AppSettingsStore.Current.AnimateMinify = isAnimated
        MinifyAnimationSlider.IsEnabled = isAnimated
        ScheduleSave()
    End Sub

    ''' <summary>How long the roll open / shut takes.</summary>
    Private Sub MinifyAnimationSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        If Not IsReactingToChanges() Then Return
        AppSettingsStore.Current.MinifyAnimationMilliseconds = SliderValue(MinifyAnimationSlider)
        UpdateValueLabels()
        ScheduleSave()
    End Sub

    ''' <summary>
    ''' Title bar > "Click the title bar to roll a Stage up". Read at each click, so nothing
    ''' needs re-applying. A Stage already rolled up can always be clicked down again.
    ''' </summary>
    Private Sub TitleClickRollsUpCheckBox_Click(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        AppSettingsStore.Current.TitleClickRollsUp = TitleClickRollsUpCheckBox.IsChecked.GetValueOrDefault()
        ScheduleSave()
    End Sub

    Private Sub NewStagesMinifyCheckBox_Click(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        AppSettingsStore.Current.NewStagesMinify = NewStagesMinifyCheckBox.IsChecked.GetValueOrDefault()
        ScheduleSave() ' only affects Stages created later, so nothing to re-apply now
    End Sub

    ''' <summary>"Start Stages when I sign in to Windows" was clicked: change Windows' startup list now.</summary>
    Private Sub StartWithWindowsCheckBox_Click(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        If Not StartupRegistration.SetEnabled(StartWithWindowsCheckBox.IsChecked.GetValueOrDefault()) Then
            MessageBox.Show(Me, StartupChangeFailedText, StartupCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
        End If
        RefreshStartupStatus() ' show what Windows really has now
    End Sub

#Region "Install page"

    ''' <summary>
    ''' Install page: the explanation, choices and button for a portable copy, or just "Stages
    ''' is installed" for the installed one.
    ''' </summary>
    Private Sub ShowInstallState()
        Dim isInstalledCopy As Boolean = PortableInstaller.IsRunningInstalledCopy()
        InstalledCard.Visibility = If(isInstalledCopy, Visibility.Visible, Visibility.Collapsed)
        PortableInstallPanel.Visibility = If(isInstalledCopy, Visibility.Collapsed, Visibility.Visible)
        InstalledFolderText.Text = "Installed in: " & AppPaths.AppFolder
        If isInstalledCopy Then Return

        PortableCopyPathText.Text = "It goes to the Recycle Bin: " & PortableInstaller.RunningProgramPath
        If PortableInstaller.IsAlreadyInstalled() Then
            Dim installedVersion As String = If(PortableInstaller.InstalledVersion(), "an older version")
            ReplacesInstalledText.Text = "Stages is already installed on this PC (version " & installedVersion &
                                         "). Installing replaces it with this copy (version " &
                                         GetType(SettingsWindow).Assembly.GetName().Version.ToString() & ")."
            ReplacesInstalledText.Visibility = Visibility.Visible
        End If
    End Sub

    ''' <summary>
    ''' Install: saves everything, installs, then hands over to the installed copy (which
    ''' finishes off - see PortableInstaller). If copying fails nothing has changed and the
    ''' reason is shown; this copy keeps running.
    ''' </summary>
    Private Sub InstallButton_Click(sender As Object, e As RoutedEventArgs)
        InstallButton.IsEnabled = False
        InstallStatusText.Visibility = Visibility.Collapsed
        Dim choices As New InstallChoices() With {
            .AddToStartMenu = InstallStartMenuCheckBox.IsChecked.GetValueOrDefault(),
            .StartWithWindows = InstallStartWithWindowsCheckBox.IsChecked.GetValueOrDefault(),
            .RemovePortableCopy = InstallRemovePortableCheckBox.IsChecked.GetValueOrDefault()
        }

        ' Everything waiting to be saved is written first, so the installed copy starts from it.
        If _saveTimer.IsEnabled Then _saveTimer.Stop()
        AppSettingsStore.Save()
        StageManager.Instance.SaveAllStagesNow()

        Dim result As InstallResult = PortableInstaller.Install(choices)
        If Not result.ProgramCopied Then
            ShowInstallProblem(result.Problem) ' the copy itself failed: stay as we are
            Return
        End If
        If result.Problem IsNot Nothing Then
            ' Copied, but a convenience part failed - say so, then carry on.
            MessageBox.Show(Me, result.Problem, "Install Stages", MessageBoxButton.OK, MessageBoxImage.Warning)
        End If
        If Not PortableInstaller.RestartIntoInstalledCopy(choices.RemovePortableCopy) Then
            ShowInstallProblem("Stages was installed, but the installed copy couldn't be started. Start it from the Start menu, or from " & PortableInstaller.InstalledProgramPath & ".")
        End If
    End Sub

    ''' <summary>Shows what went wrong under the Install button, and lets it be tried again.</summary>
    Private Sub ShowInstallProblem(problem As String)
        InstallStatusText.Text = problem
        InstallStatusText.Visibility = Visibility.Visible
        InstallButton.IsEnabled = True
    End Sub

#End Region

    ''' <summary>Notification area > "Show the Stages icon in the notification area": applies straight away.</summary>
    Private Sub ShowTrayIconCheckBox_Click(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        AppSettingsStore.Current.ShowTrayIcon = ShowTrayIconCheckBox.IsChecked.GetValueOrDefault()
        Program.ApplyTrayIconSetting()
        ScheduleSave()
    End Sub

    ''' <summary>
    ''' Notification area > Quit Stages. Saves any setting still waiting to be written first,
    ''' then quits once this click has finished (queued, like the tray menu's Quit).
    ''' </summary>
    Private Sub QuitStagesButton_Click(sender As Object, e As RoutedEventArgs)
        Logger.Info("Quit chosen in Settings")
        If _saveTimer.IsEnabled Then
            _saveTimer.Stop()
            AppSettingsStore.Save()
        End If
        Dispatcher.BeginInvoke(New Action(AddressOf StageManager.Instance.ShutDown))
    End Sub

    ''' <summary>"Use on every Stage" for the background: clears every Stage's own choice, after asking.</summary>
    Private Sub UseBackdropEverywhereButton_Click(sender As Object, e As RoutedEventArgs)
        If MessageBox.Show(Me, UseBackdropEverywhereQuestion, UseEverywhereCaption,
                           MessageBoxButton.YesNo, MessageBoxImage.Question) <> MessageBoxResult.Yes Then
            Return
        End If
        StageManager.Instance.UseDefaultBackdropOnAllStages()
        RefreshStatusTexts()
    End Sub

    ''' <summary>"Use on every Stage" for the title height: clears every Stage's own height, after asking.</summary>
    Private Sub UseTitleHeightEverywhereButton_Click(sender As Object, e As RoutedEventArgs)
        If MessageBox.Show(Me, UseTitleHeightEverywhereQuestion, UseEverywhereCaption,
                           MessageBoxButton.YesNo, MessageBoxImage.Question) <> MessageBoxResult.Yes Then
            Return
        End If
        StageManager.Instance.UseDefaultTitleHeightOnAllStages()
        RefreshStatusTexts()
    End Sub

#End Region

#Region "Appearance page"

    ''' <summary>One of the three Theme options was chosen: re-colour windows and menus now.</summary>
    Private Sub ThemeRadio_Checked(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return

        Dim chosenTheme As AppTheme = AppTheme.System
        If sender Is ThemeLightRadio Then
            chosenTheme = AppTheme.Light
        ElseIf sender Is ThemeDarkRadio Then
            chosenTheme = AppTheme.Dark
        End If

        AppSettingsStore.Current.Theme = chosenTheme
        ThemeManager.Apply()
        ScheduleSave() ' the Stages themselves aren't themed, so nothing to re-apply to them
    End Sub

    ''' <summary>Any of the four Appearance sliders moved.</summary>
    Private Sub AppearanceSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        If Not IsReactingToChanges() Then Return

        Dim currentSettings As AppSettings = AppSettingsStore.Current
        currentSettings.StageTintOpacityPercent = SliderValue(StageTintOpacitySlider)
        currentSettings.AcrylicTintOpacityPercent = SliderValue(AcrylicTintOpacitySlider)
        currentSettings.AcrylicSheenOpacityPercent = SliderValue(AcrylicSheenSlider)
        currentSettings.AcrylicGrainOpacityPercent = SliderValue(AcrylicGrainSlider)

        UpdateValueLabels()
        OnSettingChanged()
    End Sub

    Private Sub StageTintColorButton_Click(sender As Object, e As RoutedEventArgs)
        Dim pickedColor As String = PickColor(AppSettingsStore.Current.StageTintColor)
        If pickedColor Is Nothing Then Return ' cancelled
        AppSettingsStore.Current.StageTintColor = pickedColor
        UpdateColorButtons()
        OnSettingChanged()
    End Sub

    Private Sub AcrylicTintColorButton_Click(sender As Object, e As RoutedEventArgs)
        Dim pickedColor As String = PickColor(AppSettingsStore.Current.AcrylicTintColor)
        If pickedColor Is Nothing Then Return ' cancelled
        AppSettingsStore.Current.AcrylicTintColor = pickedColor
        UpdateColorButtons()
        OnSettingChanged()
    End Sub

    ''' <summary>Any of the Stage text / frame sliders moved.</summary>
    Private Sub StageTextSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        If Not IsReactingToChanges() Then Return

        Dim currentSettings As AppSettings = AppSettingsStore.Current
        currentSettings.LabelFontSizePoints = SliderValue(LabelFontSizeSlider)
        If Not AutomaticTitleSizeCheckBox.IsChecked.GetValueOrDefault() Then
            currentSettings.TitleFontSizePoints = SliderValue(TitleFontSizeSlider)
        End If
        currentSettings.TitleBarOpacityPercent = SliderValue(TitleBarOpacitySlider)
        currentSettings.BorderThickness = SliderValue(BorderThicknessSlider)
        currentSettings.BorderOpacityPercent = SliderValue(BorderOpacitySlider)

        UpdateValueLabels()
        OnSettingChanged()
    End Sub

    ''' <summary>Automatic title size on: follow the title bar height. Off: use the slider's size.</summary>
    Private Sub AutomaticTitleSizeCheckBox_Click(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        Dim isAutomatic As Boolean = AutomaticTitleSizeCheckBox.IsChecked.GetValueOrDefault()
        AppSettingsStore.Current.TitleFontSizePoints = If(isAutomatic, StageDefaults.AutomaticTitleFontSize, SliderValue(TitleFontSizeSlider))
        TitleFontSizeSlider.IsEnabled = Not isAutomatic
        OnSettingChanged()
    End Sub

    ''' <summary>Rounded or square corners for every Stage.</summary>
    Private Sub CornerRadio_Checked(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        AppSettingsStore.Current.CornerStyle = If(sender Is SquareCornersRadio, StageCornerStyle.Square, StageCornerStyle.Rounded)
        OnSettingChanged()
    End Sub

    ''' <summary>Title and tabs > Title position: left, centre or right, for every Stage.</summary>
    Private Sub TitleAlignmentRadio_Checked(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        Dim chosenAlignment As StageTitleAlignment = StageTitleAlignment.Center
        If sender Is TitleLeftRadio Then chosenAlignment = StageTitleAlignment.Left
        If sender Is TitleRightRadio Then chosenAlignment = StageTitleAlignment.Right
        AppSettingsStore.Current.TitleAlignment = chosenAlignment
        OnSettingChanged()
    End Sub

    ''' <summary>Title and tabs > Open tab: how the open tab is marked, for every Stage.</summary>
    Private Sub TabStyleRadio_Checked(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        Dim chosenStyle As StageTabStyle = StageTabStyle.Underline
        If sender Is TabShadedRadio Then chosenStyle = StageTabStyle.Shaded
        If sender Is TabOutlinedRadio Then chosenStyle = StageTabStyle.Outlined
        If sender Is TabClearRadio Then chosenStyle = StageTabStyle.Clear
        AppSettingsStore.Current.TabStyle = chosenStyle
        UpdateColorButtons() ' an Automatic tab colour depends on the look
        OnSettingChanged()
    End Sub

    ''' <summary>What the tab colour button says while no colour has been picked.</summary>
    Private Const AutomaticColorText As String = "Automatic"

    ''' <summary>Title and tabs > Automatic: forget the picked tab colour.</summary>
    Private Sub TabAccentAutomaticButton_Click(sender As Object, e As RoutedEventArgs)
        AppSettingsStore.Current.TabAccentColor = StageDefaults.DefaultTabAccentColor
        UpdateColorButtons()
        OnSettingChanged()
    End Sub

    ''' <summary>Title and tabs > Tab colour: the underline / outline colour.</summary>
    Private Sub TabAccentColorButton_Click(sender As Object, e As RoutedEventArgs)
        ' The colour dialog opens on what's shown now (Automatic's colour, if that's in use).
        Dim currentSettings As AppSettings = AppSettingsStore.Current
        Dim pickedColor As String = PickColor(currentSettings.EffectiveTabAccentColor(currentSettings.DefaultBackdrop))
        If pickedColor Is Nothing Then Return ' cancelled
        AppSettingsStore.Current.TabAccentColor = pickedColor
        UpdateColorButtons()
        OnSettingChanged()
    End Sub

    Private Sub TitleTextColorButton_Click(sender As Object, e As RoutedEventArgs)
        Dim pickedColor As String = PickColor(AppSettingsStore.Current.TitleTextColor)
        If pickedColor Is Nothing Then Return ' cancelled
        AppSettingsStore.Current.TitleTextColor = pickedColor
        UpdateColorButtons()
        OnSettingChanged()
    End Sub

    Private Sub LabelTextColorButton_Click(sender As Object, e As RoutedEventArgs)
        Dim pickedColor As String = PickColor(AppSettingsStore.Current.LabelTextColor)
        If pickedColor Is Nothing Then Return ' cancelled
        AppSettingsStore.Current.LabelTextColor = pickedColor
        UpdateColorButtons()
        OnSettingChanged()
    End Sub

    Private Sub TitleBarColorButton_Click(sender As Object, e As RoutedEventArgs)
        Dim pickedColor As String = PickColor(AppSettingsStore.Current.TitleBarColor)
        If pickedColor Is Nothing Then Return ' cancelled
        AppSettingsStore.Current.TitleBarColor = pickedColor
        UpdateColorButtons()
        OnSettingChanged()
    End Sub

    Private Sub BorderColorButton_Click(sender As Object, e As RoutedEventArgs)
        Dim pickedColor As String = PickColor(AppSettingsStore.Current.BorderColor)
        If pickedColor Is Nothing Then Return ' cancelled
        AppSettingsStore.Current.BorderColor = pickedColor
        UpdateColorButtons()
        OnSettingChanged()
    End Sub

    ''' <summary>
    ''' Shows Windows' font picker (from Windows Forms, as WPF has none). Only the chosen
    ''' font family is used; the sizes are set with the sliders.
    ''' </summary>
    Private Sub StageFontButton_Click(sender As Object, e As RoutedEventArgs)
        Dim currentSettings As AppSettings = AppSettingsStore.Current
        Dim chosenFamilyName As String = Nothing

        Using fontPicker As New WinForms.FontDialog() With {
            .ShowEffects = False,
            .ShowColor = False,
            .FontMustExist = True,
            .AllowVerticalFonts = False
        }
            Try
                fontPicker.Font = New GdiDrawing.Font(currentSettings.StageFontFamily, FontPickerPreviewPoints)
            Catch ex As ArgumentException
                ' The saved font isn't installed (any more): the picker starts on its default.
            End Try

            Try
                If fontPicker.ShowDialog(New DialogOwner(Me)) <> WinForms.DialogResult.OK Then Return
                chosenFamilyName = fontPicker.Font.FontFamily.Name
            Catch ex As ArgumentException
                ' Windows Forms refuses some fonts (e.g. old bitmap fonts) with this error.
                MessageBox.Show(Me, FontNotUsableText, FontCaption, MessageBoxButton.OK, MessageBoxImage.Information)
                Return
            End Try
        End Using

        currentSettings.StageFontFamily = chosenFamilyName
        StageFontNameText.Text = chosenFamilyName
        StageFontNameText.FontFamily = New FontFamily(chosenFamilyName)
        OnSettingChanged()
    End Sub

    ''' <summary>Puts Stage colours and strengths back to the built-in defaults (Theme and the General page are left alone).</summary>
    Private Sub RestoreAppearanceButton_Click(sender As Object, e As RoutedEventArgs)
        AppSettingsStore.Current.RestoreDefaultAppearance()
        LoadValuesIntoControls()
        OnSettingChanged()
    End Sub

    ''' <summary>
    ''' Shows Windows' colour picker (WPF has none of its own; this one comes from Windows
    ''' Forms, part of .NET Framework). Returns the chosen "#RRGGBB", or Nothing if cancelled.
    ''' </summary>
    Private Function PickColor(currentRgbText As String) As String
        Dim startColor As Color
        ColorText.TryParseRgb(currentRgbText, startColor)

        Using colorPicker As New WinForms.ColorDialog() With {
            .FullOpen = True,
            .AnyColor = True,
            .Color = GdiDrawing.Color.FromArgb(startColor.R, startColor.G, startColor.B)
        }
            ' Owning the picker by this window keeps it in front and makes it modal to it.
            If colorPicker.ShowDialog(New DialogOwner(Me)) <> WinForms.DialogResult.OK Then
                Return Nothing
            End If
            Dim chosen As GdiDrawing.Color = colorPicker.Color
            Return ColorText.FromColor(Color.FromRgb(chosen.R, chosen.G, chosen.B))
        End Using
    End Function

    ''' <summary>
    ''' Lets a Windows Forms dialog use this WPF window as its owner: Windows Forms only
    ''' needs the native window handle, which is all this wraps.
    ''' </summary>
    Private NotInheritable Class DialogOwner
        Implements WinForms.IWin32Window

        Private ReadOnly _ownerHandle As IntPtr

        Public Sub New(ownerWindow As Window)
            _ownerHandle = New WindowInteropHelper(ownerWindow).Handle
        End Sub

        Public ReadOnly Property Handle As IntPtr Implements WinForms.IWin32Window.Handle
            Get
                Return _ownerHandle
            End Get
        End Property
    End Class

#End Region

#Region "Hide / show shortcut (General page)"

    ''' <summary>Recording starts: stop the current shortcut working, so pressing it is recorded instead.</summary>
    Private Sub HotkeyBox_GotKeyboardFocus(sender As Object, e As KeyboardFocusChangedEventArgs)
        GlobalHotkey.Instance.Suspend()
        HotkeyBox.Text = HotkeyRecordingText
    End Sub

    ''' <summary>Recording ends (clicked elsewhere): switch the saved shortcut back on.</summary>
    Private Sub HotkeyBox_LostKeyboardFocus(sender As Object, e As KeyboardFocusChangedEventArgs)
        GlobalHotkey.Instance.ApplyFromSettings()
        ShowCurrentHotkey()
    End Sub

    ''' <summary>
    ''' Records the pressed combination. Modifier keys on their own just show what's held so
    ''' far; the first other key completes the shortcut. Esc cancels; Tab moves on as normal.
    ''' </summary>
    Private Sub HotkeyBox_PreviewKeyDown(sender As Object, e As KeyEventArgs)
        ' With Alt held, WPF reports the real key in SystemKey.
        Dim pressedKey As Key = If(e.Key = Key.System, e.SystemKey, e.Key)
        If pressedKey = Key.Tab Then Return ' let keyboard users leave the box

        e.Handled = True
        Dim heldModifiers As ModifierKeys = Keyboard.Modifiers

        If pressedKey = Key.Escape Then
            Keyboard.ClearFocus() ' back to the saved shortcut (LostKeyboardFocus restores it)
            Return
        End If

        If IsModifierKey(pressedKey) Then
            HotkeyBox.Text = GlobalHotkey.DescribeShortcut(heldModifiers, Key.None) & " + ..."
            Return
        End If

        Dim hasRealModifier As Boolean = (heldModifiers And (ModifierKeys.Control Or ModifierKeys.Alt Or ModifierKeys.Windows)) <> ModifierKeys.None
        If Not hasRealModifier Then
            HotkeyStatusText.Text = HotkeyNeedsModifierStatus
            Return
        End If

        SaveHotkey(heldModifiers, pressedKey)
        Keyboard.ClearFocus() ' finished: LostKeyboardFocus registers it and shows the result
    End Sub

    Private Shared Function IsModifierKey(pressedKey As Key) As Boolean
        Select Case pressedKey
            Case Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt,
                 Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Sub ClearHotkeyButton_Click(sender As Object, e As RoutedEventArgs)
        SaveHotkey(ModifierKeys.None, Key.None)
        GlobalHotkey.Instance.ApplyFromSettings()
        ShowCurrentHotkey()
    End Sub

    Private Sub DefaultHotkeyButton_Click(sender As Object, e As RoutedEventArgs)
        SaveHotkey(StageDefaults.DefaultToggleStagesHotkeyModifiers, StageDefaults.DefaultToggleStagesHotkeyKey)
        GlobalHotkey.Instance.ApplyFromSettings()
        ShowCurrentHotkey()
    End Sub

    Private Sub SaveHotkey(modifiers As ModifierKeys, mainKey As Key)
        AppSettingsStore.Current.ToggleStagesHotkeyModifiers = modifiers
        AppSettingsStore.Current.ToggleStagesHotkeyKey = mainKey
        ScheduleSave()
    End Sub

#End Region

#Region "Items page"

    ''' <summary>One of the three "File name extensions" options was chosen: relabel every Stage now.</summary>
    Private Sub ExtensionsRadio_Checked(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return

        Dim chosenDisplay As FileExtensionDisplay = FileExtensionDisplay.SameAsWindows
        If sender Is ExtensionsShowRadio Then
            chosenDisplay = FileExtensionDisplay.AlwaysShow
        ElseIf sender Is ExtensionsHideRadio Then
            chosenDisplay = FileExtensionDisplay.AlwaysHide
        End If

        AppSettingsStore.Current.FileExtensions = chosenDisplay
        OnSettingChanged() ' re-applies to every Stage, which relabels its items
    End Sub

    ''' <summary>"Refresh icons now": reload every Stage's icons from Windows.</summary>
    ''' <summary>What Delete does on a folder Stage. Nothing on the Stages needs re-applying: it's read when Delete is used.</summary>
    Private Sub FolderDeleteRadio_Checked(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return

        Dim chosenMode As FolderDeleteMode = FolderDeleteMode.RecycleBin
        If sender Is FolderDeleteWindowsRadio Then
            chosenMode = FolderDeleteMode.SameAsWindows
        ElseIf sender Is FolderDeletePermanentRadio Then
            chosenMode = FolderDeleteMode.Permanent
        End If

        AppSettingsStore.Current.FolderDelete = chosenMode
        ScheduleSave()
    End Sub

    Private Sub RefreshIconsButton_Click(sender As Object, e As RoutedEventArgs)
        IconChangeWatcher.RefreshAllIcons()
    End Sub

#End Region

#Region "Desktop page"

    ''' <summary>
    ''' One of the "Arranging Stages" switches. They are read each time a Stage starts moving,
    ''' so nothing needs re-applying to the Stages - just saving.
    ''' </summary>
    Private Sub ArrangingCheckBox_Click(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        Dim currentSettings As AppSettings = AppSettingsStore.Current
        currentSettings.PreventStageOverlap = PreventOverlapCheckBox.IsChecked.GetValueOrDefault()
        currentSettings.SnapSideBySideOnly = SnapSideBySideOnlyCheckBox.IsChecked.GetValueOrDefault()
        currentSettings.ShowSnapGuides = ShowSnapGuidesCheckBox.IsChecked.GetValueOrDefault()
        ScheduleSave()
    End Sub

    ''' <summary>Snapping: Beside other Stages / Full / Off. Read when a Stage starts moving, so just saved.</summary>
    Private Sub SnapModeRadio_Checked(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        Dim chosenMode As StageSnapMode = StageSnapMode.BesideStages
        If SnapFullRadio.IsChecked.GetValueOrDefault() Then chosenMode = StageSnapMode.Full
        If SnapOffRadio.IsChecked.GetValueOrDefault() Then chosenMode = StageSnapMode.Off
        AppSettingsStore.Current.StageSnapping = chosenMode
        ScheduleSave()
    End Sub

    ''' <summary>The minimum gap between Stages, in pixels at 100% scaling.</summary>
    Private Sub StageGapSlider_ValueChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Double))
        If Not IsReactingToChanges() Then Return
        AppSettingsStore.Current.StageGapPixels = SliderValue(StageGapSlider)
        UpdateValueLabels()
        ScheduleSave()
    End Sub

    Private Sub SnapGuideColorButton_Click(sender As Object, e As RoutedEventArgs)
        Dim pickedColor As String = PickColor(AppSettingsStore.Current.SnapGuideColor)
        If pickedColor Is Nothing Then Return ' cancelled
        AppSettingsStore.Current.SnapGuideColor = pickedColor
        UpdateColorButtons()
        ScheduleSave()
    End Sub

    ''' <summary>
    ''' "Hide desktop icons" was clicked. (Click, not Checked/Unchecked, so refreshing the
    ''' tick from code never sends Explorer a command.)
    ''' </summary>
    Private Sub HideDesktopIconsCheckBox_Click(sender As Object, e As RoutedEventArgs)
        If Not IsReactingToChanges() Then Return
        DesktopIntegration.SetDesktopIconsHidden(HideDesktopIconsCheckBox.IsChecked.GetValueOrDefault())
        ' Explorer acts a moment later; then make the tick match what really happened.
        _desktopIconsRecheckTimer.Stop()
        _desktopIconsRecheckTimer.Start()
    End Sub

    Private Sub DesktopIconsRecheckTimer_Tick(sender As Object, e As EventArgs)
        _desktopIconsRecheckTimer.Stop()
        RefreshDesktopIconsCheckBox()
    End Sub

#End Region

#Region "Backup page"

    Private Const SaveBackupDialogTitle As String = "Save a Stages backup"
    Private Const ImportBackupDialogTitle As String = "Import a Stages backup"
    Private Const BackupInProgressText As String = "Saving the backup..."
    Private Const ImportInProgressText As String = "Checking and unpacking the backup..."
    ''' <summary>{0} = number of files, {1} = where it was saved.</summary>
    Private Const BackupSavedFormat As String = "Backup saved ({0} files): {1}"
    ''' <summary>{0} = the problem, from the exception.</summary>
    Private Const BackupFailedFormat As String = "The backup couldn't be saved: {0}"
    Private Const ImportCaption As String = "Import backup"
    Private Const ImportWarningText As String =
        "Import this backup?" & ControlChars.CrLf & ControlChars.CrLf &
        "It REPLACES all your current Stages and settings with the ones in the backup. " &
        "Your current Stages and settings will be moved to the Recycle Bin, so you can bring them back from there." &
        ControlChars.CrLf & ControlChars.CrLf &
        "Stages will restart to finish."
    Private Const ImportNotABackupText As String =
        "This zip file isn't a Stages backup: it has no settings.xml and no Data folder."
    Private Const ImportUnreadableText As String =
        "This zip file is damaged, or contains files Stages won't unpack. Nothing has been changed."
    Private Const ImportTooLargeText As String =
        "This zip file would unpack to more than 2 GB, which is far more than a Stages backup. Nothing has been changed."
    Private Const ImportFileProblemText As String =
        "The backup couldn't be unpacked (a file couldn't be read or written). Nothing has been changed. The log file has the details."
    Private Const ImportRestartFailedText As String =
        "Stages couldn't restart itself to finish the import. Nothing has been changed yet: quit Stages from the tray icon, then start it again to try the import later."

    ''' <summary>Asks where to save, then writes the backup on a background thread.</summary>
    Private Async Sub SaveBackupButton_Click(sender As Object, e As RoutedEventArgs)
        Dim saveDialog As New Microsoft.Win32.SaveFileDialog() With {
            .Title = SaveBackupDialogTitle,
            .Filter = BackupArchive.BackupFileFilter,
            .FileName = String.Format(BackupArchive.SuggestedFileNameFormat, DateTime.Now),
            .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            .OverwritePrompt = True
        }
        If Not saveDialog.ShowDialog(Me).GetValueOrDefault() Then Return
        Dim zipPath As String = saveDialog.FileName

        ' The latest of everything goes in: changes still waiting for their delayed save first.
        AppSettingsStore.Save()
        StageManager.Instance.SaveAllStagesNow()

        SetBackupButtonsEnabled(False)
        BackupStatusText.Text = BackupInProgressText
        Try
            Dim fileCount As Integer = Await Task.Run(Function() BackupArchive.CreateBackup(zipPath))
            BackupStatusText.Text = String.Format(BackupSavedFormat, fileCount, zipPath)
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Logger.Error("Could not save a backup to " & zipPath, ex)
            BackupStatusText.Text = String.Format(BackupFailedFormat, ex.Message)
        Finally
            SetBackupButtonsEnabled(True)
        End Try
    End Sub

    ''' <summary>
    ''' Asks for a backup, warns that it replaces everything, unpacks it (on a background
    ''' thread) and, if that worked, restarts Stages to swap the files in.
    ''' </summary>
    Private Async Sub ImportBackupButton_Click(sender As Object, e As RoutedEventArgs)
        Dim openDialog As New Microsoft.Win32.OpenFileDialog() With {
            .Title = ImportBackupDialogTitle,
            .Filter = BackupArchive.BackupFileFilter,
            .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            .CheckFileExists = True,
            .Multiselect = False
        }
        If Not openDialog.ShowDialog(Me).GetValueOrDefault() Then Return
        Dim zipPath As String = openDialog.FileName

        ' Default answer No: a stray Enter mustn't replace everything.
        Dim answer As MessageBoxResult = MessageBox.Show(Me, ImportWarningText, ImportCaption, MessageBoxButton.YesNo,
                                                         MessageBoxImage.Warning, MessageBoxResult.No)
        If answer <> MessageBoxResult.Yes Then Return

        SetBackupButtonsEnabled(False)
        ImportStatusText.Visibility = Visibility.Collapsed
        BackupStatusText.Text = ImportInProgressText
        Dim outcome As ImportOutcome = Await Task.Run(Function() BackupArchive.PrepareImport(zipPath))
        BackupStatusText.Text = String.Empty

        If outcome = ImportOutcome.Succeeded Then
            If StageManager.Instance.RestartToFinishImport() Then Return ' Stages is quitting
            ShowImportProblem(ImportRestartFailedText)
        Else
            ShowImportProblem(ImportProblemText(outcome))
        End If
        SetBackupButtonsEnabled(True)
    End Sub

    Private Shared Function ImportProblemText(outcome As ImportOutcome) As String
        Select Case outcome
            Case ImportOutcome.NotABackup : Return ImportNotABackupText
            Case ImportOutcome.Unreadable : Return ImportUnreadableText
            Case ImportOutcome.TooLarge : Return ImportTooLargeText
            Case Else : Return ImportFileProblemText
        End Select
    End Function

    Private Sub ShowImportProblem(problemText As String)
        ImportStatusText.Text = problemText
        ImportStatusText.Visibility = Visibility.Visible
    End Sub

    ''' <summary>Only one backup or import at a time.</summary>
    Private Sub SetBackupButtonsEnabled(isEnabled As Boolean)
        SaveBackupButton.IsEnabled = isEnabled
        ImportBackupButton.IsEnabled = isEnabled
    End Sub

#End Region

#Region "About page"

    Private Sub OpenDataFolderButton_Click(sender As Object, e As RoutedEventArgs)
        OpenFolder(AppPaths.DataFolder)
    End Sub

    Private Sub OpenLogsFolderButton_Click(sender As Object, e As RoutedEventArgs)
        OpenFolder(AppPaths.LogsFolder)
    End Sub

    Private Sub OpenSettingsFolderButton_Click(sender As Object, e As RoutedEventArgs)
        OpenFolder(AppPaths.RootFolder)
    End Sub

    ''' <summary>Opens a folder in Explorer, creating it first if it doesn't exist yet.</summary>
    Private Shared Sub OpenFolder(folderPath As String)
        Try
            System.IO.Directory.CreateDirectory(folderPath)
        Catch ex As Exception
            Logger.Warn("Could not create folder " & folderPath, ex)
        End Try
        StageFolder.OpenInExplorer(folderPath)
    End Sub

#End Region

#Region "Saving and closing"

    ''' <summary>
    ''' After any change: show it on every Stage now, and save to disk shortly
    ''' (once the user stops changing things).
    ''' </summary>
    Private Sub OnSettingChanged()
        StageManager.Instance.ApplyAppSettingsToAllStages()
        ScheduleSave()
    End Sub

    ''' <summary>Restarts the save countdown.</summary>
    Private Sub ScheduleSave()
        _saveTimer.Stop()
        _saveTimer.Start()
    End Sub

    Private Sub SaveTimer_Tick(sender As Object, e As EventArgs)
        _saveTimer.Stop()
        AppSettingsStore.Save()
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As RoutedEventArgs)
        Close()
    End Sub

    ''' <summary>Saves anything still waiting and lets ShowSettings create a fresh window next time.</summary>
    Private Sub Window_Closed(sender As Object, e As EventArgs)
        _desktopIconsRecheckTimer.Stop()
        If _saveTimer.IsEnabled Then
            _saveTimer.Stop()
            AppSettingsStore.Save()
        End If
        _openInstance = Nothing
    End Sub

#End Region

End Class
