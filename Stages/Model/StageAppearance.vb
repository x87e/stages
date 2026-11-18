' ============================================================================
'  StageAppearance.vb
'  Choices for how Stages look, beyond the background effect (BackdropStyle):
'    * the corner shape of every Stage (Settings > Appearance > Stage frame)
'    * how one Stage's own background picture fills it (Background > Picture...)
'    * the picture settings being edited in the picture dialog
' ============================================================================
Namespace Model

    ''' <summary>The shape of every Stage's corners.</summary>
    Public Enum StageCornerStyle
        ''' <summary>
        ''' Rounded like other Windows 11 windows. Windows 10 can't round a blurred window's
        ''' corners, so there the corners stay square whichever is chosen.
        ''' </summary>
        Rounded
        ''' <summary>Square ("pointed") corners, as in Windows 10 and NoFences.</summary>
        Square
    End Enum

    ''' <summary>
    ''' Where the title sits in every Stage's title bar - and, on a Stage with tabs, where the
    ''' row of tabs sits (Settings > Appearance > Title and tabs). v0.8.5.
    ''' </summary>
    Public Enum StageTitleAlignment
        ''' <summary>In the middle (the default, as before).</summary>
        Center
        Left
        Right
    End Enum

    ''' <summary>
    ''' How a Stage with tabs shows which tab is open (Settings > Appearance > Title and tabs).
    ''' The other tabs are always drawn a little fainter. v0.8.5; each look is a Style in
    ''' StagesStyles.xaml (StageTab...Style).
    ''' </summary>
    Public Enum StageTabStyle
        ''' <summary>A line in the tab colour under the open tab.</summary>
        Underline
        ''' <summary>The open tab is shaded, with rounded top corners, sitting on the bottom edge.</summary>
        Shaded
        ''' <summary>An outline in the tab colour round the open tab's top and sides, like Windows' and Visual Studio's tabs.</summary>
        Outlined
        ''' <summary>No marking: the open tab is just brighter than the others (the default).</summary>
        Clear
    End Enum

    ''' <summary>How a Stage's background picture fills the Stage.</summary>
    Public Enum BackgroundPictureFit
        ''' <summary>Covers the whole Stage, keeping its shape; edges that don't fit are cut off. Default.</summary>
        Fill
        ''' <summary>The whole picture shows, keeping its shape; may leave empty bands at the sides.</summary>
        Fit
        ''' <summary>Stretched to exactly the Stage's shape (may look squashed).</summary>
        Stretch
    End Enum

    ''' <summary>
    ''' One Stage's background picture settings while they are being edited in the picture
    ''' dialog (and previewed on the Stage). Saved into StageInfo only when the user clicks OK.
    ''' </summary>
    Public NotInheritable Class BackgroundPictureChoice

        ''' <summary>Full path of the picture to show: the Stage's own copy, or a newly chosen file. Nothing = no picture.</summary>
        Public Property PictureFilePath As String
        Public Property OpacityPercent As Integer = StageDefaults.DefaultPictureOpacityPercent
        Public Property ShadePercent As Integer = StageDefaults.DefaultPictureShadePercent
        Public Property BlurRadius As Integer = StageDefaults.DefaultPictureBlurRadius
        Public Property Fit As BackgroundPictureFit = StageDefaults.DefaultPictureFit

        ''' <summary>True if a picture is chosen.</summary>
        Public ReadOnly Property HasPicture As Boolean
            Get
                Return Not String.IsNullOrEmpty(PictureFilePath)
            End Get
        End Property

        ''' <summary>A separate copy, so a preview can't be changed by later edits.</summary>
        Public Function Copy() As BackgroundPictureChoice
            Return DirectCast(MemberwiseClone(), BackgroundPictureChoice)
        End Function

        ''' <summary>The settings a Stage has saved now (its picture is a copy inside its folder).</summary>
        Public Shared Function FromStage(stage As StageInfo) As BackgroundPictureChoice
            Dim savedFile As String = Nothing
            If Not String.IsNullOrEmpty(stage.BackgroundPictureFile) AndAlso Not String.IsNullOrEmpty(stage.FolderPath) Then
                savedFile = IO.Path.Combine(stage.FolderPath, stage.BackgroundPictureFile)
            End If
            Return New BackgroundPictureChoice() With {
                .PictureFilePath = savedFile,
                .OpacityPercent = stage.BackgroundPictureOpacityPercent,
                .ShadePercent = stage.BackgroundPictureShadePercent,
                .BlurRadius = stage.BackgroundPictureBlurRadius,
                .Fit = stage.BackgroundPictureFit
            }
        End Function

    End Class

End Namespace
