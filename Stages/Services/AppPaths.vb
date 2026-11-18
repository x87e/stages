' ============================================================================
'  AppPaths.vb
'  Where Stages lives on disk. Everything sits under one folder:
'
'    %LOCALAPPDATA%\Programs\Stages\        <- RootFolder
'        App\                               <- AppFolder: Stages.exe and its config.
'                                              Application updates only ever touch this.
'        Data\                              <- DataFolder: one folder per Stage, named
'            My First Stage\                   after the Stage's title. Safe to browse,
'                .stage-settings.xml (hidden)  back up or edit by hand; never touched
'                Some File.lnk                 by updates.
'        Logs\                              <- LogsFolder: stages.log
'        settings.xml                       <- SettingsFilePath: app-wide settings
'                                              (Settings window). Never touched by updates.
'
'  The paths are fixed (not relative to the .exe), so a debug build running
'  from bin\Debug uses exactly the same Stages as the installed copy.
' ============================================================================
Imports System.IO

Namespace Services

    Public NotInheritable Class AppPaths

        Private Const ProgramsFolderName As String = "Programs"
        Private Const ApplicationFolderName As String = "Stages"
        Private Const AppSubfolderName As String = "App"
        Private Const DataSubfolderName As String = "Data"
        Private Const LogsSubfolderName As String = "Logs"
        Private Const SettingsFileName As String = "settings.xml"

        Private Sub New()
        End Sub

        ''' <summary>%LOCALAPPDATA%\Programs\Stages</summary>
        Public Shared ReadOnly Property RootFolder As String
            Get
                Dim localAppData As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                Return Path.Combine(localAppData, ProgramsFolderName, ApplicationFolderName)
            End Get
        End Property

        ''' <summary>%LOCALAPPDATA%\Programs\Stages\App - where the program itself is installed.</summary>
        Public Shared ReadOnly Property AppFolder As String
            Get
                Return Path.Combine(RootFolder, AppSubfolderName)
            End Get
        End Property

        ''' <summary>%LOCALAPPDATA%\Programs\Stages\Data - one sub-folder per Stage.</summary>
        Public Shared ReadOnly Property DataFolder As String
            Get
                Return Path.Combine(RootFolder, DataSubfolderName)
            End Get
        End Property

        ''' <summary>%LOCALAPPDATA%\Programs\Stages\Logs</summary>
        Public Shared ReadOnly Property LogsFolder As String
            Get
                Return Path.Combine(RootFolder, LogsSubfolderName)
            End Get
        End Property

        ''' <summary>
        ''' %LOCALAPPDATA%\Programs\Stages\settings.xml - the app-wide settings (AppSettings).
        ''' Deliberately outside App\ (so updates never replace it) and outside Data\ (where
        ''' every entry is treated as a Stage).
        ''' </summary>
        Public Shared ReadOnly Property SettingsFilePath As String
            Get
                Return Path.Combine(RootFolder, SettingsFileName)
            End Get
        End Property

    End Class

End Namespace
