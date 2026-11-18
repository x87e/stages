' ============================================================================
'  AtomicFile.vb
'  Writes a file so that a crash or power cut mid-save can never leave it
'  half-written. Used for every settings file Stages saves:
'      * each Stage's .stage-settings.xml (StageManager)
'      * the app-wide settings.xml        (AppSettingsStore)
'
'  How: write the new content to "<file>.tmp", force it onto the disk, then
'  swap it in with File.Replace, which is atomic on NTFS and keeps the
'  previous version as "<file>.bak". Readers fall back to the .bak if the
'  main file is ever damaged.
' ============================================================================
Imports System.IO

Namespace Services

    Public NotInheritable Class AtomicFile

        ''' <summary>Suffix of the scratch file the new content is written to first.</summary>
        Public Const TemporaryFileSuffix As String = ".tmp"
        ''' <summary>Suffix of the copy of the previous version kept after each save.</summary>
        Public Const BackupFileSuffix As String = ".bak"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Replaces <paramref name="targetPath"/> with <paramref name="contentBytes"/> safely,
        ''' keeping the previous version as targetPath + ".bak". Throws on failure (the caller
        ''' decides how to report it); the original file is left untouched in that case.
        ''' </summary>
        Public Shared Sub WriteAllBytes(targetPath As String, contentBytes As Byte())
            Dim temporaryPath As String = targetPath & TemporaryFileSuffix
            Dim backupPath As String = targetPath & BackupFileSuffix

            ' Write the scratch file and make sure it is physically on the disk.
            Using tempFile As New FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)
                tempFile.Write(contentBytes, 0, contentBytes.Length)
                tempFile.Flush(flushToDisk:=True)
            End Using

            ' Swap it in. First ever save: there's nothing to replace, so just rename.
            If File.Exists(targetPath) Then
                ReplaceWithBackup(temporaryPath, targetPath, backupPath)
            Else
                File.Move(temporaryPath, targetPath)
            End If
        End Sub

        ''' <summary>
        ''' File.Replace is atomic on NTFS. Some drives (FAT, certain network shares) don't
        ''' support it, so fall back to backup-copy-then-overwrite there.
        ''' </summary>
        Private Shared Sub ReplaceWithBackup(newFilePath As String, targetPath As String, backupPath As String)
            Try
                File.Replace(newFilePath, targetPath, backupPath, ignoreMetadataErrors:=True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is PlatformNotSupportedException
                Logger.Warn("Atomic replace not available, using copy fallback: " & ex.Message)
                File.Copy(targetPath, backupPath, overwrite:=True)
                File.Copy(newFilePath, targetPath, overwrite:=True)
                File.Delete(newFilePath)
            End Try
        End Sub

    End Class

End Namespace
