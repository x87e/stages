' ============================================================================
'  StaWorkerThread.vb
'  A single background thread that runs queued jobs one at a time.
'
'  Why it exists:
'    Windows shell calls (getting icons, launching shortcuts) must run on an
'    "STA" thread, and Microsoft's docs warn they can block for a long time
'    (e.g. an icon on a disconnected network drive). The original NoFences
'    made those calls on the UI thread, so one slow drive froze the Stage.
'    Running them here keeps the Stages responsive.
'
'  Two kinds of worker:
'    * ShellOperations - one shared worker for icons and opening items.
'    * CreateDedicated - one per Stage window for scanning its folder, so a
'      Stage on an unreachable network folder (which can take Windows 20-30
'      seconds to give up on) never holds up the other Stages' scans or icons.
'      Its owner calls Shutdown when the window closes.
' ============================================================================
Imports System.Collections.Concurrent
Imports System.Threading

Namespace Services

    Public NotInheritable Class StaWorkerThread

        ''' <summary>The one shared worker used for all shell operations in Stages.</summary>
        Public Shared ReadOnly Property ShellOperations As StaWorkerThread = New StaWorkerThread("Stages shell worker")

        ''' <summary>
        ''' A new worker of its own, for one Stage's folder scans. STA too: scanning reads
        ''' shortcut files through the shell's COM objects. Call Shutdown when finished with it.
        ''' </summary>
        Public Shared Function CreateDedicated(threadName As String) As StaWorkerThread
            Return New StaWorkerThread(threadName)
        End Function

        ''' <summary>
        ''' Lets the thread finish the job it's on and then end. Jobs queued afterwards are
        ''' refused (RunAsync returns a cancelled task). Never call this on ShellOperations.
        ''' </summary>
        Public Sub Shutdown()
            If Not _pendingJobs.IsAddingCompleted Then
                _pendingJobs.CompleteAdding()
            End If
        End Sub

        ''' <summary>Jobs waiting to run, in order. BlockingCollection makes the worker sleep while it is empty.</summary>
        Private ReadOnly _pendingJobs As New BlockingCollection(Of Action)()
        Private ReadOnly _workerThread As Thread

        Private Sub New(threadName As String)
            _workerThread = New Thread(New ThreadStart(AddressOf ProcessJobs))
            _workerThread.Name = threadName
            ' Background = this thread won't keep the process alive when Stages exits.
            _workerThread.IsBackground = True
            _workerThread.SetApartmentState(ApartmentState.STA)
            _workerThread.Start()
        End Sub

        ''' <summary>Queues a job that returns a value. Await the returned task to get the value back.</summary>
        Public Function RunAsync(Of TResult)(job As Func(Of TResult)) As Task(Of TResult)
            ' RunContinuationsAsynchronously: whoever awaits this is never resumed ON the worker thread.
            Dim completion As New TaskCompletionSource(Of TResult)(TaskCreationOptions.RunContinuationsAsynchronously)
            Try
                _pendingJobs.Add(
                    Sub()
                        Try
                            completion.SetResult(job())
                        Catch ex As Exception
                            completion.SetException(ex)
                        End Try
                    End Sub)
            Catch ex As InvalidOperationException
                ' The worker has been shut down (its Stage closed): the job will never run.
                completion.TrySetCanceled()
            End Try
            Return completion.Task
        End Function

        ''' <summary>Queues a fire-and-forget job. Errors are logged.</summary>
        Public Sub Post(job As Action)
            Try
                _pendingJobs.Add(job)
            Catch ex As InvalidOperationException
                ' Shut down: nothing more will run here.
                Logger.Warn("A background job was dropped because its worker had shut down", ex)
            End Try
        End Sub

        ''' <summary>The worker loop: take the next job (waiting if there is none) and run it.</summary>
        Private Sub ProcessJobs()
            For Each job As Action In _pendingJobs.GetConsumingEnumerable()
                Try
                    job()
                Catch ex As Exception
                    ' One bad job must never kill the worker thread.
                    Logger.Error("Background shell job failed", ex)
                End Try
            Next
        End Sub

    End Class

End Namespace
