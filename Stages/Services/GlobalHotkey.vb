' ============================================================================
'  GlobalHotkey.vb
'  The system-wide keyboard shortcut that hides / shows every Stage
'  (Settings > General > "Hide / show all Stages"). Default: Ctrl+Alt+Shift+H.
'
'  Uses Windows' RegisterHotKey: Windows watches for the key combination in
'  every program and sends our hidden window a WM_HOTKEY message. Only one
'  program can own a combination, so registering can fail if another program
'  already uses it - the Settings window then says so.
'
'  Usage (UI thread only):
'      GlobalHotkey.Instance.ApplyFromSettings()    ' at startup / after a change
'      GlobalHotkey.Instance.Suspend()              ' while the user is typing a new one
' ============================================================================
Imports Stages.Model
Imports Stages.Native
Imports WinForms = System.Windows.Forms

Namespace Services

    Public NotInheritable Class GlobalHotkey
        Implements IDisposable

        ''' <summary>Our own number for the one hotkey we register (any number unique within Stages).</summary>
        Private Const ToggleStagesHotkeyId As Integer = 1

        Private Shared _instance As GlobalHotkey

        Private ReadOnly _messageWindow As HotkeyMessageWindow
        Private _isRegistered As Boolean
        Private _isDisposed As Boolean

        ''' <summary>The single shared instance, created on first use (on the UI thread).</summary>
        Public Shared ReadOnly Property Instance As GlobalHotkey
            Get
                If _instance Is Nothing Then
                    _instance = New GlobalHotkey()
                End If
                Return _instance
            End Get
        End Property

        Private Sub New()
            _messageWindow = New HotkeyMessageWindow(AddressOf OnHotkeyPressed)
            _messageWindow.CreateHandle(New WinForms.CreateParams())
        End Sub

        ''' <summary>True while Windows is delivering the shortcut to Stages.</summary>
        Public ReadOnly Property IsRegistered As Boolean
            Get
                Return _isRegistered
            End Get
        End Property

        ''' <summary>
        ''' Registers the shortcut from the current settings (or none, if it's switched off).
        ''' Returns False if Windows refused - almost always because another program already
        ''' uses that combination.
        ''' </summary>
        Public Function ApplyFromSettings() As Boolean
            Unregister()

            Dim currentSettings As AppSettings = AppSettingsStore.Current
            If currentSettings.ToggleStagesHotkeyKey = Key.None Then
                Return True ' switched off: nothing to register, nothing failed
            End If

            Dim nativeModifiers As UInteger = ToNativeModifiers(currentSettings.ToggleStagesHotkeyModifiers) Or NativeMethods.MOD_NOREPEAT
            Dim virtualKey As UInteger = CUInt(KeyInterop.VirtualKeyFromKey(currentSettings.ToggleStagesHotkeyKey))

            _isRegistered = NativeMethods.RegisterHotKey(_messageWindow.Handle, ToggleStagesHotkeyId, nativeModifiers, virtualKey)
            Dim shortcutText As String = DescribeShortcut(currentSettings.ToggleStagesHotkeyModifiers, currentSettings.ToggleStagesHotkeyKey)
            If _isRegistered Then
                Logger.Info("Hide/show shortcut registered: " & shortcutText)
            Else
                Logger.Warn("Hide/show shortcut " & shortcutText & " is already used by another program")
            End If
            Return _isRegistered
        End Function

        ''' <summary>
        ''' Stops listening, e.g. while the user records a new shortcut in Settings (otherwise
        ''' pressing the current one would hide the Stages instead of being recorded).
        ''' </summary>
        Public Sub Suspend()
            Unregister()
        End Sub

        Private Sub Unregister()
            If _isRegistered Then
                NativeMethods.UnregisterHotKey(_messageWindow.Handle, ToggleStagesHotkeyId)
                _isRegistered = False
            End If
        End Sub

        ''' <summary>The shortcut was pressed anywhere in Windows: hide the Stages, or show them again.</summary>
        Private Sub OnHotkeyPressed()
            StageManager.Instance.SetStagesHidden(Not StageManager.Instance.AreStagesHidden)
        End Sub

        ''' <summary>
        ''' Readable text for a shortcut, e.g. "Ctrl + Alt + Shift + H". Empty for "none".
        ''' Shared with the Settings window, which shows the same text.
        ''' </summary>
        Public Shared Function DescribeShortcut(modifiers As ModifierKeys, mainKey As Key) As String
            Const Separator As String = " + "
            Dim parts As New List(Of String)()
            If (modifiers And ModifierKeys.Control) = ModifierKeys.Control Then parts.Add("Ctrl")
            If (modifiers And ModifierKeys.Alt) = ModifierKeys.Alt Then parts.Add("Alt")
            If (modifiers And ModifierKeys.Shift) = ModifierKeys.Shift Then parts.Add("Shift")
            If (modifiers And ModifierKeys.Windows) = ModifierKeys.Windows Then parts.Add("Win")
            If mainKey <> Key.None Then parts.Add(DescribeKey(mainKey))
            Return String.Join(Separator, parts)
        End Function

        ''' <summary>"D7" -> "7", "NumPad7" -> "Num 7", anything else as WPF names it ("H", "F5", "Space").</summary>
        Private Shared Function DescribeKey(mainKey As Key) As String
            If mainKey >= Key.D0 AndAlso mainKey <= Key.D9 Then
                Return CInt(mainKey - Key.D0).ToString()
            End If
            If mainKey >= Key.NumPad0 AndAlso mainKey <= Key.NumPad9 Then
                Return "Num " & CInt(mainKey - Key.NumPad0).ToString()
            End If
            Return mainKey.ToString()
        End Function

        ''' <summary>WPF's modifier flags -> RegisterHotKey's MOD_* flags.</summary>
        Private Shared Function ToNativeModifiers(modifiers As ModifierKeys) As UInteger
            Dim nativeModifiers As UInteger = 0UI
            If (modifiers And ModifierKeys.Alt) = ModifierKeys.Alt Then nativeModifiers = nativeModifiers Or NativeMethods.MOD_ALT
            If (modifiers And ModifierKeys.Control) = ModifierKeys.Control Then nativeModifiers = nativeModifiers Or NativeMethods.MOD_CONTROL
            If (modifiers And ModifierKeys.Shift) = ModifierKeys.Shift Then nativeModifiers = nativeModifiers Or NativeMethods.MOD_SHIFT
            If (modifiers And ModifierKeys.Windows) = ModifierKeys.Windows Then nativeModifiers = nativeModifiers Or NativeMethods.MOD_WIN
            Return nativeModifiers
        End Function

        ''' <summary>Releases the shortcut and the hidden window (at exit).</summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            If _isDisposed Then Return
            _isDisposed = True
            Unregister()
            _messageWindow.DestroyHandle()
        End Sub

        ''' <summary>The hidden window Windows sends WM_HOTKEY to.</summary>
        Private NotInheritable Class HotkeyMessageWindow
            Inherits WinForms.NativeWindow

            Private ReadOnly _onHotkey As Action

            Public Sub New(onHotkey As Action)
                _onHotkey = onHotkey
            End Sub

            Protected Overrides Sub WndProc(ByRef m As WinForms.Message)
                If m.Msg = NativeMethods.WM_HOTKEY AndAlso m.WParam.ToInt32() = ToggleStagesHotkeyId Then
                    _onHotkey.Invoke()
                    Return
                End If
                MyBase.WndProc(m)
            End Sub
        End Class

    End Class

End Namespace
