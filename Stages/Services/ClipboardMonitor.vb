' ============================================================================
'  ClipboardMonitor.vb
'  Tells Clipboard history Stages when something new has been copied, and
'  hands them what it was (one ClipboardSnapshot, read once for all of them).
'
'  How it works:
'    * A hidden "message-only" window asks Windows for WM_CLIPBOARDUPDATE
'      (AddClipboardFormatListener), sent after every copy in any program.
'    * Programs often put several formats on the clipboard one after another,
'      so the read waits a moment (SettleDelay) after the last update.
'    * Another program may still have the clipboard open; then the read is
'      tried again a few times, a little later each time.
'    * Content a program marks as private is reported as private and never
'      saved. Password managers (KeePass, Bitwarden, 1Password...) mark what
'      they copy this way; Windows' own clipboard history (Win+V) honours the
'      same marks:
'        - "ExcludeClipboardContentFromMonitorProcessing" present
'        - "CanIncludeInClipboardHistory" present and set to 0
'        - "Clipboard Viewer Ignore" present (an older convention)
'    * Stages' own "copy this entry back" is ignored (IgnoreOwnCopy), so it
'      isn't captured a second time.
'
'  Only runs while at least one Clipboard history Stage or tab exists
'  (AddListener / RemoveListener). UI thread only.
' ============================================================================
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Threading
Imports Stages.Native
Imports WinForms = System.Windows.Forms
Imports GdiImaging = System.Drawing.Imaging

Namespace Services

    ''' <summary>What was copied: one of text, a picture, or files and folders.</summary>
    Public Enum ClipboardContentKind
        Text
        Picture
        Files
    End Enum

    ''' <summary>One copy, as read from the clipboard. Immutable.</summary>
    Public NotInheritable Class ClipboardSnapshot
        Public ReadOnly Property ContentKind As ClipboardContentKind
        ''' <summary>The copied text (ContentKind = Text).</summary>
        Public ReadOnly Property Text As String
        ''' <summary>The copied picture as PNG file bytes (ContentKind = Picture).</summary>
        Public ReadOnly Property PicturePngBytes As Byte()
        ''' <summary>Full paths of the copied files and folders (ContentKind = Files).</summary>
        Public ReadOnly Property FilePaths As IReadOnlyList(Of String)

        Public Sub New(kind As ClipboardContentKind, copiedText As String, pngBytes As Byte(), copiedPaths As IReadOnlyList(Of String))
            ContentKind = kind
            Text = copiedText
            PicturePngBytes = pngBytes
            FilePaths = If(copiedPaths, New List(Of String)())
        End Sub
    End Class

    Public NotInheritable Class ClipboardMonitor

        ' --- Clipboard format names (registered formats, so they are plain text names) ---
        Private Const ExcludeFromMonitorsFormat As String = "ExcludeClipboardContentFromMonitorProcessing"
        Private Const CanIncludeInHistoryFormat As String = "CanIncludeInClipboardHistory"
        Private Const ViewerIgnoreFormat As String = "Clipboard Viewer Ignore"
        ''' <summary>Browsers and Office put a PNG copy of pictures under this name (it keeps transparency).</summary>
        Private Const PngFormat As String = "PNG"

        ''' <summary>Wait this long after the last clipboard update before reading it.</summary>
        Private Shared ReadOnly SettleDelay As TimeSpan = TimeSpan.FromMilliseconds(150)
        ''' <summary>Stages' own copies are ignored for this long after they're made.</summary>
        Private Shared ReadOnly OwnCopyIgnoreTime As TimeSpan = TimeSpan.FromSeconds(1)
        ''' <summary>If another program has the clipboard open, try again this many times...</summary>
        Private Const ReadAttempts As Integer = 5
        ''' <summary>...this many milliseconds apart.</summary>
        Private Const ReadRetryDelayMilliseconds As Integer = 100
        ''' <summary>Text longer than this (about 4 MB) isn't kept - it is almost certainly not something to paste again.</summary>
        Public Const MaximumTextCharacters As Integer = 2000000

        Private Shared _instance As ClipboardMonitor

        Private ReadOnly _listeners As New List(Of Action(Of ClipboardSnapshot))()
        Private ReadOnly _settleTimer As DispatcherTimer
        Private _messageWindow As ClipboardMessageWindow
        Private _ignoreCopiesUntilUtc As DateTime = DateTime.MinValue
        Private _lastUpdateUtc As DateTime = DateTime.MinValue
        Private _isReading As Boolean

        ''' <summary>The single shared monitor (created on first use, on the UI thread).</summary>
        Public Shared ReadOnly Property Instance As ClipboardMonitor
            Get
                If _instance Is Nothing Then _instance = New ClipboardMonitor()
                Return _instance
            End Get
        End Property

        Private Sub New()
            _settleTimer = New DispatcherTimer() With {.Interval = SettleDelay}
            AddHandler _settleTimer.Tick, AddressOf SettleTimer_Tick
        End Sub

        ''' <summary>Starts telling <paramref name="listener"/> about every copy (and starts listening, if this is the first).</summary>
        Public Sub AddListener(listener As Action(Of ClipboardSnapshot))
            If _listeners.Contains(listener) Then Return
            _listeners.Add(listener)
            If _messageWindow Is Nothing Then StartListening()
        End Sub

        ''' <summary>Stops telling <paramref name="listener"/> (and stops listening when nobody is left).</summary>
        Public Sub RemoveListener(listener As Action(Of ClipboardSnapshot))
            _listeners.Remove(listener)
            If _listeners.Count = 0 Then StopListening()
        End Sub

        ''' <summary>
        ''' Call just before Stages puts something on the clipboard itself (copying an entry back),
        ''' so that copy isn't captured as a new entry.
        ''' </summary>
        Public Sub IgnoreOwnCopy()
            _ignoreCopiesUntilUtc = DateTime.UtcNow + OwnCopyIgnoreTime
        End Sub

        ''' <summary>Stops listening at exit.</summary>
        Public Sub ShutDown()
            _listeners.Clear()
            StopListening()
        End Sub

        Private Sub StartListening()
            Try
                ' Parent = HWND_MESSAGE makes it message-only: never shown, never in Alt+Tab.
                Dim windowParameters As New WinForms.CreateParams() With {.Parent = NativeMethods.HWND_MESSAGE}
                _messageWindow = New ClipboardMessageWindow(AddressOf OnClipboardUpdated)
                _messageWindow.CreateHandle(windowParameters)
                If Not NativeMethods.AddClipboardFormatListener(_messageWindow.Handle) Then
                    Logger.Warn("Windows refused to report clipboard changes (error " & Marshal.GetLastWin32Error().ToString() & "); Clipboard history Stages won't fill")
                Else
                    Logger.Info("Listening for clipboard changes (Clipboard history)")
                End If
            Catch ex As Exception
                Logger.Error("Could not start listening for clipboard changes", ex)
            End Try
        End Sub

        Private Sub StopListening()
            _settleTimer.Stop()
            If _messageWindow Is Nothing Then Return
            NativeMethods.RemoveClipboardFormatListener(_messageWindow.Handle)
            _messageWindow.DestroyHandle()
            _messageWindow = Nothing
        End Sub

        ''' <summary>WM_CLIPBOARDUPDATE arrived: (re)start the short wait before reading.</summary>
        Private Sub OnClipboardUpdated()
            _lastUpdateUtc = DateTime.UtcNow
            _settleTimer.Stop()
            _settleTimer.Start()
        End Sub

        ''' <summary>
        ''' The clipboard has been quiet for SettleDelay: read it once and tell every listener.
        ''' "Async Sub" = fire-and-forget; every error is caught inside.
        ''' </summary>
        Private Async Sub SettleTimer_Tick(sender As Object, e As EventArgs)
            _settleTimer.Stop()
            If _isReading Then
                ' A slow read (a big picture) is still going: look again shortly, so this newer copy isn't missed.
                _settleTimer.Start()
                Return
            End If
            If _lastUpdateUtc <= _ignoreCopiesUntilUtc Then Return ' Stages' own copy

            _isReading = True
            Try
                Dim snapshot As ClipboardSnapshot = Await ReadClipboardAsync()
                If snapshot Is Nothing Then Return
                For Each listener As Action(Of ClipboardSnapshot) In _listeners.ToList()
                    Try
                        listener.Invoke(snapshot)
                    Catch ex As Exception
                        Logger.Error("A Clipboard history Stage could not save what was copied", ex)
                    End Try
                Next
            Catch ex As Exception
                Logger.Warn("Could not read the clipboard", ex)
            Finally
                _isReading = False
            End Try
        End Sub

        ''' <summary>
        ''' Reads what is on the clipboard now. Nothing = nothing worth keeping (empty, private,
        ''' an unsupported format, too big, or the clipboard stayed locked by another program).
        ''' Files come first, then text, then a picture: copying cells in Excel puts text AND a
        ''' picture on the clipboard, and the text is what people want back.
        ''' </summary>
        Private Async Function ReadClipboardAsync() As Task(Of ClipboardSnapshot)
            For attempt As Integer = 1 To ReadAttempts
                Try
                    Dim copiedData As IDataObject = Clipboard.GetDataObject()
                    If copiedData Is Nothing Then Return Nothing
                    If IsMarkedPrivate(copiedData) Then
                        Logger.Info("Clipboard content marked private by the program that copied it; not saved")
                        Return Nothing
                    End If

                    If copiedData.GetDataPresent(DataFormats.FileDrop) Then
                        Dim copiedPaths As String() = TryCast(copiedData.GetData(DataFormats.FileDrop), String())
                        If copiedPaths Is Nothing OrElse copiedPaths.Length = 0 Then Return Nothing
                        Return New ClipboardSnapshot(ClipboardContentKind.Files, Nothing, Nothing, copiedPaths.ToList())
                    End If

                    If copiedData.GetDataPresent(DataFormats.UnicodeText) Then
                        Dim copiedText As String = TryCast(copiedData.GetData(DataFormats.UnicodeText), String)
                        If String.IsNullOrWhiteSpace(copiedText) Then Return Nothing
                        If copiedText.Length > MaximumTextCharacters Then
                            Logger.Info("Copied text is over " & MaximumTextCharacters.ToString() & " characters; not saved in Clipboard history")
                            Return Nothing
                        End If
                        Return New ClipboardSnapshot(ClipboardContentKind.Text, copiedText, Nothing, Nothing)
                    End If

                    Dim pngBytes As Byte() = Await ReadPictureAsPngAsync(copiedData)
                    If pngBytes Is Nothing Then Return Nothing
                    Return New ClipboardSnapshot(ClipboardContentKind.Picture, Nothing, pngBytes, Nothing)
                Catch ex As Exception When TypeOf ex Is COMException OrElse TypeOf ex Is ExternalException
                    ' Another program has the clipboard open right now: wait a little and try again.
                End Try
                Await Task.Delay(ReadRetryDelayMilliseconds * attempt)
            Next
            Logger.Info("The clipboard stayed locked by another program; this copy wasn't saved")
            Return Nothing
        End Function

        ''' <summary>True if the copying program asked monitors and clipboard history to leave this content alone.</summary>
        Private Shared Function IsMarkedPrivate(copiedData As IDataObject) As Boolean
            If copiedData.GetDataPresent(ExcludeFromMonitorsFormat) Then Return True
            If copiedData.GetDataPresent(ViewerIgnoreFormat) Then Return True
            If copiedData.GetDataPresent(CanIncludeInHistoryFormat) Then
                ' A 4-byte number: 0 = "don't keep this in history", 1 = "fine to keep".
                Dim markStream As MemoryStream = TryCast(copiedData.GetData(CanIncludeInHistoryFormat), MemoryStream)
                If markStream Is Nothing Then Return True ' present but unreadable: be safe
                Dim markBytes As Byte() = markStream.ToArray()
                Const NumberSize As Integer = 4
                If markBytes.Length < NumberSize Then Return True
                Return BitConverter.ToInt32(markBytes, 0) = 0
            End If
            Return False
        End Function

        ''' <summary>
        ''' The copied picture as PNG bytes, or Nothing if there is no picture. A ready-made PNG
        ''' (browsers, Office) is used as it is; otherwise the bitmap is taken on this thread and
        ''' turned into a PNG on a background thread, so a big screenshot can't stall the Stages.
        ''' </summary>
        Private Shared Async Function ReadPictureAsPngAsync(copiedData As IDataObject) As Task(Of Byte())
            If copiedData.GetDataPresent(PngFormat) Then
                Dim pngStream As MemoryStream = TryCast(copiedData.GetData(PngFormat), MemoryStream)
                If pngStream IsNot Nothing AndAlso pngStream.Length > 0 Then Return pngStream.ToArray()
            End If

            Dim hasBitmap As Boolean = copiedData.GetDataPresent(DataFormats.Bitmap) OrElse copiedData.GetDataPresent(DataFormats.Dib)
            If Not hasBitmap Then Return Nothing

            ' Windows Forms reads the clipboard's bitmap formats more reliably than WPF does
            ' (WPF can lose transparency or turn some screenshots black).
            Dim copiedPicture As System.Drawing.Image = WinForms.Clipboard.GetImage()
            If copiedPicture Is Nothing Then Return Nothing
            Return Await Task.Run(
                Function() As Byte()
                    Using copiedPicture
                        Using encoded As New MemoryStream()
                            copiedPicture.Save(encoded, GdiImaging.ImageFormat.Png)
                            Return encoded.ToArray()
                        End Using
                    End Using
                End Function)
        End Function

        ''' <summary>The hidden window Windows sends WM_CLIPBOARDUPDATE to.</summary>
        Private NotInheritable Class ClipboardMessageWindow
            Inherits WinForms.NativeWindow

            Private ReadOnly _onUpdate As Action

            Public Sub New(onUpdate As Action)
                _onUpdate = onUpdate
            End Sub

            Protected Overrides Sub WndProc(ByRef m As WinForms.Message)
                If m.Msg = NativeMethods.WM_CLIPBOARDUPDATE Then
                    _onUpdate.Invoke()
                    Return
                End If
                MyBase.WndProc(m)
            End Sub
        End Class

    End Class

End Namespace
