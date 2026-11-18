' ============================================================================
'  AppSettings.vb
'  App-wide settings (Settings window), saved as XML to
'      %LOCALAPPDATA%\Programs\Stages\settings.xml
'  next to App\ and Data\ rather than inside either: updates never touch it,
'  and it can't be mistaken for a Stage folder.
'
'  Per-Stage settings live in each Stage's own folder (StageInfo). A Stage
'  follows the defaults here unless it has its own Backdrop / TitleHeight.
'
'  Every value is checked by Sanitize() after loading, so a hand-edited or
'  damaged file can never feed Stages an impossible value.
' ============================================================================
Imports System.Xml.Serialization

Namespace Model

    <XmlRoot("StagesSettings")>
    Public Class AppSettings

        ' --- General ----------------------------------------------------------------

        ''' <summary>Background for every Stage that hasn't chosen its own.</summary>
        Public Property DefaultBackdrop As BackdropStyle = StageDefaults.DefaultBackdrop

        ''' <summary>Title bar height for every Stage that hasn't chosen its own.</summary>
        Public Property DefaultTitleHeight As Integer = StageDefaults.DefaultTitleHeight

        ''' <summary>Whether a newly created Stage starts with Minify on (each Stage can change it later).</summary>
        Public Property NewStagesMinify As Boolean = StageDefaults.DefaultNewStagesMinify

        ''' <summary>
        ''' True = a single click on a Stage's title bar rolls it up to the title bar (another
        ''' click rolls it down), for every Stage that hasn't chosen for itself (Stage menu >
        ''' Title bar click). Off by default.
        ''' </summary>
        Public Property TitleClickRollsUp As Boolean = StageDefaults.DefaultTitleClickRollsUp

        ''' <summary>
        ''' True = the Stages icon shows in the notification area (system tray). False = no icon;
        ''' Stages keeps running, and starting Stages again opens Settings (Program.vb).
        ''' </summary>
        Public Property ShowTrayIcon As Boolean = StageDefaults.DefaultShowTrayIcon

        ''' <summary>
        ''' True = while moving or resizing, a Stage lines up only with Stages in the same row
        ''' (beside it), never with Stages above or below (StageSnapper). Off by default.
        ''' </summary>
        Public Property SnapSideBySideOnly As Boolean = StageDefaults.DefaultSnapSideBySideOnly

        ''' <summary>True = minified Stages roll open and shut; False = they change size instantly.</summary>
        Public Property AnimateMinify As Boolean = StageDefaults.DefaultAnimateMinify

        ''' <summary>Which way minified Stages open: downwards, upwards, or Automatic (StageWindow.OpenDirection.vb).</summary>
        Public Property MinifyOpenDirection As StageOpenDirection = StageDefaults.DefaultMinifyOpenDirection
        ''' <summary>How long the roll takes, in milliseconds (100-600).</summary>
        Public Property MinifyAnimationMilliseconds As Integer = StageDefaults.DefaultMinifyAnimationMilliseconds

        ''' <summary>Modifier keys of the hide/show-all-Stages shortcut (written in the XML as e.g. "Alt Control Shift").</summary>
        Public Property ToggleStagesHotkeyModifiers As ModifierKeys = StageDefaults.DefaultToggleStagesHotkeyModifiers
        ''' <summary>Main key of that shortcut; Key.None ("None") switches the shortcut off.</summary>
        Public Property ToggleStagesHotkeyKey As Key = StageDefaults.DefaultToggleStagesHotkeyKey

        ' --- Items ----------------------------------------------------------------------

        ''' <summary>Whether item labels end in ".docx" etc. (Settings > Items).</summary>
        Public Property FileExtensions As FileExtensionDisplay = StageDefaults.DefaultFileExtensionDisplay

        ''' <summary>What Delete does to real files on a folder Stage (Settings > Items).</summary>
        Public Property FolderDelete As FolderDeleteMode = StageDefaults.DefaultFolderDeleteMode

        ' --- Desktop: arranging Stages (Settings > Desktop) ----------------------------------

        ''' <summary>
        ''' How much a Stage being moved or resized lines up with others: Off, only beside another
        ''' Stage (default), or Full (every Stage and the screen edges). Replaces v0.8.0's
        ''' SnapStages on/off, which older settings files may still contain (ignored when read).
        ''' </summary>
        Public Property StageSnapping As StageSnapMode = StageDefaults.DefaultStageSnapping
        ''' <summary>
        ''' Stages are kept StageGapPixels apart: a moved Stage let go of over another slides to the
        ''' nearest free spot, and a resized edge stops short of the next Stage.
        ''' </summary>
        Public Property PreventStageOverlap As Boolean = StageDefaults.DefaultPreventStageOverlap
        ''' <summary>Show coloured lines between Stages that are in line while one is moved.</summary>
        Public Property ShowSnapGuides As Boolean = StageDefaults.DefaultShowSnapGuides
        ''' <summary>The minimum space between two Stages, in pixels at 100% scaling (0-40).</summary>
        Public Property StageGapPixels As Integer = StageDefaults.DefaultStageGapPixels
        ''' <summary>Colour of those lines ("#RRGGBB").</summary>
        Public Property SnapGuideColor As String = StageDefaults.DefaultSnapGuideColor

        ' --- Appearance ---------------------------------------------------------------

        ''' <summary>Light or dark Settings window, dialogs and right-click menus (System = follow Windows).</summary>
        Public Property Theme As AppTheme = StageDefaults.DefaultTheme

        ''' <summary>Colour of the tint over the blur for Blur and Translucent Stages ("#RRGGBB").</summary>
        Public Property StageTintColor As String = StageDefaults.DefaultStageTintColor
        ''' <summary>How strong that tint is, 0-100%.</summary>
        Public Property StageTintOpacityPercent As Integer = StageDefaults.DefaultStageTintOpacityPercent

        ''' <summary>Colour of imitation acrylic's tint ("#RRGGBB").</summary>
        Public Property AcrylicTintColor As String = StageDefaults.DefaultAcrylicTintColor
        ''' <summary>How strong the acrylic tint is, 0-100%.</summary>
        Public Property AcrylicTintOpacityPercent As Integer = StageDefaults.DefaultAcrylicTintOpacityPercent
        ''' <summary>Strength of the faint white sheen under the acrylic tint, 0-30%.</summary>
        Public Property AcrylicSheenOpacityPercent As Integer = StageDefaults.DefaultAcrylicSheenOpacityPercent
        ''' <summary>Strength of the acrylic noise grain, 0-30%.</summary>
        Public Property AcrylicGrainOpacityPercent As Integer = StageDefaults.DefaultAcrylicGrainOpacityPercent

        ' Stage text and frame (Settings > Appearance > Stage text / Stage frame)

        ''' <summary>Font of titles and labels, by its family name (e.g. "Segoe UI").</summary>
        Public Property StageFontFamily As String = StageDefaults.DefaultStageFontFamily
        ''' <summary>Item label size in points.</summary>
        Public Property LabelFontSizePoints As Integer = StageDefaults.DefaultLabelFontSizePoints
        ''' <summary>Title size in points, or 0 (AutomaticTitleFontSize) for half the title bar's height.</summary>
        Public Property TitleFontSizePoints As Integer = StageDefaults.AutomaticTitleFontSize
        ''' <summary>Colour and strength of the title bar's extra darkening ("#RRGGBB" + %).</summary>
        Public Property TitleBarColor As String = StageDefaults.DefaultTitleBarColor
        Public Property TitleBarOpacityPercent As Integer = StageDefaults.DefaultTitleBarOpacityPercent
        ''' <summary>Colour of the title text.</summary>
        Public Property TitleTextColor As String = StageDefaults.DefaultTitleTextColor
        ''' <summary>Colour of the item labels.</summary>
        Public Property LabelTextColor As String = StageDefaults.DefaultLabelTextColor
        ''' <summary>Outline round each Stage: colour, strength and width in pixels (0 = no outline).</summary>
        Public Property BorderColor As String = StageDefaults.DefaultBorderColor
        Public Property BorderOpacityPercent As Integer = StageDefaults.DefaultBorderOpacityPercent
        Public Property BorderThickness As Integer = StageDefaults.DefaultBorderThickness
        ''' <summary>Rounded (Windows 11 only) or square corners.</summary>
        Public Property CornerStyle As StageCornerStyle = StageDefaults.DefaultCornerStyle

        ''' <summary>Where the title (and, with tabs, the row of tabs) sits in the title bar.</summary>
        Public Property TitleAlignment As StageTitleAlignment = StageDefaults.DefaultTitleAlignment

        ''' <summary>How the open tab is marked on a Stage with tabs.</summary>
        Public Property TabStyle As StageTabStyle = StageDefaults.DefaultTabStyle

        ''' <summary>
        ''' The colour of the open tab's underline or outline ("#RRGGBB"), or empty for Automatic
        ''' (see StageDefaults.DefaultTabAccentColor and StageWindow.RefreshTabAccentBrush).
        ''' </summary>
        Public Property TabAccentColor As String = StageDefaults.DefaultTabAccentColor

        ''' <summary>
        ''' Replaces any out-of-range or unreadable value with its built-in default (or the
        ''' nearest allowed value for percentages). Called after every load.
        ''' </summary>
        Public Sub Sanitize()
            If Not [Enum].IsDefined(GetType(BackdropStyle), DefaultBackdrop) Then
                DefaultBackdrop = StageDefaults.DefaultBackdrop
            End If
            DefaultTitleHeight = StageDefaults.SanitizeTitleHeight(DefaultTitleHeight)
            If Not [Enum].IsDefined(GetType(AppTheme), Theme) Then
                Theme = StageDefaults.DefaultTheme
            End If
            If Not [Enum].IsDefined(GetType(Key), ToggleStagesHotkeyKey) Then
                ToggleStagesHotkeyKey = Key.None ' unreadable: switch the shortcut off rather than guess
            End If
            If Not [Enum].IsDefined(GetType(FileExtensionDisplay), FileExtensions) Then
                FileExtensions = StageDefaults.DefaultFileExtensionDisplay
            End If
            ' Anything unreadable falls back to the safest choice, the Recycle Bin.
            If Not [Enum].IsDefined(GetType(FolderDeleteMode), FolderDelete) Then
                FolderDelete = StageDefaults.DefaultFolderDeleteMode
            End If

            StageTintColor = ColorText.NormalizeOrDefault(StageTintColor, StageDefaults.DefaultStageTintColor)
            AcrylicTintColor = ColorText.NormalizeOrDefault(AcrylicTintColor, StageDefaults.DefaultAcrylicTintColor)

            StageTintOpacityPercent = StageDefaults.ClampPercent(StageTintOpacityPercent, StageDefaults.MinimumPercent, StageDefaults.MaximumPercent)
            AcrylicTintOpacityPercent = StageDefaults.ClampPercent(AcrylicTintOpacityPercent, StageDefaults.MinimumPercent, StageDefaults.MaximumPercent)
            AcrylicSheenOpacityPercent = StageDefaults.ClampPercent(AcrylicSheenOpacityPercent, StageDefaults.MinimumPercent, StageDefaults.MaximumAcrylicDetailPercent)
            AcrylicGrainOpacityPercent = StageDefaults.ClampPercent(AcrylicGrainOpacityPercent, StageDefaults.MinimumPercent, StageDefaults.MaximumAcrylicDetailPercent)

            If String.IsNullOrWhiteSpace(StageFontFamily) Then
                StageFontFamily = StageDefaults.DefaultStageFontFamily
            End If
            LabelFontSizePoints = StageDefaults.ClampPercent(LabelFontSizePoints, StageDefaults.MinimumLabelFontSizePoints, StageDefaults.MaximumLabelFontSizePoints)
            If TitleFontSizePoints <> StageDefaults.AutomaticTitleFontSize Then
                TitleFontSizePoints = StageDefaults.ClampPercent(TitleFontSizePoints, StageDefaults.MinimumTitleFontSizePoints, StageDefaults.MaximumTitleFontSizePoints)
            End If
            TitleBarColor = ColorText.NormalizeOrDefault(TitleBarColor, StageDefaults.DefaultTitleBarColor)
            TitleTextColor = ColorText.NormalizeOrDefault(TitleTextColor, StageDefaults.DefaultTitleTextColor)
            LabelTextColor = ColorText.NormalizeOrDefault(LabelTextColor, StageDefaults.DefaultLabelTextColor)
            BorderColor = ColorText.NormalizeOrDefault(BorderColor, StageDefaults.DefaultBorderColor)
            TitleBarOpacityPercent = StageDefaults.ClampPercent(TitleBarOpacityPercent, StageDefaults.MinimumPercent, StageDefaults.MaximumPercent)
            BorderOpacityPercent = StageDefaults.ClampPercent(BorderOpacityPercent, StageDefaults.MinimumPercent, StageDefaults.MaximumPercent)
            BorderThickness = StageDefaults.ClampPercent(BorderThickness, 0, StageDefaults.MaximumBorderThickness)
            MinifyAnimationMilliseconds = StageDefaults.ClampPercent(MinifyAnimationMilliseconds,
                                                                     StageDefaults.MinimumMinifyAnimationMilliseconds,
                                                                     StageDefaults.MaximumMinifyAnimationMilliseconds)
            If Not [Enum].IsDefined(GetType(StageOpenDirection), MinifyOpenDirection) Then
                MinifyOpenDirection = StageDefaults.DefaultMinifyOpenDirection
            End If
            If Not [Enum].IsDefined(GetType(StageTitleAlignment), TitleAlignment) Then
                TitleAlignment = StageDefaults.DefaultTitleAlignment
            End If
            If Not [Enum].IsDefined(GetType(StageTabStyle), TabStyle) Then
                TabStyle = StageDefaults.DefaultTabStyle
            End If
            ' Empty = Automatic; so is the old v0.8.5 default (see PreviousDefaultTabAccentColor).
            If String.IsNullOrWhiteSpace(TabAccentColor) OrElse
               String.Equals(TabAccentColor.Trim(), StageDefaults.PreviousDefaultTabAccentColor, StringComparison.OrdinalIgnoreCase) Then
                TabAccentColor = StageDefaults.DefaultTabAccentColor
            Else
                TabAccentColor = ColorText.NormalizeOrDefault(TabAccentColor, StageDefaults.DefaultTabAccentColor)
            End If
            If Not [Enum].IsDefined(GetType(StageCornerStyle), CornerStyle) Then
                CornerStyle = StageDefaults.DefaultCornerStyle
            End If
            StageGapPixels = StageDefaults.ClampPercent(StageGapPixels, StageDefaults.MinimumStageGapPixels, StageDefaults.MaximumStageGapPixels)
            If Not [Enum].IsDefined(GetType(StageSnapMode), StageSnapping) Then StageSnapping = StageDefaults.DefaultStageSnapping
            SnapGuideColor = ColorText.NormalizeOrDefault(SnapGuideColor, StageDefaults.DefaultSnapGuideColor)
        End Sub

        ''' <summary>Puts every Appearance value back to its built-in default (General is left alone).</summary>
        Public Sub RestoreDefaultAppearance()
            StageTintColor = StageDefaults.DefaultStageTintColor
            StageTintOpacityPercent = StageDefaults.DefaultStageTintOpacityPercent
            AcrylicTintColor = StageDefaults.DefaultAcrylicTintColor
            AcrylicTintOpacityPercent = StageDefaults.DefaultAcrylicTintOpacityPercent
            AcrylicSheenOpacityPercent = StageDefaults.DefaultAcrylicSheenOpacityPercent
            AcrylicGrainOpacityPercent = StageDefaults.DefaultAcrylicGrainOpacityPercent
            StageFontFamily = StageDefaults.DefaultStageFontFamily
            LabelFontSizePoints = StageDefaults.DefaultLabelFontSizePoints
            TitleFontSizePoints = StageDefaults.AutomaticTitleFontSize
            TitleBarColor = StageDefaults.DefaultTitleBarColor
            TitleBarOpacityPercent = StageDefaults.DefaultTitleBarOpacityPercent
            TitleTextColor = StageDefaults.DefaultTitleTextColor
            LabelTextColor = StageDefaults.DefaultLabelTextColor
            BorderColor = StageDefaults.DefaultBorderColor
            BorderOpacityPercent = StageDefaults.DefaultBorderOpacityPercent
            BorderThickness = StageDefaults.DefaultBorderThickness
            CornerStyle = StageDefaults.DefaultCornerStyle
            TitleAlignment = StageDefaults.DefaultTitleAlignment
            TabStyle = StageDefaults.DefaultTabStyle
            TabAccentColor = StageDefaults.DefaultTabAccentColor
        End Sub

        ' --- Ready-made brushes for StageWindow -----------------------------------------
        ' "Frozen" brushes can't be changed afterwards, which lets WPF share them between
        ' every Stage cheaply. New ones are made whenever the settings change.

        ''' <summary>The tint drawn over Blur and Translucent Stages.</summary>
        Public Function CreateStageTintBrush() As SolidColorBrush
            Return CreateFrozenBrush(StageTintColor, StageTintOpacityPercent)
        End Function

        ''' <summary>The tint drawn over imitation-acrylic Stages.</summary>
        Public Function CreateAcrylicTintBrush() As SolidColorBrush
            Return CreateFrozenBrush(AcrylicTintColor, AcrylicTintOpacityPercent)
        End Function

        ''' <summary>The faint white sheen under the acrylic tint.</summary>
        Public Function CreateAcrylicSheenBrush() As SolidColorBrush
            Const SheenColor As String = "#FFFFFF"
            Return CreateFrozenBrush(SheenColor, AcrylicSheenOpacityPercent)
        End Function

        ''' <summary>The title bar's extra darkening.</summary>
        Public Function CreateTitleBarBrush() As SolidColorBrush
            Return CreateFrozenBrush(TitleBarColor, TitleBarOpacityPercent)
        End Function

        ''' <summary>The title text colour (always solid).</summary>
        Public Function CreateTitleTextBrush() As SolidColorBrush
            Return CreateFrozenBrush(TitleTextColor, StageDefaults.MaximumPercent)
        End Function

        ''' <summary>True if the tab colour is Automatic (no colour picked).</summary>
        Public Function IsTabAccentAutomatic() As Boolean
            Return String.IsNullOrEmpty(TabAccentColor)
        End Function

        ''' <summary>
        ''' The tab colour to use ("#RRGGBB"): the one picked or, when Automatic, for the Outlined
        ''' look the Stage's own colour a little lighter, and for the other looks a light blue.
        ''' The Stage's colour depends on its background, so the caller says which it has.
        ''' </summary>
        Public Function EffectiveTabAccentColor(stageBackdrop As BackdropStyle) As String
            If Not IsTabAccentAutomatic() Then Return TabAccentColor
            If TabStyle <> StageTabStyle.Outlined Then Return StageDefaults.AutomaticUnderlineColor

            ' Imitation acrylic has its own tint colour; every other background uses the Stage tint.
            Dim stageColor As String = If(stageBackdrop = BackdropStyle.Acrylic, AcrylicTintColor, StageTintColor)
            Return ColorText.Lighten(stageColor, StageDefaults.AutomaticOutlineLightenPercent)
        End Function

        ''' <summary>The open tab's underline or outline (always solid), for a Stage with this background.</summary>
        Public Function CreateTabAccentBrush(stageBackdrop As BackdropStyle) As SolidColorBrush
            Return CreateFrozenBrush(EffectiveTabAccentColor(stageBackdrop), StageDefaults.MaximumPercent)
        End Function

        ''' <summary>The item label colour (always solid).</summary>
        Public Function CreateLabelTextBrush() As SolidColorBrush
            Return CreateFrozenBrush(LabelTextColor, StageDefaults.MaximumPercent)
        End Function

        ''' <summary>The outline round each Stage.</summary>
        Public Function CreateBorderBrush() As SolidColorBrush
            Return CreateFrozenBrush(BorderColor, BorderOpacityPercent)
        End Function

        ''' <summary>The grain layer's Opacity (0.0 to 1.0).</summary>
        Public Function AcrylicGrainOpacity() As Double
            Return ColorText.PercentToFraction(AcrylicGrainOpacityPercent)
        End Function

        ''' <summary>
        ''' Makes an unchangeable brush from "#RRGGBB" text and an opacity percentage.
        ''' (The parameter is called rgbText, not colorText: VB ignores case, so "colorText"
        ''' would hide the ColorText class inside this method.)
        ''' </summary>
        Private Shared Function CreateFrozenBrush(rgbText As String, opacityPercent As Integer) As SolidColorBrush
            Dim tintBrush As New SolidColorBrush(ColorText.ToColor(rgbText, opacityPercent))
            tintBrush.Freeze()
            Return tintBrush
        End Function

    End Class

    ''' <summary>
    ''' Converts between the "#RRGGBB" text used in settings.xml and WPF colours.
    ''' </summary>
    Public NotInheritable Class ColorText

        ''' <summary>"#" plus 6 hex digits.</summary>
        Private Const RgbTextLength As Integer = 7
        Private Const HexPrefix As String = "#"
        Private Const FullAlpha As Integer = 255
        Private Const PercentScale As Double = 100.0
        ''' <summary>Two-digit uppercase hex, e.g. 15 -> "0F".</summary>
        Private Const TwoDigitHexFormat As String = "X2"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Returns the colour as tidy "#RRGGBB" text, or <paramref name="fallbackText"/> if
        ''' <paramref name="candidateText"/> isn't a valid "#RRGGBB" colour.
        ''' </summary>
        Public Shared Function NormalizeOrDefault(candidateText As String, fallbackText As String) As String
            Dim parsedColor As Color
            If TryParseRgb(candidateText, parsedColor) Then
                Return FromColor(parsedColor)
            End If
            Return fallbackText
        End Function

        ''' <summary>Reads "#RRGGBB" text. Returns False (and black) if it isn't in that form.</summary>
        Public Shared Function TryParseRgb(candidateText As String, ByRef parsedColor As Color) As Boolean
            parsedColor = Colors.Black
            If candidateText Is Nothing Then Return False

            Dim trimmedText As String = candidateText.Trim()
            If trimmedText.Length <> RgbTextLength OrElse Not trimmedText.StartsWith(HexPrefix, StringComparison.Ordinal) Then
                Return False
            End If

            Dim rgbValue As Integer
            If Not Integer.TryParse(trimmedText.Substring(HexPrefix.Length), Globalization.NumberStyles.HexNumber,
                                    Globalization.CultureInfo.InvariantCulture, rgbValue) Then
                Return False
            End If

            ' Unpack the three bytes: RRGGBB.
            Const BitsPerByte As Integer = 8
            Const ByteMask As Integer = &HFF
            Dim red As Byte = CByte((rgbValue >> (BitsPerByte * 2)) And ByteMask)
            Dim green As Byte = CByte((rgbValue >> BitsPerByte) And ByteMask)
            Dim blue As Byte = CByte(rgbValue And ByteMask)
            parsedColor = Color.FromRgb(red, green, blue)
            Return True
        End Function

        ''' <summary>
        ''' "#RRGGBB" moved <paramref name="percent"/> percent of the way towards white: each of
        ''' red, green and blue gains that share of what it lacks of 255. 5% turns black
        ''' (#000000) into #0D0D0D, and #202020 into #2B2B2B. Unreadable text counts as black.
        ''' </summary>
        Public Shared Function Lighten(rgbText As String, percent As Integer) As String
            Dim sourceColor As Color
            TryParseRgb(rgbText, sourceColor) ' black if unreadable
            Dim share As Double = Math.Max(0, Math.Min(100, percent)) / PercentScale
            Dim lightenChannel As Func(Of Byte, Byte) =
                Function(channel) CByte(Math.Round(channel + (FullAlpha - channel) * share))
            Return FromColor(Color.FromRgb(lightenChannel(sourceColor.R), lightenChannel(sourceColor.G), lightenChannel(sourceColor.B)))
        End Function

        ''' <summary>Formats a colour as "#RRGGBB" (its opacity is ignored).</summary>
        Public Shared Function FromColor(sourceColor As Color) As String
            Return HexPrefix & sourceColor.R.ToString(TwoDigitHexFormat) &
                   sourceColor.G.ToString(TwoDigitHexFormat) &
                   sourceColor.B.ToString(TwoDigitHexFormat)
        End Function

        ''' <summary>"#RRGGBB" text plus an opacity percentage -> a WPF colour with that alpha.</summary>
        Public Shared Function ToColor(rgbText As String, opacityPercent As Integer) As Color
            Dim baseColor As Color
            TryParseRgb(rgbText, baseColor) ' falls back to black if the text is bad
            Dim alpha As Byte = CByte(Math.Round(PercentToFraction(opacityPercent) * FullAlpha))
            Return Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B)
        End Function

        ''' <summary>0-100 -> 0.0-1.0 (values outside 0-100 are clamped).</summary>
        Public Shared Function PercentToFraction(percent As Integer) As Double
            Dim clampedPercent As Integer = StageDefaults.ClampPercent(percent, StageDefaults.MinimumPercent, StageDefaults.MaximumPercent)
            Return clampedPercent / PercentScale
        End Function

    End Class

End Namespace
