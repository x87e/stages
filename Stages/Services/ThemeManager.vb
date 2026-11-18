' ============================================================================
'  ThemeManager.vb
'  Applies the light or dark theme (Settings > Appearance > Theme) to:
'    * the Settings window and the Rename / Title size dialogs - by swapping
'      which colour dictionary (Themes\LightColors.xaml or DarkColors.xaml) is
'      loaded into the application's resources. Their styles refer to those
'      colours with {DynamicResource ...}, so they repaint immediately;
'    * their title bars - through DWM (DesktopIntegration.SetDarkTitleBar);
'    * every popup menu: Stage menu, tray menu and the Explorer menu on an
'      item (DesktopIntegration.SetMenusDark).
'  The Stages themselves are not affected: they have their own Background and
'  Appearance settings.
'
'  With Theme = System, Stages follows Windows' "default app mode" and updates
'  when it changes (StageWindow passes on WM_SETTINGCHANGE).
' ============================================================================
Imports System.Windows.Interop
Imports Stages.Model
Imports Stages.Native

Namespace Services

    Public NotInheritable Class ThemeManager

        ''' <summary>The two colour dictionaries. They define the same keys with different colours.</summary>
        Private Const LightColorsUri As String = "pack://application:,,,/Themes/LightColors.xaml"
        Private Const DarkColorsUri As String = "pack://application:,,,/Themes/DarkColors.xaml"

        ''' <summary>The colour dictionary currently merged into the application's resources.</summary>
        Private Shared _loadedColors As ResourceDictionary
        Private Shared _isDark As Boolean

        Private Sub New()
        End Sub

        ''' <summary>True if dark colours are in use right now.</summary>
        Public Shared ReadOnly Property IsDark As Boolean
            Get
                Return _isDark
            End Get
        End Property

        ''' <summary>
        ''' Applies the theme from the current settings everywhere. Call at startup (before
        ''' any themed window opens) and whenever the Theme setting changes.
        ''' </summary>
        Public Shared Sub Apply()
            _isDark = ResolveIsDark(AppSettingsStore.Current.Theme)

            LoadColorDictionary(_isDark)
            DesktopIntegration.SetMenusDark(_isDark)

            ' Re-colour the title bars of themed windows that are already open.
            ' (Stage windows have no title bar, so they are skipped.)
            Dim currentApplication As Application = Application.Current
            If currentApplication IsNot Nothing Then
                For Each openWindow As Window In currentApplication.Windows
                    If Not TypeOf openWindow Is StageWindow Then
                        ApplyTitleBar(openWindow)
                    End If
                Next
            End If

            Logger.Info("Theme: " & AppSettingsStore.Current.Theme.ToString() & " (" & If(_isDark, "dark", "light") & ")")
        End Sub

        ''' <summary>
        ''' Windows reported a settings change (WM_SETTINGCHANGE). If Stages follows Windows'
        ''' app mode and that has flipped, re-apply. Cheap when nothing changed, so it's fine
        ''' that every Stage window passes the message on.
        ''' </summary>
        Public Shared Sub OnWindowsSettingsChanged()
            If AppSettingsStore.Current.Theme <> AppTheme.System Then Return
            If ResolveIsDark(AppTheme.System) = _isDark Then Return
            Apply()
        End Sub

        ''' <summary>
        ''' Gives a window a title bar matching the theme. Themed windows call this from
        ''' OnSourceInitialized, the first moment their native window exists.
        ''' </summary>
        Public Shared Sub ApplyTitleBar(targetWindow As Window)
            Dim windowHandle As IntPtr = New WindowInteropHelper(targetWindow).Handle
            DesktopIntegration.SetDarkTitleBar(windowHandle, _isDark)
        End Sub

        ''' <summary>Light or dark for a Theme setting (System asks Windows).</summary>
        Private Shared Function ResolveIsDark(chosenTheme As AppTheme) As Boolean
            Select Case chosenTheme
                Case AppTheme.Dark
                    Return True
                Case AppTheme.Light
                    Return False
                Case Else
                    Return DesktopIntegration.IsWindowsAppModeDark()
            End Select
        End Function

        ''' <summary>Replaces the loaded colour dictionary with the light or dark one.</summary>
        Private Shared Sub LoadColorDictionary(useDark As Boolean)
            Dim currentApplication As Application = Application.Current
            If currentApplication Is Nothing Then Return

            Dim newColors As New ResourceDictionary() With {
                .Source = New Uri(If(useDark, DarkColorsUri, LightColorsUri))
            }
            Dim appResources As ResourceDictionary = currentApplication.Resources
            If _loadedColors IsNot Nothing Then
                appResources.MergedDictionaries.Remove(_loadedColors)
            End If
            appResources.MergedDictionaries.Add(newColors)
            _loadedColors = newColors
        End Sub

    End Class

End Namespace
