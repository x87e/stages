' ============================================================================
'  ShellFileTypes.vb
'  The name Windows gives a kind of file - "Microsoft Word Document",
'  "File folder", "Internet Shortcut" - as shown in Explorer's Type column.
'  Used for the Details view and for Sort by > Type.
'
'  Asked by extension only (SHGFI_USEFILEATTRIBUTES), which is fast and never
'  touches the file, and remembered per extension.
'  Call on an STA thread (the shell worker thread).
' ============================================================================
Imports System.Collections.Concurrent
Imports System.Runtime.InteropServices

Namespace Native

    Friend NotInheritable Class ShellFileTypes

        ''' <summary>A made-up name with the extension, so the shell can answer without a real file.</summary>
        Private Const StandInFileName As String = "file"
        ''' <summary>Cache key for folders (no extension can clash with it: it contains a character names can't).</summary>
        Private Const FolderCacheKey As String = "<folder>"

        ''' <summary>Extension (or FolderCacheKey) -> Windows' type name.</summary>
        Private Shared ReadOnly _typeNames As New ConcurrentDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Windows' type name for files with this extension (".docx"), or for a folder when
        ''' <paramref name="isFolder"/> is True. Never Nothing (falls back to e.g. "DOCX File").
        ''' </summary>
        Public Shared Function GetTypeName(extension As String, isFolder As Boolean) As String
            Dim cacheKey As String = If(isFolder, FolderCacheKey, If(extension, String.Empty))
            Dim knownName As String = Nothing
            If _typeNames.TryGetValue(cacheKey, knownName) Then Return knownName

            Dim attributes As UInteger = If(isFolder, NativeMethods.FILE_ATTRIBUTE_DIRECTORY, NativeMethods.FILE_ATTRIBUTE_NORMAL)
            Dim shellFileInfo As New NativeMethods.SHFILEINFO()
            Dim callResult As IntPtr = NativeMethods.SHGetFileInfo(
                StandInFileName & If(isFolder, String.Empty, extension), attributes, shellFileInfo,
                CUInt(Marshal.SizeOf(GetType(NativeMethods.SHFILEINFO))),
                NativeMethods.SHGFI_TYPENAME Or NativeMethods.SHGFI_USEFILEATTRIBUTES)

            Dim typeName As String = If(callResult <> IntPtr.Zero, shellFileInfo.szTypeName, Nothing)
            If String.IsNullOrEmpty(typeName) Then
                typeName = FallbackTypeName(extension, isFolder)
            End If
            _typeNames(cacheKey) = typeName
            Return typeName
        End Function

        ''' <summary>Explorer's own fallback wording: "File folder", or ".xyz" -> "XYZ File", or "File".</summary>
        Private Shared Function FallbackTypeName(extension As String, isFolder As Boolean) As String
            Const FolderTypeName As String = "File folder"
            Const PlainFileTypeName As String = "File"
            Const ExtensionFileFormat As String = "{0} File"
            If isFolder Then Return FolderTypeName
            If String.IsNullOrEmpty(extension) Then Return PlainFileTypeName
            Return String.Format(ExtensionFileFormat, extension.TrimStart("."c).ToUpperInvariant())
        End Function

    End Class

End Namespace
