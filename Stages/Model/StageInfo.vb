' ============================================================================
'  StageInfo.vb
'  Everything saved about one Stage. Written as XML to the hidden
'  ".stage-settings.xml" file inside the Stage's own folder (see StageManager).
'
'  The Stage's ITEMS are not stored here: they are simply the shortcut files in
'  the Stage folder. Only their display order is remembered (ItemOrder).
'
'  Positions/sizes are WPF "device-independent pixels" (1/96 inch); on a 100%
'  scaled screen that's identical to real pixels.
'
'  The same class also describes each TAB of a Stage (a sub-folder with its own
'  settings file): a tab uses the item and layout properties; the window ones
'  (position, size, lock, minify, background) are read from the first tab only.
'
'  Backdrop and TitleHeight are OVERRIDES: empty (Nothing) means "use the
'  app-wide default from AppSettings". Settings files from v0.2 always stored
'  a value, so those Stages keep their own choice until reset (Settings >
'  General > "Use on every Stage", or Background > Default in the Stage menu).
' ============================================================================
Imports System.Xml.Serialization

Namespace Model

    <XmlRoot("Stage")>
    Public Class StageInfo

        ''' <summary>In-memory identity used while the app runs (folder names can change on rename).</summary>
        Public Property Id As Guid

        ''' <summary>Title shown on the desktop, exactly as typed. The folder name is a cleaned-up copy of it.</summary>
        Public Property Name As String = StageDefaults.NewStageName

        ''' <summary>Left edge on the desktop.</summary>
        Public Property PosX As Integer = StageDefaults.NewStageLeft

        ''' <summary>Top edge on the desktop.</summary>
        Public Property PosY As Integer = StageDefaults.NewStageTop

        Public Property Width As Integer = StageDefaults.NewStageWidth

        ''' <summary>Height when expanded (a minified Stage shrinks to just its title bar).</summary>
        Public Property Height As Integer = StageDefaults.NewStageHeight

        ''' <summary>Locked Stages can't be moved, resized, or have items dropped on them.</summary>
        Public Property Locked As Boolean

        ''' <summary>"Minify": collapse to the title bar when the mouse isn't over the Stage.</summary>
        Public Property CanMinify As Boolean

        ''' <summary>
        ''' What a single click on this Stage's title bar does: True = rolls it up (and down
        ''' again), False = nothing, Nothing = follow Settings > General > Title bar
        ''' (StageWindow.TitleClick.vb).
        ''' </summary>
        Public Property TitleClickRollsUp As Boolean?

        ''' <summary>
        ''' True while the Stage is rolled up by a title-bar click: only its title bar shows,
        ''' and it stays that way (hovering doesn't open it) until clicked again. Remembered
        ''' across restarts.
        ''' </summary>
        Public Property IsRolledUp As Boolean

        ''' <summary>
        ''' True while the Stage is open upwards (its title bar at the bottom - see
        ''' StageWindow.OpenDirection.vb), so a Stage that stays open opens the same way after a
        ''' restart. Its PosY is then where the title bar sits, not the window's top.
        ''' </summary>
        Public Property IsOpenedUpward As Boolean

        ''' <summary>
        ''' This Stage's own title bar height, or Nothing to follow the app-wide default
        ''' (Settings > General). "Integer?" means "an Integer that may be empty".
        ''' In the XML an empty value is written as &lt;TitleHeight xsi:nil="true" /&gt;.
        ''' </summary>
        Public Property TitleHeight As Integer?

        ''' <summary>
        ''' This Stage's own background (blur, acrylic or translucent), or Nothing to follow
        ''' the app-wide default (Settings > General).
        ''' </summary>
        Public Property Backdrop As BackdropStyle?

        ''' <summary>
        ''' File names (not full paths) of the items in their Custom order. Items not listed
        ''' here (e.g. added through Explorer) appear after these, alphabetically.
        ''' </summary>
        Public Property ItemOrder As List(Of String) = New List(Of String)()

        ' --- Layout (Stage menu > View / Sort by) - see StageLayout.vb ----------------------

        ''' <summary>Large / medium / small icons, list or details.</summary>
        Public Property ViewMode As StageViewMode = StageViewMode.MediumIcons

        ''' <summary>Custom (dragged) order, or by name / type / date modified.</summary>
        Public Property SortBy As StageSortOrder = StageSortOrder.Custom

        ''' <summary>Reverse the sort (Z-A, newest first...). Ignored for Custom.</summary>
        Public Property SortDescending As Boolean

        ''' <summary>On: icons flow in sort order. Off: icons stay where they're dropped (icon views).</summary>
        Public Property AutoArrange As Boolean = True

        ''' <summary>With Auto arrange off, snap dropped icons to an invisible grid.</summary>
        Public Property AlignToGrid As Boolean = True

        ''' <summary>Where icons were dropped by hand (used only while Auto arrange is off).</summary>
        Public Property ItemPositions As List(Of ItemPosition) = New List(Of ItemPosition)()

        ''' <summary>
        ''' The Details view's columns, in the order shown (right-click the headings to show or
        ''' hide one, drag a heading to move it). Nothing = the default set
        ''' (StageDefaults.DefaultDetailsColumns) - always read it through EffectiveDetailsColumns.
        ''' Deliberately starts as Nothing rather than a filled list: when loading, XmlSerializer
        ''' ADDS the saved entries to a list that already exists, which would double them up.
        ''' </summary>
        Public Property DetailsColumns As List(Of StageDetailsColumn)

        ''' <summary>
        ''' The Details columns to show, in order: this tab's own choice, or the defaults. A
        ''' function (not a property) so XmlSerializer doesn't save it.
        ''' </summary>
        Public Function EffectiveDetailsColumns() As List(Of StageDetailsColumn)
            If DetailsColumns Is Nothing OrElse DetailsColumns.Count = 0 Then
                Return New List(Of StageDetailsColumn)(StageDefaults.DefaultDetailsColumns)
            End If
            Return New List(Of StageDetailsColumn)(DetailsColumns)
        End Function

        ' --- Folder view (Stage menu > New folder Stage... / Tabs > New folder tab...) ---------

        ''' <summary>
        ''' Empty for a normal Stage (it shows the shortcuts in its own folder). For a FOLDER
        ''' VIEW, the full path of any folder on the PC: the Stage then shows that folder's
        ''' contents - the user's real files - so Stages never removes, renames or re-icons
        ''' them, and nothing can be dropped onto it.
        ''' </summary>
        Public Property SourceFolderPath As String

        ' --- Kind (see StageKind.vb) ------------------------------------------------------

        ''' <summary>
        ''' What this Stage or tab shows. Settings files from before v0.8 have no Kind and load as
        ''' Standard; Sanitize then turns an old folder view into a Live folder.
        ''' </summary>
        Public Property Kind As StageKind = StageKind.Standard

        ' Live folder options (used only when Kind = LiveFolder)
        ''' <summary>How many levels of sub-folders to look inside (0 = just the folder itself).</summary>
        Public Property LiveFolderDepth As Integer = StageDefaults.DefaultLiveFolderDepth
        ''' <summary>
        ''' Only names matching one of these show, e.g. "*.pdf; *.docx". Separated by ; or ,.
        ''' A word without * or ? matches any name containing it. Empty = everything.
        ''' Applies to files; folders are filtered by the exclude list only.
        ''' </summary>
        Public Property LiveFolderIncludePatterns As String = String.Empty
        ''' <summary>Names matching one of these are left out (files and folders), e.g. "*.tmp; ~$*".</summary>
        Public Property LiveFolderExcludePatterns As String = String.Empty
        ''' <summary>Whether folders themselves appear as items (their contents still show when looking inside them).</summary>
        Public Property LiveFolderShowFolders As Boolean = True
        ''' <summary>At most this many items are shown.</summary>
        Public Property LiveFolderMaximumItems As Integer = StageDefaults.DefaultLiveFolderMaximumItems

        ' Running tasks options (used only when Kind = RunningTasks)
        ''' <summary>Only windows whose title or program name matches one of these show (same rules as above). Empty = all.</summary>
        Public Property RunningTasksIncludePatterns As String = String.Empty
        ''' <summary>Windows whose title or program name matches one of these are left out.</summary>
        Public Property RunningTasksExcludePatterns As String = String.Empty
        ''' <summary>Whether minimised windows are listed too.</summary>
        Public Property RunningTasksShowMinimised As Boolean = True
        ''' <summary>Seconds between updates of the list.</summary>
        Public Property RunningTasksIntervalSeconds As Integer = StageDefaults.DefaultRunningTasksIntervalSeconds

        ' Clipboard history options (used only when Kind = ClipboardHistory)
        Public Property ClipboardCaptureText As Boolean = True
        ''' <summary>Pictures are saved as .png files in the Stage folder ("can capture images when enabled").</summary>
        Public Property ClipboardCaptureImages As Boolean = True
        ''' <summary>Copied files and folders are kept as shortcuts to them (never copies of the files).</summary>
        Public Property ClipboardCaptureFiles As Boolean = True
        Public Property ClipboardMaximumEntries As Integer = StageDefaults.DefaultClipboardMaximumEntries
        Public Property ClipboardMaximumMegabytes As Integer = StageDefaults.DefaultClipboardMaximumMegabytes
        ''' <summary>True while capturing is paused (Stage menu > Pause clipboard history).</summary>
        Public Property ClipboardPaused As Boolean

        ''' <summary>True for a Running tasks Stage or tab: its items are windows, not files.</summary>
        <XmlIgnore()>
        Public ReadOnly Property IsRunningTasks As Boolean
            Get
                Return Kind = StageKind.RunningTasks
            End Get
        End Property

        ''' <summary>True for a Clipboard history Stage or tab.</summary>
        <XmlIgnore()>
        Public ReadOnly Property IsClipboardHistory As Boolean
            Get
                Return Kind = StageKind.ClipboardHistory
            End Get
        End Property

        ''' <summary>Only a Standard Stage takes items dropped on it; the other kinds fill themselves.</summary>
        <XmlIgnore()>
        Public ReadOnly Property AcceptsDroppedItems As Boolean
            Get
                Return Kind = StageKind.Standard
            End Get
        End Property

        ''' <summary>
        ''' Sets the layout a new Stage of this kind starts with: clipboard entries and windows
        ''' read better in a list with details (newest / front-most first).
        ''' </summary>
        Public Sub ApplyKindDefaults()
            Select Case Kind
                Case StageKind.RunningTasks
                    ViewMode = StageViewMode.List
                    SortBy = StageSortOrder.Custom ' like the taskbar: each window keeps its place, new ones join at the end
                Case StageKind.ClipboardHistory
                    ViewMode = StageViewMode.Details
                    SortBy = StageSortOrder.Custom ' newest first (each new entry is put at the front)
            End Select
        End Sub

        ' --- Tabs (only used on a Stage's first tab, which holds the Stage itself) -----------
        ' A tab is a sub-folder of the Stage's folder with its own settings file; it has its
        ' own items and layout. The window's position, size, lock, minify, background and
        ' picture always come from the first tab.

        ''' <summary>Folder names of the other tabs, in the order they're shown after the first.</summary>
        Public Property TabOrder As List(Of String) = New List(Of String)()

        ''' <summary>Folder name of the tab that was open last (empty = the first tab).</summary>
        Public Property OpenTabFolderName As String

        ' --- Background picture (Stage menu > Background > Picture...) ------------------------

        ''' <summary>
        ''' File name of the Stage's own background picture: a copy kept, hidden, in the Stage's
        ''' folder (e.g. ".stage-background.jpg"), so it travels with the Stage. Empty = none.
        ''' </summary>
        Public Property BackgroundPictureFile As String
        ''' <summary>How strongly the picture shows, 0-100%.</summary>
        Public Property BackgroundPictureOpacityPercent As Integer = StageDefaults.DefaultPictureOpacityPercent
        ''' <summary>Darkening over the picture, 0-100%.</summary>
        Public Property BackgroundPictureShadePercent As Integer = StageDefaults.DefaultPictureShadePercent
        ''' <summary>Blur strength in pixels, 0 to StageDefaults.MaximumPictureBlurRadius.</summary>
        Public Property BackgroundPictureBlurRadius As Integer = StageDefaults.DefaultPictureBlurRadius
        ''' <summary>Fill (cropped), Fit (whole picture) or Stretch.</summary>
        Public Property BackgroundPictureFit As BackgroundPictureFit = StageDefaults.DefaultPictureFit

        ''' <summary>Full path of this Stage's folder. Not saved - it's wherever the settings file was found.</summary>
        <XmlIgnore()>
        Public Property FolderPath As String

        ''' <summary>True if this Stage (or tab) shows another folder's contents (see SourceFolderPath).</summary>
        <XmlIgnore()>
        Public ReadOnly Property IsFolderView As Boolean
            Get
                Return Not String.IsNullOrWhiteSpace(SourceFolderPath)
            End Get
        End Property

        ''' <summary>The folder whose contents are shown: SourceFolderPath for a folder view, otherwise FolderPath.</summary>
        <XmlIgnore()>
        Public ReadOnly Property ContentFolderPath As String
            Get
                Return If(IsFolderView, SourceFolderPath, FolderPath)
            End Get
        End Property

        ''' <summary>Puts any out-of-range picture setting back inside its limits (after loading).</summary>
        Public Sub Sanitize()
            BackgroundPictureOpacityPercent = StageDefaults.ClampPercent(BackgroundPictureOpacityPercent, StageDefaults.MinimumPercent, StageDefaults.MaximumPercent)
            BackgroundPictureShadePercent = StageDefaults.ClampPercent(BackgroundPictureShadePercent, StageDefaults.MinimumPercent, StageDefaults.MaximumPercent)
            BackgroundPictureBlurRadius = StageDefaults.ClampPercent(BackgroundPictureBlurRadius, 0, StageDefaults.MaximumPictureBlurRadius)
            If Not [Enum].IsDefined(GetType(BackgroundPictureFit), BackgroundPictureFit) Then
                BackgroundPictureFit = StageDefaults.DefaultPictureFit
            End If
            If ItemOrder Is Nothing Then ItemOrder = New List(Of String)()
            If ItemPositions Is Nothing Then ItemPositions = New List(Of ItemPosition)()
            If TabOrder Is Nothing Then TabOrder = New List(Of String)()
            SanitizeDetailsColumns()

            ' Kind: a folder view from before v0.8 becomes a Live folder; a Live folder that lost
            ' its folder path can only be a Standard Stage.
            If Not [Enum].IsDefined(GetType(StageKind), Kind) Then Kind = StageKind.Standard
            If IsFolderView AndAlso Kind = StageKind.Standard Then Kind = StageKind.LiveFolder
            If Kind = StageKind.LiveFolder AndAlso Not IsFolderView Then Kind = StageKind.Standard

            LiveFolderDepth = StageDefaults.ClampPercent(LiveFolderDepth, 0, StageDefaults.MaximumLiveFolderDepth)
            LiveFolderMaximumItems = StageDefaults.ClampPercent(LiveFolderMaximumItems,
                                                                StageDefaults.MinimumLiveFolderMaximumItems,
                                                                StageDefaults.MaximumLiveFolderMaximumItems)
            RunningTasksIntervalSeconds = StageDefaults.ClampPercent(RunningTasksIntervalSeconds,
                                                                     StageDefaults.MinimumRunningTasksIntervalSeconds,
                                                                     StageDefaults.MaximumRunningTasksIntervalSeconds)
            ClipboardMaximumEntries = StageDefaults.ClampPercent(ClipboardMaximumEntries,
                                                                 StageDefaults.MinimumClipboardMaximumEntries,
                                                                 StageDefaults.MaximumClipboardMaximumEntries)
            ClipboardMaximumMegabytes = StageDefaults.ClampPercent(ClipboardMaximumMegabytes,
                                                                   StageDefaults.MinimumClipboardMaximumMegabytes,
                                                                   StageDefaults.MaximumClipboardMaximumMegabytes)
            If LiveFolderIncludePatterns Is Nothing Then LiveFolderIncludePatterns = String.Empty
            If LiveFolderExcludePatterns Is Nothing Then LiveFolderExcludePatterns = String.Empty
            If RunningTasksIncludePatterns Is Nothing Then RunningTasksIncludePatterns = String.Empty
            If RunningTasksExcludePatterns Is Nothing Then RunningTasksExcludePatterns = String.Empty
        End Sub

        ''' <summary>
        ''' Details columns from a hand-edited or newer settings file: drops unknown and repeated
        ''' columns, and puts Name back at the front if it went missing (it can't be hidden).
        ''' </summary>
        Private Sub SanitizeDetailsColumns()
            If DetailsColumns Is Nothing Then Return
            Dim cleanColumns As New List(Of StageDetailsColumn)()
            For Each column As StageDetailsColumn In DetailsColumns
                If [Enum].IsDefined(GetType(StageDetailsColumn), column) AndAlso Not cleanColumns.Contains(column) Then
                    cleanColumns.Add(column)
                End If
            Next
            If Not cleanColumns.Contains(StageDetailsColumn.Name) Then
                cleanColumns.Insert(0, StageDetailsColumn.Name)
            End If
            DetailsColumns = cleanColumns
        End Sub

        ''' <summary>Parameterless constructor - required by XmlSerializer.</summary>
        Public Sub New()
        End Sub

        ''' <summary>Creates Stage settings with a specific id.</summary>
        Public Sub New(stageId As Guid)
            Id = stageId
        End Sub

    End Class

End Namespace
