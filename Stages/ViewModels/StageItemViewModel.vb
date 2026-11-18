' ============================================================================
'  StageItemViewModel.vb
'  What the Stage's item list binds to: one per visible icon.
'
'  WPF data binding refresher: XAML like {Binding Icon} reads the Icon property
'  below. Raising PropertyChanged("Icon") tells WPF to re-read it - that's how
'  the picture swaps from the plain icon to a thumbnail once it has loaded, and
'  how an item dims when its shortcut target goes missing, and how labels
'  gain or lose their extension when Settings > Items changes.
' ============================================================================
Imports System.ComponentModel
Imports Stages.Model
Imports Stages.Services

Namespace ViewModels

    Public NotInheritable Class StageItemViewModel
        Implements INotifyPropertyChanged
        Implements IPositionedItem

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Private _item As StageItem
        Private _icon As ImageSource
        ''' <summary>The label as last shown, so a refresh only notifies WPF when it really changed.</summary>
        Private _displayName As String
        ''' <summary>Icon size in pixels for the Stage's current view (16, 32 or 48).</summary>
        Private _iconSize As Integer
        ''' <summary>
        ''' Counts picture loads. A load that finishes after a newer one started (e.g. the view
        ''' changed size twice quickly) is ignored, so the newest picture always wins.
        ''' </summary>
        Private _pictureLoadNumber As Integer

        ''' <param name="stageItem">The folder entry to show.</param>
        ''' <param name="iconSize">Icon size in pixels for the Stage's view.</param>
        Public Sub New(stageItem As StageItem, iconSize As Integer)
            _item = stageItem
            _iconSize = iconSize
            _displayName = ItemLabels.Compose(stageItem)
            BeginLoadingPicture()
        End Sub

        ''' <summary>
        ''' Where the user dropped this item (Auto arrange off), or Nothing to let the Stage
        ''' place it. Read by StageItemsPanel when it lays the items out.
        ''' </summary>
        Public Property LayoutPosition As Point? Implements IPositionedItem.LayoutPosition

        ''' <summary>Windows' name for the kind of file (Details view).</summary>
        Public ReadOnly Property TypeName As String
            Get
                Return _item.TypeName
            End Get
        End Property

        ''' <summary>Date modified as text in the user's own date format (Details view).</summary>
        ''' <summary>
        ''' The size for the Details view, the way Windows writes it: "512 bytes", "12.3 KB",
        ''' "4.56 GB". Empty for folders and anything else without a size.
        ''' </summary>
        Public ReadOnly Property SizeDisplay As String
            Get
                If Not _item.HasSize Then Return String.Empty
                Return FormatFileSize(_item.SizeBytes)
            End Get
        End Property

        ''' <summary>Units above bytes, each 1024 times the one before (as Windows counts).</summary>
        Private Shared ReadOnly SizeUnits As String() = {"KB", "MB", "GB", "TB", "PB"}
        Private Const BytesPerUnitStep As Double = 1024
        Private Const BytesText As String = " bytes"

        ''' <summary>
        ''' Bytes as Windows shows them: under 1 KB in bytes, otherwise three significant digits
        ''' (1.23 MB, 12.3 MB, 123 MB), cut off rather than rounded up, as Explorer does.
        ''' </summary>
        Public Shared Function FormatFileSize(sizeBytes As Long) As String
            If sizeBytes < BytesPerUnitStep Then
                Return sizeBytes.ToString("N0") & BytesText
            End If
            Dim value As Double = sizeBytes
            Dim unitIndex As Integer = -1
            Do
                value /= BytesPerUnitStep
                unitIndex += 1
            Loop While value >= BytesPerUnitStep AndAlso unitIndex < SizeUnits.Length - 1

            ' 1.23 / 12.3 / 123: fewer decimals as the number grows.
            Dim decimalPlaces As Integer = If(value < 10, 2, If(value < 100, 1, 0))
            Dim scale As Double = Math.Pow(10, decimalPlaces)
            Dim cutOff As Double = Math.Floor(value * scale) / scale
            Return cutOff.ToString("N" & decimalPlaces.ToString()) & " " & SizeUnits(unitIndex)
        End Function

        Public ReadOnly Property ModifiedDisplay As String
            Get
                If _item.ModifiedTicks <= 0 Then Return String.Empty
                Const ShortDateAndTimeFormat As String = "g"
                Return New DateTime(_item.ModifiedTicks, DateTimeKind.Utc).ToLocalTime().ToString(ShortDateAndTimeFormat)
            End Get
        End Property

        ''' <summary>Switches to a different icon size (the Stage's view changed) and loads the picture at that size.</summary>
        Public Sub ChangeIconSize(newIconSize As Integer)
            If newIconSize = _iconSize Then Return
            _iconSize = newIconSize
            BeginLoadingPicture()
        End Sub

        ''' <summary>The latest snapshot of the folder entry this icon represents.</summary>
        Public ReadOnly Property Item As StageItem
            Get
                Return _item
            End Get
        End Property

        ''' <summary>Full path of the entry in the Stage folder (used to match items between refreshes).</summary>
        Public ReadOnly Property FullPath As String
            Get
                Return _item.FullPath
            End Get
        End Property

        ''' <summary>Label under the icon, with or without its extension (Settings > Items).</summary>
        Public ReadOnly Property DisplayName As String
            Get
                Return _displayName
            End Get
        End Property

        ''' <summary>
        ''' Re-works out the label (e.g. after the extension setting, or Explorer's, changed) and
        ''' updates the screen only if it differs.
        ''' </summary>
        Public Sub RefreshDisplayName()
            Dim currentLabel As String = ItemLabels.Compose(_item)
            If Not String.Equals(currentLabel, _displayName, StringComparison.Ordinal) Then
                _displayName = currentLabel
                RaisePropertyChanged(NameOf(DisplayName))
            End If
        End Sub

        ''' <summary>Loads the picture again - used after Windows' icons changed (see IconChangeWatcher).</summary>
        Public Sub ReloadPicture()
            BeginLoadingPicture()
        End Sub

        ''' <summary>True when the shortcut's target can't be found (the icon is shown dimmed).</summary>
        Public ReadOnly Property IsBroken As Boolean
            Get
                Return _item.IsBroken
            End Get
        End Property

        ''' <summary>Picture to draw. Nothing until the icon finishes loading in the background.</summary>
        Public Property Icon As ImageSource
            Get
                Return _icon
            End Get
            Private Set(value As ImageSource)
                If Not Object.Equals(_icon, value) Then
                    _icon = value
                    RaisePropertyChanged(NameOf(Icon))
                End If
            End Set
        End Property

        ''' <summary>
        ''' Takes a newer snapshot of the same entry (same path, same picture source), so
        ''' changes like "target went missing" show without reloading the icon.
        ''' </summary>
        Public Sub UpdateFrom(newerItem As StageItem)
            Dim wasBroken As Boolean = _item.IsBroken
            Dim detailsChanged As Boolean = Not String.Equals(_item.TypeName, newerItem.TypeName, StringComparison.Ordinal) OrElse
                                            _item.ModifiedTicks <> newerItem.ModifiedTicks OrElse
                                            _item.SizeBytes <> newerItem.SizeBytes
            _item = newerItem
            If wasBroken <> newerItem.IsBroken Then
                RaisePropertyChanged(NameOf(IsBroken))
            End If
            RefreshDisplayName() ' e.g. the shortcut now points at a different file type
            ' Details view columns can change too (e.g. the target file was edited).
            If detailsChanged Then
                RaisePropertyChanged(NameOf(TypeName))
                RaisePropertyChanged(NameOf(ModifiedDisplay))
                RaisePropertyChanged(NameOf(SizeDisplay))
            End If
        End Sub

        Private Sub RaisePropertyChanged(propertyName As String)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
        End Sub

        ''' <summary>
        ''' Loads the icon (then, for pictures, a thumbnail) in the background.
        ''' "Async Sub" = fire-and-forget; every error is caught inside so none can escape.
        ''' Because this starts on the UI thread, each Await resumes back on the UI thread,
        ''' so setting Icon here is safe.
        ''' </summary>
        Private Async Sub BeginLoadingPicture()
            ' A window's icon (Running tasks) arrives ready-made: nothing to load.
            If _item.PreloadedIcon IsNot Nothing Then
                _pictureLoadNumber += 1 ' any load still running for an older picture is now ignored
                Icon = _item.PreloadedIcon
                Return
            End If

            ' Normally the entry's own icon; a window without an icon of its own shows its program's.
            Dim entryPath As String = If(_item.IconSourcePath, _item.FullPath)
            Dim thumbnailSource As String = _item.ThumbnailSourcePath
            Dim requestedSize As Integer = _iconSize
            _pictureLoadNumber += 1
            Dim thisLoadNumber As Integer = _pictureLoadNumber
            Try
                ' The scan already read the entry's last-write time: passing it saves a disk
                ' access on the UI thread for every icon.
                Dim entryTicks As Long = _item.LastWriteTicks
                Dim shellIcon As ImageSource = Await IconProvider.Instance.GetIconAsync(entryPath, requestedSize, entryTicks)
                If thisLoadNumber <> _pictureLoadNumber Then Return ' a newer load has started
                If shellIcon IsNot Nothing Then
                    Icon = shellIcon
                End If

                If thumbnailSource IsNot Nothing Then
                    ' A picture in the folder is the entry itself (its time is known); a shortcut
                    ' to a picture points elsewhere, whose time IconProvider looks up itself.
                    Dim isEntryItself As Boolean = String.Equals(thumbnailSource, entryPath, StringComparison.OrdinalIgnoreCase)
                    Dim thumbnail As ImageSource = Await IconProvider.Instance.GetThumbnailAsync(
                        thumbnailSource, requestedSize, If(isEntryItself, entryTicks, 0L))
                    If thisLoadNumber <> _pictureLoadNumber Then Return
                    If thumbnail IsNot Nothing Then
                        Icon = thumbnail
                    End If
                End If
            Catch ex As Exception
                Logger.Warn("Could not load picture for " & entryPath, ex)
            End Try
        End Sub

    End Class

End Namespace
