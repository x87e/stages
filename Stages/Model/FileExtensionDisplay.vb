' ============================================================================
'  FileExtensionDisplay.vb
'  Whether item labels on Stages end in the file's extension (".docx",
'  ".pdf"...). Chosen in Settings > Items; see Services\ItemLabels.vb.
'  For a shortcut, the extension shown is that of the file it points to.
' ============================================================================
Namespace Model

    Public Enum FileExtensionDisplay
        ''' <summary>
        ''' Do what File Explorer does (View > Show > File name extensions). When Windows hides
        ''' extensions, it only hides those of file types it knows (that have a program
        ''' registered), so unknown types still show theirs - Stages copies that. Default.
        ''' </summary>
        SameAsWindows
        ''' <summary>Always show every extension.</summary>
        AlwaysShow
        ''' <summary>Never show extensions, whatever the file type.</summary>
        AlwaysHide
    End Enum

End Namespace
