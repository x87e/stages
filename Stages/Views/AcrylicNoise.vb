' ============================================================================
'  AcrylicNoise.vb
'  The fine "grain" texture that makes Stages' imitation acrylic look like
'  Windows acrylic (Background > Acrylic).
'
'  Why imitate? Windows 11 no longer reliably supports the old acrylic API
'  (it renders black), and its official replacement only draws while a window
'  is active - a Stage almost never is. Real acrylic is essentially:
'      blur  +  tint  +  faint noise
'  Windows' blur works on both Windows 10 and 11, so Stages asks for the blur
'  and draws the tint and this noise itself (see StageWindow.ApplyBackdropNow).
'
'  The texture is generated once in code - no image file to ship - from a
'  fixed random seed, so it looks identical every time Stages starts.
' ============================================================================
Imports System.Windows.Media.Imaging

Public NotInheritable Class AcrylicNoise

    ''' <summary>Width/height of the repeating noise tile, in pixels.</summary>
    Private Const TileSizePixels As Integer = 128
    ''' <summary>Fixed seed: the same "random" grain on every run.</summary>
    Private Const NoiseSeed As Integer = 20261110
    ''' <summary>Blue, green, red, alpha = 4 bytes per pixel (Bgra32 format).</summary>
    Private Const BytesPerPixel As Integer = 4
    Private Const FullyOpaque As Byte = 255
    ''' <summary>Exclusive upper bound for a random grey level (0-255).</summary>
    Private Const GreyLevelCount As Integer = 256
    Private Const StandardDpi As Double = 96

    ''' <summary>Built on first use, then shared by every Stage (it's frozen, so that's safe).</summary>
    Private Shared _tiledBrush As ImageBrush

    Private Sub New()
    End Sub

    ''' <summary>
    ''' A brush that tiles the grey noise across any area. Shown at low opacity
    ''' (the Grain strength in Settings > Appearance) it gives acrylic's subtle grain.
    ''' </summary>
    Public Shared ReadOnly Property TiledBrush As ImageBrush
        Get
            If _tiledBrush Is Nothing Then
                _tiledBrush = CreateTiledBrush()
            End If
            Return _tiledBrush
        End Get
    End Property

    Private Shared Function CreateTiledBrush() As ImageBrush
        ' One opaque grey pixel of random brightness after another.
        Dim pixelCount As Integer = TileSizePixels * TileSizePixels
        Dim pixelBytes(pixelCount * BytesPerPixel - 1) As Byte
        Dim noiseGenerator As New Random(NoiseSeed)

        For pixelIndex As Integer = 0 To pixelCount - 1
            Dim greyLevel As Byte = CByte(noiseGenerator.Next(GreyLevelCount))
            Dim byteOffset As Integer = pixelIndex * BytesPerPixel
            pixelBytes(byteOffset) = greyLevel         ' blue
            pixelBytes(byteOffset + 1) = greyLevel     ' green
            pixelBytes(byteOffset + 2) = greyLevel     ' red
            pixelBytes(byteOffset + 3) = FullyOpaque   ' alpha
        Next

        Dim bytesPerRow As Integer = TileSizePixels * BytesPerPixel
        Dim noiseBitmap As BitmapSource = BitmapSource.Create(
            TileSizePixels, TileSizePixels, StandardDpi, StandardDpi,
            PixelFormats.Bgra32, Nothing, pixelBytes, bytesPerRow)
        noiseBitmap.Freeze()

        ' Repeat the tile at its natural size rather than stretching one copy.
        Dim noiseBrush As New ImageBrush(noiseBitmap) With {
            .TileMode = TileMode.Tile,
            .Stretch = Stretch.None,
            .ViewportUnits = BrushMappingMode.Absolute,
            .Viewport = New Rect(0, 0, TileSizePixels, TileSizePixels)
        }
        noiseBrush.Freeze()
        Return noiseBrush
    End Function

End Class
