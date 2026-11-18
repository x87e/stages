' ============================================================================
'  StageKind.vb
'  What a Stage (or one tab of a Stage) shows. Chosen when it is made
'  (Stage menu or tray menu > New Stage, or Tabs > New tab) and saved in its
'  settings file (StageInfo.Kind). It never changes afterwards.
'
'    Standard ........... the shortcuts you dropped on it (the original NoFences idea)
'    LiveFolder ......... the contents of a folder on the PC - your real files - with
'                         optional sub-folders, name filters and an item limit
'    RunningTasks ....... the programs' windows open right now (like the taskbar)
'    ClipboardHistory ... what you copied recently: text, pictures and files
' ============================================================================
Namespace Model

    Public Enum StageKind
        ''' <summary>Shortcuts dropped onto the Stage, kept in its own folder. The default.</summary>
        Standard = 0
        ''' <summary>
        ''' Shows a folder on the PC (StageInfo.SourceFolderPath). Called a "folder Stage" before
        ''' v0.8; those are read as Live folders with the old behaviour (no sub-folders, no filters).
        ''' </summary>
        LiveFolder = 1
        ''' <summary>Shows the windows that are open, updated every few seconds. Nothing is saved for them.</summary>
        RunningTasks = 2
        ''' <summary>
        ''' Keeps what was recently copied: text as .txt files, pictures as .png files and copied
        ''' files as shortcuts, all inside the Stage's own folder, so they survive a restart.
        ''' </summary>
        ClipboardHistory = 3
    End Enum

End Namespace
