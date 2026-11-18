' ============================================================================
'  AppSettingsStore.vb
'  Loads and saves the app-wide settings (AppSettings) from
'      %LOCALAPPDATA%\Programs\Stages\settings.xml
'
'  Usage:
'      AppSettingsStore.Load()                 ' once, at startup
'      AppSettingsStore.Current.DefaultBackdrop ' read a setting anywhere
'      AppSettingsStore.Save()                 ' after changing something
'
'  Same robustness rules as the Stage settings files:
'    * a missing, unreadable or damaged file never stops Stages starting
'      (the .bak copy is tried, then built-in defaults are used),
'    * saves are atomic (AtomicFile) and never throw.
' ============================================================================
Imports System.IO
Imports System.Xml.Serialization
Imports Stages.Model

Namespace Services

    Public NotInheritable Class AppSettingsStore

        ''' <summary>Re-used for every load/save (creating XmlSerializers is slow).</summary>
        Private Shared ReadOnly _serializer As New XmlSerializer(GetType(AppSettings))
        Private Shared ReadOnly _saveLock As New Object()

        ''' <summary>The settings in use. Built-in defaults until Load() has run.</summary>
        Private Shared _current As New AppSettings()

        Private Sub New()
        End Sub

        ''' <summary>The settings currently in use. Change values on it directly, then call Save().</summary>
        Public Shared ReadOnly Property Current As AppSettings
            Get
                Return _current
            End Get
        End Property

        ''' <summary>
        ''' Reads settings.xml (or its .bak). If neither can be read, built-in defaults
        ''' are used; the file is written the first time something is changed.
        ''' </summary>
        Public Shared Sub Load()
            Dim settingsPath As String = AppPaths.SettingsFilePath
            Dim candidatePaths As String() = {settingsPath, settingsPath & AtomicFile.BackupFileSuffix}

            For Each candidatePath As String In candidatePaths
                If Not File.Exists(candidatePath) Then
                    Continue For
                End If

                Try
                    Using reader As FileStream = File.OpenRead(candidatePath)
                        Dim loadedSettings As AppSettings = TryCast(_serializer.Deserialize(reader), AppSettings)
                        If loadedSettings IsNot Nothing Then
                            loadedSettings.Sanitize()
                            _current = loadedSettings
                            If candidatePath <> settingsPath Then
                                Logger.Warn("settings.xml was unreadable; recovered app settings from backup: " & candidatePath)
                            End If
                            Logger.Info("Loaded app settings from " & candidatePath)
                            Return
                        End If
                    End Using
                Catch ex As Exception
                    Logger.Warn("Could not read app settings file " & candidatePath, ex)
                End Try
            Next

            Logger.Info("No readable settings.xml - using built-in defaults")
            _current = New AppSettings()
        End Sub

        ''' <summary>
        ''' Writes the current settings to settings.xml. Never throws; returns False and
        ''' logs on failure (the settings still apply for this session).
        ''' </summary>
        Public Shared Function Save() As Boolean
            SyncLock _saveLock
                Try
                    Directory.CreateDirectory(AppPaths.RootFolder)

                    ' Serialise in memory first: a serialisation error never touches the disk.
                    Dim xmlBytes As Byte()
                    Using buffer As New MemoryStream()
                        _serializer.Serialize(buffer, _current)
                        xmlBytes = buffer.ToArray()
                    End Using

                    AtomicFile.WriteAllBytes(AppPaths.SettingsFilePath, xmlBytes)
                    Return True
                Catch ex As Exception
                    Logger.Error("Could not save app settings to " & AppPaths.SettingsFilePath, ex)
                    Return False
                End Try
            End SyncLock
        End Function

    End Class

End Namespace
