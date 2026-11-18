' ============================================================================
'  FolderDeleteMode.vb
'  What "Delete" does on a FOLDER STAGE (a Stage or tab that shows the real
'  contents of a folder). Chosen in Settings > Items; carried out by
'  Native\ShellFileDelete.vb.
'
'  This never applies to ordinary Stages: their items are shortcuts, which
'  "Remove from Stage" always sends to the Recycle Bin.
' ============================================================================
Namespace Model

    Public Enum FolderDeleteMode
        ''' <summary>
        ''' Stages asks first, saying the files will go to the Recycle Bin, then sends them
        ''' there. If Windows can't recycle one (e.g. a network drive, or the Recycle Bin is
        ''' switched off for that drive), Windows itself warns before deleting it for good.
        ''' Default, and recommended.
        ''' </summary>
        RecycleBin
        ''' <summary>
        ''' Exactly what deleting in File Explorer does on this PC: Windows shows its own
        ''' confirmation if it's switched on there, and recycles or deletes according to the
        ''' Recycle Bin's own settings. Stages adds no question of its own.
        ''' </summary>
        SameAsWindows
        ''' <summary>Stages asks first (warning that this can't be undone), then deletes for good.</summary>
        Permanent
    End Enum

End Namespace
