' ============================================================================
'  ShellIcons.vb
'  Gets the icon for a Stage item as a WPF image - WITHOUT the shortcut arrow.
'
'  Why shortcuts need special handling: for .lnk/.url files Windows bakes the
'  arrow into the icon it hands back, whatever flags are passed. So Stages
'  never asks for a shortcut's own icon. Instead, like Explorer would draw it
'  minus the arrow:
'    .lnk with a custom icon   -> that icon, loaded straight from its file
'    .lnk without one          -> the icon of the file/folder it points to
'    .lnk whose target is gone -> the generic icon for the target's file type
'    .url                      -> its own icon file if local, else the .html icon
'    anything else             -> its normal shell icon
'
'  Handle-leak fix (from the NoFences rewrite): every native icon is converted
'  to a frozen WPF bitmap and destroyed straight away; IconProvider caches the
'  results, so nothing is re-extracted on repaint.
'
'  Sizes: 16px and 32px icons come straight from SHGetFileInfo; 48px icons
'  (Large icons view) come from Windows' "extra large" system icon list, which
'  is what Explorer uses for its own Large icons view.
'
'  All methods use the shell, so call them on an STA thread (StaWorkerThread).
' ============================================================================
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Media.Imaging

Namespace Native

    Friend NotInheritable Class ShellIcons

        Private Const ShellLinkExtension As String = ".lnk"
        Private Const InternetShortcutExtension As String = ".url"
        ''' <summary>Stand-in "file name" whose generic icon represents web links (the default browser's page icon).</summary>
        Private Const WebPageStandInName As String = "page.html"
        Private Const NoFlags As UInteger = 0UI
        Private Const NoFileAttributes As UInteger = 0UI
        ''' <summary>Bits to shift the small size into the high word of SHDefExtractIcon's size argument.</summary>
        Private Const HighWordShift As Integer = 16

        ''' <summary>Windows' 48px icon list, fetched once (on the shell worker thread, the only thread that uses it).</summary>
        Private Shared _extraLargeIconList As IImageList

        Private Sub New()
        End Sub

        ''' <summary>
        ''' The 32x32 icon to show for a Stage item (see header for the rules).
        ''' Returns Nothing if no icon can be found. The image is frozen (thread-safe).
        ''' </summary>
        Public Shared Function GetItemIcon(itemPath As String, iconSize As Integer) As ImageSource
            Dim extension As String = Path.GetExtension(itemPath)

            If String.Equals(extension, ShellLinkExtension, StringComparison.OrdinalIgnoreCase) Then
                Dim shortcutIcon As ImageSource = GetShellLinkIconWithoutArrow(itemPath, iconSize)
                If shortcutIcon IsNot Nothing Then
                    Return shortcutIcon
                End If
            ElseIf String.Equals(extension, InternetShortcutExtension, StringComparison.OrdinalIgnoreCase) Then
                Return GetInternetShortcutIconWithoutArrow(itemPath, iconSize)
            End If

            ' Not a shortcut, or a shortcut to something with no file path (e.g. a Control
            ' Panel item) - the normal shell icon is the best available.
            Return GetShellIcon(itemPath, NoFileAttributes, NoFlags, iconSize)
        End Function

        ''' <summary>Icon for a .lnk: its custom icon, else its target's icon, else its target type's icon.</summary>
        Private Shared Function GetShellLinkIconWithoutArrow(shortcutPath As String, iconSize As Integer) As ImageSource
            Dim details As ShortcutDetails = ShellLinks.TryReadShortcut(shortcutPath)
            If details Is Nothing Then
                Return Nothing
            End If

            ' 1. A custom icon chosen in the shortcut's Properties
            If details.IconFilePath IsNot Nothing AndAlso File.Exists(details.IconFilePath) Then
                Dim customIcon As ImageSource = ExtractIconFromFile(details.IconFilePath, details.IconIndex, iconSize)
                If customIcon IsNot Nothing Then
                    Return customIcon
                End If
            End If

            If details.TargetPath Is Nothing Then
                Return Nothing ' no file target - caller falls back
            End If

            ' 2. The target's own icon (the target isn't a shortcut, so no arrow)
            If File.Exists(details.TargetPath) OrElse Directory.Exists(details.TargetPath) Then
                Return GetShellIcon(details.TargetPath, NoFileAttributes, NoFlags, iconSize)
            End If

            ' 3. Target is missing: the generic icon for its type (a folder if it has no extension)
            Dim looksLikeFolder As Boolean = String.IsNullOrEmpty(Path.GetExtension(details.TargetPath))
            Return GetGenericIcon(details.TargetPath, looksLikeFolder, iconSize)
        End Function

        ''' <summary>Icon for a .url: its own local icon file if it names one, else the web-page icon.</summary>
        Private Shared Function GetInternetShortcutIconWithoutArrow(urlFilePath As String, iconSize As Integer) As ImageSource
            Dim iconFilePath As String = Nothing
            Dim iconIndex As Integer = 0
            If ShellLinks.TryReadInternetShortcutIcon(urlFilePath, iconFilePath, iconIndex) Then
                Dim customIcon As ImageSource = ExtractIconFromFile(iconFilePath, iconIndex, iconSize)
                If customIcon IsNot Nothing Then
                    Return customIcon
                End If
            End If
            Return GetGenericIcon(WebPageStandInName, isFolder:=False, iconSize:=iconSize)
        End Function

        ''' <summary>
        ''' The icon Windows uses for this kind of item, without looking at any real file
        ''' (works for paths that don't exist).
        ''' </summary>
        Private Shared Function GetGenericIcon(nameOrPath As String, isFolder As Boolean, iconSize As Integer) As ImageSource
            Dim attributes As UInteger = If(isFolder, NativeMethods.FILE_ATTRIBUTE_DIRECTORY, NativeMethods.FILE_ATTRIBUTE_NORMAL)
            Return GetShellIcon(nameOrPath, attributes, NativeMethods.SHGFI_USEFILEATTRIBUTES, iconSize)
        End Function

        ''' <summary>
        ''' An item's shell icon at 16, 32 or 48 pixels (no overlays requested). 48px icons come
        ''' from Windows' extra-large icon list; if that fails, the 32px icon is used instead.
        ''' </summary>
        Private Shared Function GetShellIcon(itemPath As String, fileAttributes As UInteger, extraFlags As UInteger,
                                             iconSize As Integer) As ImageSource
            If iconSize > NativeMethods.LARGE_ICON_SIZE Then
                Dim extraLargeIcon As ImageSource = GetExtraLargeShellIcon(itemPath, fileAttributes, extraFlags)
                If extraLargeIcon IsNot Nothing Then Return extraLargeIcon
            End If

            Dim sizeFlag As UInteger = If(iconSize <= NativeMethods.SMALL_ICON_SIZE, NativeMethods.SHGFI_SMALLICON, NativeMethods.SHGFI_LARGEICON)
            Dim shellFileInfo As New NativeMethods.SHFILEINFO()
            Dim requestFlags As UInteger = NativeMethods.SHGFI_ICON Or sizeFlag Or extraFlags

            Dim callResult As IntPtr = NativeMethods.SHGetFileInfo(
                itemPath, fileAttributes, shellFileInfo,
                CUInt(Marshal.SizeOf(GetType(NativeMethods.SHFILEINFO))), requestFlags)

            If callResult = IntPtr.Zero Then
                Return Nothing
            End If
            Return ConvertAndDestroy(shellFileInfo.hIcon)
        End Function

        ''' <summary>
        ''' The 48px icon: asks which entry of Windows' system icon list belongs to the item,
        ''' then takes that entry from the extra-large list. Nothing if anything fails.
        ''' </summary>
        Private Shared Function GetExtraLargeShellIcon(itemPath As String, fileAttributes As UInteger, extraFlags As UInteger) As ImageSource
            Try
                Dim shellFileInfo As New NativeMethods.SHFILEINFO()
                Dim callResult As IntPtr = NativeMethods.SHGetFileInfo(
                    itemPath, fileAttributes, shellFileInfo,
                    CUInt(Marshal.SizeOf(GetType(NativeMethods.SHFILEINFO))), NativeMethods.SHGFI_SYSICONINDEX Or extraFlags)
                If callResult = IntPtr.Zero Then Return Nothing

                Dim iconList As IImageList = GetExtraLargeIconList()
                If iconList Is Nothing Then Return Nothing

                Dim iconHandle As IntPtr = IntPtr.Zero
                If iconList.GetIcon(shellFileInfo.iIcon, NativeMethods.ILD_TRANSPARENT, iconHandle) <> NativeMethods.S_OK Then
                    Return Nothing
                End If
                Return ConvertAndDestroy(iconHandle)
            Catch ex As Exception
                Return Nothing ' caller falls back to the 32px icon
            End Try
        End Function

        Private Shared Function GetExtraLargeIconList() As IImageList
            If _extraLargeIconList Is Nothing Then
                Dim interfaceId As Guid = NativeMethods.IID_IImageList
                Dim listPointer As IntPtr = IntPtr.Zero
                If NativeMethods.SHGetImageList(NativeMethods.SHIL_EXTRALARGE, interfaceId, listPointer) <> NativeMethods.S_OK OrElse
                   listPointer = IntPtr.Zero Then
                    Return Nothing
                End If
                Try
                    _extraLargeIconList = DirectCast(Marshal.GetObjectForIUnknown(listPointer), IImageList)
                Finally
                    Marshal.Release(listPointer) ' the .NET wrapper holds its own reference
                End Try
            End If
            Return _extraLargeIconList
        End Function

        ''' <summary>Loads icon number <paramref name="iconIndex"/> from an .ico/.exe/.dll file, at the size asked for.</summary>
        Private Shared Function ExtractIconFromFile(iconFilePath As String, iconIndex As Integer, iconSize As Integer) As ImageSource
            Dim largeIcon As IntPtr = IntPtr.Zero
            Dim smallIcon As IntPtr = IntPtr.Zero
            ' Low word = the size we want, high word = the "small" size (16, discarded below).
            Dim requestedSizes As UInteger = CUInt((NativeMethods.SMALL_ICON_SIZE << HighWordShift) Or iconSize)

            Dim result As Integer = NativeMethods.SHDefExtractIcon(iconFilePath, iconIndex, NoFlags, largeIcon, smallIcon, requestedSizes)

            ' We only use the large one - never leak the small one.
            If smallIcon <> IntPtr.Zero Then
                NativeMethods.DestroyIcon(smallIcon)
            End If
            If result <> NativeMethods.S_OK Then
                If largeIcon <> IntPtr.Zero Then NativeMethods.DestroyIcon(largeIcon)
                Return Nothing
            End If
            Return ConvertAndDestroy(largeIcon)
        End Function

        ''' <summary>
        ''' Copies a native icon into a frozen (read-only, thread-safe) WPF bitmap, then
        ''' destroys the native handle immediately - the leak fix. Nothing for a zero handle.
        ''' </summary>
        Private Shared Function ConvertAndDestroy(iconHandle As IntPtr) As ImageSource
            If iconHandle = IntPtr.Zero Then
                Return Nothing
            End If

            Try
                Dim iconBitmap As BitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions())
                iconBitmap.Freeze()
                Return iconBitmap
            Finally
                NativeMethods.DestroyIcon(iconHandle)
            End Try
        End Function

    End Class

End Namespace
