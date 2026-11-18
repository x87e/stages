' ============================================================================
'  IconProvider.vb
'  Supplies the picture shown for each Stage item:
'    * the normal Explorer icon (for a shortcut: its custom icon, or its
'      target's icon - WITHOUT the shortcut arrow; see ShellIcons), and
'    * a small preview thumbnail for pictures (bmp/gif/jpg/png/tif).
'
'  Both are cached. Cache keys include the file's last-write time, so editing
'  a shortcut (e.g. changing its icon) or an image produces a fresh picture.
'
'  Fixes carried over from the NoFences ThumbnailProvider rewrite:
'    * a failed decode can never jam the loader (semaphore released in Finally)
'    * no native icon handles leak (WPF bitmaps need none)
'    * extension check is case-insensitive; aspect ratio is kept
' ============================================================================
Imports System.Collections.Concurrent
Imports System.IO
Imports System.Threading
Imports System.Windows.Media.Imaging
Imports Stages.Native

Namespace Services

    Public NotInheritable Class IconProvider

        ''' <summary>Single shared instance - one cache for all Stages.</summary>
        Public Shared ReadOnly Property Instance As IconProvider = New IconProvider()

        ''' <summary>Image types WPF can decode out of the box.</summary>
        Private Shared ReadOnly ThumbnailFileExtensions As String() =
            {".bmp", ".gif", ".jpg", ".jpeg", ".png", ".tiff", ".tif"}

        ''' <summary>Thumbnails are decoded at this many times the icon size, so they stay crisp on high-DPI screens.</summary>
        Private Const ThumbnailDecodeScale As Integer = 2
        ''' <summary>Limit parallel decodes so a set of huge photos can't exhaust memory.</summary>
        Private Const MaximumConcurrentThumbnailDecodes As Integer = 2
        ''' <summary>Images bigger than this keep their normal icon instead of a thumbnail.</summary>
        Private Const MaximumThumbnailSourceBytes As Long = 64L * 1024L * 1024L
        ''' <summary>
        ''' Cache limits. When one is reached that cache is emptied and refills as Stages need
        ''' pictures again; without a limit, pictures of changed or deleted files would pile up
        ''' for as long as Stages runs. A 48px icon is about 9 KB, so 3,000 is about 27 MB;
        ''' a thumbnail can be 128 px across (about 64 KB), so 500 is about 32 MB.
        ''' </summary>
        Private Const MaximumCachedIcons As Integer = 3000
        Private Const MaximumCachedThumbnails As Integer = 500

        ''' <summary>"path|last-write-ticks|size" -> shell icon.</summary>
        Private ReadOnly _iconCache As New ConcurrentDictionary(Of String, ImageSource)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>"path|last-write-ticks|size" -> thumbnail.</summary>
        Private ReadOnly _thumbnailCache As New ConcurrentDictionary(Of String, ImageSource)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _thumbnailDecodeSlots As New SemaphoreSlim(MaximumConcurrentThumbnailDecodes)

        Private Sub New()
        End Sub

        ''' <summary>True if Stages can show a picture preview for this file type.</summary>
        Public Shared Function SupportsThumbnail(itemPath As String) As Boolean
            Dim extension As String = Path.GetExtension(itemPath)
            Return ThumbnailFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Gets the Explorer icon for an item (no overlays). Cached; the work runs on the
        ''' shell worker thread, never the UI thread. Completes with Nothing if no icon exists.
        ''' <paramref name="knownLastWriteTicks"/>: the item's last-write time if the caller
        ''' already has it (from the folder scan), so the UI thread needn't ask the disk; 0 = unknown.
        ''' </summary>
        Public Function GetIconAsync(itemPath As String, iconSize As Integer, Optional knownLastWriteTicks As Long = 0) As Task(Of ImageSource)
            Dim cachedIcon As ImageSource = Nothing
            If knownLastWriteTicks > 0 Then
                Dim knownKey As String = MakeCacheKey(itemPath, knownLastWriteTicks, iconSize)
                If _iconCache.TryGetValue(knownKey, cachedIcon) Then
                    Return Task.FromResult(cachedIcon)
                End If
            End If

            Return StaWorkerThread.ShellOperations.RunAsync(
                Function()
                    ' Any disk access for the key happens here, off the UI thread.
                    Dim cacheKey As String = If(knownLastWriteTicks > 0,
                                                MakeCacheKey(itemPath, knownLastWriteTicks, iconSize),
                                                BuildCacheKey(itemPath, iconSize))
                    Dim alreadyLoaded As ImageSource = Nothing
                    If cacheKey IsNot Nothing AndAlso _iconCache.TryGetValue(cacheKey, alreadyLoaded) Then
                        Return alreadyLoaded
                    End If

                    Dim loadedIcon As ImageSource = ShellIcons.GetItemIcon(itemPath, iconSize)
                    If loadedIcon IsNot Nothing AndAlso cacheKey IsNot Nothing Then
                        If _iconCache.Count >= MaximumCachedIcons Then _iconCache.Clear()
                        _iconCache(cacheKey) = loadedIcon
                    End If
                    Return loadedIcon
                End Function)
        End Function

        ''' <summary>
        ''' Forgets every loaded icon, so the next request asks Windows again. Used when Windows'
        ''' icons change (IconChangeWatcher). Picture thumbnails are kept: they don't depend on
        ''' Windows' icons.
        ''' </summary>
        Public Sub ForgetIcons()
            _iconCache.Clear()
        End Sub

        ''' <summary>
        ''' Gets a small preview of an image file. Completes with Nothing if the file can't
        ''' be decoded - callers then simply keep the normal icon.
        ''' </summary>
        Public Async Function GetThumbnailAsync(imagePath As String, iconSize As Integer, Optional knownLastWriteTicks As Long = 0) As Task(Of ImageSource)
            ' Known time: no disk access. Unknown (a shortcut's target): ask the disk off the UI thread.
            Dim cacheKey As String
            If knownLastWriteTicks > 0 Then
                cacheKey = MakeCacheKey(imagePath, knownLastWriteTicks, iconSize)
            Else
                cacheKey = Await Task.Run(Function() BuildCacheKey(imagePath, iconSize))
            End If
            If cacheKey Is Nothing Then
                Return Nothing
            End If

            Dim cachedThumbnail As ImageSource = Nothing
            If _thumbnailCache.TryGetValue(cacheKey, cachedThumbnail) Then
                Return cachedThumbnail
            End If

            Await _thumbnailDecodeSlots.WaitAsync()
            Try
                Dim decodeWidth As Integer = iconSize * ThumbnailDecodeScale
                Dim decodedThumbnail As ImageSource = Await Task.Run(Function() DecodeThumbnail(imagePath, decodeWidth))
                If decodedThumbnail IsNot Nothing Then
                    If _thumbnailCache.Count >= MaximumCachedThumbnails Then _thumbnailCache.Clear()
                    _thumbnailCache(cacheKey) = decodedThumbnail
                End If
                Return decodedThumbnail
            Finally
                ' Always give the slot back, even if decoding failed.
                _thumbnailDecodeSlots.Release()
            End Try
        End Function

        ''' <summary>"path|ticks|size" so a modified file, or a new size, gets a fresh picture. Nothing if the path is gone.</summary>
        Private Shared Function BuildCacheKey(itemPath As String, iconSize As Integer) As String
            Try
                Dim lastWrite As DateTime
                If Directory.Exists(itemPath) Then
                    lastWrite = Directory.GetLastWriteTimeUtc(itemPath)
                Else
                    lastWrite = File.GetLastWriteTimeUtc(itemPath)
                End If
                Return MakeCacheKey(itemPath, lastWrite.Ticks, iconSize)
            Catch ex As Exception
                Return Nothing
            End Try
        End Function

        ''' <summary>The cache key for a path, its last-write time and a size.</summary>
        Private Shared Function MakeCacheKey(itemPath As String, lastWriteTicks As Long, iconSize As Integer) As String
            Return itemPath & "|" & lastWriteTicks.ToString() & "|" & iconSize.ToString()
        End Function

        ''' <summary>Reads and decodes an image into a small, frozen (thread-safe) WPF bitmap.</summary>
        Private Shared Function DecodeThumbnail(imagePath As String, decodeWidth As Integer) As ImageSource
            Try
                Dim sourceFile As New FileInfo(imagePath)
                If Not sourceFile.Exists OrElse sourceFile.Length > MaximumThumbnailSourceBytes Then
                    Return Nothing
                End If

                ' Read the bytes up-front so the file is never left locked by the decoder.
                Dim imageBytes As Byte() = File.ReadAllBytes(imagePath)
                Using imageStream As New MemoryStream(imageBytes)
                    Dim thumbnail As New BitmapImage()
                    thumbnail.BeginInit()
                    thumbnail.CacheOption = BitmapCacheOption.OnLoad          ' decode now, not lazily
                    thumbnail.CreateOptions = BitmapCreateOptions.IgnoreColorProfile
                    thumbnail.DecodePixelWidth = decodeWidth                  ' height follows aspect ratio
                    thumbnail.StreamSource = imageStream
                    thumbnail.EndInit()
                    thumbnail.Freeze()
                    Return thumbnail
                End Using
            Catch ex As Exception
                ' Corrupt / unsupported / locked image - not an error worth shouting about.
                Logger.Warn("Could not create thumbnail for " & imagePath & ": " & ex.Message)
                Return Nothing
            End Try
        End Function

    End Class

End Namespace
