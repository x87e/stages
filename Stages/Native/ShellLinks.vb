' ============================================================================
'  ShellLinks.vb
'  Creates and reads Windows shortcut (.lnk) files using the shell's own
'  ShellLink COM object - the same thing Explorer uses for "Create shortcut".
'  No extra libraries needed. Also reads and sets the custom icon of .lnk and
'  .url (internet shortcut) files, for "Change icon..." / "Reset icon".
'
'  Shortcuts made this way also get Windows' link tracking: on NTFS drives,
'  if the target is moved or renamed, Windows can usually still find it.
'
'  32-bit / 64-bit programs: Stages runs as a 64-bit program. Shortcuts made by
'  32-bit installers often store their target as "%ProgramFiles%\App\app.exe".
'  Read by a 64-bit program, %ProgramFiles% means "C:\Program Files", not
'  "C:\Program Files (x86)" where the program really is - so the target looks
'  missing (dimmed, wrong icon). Explorer avoids this by also checking the exact
'  location saved inside the shortcut, and so does Stages: see FindRealPath.
'  (Opening a shortcut is unaffected: Windows resolves it itself.)
'
'  Call from an STA thread (the UI thread or StaWorkerThread).
' ============================================================================
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Runtime.InteropServices.ComTypes
Imports System.Text
Imports Stages.Services

Namespace Native

    ''' <summary>
    ''' IShellLinkW - read/write the properties of a shortcut. All methods are listed
    ''' because COM needs them in exact vtable order, even the ones Stages doesn't call.
    ''' (Without PreserveSig, a failed HRESULT is thrown as an exception automatically.)
    ''' </summary>
    <ComImport(), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")>
    Friend Interface IShellLinkW
        Sub GetPath(<Out(), MarshalAs(UnmanagedType.LPWStr)> pszFile As StringBuilder, cch As Integer, pfd As IntPtr, fFlags As UInteger)
        Sub GetIDList(ByRef ppidl As IntPtr)
        Sub SetIDList(pidl As IntPtr)
        Sub GetDescription(<Out(), MarshalAs(UnmanagedType.LPWStr)> pszName As StringBuilder, cch As Integer)
        Sub SetDescription(<MarshalAs(UnmanagedType.LPWStr)> pszName As String)
        Sub GetWorkingDirectory(<Out(), MarshalAs(UnmanagedType.LPWStr)> pszDir As StringBuilder, cch As Integer)
        Sub SetWorkingDirectory(<MarshalAs(UnmanagedType.LPWStr)> pszDir As String)
        Sub GetArguments(<Out(), MarshalAs(UnmanagedType.LPWStr)> pszArgs As StringBuilder, cch As Integer)
        Sub SetArguments(<MarshalAs(UnmanagedType.LPWStr)> pszArgs As String)
        Sub GetHotkey(ByRef pwHotkey As Short)
        Sub SetHotkey(wHotkey As Short)
        Sub GetShowCmd(ByRef piShowCmd As Integer)
        Sub SetShowCmd(iShowCmd As Integer)
        Sub GetIconLocation(<Out(), MarshalAs(UnmanagedType.LPWStr)> pszIconPath As StringBuilder, cch As Integer, ByRef piIcon As Integer)
        Sub SetIconLocation(<MarshalAs(UnmanagedType.LPWStr)> pszIconPath As String, iIcon As Integer)
        Sub SetRelativePath(<MarshalAs(UnmanagedType.LPWStr)> pszPathRel As String, dwReserved As Integer)
        Sub Resolve(hwnd As IntPtr, fFlags As UInteger)
        Sub SetPath(<MarshalAs(UnmanagedType.LPWStr)> pszFile As String)
    End Interface

    ''' <summary>The parts of a .lnk that Stages uses.</summary>
    Friend NotInheritable Class ShortcutDetails
        ''' <summary>File/folder the shortcut opens, or Nothing (e.g. a Control Panel item).</summary>
        Public Property TargetPath As String
        ''' <summary>Custom icon file chosen in the shortcut's Properties (environment variables expanded), or Nothing.</summary>
        Public Property IconFilePath As String
        ''' <summary>Index (or negative resource id) of the icon inside IconFilePath.</summary>
        Public Property IconIndex As Integer
        ''' <summary>The custom icon path exactly as stored (environment variables NOT expanded), or Nothing.</summary>
        Public Property IconLocationAsStored As String
    End Class

    Friend NotInheritable Class ShellLinks

        ''' <summary>Class id of the shell's ShellLink object.</summary>
        Private Shared ReadOnly ShellLinkClassId As New Guid("00021401-0000-0000-C000-000000000046")
        ''' <summary>Buffer size for reading a shortcut's target (long enough for long paths).</summary>
        Private Const TargetPathBufferCharacters As Integer = 1024
        ''' <summary>IPersistFile.Load mode STGM_READ.</summary>
        Private Const OpenForReading As Integer = 0
        ''' <summary>IPersistFile.Load mode STGM_READWRITE (needed to save changes back).</summary>
        Private Const OpenForReadingAndWriting As Integer = 2
        ''' <summary>SetIconLocation with an empty path removes a custom icon (back to the target's own).</summary>
        Private Const NoCustomIconPath As String = ""
        Private Const NoCustomIconIndex As Integer = 0
        ''' <summary>GetPath flags: 0 = the normal, expanded target path.</summary>
        Private Const DefaultGetPathFlags As UInteger = 0UI

        ' .url files are small INI files:  [InternetShortcut] / URL=... / IconFile=... / IconIndex=...
        Private Const InternetShortcutIconFileKey As String = "IconFile="
        Private Const InternetShortcutIconIndexKey As String = "IconIndex="
        ''' <summary>The same keys and section, as names for WritePrivateProfileString.</summary>
        Private Const InternetShortcutSection As String = "InternetShortcut"
        Private Const InternetShortcutIconFileName As String = "IconFile"
        Private Const InternetShortcutIconIndexName As String = "IconIndex"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Writes a new .lnk at <paramref name="shortcutPath"/> pointing at <paramref name="targetPath"/>
        ''' (a file or folder). The working folder is set to the target's folder, as Explorer does.
        ''' </summary>
        Public Shared Sub CreateShortcut(shortcutPath As String, targetPath As String)
            Dim link As IShellLinkW = CreateShellLinkObject()
            Try
                link.SetPath(targetPath)

                Dim workingFolder As String
                If Directory.Exists(targetPath) Then
                    workingFolder = targetPath
                Else
                    workingFolder = Path.GetDirectoryName(targetPath)
                End If
                If Not String.IsNullOrEmpty(workingFolder) Then
                    link.SetWorkingDirectory(workingFolder)
                End If

                ' The same COM object also implements IPersistFile, which saves it to disk.
                Const RememberAsCurrentFile As Boolean = True
                DirectCast(link, IPersistFile).Save(shortcutPath, RememberAsCurrentFile)
            Finally
                Marshal.FinalReleaseComObject(link)
            End Try
        End Sub

        ''' <summary>
        ''' Reads where a .lnk points. Returns Nothing if the file can't be read, or if the
        ''' shortcut points at something with no file path (e.g. a Control Panel item).
        ''' Does NOT go searching for a moved target (that can be slow and show UI).
        ''' </summary>
        Public Shared Function TryGetTargetPath(shortcutPath As String) As String
            Dim details As ShortcutDetails = TryReadShortcut(shortcutPath)
            If details Is Nothing Then
                Return Nothing
            End If
            Return details.TargetPath
        End Function

        ''' <summary>Reads a .lnk's target and custom icon. Returns Nothing if the file can't be read.</summary>
        Public Shared Function TryReadShortcut(shortcutPath As String) As ShortcutDetails
            Dim link As IShellLinkW = Nothing
            Try
                link = CreateShellLinkObject()
                DirectCast(link, IPersistFile).Load(shortcutPath, OpenForReading)

                Dim targetBuffer As New StringBuilder(TargetPathBufferCharacters)
                link.GetPath(targetBuffer, targetBuffer.Capacity, IntPtr.Zero, DefaultGetPathFlags)
                Dim targetPath As String = FindRealPath(EmptyToNothing(targetBuffer.ToString()), GetSavedLocation(link))

                Dim iconBuffer As New StringBuilder(TargetPathBufferCharacters)
                Dim iconIndex As Integer = 0
                link.GetIconLocation(iconBuffer, iconBuffer.Capacity, iconIndex)
                Dim expandedIconPath As String = EmptyToNothing(Environment.ExpandEnvironmentVariables(iconBuffer.ToString()))

                Return New ShortcutDetails With {
                    .TargetPath = targetPath,
                    .IconFilePath = FindRealPath(expandedIconPath, Nothing),
                    .IconIndex = iconIndex,
                    .IconLocationAsStored = EmptyToNothing(iconBuffer.ToString())
                }
            Catch ex As Exception
                Logger.Warn("Could not read shortcut " & shortcutPath & ": " & ex.Message)
                Return Nothing
            Finally
                If link IsNot Nothing Then
                    Marshal.FinalReleaseComObject(link)
                End If
            End Try
        End Function

        ''' <summary>
        ''' Reads the custom icon named inside a .url internet shortcut, if it is a local file.
        ''' Returns False if there is none (many .url files point at a web favicon instead).
        ''' </summary>
        Public Shared Function TryReadInternetShortcutIcon(urlFilePath As String, ByRef iconFilePath As String,
                                                           ByRef iconIndex As Integer) As Boolean
            Dim storedIconPath As String = Nothing
            If Not TryReadInternetShortcutIconSetting(urlFilePath, storedIconPath, iconIndex) Then
                iconFilePath = Nothing
                Return False
            End If
            iconFilePath = Environment.ExpandEnvironmentVariables(storedIconPath)
            Return File.Exists(iconFilePath)
        End Function

        ''' <summary>
        ''' Reads the IconFile / IconIndex lines of a .url file exactly as stored (may be a web
        ''' address or contain %variables%). Returns False if it has no IconFile line.
        ''' </summary>
        Public Shared Function TryReadInternetShortcutIconSetting(urlFilePath As String, ByRef storedIconPath As String,
                                                                  ByRef iconIndex As Integer) As Boolean
            storedIconPath = Nothing
            iconIndex = 0
            Try
                For Each line As String In File.ReadAllLines(urlFilePath)
                    Dim trimmedLine As String = line.Trim()
                    If trimmedLine.StartsWith(InternetShortcutIconFileKey, StringComparison.OrdinalIgnoreCase) Then
                        storedIconPath = trimmedLine.Substring(InternetShortcutIconFileKey.Length)
                    ElseIf trimmedLine.StartsWith(InternetShortcutIconIndexKey, StringComparison.OrdinalIgnoreCase) Then
                        Integer.TryParse(trimmedLine.Substring(InternetShortcutIconIndexKey.Length), iconIndex)
                    End If
                Next
            Catch ex As Exception
                Return False
            End Try
            Return Not String.IsNullOrEmpty(storedIconPath)
        End Function

        ''' <summary>
        ''' Gives a .lnk a custom icon (as Explorer's Properties > Change Icon does), or removes
        ''' it when <paramref name="iconPath"/> is Nothing. Returns False (and logs) on failure.
        ''' Call on an STA thread.
        ''' </summary>
        Public Shared Function TrySetShortcutIcon(shortcutPath As String, iconPath As String, iconIndex As Integer) As Boolean
            Dim link As IShellLinkW = Nothing
            Try
                link = CreateShellLinkObject()
                Dim linkFile As IPersistFile = DirectCast(link, IPersistFile)
                linkFile.Load(shortcutPath, OpenForReadingAndWriting)

                If String.IsNullOrEmpty(iconPath) Then
                    link.SetIconLocation(NoCustomIconPath, NoCustomIconIndex)
                Else
                    link.SetIconLocation(iconPath, iconIndex)
                End If

                Const RememberAsCurrentFile As Boolean = True
                linkFile.Save(shortcutPath, RememberAsCurrentFile)
                Return True
            Catch ex As Exception
                Logger.Warn("Could not change the icon of shortcut " & shortcutPath, ex)
                Return False
            Finally
                If link IsNot Nothing Then
                    Marshal.FinalReleaseComObject(link)
                End If
            End Try
        End Function

        ''' <summary>
        ''' Gives a .url internet shortcut a custom icon by writing its IconFile / IconIndex lines,
        ''' or removes them when <paramref name="iconPath"/> is Nothing (back to the browser's
        ''' page icon). Uses Windows' own INI-file writer. Returns False (and logs) on failure.
        ''' </summary>
        Public Shared Function TrySetInternetShortcutIcon(urlFilePath As String, iconPath As String, iconIndex As Integer) As Boolean
            Try
                ' Passing Nothing as the value deletes the line.
                Dim iconFileValue As String = If(String.IsNullOrEmpty(iconPath), Nothing, iconPath)
                Dim iconIndexValue As String = If(iconFileValue Is Nothing, Nothing,
                                                  iconIndex.ToString(Globalization.CultureInfo.InvariantCulture))
                Dim fileWritten As Boolean =
                    NativeMethods.WritePrivateProfileString(InternetShortcutSection, InternetShortcutIconFileName, iconFileValue, urlFilePath) AndAlso
                    NativeMethods.WritePrivateProfileString(InternetShortcutSection, InternetShortcutIconIndexName, iconIndexValue, urlFilePath)
                If Not fileWritten Then
                    Logger.Warn("Windows could not write the icon into " & urlFilePath &
                                " (error " & Marshal.GetLastWin32Error().ToString() & ")")
                End If
                Return fileWritten
            Catch ex As Exception
                Logger.Warn("Could not change the icon of " & urlFilePath, ex)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' The exact location saved inside the shortcut when it was made (its "ID list"), as a
        ''' file path, or Nothing if it has none or it isn't a file/folder (e.g. a Store app).
        ''' </summary>
        Private Shared Function GetSavedLocation(link As IShellLinkW) As String
            Dim savedItemId As IntPtr = IntPtr.Zero
            Try
                link.GetIDList(savedItemId)
                If savedItemId = IntPtr.Zero Then Return Nothing
                Dim pathBuffer As New StringBuilder(NativeMethods.MAX_PATH)
                If NativeMethods.SHGetPathFromIDList(savedItemId, pathBuffer) Then
                    Return EmptyToNothing(pathBuffer.ToString())
                End If
                Return Nothing
            Catch ex As Exception
                Return Nothing
            Finally
                If savedItemId <> IntPtr.Zero Then Marshal.FreeCoTaskMem(savedItemId)
            End Try
        End Function

        ''' <summary>
        ''' Returns <paramref name="reportedPath"/> if it exists. If not, tries (1) the location
        ''' saved inside the shortcut and (2) the same path under the 32-bit Program Files
        ''' folders, returning the first that exists. If none exist (the target really is
        ''' gone), returns <paramref name="reportedPath"/> unchanged, so it shows as missing.
        ''' </summary>
        Private Shared Function FindRealPath(reportedPath As String, savedLocation As String) As String
            If reportedPath Is Nothing Then Return savedLocation ' e.g. only an ID list was stored
            If PathExists(reportedPath) Then Return reportedPath

            If savedLocation IsNot Nothing AndAlso PathExists(savedLocation) Then Return savedLocation

            Dim programFiles32BitPath As String = SwapTo32BitProgramFiles(reportedPath)
            If programFiles32BitPath IsNot Nothing AndAlso PathExists(programFiles32BitPath) Then Return programFiles32BitPath

            Return reportedPath
        End Function

        ''' <summary>
        ''' "C:\Program Files\App\x.exe" -> "C:\Program Files (x86)\App\x.exe" (and the same for the
        ''' Common Files folder). Nothing if the path isn't under the 64-bit folders, or on
        ''' 32-bit Windows, where there are no separate folders.
        ''' </summary>
        Private Shared Function SwapTo32BitProgramFiles(originalPath As String) As String
            Dim folderPairs As String()() = {
                New String() {Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                              Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)},
                New String() {Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                              Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86)}
            }
            Const NativeFolderIndex As Integer = 0
            Const ThirtyTwoBitFolderIndex As Integer = 1

            For Each folderPair As String() In folderPairs
                Dim nativeFolder As String = folderPair(NativeFolderIndex).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
                Dim thirtyTwoBitFolder As String = folderPair(ThirtyTwoBitFolderIndex).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
                Dim foldersDiffer As Boolean = Not String.Equals(nativeFolder, thirtyTwoBitFolder, StringComparison.OrdinalIgnoreCase)
                If foldersDiffer AndAlso originalPath.StartsWith(nativeFolder, StringComparison.OrdinalIgnoreCase) Then
                    Return thirtyTwoBitFolder & originalPath.Substring(nativeFolder.Length)
                End If
            Next
            Return Nothing
        End Function

        Private Shared Function PathExists(candidatePath As String) As Boolean
            Try
                Return File.Exists(candidatePath) OrElse Directory.Exists(candidatePath)
            Catch ex As Exception
                Return False
            End Try
        End Function

        Private Shared Function EmptyToNothing(text As String) As String
            If String.IsNullOrEmpty(text) Then
                Return Nothing
            End If
            Return text
        End Function

        ''' <summary>Creates a new, empty ShellLink COM object.</summary>
        Private Shared Function CreateShellLinkObject() As IShellLinkW
            Dim shellLinkType As Type = Type.GetTypeFromCLSID(ShellLinkClassId)
            Return DirectCast(Activator.CreateInstance(shellLinkType), IShellLinkW)
        End Function

    End Class

End Namespace
