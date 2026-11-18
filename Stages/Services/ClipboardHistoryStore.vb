' ============================================================================
'  ClipboardHistoryStore.vb
'  Keeps a Clipboard history Stage's (or tab's) entries in its own folder, so
'  they survive a restart and are ordinary files you can open:
'
'    text copied ......... "First words of the text.txt"   (UTF-8)
'    picture copied ...... "Picture 2026-11-14 20.15.03.png" (only if pictures are on)
'    files copied ........ a SHORTCUT to each one (never a copy of the file -
'                          Dan's choice), at most MaximumFilesPerCopy per copy
'
'  The newest entry goes first (StageInfo.ItemOrder). Copying something that
'  is already kept moves that entry back to the front instead of adding it
'  twice. Then the limits are applied: the newest ClipboardMaximumEntries
'  entries, together at most ClipboardMaximumMegabytes, are kept; older ones
'  are deleted for good (they are Stages' own copies, not the user's files).
'
'  A hidden marker file (".stage-clipboard-history") says "this folder is a
'  Clipboard history", so removing the Stage or tab isn't refused because its
'  .txt and .png entries look like the user's own files (StageFolder.
'  FindNonShortcutEntries).
'
'  Only files this store recognises as entries (.txt, .png, .lnk, .url) are
'  ever counted or deleted; anything else put in the folder is left alone.
'  UI thread only (the clipboard and shortcut creation need it).
' ============================================================================
Imports System.Collections.Concurrent
Imports System.Collections.Specialized
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Windows.Media.Imaging
Imports Stages.Model
Imports Stages.Native

Namespace Services

    Public NotInheritable Class ClipboardHistoryStore

        ''' <summary>Marks a folder as a Clipboard history (starts with ".stage-", so it is never shown as an item).</summary>
        Public Const MarkerFileName As String = ".stage-clipboard-history"
        Private Const TextEntryExtension As String = ".txt"
        Private Const PictureEntryExtension As String = ".png"
        ''' <summary>Shortcut types that copied files are kept as.</summary>
        Private Shared ReadOnly ShortcutEntryExtensions As String() = {".lnk", ".url"}

        ''' <summary>A text entry is named after its first words, up to this many characters.</summary>
        Private Const MaximumTextNameLength As Integer = 40
        ''' <summary>Name for a text entry with nothing usable in its first line.</summary>
        Private Const FallbackTextEntryName As String = "Text"
        ''' <summary>Picture entries are named after when they were copied (no ":" - it isn't allowed in names).</summary>
        Private Const PictureEntryNameFormat As String = "Picture {0:yyyy-MM-dd HH.mm.ss}"
        ''' <summary>Copying more files than this at once keeps only the first ones (a whole folder's worth would flush the history).</summary>
        Public Const MaximumFilesPerCopy As Integer = 10
        Private Const BytesPerMegabyte As Long = 1024L * 1024L
        ''' <summary>Setting the clipboard is retried this many times (another program may have it open)...</summary>
        Private Const ClipboardWriteAttempts As Integer = 5
        ''' <summary>...this many milliseconds apart.</summary>
        Private Const ClipboardWriteRetryMilliseconds As Integer = 50
        ''' <summary>The content-fingerprint cache is emptied when it grows beyond this.</summary>
        Private Const MaximumCachedFingerprints As Integer = 2000

        ''' <summary>Text entries are written with a byte-order mark, which helps old editors see UTF-8.</summary>
        Private Shared ReadOnly TextEntryEncoding As New UTF8Encoding(encoderShouldEmitUTF8Identifier:=True)
        ''' <summary>"path|size|last-write" -> fingerprint of the entry's content, so files aren't re-read on every copy.</summary>
        Private Shared ReadOnly FingerprintCache As New ConcurrentDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>Whitespace runs (line breaks, tabs) become one space in an entry's name.</summary>
        Private Shared ReadOnly WhitespaceRun As New RegularExpressions.Regex("\s+")

        Private Sub New()
        End Sub

#Region "Recognising entries"

        ''' <summary>True if the folder belongs to a Clipboard history Stage or tab.</summary>
        Public Shared Function IsClipboardFolder(folderPath As String) As Boolean
            Return Not String.IsNullOrEmpty(folderPath) AndAlso File.Exists(Path.Combine(folderPath, MarkerFileName))
        End Function

        ''' <summary>True for the text and picture files this store writes (shortcuts are shortcuts anyway).</summary>
        Public Shared Function IsCapturedEntryFile(entryPath As String) As Boolean
            Dim extension As String = Path.GetExtension(entryPath)
            Return String.Equals(extension, TextEntryExtension, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(extension, PictureEntryExtension, StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>True for any file this store counts as an entry (and may delete when over the limits).</summary>
        Private Shared Function IsEntryFile(entryPath As String) As Boolean
            If IsCapturedEntryFile(entryPath) Then Return True
            Dim extension As String = Path.GetExtension(entryPath)
            Return ShortcutEntryExtensions.Any(Function(shortcutExtension) String.Equals(extension, shortcutExtension, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>Creates the hidden marker file if it isn't there yet.</summary>
        Public Shared Sub EnsureMarker(folderPath As String)
            Dim markerPath As String = Path.Combine(folderPath, MarkerFileName)
            If File.Exists(markerPath) Then Return
            Try
                File.WriteAllText(markerPath, String.Empty)
                File.SetAttributes(markerPath, FileAttributes.Hidden)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Logger.Warn("Could not mark " & folderPath & " as a Clipboard history folder", ex)
            End Try
        End Sub

#End Region

#Region "Capturing a copy"

        ''' <summary>
        ''' Saves what was copied into <paramref name="tab"/>'s folder, following the tab's options
        ''' (paused? text / pictures / files on?), then applies the limits. The caller saves the
        ''' tab's settings and refreshes the Stage. Returns True if anything changed.
        ''' </summary>
        Public Shared Function Capture(tab As StageInfo, snapshot As ClipboardSnapshot) As Boolean
            If tab.ClipboardPaused OrElse Not Directory.Exists(tab.FolderPath) Then Return False
            EnsureMarker(tab.FolderPath)

            Dim changed As Boolean
            Select Case snapshot.ContentKind
                Case ClipboardContentKind.Text
                    changed = tab.ClipboardCaptureText AndAlso CaptureText(tab, snapshot.Text)
                Case ClipboardContentKind.Picture
                    changed = tab.ClipboardCaptureImages AndAlso CapturePicture(tab, snapshot.PicturePngBytes)
                Case ClipboardContentKind.Files
                    changed = tab.ClipboardCaptureFiles AndAlso CaptureFiles(tab, snapshot.FilePaths)
            End Select

            If changed Then ApplyLimits(tab)
            Return changed
        End Function

        Private Shared Function CaptureText(tab As StageInfo, copiedText As String) As Boolean
            Dim fingerprint As String = FingerprintOf(Encoding.UTF8.GetBytes(copiedText))
            If BringExistingEntryToFront(tab, TextEntryExtension, fingerprint) Then Return True

            Dim entryPath As String = StageFolder.MakeUniquePath(tab.FolderPath, NameForText(copiedText), TextEntryExtension)
            File.WriteAllText(entryPath, copiedText, TextEntryEncoding)
            PutAtFront(tab, Path.GetFileName(entryPath))
            Return True
        End Function

        Private Shared Function CapturePicture(tab As StageInfo, pngBytes As Byte()) As Boolean
            If pngBytes Is Nothing OrElse pngBytes.Length = 0 Then Return False
            ' A single picture bigger than the whole allowance could never be kept.
            If pngBytes.LongLength > tab.ClipboardMaximumMegabytes * BytesPerMegabyte Then
                Logger.Info("Copied picture is larger than Clipboard history's size limit; not saved")
                Return False
            End If

            Dim fingerprint As String = FingerprintOf(pngBytes)
            If BringExistingEntryToFront(tab, PictureEntryExtension, fingerprint) Then Return True

            Dim entryName As String = String.Format(PictureEntryNameFormat, DateTime.Now)
            Dim entryPath As String = StageFolder.MakeUniquePath(tab.FolderPath, entryName, PictureEntryExtension)
            File.WriteAllBytes(entryPath, pngBytes)
            PutAtFront(tab, Path.GetFileName(entryPath))
            Return True
        End Function

        ''' <summary>
        ''' One shortcut per copied file or folder (the first MaximumFilesPerCopy). A file that
        ''' already has a shortcut here just moves that shortcut to the front. Worked through
        ''' last-to-first, so the first copied file ends up first.
        ''' </summary>
        Private Shared Function CaptureFiles(tab As StageInfo, copiedPaths As IReadOnlyList(Of String)) As Boolean
            Dim pathsToKeep As List(Of String) = copiedPaths.Where(AddressOf StageFolder.PathExists).Take(MaximumFilesPerCopy).ToList()
            If pathsToKeep.Count = 0 Then Return False
            If copiedPaths.Count > MaximumFilesPerCopy Then
                Logger.Info(copiedPaths.Count.ToString() & " files were copied; Clipboard history keeps the first " & MaximumFilesPerCopy.ToString())
            End If

            Dim existingShortcutsByTarget As Dictionary(Of String, String) = ReadShortcutTargets(tab.FolderPath)
            pathsToKeep.Reverse()
            For Each copiedPath As String In pathsToKeep
                Dim existingEntryName As String = Nothing
                If existingShortcutsByTarget.TryGetValue(copiedPath, existingEntryName) Then
                    TouchEntry(Path.Combine(tab.FolderPath, existingEntryName))
                    PutAtFront(tab, existingEntryName)
                    Continue For
                End If
                Try
                    Dim newEntryPath As String = StageFolder.CreateShortcutIn(tab.FolderPath, copiedPath)
                    PutAtFront(tab, Path.GetFileName(newEntryPath))
                Catch ex As Exception
                    Logger.Warn("Clipboard history could not make a shortcut to " & copiedPath, ex)
                End Try
            Next
            Return True
        End Function

        ''' <summary>Target path -> entry file name, for the folder's .lnk shortcuts.</summary>
        Private Shared Function ReadShortcutTargets(folderPath As String) As Dictionary(Of String, String)
            Dim byTarget As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each entryPath As String In ListEntryFiles(folderPath)
                If Not StageFolder.IsShortcutFile(entryPath) Then Continue For
                Dim targetPath As String = ShellLinks.TryGetTargetPath(entryPath)
                If targetPath IsNot Nothing AndAlso Not byTarget.ContainsKey(targetPath) Then
                    byTarget.Add(targetPath, Path.GetFileName(entryPath))
                End If
            Next
            Return byTarget
        End Function

        ''' <summary>
        ''' If an entry of this type already holds exactly this content, moves it to the front
        ''' (and marks it as just copied) and returns True.
        ''' </summary>
        Private Shared Function BringExistingEntryToFront(tab As StageInfo, extension As String, fingerprint As String) As Boolean
            For Each entryPath As String In ListEntryFiles(tab.FolderPath)
                If Not String.Equals(Path.GetExtension(entryPath), extension, StringComparison.OrdinalIgnoreCase) Then Continue For
                If String.Equals(CachedFingerprintOfFile(entryPath, extension), fingerprint, StringComparison.Ordinal) Then
                    TouchEntry(entryPath)
                    PutAtFront(tab, Path.GetFileName(entryPath))
                    Return True
                End If
            Next
            Return False
        End Function

        ''' <summary>"First words of the text" as a safe file name.</summary>
        Private Shared Function NameForText(copiedText As String) As String
            Dim oneLine As String = WhitespaceRun.Replace(copiedText.Trim(), " ")
            If oneLine.Length > MaximumTextNameLength Then oneLine = oneLine.Substring(0, MaximumTextNameLength).TrimEnd()
            Dim safeName As String = StageFolder.SanitizeName(oneLine)
            Return If(String.IsNullOrWhiteSpace(safeName), FallbackTextEntryName, safeName)
        End Function

        ''' <summary>Makes <paramref name="entryName"/> the first entry (newest first).</summary>
        Private Shared Sub PutAtFront(tab As StageInfo, entryName As String)
            tab.ItemOrder.RemoveAll(Function(listedName) String.Equals(listedName, entryName, StringComparison.OrdinalIgnoreCase))
            tab.ItemOrder.Insert(0, entryName)
        End Sub

        ''' <summary>Gives an entry the current time, so Sort by > Date modified shows it as the newest too.</summary>
        Private Shared Sub TouchEntry(entryPath As String)
            Try
                File.SetLastWriteTimeUtc(entryPath, DateTime.UtcNow)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                ' Harmless: the entry still moves to the front.
            End Try
        End Sub

#End Region

#Region "Limits and clearing"

        ''' <summary>
        ''' Keeps the newest entries within the tab's count and size limits; deletes the rest
        ''' permanently (they are Stages' own copies and shortcuts, never the user's files).
        ''' </summary>
        Public Shared Sub ApplyLimits(tab As StageInfo)
            Dim maximumBytes As Long = tab.ClipboardMaximumMegabytes * BytesPerMegabyte
            Dim keptCount As Integer = 0
            Dim keptBytes As Long = 0L

            For Each entryPath As String In EntriesNewestFirst(tab)
                Dim entrySize As Long = SizeOf(entryPath)
                Dim fitsLimits As Boolean = keptCount < tab.ClipboardMaximumEntries AndAlso keptBytes + entrySize <= maximumBytes
                If fitsLimits Then
                    keptCount += 1
                    keptBytes += entrySize
                    Continue For
                End If
                DeleteEntry(tab, entryPath)
            Next
        End Sub

        ''' <summary>Deletes every entry (Stage menu > Clear clipboard history...). Returns how many were deleted.</summary>
        Public Shared Function ClearAll(tab As StageInfo) As Integer
            Dim deletedCount As Integer = 0
            For Each entryPath As String In ListEntryFiles(tab.FolderPath)
                If DeleteEntry(tab, entryPath) Then deletedCount += 1
            Next
            Return deletedCount
        End Function

        ''' <summary>The folder's entries: newest first by ItemOrder, then any unlisted ones by date, newest first.</summary>
        Private Shared Function EntriesNewestFirst(tab As StageInfo) As List(Of String)
            Dim entries As List(Of String) = ListEntryFiles(tab.FolderPath)
            Dim orderPositions As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            For position As Integer = 0 To tab.ItemOrder.Count - 1
                If Not orderPositions.ContainsKey(tab.ItemOrder(position)) Then orderPositions.Add(tab.ItemOrder(position), position)
            Next

            Dim byOrder As IOrderedEnumerable(Of String) = entries.OrderBy(
                Function(entryPath)
                    Dim position As Integer
                    Return If(orderPositions.TryGetValue(Path.GetFileName(entryPath), position), position, Integer.MaxValue)
                End Function)
            Dim thenNewestFirst As IOrderedEnumerable(Of String) = byOrder.ThenByDescending(Function(entryPath) LastWriteUtc(entryPath))
            Return thenNewestFirst.ToList()
        End Function

        ''' <summary>The entry files directly in the folder (not hidden ones, not Stages' own files, not folders).</summary>
        Private Shared Function ListEntryFiles(folderPath As String) As List(Of String)
            Dim entries As New List(Of String)()
            If Not Directory.Exists(folderPath) Then Return entries
            Try
                For Each filePath As String In Directory.EnumerateFiles(folderPath)
                    If StageFolder.IsStageOwnFile(Path.GetFileName(filePath)) Then Continue For
                    If Not IsEntryFile(filePath) Then Continue For
                    Dim attributes As FileAttributes = File.GetAttributes(filePath)
                    If (attributes And (FileAttributes.Hidden Or FileAttributes.System)) <> 0 Then Continue For
                    entries.Add(filePath)
                Next
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Logger.Warn("Could not list the Clipboard history in " & folderPath, ex)
            End Try
            Return entries
        End Function

        ''' <summary>Deletes one entry for good and forgets its place. False if it couldn't be deleted.</summary>
        Private Shared Function DeleteEntry(tab As StageInfo, entryPath As String) As Boolean
            Try
                File.Delete(entryPath)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Logger.Warn("Could not delete old Clipboard history entry " & entryPath, ex)
                Return False
            End Try
            Dim entryName As String = Path.GetFileName(entryPath)
            tab.ItemOrder.RemoveAll(Function(listedName) String.Equals(listedName, entryName, StringComparison.OrdinalIgnoreCase))
            tab.ItemPositions.RemoveAll(Function(placed) String.Equals(placed.FileName, entryName, StringComparison.OrdinalIgnoreCase))
            Return True
        End Function

        Private Shared Function SizeOf(filePath As String) As Long
            Try
                Return New FileInfo(filePath).Length
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Return 0L
            End Try
        End Function

        Private Shared Function LastWriteUtc(filePath As String) As DateTime
            Try
                Return File.GetLastWriteTimeUtc(filePath)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Return DateTime.MinValue
            End Try
        End Function

#End Region

#Region "Copying an entry back to the clipboard"

        ''' <summary>
        ''' Puts an entry back on the clipboard (double-click, Enter, or Copy in its menu): the text
        ''' of a .txt, the picture of a .png, or the file a shortcut points to. The entry moves to the
        ''' front. Returns False (logged) if it couldn't be done.
        ''' </summary>
        Public Shared Function CopyBack(tab As StageInfo, item As StageItem) As Boolean
            Dim entryPath As String = item.FullPath
            Dim extension As String = Path.GetExtension(entryPath)
            Try
                Dim dataToCopy As New DataObject()
                If String.Equals(extension, TextEntryExtension, StringComparison.OrdinalIgnoreCase) Then
                    dataToCopy.SetText(File.ReadAllText(entryPath))
                ElseIf String.Equals(extension, PictureEntryExtension, StringComparison.OrdinalIgnoreCase) Then
                    Dim pngBytes As Byte() = File.ReadAllBytes(entryPath)
                    dataToCopy.SetImage(LoadPicture(pngBytes))
                    ' A PNG copy too: programs that understand it keep the picture's transparency.
                    dataToCopy.SetData("PNG", New MemoryStream(pngBytes))
                Else
                    ' A shortcut: the file it points to. A web link (.url), or a shortcut whose
                    ' file has gone, is copied as the shortcut file itself.
                    Dim pathToCopy As String = entryPath
                    If item.TargetPath IsNot Nothing AndAlso StageFolder.PathExists(item.TargetPath) Then
                        pathToCopy = item.TargetPath
                    End If
                    Dim copiedFiles As New StringCollection() From {pathToCopy}
                    dataToCopy.SetFileDropList(copiedFiles)
                End If

                ClipboardMonitor.Instance.IgnoreOwnCopy()
                If Not TrySetClipboard(dataToCopy) Then Return False
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is NotSupportedException
                Logger.Warn("Could not copy Clipboard history entry " & entryPath, ex)
                Return False
            End Try

            TouchEntry(entryPath)
            PutAtFront(tab, item.FileName)
            Return True
        End Function

        ''' <summary>A .png's bytes as a frozen picture for the clipboard.</summary>
        Private Shared Function LoadPicture(pngBytes As Byte()) As BitmapSource
            Using pictureStream As New MemoryStream(pngBytes)
                Dim picture As New BitmapImage()
                picture.BeginInit()
                picture.CacheOption = BitmapCacheOption.OnLoad ' read it all now, so the stream can close
                picture.StreamSource = pictureStream
                picture.EndInit()
                picture.Freeze()
                Return picture
            End Using
        End Function

        ''' <summary>Sets the clipboard, retrying briefly if another program has it open. Kept after Stages exits.</summary>
        Private Shared Function TrySetClipboard(dataToCopy As DataObject) As Boolean
            For attempt As Integer = 1 To ClipboardWriteAttempts
                Try
                    Clipboard.SetDataObject(dataToCopy, copy:=True)
                    Return True
                Catch ex As Exception When TypeOf ex Is COMException OrElse TypeOf ex Is ExternalException
                    Thread.Sleep(ClipboardWriteRetryMilliseconds)
                End Try
            Next
            Logger.Warn("The clipboard stayed locked by another program; the entry wasn't copied")
            Return False
        End Function

#End Region

#Region "Content fingerprints"

        ''' <summary>A short text that is the same for identical content and different otherwise (SHA-256).</summary>
        Private Shared Function FingerprintOf(content As Byte()) As String
            Using hasher As SHA256 = SHA256.Create()
                Return Convert.ToBase64String(hasher.ComputeHash(content))
            End Using
        End Function

        ''' <summary>
        ''' The fingerprint of an entry file's content, remembered while the file is unchanged.
        ''' Text entries are fingerprinted as text (so the byte-order mark doesn't count).
        ''' </summary>
        Private Shared Function CachedFingerprintOfFile(entryPath As String, extension As String) As String
            Try
                Dim details As New FileInfo(entryPath)
                Dim cacheKey As String = entryPath & "|" & details.Length.ToString() & "|" & details.LastWriteTimeUtc.Ticks.ToString()
                Dim known As String = Nothing
                If FingerprintCache.TryGetValue(cacheKey, known) Then Return known

                If FingerprintCache.Count > MaximumCachedFingerprints Then FingerprintCache.Clear()
                Dim content As Byte()
                If String.Equals(extension, TextEntryExtension, StringComparison.OrdinalIgnoreCase) Then
                    content = Encoding.UTF8.GetBytes(File.ReadAllText(entryPath))
                Else
                    content = File.ReadAllBytes(entryPath)
                End If
                Dim fingerprint As String = FingerprintOf(content)
                FingerprintCache(cacheKey) = fingerprint
                Return fingerprint
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Return Nothing ' unreadable: treat as different
            End Try
        End Function

#End Region

    End Class

End Namespace
