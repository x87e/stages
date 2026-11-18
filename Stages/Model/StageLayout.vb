' ============================================================================
'  StageLayout.vb
'  How the items on a Stage are shown and arranged - the Stage menu's View
'  and Sort by sub-menus. Saved per Stage in StageInfo.
'
'  The model copies the Windows desktop:
'    * View: large / medium / small icons, or Explorer-style List / Details.
'    * Sort by: name, type, date modified, size, or Custom (the order you dragged them into).
'    * Details view columns: which of Name / Type / Date modified / Size show,
'      and in what order (StageInfo.DetailsColumns, v0.8.4).
'    * Auto arrange icons ON  -> icons flow left-to-right in sort order.
'      Auto arrange icons OFF -> each icon stays where you drop it (icon views
'      only), snapped to an invisible grid if "Align icons to grid" is on.
' ============================================================================
Imports System.Xml.Serialization

Namespace Model

    ''' <summary>What the items look like.</summary>
    Public Enum StageViewMode
        ''' <summary>48px icons with the label underneath.</summary>
        LargeIcons
        ''' <summary>32px icons with the label underneath (the original NoFences look). Default.</summary>
        MediumIcons
        ''' <summary>16px icons with the label underneath.</summary>
        SmallIcons
        ''' <summary>One item per row: small icon then name.</summary>
        List
        ''' <summary>One item per row with columns: Name, Type, Date modified and Size, unless changed (StageDetailsColumn).</summary>
        Details
    End Enum

    ''' <summary>The order items are shown in.</summary>
    Public Enum StageSortOrder
        ''' <summary>The order the user dragged them into (or the order they were added). Default.</summary>
        Custom
        Name
        ''' <summary>By file type (e.g. "Microsoft Word Document"), then name.</summary>
        Type
        ''' <summary>By the date the file a shortcut opens was last changed.</summary>
        DateModified
        ''' <summary>By the size of the file (a shortcut: the file it opens). Folders have no size and come first, by name. v0.8.4.</summary>
        Size
    End Enum

    ''' <summary>
    ''' Which way a minified (or rolled-up) Stage opens - Settings > General > Minified Stages
    ''' (StageWindow.OpenDirection.vb). v0.8.9.
    ''' </summary>
    Public Enum StageOpenDirection
        ''' <summary>Below the title bar, as always (the default).</summary>
        Down
        ''' <summary>Above the title bar, which stays where it is.</summary>
        Up
        ''' <summary>Downwards, unless that would run off the screen and there's more room above.</summary>
        Automatic
    End Enum

    ''' <summary>
    ''' One column of the Details view. Which ones show, and in what order, is saved per tab
    ''' (StageInfo.DetailsColumns). Name is always shown: it holds the icon and the label.
    ''' Saved in the XML by name, so new columns can be added at the end safely.
    ''' </summary>
    Public Enum StageDetailsColumn
        ''' <summary>Icon and name. Always shown.</summary>
        Name
        ''' <summary>Windows' name for the kind of file ("Microsoft Word Document").</summary>
        Type
        ''' <summary>When the file (a shortcut: the file it opens) was last changed.</summary>
        DateModified
        ''' <summary>How big the file is (a shortcut: the file it opens). Empty for folders.</summary>
        Size
    End Enum

    ''' <summary>
    ''' Where one item was placed by hand (Auto arrange off). Kept by file name, like
    ''' ItemOrder, in device-independent pixels from the top-left of the item area.
    ''' </summary>
    Public Class ItemPosition

        <XmlAttribute()>
        Public Property FileName As String

        <XmlAttribute()>
        Public Property X As Double

        <XmlAttribute()>
        Public Property Y As Double

        ''' <summary>Parameterless constructor - required by XmlSerializer.</summary>
        Public Sub New()
        End Sub

        Public Sub New(itemFileName As String, left As Double, top As Double)
            FileName = itemFileName
            X = left
            Y = top
        End Sub

    End Class

End Namespace
