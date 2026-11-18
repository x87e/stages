' ============================================================================
'  ShortcutIcons.vb
'  "Change icon..." and "Reset icon" for an item on a Stage.
'
'  The icon is picked with Windows' own Change Icon dialog (the one in a
'  shortcut's Properties), so it can come from any .ico, .exe or .dll file,
'  and it is saved INTO the shortcut itself:
'      .lnk  -> the shortcut's icon location (IShellLink.SetIconLocation)
'      .url  -> the IconFile / IconIndex lines of the internet shortcut
'  That means the icon also shows anywhere else the shortcut is used, travels
'  with the Stage folder, and is exactly what Explorer would show.
'  ShellIcons already prefers a shortcut's custom icon, so nothing else in
'  Stages needs to know about it.
'
'  Only shortcuts (.lnk / .url) can have a custom icon; Stages never changes
'  the user's real files or folders.
'  Call on the UI thread (the dialog and the shortcut COM object need an STA thread).
' ============================================================================
Imports System.IO
Imports System.Text
Imports Stages.Model
Imports Stages.Native

Namespace Services

    Public NotInheritable Class ShortcutIcons

        ''' <summary>An icon chosen in the dialog: the file it's in, and which icon in that file.</summary>
        Public NotInheritable Class IconChoice
            ''' <summary>.ico, .exe or .dll file (may contain %variables% such as %SystemRoot%).</summary>
            Public Property IconPath As String
            ''' <summary>Position of the icon inside IconPath (or a negative resource id).</summary>
            Public Property IconIndex As Integer
        End Class

        ''' <summary>The file the dialog opens on when the shortcut has no icon of its own: Windows' standard icons.</summary>
        Private Const WindowsStandardIconLibrary As String = "%SystemRoot%\System32\shell32.dll"
        Private Const FirstIconInFile As Integer = 0
        ''' <summary>Files the dialog can show icons from - if the shortcut points at one, it opens on that.</summary>
        Private Shared ReadOnly IconContainingExtensions As String() = {".ico", ".exe", ".dll", ".icl"}
        Private Const InternetShortcutExtension As String = ".url"
        ''' <summary>The dialog's result for Cancel.</summary>
        Private Const DialogCancelled As Integer = 0

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Shows Windows' Change Icon dialog for a shortcut. It opens on the shortcut's current
        ''' custom icon, else on the program it points to (if that has icons), else on Windows'
        ''' standard icons. Returns the choice, or Nothing if the user cancelled.
        ''' </summary>
        Public Shared Function AskForIcon(ownerWindow As IntPtr, item As StageItem) As IconChoice
            Dim startFile As String = WindowsStandardIconLibrary
            Dim startIndex As Integer = FirstIconInFile

            If item.HasCustomIcon Then
                startFile = item.CustomIconPath
                startIndex = item.CustomIconIndex
            ElseIf TargetContainsIcons(item.TargetPath) Then
                startFile = item.TargetPath
            End If

            ' The dialog reads the starting file from this buffer and writes the chosen file back into it.
            Dim iconPathBuffer As New StringBuilder(startFile, NativeMethods.MAX_PATH)
            Dim chosenIndex As Integer = startIndex
            If ShowPickIconDialog(ownerWindow, iconPathBuffer, chosenIndex) = DialogCancelled Then
                Return Nothing
            End If

            Return New IconChoice With {.IconPath = iconPathBuffer.ToString(), .IconIndex = chosenIndex}
        End Function

        ''' <summary>
        ''' Saves <paramref name="choice"/> into the shortcut, or removes its custom icon when
        ''' <paramref name="choice"/> is Nothing. Returns False (already logged) on failure.
        ''' </summary>
        Public Shared Function SetIcon(item As StageItem, choice As IconChoice) As Boolean
            If Not item.CanHaveCustomIcon Then Return False

            Dim iconPath As String = Nothing
            Dim iconIndex As Integer = FirstIconInFile
            If choice IsNot Nothing Then
                iconPath = choice.IconPath
                iconIndex = choice.IconIndex
            End If

            Dim isInternetShortcut As Boolean =
                String.Equals(Path.GetExtension(item.FullPath), InternetShortcutExtension, StringComparison.OrdinalIgnoreCase)
            Dim saved As Boolean
            If isInternetShortcut Then
                saved = ShellLinks.TrySetInternetShortcutIcon(item.FullPath, iconPath, iconIndex)
            Else
                saved = ShellLinks.TrySetShortcutIcon(item.FullPath, iconPath, iconIndex)
            End If

            If saved Then
                If choice Is Nothing Then
                    Logger.Info("Reset the icon of " & item.FullPath)
                Else
                    Logger.Info("Set the icon of " & item.FullPath & " to " & iconPath & "," & iconIndex.ToString())
                End If
            End If
            Return saved
        End Function

        ''' <summary>True if the shortcut points at an existing file that can hold icons (a program, .dll or .ico).</summary>
        Private Shared Function TargetContainsIcons(targetPath As String) As Boolean
            If String.IsNullOrEmpty(targetPath) OrElse Not File.Exists(targetPath) Then Return False
            Return IconContainingExtensions.Contains(Path.GetExtension(targetPath), StringComparer.OrdinalIgnoreCase)
        End Function

        ''' <summary>Calls PickIconDlg by name, falling back to its old export number on very old Windows.</summary>
        Private Shared Function ShowPickIconDialog(ownerWindow As IntPtr, iconPathBuffer As StringBuilder,
                                                   ByRef chosenIndex As Integer) As Integer
            Try
                Return NativeMethods.PickIconDlg(ownerWindow, iconPathBuffer, CUInt(iconPathBuffer.Capacity), chosenIndex)
            Catch ex As EntryPointNotFoundException
                Return NativeMethods.PickIconDlgByNumber(ownerWindow, iconPathBuffer, CUInt(iconPathBuffer.Capacity), chosenIndex)
            End Try
        End Function

    End Class

End Namespace
