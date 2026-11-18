' ============================================================================
'  StageWindow.xaml.vb
'  Behaviour of one Stage on the desktop.
'
'  A Stage shows the shortcut files in its folder (Data\<Stage name>\).
'  Dropping something on it creates a shortcut there; "Remove from Stage"
'  sends that shortcut to the Recycle Bin. The user's real files are never
'  touched. A FileSystemWatcher keeps the Stage in step with changes made to
'  the folder in Explorer.
'
'  The class is split over eleven files:
'    StageWindow.xaml.vb (this) ...... window, folder, items, menus, saving
'    StageWindow.Layout.vb ........... views, sorting, arranging, selecting, dragging
'    StageWindow.Tabs.vb ............. tabs and folder views
'    StageWindow.Appearance.vb ....... fonts, colours, frame, background picture
'    StageWindow.Snapping.vb ......... lining Stages up while one is moved or resized
'    StageWindow.Resizing.vb ......... resizing by dragging an edge or corner
'    StageWindow.TitleClick.vb ....... rolling up / down with a click on the title bar
'    StageWindow.DetailsColumns.vb ... the Details view's columns: show / hide, drag to reorder
'    StageWindow.TabFrame.vb ......... the Outlined tab look's frame round the Stage
'    StageWindow.OpenDirection.vb .... minified Stages opening downwards or upwards
'    StageWindow.Kinds.vb ............ Live folder, Running tasks and Clipboard history
'                                      Stages, and the New Stage / New tab sub-menus
'
'  Map of this file:
'    Construction & setup ............ constructor, OnSourceInitialized, Loaded
'    Win32 message hook .............. snapping (moves), stay-at-bottom, block
'                                      maximise/minimise, Explorer restarts
'    Minify (collapse to title) ...... mouse enter/leave handling
'    Folder watching ................. FileSystemWatcher on the Stage folder
'    Items ........................... refresh, open, drag & drop
'    Menus and dialogs ............... Explorer menu, Stage menu, rename, etc.
'  App-wide settings (AppSettingsStore) supply the colours and, unless this
'  Stage has its own, the background and title height.
'    Saving .......................... debounced saves of position/size
'    Health watchdog ................. re-attach / re-show if Windows interferes
'    Closing ......................... Alt+F4 protection, clean-up
' ============================================================================
Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Interop
Imports System.Windows.Threading
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports Stages.ViewModels

Partial Public Class StageWindow

#Region "Constants"
    ''' <summary>Width (device-independent pixels) of the invisible grab zone for resizing at the edges (StageWindow.Resizing.vb).</summary>
    Private Const ResizeBorderThickness As Double = 8
    ''' <summary>Mouse clicks that make a double-click (on the title: rename the Stage).</summary>
    Private Const DoubleClickCount As Integer = 2
    ''' <summary>The title font is sized in points (1/72"); WPF uses 1/96" units.</summary>
    Private Const PointsToDeviceIndependentPixels As Double = 96.0 / 72.0
    ''' <summary>At least this much of the Stage must be on a screen, or it is moved back to the default spot.</summary>
    Private Const MinimumVisibleSpan As Double = 40

    ''' <summary>Wait this long after the last move/resize before writing settings to disk.</summary>
    Private Shared ReadOnly SaveDebounceDelay As TimeSpan = TimeSpan.FromSeconds(1)
    ''' <summary>How often to check whether the mouse has left the Stage (for minify).</summary>
    Private Shared ReadOnly MouseWatchInterval As TimeSpan = TimeSpan.FromMilliseconds(300)
    ''' <summary>Safety-net rescan of the folder (the watcher normally reports changes instantly).</summary>
    Private Shared ReadOnly ItemRefreshInterval As TimeSpan = TimeSpan.FromSeconds(5)
    ''' <summary>
    ''' While the folder watcher is working, it reports changes straight away, so the full
    ''' rescan (a safety net for anything it missed) only runs on every Nth refresh tick:
    ''' 6 x 5 seconds = every 30 seconds. Without a watcher, every tick rescans.
    ''' </summary>
    Private Const WatchedFolderRescanEveryTicks As Integer = 6
    ''' <summary>How often to verify the Stage is still attached to the desktop and visible.</summary>
    Private Shared ReadOnly DesktopWatchdogInterval As TimeSpan = TimeSpan.FromSeconds(5)

    ''' <summary>Id of the "TaskbarCreated" message Explorer broadcasts when it (re)starts.</summary>
    Private Shared ReadOnly TaskbarCreatedMessageId As Integer =
        CInt(NativeMethods.RegisterWindowMessage(NativeMethods.TaskbarCreatedMessageName))

    ' --- Menu texts and messages -----------------------------------------------
    Private Const RemoveFromStageText As String = "Remove from Stage"
    Private Const RenameItemText As String = "Rename..."
    Private Const RenameItemCaption As String = "Rename shortcut"
    ''' <summary>{0} = the reason Windows gave.</summary>
    Private Const RenameItemFailedFormat As String = "The shortcut couldn't be renamed: {0}"
    Private Const ChangeIconText As String = "Change icon..."
    Private Const ResetIconText As String = "Reset icon"
    Private Const ChangeIconCaption As String = "Change icon"
    Private Const ChangeIconFailedText As String = "The shortcut's icon couldn't be changed. It may be read-only; see the log for details."

    Private Const RemoveStageCaption As String = "Remove Stage"
    Private Const RenameStageCaption As String = "Rename Stage"
    Private Const RemoveItemCaption As String = "Remove from Stage"
    Private Const RemoveStageConfirmationText As String =
        "Remove this Stage?" & ControlChars.CrLf & ControlChars.CrLf &
        "Its shortcuts (and those in its tabs) will be moved to the Recycle Bin. Your original files are not affected."
    Private Const RemoveFolderStageConfirmationText As String =
        "Remove this Stage?" & ControlChars.CrLf & ControlChars.CrLf &
        "The folder it shows is not affected; only the Stage's own settings (and any tabs) are moved to the Recycle Bin."
    ''' <summary>{0} = number of real items, {1} = folder path, {2} = line break.</summary>
    Private Const RealFilesInStageFormat As String =
        "This Stage's folder contains {0} item(s) that aren't shortcuts:{2}{1}{2}{2}" &
        "Stages never deletes real files, so the Stage hasn't been removed. " &
        "Move those items out of the folder, then remove the Stage again.{2}{2}" &
        "Open the folder now?"
    ''' <summary>{0} = folder path, {1} = line break.</summary>
    Private Const FolderRenameFailedFormat As String =
        "The Stage has been renamed, but its folder couldn't be renamed - it may be open in Explorer or another program.{1}{1}" &
        "The folder keeps its old name:{1}{0}{1}{1}Close anything using it and rename the Stage again to update the folder."
    Private Const BackgroundMenuText As String = "Background"
    ''' <summary>{0} = the app-wide default background's menu name, e.g. "Blur".</summary>
    Private Const DefaultBackgroundMenuFormat As String = "Default ({0})"
    Private Const BlurMenuText As String = "Blur"
    Private Const AcrylicMenuText As String = "Acrylic (imitation)"
    Private Const TranslucentMenuText As String = "Translucent (no blur)"
    Private Const SettingsMenuText As String = "Settings..."
    Private Const LockMenuText As String = "Lock"
    Private Const MinifyMenuText As String = "Minify"
    ''' <summary>Only in the menu while the tray icon is switched off (Settings > General).</summary>
    Private Const QuitStagesMenuText As String = "Quit Stages"
    Private Const RenameStageMenuText As String = "Rename Stage..."
    Private Const TitleSizeMenuText As String = "Title size..."
    Private Const OpenStageFolderMenuText As String = "Open Stage folder"
    Private Const OpenShownFolderMenuText As String = "Open folder"
    Private Const RemoveStageMenuText As String = "Remove Stage"
    Private Const TransparencyOffCaption As String = "Background"
    Private Const TransparencyEffectsOffText As String =
        "Windows' transparency effects are switched off, so Stages can't show blur or transparency " &
        "and will look solid." & ControlChars.CrLf & ControlChars.CrLf &
        "To turn them on: Settings > Personalization > Colors > Transparency effects."
    Private Const RemoveItemFailedText As String = "That item couldn't be moved to the Recycle Bin. See the log for details."
    Private Const RemoveStageFailedText As String = "The Stage's folder couldn't be moved to the Recycle Bin, so the Stage was kept. See the log for details."
#End Region

#Region "Fields"
    ''' <summary>This Stage's settings (shared with StageManager, which writes them to disk).</summary>
    Private ReadOnly _settings As StageInfo
    ''' <summary>The icons currently displayed. ObservableCollection tells the ListBox when it changes.</summary>
    Private ReadOnly _items As New ObservableCollection(Of StageItemViewModel)()
    Private ReadOnly _shellContextMenu As New ShellContextMenu()
    Private ReadOnly _messageHook As HwndSourceHook

    Private ReadOnly _saveTimer As DispatcherTimer
    Private ReadOnly _mouseWatchTimer As DispatcherTimer
    Private ReadOnly _itemRefreshTimer As DispatcherTimer
    Private ReadOnly _desktopWatchdogTimer As DispatcherTimer

    ''' <summary>Reports file changes in the Stage folder. Nothing while not watching.</summary>
    Private _folderWatcher As FileSystemWatcher
    ''' <summary>This Stage's own scanning thread (see StaWorkerThread.CreateDedicated); shut down in OnClosed.</summary>
    Private ReadOnly _scanWorker As StaWorkerThread = StaWorkerThread.CreateDedicated("Stages folder scan")
    ''' <summary>Refresh ticks since the last full rescan (see WatchedFolderRescanEveryTicks).</summary>
    Private _ticksSinceFullRescan As Integer
    ''' <summary>True if a timed rescan was skipped while all Stages were hidden; caught up when shown.</summary>
    Private _rescanSkippedWhileHidden As Boolean

    ''' <summary>Native window handle (available from OnSourceInitialized onwards).</summary>
    Private _windowHandle As IntPtr = IntPtr.Zero
    Private _titleHeight As Integer
    ''' <summary>True while collapsed down to just the title bar.</summary>
    Private _isMinified As Boolean

    ' --- Minify animation (see the Minify region) ---
    ''' <summary>True while the Stage is rolling open or shut; the changing height must not be saved.</summary>
    Private _isAnimatingMinify As Boolean
    ''' <summary>Steps the roll; created the first time it's needed.</summary>
    Private _minifyAnimationTimer As DispatcherTimer
    ''' <summary>Measures how far through the roll we are (a timer's ticks aren't regular enough to count).</summary>
    Private ReadOnly _minifyAnimationClock As New System.Diagnostics.Stopwatch()
    Private _minifyAnimationFromHeight As Double
    Private _minifyAnimationToHeight As Double
    Private _minifyAnimationMilliseconds As Double
    ''' <summary>True while a menu or dialog belonging to this Stage is open (blocks minify).</summary>
    Private _isPopupOpen As Boolean
    Private _isRefreshingItems As Boolean
    Private _refreshRequestedWhileBusy As Boolean
    ''' <summary>Set once the window has closed; stops late background work touching it.</summary>
    Private _isClosed As Boolean
    ''' <summary>True while the title is being edited in place (after a double-click on it).</summary>
    Private _isEditingTitle As Boolean
#End Region

#Region "Public surface"
    ''' <summary>The settings object this window displays.</summary>
    Public ReadOnly Property Settings As StageInfo
        Get
            Return _settings
        End Get
    End Property

    ''' <summary>
    ''' Set by StageManager.RemoveStage just before closing. Distinguishes "user removed
    ''' this Stage" from "Windows destroyed the window", which triggers recovery.
    ''' </summary>
    Public Property IsBeingRemoved As Boolean
#End Region

#Region "Construction and setup"

    Public Sub New(stageSettings As StageInfo)
        ' Builds the controls declared in StageWindow.xaml.
        InitializeComponent()

        _settings = stageSettings
        _messageHook = New HwndSourceHook(AddressOf WindowMessageHook)

        ' Timers are created before anything that could raise an event that uses them.
        _saveTimer = CreateTimer(SaveDebounceDelay, AddressOf SaveTimer_Tick)
        _mouseWatchTimer = CreateTimer(MouseWatchInterval, AddressOf MouseWatchTimer_Tick)
        _itemRefreshTimer = CreateTimer(ItemRefreshInterval, AddressOf ItemRefreshTimer_Tick)
        AddHandler IsVisibleChanged, AddressOf StageWindow_IsVisibleChanged
        _desktopWatchdogTimer = CreateTimer(DesktopWatchdogInterval, AddressOf DesktopWatchdogTimer_Tick)

        _titleHeight = EffectiveTitleHeight()
        MinWidth = StageDefaults.MinimumStageWidth
        ApplyTitleHeight()
        ApplyName(stageSettings.Name)
        ApplyInitialGeometry()

        ItemsList.ItemsSource = _items
        LoadTabs()            ' StageWindow.Tabs.vb - before anything that reads ActiveTab
        ApplyStageAppearance() ' StageWindow.Appearance.vb
        ApplyViewMode()       ' StageWindow.Layout.vb
        ApplyBackgroundPicture()
    End Sub

    ''' <summary>Creates a stopped UI-thread timer that calls <paramref name="tickHandler"/> every <paramref name="interval"/>.</summary>
    Private Shared Function CreateTimer(interval As TimeSpan, tickHandler As EventHandler) As DispatcherTimer
        Dim newTimer As New DispatcherTimer(DispatcherPriority.Background) With {.Interval = interval}
        AddHandler newTimer.Tick, tickHandler
        Return newTimer
    End Function

    ''' <summary>Positions and sizes the window from the saved settings, keeping it on a visible screen.</summary>
    Private Sub ApplyInitialGeometry()
        Dim initialWidth As Double = Math.Max(StageDefaults.MinimumStageWidth, _settings.Width)
        Dim initialHeight As Double = Math.Max(_titleHeight, _settings.Height)
        Dim initialLeft As Double = _settings.PosX
        Dim initialTop As Double = _settings.PosY

        ' If a monitor was unplugged or the resolution changed, a Stage could end up
        ' entirely off-screen and appear to have "vanished". Bring it back if so.
        Dim screensLeft As Double = SystemParameters.VirtualScreenLeft
        Dim screensTop As Double = SystemParameters.VirtualScreenTop
        Dim screensRight As Double = screensLeft + SystemParameters.VirtualScreenWidth
        Dim screensBottom As Double = screensTop + SystemParameters.VirtualScreenHeight

        Dim horizontallyVisible As Boolean = initialLeft < screensRight - MinimumVisibleSpan AndAlso
                                             initialLeft + initialWidth > screensLeft + MinimumVisibleSpan
        Dim titleVerticallyVisible As Boolean = initialTop > screensTop - _titleHeight AndAlso
                                                initialTop < screensBottom - _titleHeight

        If Not horizontallyVisible OrElse Not titleVerticallyVisible Then
            Logger.Warn("Stage '" & _settings.Name & "' was off-screen; moved back to the default position")
            initialLeft = StageDefaults.NewStageLeft
            initialTop = StageDefaults.NewStageTop
        End If

        Left = initialLeft
        Top = initialTop
        Width = initialWidth
        Height = initialHeight
    End Sub

    ''' <summary>
    ''' The title height in use: this Stage's own choice if it has one, otherwise the
    ''' app-wide default from Settings > General.
    ''' </summary>
    Private Function EffectiveTitleHeight() As Integer
        If _settings.TitleHeight.HasValue Then
            Return StageDefaults.SanitizeTitleHeight(_settings.TitleHeight.Value)
        End If
        Return AppSettingsStore.Current.DefaultTitleHeight
    End Function

    ''' <summary>The background in use: this Stage's own choice if it has one, otherwise the app-wide default.</summary>
    Private Function EffectiveBackdrop() As BackdropStyle
        Return _settings.Backdrop.GetValueOrDefault(AppSettingsStore.Current.DefaultBackdrop)
    End Function

    ''' <summary>
    ''' Sizes the title bar, its font and the minimum window height from _titleHeight.
    ''' The font is the size chosen in Settings > Appearance, or (Automatic) half the title
    ''' height in points, rounded down.
    ''' </summary>
    Private Sub ApplyTitleHeight()
        TitleBar.Height = _titleHeight
        Dim chosenPoints As Integer = AppSettingsStore.Current.TitleFontSizePoints
        Dim titlePoints As Double = If(chosenPoints = StageDefaults.AutomaticTitleFontSize,
                                       Math.Floor(_titleHeight / 2.0),
                                       chosenPoints)
        TitleText.FontSize = titlePoints * PointsToDeviceIndependentPixels
        MinHeight = _titleHeight
    End Sub

    Private Sub ApplyName(stageName As String)
        Title = stageName
        TitleText.Text = stageName
    End Sub

    ''' <summary>
    ''' Runs once the native window exists but before it is shown - the right moment
    ''' to apply Win32 styles and hook into its message stream.
    ''' </summary>
    Protected Overrides Sub OnSourceInitialized(e As EventArgs)
        MyBase.OnSourceInitialized(e)

        _windowHandle = New WindowInteropHelper(Me).Handle
        Dim windowSource As HwndSource = HwndSource.FromHwnd(_windowHandle)
        windowSource.AddHook(_messageHook)

        ' Make WPF render onto a transparent surface (its default is opaque black). Windows'
        ' compositor then shows the backdrop effect wherever our tint lets it through.
        windowSource.CompositionTarget.BackgroundColor = Colors.Transparent

        DesktopIntegration.PrepareStageWindow(_windowHandle)
        ApplyBackdropNow()
        ApplyCornerStyle() ' StageWindow.Appearance.vb
    End Sub

    ''' <summary>
    ''' (Re)applies this Stage's background. Called at start-up, when the user picks a
    ''' different Background, when the Settings window changes colours or defaults, and
    ''' whenever Windows might have reset the effect.
    ''' Acrylic is Stages' own imitation: Windows supplies the blur and the extra
    ''' acrylic layers (sheen, grey tint, noise) are drawn in XAML - see AcrylicNoise.
    ''' Colours and strengths come from AppSettings (Settings > Appearance).
    ''' </summary>
    Private Sub ApplyBackdropNow()
        Dim currentAppSettings As AppSettings = AppSettingsStore.Current
        Dim backdropInUse As BackdropStyle = EffectiveBackdrop()
        Dim isImitationAcrylic As Boolean = (backdropInUse = BackdropStyle.Acrylic)

        ' 1. Our own layers
        If isImitationAcrylic Then
            TintLayer.Background = currentAppSettings.CreateAcrylicTintBrush()
            AcrylicLuminosityLayer.Background = currentAppSettings.CreateAcrylicSheenBrush()
            AcrylicNoiseLayer.Opacity = currentAppSettings.AcrylicGrainOpacity()
            If AcrylicNoiseLayer.Fill Is Nothing Then
                AcrylicNoiseLayer.Fill = AcrylicNoise.TiledBrush
            End If
        Else
            TintLayer.Background = currentAppSettings.CreateStageTintBrush()
        End If
        AcrylicLuminosityLayer.Visibility = If(isImitationAcrylic, Visibility.Visible, Visibility.Collapsed)
        AcrylicNoiseLayer.Visibility = If(isImitationAcrylic, Visibility.Visible, Visibility.Collapsed)
        ' An Automatic tab outline is the Stage's colour, a little lighter - see StageWindow.Appearance.vb.
        RefreshTabAccentBrush()

        ' 2. Windows' effect behind them (needs the native window to exist)
        If _windowHandle = IntPtr.Zero Then Return
        DesktopIntegration.ApplyBackdrop(_windowHandle, backdropInUse)
    End Sub

    ''' <summary>
    ''' Called (through StageManager) whenever the Settings window changes something:
    ''' re-reads the colours and, where this Stage follows the defaults, the background
    ''' and title height.
    ''' </summary>
    Public Sub ApplyAppSettings()
        ApplyBackdropNow()
        ApplyStageAppearance()
        ApplyTitleHeight() ' the title font size may have changed
        ApplyEffectiveTitleHeight()
        ApplyPanelLayout() ' label size changes the icon cells' height
        RefreshItemLabels()
    End Sub

    ''' <summary>Re-works out every item's label (with or without its extension - Settings > Items).</summary>
    Public Sub RefreshItemLabels()
        For Each itemViewModel As StageItemViewModel In _items
            itemViewModel.RefreshDisplayName()
        Next
    End Sub

    ''' <summary>Loads every item's picture again, after Windows' icons changed (IconChangeWatcher).</summary>
    Public Sub ReloadIcons()
        For Each itemViewModel As StageItemViewModel In _items
            itemViewModel.ReloadPicture()
        Next
    End Sub

    ''' <summary>Forgets this Stage's own background, so it follows the app default. Saves.</summary>
    Public Sub UseDefaultBackdrop()
        If Not _settings.Backdrop.HasValue Then Return
        _settings.Backdrop = Nothing
        ApplyBackdropNow()
        SaveNow()
    End Sub

    ''' <summary>Forgets this Stage's own title height, so it follows the app default. Saves.</summary>
    Public Sub UseDefaultTitleHeight()
        If Not _settings.TitleHeight.HasValue Then Return
        _settings.TitleHeight = Nothing
        ApplyEffectiveTitleHeight()
        SaveNow()
    End Sub

    ''' <summary>Re-sizes the title bar if the height in use has changed (e.g. the default was changed in Settings).</summary>
    Private Sub ApplyEffectiveTitleHeight()
        Dim heightInUse As Integer = EffectiveTitleHeight()
        If heightInUse = _titleHeight Then Return
        _titleHeight = heightInUse
        ApplyTitleHeight()
        If _isMinified Then
            StopMinifyAnimation()
            Height = _titleHeight
        End If
    End Sub

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs)
        ' Minify-enabled Stages start collapsed, and so does a Stage rolled up by a title click.
        If _settings.CanMinify OrElse _settings.IsRolledUp Then
            CollapseToTitleBar() ' shut at its saved position, the title bar on top
            SetOpenedUpward(False)
        ElseIf _settings.IsOpenedUpward Then
            RestoreUpwardOpening() ' left open upwards (StageWindow.OpenDirection.vb)
        End If

        ' The Outlined tab look's frame follows every layout change - StageWindow.TabFrame.vb.
        AddHandler LayoutUpdated, AddressOf StageWindow_LayoutUpdated

        StartWatchingFolder()
        RefreshItems()
        _itemRefreshTimer.Start()
        _desktopWatchdogTimer.Start()
        UpdateKindTimers() ' a Running tasks tab reads its windows every few seconds - StageWindow.Kinds.vb
    End Sub

#End Region

#Region "Win32 message hook"

    ''' <summary>
    ''' Sees every Windows message sent to this Stage before WPF handles it.
    ''' Set <paramref name="handled"/> = True to stop WPF processing that message.
    ''' </summary>
    Private Function WindowMessageHook(hwnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr,
                                       ByRef handled As Boolean) As IntPtr
        ' While an Explorer context menu is open, let it see the messages it needs first.
        Dim shellMenuResult As IntPtr
        If _shellContextMenu.TryHandleMenuMessage(msg, wParam, lParam, shellMenuResult) Then
            handled = True
            Return shellMenuResult
        End If

        ' Lining Stages up while this one is moved or resized - StageWindow.Snapping.vb.
        If HandleSnappingMessage(msg, wParam, lParam) Then
            handled = True
            Return New IntPtr(1) ' TRUE: "the proposed rectangle may have been changed"
        End If

        Select Case msg
            Case NativeMethods.WM_WINDOWPOSCHANGING
                KeepAtBottomOfZOrder(lParam)

            Case NativeMethods.WM_SYSCOMMAND
                ' Block maximise/minimise (e.g. Win+Up, or Aero Snap) - a Stage is always "normal".
                Dim command As Integer = CInt(wParam.ToInt64() And NativeMethods.SC_COMMAND_MASK)
                If command = NativeMethods.SC_MAXIMIZE OrElse command = NativeMethods.SC_MINIMIZE Then
                    handled = True
                End If

            Case NativeMethods.WM_NCMOUSELEAVE
                CheckWhetherMouseHasLeft()

            Case NativeMethods.WM_SETTINGCHANGE
                ' e.g. transparency effects or the theme changed - Windows may have reset the effect.
                ApplyBackdropNow()
                ' Windows' light/dark app mode may have changed (matters when Theme = System).
                ThemeManager.OnWindowsSettingsChanged()
                ' ...or Explorer's "File name extensions" setting.
                RefreshItemLabels()

            Case Else
                ' Not a constant, so it can't be a Case label.
                If msg = TaskbarCreatedMessageId Then
                    OnExplorerRestarted()
                End If
        End Select

        Return IntPtr.Zero
    End Function

    ''' <summary>
    ''' Whenever Windows is about to change our z-order (e.g. because the Stage was clicked),
    ''' rewrite the request to "bottom of the pile" so Stages always sit behind other windows.
    ''' </summary>
    Private Shared Sub KeepAtBottomOfZOrder(lParam As IntPtr)
        Dim pendingPosition As NativeMethods.WINDOWPOS = Marshal.PtrToStructure(Of NativeMethods.WINDOWPOS)(lParam)
        Dim zOrderIsChanging As Boolean = (pendingPosition.flags And NativeMethods.SWP_NOZORDER) = 0UI
        If zOrderIsChanging Then
            pendingPosition.hwndInsertAfter = NativeMethods.HWND_BOTTOM
            Marshal.StructureToPtr(pendingPosition, lParam, False)
        End If
    End Sub

    ''' <summary>
    ''' Explorer restarted but this window survived: re-attach it to the NEW desktop window,
    ''' otherwise Show Desktop would start hiding it (and the next restart would destroy it).
    ''' </summary>
    Private Sub OnExplorerRestarted()
        Logger.Info("Explorer restarted - re-attaching Stage '" & _settings.Name & "' to the desktop")
        DesktopIntegration.PrepareStageWindow(_windowHandle)
        ApplyBackdropNow()
        DesktopIntegration.EnsureWindowVisible(_windowHandle)
    End Sub

#End Region

#Region "Minify (collapse to the title bar when the mouse leaves)"

    ''' <summary>One animation step roughly every 15 ms (about 60 a second).</summary>
    Private Shared ReadOnly MinifyAnimationStepInterval As TimeSpan = TimeSpan.FromMilliseconds(15)
    ''' <summary>Height changes smaller than this (DIPs) aren't worth animating.</summary>
    Private Const SmallestAnimatedHeightChange As Double = 2
    ''' <summary>The power in the ease-out curve: 3 = starts quickly and settles gently.</summary>
    Private Const EaseOutPower As Double = 3

    Private Sub CollapseToTitleBar()
        If _isMinified Then Return
        ' Set the flag first so the resulting SizeChanged doesn't overwrite the saved height.
        _isMinified = True
        ChangeHeightForMinify(_titleHeight)
    End Sub

    ''' <param name="mayOpenUpward">False = always open downwards (a Stage that stops minifying).</param>
    Private Sub ExpandFromTitleBar(Optional mayOpenUpward As Boolean = True)
        If Not _isMinified Then Return
        _isMinified = False
        ' Which way to open (Settings > General > Minified Stages) is chosen only from fully
        ' shut; a roll that turns round half-way keeps going the way it was - see
        ' StageWindow.OpenDirection.vb.
        If Not _isAnimatingMinify Then
            SetOpenedUpward(mayOpenUpward AndAlso ShouldOpenUpward())
        End If
        ChangeHeightForMinify(Math.Max(_titleHeight, _settings.Height))
    End Sub

    ''' <summary>
    ''' Takes the Stage to a new height for minify: rolling there if "Animate opening and
    ''' closing" is on (Settings > General) and Windows' own animation effects are on,
    ''' otherwise instantly. A roll that's interrupted (the mouse comes back while it's
    ''' closing) simply starts again from wherever the height has got to.
    ''' </summary>
    Private Sub ChangeHeightForMinify(targetHeight As Double)
        Dim currentSettings As AppSettings = AppSettingsStore.Current
        Dim startHeight As Double = If(ActualHeight > 0, ActualHeight, Height)
        Dim shouldAnimate As Boolean = currentSettings.AnimateMinify AndAlso
                                       SystemParameters.ClientAreaAnimation AndAlso
                                       IsLoaded AndAlso IsVisible AndAlso
                                       Math.Abs(targetHeight - startHeight) >= SmallestAnimatedHeightChange
        If Not shouldAnimate Then
            StopMinifyAnimation()
            ApplyMinifyHeight(targetHeight) ' from the top, or keeping the bottom when opening upwards
            OnMinifyHeightReached()
            Return
        End If

        _minifyAnimationFromHeight = startHeight
        _minifyAnimationToHeight = targetHeight
        _minifyAnimationMilliseconds = currentSettings.MinifyAnimationMilliseconds
        _isAnimatingMinify = True
        _minifyAnimationClock.Restart()
        If _minifyAnimationTimer Is Nothing Then
            ' Render priority, so steps aren't held up behind background work like icon loading.
            _minifyAnimationTimer = New DispatcherTimer(DispatcherPriority.Render) With {.Interval = MinifyAnimationStepInterval}
            AddHandler _minifyAnimationTimer.Tick, AddressOf MinifyAnimationTimer_Tick
        End If
        _minifyAnimationTimer.Start()
    End Sub

    ''' <summary>One step of the roll: set the height for the time that has passed.</summary>
    Private Sub MinifyAnimationTimer_Tick(sender As Object, e As EventArgs)
        Dim progress As Double = _minifyAnimationClock.ElapsedMilliseconds / _minifyAnimationMilliseconds
        If progress >= 1 OrElse _isClosed Then
            ' Finished: land exactly on the target height.
            Dim finalHeight As Double = _minifyAnimationToHeight
            StopMinifyAnimation()
            If Not _isClosed Then
                ApplyMinifyHeight(finalHeight)
                OnMinifyHeightReached()
            End If
            Return
        End If

        ' Ease-out: 1 - (1 - t)^3 moves fast at first, then slows into place.
        Dim easedProgress As Double = 1 - Math.Pow(1 - progress, EaseOutPower)
        ApplyMinifyHeight(_minifyAnimationFromHeight + (_minifyAnimationToHeight - _minifyAnimationFromHeight) * easedProgress)
    End Sub

    ''' <summary>Stops a roll in progress, leaving the height wherever it is (callers set the height they want).</summary>
    Private Sub StopMinifyAnimation()
        If _minifyAnimationTimer IsNot Nothing Then _minifyAnimationTimer.Stop()
        _minifyAnimationClock.Reset()
        _isAnimatingMinify = False
    End Sub

    Private Sub Window_MouseEnter(sender As Object, e As MouseEventArgs)
        OnMouseEnteredStage()
    End Sub

    Private Sub Window_MouseLeave(sender As Object, e As MouseEventArgs)
        CheckWhetherMouseHasLeft()
    End Sub

    Private Sub OnMouseEnteredStage()
        ' A Stage rolled up by a title click stays shut until clicked again - StageWindow.TitleClick.vb.
        If _settings.CanMinify AndAlso Not _settings.IsRolledUp Then
            ExpandFromTitleBar()
        End If
        ' Poll until the mouse really leaves - WPF's MouseLeave alone is unreliable here (it
        ' also fires when another window passes over the Stage, or a menu takes the mouse).
        _mouseWatchTimer.Start()
    End Sub

    Private Sub MouseWatchTimer_Tick(sender As Object, e As EventArgs)
        CheckWhetherMouseHasLeft()
    End Sub

    ''' <summary>
    ''' If the mouse is truly outside the Stage (and the user isn't mid-resize or in a menu),
    ''' clear the selection and collapse the Stage if minify is on.
    ''' </summary>
    Private Sub CheckWhetherMouseHasLeft()
        If _isPopupOpen Then Return
        If DesktopIntegration.IsLeftMouseButtonDown() Then Return ' mid-drag or mid-resize
        If DesktopIntegration.IsCursorInsideWindow(_windowHandle) Then Return

        _mouseWatchTimer.Stop()
        ItemsList.UnselectAll()
        If _settings.CanMinify Then
            CollapseToTitleBar()
        End If
    End Sub

#End Region

#Region "Folder watching"

    ''' <summary>
    ''' Starts reporting changes in the open tab's folder (shortcuts added, removed or renamed
    ''' in Explorer; for a folder view, the folder it shows) so the Stage updates straight
    ''' away. Also used after a rename or a tab switch, when the folder has changed. Failure is
    ''' harmless: the 5-second rescan still catches up. A Running tasks tab has no folder to
    ''' watch; a Live folder that looks inside sub-folders is watched with them.
    ''' </summary>
    Public Sub StartWatchingFolder()
        StopWatchingFolder()
        Dim folderToWatch As String = ActiveTab.ContentFolderPath
        If _isClosed OrElse Not ShouldWatchActiveFolder() OrElse String.IsNullOrEmpty(folderToWatch) OrElse Not Directory.Exists(folderToWatch) Then
            Return
        End If

        Try
            Dim watcher As New FileSystemWatcher(folderToWatch) With {
                .IncludeSubdirectories = ShouldWatchSubfolders(),
                .NotifyFilter = NotifyFilters.FileName Or NotifyFilters.DirectoryName Or NotifyFilters.LastWrite
            }
            AddHandler watcher.Created, AddressOf FolderWatcher_Changed
            AddHandler watcher.Deleted, AddressOf FolderWatcher_Changed
            AddHandler watcher.Changed, AddressOf FolderWatcher_Changed
            AddHandler watcher.Renamed, AddressOf FolderWatcher_Renamed
            AddHandler watcher.Error, AddressOf FolderWatcher_Error
            watcher.EnableRaisingEvents = True
            _folderWatcher = watcher
        Catch ex As Exception
            Logger.Warn("Could not watch Stage folder " & folderToWatch, ex)
        End Try
    End Sub

    ''' <summary>
    ''' Called by StageManager when this Stage's folder was renamed in Explorer: follow the
    ''' folder to its new path and take the new folder name as the Stage's title.
    ''' </summary>
    Public Sub OnFolderRenamedExternally(newFolderPath As String)
        StopWatchingFolder()
        _settings.FolderPath = newFolderPath
        _settings.Name = Path.GetFileName(newFolderPath)
        FollowTabsToRenamedFolder()
        ApplyName(_settings.Name)
        RebuildTabHeaders()
        SaveNow()
        StartWatchingFolder()
        RefreshItems() ' item paths now live under the new folder
    End Sub

    ''' <summary>Called by StageManager when this Stage's folder was deleted or moved out of Data in Explorer.</summary>
    Public Sub OnFolderDeletedExternally()
        ' Nothing left to save into - close as a deliberate removal (no recovery).
        IsBeingRemoved = True
        Close()
    End Sub

    ''' <summary>Stops watching (before the folder is renamed or removed, and on close).</summary>
    Public Sub StopWatchingFolder()
        If _folderWatcher Is Nothing Then Return
        _folderWatcher.EnableRaisingEvents = False
        _folderWatcher.Dispose()
        _folderWatcher = Nothing
    End Sub

    ''' <summary>
    ''' The watcher lost track (too many changes at once overflowed its buffer, or a network
    ''' folder dropped its connection). Runs on a background thread: on the UI thread, start a
    ''' fresh watcher and rescan, so nothing is missed.
    ''' </summary>
    Private Sub FolderWatcher_Error(sender As Object, e As ErrorEventArgs)
        Logger.Warn("The folder watcher for Stage '" & _settings.Name & "' reported a problem; restarting it", e.GetException())
        Dispatcher.BeginInvoke(New Action(
            Sub()
                If _isClosed Then Return
                StartWatchingFolder()
                RefreshItems()
            End Sub))
    End Sub

    ''' <summary>Runs on a background thread - hands the work to the UI thread.</summary>
    Private Sub FolderWatcher_Changed(sender As Object, e As FileSystemEventArgs)
        ' Our own settings saves would otherwise trigger a pointless rescan every time.
        If StageFolder.IsStageOwnFile(e.Name) Then Return
        Dispatcher.BeginInvoke(New Action(AddressOf RefreshItems))
    End Sub

    Private Sub FolderWatcher_Renamed(sender As Object, e As RenamedEventArgs)
        Dispatcher.BeginInvoke(New Action(AddressOf RefreshItems))
    End Sub

#End Region

#Region "Items: refresh, open, drag and drop"

    ''' <summary>
    ''' Every 5 seconds. Labels are re-checked every time (Explorer's "File name extensions"
    ''' setting may have changed); the folder itself is rescanned every time only when no
    ''' watcher is reporting changes, otherwise every 30 seconds as a safety net. Nothing is
    ''' rescanned while all Stages are hidden; they catch up when shown.
    ''' </summary>
    Private Sub ItemRefreshTimer_Tick(sender As Object, e As EventArgs)
        If Not IsVisible Then
            _rescanSkippedWhileHidden = True
            Return
        End If

        RefreshItemLabels()
        _ticksSinceFullRescan += 1
        Dim isWatcherWorking As Boolean = _folderWatcher IsNot Nothing
        If isWatcherWorking AndAlso _ticksSinceFullRescan < WatchedFolderRescanEveryTicks Then Return
        RefreshItems()
    End Sub

    ''' <summary>Shown again after Hide Stages: catch up on any rescan skipped while hidden.</summary>
    Private Sub StageWindow_IsVisibleChanged(sender As Object, e As DependencyPropertyChangedEventArgs)
        If IsVisible AndAlso _rescanSkippedWhileHidden Then
            _rescanSkippedWhileHidden = False
            RefreshItems()
        End If
    End Sub

    ''' <summary>
    ''' Rescans the open tab's folder and updates the icons. The scan runs on the shell worker
    ''' thread (it reads shortcut files and checks targets, which can be slow on network
    ''' drives). "Async Sub" = fire-and-forget; all errors are caught inside.
    ''' </summary>
    Private Async Sub RefreshItems()
        If _isClosed Then Return
        ' Not while the user is dragging items or drawing a selection box: replacing the
        ' items mid-drag would pull them out from under the mouse. The 5-second rescan
        ' (ItemRefreshTimer) picks up whatever changed once the mouse is released.
        If _isDraggingItems OrElse _isBandSelecting Then Return

        ' If a refresh is already running, ask it to run once more when it finishes.
        If _isRefreshingItems Then
            _refreshRequestedWhileBusy = True
            Return
        End If

        _isRefreshingItems = True
        _ticksSinceFullRescan = 0
        Try
            Do
                _refreshRequestedWhileBusy = False
                ' Tabs added or removed in Explorer (cheap: only looks for settings files).
                SyncTabsWithDisk()

                ' The scan for this tab's kind (folder, Live folder or open windows), with
                ' snapshots of what it needs - StageWindow.Kinds.vb.
                Dim scannedTab As StageInfo = ActiveTab
                Dim scanJob As Func(Of List(Of StageItem)) = BuildScanJob(scannedTab)

                ' Nothing back = the folder isn't there right now (e.g. a folder view of an
                ' unplugged drive): show nothing, but keep the saved order and positions.
                ' On this Stage's own scan thread, so a slow folder only ever delays this Stage.
                Dim scannedItems As List(Of StageItem) = Await _scanWorker.RunAsync(scanJob)
                If _isClosed Then Return

                ' Another tab was opened while scanning: these items belong to the old one.
                If scannedTab IsNot ActiveTab Then
                    _refreshRequestedWhileBusy = True
                    Continue Do
                End If

                If scannedItems Is Nothing Then
                    ShowScannedItems(New List(Of StageItem)())
                    Continue Do
                End If

                ' The folder is there: if it wasn't when the Stage started (an unplugged drive
                ' that's back), start watching it now.
                If _folderWatcher Is Nothing Then StartWatchingFolder()

                ' The scan's order is the Custom order (saved order, new items after it).
                ' Not for open windows: they come and go, so their order isn't saved.
                If ShouldRememberScannedOrder(scannedTab) Then RememberDisplayOrder(scannedItems)
                ' Open windows keep a steady order, like taskbar buttons - StageWindow.Kinds.vb.
                If scannedTab.IsRunningTasks Then scannedItems = InTaskbarOrder(scannedItems)
                ShowScannedItems(SortForDisplay(scannedItems))
                ApplyItemPositions()
                ' Picks up a change to Explorer's "File name extensions" setting within a refresh.
                RefreshItemLabels()
            Loop While _refreshRequestedWhileBusy
        Catch ex As OperationCanceledException
            ' The Stage closed while its scan was queued: nothing to show any more.
        Catch ex As Exception
            Logger.Error("Could not refresh the items in Stage '" & _settings.Name & "'", ex)
        Finally
            _isRefreshingItems = False
        End Try
    End Sub

    ''' <summary>
    ''' Updates the displayed items to match a fresh scan. Existing items are kept (so
    ''' their icons don't reload) unless their picture source changed.
    ''' </summary>
    Private Sub ShowScannedItems(scannedItems As List(Of StageItem))
        Dim existingByPath As New Dictionary(Of String, StageItemViewModel)(StringComparer.OrdinalIgnoreCase)
        For Each existingItem As StageItemViewModel In _items
            If Not existingByPath.ContainsKey(existingItem.FullPath) Then
                existingByPath.Add(existingItem.FullPath, existingItem)
            End If
        Next

        Dim updatedItems As New List(Of StageItemViewModel)()
        For Each scannedItem As StageItem In scannedItems
            Dim viewModel As StageItemViewModel = Nothing
            ' Reuse only if the picture can't have changed: same thumbnail source and the entry
            ' unchanged on disk (a new custom icon changes the shortcut file).
            Dim canReuse As Boolean =
                existingByPath.TryGetValue(scannedItem.FullPath, viewModel) AndAlso
                String.Equals(viewModel.Item.ThumbnailSourcePath, scannedItem.ThumbnailSourcePath, StringComparison.OrdinalIgnoreCase) AndAlso
                viewModel.Item.LastWriteTicks = scannedItem.LastWriteTicks

            If canReuse Then
                viewModel.UpdateFrom(scannedItem)
            Else
                viewModel = New StageItemViewModel(scannedItem, CurrentIconSize())
            End If
            updatedItems.Add(viewModel)
        Next

        If updatedItems.SequenceEqual(_items) Then
            Return ' same items in the same order - nothing to redraw
        End If

        ' Apply only the differences, so items that are still there keep their selection and
        ' the list keeps its scroll position (emptying and refilling it would lose both).
        ' 1. Take out items that have gone (or are being replaced by a fresh copy).
        Dim stillShown As New HashSet(Of StageItemViewModel)(updatedItems)
        For existingIndex As Integer = _items.Count - 1 To 0 Step -1
            If Not stillShown.Contains(_items(existingIndex)) Then
                _items.RemoveAt(existingIndex)
            End If
        Next
        ' 2. Put every item in its place: new ones are inserted, moved ones are moved.
        For targetIndex As Integer = 0 To updatedItems.Count - 1
            Dim wanted As StageItemViewModel = updatedItems(targetIndex)
            If targetIndex < _items.Count AndAlso _items(targetIndex) Is wanted Then
                Continue For ' already in place (the usual case, and the fast one)
            End If
            Dim currentIndex As Integer = _items.IndexOf(wanted)
            If currentIndex < 0 Then
                _items.Insert(targetIndex, wanted)
            Else
                _items.Move(currentIndex, targetIndex)
            End If
        Next
    End Sub

    ''' <summary>
    ''' Saves the Custom order as scanned, so new and removed items are reflected in ItemOrder,
    ''' and forgets hand-placed positions of items that have gone.
    ''' (Opening an item is handled in StageWindow.Layout.vb: double-click or Enter.)
    ''' </summary>
    Private Sub RememberDisplayOrder(scannedItems As List(Of StageItem))
        Dim customOrder As List(Of String) = scannedItems.Select(Function(scannedItem) scannedItem.FileName).ToList()
        If Not customOrder.SequenceEqual(ActiveTab.ItemOrder, StringComparer.OrdinalIgnoreCase) Then
            ActiveTab.ItemOrder = customOrder
            ScheduleActiveTabSave()
        End If
        ForgetPositionsOfMissingItems(customOrder)
    End Sub

    ''' <summary>
    ''' Walks up from whatever element was clicked (an Image, a TextBlock...) to the
    ''' ListBoxItem that contains it, and returns that item's data. Nothing if the click
    ''' wasn't on an item (e.g. empty space or the title bar).
    ''' </summary>
    Private Shared Function FindItemFromElement(clickedElement As DependencyObject) As StageItemViewModel
        Dim current As DependencyObject = clickedElement
        While current IsNot Nothing
            Dim itemContainer As ListBoxItem = TryCast(current, ListBoxItem)
            If itemContainer IsNot Nothing Then
                Return TryCast(itemContainer.DataContext, StageItemViewModel)
            End If

            ' Visual elements have a visual parent; text fragments only have a logical one.
            If TypeOf current Is Visual Then
                current = VisualTreeHelper.GetParent(current)
            Else
                current = LogicalTreeHelper.GetParent(current)
            End If
        End While
        Return Nothing
    End Function

    Private Sub Window_DragEnter(sender As Object, e As DragEventArgs)
        ' Dragging a file onto a minified Stage opens it up so you can see where it lands.
        OnMouseEnteredStage()
        e.Effects = ChooseDropEffect(e)
        e.Handled = True
    End Sub

    Private Sub Window_DragOver(sender As Object, e As DragEventArgs)
        e.Effects = ChooseDropEffect(e)
        e.Handled = True
    End Sub

    Private Sub Window_DragLeave(sender As Object, e As DragEventArgs)
        CheckWhetherMouseHasLeft()
    End Sub

    ''' <summary>
    ''' What the drop will do. A Stage only makes a SHORTCUT to the dropped item, so we
    ''' report "Link" (or "Copy" if the source doesn't offer Link) - never "Move", which
    ''' could make some drag sources delete the original.
    ''' </summary>
    Private Function ChooseDropEffect(e As DragEventArgs) As DragDropEffects
        ' Only a Standard Stage takes drops: a Live folder shows a real folder, and Running
        ' tasks and Clipboard history fill themselves.
        If _settings.Locked OrElse Not ActiveTab.AcceptsDroppedItems OrElse Not e.Data.GetDataPresent(DataFormats.FileDrop) Then
            Return DragDropEffects.None
        End If
        If (e.AllowedEffects And DragDropEffects.Link) = DragDropEffects.Link Then
            Return DragDropEffects.Link
        End If
        If (e.AllowedEffects And DragDropEffects.Copy) = DragDropEffects.Copy Then
            Return DragDropEffects.Copy
        End If
        Return DragDropEffects.None
    End Function

    ''' <summary>Creates a shortcut in the Stage folder for each dropped file or folder.</summary>
    Private Sub Window_Drop(sender As Object, e As DragEventArgs)
        e.Handled = True
        e.Effects = ChooseDropEffect(e)
        If e.Effects = DragDropEffects.None Then Return

        Dim droppedPaths As String() = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If droppedPaths Is Nothing Then Return

        Dim addedCount As Integer = StageFolder.AddDroppedItems(ActiveTab, droppedPaths)
        If addedCount > 0 Then
            SaveActiveTab()
            RefreshItems()
        End If
    End Sub

#End Region

#Region "Menus and dialogs"

    ''' <summary>
    ''' Right-click: Explorer's menu (with "Remove from Stage" at the top) for an item,
    ''' a short Open / Remove menu when several selected items are right-clicked, or the
    ''' Stage's own menu elsewhere. Shift+right-click on an item shows the Stage menu instead.
    ''' </summary>
    Private Sub Window_PreviewMouseRightButtonUp(sender As Object, e As MouseButtonEventArgs)
        e.Handled = True

        ' The Details view's column headings have their own menu: which columns show.
        If IsInsideDetailsHeader(TryCast(e.OriginalSource, DependencyObject)) Then
            ShowDetailsColumnsMenu() ' StageWindow.DetailsColumns.vb
            Return
        End If

        Dim clickedItem As StageItemViewModel = FindItemFromElement(TryCast(e.OriginalSource, DependencyObject))
        Dim isShiftHeld As Boolean = (Keyboard.Modifiers And ModifierKeys.Shift) = ModifierKeys.Shift

        If clickedItem IsNot Nothing AndAlso Not isShiftHeld Then
            Dim isPartOfSelection As Boolean = ItemsList.SelectedItems.Contains(clickedItem)
            If isPartOfSelection AndAlso ItemsList.SelectedItems.Count > 1 Then
                ShowSelectionMenu(SelectedItemsInDisplayOrder())
                Return
            End If
            ' Like Explorer: right-clicking an unselected item selects just that item.
            ItemsList.UnselectAll()
            ItemsList.SelectedItem = clickedItem
            ShowExplorerMenu(clickedItem)
        Else
            ShowStageMenu(clickedItem)
        End If
    End Sub

    ''' <summary>
    ''' Shows Explorer's right-click menu for an item, with Stages' own commands at the top:
    ''' Remove from Stage, and for shortcuts Change icon... (plus Reset icon if it has one).
    ''' </summary>
    Private Sub ShowExplorerMenu(item As StageItemViewModel)
        ' An open window (Running tasks) has no file, so no Explorer menu: it gets its own.
        If item.Item.IsWindow Then
            ShowWindowItemMenu(item) ' StageWindow.Kinds.vb
            Return
        End If

        ' Our commands: texts for the menu, and what each one does, in the same order.
        Dim extraCommandTexts As New List(Of String)()
        Dim extraCommandActions As New List(Of Action)()
        AddItemCommands(item, extraCommandTexts, extraCommandActions)

        Dim cursorPosition As NativeMethods.NativePoint = DesktopIntegration.GetCursorScreenPosition()
        Dim chosenExtraCommand As Integer = ShellContextMenu.NoExtraCommandChosen

        _isPopupOpen = True
        Try
            chosenExtraCommand = _shellContextMenu.Show(item.FullPath, _windowHandle, cursorPosition.X, cursorPosition.Y,
                                                        extraCommandTexts)
        Catch ex As Exception
            Logger.Warn("Could not show the Explorer menu for " & item.FullPath, ex)
        Finally
            _isPopupOpen = False
        End Try

        If chosenExtraCommand >= 0 AndAlso chosenExtraCommand < extraCommandActions.Count Then
            extraCommandActions(chosenExtraCommand).Invoke()
        Else
            ' An Explorer command may have changed things (e.g. Properties > change icon).
            RefreshItems()
        End If
    End Sub

    ''' <summary>
    ''' Stages' own commands for one item, shared by the Explorer menu and the Stage menu:
    ''' Remove from Stage; for shortcuts also Rename..., Change icon... and, if it has one,
    ''' Reset icon.
    ''' </summary>
    Private Sub AddItemCommands(item As StageItemViewModel, commandTexts As List(Of String), commandActions As List(Of Action))
        ' An open window: Switch to, Minimise / Restore, Close window - StageWindow.Kinds.vb.
        If item.Item.IsWindow Then
            AddWindowCommands(item, commandTexts, commandActions)
            Return
        End If

        ' A Clipboard history entry can be put back on the clipboard (as a double-click does).
        If ActiveTab.IsClipboardHistory Then
            commandTexts.Add(CopyToClipboardText)
            commandActions.Add(Sub() CopyClipboardEntry(item))
        End If

        ' A folder view shows the user's real files: the only command Stages adds is Delete,
        ' which follows Settings > Items > Deleting from folder Stages (StageWindow.Tabs.vb).
        If ActiveTab.IsFolderView Then
            commandTexts.Add(DeleteFolderItemText)
            commandActions.Add(Sub() DeleteFolderItems(New List(Of StageItemViewModel) From {item}))
            Return
        End If

        commandTexts.Add(RemoveFromStageText)
        commandActions.Add(Sub() RemoveItemFromStage(item))

        ' Only shortcuts can be renamed here: Stages never renames the user's real files.
        If item.Item.IsShortcut Then
            commandTexts.Add(RenameItemText)
            commandActions.Add(Sub() RenameItem(item))
        End If

        If item.Item.CanHaveCustomIcon Then
            commandTexts.Add(ChangeIconText)
            commandActions.Add(Sub() ChangeItemIcon(item))
            If item.Item.HasCustomIcon Then
                commandTexts.Add(ResetIconText)
                commandActions.Add(Sub() ResetItemIcon(item))
            End If
        End If
    End Sub

    ''' <summary>
    ''' Builds and shows the Stage's own menu. <paramref name="itemUnderMouse"/> may be Nothing.
    ''' It's a native Windows menu (NativePopupMenu), like the Explorer menu on items, so
    ''' both look the same and follow Settings > Appearance > Theme.
    ''' </summary>
    Private Sub ShowStageMenu(itemUnderMouse As StageItemViewModel)
        Dim chosenAction As Action = Nothing

        Using stageMenu As New NativePopupMenu()
            If itemUnderMouse IsNot Nothing Then
                Dim itemCommandTexts As New List(Of String)()
                Dim itemCommandActions As New List(Of Action)()
                AddItemCommands(itemUnderMouse, itemCommandTexts, itemCommandActions)
                For commandIndex As Integer = 0 To itemCommandTexts.Count - 1
                    stageMenu.AddItem(itemCommandTexts(commandIndex), itemCommandActions(commandIndex))
                Next
                If itemCommandTexts.Count > 0 Then stageMenu.AddSeparator()
            End If

            AddLayoutSubmenus(stageMenu) ' View, Sort by - StageWindow.Layout.vb
            stageMenu.AddSeparator()
            stageMenu.AddItem(LockMenuText, AddressOf ToggleLocked, isChecked:=_settings.Locked)
            stageMenu.AddItem(MinifyMenuText, AddressOf ToggleMinify, isChecked:=_settings.CanMinify)
            AddTitleClickSubmenu(stageMenu) ' click the title bar to roll up - StageWindow.TitleClick.vb
            stageMenu.AddItem(RenameStageMenuText, AddressOf RenameStage)
            stageMenu.AddItem(TitleSizeMenuText, AddressOf ChangeTitleHeight)
            AddBackgroundSubmenu(stageMenu)
            AddTabsSubmenu(stageMenu) ' StageWindow.Tabs.vb
            ' A Running tasks tab's folder only holds its settings: nothing worth opening.
            If Not ActiveTab.IsRunningTasks Then
                stageMenu.AddItem(If(ActiveTab.IsFolderView, OpenShownFolderMenuText, OpenStageFolderMenuText), AddressOf OpenStageFolder)
            End If
            ' Options of a Live folder / Running tasks / Clipboard history tab - StageWindow.Kinds.vb.
            AddKindMenuItems(stageMenu)
            stageMenu.AddSeparator()
            AddNewStageSubmenu(stageMenu) ' one entry per kind - StageWindow.Kinds.vb
            stageMenu.AddItem(RemoveStageMenuText, AddressOf RemoveThisStage)
            stageMenu.AddSeparator()
            stageMenu.AddItem(SettingsMenuText, AddressOf SettingsWindow.ShowSettings)
            ' Without the tray icon (Settings > General), Quit needs another home.
            If Not AppSettingsStore.Current.ShowTrayIcon Then
                stageMenu.AddItem(QuitStagesMenuText, AddressOf QuitStagesFromMenu)
            End If

            ' The menu blocks here until it closes; meanwhile the Stage must not minify.
            Dim cursorPosition As NativeMethods.NativePoint = DesktopIntegration.GetCursorScreenPosition()
            _isPopupOpen = True
            Try
                chosenAction = stageMenu.ShowAndGetChoice(_windowHandle, cursorPosition.X, cursorPosition.Y)
            Finally
                _isPopupOpen = False
            End Try
        End Using

        CheckWhetherMouseHasLeft()

        ' Run the choice once the menu has fully gone, so any dialog it opens appears cleanly.
        If chosenAction IsNot Nothing Then
            Dispatcher.InvokeAsync(chosenAction)
        End If
    End Sub

    ''' <summary>
    ''' "Background" sub-menu: "Default (...)" to follow the app-wide setting, then one
    ''' entry per BackdropStyle. The ticked entry is this Stage's current choice.
    ''' </summary>
    Private Sub AddBackgroundSubmenu(stageMenu As NativePopupMenu)
        Dim backgroundMenu As NativePopupMenu = stageMenu.AddSubmenu(BackgroundMenuText)
        Dim ownChoice As BackdropStyle? = _settings.Backdrop
        Dim defaultName As String = BackdropMenuName(AppSettingsStore.Current.DefaultBackdrop)

        backgroundMenu.AddItem(String.Format(DefaultBackgroundMenuFormat, defaultName),
                               Sub() SetBackdrop(Nothing),
                               isChecked:=Not ownChoice.HasValue)
        backgroundMenu.AddSeparator()
        For Each backdropOption As BackdropStyle In {BackdropStyle.Blur, BackdropStyle.Acrylic, BackdropStyle.Translucent}
            ' A copy for the lambda below, so each entry remembers its own style.
            Dim styleForEntry As BackdropStyle = backdropOption
            Dim isThisStagesChoice As Boolean = ownChoice.HasValue AndAlso ownChoice.Value = styleForEntry
            backgroundMenu.AddItem(BackdropMenuName(styleForEntry),
                                   Sub() SetBackdrop(styleForEntry),
                                   isChecked:=isThisStagesChoice)
        Next
        ' This Stage's own picture on top of the effect - StageWindow.Appearance.vb.
        backgroundMenu.AddSeparator()
        AddPictureMenuItems(backgroundMenu)
    End Sub

    ''' <summary>The name a BackdropStyle has in menus.</summary>
    Public Shared Function BackdropMenuName(backdropOption As BackdropStyle) As String
        Select Case backdropOption
            Case BackdropStyle.Acrylic
                Return AcrylicMenuText
            Case BackdropStyle.Translucent
                Return TranslucentMenuText
            Case Else
                Return BlurMenuText
        End Select
    End Function

    ''' <summary>
    ''' Switches this Stage's background effect and saves the choice.
    ''' Nothing = follow the app-wide default again.
    ''' </summary>
    Private Sub SetBackdrop(newStyle As BackdropStyle?)
        _settings.Backdrop = newStyle
        ApplyBackdropNow()
        SaveNow()

        ' Explain the one case where no choice can work.
        If Not DesktopIntegration.AreTransparencyEffectsEnabled() Then
            ShowMessage(TransparencyEffectsOffText, TransparencyOffCaption, MessageBoxButton.OK, MessageBoxImage.Information)
        End If
    End Sub

    ''' <summary>
    ''' Renames a shortcut on this Stage (the shortcut file in the Stage folder - never the
    ''' file it points to). It keeps its place in the Stage's order.
    ''' </summary>
    Private Sub RenameItem(item As StageItemViewModel)
        Dim dialog As New RenameDialog(item.Item.LabelWithoutExtension, RenameItemCaption) With {.Owner = Me}
        Dim wasConfirmed As Boolean
        _isPopupOpen = True
        Try
            wasConfirmed = dialog.ShowDialog().GetValueOrDefault()
        Finally
            _isPopupOpen = False
        End Try
        If Not wasConfirmed Then Return

        Try
            StageFolder.RenameShortcut(ActiveTab, item.FullPath, dialog.NewName)
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Logger.Warn("Could not rename " & item.FullPath, ex)
            ShowMessage(String.Format(RenameItemFailedFormat, ex.Message), RenameItemCaption,
                        MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End Try
        SaveActiveTab() ' the new name's place in ItemOrder (and its hand-placed position)
        RefreshItems()
    End Sub

    ''' <summary>
    ''' Lets the user pick a new icon for a shortcut with Windows' Change Icon dialog, and saves
    ''' it into the shortcut itself (as Explorer's Properties does). The Stage then reloads it.
    ''' </summary>
    Private Sub ChangeItemIcon(item As StageItemViewModel)
        Dim chosenIcon As ShortcutIcons.IconChoice = Nothing
        _isPopupOpen = True
        Try
            Using host As New DialogHost(Me) ' keeps Windows' Change Icon dialog in front
                chosenIcon = ShortcutIcons.AskForIcon(host.Handle, item.Item)
            End Using
        Finally
            _isPopupOpen = False
        End Try
        If chosenIcon Is Nothing Then Return ' cancelled

        If Not ShortcutIcons.SetIcon(item.Item, chosenIcon) Then
            ShowMessage(ChangeIconFailedText, ChangeIconCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
        End If
        RefreshItems() ' the shortcut file changed, so its picture is loaded afresh
        CheckWhetherMouseHasLeft()
    End Sub

    ''' <summary>Removes a shortcut's custom icon, so it shows its target's own icon again.</summary>
    Private Sub ResetItemIcon(item As StageItemViewModel)
        If Not ShortcutIcons.SetIcon(item.Item, Nothing) Then
            ShowMessage(ChangeIconFailedText, ChangeIconCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
        End If
        RefreshItems()
    End Sub

    ''' <summary>
    ''' Sends the item's shortcut to the Recycle Bin (restorable from there). The file
    ''' or folder it points at is not affected.
    ''' </summary>
    Private Sub RemoveItemFromStage(item As StageItemViewModel)
        RemoveItemsFromStage(New List(Of StageItemViewModel) From {item})
    End Sub

    ''' <summary>
    ''' Sends each item's shortcut to the Recycle Bin (restorable from there). Never in a
    ''' folder view, whose items are the user's real files. Stops at the first failure.
    ''' </summary>
    Private Sub RemoveItemsFromStage(itemsToRemove As List(Of StageItemViewModel))
        If Not CanRemoveItems() Then Return

        For Each item As StageItemViewModel In itemsToRemove
            Try
                StageFolder.SendToRecycleBin(item.FullPath)
            Catch ex As OperationCanceledException
                Exit For ' the user cancelled a Windows error dialog
            Catch ex As Exception
                Logger.Error("Could not remove " & item.FullPath & " from Stage '" & _settings.Name & "'", ex)
                ShowMessage(RemoveItemFailedText, RemoveItemCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
                Exit For
            End Try

            Dim removedName As String = item.Item.FileName
            ActiveTab.ItemOrder.RemoveAll(Function(fileName) String.Equals(fileName, removedName, StringComparison.OrdinalIgnoreCase))
            ActiveTab.ItemPositions.RemoveAll(Function(placed) String.Equals(placed.FileName, removedName, StringComparison.OrdinalIgnoreCase))
        Next

        SaveActiveTab()
        RefreshItems()
    End Sub

    ''' <summary>Stage menu > Quit Stages (shown only while the tray icon is hidden).</summary>
    Private Shared Sub QuitStagesFromMenu()
        Logger.Info("Quit chosen from a Stage menu")
        StageManager.Instance.ShutDown()
    End Sub

    Private Sub ToggleLocked()
        _settings.Locked = Not _settings.Locked
        SaveNow()
    End Sub

    Private Sub ToggleMinify()
        _settings.CanMinify = Not _settings.CanMinify
        If Not _settings.CanMinify AndAlso Not _settings.IsRolledUp Then
            ' It becomes a normal Stage: open, title bar on top. One that's open (or rolling)
            ' upwards first stops where it is with its title bar back on top...
            If _isOpenedUpward Then
                StopMinifyAnimation()
                SettleAsNormalStage()
            End If
            ' ...then anything still shut opens downwards, the normal way.
            ExpandFromTitleBar(mayOpenUpward:=False)
        End If
        SaveNow()
    End Sub

    ''' <summary>Renames the Stage and its folder (the folder watcher is paused during the move).</summary>
    Private Sub RenameStage()
        Dim dialog As New RenameDialog(_settings.Name) With {.Owner = Me}
        Dim wasConfirmed As Boolean
        _isPopupOpen = True
        Try
            wasConfirmed = dialog.ShowDialog().GetValueOrDefault()
        Finally
            _isPopupOpen = False
        End Try
        If Not wasConfirmed Then Return
        ApplyStageRename(dialog.NewName)
    End Sub

    ''' <summary>
    ''' Gives the Stage a new title and renames its folder to match (the folder watcher is
    ''' paused during the move). Used by Rename Stage... and by editing the title in place.
    ''' </summary>
    Private Sub ApplyStageRename(newTitle As String)
        StopWatchingFolder()
        Dim folderWasRenamed As Boolean = StageManager.Instance.RenameStage(_settings, newTitle)
        FollowTabsToRenamedFolder() ' tabs live inside the renamed folder
        StartWatchingFolder()
        ApplyName(_settings.Name)
        RebuildTabHeaders() ' the first tab shows the Stage's name
        RefreshItems() ' item paths now live under the new folder name

        If Not folderWasRenamed Then
            ShowMessage(String.Format(FolderRenameFailedFormat, _settings.FolderPath, Environment.NewLine),
                        RenameStageCaption, MessageBoxButton.OK, MessageBoxImage.Information)
        End If
    End Sub

    ''' <summary>
    ''' Lets the user pick this Stage's title height. Picking the same height as the
    ''' app-wide default (or "Restore default") makes the Stage follow the default again,
    ''' so later changes to the default in Settings apply to it too.
    ''' </summary>
    Private Sub ChangeTitleHeight()
        Dim defaultHeight As Integer = AppSettingsStore.Current.DefaultTitleHeight
        Dim dialog As New TitleHeightDialog(_titleHeight, defaultHeight) With {.Owner = Me}
        _isPopupOpen = True
        Try
            If dialog.ShowDialog().GetValueOrDefault() Then
                Dim chosenHeight As Integer = StageDefaults.SanitizeTitleHeight(dialog.SelectedTitleHeight)
                If chosenHeight = defaultHeight Then
                    _settings.TitleHeight = Nothing
                Else
                    _settings.TitleHeight = chosenHeight
                End If
                ApplyEffectiveTitleHeight()
                SaveNow()
            End If
        Finally
            _isPopupOpen = False
        End Try
    End Sub

    ''' <summary>Opens the open tab's folder in Explorer (for a folder view, the folder it shows).</summary>
    Private Sub OpenStageFolder()
        StageFolder.OpenInExplorer(ActiveTab.ContentFolderPath)
    End Sub

    Private Sub CreateNewStage()
        StageManager.Instance.CreateStage(StageDefaults.NewStageName)
    End Sub

    ''' <summary>
    ''' Removes this Stage, sending its folder to the Recycle Bin. Refuses (and explains)
    ''' if the folder holds anything other than shortcuts, so a real file is never deleted.
    ''' </summary>
    Private Sub RemoveThisStage()
        Dim realEntries As List(Of String) = StageFolder.FindNonShortcutEntries(_settings.FolderPath)
        If realEntries.Count > 0 Then
            Dim message As String = String.Format(RealFilesInStageFormat, realEntries.Count, _settings.FolderPath, Environment.NewLine)
            If ShowMessage(message, RemoveStageCaption, MessageBoxButton.YesNo, MessageBoxImage.Warning) = MessageBoxResult.Yes Then
                OpenStageFolder()
            End If
            Return
        End If

        Dim confirmationText As String = If(_settings.IsFolderView, RemoveFolderStageConfirmationText, RemoveStageConfirmationText)
        ' Running tasks and Clipboard history have texts of their own - StageWindow.Kinds.vb.
        confirmationText = If(RemoveConfirmationForKind(_settings), confirmationText)
        If ShowMessage(confirmationText, RemoveStageCaption, MessageBoxButton.YesNo, MessageBoxImage.Question) <> MessageBoxResult.Yes Then
            Return
        End If

        If Not StageManager.Instance.RemoveStage(Me) Then
            ShowMessage(RemoveStageFailedText, RemoveStageCaption, MessageBoxButton.OK, MessageBoxImage.Warning)
        End If
    End Sub

    ''' <summary>Shows a message box owned by this Stage, keeping it expanded while open.</summary>
    Private Function ShowMessage(text As String, caption As String, buttons As MessageBoxButton, icon As MessageBoxImage) As MessageBoxResult
        _isPopupOpen = True
        Try
            ' Owned by an always-on-top host, so it can't open behind other windows (DialogHost.vb).
            Using host As New DialogHost(Me)
                Return MessageBox.Show(host.OwnerWindow, text, caption, buttons, icon)
            End Using
        Finally
            _isPopupOpen = False
        End Try
    End Function

    ''' <summary>
    ''' Title bar: a double-click renames the Stage in place; a single press-and-drag moves
    ''' the Stage (unless it is locked); a single click without moving may roll the Stage up
    ''' or down (StageWindow.TitleClick.vb). Renaming works on locked Stages too, as the Stage
    ''' menu's Rename Stage... always has.
    ''' </summary>
    Private Sub TitleBar_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        If _isEditingTitle Then Return ' clicks belong to the edit box

        If e.ClickCount = DoubleClickCount Then
            CancelPendingTitleClick() ' the first click of the pair mustn't roll the Stage
            e.Handled = True
            ' With tabs showing, a double-click on the bar (not on a tab) renames the open tab.
            If HasTabs Then
                RenameTab(ActiveTab)
            Else
                BeginTitleEdit()
            End If
            Return
        End If

        ' DragMove only returns once the button is released. Comparing the position before
        ' and after tells a click (didn't move) from a drag. A locked Stage can't be moved,
        ' but can still be rolled up.
        Dim leftBefore As Double = Left
        Dim topBefore As Double = Top
        If Not _settings.Locked Then
            Try
                DragMove()
            Catch ex As InvalidOperationException
                ' DragMove throws if the button was already released by the time it runs - harmless.
            End Try
        End If
        Dim stageMoved As Boolean = Left <> leftBefore OrElse Top <> topBefore
        OnTitleBarPressReleased(stageMoved, TryCast(e.OriginalSource, DependencyObject))
    End Sub

    ''' <summary>Swaps the title for an edit box holding the current title, all selected.</summary>
    Private Sub BeginTitleEdit()
        _isEditingTitle = True
        _isPopupOpen = True ' a minified Stage stays open while typing

        TitleEditBox.FontSize = TitleText.FontSize
        TitleEditBox.Text = _settings.Name
        TitleText.Visibility = Visibility.Hidden
        TitleEditBox.Visibility = Visibility.Visible

        ' A Stage never takes keyboard focus on its own; typing needs it. The z-order hook
        ' still keeps the Stage behind other windows.
        Activate()
        TitleEditBox.Focus()
        TitleEditBox.SelectAll()
    End Sub

    ''' <summary>
    ''' Puts the title back. With <paramref name="keepChanges"/>, a new non-empty title is
    ''' applied (renaming the folder too); otherwise the edit is thrown away.
    ''' </summary>
    Private Sub EndTitleEdit(keepChanges As Boolean)
        ' Hiding the edit box makes it lose focus, which calls this again - only act once.
        If Not _isEditingTitle Then Return
        _isEditingTitle = False

        Dim typedTitle As String = TitleEditBox.Text.Trim()
        TitleEditBox.Visibility = Visibility.Collapsed
        TitleText.Visibility = Visibility.Visible
        _isPopupOpen = False

        Dim titleChanged As Boolean = Not String.Equals(typedTitle, _settings.Name, StringComparison.Ordinal)
        If keepChanges AndAlso typedTitle.Length > 0 AndAlso titleChanged Then
            ApplyStageRename(typedTitle)
        End If
        CheckWhetherMouseHasLeft()
    End Sub

    Private Sub TitleEditBox_KeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.Key
            Case Key.Enter
                e.Handled = True
                EndTitleEdit(keepChanges:=True)
            Case Key.Escape
                e.Handled = True
                EndTitleEdit(keepChanges:=False)
        End Select
    End Sub

    ''' <summary>Clicking elsewhere (or switching windows) keeps what was typed, like Explorer's rename.</summary>
    Private Sub TitleEditBox_LostKeyboardFocus(sender As Object, e As KeyboardFocusChangedEventArgs)
        EndTitleEdit(keepChanges:=True)
    End Sub

#End Region

#Region "Saving position and size"

    Private Sub Window_LocationChanged(sender As Object, e As EventArgs)
        If Double.IsNaN(Left) OrElse Double.IsNaN(Top) Then Return

        Dim newLeft As Integer = CInt(Math.Round(Left))
        ' Usually the window's top; for a Stage open upwards, where its title bar sits.
        Dim newTop As Integer = CInt(Math.Round(SavedTopPosition()))
        If newLeft <> _settings.PosX OrElse newTop <> _settings.PosY Then
            _settings.PosX = newLeft
            _settings.PosY = newTop
            ScheduleSave()
        End If
    End Sub

    Private Sub Window_SizeChanged(sender As Object, e As SizeChangedEventArgs)
        Dim newWidth As Integer = CInt(Math.Round(ActualWidth))
        Dim changed As Boolean = newWidth <> _settings.Width
        _settings.Width = newWidth

        ' While minified the window is only title-high, and while rolling open or shut its
        ' height is changing by itself; keep remembering the real expanded height.
        If Not _isMinified AndAlso Not _isAnimatingMinify Then
            Dim newHeight As Integer = CInt(Math.Round(ActualHeight))
            changed = changed OrElse newHeight <> _settings.Height
            _settings.Height = newHeight
        End If

        If changed Then
            ScheduleSave()
        End If
    End Sub

    Private Sub Window_StateChanged(sender As Object, e As EventArgs)
        ' Belt and braces: whatever minimised/maximised us, go straight back to normal.
        If WindowState <> WindowState.Normal Then
            WindowState = WindowState.Normal
        End If
    End Sub

    ''' <summary>
    ''' Restarts the save countdown. Dragging fires dozens of moves per second;
    ''' this writes the file once, a second after the movement stops.
    ''' </summary>
    Private Sub ScheduleSave()
        _saveTimer.Stop()
        _saveTimer.Start()
    End Sub

    Private Sub SaveTimer_Tick(sender As Object, e As EventArgs)
        SaveNow()
    End Sub

    ''' <summary>Writes any change still waiting for its delayed save (used before a backup).</summary>
    Public Sub SavePendingChanges()
        SaveNow()
    End Sub

    ''' <summary>Writes the settings immediately (cancelling any pending delayed save).</summary>
    Private Sub SaveNow()
        _saveTimer.Stop()
        If IsBeingRemoved Then Return ' the folder is being removed - don't recreate it
        StageManager.Instance.SaveStage(_settings)
    End Sub

#End Region

#Region "Health watchdog"

    ''' <summary>
    ''' Every few seconds, make sure nothing has quietly detached or hidden the Stage.
    ''' Covers cases where Explorer swaps its desktop window without broadcasting
    ''' "TaskbarCreated", and shell extensions that hide windows they don't own.
    ''' </summary>
    Private Sub DesktopWatchdogTimer_Tick(sender As Object, e As EventArgs)
        If _windowHandle = IntPtr.Zero Then Return

        If Not DesktopIntegration.IsGluedToDesktop(_windowHandle) Then
            Logger.Info("Stage '" & _settings.Name & "' had lost its desktop attachment - re-attaching")
            DesktopIntegration.PrepareStageWindow(_windowHandle)
            ApplyBackdropNow()
        End If

        ' A Stage hidden from the tray menu is meant to stay hidden.
        If StageManager.Instance.AreStagesHidden Then Return

        If DesktopIntegration.EnsureWindowVisible(_windowHandle) Then
            Logger.Info("Stage '" & _settings.Name & "' had been hidden - shown again")
        End If
    End Sub

#End Region

#Region "Closing"

    ''' <summary>
    ''' Stages only close through "Remove Stage" or when the app exits. Anything else
    ''' (Alt+F4 on a focused Stage, a stray WM_CLOSE) is refused, so a Stage can't be
    ''' closed by accident.
    ''' </summary>
    Protected Overrides Sub OnClosing(e As CancelEventArgs)
        MyBase.OnClosing(e)
        If Not IsBeingRemoved AndAlso Not StageManager.Instance.IsShuttingDown Then
            e.Cancel = True
        End If
    End Sub

    ''' <summary>
    ''' Runs however the window ended - including when Windows destroyed it during an
    ''' Explorer restart (that path skips OnClosing entirely). StageManager decides
    ''' whether to bring it back.
    ''' </summary>
    Protected Overrides Sub OnClosed(e As EventArgs)
        _isClosed = True
        StopWatchingFolder()
        UpdateClipboardListening() ' stops listening for copies (closed Stages don't capture)
        UpdateKindTimers()         ' stops the Running tasks timer
        EndSnapping()              ' in case it closed mid-move
        _scanWorker.Shutdown() ' its thread ends after any scan in progress
        StopMinifyAnimation()
        _mouseWatchTimer.Stop()
        _itemRefreshTimer.Stop()
        _desktopWatchdogTimer.Stop()

        ' Don't lose a move/resize that was still waiting to be saved.
        If _saveTimer.IsEnabled Then
            SaveNow()
        End If

        MyBase.OnClosed(e)
        StageManager.Instance.OnStageWindowClosed(Me)
    End Sub

#End Region

End Class
