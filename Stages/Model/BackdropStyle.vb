' ============================================================================
'  BackdropStyle.vb
'  What is shown behind a Stage's dark tint. Stored per Stage; will be offered
'  in the future settings menu (Mica, Windows 11 only, can be added here later).
' ============================================================================
Namespace Model

    Public Enum BackdropStyle
        ''' <summary>The desktop behind the Stage, blurred, under a dark tint (the NoFences look). Default.</summary>
        Blur
        ''' <summary>
        ''' An imitation of Windows acrylic: the blur plus a grey tint, a faint sheen and fine
        ''' noise drawn by Stages. Not "true" Windows acrylic, but looks very close and works
        ''' the same on Windows 10 and 11.
        ''' </summary>
        Acrylic
        ''' <summary>Only the semi-transparent tint, no blur. Works everywhere, even with Windows transparency effects off.</summary>
        Translucent
    End Enum

End Namespace
