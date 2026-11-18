' ============================================================================
'  StageFolder.vb
'  Everything to do with a Stage's folder on disk:
'    * turning a Stage title into a safe, unique folder name
'    * listing the items (shortcuts) in a folder, in display order
'    * turning dropped files/folders into shortcuts in the folder
'    * renaming the folder when the Stage is renamed
'    * sending items / whole Stages to the Recycle Bin
'
'  The folder IS the Stage's content: whatever shortcuts are in it are what
'  the Stage shows. Stages only ever creates and removes SHORTCUTS - it never
'  deletes or moves the user's real files.
' ============================================================================
Imports System.Collections.Concurrent
Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports Stages.Model
Imports Stages.Native

Namespace Services

    Public NotInheritable Class StageFolder

#Region "Constants"
        ''' <summary>The hidden settings file inside each Stage folder.</summary>
        Public Const SettingsFileName As String = ".stage-settings.xml"
        ''' <summary>
        ''' Stages' own hidden files all start with this - the settings file with its .tmp/.bak
        ''' companions, and a Stage's background picture - so they are never shown as items.
        ''' </summary>
        Private Const OwnFilePrefix As String = ".stage-"
        ''' <summary>A Stage's background picture is copied in under this name plus its own extension (.jpg, .png...).</summary>
        Public Const BackgroundPictureBaseName As String = ".stage-background"

        ''' <summary>Extension of the shortcuts Stages creates.</summary>
        Private Const ShellShortcutExtension As String = ".lnk"
        ''' <summary>File types that are already shortcuts - these are copied in as-is, not wrapped in another shortcut.</summary>
        Private Shared ReadOnly ShortcutFileExtensions As String() = {".lnk", ".url"}

        ''' <summary>Used when a title is empty or made entirely of illegal characters.</summary>
        Private Const FallbackName As String = "Stage"
        ''' <summary>Illegal file-name characters in a title are replaced with this.</summary>
        Private Const IllegalCharacterReplacement As Char = "_"c
        ''' <summary>Appended to names Windows reserves for devices (CON, NUL...) to make them legal.</summary>
        Private Const ReservedNameSuffix As String = "_"
        ''' <summary>Keeps folder/shortcut names comfortably inside Windows path limits.</summary>
        Private Const MaximumNameLength As Integer = 80
        ''' <summary>Duplicate names become "Name (2)", "Name (3)"... like Explorer.</summary>
        Private Const DuplicateNameFormat As String = "{0} ({1})"
        Private Const FirstDuplicateNumber As Integer = 2
        ''' <summary>Temporary suffix used for case-only folder renames ("work" to "Work").</summary>
        Private Const CaseRenameSuffix As String = " (renaming)"
        ''' <summary>Label prefix for a shortcut to a whole drive, e.g. "Drive D".</summary>
        Friend Const DriveLabelPrefix As String = "Drive "

        ''' <summary>Names Windows won't allow for files or folders, with or without an extension.</summary>
        Private Shared ReadOnly ReservedDeviceNames As String() = {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"}

        ''' <summary>Characters trimmed from the end of names (Windows silently drops trailing dots/spaces).</summary>
        Private Shared ReadOnly TrailingCharactersToTrim As Char() = {"."c, " "c}
        Private Shared ReadOnly PathSeparators As Char() = {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar}
#End Region

        ''' <summary>"shortcut path|last-write-ticks" -> its target and icon, so links aren't re-read on every refresh.</summary>
        Private Shared ReadOnly ShortcutDetailsCache As New ConcurrentDictionary(Of String, ShortcutDetails)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>
        ''' When the shortcut cache holds more than this many entries it's emptied and refilled
        ''' as Stages rescan. Entries for edited or deleted shortcuts would otherwise pile up for
        ''' as long as Stages runs (weeks). 5,000 small entries is well under a megabyte.
        ''' </summary>
        Private Const MaximumCachedShortcuts As Integer = 5000

        ' --- Network targets ---------------------------------------------------------------
        ' Checking whether a shortcut's target exists is instant on a local disk, but for a
        ' network path whose server is switched off Windows can take 20-30 seconds to give
        ' up, per path. So network answers are remembered for a minute, and a server that was
        ' slow to say "not there" is treated as unreachable for a minute (its other paths are
        ' not checked at all in that time).

        ''' <summary>How long an answer about a network path is reused.</summary>
        Private Shared ReadOnly NetworkAnswerLifetime As TimeSpan = TimeSpan.FromMinutes(1)
        ''' <summary>A "not there" that took longer than this means the server itself isn't answering.</summary>
        Private Const SlowNetworkCheckMilliseconds As Long = 2000
        ''' <summary>Network path -> (exists, is a folder, when checked).</summary>
        Private Shared ReadOnly NetworkAnswers As New ConcurrentDictionary(Of String, NetworkAnswer)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>Root ("\\server\share\" or "Z:\") -> when it was found not answering.</summary>
        Private Shared ReadOnly UnreachableNetworkRoots As New ConcurrentDictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>Drive letter root -> is it a network drive (mapped drives don't change type).</summary>
        Private Shared ReadOnly NetworkDriveRoots As New ConcurrentDictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        Private Const UncPathPrefix As String = "\\"
        ''' <summary>Network answers kept at most (emptied and refilled when exceeded).</summary>
        Private Const MaximumNetworkAnswers As Integer = 5000

        ''' <summary>One remembered answer about a network path.</summary>
        Private NotInheritable Class NetworkAnswer
            Public Property Exists As Boolean
            Public Property IsFolder As Boolean
            Public Property CheckedAtUtc As DateTime
        End Class

        Private Sub New()
        End Sub

#Region "Names"

        ''' <summary>True for Stages' own hidden files (settings with its .tmp and .bak, background picture).</summary>
        Public Shared Function IsStageOwnFile(fileName As String) As Boolean
            Return fileName IsNot Nothing AndAlso fileName.StartsWith(OwnFilePrefix, StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' True if a folder is a TAB of a Stage: a sub-folder of a Stage folder with its own
        ''' settings file. Tabs are shown in the title bar, never as items.
        ''' </summary>
        Public Shared Function IsTabFolder(folderPath As String) As Boolean
            Try
                Return File.Exists(Path.Combine(folderPath, SettingsFileName))
            Catch ex As Exception
                Return False
            End Try
        End Function

        ''' <summary>True for .lnk and .url files.</summary>
        Public Shared Function IsShortcutFile(itemPath As String) As Boolean
            Return ShortcutFileExtensions.Contains(Path.GetExtension(itemPath), StringComparer.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Turns any text into a legal Windows file/folder name:
        ''' illegal characters become "_", trailing dots/spaces go, overly long names are
        ''' shortened, reserved device names (CON, NUL, COM1...) get a "_" added, and an
        ''' empty result becomes "Stage". The Stage's displayed title is never changed.
        ''' </summary>
        Public Shared Function SanitizeName(requestedName As String) As String
            Dim illegalCharacters As Char() = Path.GetInvalidFileNameChars()
            Dim cleaned As New StringBuilder()
            For Each character As Char In If(requestedName, String.Empty)
                cleaned.Append(If(illegalCharacters.Contains(character), IllegalCharacterReplacement, character))
            Next

            Dim result As String = cleaned.ToString().Trim().TrimEnd(TrailingCharactersToTrim)
            If result.Length > MaximumNameLength Then
                result = result.Substring(0, MaximumNameLength).TrimEnd(TrailingCharactersToTrim)
            End If
            If result.Length = 0 Then
                result = FallbackName
            End If

            ' "CON" and "CON.txt" are both reserved - check the part before the first dot.
            Dim nameBeforeFirstDot As String = result.Split("."c)(0).Trim()
            If ReservedDeviceNames.Contains(nameBeforeFirstDot, StringComparer.OrdinalIgnoreCase) Then
                result &= ReservedNameSuffix
            End If
            Return result
        End Function

        ''' <summary>
        ''' Returns folder\baseName+extension, or folder\baseName (2)+extension, (3)... -
        ''' the first one that doesn't exist yet.
        ''' </summary>
        Public Shared Function MakeUniquePath(folder As String, baseName As String, extension As String) As String
            Dim candidate As String = Path.Combine(folder, baseName & extension)
            Dim duplicateNumber As Integer = FirstDuplicateNumber
            While File.Exists(candidate) OrElse Directory.Exists(candidate)
                candidate = Path.Combine(folder, String.Format(DuplicateNameFormat, baseName, duplicateNumber) & extension)
                duplicateNumber += 1
            End While
            Return candidate
        End Function

        ''' <summary>
        ''' True if a folder name is what Stages would have made from this title - either
        ''' the cleaned-up title itself or a numbered duplicate of it ("Work (2)").
        ''' Case-sensitive, so a case-only rename in Explorer still counts as a change.
        ''' </summary>
        Public Shared Function FolderNameMatchesTitle(folderName As String, stageTitle As String) As Boolean
            Dim expectedName As String = SanitizeName(stageTitle)
            If String.Equals(folderName, expectedName, StringComparison.Ordinal) Then
                Return True
            End If
            ' Matches e.g. "Work (2)", "Work (17)"
            Dim numberedDuplicatePattern As String = "^" & Regex.Escape(expectedName) & " \(\d+\)$"
            Return Regex.IsMatch(folderName, numberedDuplicatePattern)
        End Function

        ''' <summary>True if two paths refer to the same folder (ignores case and trailing slashes). Never throws.</summary>
        Public Shared Function IsSamePath(firstPath As String, secondPath As String) As Boolean
            If String.IsNullOrEmpty(firstPath) OrElse String.IsNullOrEmpty(secondPath) Then
                Return False
            End If
            Try
                Dim firstFull As String = Path.GetFullPath(firstPath).TrimEnd(PathSeparators)
                Dim secondFull As String = Path.GetFullPath(secondPath).TrimEnd(PathSeparators)
                Return String.Equals(firstFull, secondFull, StringComparison.OrdinalIgnoreCase)
            Catch ex As Exception
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Renames a shortcut (.lnk / .url) in a Stage folder - right-click > Rename... on a Stage.
        ''' The new name is cleaned up like a Stage title (illegal characters become "_"), keeps
        ''' the shortcut's own extension, and becomes "Name (2)" if taken. The shortcut keeps its
        ''' place in the Stage's ItemOrder. The file it points to is never touched.
        ''' Throws IOException / UnauthorizedAccessException if Windows refuses.
        ''' </summary>
        ''' <returns>The shortcut's new full path (the same path if nothing changed).</returns>
        Public Shared Function RenameShortcut(stage As StageInfo, shortcutPath As String, newLabel As String) As String
            Dim shortcutExtension As String = Path.GetExtension(shortcutPath)
            Dim folder As String = Path.GetDirectoryName(shortcutPath)
            Dim currentName As String = Path.GetFileNameWithoutExtension(shortcutPath)
            Dim cleanName As String = SanitizeName(newLabel)

            If String.Equals(cleanName, currentName, StringComparison.Ordinal) Then
                Return shortcutPath ' nothing to do
            End If

            Dim newPath As String
            If String.Equals(cleanName, currentName, StringComparison.OrdinalIgnoreCase) Then
                ' Only the capitals change: it's the same file, so no "(2)" - Windows renames in place.
                newPath = Path.Combine(folder, cleanName & shortcutExtension)
            Else
                newPath = MakeUniquePath(folder, cleanName, shortcutExtension)
            End If
            File.Move(shortcutPath, newPath)

            ' Same place in the display order (and the same hand-placed spot), under the new name.
            Dim oldFileName As String = Path.GetFileName(shortcutPath)
            Dim newFileName As String = Path.GetFileName(newPath)
            Dim orderPosition As Integer = stage.ItemOrder.FindIndex(
                Function(listedName) String.Equals(listedName, oldFileName, StringComparison.OrdinalIgnoreCase))
            If orderPosition >= 0 Then
                stage.ItemOrder(orderPosition) = newFileName
            End If
            For Each placed As ItemPosition In stage.ItemPositions
                If String.Equals(placed.FileName, oldFileName, StringComparison.OrdinalIgnoreCase) Then
                    placed.FileName = newFileName
                End If
            Next

            Logger.Info("Renamed shortcut '" & oldFileName & "' to '" & Path.GetFileName(newPath) & "'")
            Return newPath
        End Function

        ''' <summary>A new, unused folder path in Data for a Stage with this title.</summary>
        Public Shared Function MakeNewStageFolderPath(stageTitle As String) As String
            Return MakeUniquePath(AppPaths.DataFolder, SanitizeName(stageTitle), String.Empty)
        End Function

        ''' <summary>A new, unused folder path inside a Stage's folder, for a new tab with this title.</summary>
        Public Shared Function MakeNewTabFolderPath(hostStageFolder As String, tabTitle As String) As String
            Return MakeUniquePath(hostStageFolder, SanitizeName(tabTitle), String.Empty)
        End Function

#End Region

#Region "Listing items"

        ''' <summary>
        ''' Lists the visible items in a Stage folder, in display order. Skips Stages' own
        ''' settings files and anything hidden or system (e.g. desktop.ini).
        ''' Reads shortcut files through COM, so call this on the STA shell worker thread.
        ''' </summary>
        Public Shared Function ScanItems(stageFolderPath As String, savedOrder As List(Of String)) As List(Of StageItem)
            Dim foundItems As New List(Of StageItem)()
            If String.IsNullOrEmpty(stageFolderPath) OrElse Not Directory.Exists(stageFolderPath) Then
                Return foundItems
            End If

            For Each entryPath As String In Directory.EnumerateFileSystemEntries(stageFolderPath)
                If IsStageOwnFile(Path.GetFileName(entryPath)) Then
                    Continue For
                End If

                Dim attributes As FileAttributes
                Try
                    attributes = File.GetAttributes(entryPath)
                Catch ex As Exception
                    Continue For ' vanished or inaccessible since the listing was taken
                End Try

                If (attributes And (FileAttributes.Hidden Or FileAttributes.System)) <> 0 Then
                    Continue For
                End If

                Dim entryIsFolder As Boolean = (attributes And FileAttributes.Directory) = FileAttributes.Directory
                ' A Stage's tabs are sub-folders of it; they appear in the title bar instead.
                If entryIsFolder AndAlso IsTabFolder(entryPath) Then
                    Continue For
                End If
                foundItems.Add(InspectEntry(entryPath, entryIsFolder))
            Next

            Return SortIntoDisplayOrder(foundItems, savedOrder)
        End Function

        ''' <summary>
        ''' What a Live folder scan should include - a snapshot of the tab's options, taken on
        ''' the UI thread and handed to the scan thread (so the user changing the options
        ''' mid-scan can't change them under it). Immutable.
        ''' </summary>
        Public NotInheritable Class LiveFolderOptions
            ''' <summary>Levels of sub-folders to look inside (0 = only the folder itself).</summary>
            Public ReadOnly Property Depth As Integer
            ''' <summary>Files must match one of these (empty = every file).</summary>
            Public ReadOnly Property IncludePatterns As NamePatterns
            ''' <summary>Files and folders matching one of these are left out (folders aren't looked inside either).</summary>
            Public ReadOnly Property ExcludePatterns As NamePatterns
            Public ReadOnly Property ShowFolders As Boolean
            Public ReadOnly Property MaximumItems As Integer

            ''' <summary>Copies the Live folder options of <paramref name="tab"/>.</summary>
            Public Sub New(tab As StageInfo)
                Depth = tab.LiveFolderDepth
                IncludePatterns = New NamePatterns(tab.LiveFolderIncludePatterns)
                ExcludePatterns = New NamePatterns(tab.LiveFolderExcludePatterns)
                ShowFolders = tab.LiveFolderShowFolders
                MaximumItems = tab.LiveFolderMaximumItems
            End Sub
        End Class

        ''' <summary>
        ''' Lists a Live folder: the folder's entries, and (up to options.Depth levels down) its
        ''' sub-folders' entries too, filtered by name, at most options.MaximumItems of them.
        ''' Shallower entries are listed first, so the limit cuts the deepest ones. Hidden and
        ''' system entries are skipped as in ScanItems, and so are folder links (junctions and
        ''' symbolic links), which could otherwise lead round in a circle forever.
        ''' Call on the scan thread, like ScanItems.
        ''' </summary>
        Public Shared Function ScanLiveFolder(rootFolderPath As String, options As LiveFolderOptions,
                                              savedOrder As List(Of String)) As List(Of StageItem)
            Dim foundItems As New List(Of StageItem)()
            If String.IsNullOrEmpty(rootFolderPath) OrElse Not Directory.Exists(rootFolderPath) Then
                Return foundItems
            End If

            ' Deeper entries are remembered by their path below the root ("Sub\File.txt").
            Dim rootWithSeparator As String = Path.GetFullPath(rootFolderPath).TrimEnd(PathSeparators) & Path.DirectorySeparatorChar
            ' Folders still to list, each with how deep it is (the root is level 0).
            Dim foldersToList As New Queue(Of KeyValuePair(Of String, Integer))()
            foldersToList.Enqueue(New KeyValuePair(Of String, Integer)(rootFolderPath, 0))
            Dim reachedLimit As Boolean = False

            While foldersToList.Count > 0 AndAlso Not reachedLimit
                Dim current As KeyValuePair(Of String, Integer) = foldersToList.Dequeue()
                Dim currentFolder As String = current.Key
                Dim currentDepth As Integer = current.Value
                Dim isRootLevel As Boolean = (currentDepth = 0)

                Dim entries As IEnumerable(Of String)
                Try
                    ' ToList: reading the listing now means an unreadable folder fails here, not mid-loop.
                    entries = Directory.EnumerateFileSystemEntries(currentFolder).ToList()
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                    If isRootLevel Then Throw ' the folder itself is unreadable: let the Stage report it
                    Continue While ' one locked sub-folder shouldn't hide everything else
                End Try

                For Each entryPath As String In entries
                    Dim entryName As String = Path.GetFileName(entryPath)
                    If IsStageOwnFile(entryName) Then Continue For

                    Dim attributes As FileAttributes
                    Try
                        attributes = File.GetAttributes(entryPath)
                    Catch ex As Exception
                        Continue For ' vanished or inaccessible since the listing was taken
                    End Try
                    If (attributes And (FileAttributes.Hidden Or FileAttributes.System)) <> 0 Then Continue For
                    If options.ExcludePatterns.Matches(entryName) Then Continue For

                    Dim entryIsFolder As Boolean = (attributes And FileAttributes.Directory) = FileAttributes.Directory
                    If entryIsFolder Then
                        If IsTabFolder(entryPath) Then Continue For ' only possible if the Live folder is inside Data
                        Dim isFolderLink As Boolean = (attributes And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint
                        If currentDepth < options.Depth AndAlso Not isFolderLink Then
                            foldersToList.Enqueue(New KeyValuePair(Of String, Integer)(entryPath, currentDepth + 1))
                        End If
                        If Not options.ShowFolders Then Continue For
                    ElseIf Not options.IncludePatterns.IsEmpty AndAlso Not options.IncludePatterns.Matches(entryName) Then
                        Continue For
                    End If

                    ' Entries directly in the folder keep their plain file name as their key,
                    ' exactly as folder Stages before v0.8 did (so saved orders still apply).
                    Dim orderKey As String = Nothing
                    If Not isRootLevel Then
                        orderKey = Path.GetFullPath(entryPath).Substring(rootWithSeparator.Length)
                    End If
                    foundItems.Add(InspectEntry(entryPath, entryIsFolder, orderKey))

                    If foundItems.Count >= options.MaximumItems Then
                        reachedLimit = True
                        Exit For
                    End If
                Next
            End While

            If reachedLimit Then
                Logger.Info("Live folder " & rootFolderPath & " has more than " & options.MaximumItems.ToString() &
                            " items; only the first " & options.MaximumItems.ToString() & " are shown")
            End If
            Return SortIntoDisplayOrder(foundItems, savedOrder)
        End Function

        ''' <summary>
        ''' Builds the snapshot for one folder entry: label (in two parts - see ItemLabels),
        ''' shortcut target, broken?, custom icon, thumbnail source.
        ''' <paramref name="itemOrderKey"/>: see StageItem (Nothing = the file name).
        ''' </summary>
        Private Shared Function InspectEntry(entryPath As String, entryIsFolder As Boolean,
                                             Optional itemOrderKey As String = Nothing) As StageItem
            Dim fileName As String = Path.GetFileName(entryPath)
            Dim entryIsShortcut As Boolean = Not entryIsFolder AndAlso IsShortcutFile(entryPath)
            Dim isShellShortcut As Boolean = entryIsShortcut AndAlso
                String.Equals(Path.GetExtension(entryPath), ShellShortcutExtension, StringComparison.OrdinalIgnoreCase)

            ' --- Label: "name" + an extension that may or may not be shown ---------------
            Dim labelText As String
            Dim labelExtension As String = String.Empty
            If entryIsFolder Then
                labelText = fileName
            Else
                labelText = Path.GetFileNameWithoutExtension(entryPath)
                If Not entryIsShortcut Then
                    labelExtension = Path.GetExtension(entryPath) ' a real file put in the folder through Explorer
                End If
            End If
            If String.IsNullOrEmpty(labelText) Then
                labelText = fileName ' e.g. a file called ".gitignore"
                labelExtension = String.Empty
            End If

            ' --- Shortcut details --------------------------------------------------------
            Dim shortcutTarget As String = Nothing
            Dim targetIsMissing As Boolean = False
            Dim shortcutTargetIsFolder As Boolean = False
            Dim thumbnailSource As String = Nothing
            Dim customIconFile As String = Nothing
            Dim customIconNumber As Integer = 0

            If isShellShortcut Then
                Dim details As ShortcutDetails = GetCachedShortcutDetails(entryPath)
                If details IsNot Nothing Then
                    shortcutTarget = details.TargetPath
                    customIconFile = details.IconLocationAsStored
                    customIconNumber = details.IconIndex
                End If

                If shortcutTarget IsNot Nothing Then
                    ' One existence check (remembered for network paths) answers both questions.
                    targetIsMissing = Not TargetExists(shortcutTarget, shortcutTargetIsFolder)
                    labelExtension = TargetFileExtension(shortcutTarget)
                    labelText = RemoveTrailingExtension(labelText, labelExtension)
                    ' Shortcut to a picture: preview the picture itself - unless the user gave
                    ' the shortcut its own icon, which then wins.
                    If customIconFile Is Nothing AndAlso Not targetIsMissing AndAlso Not shortcutTargetIsFolder AndAlso
                       IconProvider.SupportsThumbnail(shortcutTarget) Then
                        thumbnailSource = shortcutTarget
                    End If
                End If
            ElseIf entryIsShortcut Then
                ' A .url web link: no file extension to show; it may carry its own icon.
                Dim storedIconPath As String = Nothing
                If ShellLinks.TryReadInternetShortcutIconSetting(entryPath, storedIconPath, customIconNumber) Then
                    customIconFile = storedIconPath
                End If
            ElseIf Not entryIsFolder AndAlso IconProvider.SupportsThumbnail(entryPath) Then
                ' A real picture placed in the folder through Explorer.
                thumbnailSource = entryPath
            End If

            ' --- Details view / sorting: the kind of thing, when it last changed, its size ----
            Dim entryLastWriteTicks As Long = ReadLastWriteTicks(entryPath, entryIsFolder)
            Dim itemTypeName As String
            Dim contentModifiedTicks As Long = entryLastWriteTicks
            Dim contentSizeBytes As Long = StageItem.NoSize ' folders and web links have no size
            Dim opensAFolder As Boolean = entryIsFolder
            If isShellShortcut AndAlso shortcutTarget IsNot Nothing Then
                opensAFolder = shortcutTargetIsFolder
                itemTypeName = ShellFileTypes.GetTypeName(TargetFileExtension(shortcutTarget), shortcutTargetIsFolder)
                ' Not for network targets: one more slow round trip per item, only for a date.
                ' Not for network targets: one more slow round trip per item, only for a date
                ' and a size.
                If Not targetIsMissing AndAlso Not IsNetworkPath(shortcutTarget) Then
                    contentModifiedTicks = ReadLastWriteTicks(shortcutTarget, shortcutTargetIsFolder)
                    If Not shortcutTargetIsFolder Then
                        contentSizeBytes = ReadFileSizeBytes(shortcutTarget)
                    End If
                End If
            Else
                itemTypeName = ShellFileTypes.GetTypeName(Path.GetExtension(entryPath), entryIsFolder)
                ' A real file (a Live folder's, a clipboard entry's text or picture): its own size.
                ' A .url web link's size would only be the size of the link file itself.
                If Not entryIsFolder AndAlso Not entryIsShortcut Then
                    contentSizeBytes = ReadFileSizeBytes(entryPath)
                End If
            End If

            Return New StageItem(entryPath, labelText, labelExtension, entryIsFolder, entryIsShortcut,
                                 shortcutTarget, targetIsMissing, thumbnailSource,
                                 customIconFile, customIconNumber, entryLastWriteTicks,
                                 itemTypeName, contentModifiedTicks, opensAFolder,
                                 itemOrderKey:=itemOrderKey,
                                 contentSizeBytes:=contentSizeBytes)
        End Function

        ''' <summary>
        ''' The extension to show for a shortcut: its target's (e.g. ".docx"), or empty for a
        ''' folder, a drive or a target with no extension.
        ''' </summary>
        Private Shared Function TargetFileExtension(targetPath As String) As String
            If Directory.Exists(targetPath) Then Return String.Empty
            Try
                Return Path.GetExtension(targetPath)
            Catch ex As ArgumentException
                Return String.Empty ' characters a path can't contain
            End Try
        End Function

        ''' <summary>
        ''' "Report.docx" + ".docx" -> "Report". Shortcuts made by Explorer (rather than by
        ''' Stages) are often named with the extension, which would otherwise show twice.
        ''' </summary>
        Private Shared Function RemoveTrailingExtension(labelText As String, extension As String) As String
            Dim hasRoomForName As Boolean = labelText.Length > extension.Length
            If extension.Length > 0 AndAlso hasRoomForName AndAlso labelText.EndsWith(extension, StringComparison.OrdinalIgnoreCase) Then
                Return labelText.Substring(0, labelText.Length - extension.Length)
            End If
            Return labelText
        End Function

        ''' <summary>When the entry was last written (UTC ticks), or 0 if that can't be read.</summary>
        Private Shared Function ReadLastWriteTicks(entryPath As String, entryIsFolder As Boolean) As Long
            Try
                If entryIsFolder Then
                    Return Directory.GetLastWriteTimeUtc(entryPath).Ticks
                End If
                Return File.GetLastWriteTimeUtc(entryPath).Ticks
            Catch ex As Exception
                Return 0L
            End Try
        End Function

        ''' <summary>A file's size in bytes, or StageItem.NoSize if it can't be read (gone, no access...).</summary>
        Private Shared Function ReadFileSizeBytes(filePath As String) As Long
            Try
                Return New FileInfo(filePath).Length
            Catch ex As Exception
                Return StageItem.NoSize
            End Try
        End Function

        ''' <summary>Reads a .lnk's target and icon, re-reading only if the .lnk file has changed.</summary>
        Private Shared Function GetCachedShortcutDetails(shortcutPath As String) As ShortcutDetails
            Dim cacheKey As String
            Try
                cacheKey = shortcutPath & "|" & File.GetLastWriteTimeUtc(shortcutPath).Ticks.ToString()
            Catch ex As Exception
                Return Nothing
            End Try

            Dim cachedDetails As ShortcutDetails = Nothing
            If ShortcutDetailsCache.TryGetValue(cacheKey, cachedDetails) Then
                Return cachedDetails
            End If

            Dim details As ShortcutDetails = ShellLinks.TryReadShortcut(shortcutPath)
            If ShortcutDetailsCache.Count >= MaximumCachedShortcuts Then
                ShortcutDetailsCache.Clear()
            End If
            ShortcutDetailsCache(cacheKey) = details
            Return details
        End Function

        ''' <summary>
        ''' True if a shortcut's target exists; <paramref name="isFolder"/> says whether it's a
        ''' folder. Local paths are checked every time (instant). Network paths are answered
        ''' from memory for a minute, and not checked at all while their server isn't answering.
        ''' Never throws.
        ''' </summary>
        Private Shared Function TargetExists(targetPath As String, ByRef isFolder As Boolean) As Boolean
            isFolder = False
            Try
                If Not IsNetworkPath(targetPath) Then
                    isFolder = Directory.Exists(targetPath)
                    Return isFolder OrElse File.Exists(targetPath)
                End If

                Dim nowUtc As DateTime = DateTime.UtcNow
                Dim networkRoot As String = Path.GetPathRoot(targetPath)
                Dim foundUnreachableAt As DateTime
                If UnreachableNetworkRoots.TryGetValue(networkRoot, foundUnreachableAt) AndAlso
                   nowUtc - foundUnreachableAt < NetworkAnswerLifetime Then
                    Return False ' the server wasn't answering a moment ago: don't wait on it again
                End If

                Dim remembered As NetworkAnswer = Nothing
                If NetworkAnswers.TryGetValue(targetPath, remembered) AndAlso nowUtc - remembered.CheckedAtUtc < NetworkAnswerLifetime Then
                    isFolder = remembered.IsFolder
                    Return remembered.Exists
                End If

                Dim checkTimer As Stopwatch = Stopwatch.StartNew()
                isFolder = Directory.Exists(targetPath)
                Dim exists As Boolean = isFolder OrElse File.Exists(targetPath)
                checkTimer.Stop()

                If Not exists AndAlso checkTimer.ElapsedMilliseconds >= SlowNetworkCheckMilliseconds Then
                    UnreachableNetworkRoots(networkRoot) = nowUtc
                    Logger.Info("Network location " & networkRoot & " isn't answering; not checking it again for a minute")
                End If
                If NetworkAnswers.Count >= MaximumNetworkAnswers Then
                    NetworkAnswers.Clear()
                End If
                NetworkAnswers(targetPath) = New NetworkAnswer() With {.Exists = exists, .IsFolder = isFolder, .CheckedAtUtc = nowUtc}
                Return exists
            Catch ex As Exception
                Return False
            End Try
        End Function

        ''' <summary>True for "\\server\share\..." paths and paths on mapped network drives. Never throws.</summary>
        Private Shared Function IsNetworkPath(itemPath As String) As Boolean
            Try
                If String.IsNullOrEmpty(itemPath) Then Return False
                If itemPath.StartsWith(UncPathPrefix, StringComparison.Ordinal) Then Return True

                Dim driveRoot As String = Path.GetPathRoot(itemPath)
                If String.IsNullOrEmpty(driveRoot) Then Return False
                Dim isNetworkDrive As Boolean
                If Not NetworkDriveRoots.TryGetValue(driveRoot, isNetworkDrive) Then
                    ' DriveType is answered locally (from the drive mapping), even when the server is off.
                    isNetworkDrive = (New DriveInfo(driveRoot).DriveType = DriveType.Network)
                    NetworkDriveRoots(driveRoot) = isNetworkDrive
                End If
                Return isNetworkDrive
            Catch ex As Exception
                Return False
            End Try
        End Function

        ''' <summary>Items listed in savedOrder come first in that order; the rest follow alphabetically.</summary>
        Private Shared Function SortIntoDisplayOrder(items As List(Of StageItem), savedOrder As List(Of String)) As List(Of StageItem)
            Dim savedPositions As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            For position As Integer = 0 To savedOrder.Count - 1
                If Not savedPositions.ContainsKey(savedOrder(position)) Then
                    savedPositions.Add(savedOrder(position), position)
                End If
            Next

            Dim bySavedPosition As IOrderedEnumerable(Of StageItem) =
                items.OrderBy(Function(item) GetSavedPosition(savedPositions, item.FileName))
            Dim thenAlphabetically As IOrderedEnumerable(Of StageItem) =
                bySavedPosition.ThenBy(Function(item) item.FileName, StringComparer.OrdinalIgnoreCase)
            Return thenAlphabetically.ToList()
        End Function

        Private Shared Function GetSavedPosition(savedPositions As Dictionary(Of String, Integer), fileName As String) As Integer
            Dim position As Integer
            If savedPositions.TryGetValue(fileName, position) Then
                Return position
            End If
            Return Integer.MaxValue ' not in the saved order: goes after everything that is
        End Function

#End Region

#Region "Adding dropped items"

        ''' <summary>
        ''' Adds dropped files/folders to a Stage:
        '''   * an existing shortcut (.lnk/.url) is copied in as-is (no shortcut-to-a-shortcut);
        '''     if it came from another Stage, it is moved instead,
        '''   * anything else gets a new shortcut pointing at it.
        ''' The originals are never touched. Each new name is appended to the Stage's ItemOrder.
        ''' Uses COM (shortcut creation), so call on the UI thread.
        ''' </summary>
        ''' <returns>How many items were added.</returns>
        Public Shared Function AddDroppedItems(stage As StageInfo, droppedPaths As IEnumerable(Of String)) As Integer
            Dim addedCount As Integer = 0
            If Not Directory.Exists(stage.FolderPath) Then
                ' Never re-create a missing Stage folder (it may be mid-rename in Explorer).
                Logger.Warn("Drop ignored: Stage folder not found: " & stage.FolderPath)
                Return addedCount
            End If

            For Each droppedPath As String In droppedPaths
                Try
                    If Not PathExists(droppedPath) Then
                        Continue For
                    End If
                    If IsDirectlyInsideFolder(droppedPath, stage.FolderPath) Then
                        Continue For ' already in this Stage
                    End If

                    Dim newEntryPath As String
                    If File.Exists(droppedPath) AndAlso IsShortcutFile(droppedPath) Then
                        Dim shortcutName As String = SanitizeName(Path.GetFileNameWithoutExtension(droppedPath))
                        newEntryPath = MakeUniquePath(stage.FolderPath, shortcutName, Path.GetExtension(droppedPath))
                        If IsInsideDataFolder(droppedPath) Then
                            File.Move(droppedPath, newEntryPath) ' moving between Stages
                        Else
                            File.Copy(droppedPath, newEntryPath)
                        End If
                    Else
                        Dim shortcutName As String = SanitizeName(BuildShortcutName(droppedPath))
                        newEntryPath = MakeUniquePath(stage.FolderPath, shortcutName, ShellShortcutExtension)
                        ShellLinks.CreateShortcut(newEntryPath, droppedPath)
                    End If

                    stage.ItemOrder.Add(Path.GetFileName(newEntryPath))
                    addedCount += 1
                Catch ex As Exception
                    Logger.Error("Could not add " & droppedPath & " to Stage '" & stage.Name & "'", ex)
                End Try
            Next

            Return addedCount
        End Function

        ''' <summary>
        ''' Puts a shortcut to <paramref name="targetPath"/> in <paramref name="folderPath"/> (used by
        ''' Clipboard history for copied files). An existing shortcut file is COPIED in as it is -
        ''' never moved, unlike a drop, because a copy on the clipboard must leave the original
        ''' alone. Returns the new entry's full path. Uses COM, so call on the UI thread.
        ''' </summary>
        Public Shared Function CreateShortcutIn(folderPath As String, targetPath As String) As String
            Dim newEntryPath As String
            If File.Exists(targetPath) AndAlso IsShortcutFile(targetPath) Then
                Dim shortcutName As String = SanitizeName(Path.GetFileNameWithoutExtension(targetPath))
                newEntryPath = MakeUniquePath(folderPath, shortcutName, Path.GetExtension(targetPath))
                File.Copy(targetPath, newEntryPath)
            Else
                Dim shortcutName As String = SanitizeName(BuildShortcutName(targetPath))
                newEntryPath = MakeUniquePath(folderPath, shortcutName, ShellShortcutExtension)
                ShellLinks.CreateShortcut(newEntryPath, targetPath)
            End If
            Return newEntryPath
        End Function

        ''' <summary>
        ''' Name for a new shortcut: the file name without extension (so it matches the
        ''' desktop label), the folder name, or "Drive C" for a drive root.
        ''' </summary>
        Private Shared Function BuildShortcutName(targetPath As String) As String
            Dim trimmedPath As String = targetPath.TrimEnd(PathSeparators)

            If Directory.Exists(targetPath) Then
                Dim folderName As String = Path.GetFileName(trimmedPath)
                If String.IsNullOrEmpty(folderName) Then
                    ' "C:" (after trimming the backslash) - a whole drive.
                    Return DriveLabelPrefix & trimmedPath.TrimEnd(":"c)
                End If
                Return folderName
            End If

            Dim fileLabel As String = Path.GetFileNameWithoutExtension(trimmedPath)
            If String.IsNullOrEmpty(fileLabel) Then
                fileLabel = Path.GetFileName(trimmedPath)
            End If
            Return fileLabel
        End Function

        ''' <summary>True if itemPath sits directly in folderPath (not in a sub-folder of it).</summary>
        Private Shared Function IsDirectlyInsideFolder(itemPath As String, folderPath As String) As Boolean
            Dim parentFolder As String = Path.GetDirectoryName(Path.GetFullPath(itemPath))
            If parentFolder Is Nothing Then
                Return False ' a drive root has no parent
            End If
            Return String.Equals(parentFolder.TrimEnd(PathSeparators), Path.GetFullPath(folderPath).TrimEnd(PathSeparators),
                                 StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>True if the path is anywhere inside the Stages Data folder.</summary>
        Private Shared Function IsInsideDataFolder(itemPath As String) As Boolean
            Dim dataFolderWithSeparator As String = Path.GetFullPath(AppPaths.DataFolder).TrimEnd(PathSeparators) & Path.DirectorySeparatorChar
            Return Path.GetFullPath(itemPath).StartsWith(dataFolderWithSeparator, StringComparison.OrdinalIgnoreCase)
        End Function

#End Region

#Region "Renaming, removing, opening"

        ''' <summary>
        ''' Renames a Stage's folder to match a new title, updating stage.FolderPath.
        ''' Fails (returns False, folder unchanged) if something has the folder open.
        ''' </summary>
        Public Shared Function TryRenameFolder(stage As StageInfo, newTitle As String) As Boolean
            Dim currentFolder As String = stage.FolderPath
            Dim parentFolder As String = Path.GetDirectoryName(currentFolder)
            Dim currentName As String = Path.GetFileName(currentFolder)
            Dim desiredName As String = SanitizeName(newTitle)

            If String.Equals(currentName, desiredName, StringComparison.Ordinal) Then
                Return True ' already right
            End If

            Try
                If String.Equals(currentName, desiredName, StringComparison.OrdinalIgnoreCase) Then
                    ' Case-only change: Windows sees "work" and "Work" as the same name,
                    ' so rename via a temporary name.
                    Dim temporaryFolder As String = MakeUniquePath(parentFolder, desiredName & CaseRenameSuffix, String.Empty)
                    Directory.Move(currentFolder, temporaryFolder)
                    stage.FolderPath = temporaryFolder ' if the next step fails, still point at the real folder
                    Dim finalFolder As String = Path.Combine(parentFolder, desiredName)
                    Directory.Move(temporaryFolder, finalFolder)
                    stage.FolderPath = finalFolder
                Else
                    Dim finalFolder As String = MakeUniquePath(parentFolder, desiredName, String.Empty)
                    Directory.Move(currentFolder, finalFolder)
                    stage.FolderPath = finalFolder
                End If
                Return True
            Catch ex As Exception
                Logger.Warn("Could not rename Stage folder " & currentFolder & " for new title '" & newTitle & "'", ex)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Lists folder entries that are NOT shortcuts (real files or folders someone put
        ''' there through Explorer), including any inside the Stage's tabs. A Stage
        ''' containing any of these is never deleted.
        ''' </summary>
        Public Shared Function FindNonShortcutEntries(stageFolderPath As String) As List(Of String)
            Dim realEntries As New List(Of String)()
            If Not Directory.Exists(stageFolderPath) Then
                Return realEntries
            End If
            ' A Clipboard history folder's text and picture files are Stages' own copies, made
            ' when something was copied - not the user's files - so they don't count.
            Dim isClipboardFolder As Boolean = ClipboardHistoryStore.IsClipboardFolder(stageFolderPath)

            For Each entryPath As String In Directory.EnumerateFileSystemEntries(stageFolderPath)
                If IsStageOwnFile(Path.GetFileName(entryPath)) Then
                    Continue For
                End If
                If isClipboardFolder AndAlso ClipboardHistoryStore.IsCapturedEntryFile(entryPath) Then
                    Continue For
                End If

                Dim isFolder As Boolean = Directory.Exists(entryPath)
                ' A tab is part of the Stage: what matters is whether IT holds real files.
                If isFolder AndAlso IsTabFolder(entryPath) Then
                    realEntries.AddRange(FindNonShortcutEntries(entryPath))
                    Continue For
                End If
                If isFolder OrElse Not IsShortcutFile(entryPath) Then
                    ' Hidden system files Windows itself creates (desktop.ini) don't count.
                    Dim attributes As FileAttributes
                    Try
                        attributes = File.GetAttributes(entryPath)
                    Catch ex As Exception When TypeOf ex Is FileNotFoundException OrElse TypeOf ex Is DirectoryNotFoundException
                        Continue For ' gone between listing and checking: nothing to protect
                    Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                        ' Can't tell what it is: count it as a real file, so the Stage isn't deleted.
                        realEntries.Add(entryPath)
                        Continue For
                    End Try
                    Dim isWindowsSystemFile As Boolean = (attributes And FileAttributes.System) = FileAttributes.System AndAlso
                                                         (attributes And FileAttributes.Hidden) = FileAttributes.Hidden
                    If Not isWindowsSystemFile Then
                        realEntries.Add(entryPath)
                    End If
                End If
            Next
            Return realEntries
        End Function

        ''' <summary>
        ''' Moves a file or a whole folder to the Recycle Bin (so it can be restored).
        ''' Throws OperationCanceledException if the user cancels an error dialog, or an
        ''' IOException-type error on failure.
        ''' </summary>
        Public Shared Sub SendToRecycleBin(itemPath As String)
            If Directory.Exists(itemPath) Then
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    itemPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin)
            ElseIf File.Exists(itemPath) Then
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    itemPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin)
            End If
        End Sub

        ''' <summary>Opens a folder in a new Explorer window (on the shell worker thread).</summary>
        Public Shared Sub OpenInExplorer(folderPath As String)
            Const ExplorerProgram As String = "explorer.exe"
            Dim quotedFolder As String = ControlChars.Quote & folderPath & ControlChars.Quote

            StaWorkerThread.ShellOperations.Post(
                Sub()
                    Try
                        Process.Start(New ProcessStartInfo(ExplorerProgram, quotedFolder) With {.UseShellExecute = True})
                    Catch ex As Exception
                        Logger.Warn("Could not open folder " & folderPath, ex)
                    End Try
                End Sub)
        End Sub

        ''' <summary>True if a file or folder exists at the path. Never throws.</summary>
        Public Shared Function PathExists(itemPath As String) As Boolean
            Try
                Return File.Exists(itemPath) OrElse Directory.Exists(itemPath)
            Catch ex As Exception
                Return False
            End Try
        End Function

#End Region

    End Class

End Namespace
