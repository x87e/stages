' ============================================================================
'  Logger.vb
'  A tiny text-file logger: %LOCALAPPDATA%\Programs\Stages\Logs\stages.log
'
'  Records startup, recoveries and every error Stages catches, so if anything
'  misbehaves the log says what and when. The logger itself can never throw -
'  a logging failure must not crash the app.
' ============================================================================
Imports System.Diagnostics
Imports System.IO
Imports System.Text

Namespace Services

    Public NotInheritable Class Logger

        Private Const LogFileName As String = "stages.log"
        Private Const PreviousLogFileName As String = "stages.previous.log"
        ''' <summary>When the log grows past this size it is renamed and a fresh one started.</summary>
        Private Const MaximumLogSizeBytes As Long = 1024L * 1024L
        Private Const TimestampFormat As String = "yyyy-MM-dd HH:mm:ss.fff"

        ''' <summary>Several threads may log at once; this keeps lines from interleaving.</summary>
        Private Shared ReadOnly WriteLock As New Object()

        Private Sub New()
        End Sub

        ''' <summary>Full path of the current log file.</summary>
        Public Shared ReadOnly Property LogFilePath As String
            Get
                Return Path.Combine(AppPaths.LogsFolder, LogFileName)
            End Get
        End Property

        ''' <summary>Normal, noteworthy events (startup, recoveries).</summary>
        Public Shared Sub Info(message As String)
            Write("INFO ", message, Nothing)
        End Sub

        ''' <summary>Something went wrong but was handled with no visible effect.</summary>
        Public Shared Sub Warn(message As String, Optional problem As Exception = Nothing)
            Write("WARN ", message, problem)
        End Sub

        ''' <summary>Something failed. The exception's full details are recorded.</summary>
        Public Shared Sub [Error](message As String, Optional problem As Exception = Nothing)
            Write("ERROR", message, problem)
        End Sub

        Private Shared Sub Write(level As String, message As String, problem As Exception)
            Try
                Dim line As New StringBuilder()
                line.Append(DateTime.Now.ToString(TimestampFormat))
                line.Append(" [").Append(level).Append("] ")
                line.Append(message)
                If problem IsNot Nothing Then
                    line.AppendLine()
                    line.Append(problem.ToString())
                End If
                line.AppendLine()

                SyncLock WriteLock
                    Directory.CreateDirectory(AppPaths.LogsFolder)
                    RotateIfTooLarge()
                    File.AppendAllText(LogFilePath, line.ToString(), Encoding.UTF8)
                End SyncLock

                ' Also visible in Visual Studio's Output window while debugging.
                Debug.Write(line.ToString())
            Catch
                ' Deliberately swallowed: there is nowhere left to report a logging failure.
            End Try
        End Sub

        ''' <summary>Keeps disk use bounded: current log + one previous log, ~1 MB each.</summary>
        Private Shared Sub RotateIfTooLarge()
            Dim currentLog As New FileInfo(LogFilePath)
            If Not currentLog.Exists OrElse currentLog.Length < MaximumLogSizeBytes Then
                Return
            End If

            Dim previousLogPath As String = Path.Combine(AppPaths.LogsFolder, PreviousLogFileName)
            If File.Exists(previousLogPath) Then
                File.Delete(previousLogPath)
            End If
            File.Move(LogFilePath, previousLogPath)
        End Sub

    End Class

End Namespace
