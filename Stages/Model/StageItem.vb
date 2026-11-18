' ============================================================================
'  StageItem.vb
'  A snapshot of one entry in a Stage's folder - normally a shortcut (.lnk or
'  .url) that Stages created when something was dropped on the Stage.
'
'  Created by StageFolder.ScanItems; immutable afterwards. Each refresh makes
'  fresh snapshots, so "is the target still there?" stays up to date.
'
'  Since v0.8 an item can also be:
'    * an entry deep inside a Live folder (StageFolder.ScanLiveFolder) - its
'      FileName is then its path relative to the folder ("Sub\Report.docx"),
'      so two files called the same in different sub-folders stay apart;
'    * an open window on a Running tasks Stage (RunningWindows.vb) - it has no
'      file at all: FullPath is a made-up unique name, WindowHandle says which
'      window it is, and its picture comes ready-made (PreloadedIcon) or from
'      the program's file (IconSourcePath).
' ============================================================================
Imports System.Diagnostics
Imports System.IO
Imports Stages.Services

Namespace Model

    Public NotInheritable Class StageItem

        ''' <summary>Full path of the entry inside the Stage folder (e.g. ...\Data\Work\Report.lnk).</summary>
        Public ReadOnly Property FullPath As String

        ''' <summary>File name only (e.g. "Report.lnk") - what ItemOrder stores.</summary>
        Public ReadOnly Property FileName As String

        ''' <summary>
        ''' Label under the icon, without any extension (e.g. "Report"). Whether the extension is
        ''' added is decided when it's displayed - see Services\ItemLabels.vb.
        ''' </summary>
        Public ReadOnly Property LabelWithoutExtension As String

        ''' <summary>
        ''' The extension that may be shown after the label (e.g. ".docx"; for a shortcut, the
        ''' extension of the file it points to). Empty for folders, web links and extension-less files.
        ''' </summary>
        Public ReadOnly Property LabelExtension As String

        ''' <summary>True if the entry is a real sub-folder (only possible if added through Explorer).</summary>
        Public ReadOnly Property IsFolder As Boolean

        ''' <summary>True for .lnk / .url shortcut files.</summary>
        Public ReadOnly Property IsShortcut As Boolean

        ''' <summary>What a .lnk points at. Nothing for non-shortcuts, .url files, or unreadable links.</summary>
        Public ReadOnly Property TargetPath As String

        ''' <summary>True when a shortcut's target no longer exists. Shown dimmed.</summary>
        Public ReadOnly Property IsBroken As Boolean

        ''' <summary>Image file to make a preview thumbnail from, or Nothing for a normal icon.</summary>
        Public ReadOnly Property ThumbnailSourcePath As String

        ''' <summary>
        ''' The custom icon set on this shortcut (Change icon..., or Explorer's Properties), or
        ''' Nothing if it uses its normal icon. As stored in the shortcut, so it may contain
        ''' environment variables such as %SystemRoot%.
        ''' </summary>
        Public ReadOnly Property CustomIconPath As String

        ''' <summary>Which icon inside CustomIconPath (an index, or a negative resource id).</summary>
        Public ReadOnly Property CustomIconIndex As Integer

        ''' <summary>
        ''' When the entry was last changed on disk (UTC ticks). A shortcut whose icon was just
        ''' changed has a new value, which tells the Stage to load its picture again.
        ''' </summary>
        Public ReadOnly Property LastWriteTicks As Long

        ''' <summary>Windows' name for the kind of file ("Microsoft Word Document") - Details view and Sort by > Type.</summary>
        Public ReadOnly Property TypeName As String

        ''' <summary>
        ''' When the file a shortcut opens (or the entry itself) was last changed, UTC ticks -
        ''' Details view and Sort by > Date modified.
        ''' </summary>
        Public ReadOnly Property ModifiedTicks As Long

        ''' <summary>
        ''' How many bytes the file holds (a shortcut: the file it opens) - Details view and
        ''' Sort by > Size. NoSize for folders, web links, windows, broken shortcuts and shortcuts
        ''' to network files (reading those would cost a slow round trip per item).
        ''' </summary>
        Public ReadOnly Property SizeBytes As Long

        ''' <summary>SizeBytes when an item has no size to show.</summary>
        Public Const NoSize As Long = -1

        ''' <summary>True if there is a size to show (SizeBytes isn't NoSize).</summary>
        Public ReadOnly Property HasSize As Boolean
            Get
                Return SizeBytes <> NoSize
            End Get
        End Property

        ''' <summary>
        ''' Running tasks only: the window this item stands for. IntPtr.Zero for every
        ''' item that is a file or folder.
        ''' </summary>
        Public ReadOnly Property WindowHandle As IntPtr

        ''' <summary>
        ''' A picture that is already made (a window's own icon), shown as it is - nothing is
        ''' loaded for it. Nothing for normal items.
        ''' </summary>
        Public ReadOnly Property PreloadedIcon As ImageSource

        ''' <summary>
        ''' The file whose icon to show, when that isn't FullPath (a window shows its program's
        ''' icon). Nothing = use FullPath, as before v0.8.
        ''' </summary>
        Public ReadOnly Property IconSourcePath As String

        ''' <param name="itemOrderKey">
        ''' What ItemOrder and hand-placed positions remember this item by. Nothing = its file
        ''' name (as before v0.8); a Live folder's deeper entries pass their relative path.
        ''' </param>
        ''' <param name="itemWindowHandle">Running tasks only: the window the item stands for.</param>
        ''' <param name="readyMadeIcon">A ready-made picture (frozen), or Nothing.</param>
        ''' <param name="iconFilePath">The file to take the icon from, or Nothing for FullPath.</param>
        ''' <param name="contentSizeBytes">The file's size in bytes, or NoSize (folders, windows...).</param>
        Public Sub New(entryPath As String, labelText As String, labelExtensionText As String,
                       entryIsFolder As Boolean, entryIsShortcut As Boolean,
                       shortcutTarget As String, targetIsMissing As Boolean, thumbnailSource As String,
                       customIconFile As String, customIconNumber As Integer, entryLastWriteTicks As Long,
                       fileTypeName As String, contentModifiedTicks As Long, opensAFolder As Boolean,
                       Optional itemOrderKey As String = Nothing,
                       Optional itemWindowHandle As IntPtr = Nothing,
                       Optional readyMadeIcon As ImageSource = Nothing,
                       Optional iconFilePath As String = Nothing,
                       Optional contentSizeBytes As Long = NoSize)
            FullPath = entryPath
            FileName = If(itemOrderKey, Path.GetFileName(entryPath))
            WindowHandle = itemWindowHandle
            PreloadedIcon = readyMadeIcon
            IconSourcePath = iconFilePath
            LabelWithoutExtension = labelText
            LabelExtension = If(labelExtensionText, String.Empty)
            IsFolder = entryIsFolder
            IsShortcut = entryIsShortcut
            TargetPath = shortcutTarget
            IsBroken = targetIsMissing
            ThumbnailSourcePath = thumbnailSource
            CustomIconPath = customIconFile
            CustomIconIndex = customIconNumber
            LastWriteTicks = entryLastWriteTicks
            TypeName = If(fileTypeName, String.Empty)
            ModifiedTicks = contentModifiedTicks
            SizeBytes = contentSizeBytes
            OpensFolder = opensAFolder
        End Sub

        ''' <summary>True for a folder, or a shortcut to a folder or drive - sorted before files, like Explorer.</summary>
        Public ReadOnly Property OpensFolder As Boolean

        ''' <summary>True if this item is an open window (Running tasks) rather than a file or folder.</summary>
        Public ReadOnly Property IsWindow As Boolean
            Get
                Return WindowHandle <> IntPtr.Zero
            End Get
        End Property

        ''' <summary>True if this entry can be given its own icon (only .lnk and .url shortcuts can).</summary>
        Public ReadOnly Property CanHaveCustomIcon As Boolean
            Get
                Return IsShortcut
            End Get
        End Property

        ''' <summary>True if this shortcut currently has a custom icon (so "Reset icon" makes sense).</summary>
        Public ReadOnly Property HasCustomIcon As Boolean
            Get
                Return Not String.IsNullOrEmpty(CustomIconPath)
            End Get
        End Property

        ''' <summary>
        ''' Opens the item exactly like a double-click in Explorer would (a shortcut opens
        ''' its target). Runs on the shell worker thread so a slow program start can't
        ''' freeze the Stage.
        ''' </summary>
        Public Sub Open()
            ' Copy to locals so the background job doesn't depend on this object.
            Dim entryPath As String = FullPath
            Dim entryIsFolder As Boolean = IsFolder
            Dim entryIsShortcut As Boolean = IsShortcut

            StaWorkerThread.ShellOperations.Post(
                Sub()
                    Try
                        If entryIsFolder Then
                            StageFolder.OpenInExplorer(entryPath)
                            Return
                        End If

                        Dim startInfo As New ProcessStartInfo(entryPath) With {.UseShellExecute = True}
                        ' A shortcut carries its own working folder; for a plain file, use its folder.
                        If Not entryIsShortcut Then
                            startInfo.WorkingDirectory = If(Path.GetDirectoryName(entryPath), String.Empty)
                        End If
                        Process.Start(startInfo)
                    Catch ex As Exception
                        ' e.g. no program associated with the file type, or the user cancelled a UAC prompt.
                        Logger.Warn("Failed to open " & entryPath, ex)
                    End Try
                End Sub)
        End Sub

    End Class

End Namespace
