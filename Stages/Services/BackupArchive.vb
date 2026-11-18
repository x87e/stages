' ============================================================================
'  BackupArchive.vb
'  Settings > Backup: saves everything Stages knows into one .zip file, and
'  puts a .zip like that back.
'
'  WHAT A BACKUP HOLDS (paths inside the zip):
'      settings.xml        Stages' settings (Settings window)
'      Data\...            every Stage folder: its shortcuts, its hidden
'                          .stage-settings.xml, its tabs, its background picture
'  Not included: Logs\ and App\ (the program itself). A folder Stage's backup
'  holds its settings, not the files of the folder it shows.
'
'  IMPORTING happens in two halves, because files can't be swapped safely
'  while Stages has them open and is watching its folders:
'    1. PrepareImport (Stages running): checks the zip and extracts it into
'       <Stages folder>\Import-temp. Nothing current is touched yet.
'    2. Stages restarts with FinishImportArgument. Before loading anything,
'       FinishPendingImport moves the current Data folder / settings.xml
'       aside, moves the extracted ones into place, sends the old copies to
'       the Recycle Bin, and deletes Import-temp. If any move fails, what was
'       already moved is put back, so Stages is never left half-imported.
'  Each part only replaces its current counterpart if the backup has it: a
'  backup without settings.xml keeps the current settings, and so on.
' ============================================================================
Imports System.IO
Imports System.IO.Compression

Namespace Services

    ''' <summary>How an import step went.</summary>
    Public Enum ImportOutcome
        ''' <summary>Done (or ready to finish after the restart).</summary>
        Succeeded
        ''' <summary>The zip doesn't look like a Stages backup (no settings.xml and no Data folder).</summary>
        NotABackup
        ''' <summary>The zip is damaged, or a file is unsafe (e.g. it would land outside the Stages folder).</summary>
        Unreadable
        ''' <summary>The zip would unpack to more than MaximumUnpackedBytes.</summary>
        TooLarge
        ''' <summary>A file couldn't be read, written or moved (details in the log).</summary>
        FileProblem
    End Enum

    Public NotInheritable Class BackupArchive

#Region "Constants"
        ''' <summary>Command-line switch the restarted Stages gets, telling it to finish an import first.</summary>
        Public Const FinishImportArgument As String = "--finish-import"

        ''' <summary>Names inside a backup. Data is a folder, settings.xml a file, both at the top.</summary>
        Private Const DataEntryName As String = "Data"
        Private Const SettingsEntryName As String = "settings.xml"
        ''' <summary>Zip files always use "/" between folder names (Windows zips sometimes use "\").</summary>
        Private Const ZipFolderSeparator As Char = "/"c
        Private Const WindowsFolderSeparator As Char = "\"c

        ''' <summary>Folder inside the Stages folder that an import is extracted into (same drive, so moves are instant).</summary>
        Private Const ImportTempFolderName As String = "Import-temp"
        ''' <summary>The current Data / settings.xml are renamed to these during the swap, then recycled.</summary>
        Private Const ReplacedDataFolderName As String = "Data (before import)"
        Private Const ReplacedSettingsFileName As String = "settings (before import).xml"

        ''' <summary>Refuse backups that unpack to more than 2 GB (a damaged or hostile zip can claim huge sizes).</summary>
        Private Const MaximumUnpackedBytes As Long = 2L * 1024L * 1024L * 1024L
        ''' <summary>Stages' own hidden files in a Stage folder start with this (as in StageFolder).</summary>
        Private Const StagesOwnFilePrefix As String = ".stage-"
        ''' <summary>Copy buffer size for each file.</summary>
        Private Const CopyBufferBytes As Integer = 81920

        ''' <summary>Suggested backup file name; {0} = today's date.</summary>
        Public Const SuggestedFileNameFormat As String = "Stages backup {0:yyyy-MM-dd}.zip"
        Public Const BackupFileFilter As String = "Stages backup (*.zip)|*.zip|All files (*.*)|*.*"
#End Region

        Private Sub New()
        End Sub

#Region "Backup"

        ''' <summary>
        ''' Writes settings.xml and the whole Data folder into a new zip at
        ''' <paramref name="zipPath"/> (replacing any file already there). The caller saves
        ''' any pending changes first. Returns the number of files in the backup. Throws
        ''' IOException / UnauthorizedAccessException if the zip can't be written.
        ''' </summary>
        Public Shared Function CreateBackup(zipPath As String) As Integer
            ' Written to a temporary name first, so a failure never leaves a half-written
            ' file under the name the user chose (or destroys an older backup there).
            Dim partialZipPath As String = zipPath & AtomicFile.TemporaryFileSuffix
            Dim fileCount As Integer = 0
            Try
                Using zipStream As New FileStream(partialZipPath, FileMode.Create, FileAccess.Write, FileShare.None)
                    Using backup As New ZipArchive(zipStream, ZipArchiveMode.Create)
                        If File.Exists(AppPaths.SettingsFilePath) Then
                            AddFile(backup, AppPaths.SettingsFilePath, SettingsEntryName)
                            fileCount += 1
                        End If
                        If Directory.Exists(AppPaths.DataFolder) Then
                            fileCount += AddFolder(backup, AppPaths.DataFolder, DataEntryName)
                        End If
                    End Using
                End Using
                If File.Exists(zipPath) Then File.Delete(zipPath)
                File.Move(partialZipPath, zipPath)
            Catch
                TryDeleteFile(partialZipPath)
                Throw
            End Try

            Logger.Info("Backup saved to " & zipPath & " (" & fileCount.ToString() & " files)")
            Return fileCount
        End Function

        ''' <summary>
        ''' Adds every file under <paramref name="folderPath"/>, hidden ones included (each
        ''' Stage's settings file is hidden), as "<paramref name="entryPrefix"/>/relative/path".
        ''' Empty folders get an entry of their own, so an empty Stage comes back too.
        ''' </summary>
        Private Shared Function AddFolder(backup As ZipArchive, folderPath As String, entryPrefix As String) As Integer
            Dim fileCount As Integer = 0
            For Each filePath As String In Directory.EnumerateFiles(folderPath)
                AddFile(backup, filePath, entryPrefix & ZipFolderSeparator & Path.GetFileName(filePath))
                fileCount += 1
            Next
            Dim subfolders As List(Of String) = Directory.EnumerateDirectories(folderPath).ToList()
            For Each subfolderPath As String In subfolders
                fileCount += AddFolder(backup, subfolderPath, entryPrefix & ZipFolderSeparator & Path.GetFileName(subfolderPath))
            Next
            If fileCount = 0 AndAlso subfolders.Count = 0 Then
                backup.CreateEntry(entryPrefix & ZipFolderSeparator) ' a name ending in "/" is a folder
            End If
            Return fileCount
        End Function

        ''' <summary>
        ''' Copies one file into the zip. Opened with sharing allowed, so a file Stages or
        ''' another program has open (e.g. a settings file being saved) can still be read.
        ''' </summary>
        Private Shared Sub AddFile(backup As ZipArchive, filePath As String, entryName As String)
            Dim entry As ZipArchiveEntry = backup.CreateEntry(entryName, CompressionLevel.Optimal)
            entry.LastWriteTime = File.GetLastWriteTime(filePath)
            Using source As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                Using destination As Stream = entry.Open()
                    source.CopyTo(destination, CopyBufferBytes)
                End Using
            End Using
        End Sub

#End Region

#Region "Import, part 1: check and extract (Stages running)"

        ''' <summary>
        ''' Checks <paramref name="zipPath"/> is a Stages backup and extracts it into the
        ''' Import-temp folder, ready for FinishPendingImport after a restart. Changes nothing
        ''' current. Never throws.
        ''' </summary>
        Public Shared Function PrepareImport(zipPath As String) As ImportOutcome
            Dim tempFolder As String = ImportTempFolder()
            Try
                DeleteFolderIfPresent(tempFolder) ' leftovers from an import that didn't finish
                Using backup As ZipArchive = ZipFile.OpenRead(zipPath)
                    ' A zip made by hand from the Stages folder itself has everything inside one
                    ' top folder ("Stages/Data/..."): look inside it if so.
                    Dim rootPrefix As String = FindBackupRoot(backup)
                    If rootPrefix Is Nothing Then Return ImportOutcome.NotABackup

                    Dim unpackedBytes As Long = 0
                    For Each entry As ZipArchiveEntry In backup.Entries
                        unpackedBytes += entry.Length
                    Next
                    If unpackedBytes > MaximumUnpackedBytes Then Return ImportOutcome.TooLarge

                    Directory.CreateDirectory(tempFolder)
                    Dim tempFolderWithSeparator As String = Path.GetFullPath(tempFolder).TrimEnd(WindowsFolderSeparator) & WindowsFolderSeparator
                    For Each entry As ZipArchiveEntry In backup.Entries
                        Dim relativeName As String = NormaliseEntryName(entry.FullName)
                        If Not relativeName.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) Then Continue For
                        relativeName = relativeName.Substring(rootPrefix.Length)
                        If Not IsPartOfBackup(relativeName) Then Continue For ' anything else in the zip is ignored

                        ' Safety: refuse any name that would land outside Import-temp ("..\..\Windows\...").
                        Dim targetPath As String = Path.GetFullPath(Path.Combine(tempFolder, relativeName.Replace(ZipFolderSeparator, WindowsFolderSeparator)))
                        If Not targetPath.StartsWith(tempFolderWithSeparator, StringComparison.OrdinalIgnoreCase) Then
                            Logger.Warn("Import refused: the backup contains an unsafe path, " & entry.FullName)
                            DeleteFolderIfPresent(tempFolder)
                            Return ImportOutcome.Unreadable
                        End If

                        If relativeName.EndsWith(ZipFolderSeparator) Then
                            Directory.CreateDirectory(targetPath) ' an empty folder
                        Else
                            Directory.CreateDirectory(Path.GetDirectoryName(targetPath))
                            entry.ExtractToFile(targetPath, overwrite:=True)
                            ' Stages' own files (.stage-settings.xml, .stage-background.*) are hidden; keep them so.
                            If Path.GetFileName(targetPath).StartsWith(StagesOwnFilePrefix, StringComparison.OrdinalIgnoreCase) Then
                                File.SetAttributes(targetPath, File.GetAttributes(targetPath) Or FileAttributes.Hidden)
                            End If
                        End If
                    Next
                End Using
                Logger.Info("Backup " & zipPath & " extracted, ready to import after restarting")
                Return ImportOutcome.Succeeded
            Catch ex As InvalidDataException
                Logger.Warn("Import refused: " & zipPath & " is not a readable zip file", ex)
                DeleteFolderIfPresent(tempFolder)
                Return ImportOutcome.Unreadable
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                       TypeOf ex Is NotSupportedException OrElse TypeOf ex Is ArgumentException
                Logger.Error("Could not extract the backup " & zipPath, ex)
                DeleteFolderIfPresent(tempFolder)
                Return ImportOutcome.FileProblem
            End Try
        End Function

        ''' <summary>
        ''' Where the backup's settings.xml / Data are inside the zip: "" for the top, or
        ''' "SomeFolder/" if everything is inside one folder. Nothing if neither is found.
        ''' </summary>
        Private Shared Function FindBackupRoot(backup As ZipArchive) As String
            Dim names As List(Of String) = backup.Entries.Select(Function(entry) NormaliseEntryName(entry.FullName)).ToList()
            If names.Any(Function(name) IsPartOfBackup(name)) Then Return String.Empty

            ' One level down: "Stages/settings.xml", "Stages/Data/...".
            Dim namesInFolders As IEnumerable(Of String) = names.Where(Function(name) name.Contains(ZipFolderSeparator))
            Dim topFolderOfEach As IEnumerable(Of String) =
                namesInFolders.Select(Function(name) name.Substring(0, name.IndexOf(ZipFolderSeparator) + 1))
            Dim topFolders As List(Of String) = topFolderOfEach.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            If topFolders.Count <> 1 Then Return Nothing
            Dim onlyTopFolder As String = topFolders(0)
            Dim hasBackupInside As Boolean = names.Any(
                Function(name) name.StartsWith(onlyTopFolder, StringComparison.OrdinalIgnoreCase) AndAlso
                               IsPartOfBackup(name.Substring(onlyTopFolder.Length)))
            Return If(hasBackupInside, onlyTopFolder, Nothing)
        End Function

        ''' <summary>True for "settings.xml" and anything under "Data/".</summary>
        Private Shared Function IsPartOfBackup(relativeName As String) As Boolean
            Return String.Equals(relativeName, SettingsEntryName, StringComparison.OrdinalIgnoreCase) OrElse
                   relativeName.StartsWith(DataEntryName & ZipFolderSeparator, StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>Zip entry names with "/" between folders and no leading "/".</summary>
        Private Shared Function NormaliseEntryName(entryName As String) As String
            Return entryName.Replace(WindowsFolderSeparator, ZipFolderSeparator).TrimStart(ZipFolderSeparator)
        End Function

#End Region

#Region "Import, part 2: swap the files in (restarted Stages, before loading anything)"

        ''' <summary>
        ''' Moves an extracted backup into place. Called once at startup, before settings and
        ''' Stages are loaded, only when Stages was restarted with FinishImportArgument.
        ''' Returns Succeeded if there was nothing to do. Never throws.
        ''' </summary>
        Public Shared Function FinishPendingImport() As ImportOutcome
            Dim tempFolder As String = ImportTempFolder()
            If Not Directory.Exists(tempFolder) Then Return ImportOutcome.Succeeded

            Dim importedData As String = Path.Combine(tempFolder, DataEntryName)
            Dim importedSettings As String = Path.Combine(tempFolder, SettingsEntryName)
            Dim hasData As Boolean = Directory.Exists(importedData)
            Dim hasSettings As Boolean = File.Exists(importedSettings)
            If Not hasData AndAlso Not hasSettings Then
                DeleteFolderIfPresent(tempFolder)
                Return ImportOutcome.NotABackup
            End If

            Dim replacedData As String = Path.Combine(AppPaths.RootFolder, ReplacedDataFolderName)
            Dim replacedSettings As String = Path.Combine(AppPaths.RootFolder, ReplacedSettingsFileName)
            Dim movedOldData As Boolean = False
            Dim movedNewData As Boolean = False
            Dim movedOldSettings As Boolean = False
            Try
                ' Clear names left by an earlier import that couldn't recycle its old copies.
                DeleteFolderIfPresent(replacedData)
                TryDeleteFile(replacedSettings)

                If hasData Then
                    If Directory.Exists(AppPaths.DataFolder) Then
                        Directory.Move(AppPaths.DataFolder, replacedData)
                        movedOldData = True
                    End If
                    Directory.Move(importedData, AppPaths.DataFolder)
                    movedNewData = True
                End If
                If hasSettings Then
                    If File.Exists(AppPaths.SettingsFilePath) Then
                        File.Move(AppPaths.SettingsFilePath, replacedSettings)
                        movedOldSettings = True
                    End If
                    File.Move(importedSettings, AppPaths.SettingsFilePath)
                    ' The old settings' backup copy must not be read in place of the imported file.
                    TryDeleteFile(AppPaths.SettingsFilePath & AtomicFile.BackupFileSuffix)
                End If
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Logger.Error("Could not move the imported backup into place; putting the previous Stages back", ex)
                PutBackAfterFailedImport(movedOldData, movedNewData, movedOldSettings, replacedData, replacedSettings)
                DeleteFolderIfPresent(tempFolder)
                Return ImportOutcome.FileProblem
            End Try

            ' In place. The old copies go to the Recycle Bin (restorable from there); the
            ' extracted files have all been moved, so Import-temp only holds empty leftovers.
            TryRecycle(replacedData)
            TryRecycle(replacedSettings)
            DeleteFolderIfPresent(tempFolder)
            Logger.Info("Backup imported (Stage folders: " & hasData.ToString() & ", settings: " & hasSettings.ToString() & ")")
            Return ImportOutcome.Succeeded
        End Function

        ''' <summary>Undoes the moves of a failed swap, in reverse order. Problems are logged, never thrown.</summary>
        Private Shared Sub PutBackAfterFailedImport(movedOldData As Boolean, movedNewData As Boolean, movedOldSettings As Boolean,
                                                    replacedData As String, replacedSettings As String)
            Try
                If movedOldSettings AndAlso File.Exists(replacedSettings) Then
                    TryDeleteFile(AppPaths.SettingsFilePath)
                    File.Move(replacedSettings, AppPaths.SettingsFilePath)
                End If
                If movedNewData Then
                    ' The imported Data goes back to Import-temp, which is deleted afterwards.
                    Directory.Move(AppPaths.DataFolder, Path.Combine(ImportTempFolder(), DataEntryName))
                End If
                If movedOldData Then
                    Directory.Move(replacedData, AppPaths.DataFolder)
                End If
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                ' Nothing has been deleted: the previous Stages are still in "Data (before import)".
                Logger.Error("Could not fully put back the previous Stages after a failed import. " &
                             "They are safe in " & replacedData, ex)
            End Try
        End Sub

#End Region

#Region "Small helpers"

        Private Shared Function ImportTempFolder() As String
            Return Path.Combine(AppPaths.RootFolder, ImportTempFolderName)
        End Function

        ''' <summary>Deletes a folder of Stages' own (Import-temp, a leftover copy) and everything in it. Logged, never thrown.</summary>
        Private Shared Sub DeleteFolderIfPresent(folderPath As String)
            Try
                If Not Directory.Exists(folderPath) Then Return
                ' Hidden and read-only files would make Directory.Delete fail; clear those flags first.
                For Each filePath As String In Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                    File.SetAttributes(filePath, FileAttributes.Normal)
                Next
                Directory.Delete(folderPath, recursive:=True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Logger.Warn("Could not delete the temporary folder " & folderPath, ex)
            End Try
        End Sub

        Private Shared Sub TryDeleteFile(filePath As String)
            Try
                If File.Exists(filePath) Then
                    File.SetAttributes(filePath, FileAttributes.Normal)
                    File.Delete(filePath)
                End If
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Logger.Warn("Could not delete " & filePath, ex)
            End Try
        End Sub

        ''' <summary>Sends the previous Stages' copy to the Recycle Bin. If that fails it stays where it is, and is logged.</summary>
        Private Shared Sub TryRecycle(itemPath As String)
            Try
                StageFolder.SendToRecycleBin(itemPath)
            Catch ex As Exception
                Logger.Warn("Could not send " & itemPath & " to the Recycle Bin; it has been left in place", ex)
            End Try
        End Sub

#End Region

    End Class

End Namespace
