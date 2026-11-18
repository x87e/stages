' ============================================================================
'  StartupRegistration.vb
'  "Start Stages when I sign in to Windows" (Settings > General).
'
'  Uses the standard per-user Run key - no admin rights, nothing installed:
'      HKCU\Software\Microsoft\Windows\CurrentVersion\Run
'          Stages = "C:\...\Stages.exe"
'  It's also listed in Windows' own Settings > Apps > Startup and Task
'  Manager's Startup tab, where it can be switched off. Windows records that
'  switch separately, under ...\Explorer\StartupApproved\Run, so Stages reads
'  it too: an entry switched off there counts as "off" here, and turning the
'  option on in Stages clears it so the entry really runs.
'
'  The registry is the only record (nothing in settings.xml), so the Settings
'  window always shows what Windows will actually do.
' ============================================================================
Imports Microsoft.Win32

Namespace Services

    Public NotInheritable Class StartupRegistration

        Private Const RunKeyPath As String = "Software\Microsoft\Windows\CurrentVersion\Run"
        Private Const StartupApprovedKeyPath As String = "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
        ''' <summary>The name of Stages' entry in both keys.</summary>
        Private Const EntryName As String = "Stages"
        ''' <summary>
        ''' In StartupApproved, the first byte of the value says whether the entry is switched
        ''' on: even numbers (02, 06) = on, odd numbers (03, 07) = switched off by the user.
        ''' </summary>
        Private Const SwitchedOffBitMask As Byte = &H1

        Private Sub New()
        End Sub

        ''' <summary>Full path of the running Stages.exe - what the Run entry points at.</summary>
        Public Shared ReadOnly Property CurrentProgramPath As String
            Get
                Return GetType(StartupRegistration).Assembly.Location
            End Get
        End Property

        ''' <summary>
        ''' True if Windows will start Stages at sign-in: the Run entry exists and hasn't been
        ''' switched off in Windows' Startup apps list.
        ''' </summary>
        Public Shared Function IsEnabled() As Boolean
            Try
                Using runKey As RegistryKey = Registry.CurrentUser.OpenSubKey(RunKeyPath)
                    If runKey Is Nothing OrElse runKey.GetValue(EntryName) Is Nothing Then Return False
                End Using
                Return Not IsSwitchedOffInWindowsSettings()
            Catch ex As Exception
                Logger.Warn("Could not read the start-with-Windows setting", ex)
                Return False
            End Try
        End Function

        ''' <summary>The program path the Run entry points at, or Nothing if there is no entry.</summary>
        Public Shared Function RegisteredProgramPath() As String
            Try
                Using runKey As RegistryKey = Registry.CurrentUser.OpenSubKey(RunKeyPath)
                    If runKey Is Nothing Then Return Nothing
                    Dim storedCommand As String = TryCast(runKey.GetValue(EntryName), String)
                    If storedCommand Is Nothing Then Return Nothing
                    Return storedCommand.Trim(ControlChars.Quote)
                End Using
            Catch ex As Exception
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Turns starting with Windows on (pointing at the running Stages.exe, or at
        ''' <paramref name="programPath"/> if given - installing names the installed copy, see
        ''' PortableInstaller) or off. Returns False (and logs) if the registry couldn't be changed.
        ''' </summary>
        Public Shared Function SetEnabled(shouldStart As Boolean, Optional programPath As String = Nothing) As Boolean
            Dim startedProgram As String = If(programPath, CurrentProgramPath)
            Try
                Using runKey As RegistryKey = Registry.CurrentUser.CreateSubKey(RunKeyPath)
                    If shouldStart Then
                        ' Quoted, so a path with spaces is read as one program.
                        runKey.SetValue(EntryName, ControlChars.Quote & startedProgram & ControlChars.Quote, RegistryValueKind.String)
                    Else
                        runKey.DeleteValue(EntryName, throwOnMissingValue:=False)
                    End If
                End Using

                ' Either way, forget any old on/off switch from Windows' Startup apps list:
                ' turning it on here must really turn it on.
                Using approvedKey As RegistryKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, writable:=True)
                    If approvedKey IsNot Nothing Then
                        approvedKey.DeleteValue(EntryName, throwOnMissingValue:=False)
                    End If
                End Using

                Logger.Info("Start with Windows " & If(shouldStart, "on (" & startedProgram & ")", "off"))
                Return True
            Catch ex As Exception
                Logger.Error("Could not change the start-with-Windows setting", ex)
                Return False
            End Try
        End Function

        ''' <summary>True if the user switched Stages off in Windows' Startup apps list / Task Manager.</summary>
        Private Shared Function IsSwitchedOffInWindowsSettings() As Boolean
            Using approvedKey As RegistryKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath)
                If approvedKey Is Nothing Then Return False
                Dim switchData As Byte() = TryCast(approvedKey.GetValue(EntryName), Byte())
                If switchData Is Nothing OrElse switchData.Length = 0 Then Return False
                Return (switchData(0) And SwitchedOffBitMask) = SwitchedOffBitMask
            End Using
        End Function

    End Class

End Namespace
