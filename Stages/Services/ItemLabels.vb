' ============================================================================
'  ItemLabels.vb
'  Builds the text under each icon on a Stage, with or without the file's
'  extension according to Settings > Items > File name extensions.
'
'  Each StageItem carries its label in two parts, worked out once when the
'  folder is scanned (StageFolder.InspectEntry):
'      LabelWithoutExtension  "Report"
'      LabelExtension         ".docx"   (empty for folders, web links, etc.)
'  so switching the setting only re-joins the parts; nothing is re-scanned.
'
'  Called on the UI thread only (from StageItemViewModel).
' ============================================================================
Imports System.Diagnostics
Imports Stages.Model
Imports Stages.Native

Namespace Services

    Public NotInheritable Class ItemLabels

        ''' <summary>
        ''' Extension -> "does Windows know this file type?". Registry lookups are cheap, but
        ''' every label asks, so answers are remembered until ForgetKnownFileTypes is called
        ''' (when Windows reports that file associations changed).
        ''' </summary>
        Private Shared ReadOnly _knownFileTypes As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' How long one reading of Explorer's "hide extensions" setting is reused. Every label
        ''' asks for it, and every Stage re-labels its items every few seconds, so without this
        ''' a big folder Stage would read the registry thousands of times a minute. One second
        ''' is short enough that a change in Explorer still shows up at the next refresh.
        ''' </summary>
        Private Const ExplorerSettingReuseMilliseconds As Long = 1000
        ''' <summary>Measures the time since Explorer's setting was last read (never wraps or goes backwards).</summary>
        Private Shared ReadOnly _sinceExplorerSettingRead As New Stopwatch()
        ''' <summary>The last reading of Explorer's setting (valid while the stopwatch is running).</summary>
        Private Shared _windowsHidesExtensions As Boolean

        Private Sub New()
        End Sub

        ''' <summary>Explorer's "hide extensions for known file types" setting, read at most once a second.</summary>
        Private Shared Function WindowsHidesExtensions() As Boolean
            Dim readingIsStale As Boolean = Not _sinceExplorerSettingRead.IsRunning OrElse
                                            _sinceExplorerSettingRead.ElapsedMilliseconds >= ExplorerSettingReuseMilliseconds
            If readingIsStale Then
                _windowsHidesExtensions = DesktopIntegration.IsWindowsHidingFileExtensions()
                _sinceExplorerSettingRead.Restart()
            End If
            Return _windowsHidesExtensions
        End Function

        ''' <summary>The label to show for an item right now.</summary>
        Public Shared Function Compose(item As StageItem) As String
            If ShouldShowExtension(item.LabelExtension) Then
                Return item.LabelWithoutExtension & item.LabelExtension
            End If
            Return item.LabelWithoutExtension
        End Function

        ''' <summary>
        ''' Whether to show <paramref name="extension"/> (e.g. ".docx") under the current setting.
        ''' Same as Windows: shown if Explorer shows extensions, or if the type is unknown to
        ''' Windows (Explorer only hides extensions of KNOWN file types).
        ''' </summary>
        Public Shared Function ShouldShowExtension(extension As String) As Boolean
            If String.IsNullOrEmpty(extension) Then Return False ' nothing to show

            Select Case AppSettingsStore.Current.FileExtensions
                Case FileExtensionDisplay.AlwaysShow
                    Return True
                Case FileExtensionDisplay.AlwaysHide
                    Return False
                Case Else
                    If Not WindowsHidesExtensions() Then Return True
                    Return Not IsKnownFileType(extension)
            End Select
        End Function

        ''' <summary>Forgets the remembered file types, e.g. after Windows reports changed associations.</summary>
        Public Shared Sub ForgetKnownFileTypes()
            _knownFileTypes.Clear()
        End Sub

        Private Shared Function IsKnownFileType(extension As String) As Boolean
            Dim isKnown As Boolean
            If Not _knownFileTypes.TryGetValue(extension, isKnown) Then
                isKnown = DesktopIntegration.IsKnownFileType(extension)
                _knownFileTypes(extension) = isKnown
            End If
            Return isKnown
        End Function

    End Class

End Namespace
