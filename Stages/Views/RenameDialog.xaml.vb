' ============================================================================
'  RenameDialog.xaml.vb
'  Asks for a new name - of a Stage, or of a shortcut on a Stage. Usage:
'      Dim dialog As New RenameDialog(currentName, "Rename shortcut") With {.Owner = someWindow}
'      If dialog.ShowDialog().GetValueOrDefault() Then use dialog.NewName
'  The window title defaults to "Rename Stage" (set in RenameDialog.xaml).
' ============================================================================
Partial Public Class RenameDialog

    ''' <param name="currentName">Pre-filled into the text box, fully selected so typing replaces it.</param>
    ''' <param name="windowTitle">Optional window title; leave it out for "Rename Stage".</param>
    Public Sub New(currentName As String, Optional windowTitle As String = Nothing)
        InitializeComponent()
        If Not String.IsNullOrEmpty(windowTitle) Then
            Title = windowTitle
        End If
        NameTextBox.Text = currentName
        NameTextBox.SelectAll()
    End Sub

    ''' <summary>The native window now exists: give it a title bar matching the theme.</summary>
    Protected Overrides Sub OnSourceInitialized(e As EventArgs)
        MyBase.OnSourceInitialized(e)
        Services.ThemeManager.ApplyTitleBar(Me)
    End Sub

    ''' <summary>The name the user entered, without leading/trailing spaces.</summary>
    Public ReadOnly Property NewName As String
        Get
            Return NameTextBox.Text.Trim()
        End Get
    End Property

    Private Sub OkButton_Click(sender As Object, e As RoutedEventArgs)
        ' An empty title isn't allowed.
        If NewName.Length = 0 Then
            NameTextBox.Focus()
            Return
        End If

        ' Setting DialogResult closes the dialog and makes ShowDialog() return True.
        DialogResult = True
    End Sub

End Class
