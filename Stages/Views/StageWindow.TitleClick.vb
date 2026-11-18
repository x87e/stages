' ============================================================================
'  StageWindow.TitleClick.vb   (part of the StageWindow class - see StageWindow.xaml.vb)
'  Rolling a Stage up to its title bar with a single click on the title bar,
'  and down again with another (v0.8.3, at Dan's request).
'
'    * Switched on for every Stage in Settings > General > Title bar ("Click
'      the title bar to roll a Stage up"), off by default.
'    * Each Stage can follow that setting or choose for itself: Stage menu >
'      Title bar click > Same as Settings / Roll up and down / Do nothing
'      (StageInfo.TitleClickRollsUp: Nothing = Same as Settings).
'
'  How it differs from Minify (which shrinks a Stage while the mouse is
'  away): a rolled-up Stage STAYS rolled up - hovering doesn't open it - until
'  its title bar is clicked again. It's remembered across restarts
'  (StageInfo.IsRolledUp). Both use the same height change, so "Animate
'  opening and closing" (Settings > General > Minified Stages) applies too.
'
'  Telling a click from the other things the title bar does:
'    * press-and-drag moves the Stage - if the Stage moved, it wasn't a click;
'    * a double-click renames it - so a single click only acts once the
'      double-click time (Windows' mouse setting, usually half a second) has
'      passed without a second click. That short wait is the price of keeping
'      double-click-to-rename;
'    * a click on a tab switches tabs, never rolls up.
'  A rolled-up Stage can always be clicked down again, even if the option has
'  been switched off since, so a Stage can never get stuck rolled up.
' ============================================================================
Imports System.Windows.Threading
Imports Stages.Model
Imports Stages.Native
Imports Stages.Services
Imports WinForms = System.Windows.Forms

Partial Public Class StageWindow

#Region "Title click constants"
    Private Const TitleClickSubmenuText As String = "Title bar click"
    ''' <summary>{0} = what Settings currently says, e.g. "rolls up".</summary>
    Private Const TitleClickSameAsSettingsFormat As String = "Same as Settings ({0})"
    Private Const TitleClickSettingsRollsUpText As String = "rolls up"
    Private Const TitleClickSettingsDoesNothingText As String = "does nothing"
    Private Const TitleClickRollsUpText As String = "Roll up and down"
    Private Const TitleClickDoesNothingText As String = "Do nothing"
#End Region

#Region "Title click fields"
    ''' <summary>Waits out the double-click time after a click on the title bar (then rolls up or down).</summary>
    Private _titleClickTimer As DispatcherTimer
#End Region

#Region "Title click: rolling up and down"

    ''' <summary>True if a click on this Stage's title bar rolls it up (its own choice, or Settings').</summary>
    Private Function ClickRollsUpThisStage() As Boolean
        If _settings.TitleClickRollsUp.HasValue Then Return _settings.TitleClickRollsUp.Value
        Return AppSettingsStore.Current.TitleClickRollsUp
    End Function

    ''' <summary>
    ''' Called after a press on the title bar has been let go (StageWindow.xaml.vb). If the Stage
    ''' didn't move and the press wasn't on a tab, waits to see whether a second click (a
    ''' double-click = rename) follows; if not, rolls the Stage up or down.
    ''' </summary>
    Private Sub OnTitleBarPressReleased(stageMoved As Boolean, pressedElement As DependencyObject)
        If stageMoved Then Return
        If FindTabHeaderFromElement(pressedElement) IsNot Nothing Then Return ' that click switched tabs
        ' Rolling down is always allowed; rolling up only when the option is on.
        If Not _settings.IsRolledUp AndAlso Not ClickRollsUpThisStage() Then Return

        If _titleClickTimer Is Nothing Then
            _titleClickTimer = New DispatcherTimer()
            AddHandler _titleClickTimer.Tick, AddressOf TitleClickTimer_Tick
        End If
        _titleClickTimer.Interval = TimeSpan.FromMilliseconds(WinForms.SystemInformation.DoubleClickTime)
        _titleClickTimer.Stop()
        _titleClickTimer.Start()
    End Sub

    ''' <summary>A second click came (a double-click): it renames, so the waiting single click is dropped.</summary>
    Private Sub CancelPendingTitleClick()
        If _titleClickTimer IsNot Nothing Then _titleClickTimer.Stop()
    End Sub

    ''' <summary>No second click came: it was a single click.</summary>
    Private Sub TitleClickTimer_Tick(sender As Object, e As EventArgs)
        _titleClickTimer.Stop()
        If _isClosed OrElse _isEditingTitle Then Return
        If _settings.IsRolledUp Then
            RollDown()
        ElseIf ClickRollsUpThisStage() Then
            RollUp()
        End If
    End Sub

    ''' <summary>Shrinks the Stage to its title bar and keeps it there (hovering won't open it).</summary>
    Private Sub RollUp()
        _settings.IsRolledUp = True
        CollapseToTitleBar()
        SaveNow()
    End Sub

    ''' <summary>
    ''' Opens a rolled-up Stage again. A Stage that also minifies then behaves as usual: it
    ''' stays open while the mouse is over it and shrinks when the mouse leaves.
    ''' </summary>
    Private Sub RollDown()
        _settings.IsRolledUp = False
        ExpandFromTitleBar()
        SaveNow()
    End Sub

#End Region

#Region "Title click: the Stage menu entry"

    ''' <summary>Stage menu: "Title bar click" > Same as Settings (...) / Roll up and down / Do nothing.</summary>
    Private Sub AddTitleClickSubmenu(stageMenu As NativePopupMenu)
        Dim settingsSays As String = If(AppSettingsStore.Current.TitleClickRollsUp, TitleClickSettingsRollsUpText, TitleClickSettingsDoesNothingText)
        Dim choiceMenu As NativePopupMenu = stageMenu.AddSubmenu(TitleClickSubmenuText)
        choiceMenu.AddItem(String.Format(TitleClickSameAsSettingsFormat, settingsSays),
                           Sub() ChooseTitleClick(Nothing),
                           isChecked:=Not _settings.TitleClickRollsUp.HasValue)
        choiceMenu.AddItem(TitleClickRollsUpText, Sub() ChooseTitleClick(True),
                           isChecked:=_settings.TitleClickRollsUp.HasValue AndAlso _settings.TitleClickRollsUp.Value)
        choiceMenu.AddItem(TitleClickDoesNothingText, Sub() ChooseTitleClick(False),
                           isChecked:=_settings.TitleClickRollsUp.HasValue AndAlso Not _settings.TitleClickRollsUp.Value)
    End Sub

    ''' <summary>
    ''' This Stage's own choice (Nothing = Same as Settings). Choosing "Do nothing" while rolled
    ''' up also rolls the Stage down, so it isn't left rolled up with no click to open it.
    ''' </summary>
    Private Sub ChooseTitleClick(choice As Boolean?)
        _settings.TitleClickRollsUp = choice
        If _settings.IsRolledUp AndAlso Not ClickRollsUpThisStage() Then
            RollDown()
            Return ' RollDown saved
        End If
        SaveNow()
    End Sub

#End Region

End Class
