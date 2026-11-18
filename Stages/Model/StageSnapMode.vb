' ============================================================================
'  StageSnapMode.vb
'  How much a Stage being moved or resized lines itself up with others
'  (Settings > Desktop > Arranging Stages > Snapping). Added in v0.8.2, when
'  snapping to everything on screen turned out to get in the way of simply
'  placing a Stage where you want it.
' ============================================================================
Namespace Model

    Public Enum StageSnapMode
        ''' <summary>No snapping at all (the gap is still kept, if that's switched on).</summary>
        Off = 0
        ''' <summary>
        ''' The default. Snaps only when the Stage is brought up beside another one, level with
        ''' it: it then sits the gap away and lines up flush with THAT Stage's top or bottom
        ''' (or left or right). Distant Stages and the screen's edges don't pull at it.
        ''' </summary>
        BesideStages = 1
        ''' <summary>
        ''' As v0.8.0: also lines up with the same edge of any Stage anywhere on screen, and
        ''' with the screen's edges.
        ''' </summary>
        Full = 2
    End Enum

End Namespace
