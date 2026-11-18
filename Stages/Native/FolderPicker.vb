' ============================================================================
'  FolderPicker.vb
'  "Choose a folder" with Windows' modern folder dialog (the same one File
'  Explorer's "Select folder" uses), for New folder Stage... and New folder
'  tab....
'
'  .NET Framework's own FolderBrowserDialog is the old tree-view dialog from
'  Windows 95 days, so this talks to Windows' file dialog (IFileOpenDialog)
'  directly, in "pick folders" mode. If that ever fails, it falls back to the
'  old dialog rather than doing nothing.
' ============================================================================
Imports System.Runtime.InteropServices
Imports WinForms = System.Windows.Forms

Namespace Native

    Friend NotInheritable Class FolderPicker

        ''' <summary>FOS_PICKFOLDERS: choose folders instead of files.</summary>
        Private Const FOS_PICKFOLDERS As UInteger = &H20UI
        ''' <summary>FOS_FORCEFILESYSTEM: only real folders on disk (not "This PC", libraries...).</summary>
        Private Const FOS_FORCEFILESYSTEM As UInteger = &H40UI
        ''' <summary>FOS_PATHMUSTEXIST: the chosen folder must exist.</summary>
        Private Const FOS_PATHMUSTEXIST As UInteger = &H800UI
        ''' <summary>SIGDN_FILESYSPATH: ask an IShellItem for its ordinary file-system path.</summary>
        Private Const SIGDN_FILESYSPATH As UInteger = &H80058000UI
        ''' <summary>HRESULT returned by Show when the user pressed Cancel.</summary>
        Private Const ERROR_CANCELLED_HRESULT As Integer = &H800704C7
        Private Const S_OK As Integer = 0

        ''' <summary>Windows' FileOpenDialog class.</summary>
        Private Shared ReadOnly FileOpenDialogClassId As New Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Asks the user to choose a folder. Returns its full path, or Nothing if cancelled.
        ''' </summary>
        ''' <param name="ownerWindow">Our window the dialog belongs to (it stays on top of it).</param>
        ''' <param name="dialogTitle">Text in the dialog's title bar.</param>
        Public Shared Function ChooseFolder(ownerWindow As IntPtr, dialogTitle As String) As String
            Try
                Return ChooseWithModernDialog(ownerWindow, dialogTitle)
            Catch ex As Exception When TypeOf ex Is COMException OrElse TypeOf ex Is InvalidCastException
                Services.Logger.Warn("The folder dialog couldn't be shown; using the older one instead", ex)
                Return ChooseWithOldDialog(dialogTitle)
            End Try
        End Function

        Private Shared Function ChooseWithModernDialog(ownerWindow As IntPtr, dialogTitle As String) As String
            Dim dialogType As Type = Type.GetTypeFromCLSID(FileOpenDialogClassId)
            Dim dialog As IFileDialog = DirectCast(Activator.CreateInstance(dialogType), IFileDialog)
            Try
                Dim currentOptions As UInteger
                dialog.GetOptions(currentOptions)
                dialog.SetOptions(currentOptions Or FOS_PICKFOLDERS Or FOS_FORCEFILESYSTEM Or FOS_PATHMUSTEXIST)
                dialog.SetTitle(dialogTitle)

                Dim showResult As Integer = dialog.Show(ownerWindow)
                If showResult = ERROR_CANCELLED_HRESULT Then Return Nothing
                If showResult <> S_OK Then Marshal.ThrowExceptionForHR(showResult)

                Dim chosenItem As IShellItem = Nothing
                dialog.GetResult(chosenItem)
                Try
                    Dim pathPointer As IntPtr = IntPtr.Zero
                    chosenItem.GetDisplayName(SIGDN_FILESYSPATH, pathPointer)
                    Try
                        Return Marshal.PtrToStringUni(pathPointer)
                    Finally
                        ' Windows allocated the text; it must be freed with CoTaskMemFree.
                        Marshal.FreeCoTaskMem(pathPointer)
                    End Try
                Finally
                    Marshal.ReleaseComObject(chosenItem)
                End Try
            Finally
                Marshal.ReleaseComObject(dialog)
            End Try
        End Function

        ''' <summary>The old tree-view dialog, used only if the modern one can't be created.</summary>
        Private Shared Function ChooseWithOldDialog(dialogTitle As String) As String
            Using oldDialog As New WinForms.FolderBrowserDialog() With {
                .Description = dialogTitle,
                .ShowNewFolderButton = False
            }
                If oldDialog.ShowDialog() = WinForms.DialogResult.OK Then
                    Return oldDialog.SelectedPath
                End If
            End Using
            Return Nothing
        End Function

#Region "COM interfaces (only the methods used here are called; all are declared, in order)"

        ''' <summary>
        ''' IFileDialog (shobjidl.h). Windows looks methods up by their POSITION, so every
        ''' method must be declared in exactly this order, including the unused ones.
        ''' It inherits IModalWindow, whose only method is Show - declared first.
        ''' </summary>
        <ComImport(), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")>
        Private Interface IFileDialog
            ' IModalWindow
            <PreserveSig()> Function Show(parent As IntPtr) As Integer
            ' IFileDialog
            Sub SetFileTypes(cFileTypes As UInteger, rgFilterSpec As IntPtr)
            Sub SetFileTypeIndex(iFileType As UInteger)
            Sub GetFileTypeIndex(ByRef piFileType As UInteger)
            Sub Advise(pfde As IntPtr, ByRef pdwCookie As UInteger)
            Sub Unadvise(dwCookie As UInteger)
            Sub SetOptions(fos As UInteger)
            Sub GetOptions(ByRef pfos As UInteger)
            Sub SetDefaultFolder(psi As IShellItem)
            Sub SetFolder(psi As IShellItem)
            Sub GetFolder(ByRef ppsi As IShellItem)
            Sub GetCurrentSelection(ByRef ppsi As IShellItem)
            Sub SetFileName(<MarshalAs(UnmanagedType.LPWStr)> pszName As String)
            Sub GetFileName(ByRef pszName As IntPtr)
            Sub SetTitle(<MarshalAs(UnmanagedType.LPWStr)> pszTitle As String)
            Sub SetOkButtonLabel(<MarshalAs(UnmanagedType.LPWStr)> pszText As String)
            Sub SetFileNameLabel(<MarshalAs(UnmanagedType.LPWStr)> pszLabel As String)
            Sub GetResult(ByRef ppsi As IShellItem)
            Sub AddPlace(psi As IShellItem, fdap As Integer)
            Sub SetDefaultExtension(<MarshalAs(UnmanagedType.LPWStr)> pszDefaultExtension As String)
            Sub Close(hr As Integer)
            Sub SetClientGuid(ByRef guid As Guid)
            Sub ClearClientData()
            Sub SetFilter(pFilter As IntPtr)
        End Interface

        ''' <summary>IShellItem (shobjidl.h): one file or folder, as the shell sees it.</summary>
        <ComImport(), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")>
        Private Interface IShellItem
            Sub BindToHandler(pbc As IntPtr, ByRef bhid As Guid, ByRef riid As Guid, ByRef ppv As IntPtr)
            Sub GetParent(ByRef ppsi As IShellItem)
            Sub GetDisplayName(sigdnName As UInteger, ByRef ppszName As IntPtr)
            Sub GetAttributes(sfgaoMask As UInteger, ByRef psfgaoAttribs As UInteger)
            Sub Compare(psi As IShellItem, hint As UInteger, ByRef piOrder As Integer)
        End Interface

#End Region

    End Class

End Namespace
