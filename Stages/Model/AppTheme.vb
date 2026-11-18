' ============================================================================
'  AppTheme.vb
'  Light or dark colours for Stages' own windows and menus: the Settings
'  window, the Rename / Title size dialogs, the Stage menu, the tray menu and
'  the Explorer menu on an item. (The Stages themselves are styled by their
'  Background and Appearance settings instead.)
'  Chosen in Settings > Appearance > Theme; see Services\ThemeManager.vb.
' ============================================================================
Namespace Model

    Public Enum AppTheme
        ''' <summary>Follow Windows: Settings > Personalization > Colors > "Choose your default app mode".</summary>
        System
        ''' <summary>Always light, whatever Windows is set to.</summary>
        Light
        ''' <summary>Always dark, whatever Windows is set to. Default.</summary>
        Dark
    End Enum

End Namespace
