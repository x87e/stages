' ============================================================================
'  PictureLoader.vb
'  Loads a picture file (a Stage's background picture) into memory, ready for
'  WPF to draw, without keeping the file open or locked.
'
'  Big photos are shrunk while they're decoded (to at most MaximumWidth
'  pixels wide), so a 50-megapixel camera picture doesn't cost hundreds of MB
'  for a Stage a few hundred pixels across. The result is "frozen" so it can
'  be loaded on a background thread and then used on the UI thread.
' ============================================================================
Imports System.IO
Imports System.Windows.Media.Imaging

Namespace Services

    Public NotInheritable Class PictureLoader

        ''' <summary>Pictures wider than this are decoded at this width (keeping their shape).</summary>
        Public Const MaximumWidth As Integer = 2560

        ''' <summary>The file types offered in the picture dialog (all decoded by Windows' own codecs).</summary>
        Public Const PictureFileFilter As String =
            "Pictures (*.jpg; *.jpeg; *.png; *.bmp; *.gif; *.tif; *.tiff; *.webp)|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files (*.*)|*.*"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Reads and decodes a picture file. Returns Nothing (and logs why) if it can't be read
        ''' or isn't a picture Windows can decode. Safe to call on any thread.
        ''' </summary>
        Public Shared Function Load(pictureFilePath As String) As BitmapSource
            Try
                Using pictureStream As FileStream = File.OpenRead(pictureFilePath)
                    ' Find the picture's width without decoding it (DelayCreation).
                    Dim decoder As BitmapDecoder = BitmapDecoder.Create(pictureStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None)
                    Dim originalWidth As Integer = decoder.Frames(0).PixelWidth

                    ' Decode it for real, shrunk if it's very wide. OnLoad reads everything now,
                    ' so the file can be closed (and replaced or deleted) straight afterwards.
                    pictureStream.Position = 0
                    Dim picture As New BitmapImage()
                    picture.BeginInit()
                    picture.CacheOption = BitmapCacheOption.OnLoad
                    picture.StreamSource = pictureStream
                    If originalWidth > MaximumWidth Then
                        picture.DecodePixelWidth = MaximumWidth
                    End If
                    picture.EndInit()
                    picture.Freeze() ' usable from any thread from now on
                    Return picture
                End Using
            Catch ex As Exception
                ' Unreadable, not a picture, or a format this PC has no codec for (e.g. some WebP).
                Logger.Warn("Could not load the picture " & pictureFilePath, ex)
                Return Nothing
            End Try
        End Function

    End Class

End Namespace
