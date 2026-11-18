' ============================================================================
'  NativePopupMenu.vb
'  A Windows (Win32) right-click menu, built item by item with an action for
'  each entry. Used for the Stage menu and the tray menu.
'
'  Why native rather than WPF / Windows Forms menus? The Explorer menu on an
'  item is always a native menu. Using the same kind everywhere means all three
'  look identical (including Windows 11's rounded style) and follow the light /
'  dark Theme through one switch (DesktopIntegration.SetMenusDark).
'
'  Usage:
'      Using stageMenu As New NativePopupMenu()
'          stageMenu.AddItem("Lock", AddressOf ToggleLocked, isChecked:=True)
'          Dim backgroundMenu As NativePopupMenu = stageMenu.AddSubmenu("Background")
'          backgroundMenu.AddItem("Blur", Sub() SetBackdrop(BackdropStyle.Blur))
'          Dim chosenAction As Action = stageMenu.ShowAndGetChoice(ownerHandle, x, y)
'          If chosenAction IsNot Nothing Then chosenAction()
'      End Using
'
'  The chosen action is RETURNED rather than run, so the caller can run it
'  once the menu has completely finished (e.g. queue it on the dispatcher).
'
'  Don't add owner-drawn items (MF_OWNERDRAW) to these menus. Windows draws a
'  menu containing any owner-drawn item in the old unthemed style, so it loses
'  dark mode. v0.4.1 tried an invisible owner-drawn item to add space under the
'  last entry; it turned the menus light, and Dan chose dark over the spacing.
'  Native menus have no padding setting of their own.
' ============================================================================
Namespace Native

    Friend NotInheritable Class NativePopupMenu
        Implements IDisposable

        ''' <summary>Command ids start at 1: TrackPopupMenuEx returns 0 for "nothing chosen".</summary>
        Private Const FirstCommandId As Integer = 1
        ''' <summary>TrackPopupMenuEx's result when the menu was dismissed without a choice.</summary>
        Private Const NothingChosen As Integer = 0

        ''' <summary>The Windows menu this object builds.</summary>
        Private ReadOnly _menuHandle As IntPtr
        ''' <summary>
        ''' The top-level menu. Sub-menus hand their items to it, so every command id is
        ''' unique across the whole menu and the top-level menu can look up any choice.
        ''' </summary>
        Private ReadOnly _rootMenu As NativePopupMenu
        ''' <summary>Command id -> what to do. Only filled on the top-level menu.</summary>
        Private ReadOnly _actionsByCommandId As Dictionary(Of Integer, Action)
        ''' <summary>Next free command id. Only used on the top-level menu.</summary>
        Private _nextCommandId As Integer = FirstCommandId
        Private _isDisposed As Boolean

        ''' <summary>Creates an empty top-level menu.</summary>
        Public Sub New()
            _menuHandle = NativeMethods.CreatePopupMenu()
            _rootMenu = Me
            _actionsByCommandId = New Dictionary(Of Integer, Action)()
        End Sub

        ''' <summary>Creates an empty sub-menu belonging to <paramref name="rootMenu"/>.</summary>
        Private Sub New(rootMenu As NativePopupMenu)
            _menuHandle = NativeMethods.CreatePopupMenu()
            _rootMenu = rootMenu
        End Sub

        ''' <summary>Adds a clickable entry.</summary>
        ''' <param name="text">The entry's text. An "&amp;" marks the Alt shortcut letter, so write "&amp;&amp;" for a real one.</param>
        ''' <param name="onChosen">What to do if this entry is chosen.</param>
        ''' <param name="isChecked">Show a tick beside it.</param>
        ''' <param name="isDefault">Show it in bold as the default entry (what double-click does).</param>
        ''' <param name="isEnabled">False shows it greyed out and unclickable.</param>
        Public Sub AddItem(text As String, onChosen As Action, Optional isChecked As Boolean = False,
                           Optional isDefault As Boolean = False, Optional isEnabled As Boolean = True)
            Dim commandId As Integer = _rootMenu.RegisterAction(onChosen)
            Dim itemFlags As Integer = NativeMethods.MF_STRING
            If isChecked Then
                itemFlags = itemFlags Or NativeMethods.MF_CHECKED
            End If
            If Not isEnabled Then
                itemFlags = itemFlags Or NativeMethods.MF_GRAYED
            End If

            NativeMethods.AppendMenu(_menuHandle, CUInt(itemFlags), New UIntPtr(CUInt(commandId)), text)
            If isDefault Then
                NativeMethods.SetMenuDefaultItem(_menuHandle, CUInt(commandId), NativeMethods.MENU_ITEM_BY_COMMAND)
            End If
        End Sub

        ''' <summary>Adds a dividing line.</summary>
        Public Sub AddSeparator()
            NativeMethods.AppendMenu(_menuHandle, CUInt(NativeMethods.MF_SEPARATOR), UIntPtr.Zero, Nothing)
        End Sub

        ''' <summary>Adds an entry that opens a sub-menu, and returns that sub-menu so items can be added to it.</summary>
        Public Function AddSubmenu(text As String) As NativePopupMenu
            Dim childMenu As New NativePopupMenu(_rootMenu)
            ' For MF_POPUP entries, the "id" parameter carries the sub-menu's handle instead.
            Dim childMenuHandleAsId As New UIntPtr(CULng(childMenu._menuHandle.ToInt64()))
            NativeMethods.AppendMenu(_menuHandle, CUInt(NativeMethods.MF_STRING Or NativeMethods.MF_POPUP), childMenuHandleAsId, text)
            ' No need to keep track of it for clean-up: destroying a menu also destroys its sub-menus.
            Return childMenu
        End Function

        ''' <summary>
        ''' Shows the menu at a screen position (physical pixels) and waits until it closes.
        ''' Returns the chosen entry's action, or Nothing if the menu was dismissed.
        ''' Only call this on the top-level menu.
        ''' </summary>
        ''' <param name="ownerWindow">A window of ours; it receives the menu's messages.</param>
        Public Function ShowAndGetChoice(ownerWindow As IntPtr, screenX As Integer, screenY As Integer) As Action
            ' The owner must be the foreground window, or the menu won't close when the user
            ' clicks elsewhere (documented TrackPopupMenu behaviour).
            NativeMethods.SetForegroundWindow(ownerWindow)
            Dim chosenCommandId As Integer = NativeMethods.TrackPopupMenuEx(
                _menuHandle, NativeMethods.TPM_RETURNCMD Or NativeMethods.TPM_RIGHTBUTTON,
                screenX, screenY, ownerWindow, IntPtr.Zero)
            ' Also documented (KB135788): post a harmless message afterwards, so the next
            ' menu shown from a background window closes properly too.
            NativeMethods.PostMessage(ownerWindow, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero)

            If chosenCommandId = NothingChosen Then Return Nothing
            Dim chosenAction As Action = Nothing
            _actionsByCommandId.TryGetValue(chosenCommandId, chosenAction)
            Return chosenAction
        End Function

        ''' <summary>Gives an action the next free command id (top-level menu only).</summary>
        Private Function RegisterAction(onChosen As Action) As Integer
            Dim commandId As Integer = _nextCommandId
            _nextCommandId += 1
            _actionsByCommandId(commandId) = onChosen
            Return commandId
        End Function

        ''' <summary>Releases the Windows menu (and with it every sub-menu). Only the top-level menu does this.</summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            If _isDisposed Then Return
            _isDisposed = True
            If _rootMenu Is Me AndAlso _menuHandle <> IntPtr.Zero Then
                NativeMethods.DestroyMenu(_menuHandle)
            End If
        End Sub

    End Class

End Namespace
