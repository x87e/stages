' ============================================================================
'  ShellFileDelete.vb
'  Deletes real files and folders the way File Explorer does, for "Delete" on
'  a folder Stage. Uses Windows' own SHFileOperation, so the Recycle Bin, the
'  progress window, "file in use" messages and so on all behave as they do in
'  Explorer.
'
'  Why not .NET's own FileSystem.DeleteFile(..., SendToRecycleBin)? It can't
'  ask Windows to WARN when a file can't be recycled (a network drive, or a
'  drive whose Recycle Bin is switched off): the file would silently be
'  deleted for good. SHFileOperation's FOF_WANTNUKEWARNING flag makes Windows
'  ask first in exactly that case.
'
'  SAFETY: this is the only code in Stages that deletes the user's real
'  files. It is only called from a folder Stage, after the confirmation that
'  the chosen FolderDeleteMode requires (see StageWindow.Tabs.vb).
' ============================================================================
Imports System.Runtime.InteropServices
Imports Stages.Model

Namespace Native

    ''' <summary>How a delete ended.</summary>
    Friend Enum ShellDeleteResult
        ''' <summary>Windows reported success.</summary>
        Completed
        ''' <summary>The user said No or Cancel in one of Windows' own dialogs (some items may already be gone).</summary>
        Cancelled
        ''' <summary>Windows reported an error (it has usually already shown its own message).</summary>
        Failed
    End Enum

    Friend NotInheritable Class ShellFileDelete

        ''' <summary>FO_DELETE: the "delete" operation of SHFileOperation.</summary>
        Private Const FO_DELETE As UInteger = &H3UI
        ''' <summary>FOF_NOCONFIRMATION: don't show Windows' "Are you sure?" question.</summary>
        Private Const FOF_NOCONFIRMATION As UShort = &H10US
        ''' <summary>FOF_ALLOWUNDO: send to the Recycle Bin where possible, instead of deleting for good.</summary>
        Private Const FOF_ALLOWUNDO As UShort = &H40US
        ''' <summary>FOF_WANTNUKEWARNING: ask before deleting for good something that can't be recycled.</summary>
        Private Const FOF_WANTNUKEWARNING As UShort = &H4000US

        Private Const OperationSucceeded As Integer = 0
        ''' <summary>Windows error 1223 (ERROR_CANCELLED), and SHFileOperation's own "cancelled" code.</summary>
        Private Const ErrorCancelled As Integer = 1223
        Private Const ShellCancelledCode As Integer = &H75
        ''' <summary>SHFileOperation's file list is separated, and ended, by this character.</summary>
        Private Const ListSeparator As Char = ControlChars.NullChar
        ''' <summary>A 64-bit program's pointers are this many bytes.</summary>
        Private Const PointerSizeIn64Bit As Integer = 8

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Deletes the given files/folders according to <paramref name="deleteMode"/>.
        ''' Any question Stages itself asks must already have been answered Yes; this only
        ''' shows what Windows shows.
        ''' </summary>
        ''' <param name="ownerWindow">Our window that Windows' dialogs belong to.</param>
        ''' <param name="itemPaths">Full paths. Each must be a full path: SHFileOperation treats a bare name as relative.</param>
        Public Shared Function DeleteItems(ownerWindow As IntPtr, itemPaths As IEnumerable(Of String),
                                           deleteMode As FolderDeleteMode) As ShellDeleteResult
            ' Only full paths are ever passed on - never a relative name or a wildcard.
            Dim fullPaths As New List(Of String)()
            For Each itemPath As String In itemPaths
                Dim isUsable As Boolean = Not String.IsNullOrWhiteSpace(itemPath) AndAlso
                                          IO.Path.IsPathRooted(itemPath) AndAlso
                                          itemPath.IndexOfAny(WildcardCharacters) < 0
                If isUsable Then fullPaths.Add(itemPath)
            Next
            If fullPaths.Count = 0 Then Return ShellDeleteResult.Completed

            ' "path1<NUL>path2<NUL><NUL>": a NUL after each path, and one more to end the list.
            Dim fileList As String = String.Join(ListSeparator, fullPaths) & ListSeparator & ListSeparator

            Dim wasAborted As Boolean
            Dim resultCode As Integer
            If IntPtr.Size = PointerSizeIn64Bit Then
                Dim operation As New FileOperation64 With {
                    .hwnd = ownerWindow, .wFunc = FO_DELETE, .pFrom = fileList, .fFlags = FlagsFor(deleteMode)}
                resultCode = SHFileOperation64(operation)
                wasAborted = operation.fAnyOperationsAborted
            Else
                Dim operation As New FileOperation32 With {
                    .hwnd = ownerWindow, .wFunc = FO_DELETE, .pFrom = fileList, .fFlags = FlagsFor(deleteMode)}
                resultCode = SHFileOperation32(operation)
                wasAborted = operation.fAnyOperationsAborted
            End If

            If resultCode = ErrorCancelled OrElse resultCode = ShellCancelledCode OrElse
               (resultCode = OperationSucceeded AndAlso wasAborted) Then
                Return ShellDeleteResult.Cancelled
            End If
            If resultCode <> OperationSucceeded Then
                Services.Logger.Warn("Windows could not delete " & fullPaths.Count.ToString() &
                                     " item(s) (SHFileOperation code " & resultCode.ToString() & ")")
                Return ShellDeleteResult.Failed
            End If
            Return ShellDeleteResult.Completed
        End Function

        ''' <summary>Characters that would make SHFileOperation match several files; never allowed here.</summary>
        Private Shared ReadOnly WildcardCharacters As Char() = {"*"c, "?"c}

        ''' <summary>The SHFileOperation flags for each delete mode.</summary>
        Private Shared Function FlagsFor(deleteMode As FolderDeleteMode) As UShort
            Select Case deleteMode
                Case FolderDeleteMode.SameAsWindows
                    ' Windows' own question (if the user has it switched on) and Recycle Bin rules.
                    Return FOF_ALLOWUNDO Or FOF_WANTNUKEWARNING
                Case FolderDeleteMode.Permanent
                    ' Stages has already asked; delete for good.
                    Return FOF_NOCONFIRMATION
                Case Else
                    ' Stages has already asked; recycle, and let Windows warn if it can't.
                    Return FOF_ALLOWUNDO Or FOF_NOCONFIRMATION Or FOF_WANTNUKEWARNING
            End Select
        End Function

#Region "SHFileOperation (shell32.dll)"
        ' SHFILEOPSTRUCTW is laid out differently in 32-bit and 64-bit programs: 32-bit
        ' Windows headers pack it with no gaps (Pack 1), 64-bit ones use normal alignment.
        ' Stages is built "AnyCPU", so both layouts are declared and the right one is picked
        ' at run time (see DeleteItems).

        <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
        Private Structure FileOperation64
            Public hwnd As IntPtr
            Public wFunc As UInteger
            <MarshalAs(UnmanagedType.LPWStr)> Public pFrom As String
            <MarshalAs(UnmanagedType.LPWStr)> Public pTo As String
            Public fFlags As UShort
            <MarshalAs(UnmanagedType.Bool)> Public fAnyOperationsAborted As Boolean
            Public hNameMappings As IntPtr
            <MarshalAs(UnmanagedType.LPWStr)> Public lpszProgressTitle As String
        End Structure

        <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode, Pack:=1)>
        Private Structure FileOperation32
            Public hwnd As IntPtr
            Public wFunc As UInteger
            <MarshalAs(UnmanagedType.LPWStr)> Public pFrom As String
            <MarshalAs(UnmanagedType.LPWStr)> Public pTo As String
            Public fFlags As UShort
            <MarshalAs(UnmanagedType.Bool)> Public fAnyOperationsAborted As Boolean
            Public hNameMappings As IntPtr
            <MarshalAs(UnmanagedType.LPWStr)> Public lpszProgressTitle As String
        End Structure

        <DllImport("shell32.dll", EntryPoint:="SHFileOperationW", CharSet:=CharSet.Unicode)>
        Private Shared Function SHFileOperation64(ByRef lpFileOp As FileOperation64) As Integer
        End Function

        <DllImport("shell32.dll", EntryPoint:="SHFileOperationW", CharSet:=CharSet.Unicode)>
        Private Shared Function SHFileOperation32(ByRef lpFileOp As FileOperation32) As Integer
        End Function
#End Region

    End Class

End Namespace
