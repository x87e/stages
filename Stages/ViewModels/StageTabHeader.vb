' ============================================================================
'  StageTabHeader.vb
'  What one tab in a Stage's title bar binds to (StageWindow.xaml, TabStrip):
'  its title, and whether it is the open tab (drawn highlighted - see
'  StageTabHeaderStyle in StagesStyles.xaml).
' ============================================================================
Imports System.ComponentModel
Imports Stages.Model

Namespace ViewModels

    Public NotInheritable Class StageTabHeader
        Implements INotifyPropertyChanged

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Private _isActive As Boolean

        ''' <param name="tabSettings">The tab this header stands for.</param>
        ''' <param name="isOpenTab">True for the tab currently shown.</param>
        Public Sub New(tabSettings As StageInfo, isOpenTab As Boolean)
            Tab = tabSettings
            _isActive = isOpenTab
        End Sub

        ''' <summary>The tab's settings (its folder, items and layout).</summary>
        Public ReadOnly Property Tab As StageInfo

        ''' <summary>The text on the tab: the tab's name.</summary>
        Public ReadOnly Property Title As String
            Get
                Return Tab.Name
            End Get
        End Property

        ''' <summary>True for the tab currently shown.</summary>
        Public Property IsActive As Boolean
            Get
                Return _isActive
            End Get
            Set(value As Boolean)
                If value = _isActive Then Return
                _isActive = value
                RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(IsActive)))
            End Set
        End Property

    End Class

End Namespace
