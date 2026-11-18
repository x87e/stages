' ============================================================================
'  ShellContextMenu.vb
'  Shows Explorer's real right-click menu (Open, Open with, Open file
'  location, Send to, Properties, 7-Zip, etc.) for an item on a Stage, with
'  Stages' own commands added at the top (e.g. "Remove from Stage").
'
'  Explorer's own "Delete" and "Rename" are taken out of the menu:
'    * Delete - "Remove from Stage" is the one clear way to take an item off a
'      Stage (and sends the shortcut to the Recycle Bin);
'    * Rename - only works inside an Explorer window, so it did nothing here.
'
'  Rebuilt around SHParseDisplayName + SHBindToParent (works for drive roots
'  too). Every failure is caught by the caller and logged, never fatal.
'
'  How "Send to" / "Open with" sub-menus work: Explorer fills those in lazily
'  as they open and needs a handful of window messages (WM_INITMENUPOPUP...)
'  forwarded to it while the menu is showing. The Stage window's message hook
'  calls TryHandleMenuMessage() for that.
' ============================================================================
Imports System.Runtime.InteropServices

Namespace Native

    Friend NotInheritable Class ShellContextMenu

        ''' <summary>Returned by Show when none of the caller's extra commands was chosen.</summary>
        Public Const NoExtraCommandChosen As Integer = -1

        ''' <summary>Canonical Explorer verbs removed from the menu (see header comment).</summary>
        Private Shared ReadOnly HiddenExplorerVerbs As String() = {"delete", "rename"}
        ''' <summary>Buffer size (characters) for reading a menu item's verb.</summary>
        Private Const VerbBufferCharacters As Integer = 256
        Private Const BytesPerUnicodeCharacter As Integer = 2

        ' While a menu is open, these hold the richer menu interfaces so that
        ' TryHandleMenuMessage can forward messages to them. Nothing otherwise.
        Private _openMenuVersion2 As IContextMenu2
        Private _openMenuVersion3 As IContextMenu3

        ''' <summary>
        ''' Shows the Explorer context menu for one item, with <paramref name="extraCommands"/>
        ''' listed first. Runs the chosen Explorer command itself; if one of the extra
        ''' commands is chosen, returns its index so the caller can act on it.
        ''' Blocks until the menu closes (TrackPopupMenuEx runs its own message loop).
        ''' </summary>
        ''' <param name="itemPath">Full path of the file, folder or shortcut.</param>
        ''' <param name="ownerWindow">Window that owns the menu (the Stage).</param>
        ''' <param name="screenX">Menu position, physical screen pixels.</param>
        ''' <param name="screenY">Menu position, physical screen pixels.</param>
        ''' <param name="extraCommands">Menu texts for the caller's own commands.</param>
        ''' <returns>Index into <paramref name="extraCommands"/>, or NoExtraCommandChosen.</returns>
        Public Function Show(itemPath As String, ownerWindow As IntPtr, screenX As Integer, screenY As Integer,
                             extraCommands As IList(Of String)) As Integer
            ' Everything that needs releasing is declared up-front so Finally can clean it up.
            Dim fullItemIdList As IntPtr = IntPtr.Zero
            Dim parentFolderPointer As IntPtr = IntPtr.Zero
            Dim contextMenuPointer As IntPtr = IntPtr.Zero
            Dim parentFolder As IShellFolder = Nothing
            Dim contextMenu As IContextMenu = Nothing
            Dim popupMenu As IntPtr = IntPtr.Zero
            Dim chosenExtraCommand As Integer = NoExtraCommandChosen

            Try
                ' 1. Path -> shell item id list (PIDL)
                Dim ignoredAttributes As UInteger = 0UI
                ThrowIfFailed(NativeMethods.SHParseDisplayName(itemPath, IntPtr.Zero, fullItemIdList, 0UI, ignoredAttributes),
                              "SHParseDisplayName")

                ' 2. PIDL -> parent folder object + the item's id relative to that folder
                Dim childItemId As IntPtr = IntPtr.Zero
                Dim shellFolderInterfaceId As Guid = NativeMethods.IID_IShellFolder
                ThrowIfFailed(NativeMethods.SHBindToParent(fullItemIdList, shellFolderInterfaceId, parentFolderPointer, childItemId),
                              "SHBindToParent")
                parentFolder = DirectCast(Marshal.GetObjectForIUnknown(parentFolderPointer), IShellFolder)

                ' 3. Ask the folder for the item's context menu object
                Dim contextMenuInterfaceId As Guid = NativeMethods.IID_IContextMenu
                Const SingleItem As UInteger = 1UI
                ThrowIfFailed(parentFolder.GetUIObjectOf(ownerWindow, SingleItem, New IntPtr() {childItemId},
                                                         contextMenuInterfaceId, IntPtr.Zero, contextMenuPointer),
                              "IShellFolder.GetUIObjectOf")
                contextMenu = DirectCast(Marshal.GetObjectForIUnknown(contextMenuPointer), IContextMenu)

                ' 4. Build the menu: our own commands first, then a separator...
                popupMenu = NativeMethods.CreatePopupMenu()
                For commandIndex As Integer = 0 To extraCommands.Count - 1
                    Dim commandId As UInteger = NativeMethods.CUSTOM_COMMAND_FIRST_ID + CUInt(commandIndex)
                    NativeMethods.AppendMenu(popupMenu, CUInt(NativeMethods.MF_STRING), New UIntPtr(commandId), extraCommands(commandIndex))
                Next
                If extraCommands.Count > 0 Then
                    NativeMethods.AppendMenu(popupMenu, CUInt(NativeMethods.MF_SEPARATOR), UIntPtr.Zero, Nothing)
                End If

                ' ...then let Explorer add its items after ours.
                Dim queryFlags As UInteger = NativeMethods.CMF_NORMAL Or NativeMethods.CMF_EXPLORE
                If IsShiftHeld() Then
                    queryFlags = queryFlags Or NativeMethods.CMF_EXTENDEDVERBS
                End If
                Dim explorerItemsPosition As UInteger = CUInt(NativeMethods.GetMenuItemCount(popupMenu))
                ThrowIfFailed(contextMenu.QueryContextMenu(popupMenu, explorerItemsPosition,
                                                           NativeMethods.CONTEXT_MENU_FIRST_COMMAND_ID,
                                                           NativeMethods.CONTEXT_MENU_LAST_COMMAND_ID, queryFlags),
                              "IContextMenu.QueryContextMenu")

                ' 5. Take out Explorer's Delete/Rename and tidy up any doubled separators
                RemoveHiddenVerbs(contextMenu, popupMenu)
                RemoveRedundantSeparators(popupMenu)

                ' 6. Grab the newer interfaces (if supported) so sub-menus work - see header comment
                _openMenuVersion2 = TryCast(contextMenu, IContextMenu2)
                _openMenuVersion3 = TryCast(contextMenu, IContextMenu3)

                ' 7. Show it. Making our window foreground first is required, otherwise
                '    the menu won't close when the user clicks somewhere else.
                NativeMethods.SetForegroundWindow(ownerWindow)
                Dim chosenCommandId As Integer = NativeMethods.TrackPopupMenuEx(
                    popupMenu, NativeMethods.TPM_RETURNCMD Or NativeMethods.TPM_RIGHTBUTTON,
                    screenX, screenY, ownerWindow, IntPtr.Zero)

                ' 8. Act on the choice (0 means the menu was dismissed)
                If chosenCommandId >= CInt(NativeMethods.CUSTOM_COMMAND_FIRST_ID) Then
                    chosenExtraCommand = chosenCommandId - CInt(NativeMethods.CUSTOM_COMMAND_FIRST_ID)
                ElseIf chosenCommandId >= CInt(NativeMethods.CONTEXT_MENU_FIRST_COMMAND_ID) Then
                    Dim commandOffset As Integer = chosenCommandId - CInt(NativeMethods.CONTEXT_MENU_FIRST_COMMAND_ID)
                    InvokeCommand(contextMenu, commandOffset, ownerWindow, GetContainingFolder(itemPath), screenX, screenY)
                End If
            Finally
                ' Release in reverse order of acquisition. Each step is guarded so a
                ' partial failure above never leaks what WAS acquired.
                _openMenuVersion2 = Nothing
                _openMenuVersion3 = Nothing

                If popupMenu <> IntPtr.Zero Then NativeMethods.DestroyMenu(popupMenu)
                If contextMenu IsNot Nothing Then Marshal.ReleaseComObject(contextMenu)
                If contextMenuPointer <> IntPtr.Zero Then Marshal.Release(contextMenuPointer)
                If parentFolder IsNot Nothing Then Marshal.ReleaseComObject(parentFolder)
                If parentFolderPointer <> IntPtr.Zero Then Marshal.Release(parentFolderPointer)
                If fullItemIdList <> IntPtr.Zero Then Marshal.FreeCoTaskMem(fullItemIdList)
            End Try

            Return chosenExtraCommand
        End Function

        ''' <summary>
        ''' Forwards menu-related window messages to Explorer while a shell menu is open.
        ''' Call this first thing from the owner window's message hook.
        ''' </summary>
        ''' <returns>True if Explorer handled the message; <paramref name="result"/> then holds the value to return.</returns>
        Public Function TryHandleMenuMessage(message As Integer, wParam As IntPtr, lParam As IntPtr,
                                             ByRef result As IntPtr) As Boolean
            result = IntPtr.Zero

            ' Only these four messages matter to the shell menu.
            Select Case message
                Case NativeMethods.WM_INITMENUPOPUP, NativeMethods.WM_DRAWITEM,
                     NativeMethods.WM_MEASUREITEM, NativeMethods.WM_MENUCHAR
                    ' handled below
                Case Else
                    Return False
            End Select

            ' Prefer version 3, which can also return a result value.
            If _openMenuVersion3 IsNot Nothing Then
                Dim handlerResult As IntPtr = IntPtr.Zero
                If _openMenuVersion3.HandleMenuMsg2(CUInt(message), wParam, lParam, handlerResult) = NativeMethods.S_OK Then
                    result = handlerResult
                    Return True
                End If
                Return False
            End If

            ' Version 2 can't handle WM_MENUCHAR.
            If _openMenuVersion2 IsNot Nothing AndAlso message <> NativeMethods.WM_MENUCHAR Then
                If _openMenuVersion2.HandleMenuMsg(CUInt(message), wParam, lParam) = NativeMethods.S_OK Then
                    ' Windows expects TRUE back for handled draw/measure messages.
                    Const MessageHandledTrue As Integer = 1
                    result = New IntPtr(MessageHandledTrue)
                    Return True
                End If
            End If

            Return False
        End Function

        ''' <summary>Deletes Explorer's items whose verb is in HiddenExplorerVerbs.</summary>
        Private Shared Sub RemoveHiddenVerbs(contextMenu As IContextMenu, popupMenu As IntPtr)
            ' Walk backwards so deleting an item doesn't shift the ones still to check.
            For position As Integer = NativeMethods.GetMenuItemCount(popupMenu) - 1 To 0 Step -1
                Dim commandId As Integer = NativeMethods.GetMenuItemID(popupMenu, position)
                Dim isExplorerCommand As Boolean =
                    commandId >= CInt(NativeMethods.CONTEXT_MENU_FIRST_COMMAND_ID) AndAlso
                    commandId <= CInt(NativeMethods.CONTEXT_MENU_LAST_COMMAND_ID)
                If Not isExplorerCommand Then
                    Continue For ' our own item, a separator or a sub-menu
                End If

                Dim verb As String = GetVerb(contextMenu, commandId - CInt(NativeMethods.CONTEXT_MENU_FIRST_COMMAND_ID))
                If verb IsNot Nothing AndAlso HiddenExplorerVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase) Then
                    NativeMethods.DeleteMenu(popupMenu, CUInt(position), NativeMethods.MF_BYPOSITION)
                End If
            Next
        End Sub

        ''' <summary>The canonical verb ("open", "delete"...) of an Explorer command, or Nothing.</summary>
        Private Shared Function GetVerb(contextMenu As IContextMenu, commandOffset As Integer) As String
            Dim verbBuffer As IntPtr = Marshal.AllocCoTaskMem(VerbBufferCharacters * BytesPerUnicodeCharacter)
            Try
                ' Start with an empty string - some handlers report success without writing anything.
                Marshal.WriteInt16(verbBuffer, 0S)
                Dim hresult As Integer = contextMenu.GetCommandString(New UIntPtr(CUInt(commandOffset)), NativeMethods.GCS_VERBW,
                                                                      IntPtr.Zero, verbBuffer, VerbBufferCharacters)
                If hresult <> NativeMethods.S_OK Then
                    Return Nothing
                End If
                Return Marshal.PtrToStringUni(verbBuffer)
            Catch ex As Exception
                Return Nothing ' a misbehaving shell extension - just leave its item alone
            Finally
                Marshal.FreeCoTaskMem(verbBuffer)
            End Try
        End Function

        ''' <summary>Removes separators at the top/bottom and any that follow another separator.</summary>
        Private Shared Sub RemoveRedundantSeparators(popupMenu As IntPtr)
            Dim previousWasSeparator As Boolean = True ' so a separator at the very top is removed
            Dim position As Integer = 0
            While position < NativeMethods.GetMenuItemCount(popupMenu)
                Dim isSeparator As Boolean = IsSeparatorAt(popupMenu, position)
                If isSeparator AndAlso previousWasSeparator Then
                    NativeMethods.DeleteMenu(popupMenu, CUInt(position), NativeMethods.MF_BYPOSITION)
                    ' don't advance: the next item has moved into this position
                Else
                    previousWasSeparator = isSeparator
                    position += 1
                End If
            End While

            Dim lastPosition As Integer = NativeMethods.GetMenuItemCount(popupMenu) - 1
            If lastPosition >= 0 AndAlso IsSeparatorAt(popupMenu, lastPosition) Then
                NativeMethods.DeleteMenu(popupMenu, CUInt(lastPosition), NativeMethods.MF_BYPOSITION)
            End If
        End Sub

        Private Shared Function IsSeparatorAt(popupMenu As IntPtr, position As Integer) As Boolean
            Dim state As Integer = NativeMethods.GetMenuState(popupMenu, CUInt(position), NativeMethods.MF_BYPOSITION)
            If state = NativeMethods.MENU_LOOKUP_FAILED Then
                Return False
            End If
            ' For sub-menus the high bits hold an item count, so rule those out first.
            If (state And NativeMethods.MF_POPUP) <> 0 Then
                Return False
            End If
            Return (state And NativeMethods.MF_SEPARATOR) <> 0
        End Function

        ''' <summary>Runs the Explorer command the user picked.</summary>
        Private Shared Sub InvokeCommand(contextMenu As IContextMenu, commandOffset As Integer, ownerWindow As IntPtr,
                                         workingFolder As String, screenX As Integer, screenY As Integer)
            Dim invokeInfo As New NativeMethods.CMINVOKECOMMANDINFOEX()
            invokeInfo.cbSize = Marshal.SizeOf(GetType(NativeMethods.CMINVOKECOMMANDINFOEX))
            invokeInfo.fMask = NativeMethods.CMIC_MASK_UNICODE Or NativeMethods.CMIC_MASK_PTINVOKE
            If IsShiftHeld() Then invokeInfo.fMask = invokeInfo.fMask Or NativeMethods.CMIC_MASK_SHIFT_DOWN
            If IsControlHeld() Then invokeInfo.fMask = invokeInfo.fMask Or NativeMethods.CMIC_MASK_CONTROL_DOWN
            invokeInfo.hwnd = ownerWindow
            ' The shell accepts a command OFFSET (not a string) by passing it as the verb "pointer".
            invokeInfo.lpVerb = New IntPtr(commandOffset)
            invokeInfo.lpVerbW = New IntPtr(commandOffset)
            invokeInfo.lpDirectory = workingFolder
            invokeInfo.lpDirectoryW = workingFolder
            invokeInfo.nShow = NativeMethods.SW_SHOWNORMAL
            invokeInfo.ptInvoke = New NativeMethods.NativePoint With {.X = screenX, .Y = screenY}

            Dim invokeResult As Integer = contextMenu.InvokeCommand(invokeInfo)
            If invokeResult < 0 Then
                ' A negative HRESULT is a failure. Not fatal (e.g. the user cancelled a dialog) - just note it.
                Services.Logger.Warn(String.Format("Shell menu command {0} returned HRESULT 0x{1:X8}", commandOffset, invokeResult))
            End If
        End Sub

        ''' <summary>Folder containing the item (Nothing for a drive root).</summary>
        Private Shared Function GetContainingFolder(itemPath As String) As String
            Try
                Return System.IO.Path.GetDirectoryName(itemPath)
            Catch ex As ArgumentException
                Return Nothing
            End Try
        End Function

        ''' <summary>Turns a failed HRESULT into an exception carrying a readable step name.</summary>
        Private Shared Sub ThrowIfFailed(hresult As Integer, stepName As String)
            If hresult < 0 Then
                Throw New COMException(stepName & " failed", hresult)
            End If
        End Sub

        Private Shared Function IsShiftHeld() As Boolean
            Return (Keyboard.Modifiers And ModifierKeys.Shift) = ModifierKeys.Shift
        End Function

        Private Shared Function IsControlHeld() As Boolean
            Return (Keyboard.Modifiers And ModifierKeys.Control) = ModifierKeys.Control
        End Function

    End Class

End Namespace
