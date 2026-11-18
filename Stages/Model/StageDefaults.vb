' ============================================================================
'  StageDefaults.vb
'  Named defaults and limits for Stages, so no "magic numbers" are scattered
'  through the code. These are the BUILT-IN defaults: the user's own choices
'  live in AppSettings (settings.xml) and start out equal to these.
' ============================================================================
Namespace Model

    Public NotInheritable Class StageDefaults

        Private Sub New()
        End Sub

        ' --- Names ---------------------------------------------------------------
        Public Const FirstStageName As String = "My First Stage"
        Public Const NewStageName As String = "New Stage"
        ''' <summary>Titles given to new Stages of the other kinds (a Live folder is named after its folder).</summary>
        Public Const NewRunningTasksStageName As String = "Running tasks"
        Public Const NewClipboardStageName As String = "Clipboard history"

        ' --- Geometry for a brand new Stage (device-independent pixels) ---------
        Public Const NewStageLeft As Integer = 100
        Public Const NewStageTop As Integer = 250
        Public Const NewStageWidth As Integer = 300
        Public Const NewStageHeight As Integer = 300
        ''' <summary>Each new Stage is nudged by this much if another Stage already sits at the same spot.</summary>
        Public Const NewStageCascadeOffset As Integer = 30
        ''' <summary>Stop cascading after this many steps (then just overlap).</summary>
        Public Const MaximumCascadeSteps As Integer = 20

        ' --- Size limits ---------------------------------------------------------
        Public Const MinimumStageWidth As Integer = 100

        ' --- Title bar height ----------------------------------------------------
        Public Const DefaultTitleHeight As Integer = 35
        Public Const MinimumTitleHeight As Integer = 16
        Public Const MaximumTitleHeight As Integer = 100

        ''' <summary>
        ''' Returns a valid title height. Out-of-range values (e.g. from a hand-edited
        ''' settings file) fall back to the built-in default.
        ''' </summary>
        Public Shared Function SanitizeTitleHeight(requestedHeight As Integer) As Integer
            If requestedHeight < MinimumTitleHeight OrElse requestedHeight > MaximumTitleHeight Then
                Return DefaultTitleHeight
            End If
            Return requestedHeight
        End Function

        ' --- Behaviour of new Stages ---------------------------------------------
        ''' <summary>Built-in background for Stages that don't choose their own.</summary>
        Public Const DefaultBackdrop As BackdropStyle = BackdropStyle.Blur
        ''' <summary>Whether brand new Stages start with Minify switched on.</summary>
        Public Const DefaultNewStagesMinify As Boolean = False

        ''' <summary>The Stages icon shows in the notification area unless switched off in Settings > General.</summary>
        Public Const DefaultShowTrayIcon As Boolean = True

        ''' <summary>Snapping looks above and below too, unless "Only snap side by side" is ticked.</summary>
        Public Const DefaultSnapSideBySideOnly As Boolean = False

        ''' <summary>
        ''' The Details view's columns, in order, for a tab that hasn't changed them (Explorer's
        ''' usual set). "ReadOnly" array: the list itself can't be swapped; copy it before changing.
        ''' </summary>
        Public Shared ReadOnly DefaultDetailsColumns As StageDetailsColumn() =
            {StageDetailsColumn.Name, StageDetailsColumn.Type, StageDetailsColumn.DateModified, StageDetailsColumn.Size}
        ''' <summary>Clicking a title bar does nothing by default (it can roll the Stage up when switched on).</summary>
        Public Const DefaultTitleClickRollsUp As Boolean = False

        ' --- Minify animation (Settings > General > Minified Stages) ---------------------
        ''' <summary>Off by default: a minified Stage opens and closes instantly, as it always has.</summary>
        Public Const DefaultAnimateMinify As Boolean = False
        ''' <summary>Minified Stages open downwards unless Settings says otherwise.</summary>
        Public Const DefaultMinifyOpenDirection As StageOpenDirection = StageOpenDirection.Down
        ''' <summary>How long the roll open / roll shut takes, in milliseconds.</summary>
        Public Const DefaultMinifyAnimationMilliseconds As Integer = 200
        Public Const MinimumMinifyAnimationMilliseconds As Integer = 100
        Public Const MaximumMinifyAnimationMilliseconds As Integer = 600

        ''' <summary>Light/dark for Settings, dialogs and menus: dark unless the user picks another (Dan's choice).</summary>
        Public Const DefaultTheme As AppTheme = AppTheme.Dark

        ''' <summary>Item labels show extensions exactly when File Explorer would.</summary>
        Public Const DefaultFileExtensionDisplay As FileExtensionDisplay = FileExtensionDisplay.SameAsWindows
        ''' <summary>Delete on a folder Stage: ask, then send to the Recycle Bin.</summary>
        Public Const DefaultFolderDeleteMode As FolderDeleteMode = FolderDeleteMode.RecycleBin

        ' --- Hide / show all Stages shortcut (Settings > General) -------------------------
        ''' <summary>Default shortcut: Ctrl+Alt+Shift+H ("H" for hide). Rarely used by other programs.</summary>
        Public Const DefaultToggleStagesHotkeyModifiers As ModifierKeys = ModifierKeys.Control Or ModifierKeys.Alt Or ModifierKeys.Shift
        Public Const DefaultToggleStagesHotkeyKey As Key = Key.H

        ' --- Appearance (Settings > Appearance) ------------------------------------
        ' Colours are "#RRGGBB" text; how see-through they are is a separate percentage
        ' (0 = invisible, 100 = solid), which is easier to understand and edit by hand
        ' than the alpha byte of a "#AARRGGBB" colour.

        ''' <summary>Dark tint over the blur, as in NoFences (black at alpha 100 of 255, about 39%).</summary>
        Public Const DefaultStageTintColor As String = "#000000"
        Public Const DefaultStageTintOpacityPercent As Integer = 39

        ''' <summary>Imitation acrylic's grey tint (alpha 128 of 255 = 50%).</summary>
        Public Const DefaultAcrylicTintColor As String = "#202020"
        Public Const DefaultAcrylicTintOpacityPercent As Integer = 50
        ''' <summary>Imitation acrylic's faint white "sheen" (alpha 15 of 255, about 6%).</summary>
        Public Const DefaultAcrylicSheenOpacityPercent As Integer = 6
        ''' <summary>Imitation acrylic's noise grain.</summary>
        Public Const DefaultAcrylicGrainOpacityPercent As Integer = 4

        ''' <summary>Lowest and highest allowed value for any percentage setting.</summary>
        Public Const MinimumPercent As Integer = 0
        Public Const MaximumPercent As Integer = 100
        ''' <summary>The sheen and grain look broken above this, so their sliders stop here.</summary>
        Public Const MaximumAcrylicDetailPercent As Integer = 30

        ' --- Stage text and frame (Settings > Appearance) ------------------------------
        ' Font sizes are in points (1/72 inch), as in every Windows font picker.

        ''' <summary>Font of Stage titles and item labels.</summary>
        Public Const DefaultStageFontFamily As String = "Segoe UI"
        ''' <summary>Item labels: 9pt, the size NoFences and the Windows desktop use.</summary>
        Public Const DefaultLabelFontSizePoints As Integer = 9
        Public Const MinimumLabelFontSizePoints As Integer = 7
        Public Const MaximumLabelFontSizePoints As Integer = 18
        ''' <summary>Title font size 0 means "automatic": half the title bar's height, as before.</summary>
        Public Const AutomaticTitleFontSize As Integer = 0
        Public Const MinimumTitleFontSizePoints As Integer = 7
        Public Const MaximumTitleFontSizePoints As Integer = 36

        ''' <summary>The title bar's extra darkening (NoFences: black at alpha 50 of 255, about 20%).</summary>
        Public Const DefaultTitleBarColor As String = "#000000"
        Public Const DefaultTitleBarOpacityPercent As Integer = 20
        ''' <summary>Title and label text: white.</summary>
        Public Const DefaultTitleTextColor As String = "#FFFFFF"
        Public Const DefaultLabelTextColor As String = "#FFFFFF"

        ''' <summary>Outline round each Stage: none (0px) by default; white at 40% when switched on.</summary>
        Public Const DefaultBorderColor As String = "#FFFFFF"
        Public Const DefaultBorderOpacityPercent As Integer = 40
        Public Const DefaultBorderThickness As Integer = 0
        Public Const MaximumBorderThickness As Integer = 6
        Public Const DefaultCornerStyle As StageCornerStyle = StageCornerStyle.Rounded
        ''' <summary>Titles (and tabs) in the middle of the title bar.</summary>
        Public Const DefaultTitleAlignment As StageTitleAlignment = StageTitleAlignment.Center
        ''' <summary>
        ''' The open tab is just brighter than the others, with no marking (v0.8.8, Dan's choice).
        ''' Used for a new settings file and by Restore default appearance; a saved choice is kept.
        ''' </summary>
        Public Const DefaultTabStyle As StageTabStyle = StageTabStyle.Clear
        ''' <summary>
        ''' The underline / outline colour: empty = Automatic (v0.8.6). Automatic means the
        ''' Outlined look uses the Stage's own colour, AutomaticOutlineLightenPercent lighter,
        ''' and the Underline look uses AutomaticUnderlineColor.
        ''' </summary>
        Public Const DefaultTabAccentColor As String = ""
        ''' <summary>Automatic underline colour: a light blue like Windows 11's default accent.</summary>
        Public Const AutomaticUnderlineColor As String = "#4CC2FF"
        ''' <summary>Automatic outline: the Stage's colour, this many percent of the way to white.</summary>
        Public Const AutomaticOutlineLightenPercent As Integer = 5
        ''' <summary>
        ''' The tab colour v0.8.5 saved before Automatic existed. A settings file holding exactly
        ''' this is read as Automatic, so it gets the new default rather than keeping the old one.
        ''' </summary>
        Public Const PreviousDefaultTabAccentColor As String = "#4CC2FF"

        ' --- A Stage's own background picture (Background > Picture...) ------------------
        Public Const DefaultPictureOpacityPercent As Integer = 100
        ''' <summary>A dark shade over the picture keeps white labels readable on bright pictures.</summary>
        Public Const DefaultPictureShadePercent As Integer = 30
        ''' <summary>Blur strength in pixels (0 = sharp).</summary>
        Public Const DefaultPictureBlurRadius As Integer = 0
        Public Const MaximumPictureBlurRadius As Integer = 40
        Public Const DefaultPictureFit As BackgroundPictureFit = BackgroundPictureFit.Fill

        ' --- Lining Stages up (Settings > Desktop > Arranging Stages) ----------------------
        ' Distances are in device-independent pixels (the same as real pixels at 100% scaling);
        ' the Stage converts them for its screen's scaling while it is moved.

        ''' <summary>
        ''' By default a moved Stage snaps only when brought up beside another one (v0.8.2;
        ''' v0.8.0 snapped to every Stage and screen edge, which got in the way).
        ''' </summary>
        Public Const DefaultStageSnapping As StageSnapMode = StageSnapMode.BesideStages
        ''' <summary>
        ''' Stages are kept from overlapping by default: a Stage let go of on top of another
        ''' slides to the nearest free spot (hold Alt while moving to allow overlapping).
        ''' </summary>
        Public Const DefaultPreventStageOverlap As Boolean = True
        ''' <summary>Coloured lines show what a Stage has lined up with while it is moved.</summary>
        Public Const DefaultShowSnapGuides As Boolean = True
        ''' <summary>The minimum gap kept between two Stages (Dan's choice: 2px to begin with).</summary>
        Public Const DefaultStageGapPixels As Integer = 2
        Public Const MinimumStageGapPixels As Integer = 0
        Public Const MaximumStageGapPixels As Integer = 40
        ''' <summary>How close an edge must come before it snaps into line.</summary>
        Public Const SnapDistancePixels As Integer = 10
        ''' <summary>Thickness of the guide lines.</summary>
        Public Const SnapGuideThicknessPixels As Integer = 2
        ''' <summary>Guide line colour: a bright cyan that stands out against most wallpapers and Stage tints.</summary>
        Public Const DefaultSnapGuideColor As String = "#00C8FF"

        ' --- Live folder Stages (Stage menu > Live folder options...) ----------------------
        ''' <summary>0 = only the folder itself; 1 = also its sub-folders; and so on.</summary>
        Public Const DefaultLiveFolderDepth As Integer = 0
        Public Const MaximumLiveFolderDepth As Integer = 5
        ''' <summary>A folder with more entries than this shows only the first ones (protects against huge folders).</summary>
        Public Const DefaultLiveFolderMaximumItems As Integer = 1000
        Public Const MinimumLiveFolderMaximumItems As Integer = 50
        Public Const MaximumLiveFolderMaximumItems As Integer = 5000

        ' --- Running tasks Stages (Stage menu > Running tasks options...) ------------------
        ''' <summary>How often the list of windows is read again, in seconds.</summary>
        Public Const DefaultRunningTasksIntervalSeconds As Integer = 2
        Public Const MinimumRunningTasksIntervalSeconds As Integer = 1
        Public Const MaximumRunningTasksIntervalSeconds As Integer = 60

        ' --- Clipboard history Stages (Stage menu > Clipboard history options...) ----------
        ''' <summary>The newest this many entries are kept; older ones are deleted.</summary>
        Public Const DefaultClipboardMaximumEntries As Integer = 50
        Public Const MinimumClipboardMaximumEntries As Integer = 5
        Public Const MaximumClipboardMaximumEntries As Integer = 500
        ''' <summary>The entries together may take at most this many megabytes (Dan's suggestion: 50 MB).</summary>
        Public Const DefaultClipboardMaximumMegabytes As Integer = 50
        Public Const MinimumClipboardMaximumMegabytes As Integer = 5
        Public Const MaximumClipboardMaximumMegabytes As Integer = 500

        ''' <summary>Keeps a percentage inside <paramref name="lowest"/>..<paramref name="highest"/>.</summary>
        Public Shared Function ClampPercent(requestedPercent As Integer, lowest As Integer, highest As Integer) As Integer
            Return Math.Max(lowest, Math.Min(highest, requestedPercent))
        End Function

    End Class

End Namespace
