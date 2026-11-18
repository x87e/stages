# Stages user guide

How everything in Stages works, in detail. For an overview, see the [README](README.md); for a checklist of everything Stages does, see [FEATURES.md](FEATURES.md).

Stages for the Windows 10/11 desktop: semi-transparent panels that hold shortcuts to your files and folders.
Stages started as a VB.NET/WPF rewrite of [NoFences](https://github.com/Twometer/NoFences) by Twometer.

- **Language/UI:** VB.NET, WPF
- **Target:** .NET Framework 4.8 (built into Windows 10 1903+ and Windows 11)
- **Dependencies:** none. The tray icon and colour picker use Windows Forms and System.Drawing, which are part of .NET Framework.

## Building

1. Open `Stages.sln` in Visual Studio 2019 or 2022 (needs the **.NET desktop development** workload).
2. Build → Build Solution (Ctrl+Shift+B).
3. Output: `Stages\bin\Debug\Stages.exe` (or `bin\Release\`). To install it, run it and use **Settings → Install** (see Installing a portable copy below (see the [README](README.md#installing)).

Only one copy runs at a time. Starting Stages again while it's running opens its Settings window (and brings back hidden Stages). To quit, right-click the Stages icon in the notification area (system tray) and choose **Quit Stages**, or use **Quit Stages** in Settings → General.

## Tray icon and Settings

The Stages icon sits in the notification area, unless it's switched off in Settings → General → **Notification area** (see Without the tray icon below). Right-click it for:

- **New Stage**: a Standard Stage, a Live folder (asks for the folder), Running tasks or Clipboard history (see Kinds of Stage below).
- **Hide Stages** / **Show Stages**: takes every Stage off the desktop, or brings them back. They keep running while hidden. Stages always start visible.
- **Open Settings** (double-clicking the icon does the same)
- **Quit Stages**

The Settings window also opens from **Settings...** in any Stage's right-click menu. Changes apply to the Stages straight away and are saved automatically. Its pages:

- **General**: **Start Stages when I sign in to Windows**; **Show the Stages icon in the notification area** (on by default) and **Quit Stages** (a normal per-user startup entry, also shown in Windows' Startup apps list; Settings shows which Stages.exe it starts); the **Hide / show all Stages** keyboard shortcut (default **Ctrl+Alt+Shift+H**; click the box and press a new one, or choose "No shortcut"); the default background and default title bar height, used by every Stage that hasn't picked its own; **Title bar → Click the title bar to roll a Stage up** (off by default; see Title bar click below); and whether new Stages start minified. "Use on every Stage" makes Stages that picked their own value follow the default again.
- **Appearance**: the **Theme** (Same as Windows, Light or Dark) for the Settings window, the dialogs and every right-click menu; the Stage tint colour and strength; the imitation acrylic's tint, sheen and grain; **Stage text** (font, label size, title size or automatic, title and label colours); **Title and tabs** (see below); and **Stage frame** (title bar colour and strength, an outline's width, colour and strength, rounded or square corners). "Restore default appearance" puts all of these back (the Theme is left alone). Rounded corners need Windows 11: Windows 10 can't round a blurred window, so there Stages stay square.
- **Items**: **File name extensions** on item labels: Same as Windows (the default; follows File Explorer's View > Show > File name extensions, including its "known file types only" rule), Always show, or Always hide. For a shortcut, the extension is that of the file it opens. **Deleting from folder Stages** (Recycle Bin, Same as Windows, or permanently; see Live folders below). Also **Refresh icons now**.
- **Desktop**: **Arranging Stages** (snapping: beside other Stages, full or off; only snapping side by side; keeping Stages from overlapping; the gap between them, guide lines and their colour; see Lining Stages up below); why files on the desktop still show after being added to a Stage; and a **Hide desktop icons** switch (the same switch as the desktop's View menu).
- **Backup**: **Save a backup...** writes every Stage (shortcuts, tabs, layout, background picture) and the settings into one .zip file. **Import a backup...** replaces all current Stages and settings with a backup's, after a warning, then restarts Stages (see Backup and import below).
- **Install**: installs a portable copy (one run straight from where it was built or downloaded) as a normal Windows program; see Installing a portable copy below.
- **About**: version, and buttons to open the Stage folders, the logs and the settings file's folder.

### Without the tray icon

Settings → General → Notification area → **Show the Stages icon in the notification area** is on by default. Untick it for a tidier notification area: Stages keeps running exactly as before, just without the icon. Everything the tray menu offers is still reachable:

- **Settings**: right-click any Stage → **Settings...**, or start Stages again (Start menu, desktop icon or Stages.exe). A second copy never runs; it tells the running one to open Settings and show any hidden Stages, then leaves. That also gets you back if every Stage is hidden and the hide/show shortcut is off.
- **Quit**: **Quit Stages** in the same Settings card, or at the bottom of any Stage's menu (it's only there while the icon is hidden).
- **New Stage** and **Hide / show Stages**: any Stage's menu, and the hide/show shortcut.

### Installing a portable copy (Settings → Install)

Running `Stages.exe` straight from where you built or downloaded it works, but Windows doesn't know about it. **Settings → Install** ("Install Stages from portable version") makes it a normal Windows program. The page explains what happens in plain words, then offers three choices, all ticked to start with:

- **Add Stages to the Start menu**: a Stages shortcut in your Start menu.
- **Start Stages when I sign in to Windows**.
- **Remove this portable copy afterwards**: the file you ran goes to the Recycle Bin once the installed copy is running.

**Install** copies Stages into `%LOCALAPPDATA%\Programs\Stages\App` (the same place the installer uses), lists it in Windows Settings → Apps with an **Uninstall** button, then closes and opens again as the installed program, which says when it's done. Your Stages and settings aren't touched. Everything is just for your Windows account, so no administrator rights are needed and none are asked for. (Run as an administrator, it would install into the administrator's account instead of yours.)

If Stages is already installed, the page says which version, and installing replaces it. Run from the installed copy, the page simply says Stages is installed.

**Uninstalling** (Windows Settings → Apps → Stages → Uninstall) asks to confirm, then whether to remove your Stages and settings too (they go to the Recycle Bin; the default is to keep them). It closes Stages if it's running, removes the Start menu shortcut, the start-with-Windows entry and the Apps entry, and removes the program's folder a couple of seconds after it finishes. If Stages was installed with the installer, that installer's own uninstaller is used instead.

### Light and dark theme

Settings → Appearance → Theme: **Dark** (the default), **Light**, or **Same as Windows** (follows Windows' "default app mode"). It covers the Settings window, the Rename and Title size dialogs (including their title bars) and all three right-click menus: the Stage menu, the tray menu and the Explorer menu on an item. The Stages themselves keep their own Background and colour settings.

- All three menus are native Windows menus, so they look alike and switch together. Dark menus use an undocumented Windows function (also used by Notepad++ and others) that exists from Windows 10 1903. On older Windows, or if Microsoft ever removes it, menus simply stay light.
- Windows' own message boxes and the colour picker always use Windows' default (light) style.

### Hide / show shortcut

Press the shortcut anywhere (default **Ctrl+Alt+Shift+H**) to hide every Stage, and again to bring them back, the same as the tray menu's Hide Stages. It must include Ctrl, Alt or Win, so ordinary typing can't trigger it. If another program already owns the combination, Settings says so and the shortcut does nothing until you pick another.

### Backup and import (Settings → Backup)

A backup is an ordinary .zip file holding `settings.xml` and the `Data` folder, exactly as they are in `%LOCALAPPDATA%\Programs\Stages`. Logs and the program itself aren't included. A folder Stage's backup holds the Stage, not the files in the folder it shows.

Importing asks first (the default answer is No). It then checks and unpacks the zip into a temporary `Import-temp` folder, and restarts Stages. Before loading anything, the restarted Stages moves the current `Data` folder and `settings.xml` aside, moves the backup's into place, sends the old ones to the **Recycle Bin**, and deletes `Import-temp`. If a move fails, the old files are put back and nothing changes. A zip with only `settings.xml` or only `Data` replaces just that part. A zip made by hand from the Stages folder (with `Stages\Data\...` inside) works too.

## How Stages work

A Stage is a folder. Each Stage is shown on the desktop, and its contents are the shortcut files in its own folder.

- **Dropping** a file or folder onto a Stage creates a shortcut to it in the Stage's folder, and that shortcut is what the Stage shows. The original isn't moved, copied or changed.
  - Dropping an existing shortcut (`.lnk` / `.url`) copies that shortcut in, rather than making a shortcut to a shortcut.
  - Name clashes become `Name (2)`, `Name (3)` and so on.
- **Remove from Stage** (top of the right-click menu) sends the shortcut to the Recycle Bin, so it can be restored. The original file is never touched. Stages is not a file manager: to delete a real file, delete it where it lives.
- A shortcut whose target has been moved or deleted stays on the Stage, **dimmed**, until you remove it.
- Shortcut icons are shown **without the arrow overlay**. Stages uses the shortcut's custom icon if it has one, otherwise the target's icon, because Windows adds the arrow to any icon it hands back for a shortcut file.
- **Change icon...** (right-click a shortcut on a Stage) opens Windows' own Change Icon dialog and saves the chosen icon (from any .ico, .exe or .dll) into the shortcut itself, as Explorer's Properties does. **Reset icon** removes it again. A custom icon also replaces the picture preview of a shortcut to an image.
- Icons come from Windows, so custom file-type icons, custom folder icons and icon packs show as they do in Explorer. When Windows announces that icons changed, Stages reloads them; Settings → Items → **Refresh icons now** covers anything Windows doesn't announce.
- Changes made to a Stage's folder in Explorer show up on the Stage straight away.

### Views and arranging (Stage menu → View / Sort by)

- **View**: Large icons, Medium icons (the default), Small icons, List or Details (see Details view below).
- **Sort by**: Name, Type, Date modified, Size (folders always first, names sorted the way Explorer does, so "2" comes before "10"), or **Custom order**, which is whatever order you've dragged the items into. **Descending** reverses the sort.
- **Auto arrange icons** (on by default): icons line up in order. Switch it off and icons stay wherever you drop them, like the desktop; with **Align icons to grid** on, a dropped icon snaps to the nearest free spot on an invisible grid. Choosing a sort tidies hand-placed icons back into order, as on the desktop.
- **Selecting**: click; Ctrl+click and Shift+click add to the selection; drag across empty space to draw a selection box (hold Ctrl to add to the current selection); Ctrl+A selects everything.
- **Rearranging**: drag selected items to a new place. With Auto arrange on (and in List/Details), they move to where you drop them in the order, and the Stage switches to Custom order. A **locked** Stage can't be rearranged.
- **Opening**: double-click, or press Enter to open every selected item.
- Right-clicking several selected items offers **Open N items** and **Remove N items from Stage**.
- Each Stage (and each tab) remembers its own view, sort and arrangement.

### Details view

Details shows each item on a row with columns: **Name**, **Type**, **Date modified** and **Size**, by default in that order.

- **Sort**: click a column heading to sort by it; click it again to reverse.
- **Move a column**: drag its heading left or right and let go where you want it.
- **Show or hide columns**: right-click the headings (or Stage menu → View → **Details columns**) and tick or untick Type, Date modified and Size. Name is always shown, because it holds the icon and the name. **Reset columns** puts back the default set and order.
- **Size** is the size of the file (for a shortcut, the file it opens), written the way Windows does: 512 bytes, 12.3 KB, 4.56 GB. It's empty for folders (as in Explorer: adding up a folder means reading everything inside it), web links, open windows, broken shortcuts and shortcuts to files on a network drive (each one would cost a slow trip across the network). For sorting, items without a size count as the smallest.
- Type, Date modified and Size have fixed widths and Name takes the rest. On a narrow Stage the other columns shrink so Name stays readable.
- Each tab remembers its own columns, as it remembers its view and sort.

### Background picture (Stage menu → Background → Picture...)

Each Stage can show its own picture on top of its background. The dialog chooses the picture and how it fits (Fill, Fit or Stretch), its strength, a dark shade over it (keeps white labels readable on bright pictures) and a blur. Changes show on the Stage while the dialog is open; Cancel puts it back. The picture is **copied into the Stage's folder** as a hidden `.stage-background` file, so it stays even if the original is moved, and travels with the Stage. **Remove picture** deletes only that copy.

### Tabs (Stage menu → Tabs)

A Stage can hold several tabs, each with its own items, view and sort. With more than one tab, the title bar shows the tabs instead of the title: click a tab to open it, double-click it to rename it. The Tabs menu adds a tab (**New tab** → Standard tab..., Live folder tab..., Running tasks tab or Clipboard history tab), renames, moves left/right, **Move tab to its own Stage**, **Remove tab**, and **Add this Stage as a tab to** another Stage (for a Stage that has no tabs itself). The first tab is the Stage itself: its name is the Stage's name, and the window's position, size, lock, minify, background and picture belong to the whole Stage.

How the open tab is marked is chosen in Settings → Appearance → **Title and tabs**: **Underline** (a line in the tab colour under it), **Shaded** (a soft shaded tab resting on the bottom of the title bar), **Outlined** (like Windows' and Visual Studio's tabs: a 2-pixel outline in the tab colour round its top and sides, which carries on along the bottom of the title bar and round the rest of the Stage, so the open tab and the Stage form one outlined shape) or **Clear** (the default: no marking; it's just brighter). **Tab colour** sets the underline and outline colour. It starts as **Automatic**: an outline is the Stage's own colour (its tint, or the acrylic tint for imitation-acrylic Stages) 5% lighter, and an underline is light blue. Pick a colour to use that instead, or press **Automatic** to go back. The other tabs are always a little fainter. **Title position** (Left, Centre, the default, or Right) places each Stage's title, or its row of tabs.

On disk, a tab is a sub-folder of the Stage's folder with its own hidden settings file, so tabs can be made, renamed and removed in Explorer too (the Stage catches up within a few seconds).

### Lining Stages up (Settings → Desktop → Arranging Stages)

**Snapping** has three settings:

- **Beside other Stages** (the default): bring a Stage up next to another, level with it, and when its edge comes within 10 pixels it jumps to sit exactly the **Gap** away (2 pixels by default, 0 to 40), lined up flush with that Stage's top or bottom (or left or right, when one is above the other). Nothing else pulls at it, so away from other Stages it moves completely freely.
- **Full**: also lines up with the same edge of every Stage on screen, however far away, and with the screen's edges (the desktop without the taskbar).
- **Off**.

The same applies while resizing.

**Only snap side by side** (off by default): a Stage lines up only with Stages in the same row, never with ones above or below it. A minified Stage then counts as just its title bar, rather than its full open height, so you can line up a row right underneath a row of minified Stages; they open over it when you point at them. Without this, a Stage placed under a minified one is pulled to (or pushed below) the edge it will have when open, which you can't see.

**Keep Stages from overlapping** (on by default): while you drag, a Stage moves over others freely; let go of it on top of (or closer than the gap to) another, and it moves to the nearest free spot. While resizing, a dragged edge stops the gap away from the next Stage. A minified Stage counts at its full open height, so nothing gets covered when it rolls open (above its title bar for one that opens upwards; unless Only snap side by side is on).

**Show guide lines** (on by default): a coloured line (cyan by default; **Line colour** changes it) runs along each edge that's exactly in line with another Stage's (in Beside mode, only the Stage it sits beside).

Hold **Alt** while moving or resizing to place the Stage freely: no snapping, and it may overlap. Resting the pointer on a Stage's title bar shows this as a tip.

Distances are in pixels at 100% scaling and grow with the screen's scaling. Hidden Stages (Hide Stages) are left out.

### Kinds of Stage (Stage menu or tray menu → New Stage, Tabs → New tab)

Every Stage, and every tab, is one of four kinds. The kind is chosen when it's made and saved in its settings file. A Stage's tabs can be of different kinds.

| Kind | Shows | Opening an item | Options (Stage menu) |
|---|---|---|---|
| **Standard** | The shortcuts you drop on it (everything above) | Opens it | - |
| **Live folder** | The files and folders in a folder you choose (the old "folder Stage") | Opens it | **Live folder options...**: change the folder, look inside sub-folders (up to 5 levels), only show files named..., leave out files and folders named..., show folders as items or not, show at most 50-5000 items (1000 by default) |
| **Running tasks** | The open windows, roughly those the taskbar shows | Switches to the window (restoring it if minimised) | **Running tasks options...**: only show / leave out windows by title or program name, include minimised windows or not, update every 1-60 seconds (2 by default) |
| **Clipboard history** | What you copied recently: text, pictures and files | Puts it back on the clipboard | **Clipboard history options...**: keep text, pictures and/or files, keep at most 5-500 entries (50 by default) using at most 5-500 MB (50 MB by default); also **Pause clipboard history** and **Clear clipboard history...** |

Name filters (Live folder and Running tasks) are separated by `;` or `,`, for example `*.pdf; *.docx`. `*` stands for any characters and `?` for one. A name without `*` or `?` matches anything containing it (`report` matches "Q3 report.docx"). Case never matters.

**Running tasks**: right-click a window for **Switch to**, **Minimise** (or **Restore**) and **Close window** (as its own X button: the program may ask to save first). With several selected: **Minimise N windows** / **Close N windows**. Windows keep their places like taskbar buttons; new ones join at the end. Stages' own windows, tool windows, dialogs and windows on other virtual desktops aren't listed. Each window shows its own icon, or its program's. Nothing can be dropped on it or removed from it.

**Clipboard history**: each copy becomes an entry in the Stage's own folder, newest first, so the history survives a restart:

- text: a `.txt` file named after its first words;
- a picture (a screenshot, "Copy image" in a browser): a `.png` file, if pictures are switched on;
- copied files and folders: a **shortcut** to each (at most 10 per copy), never a copy of the file.

Copying something already in the history moves that entry back to the front instead of adding it twice. Over either limit, the oldest entries are deleted for good (they are Stages' own copies; a copied file itself is never touched). Content a program marks as private is never kept: password managers such as KeePass, Bitwarden and 1Password mark what they copy, the same marks Windows' own clipboard history (Win+V) respects. Text over 2 million characters isn't kept. Double-click (or Enter, or **Copy to clipboard** in its menu) puts an entry back on the clipboard: the text, the picture, or the file a shortcut points to. **Remove from Stage** sends an entry to the Recycle Bin. A Clipboard history tab keeps capturing while another tab is open, and while Stages are hidden.

### Live folders (Stage menu → New Stage → Live folder...)

A Live folder (called a folder Stage before v0.8; those carry on as Live folders with no sub-folders or filters) shows the contents of any folder on your PC (Downloads, a project folder, a drive) instead of shortcuts. Those are your real files, so on a folder Stage Stages never removes, renames or changes the icon of anything, and nothing can be dropped onto it. Explorer's own right-click menu still works on its items (Open, Open with, Copy, Properties and so on). Explorer's Rename isn't offered (it only works inside Explorer); use **Open folder** for that.

**Delete** is at the top of an item's right-click menu on a folder Stage (and **Delete N items** when several are selected). It deletes the real file, in the way chosen in Settings → Items → **Deleting from folder Stages**:

- **Move to the Recycle Bin (recommended, the default)**: Stages asks first, then sends the file to the Recycle Bin. If Windows can't recycle it (a network drive, or a drive whose Recycle Bin is switched off), Windows itself warns before deleting it for good.
- **Same as Windows**: behaves like Delete in File Explorer on this PC. Stages asks nothing; Windows asks only if its own delete confirmation is switched on, and follows the Recycle Bin's own settings.
- **Delete permanently**: Stages asks first (the highlighted answer is No), then deletes for good.

Ordinary Stages are not affected by this setting: **Remove from Stage** always sends only the shortcut to the Recycle Bin. Removing a folder Stage removes only the Stage, never the folder. If the folder isn't there (an unplugged drive), the Stage is empty until it comes back, keeping its arrangement.

Note for desktop files: a shortcut to a file on the desktop doesn't remove that file from the desktop. You can either move the file somewhere else (the shortcut follows it on NTFS drives), or hide desktop icons (Settings → Desktop, or right-click the desktop → View → Show desktop icons).

| Action | How |
|---|---|
| Add items | Drag files/folders onto a Stage |
| Open an item | Double-click, or select and press Enter |
| Select several | Ctrl/Shift+click, or drag a box across empty space |
| Rearrange | Drag selected items (not on a locked Stage) |
| View / Sort by | Stage menu: icon sizes, list, details (and which details columns show); name, type, date, size, custom; auto arrange, align to grid |
| Details columns | Right-click the column headings to show or hide Type, Date modified and Size; drag a heading to move its column |
| Tabs | Stage menu → Tabs; click a tab to open it, double-click to rename it |
| Hide / show all Stages | Ctrl+Alt+Shift+H (changeable in Settings → General), or the tray menu |
| Item menu | Right-click: Explorer's menu, with **Remove from Stage**, **Change icon...** and (if set) **Reset icon** at the top (Explorer's Delete/Rename are removed) |
| Stage menu | Right-click empty space or the title (Shift+right-click on an item also works) |
| Rename a Stage | Double-click its title and type (Enter keeps, Esc cancels, clicking elsewhere keeps), or Rename Stage... in its menu |
| Rename a shortcut | Right-click it → **Rename...** (renames the shortcut only, never the file it opens) |
| Move / resize | Drag the title to move. Drag any corner (the pointer turns into a diagonal arrow) or the left, right or bottom edge to resize. Not while locked or minified |
| Lock | Stops moving, resizing, dropping and rearranging |
| Title bar click | **Same as Settings** (the default), **Roll up and down** or **Do nothing**: what a single click on this Stage's title bar does. Rolled up, only the title bar shows, and it stays that way, even with the mouse over it, until the title bar is clicked again; it's remembered after a restart. Dragging the title bar still moves the Stage and double-clicking still renames it, so the roll waits for Windows' double-click time (about half a second) first. Clicking a tab never rolls the Stage. A rolled-up Stage can always be clicked down again |
| Minify | Collapses the Stage to its title bar until the mouse comes over it. It can roll open and shut instead of changing instantly: Settings → General → Minified Stages → **Animate opening and closing**, with a duration from 100 to 600 ms (off by default). If Windows' own animation effects are switched off, Stages don't animate either. **Open** (same card) sets which way it opens: **Downwards** (the default), **Upwards** (the title bar stays where it is and the Stage opens above it, its title bar at the bottom while open; then its top edge is the one to resize by), or **Automatic** (downwards unless that would run off the bottom of the screen, then upwards; if it fits neither way, towards the side with more room). This also applies to a Stage rolled up by clicking its title bar. Snapping and Keep Stages from overlapping allow for the way each minified Stage will open |
| Open Stage folder | Opens the open tab's folder in Explorer (**Open folder** on a Live folder: the folder it shows; not offered on Running tasks) |
| Background | **Default** (follows Settings → General), or this Stage's own choice: Blur, Acrylic (imitation), or Translucent (no blur). Needs Windows' *Transparency effects* on (Windows Settings → Personalization → Colors), or every option looks solid. **Picture...** adds a picture on top |
| New Stage | Standard Stage, Live folder... (a folder you choose), Running tasks or Clipboard history |
| Line Stages up | Move or resize a Stage near another: it snaps into line, keeps the gap, and shows guide lines (hold Alt to place it freely) |
| Title size | This Stage's title bar height. Choosing the same height as the default (or **Restore default**) makes the Stage follow the default again |
| Settings... | Opens the Settings window |

## Where things live

```
%LOCALAPPDATA%\Programs\Stages\
    App\      program files - application updates only ever touch this folder
    Data\     one folder per Stage, named after its title - never touched by updates
        My First Stage\
            .stage-settings.xml      (hidden) position, size, options, item order, view, layout
            .stage-settings.xml.bak  (hidden) previous save, used if the main file is damaged
            .stage-background.jpg    (hidden, optional) this Stage's own copy of its picture
            Some File.lnk
            Another tab\             a tab: a sub-folder with its own settings file
                .stage-settings.xml
                Other File.lnk
    Logs\     stages.log (startups, recoveries, every handled error)
    settings.xml           app-wide settings (Settings window) - never touched by updates
    settings.xml.bak       previous save, used if the main file is damaged
```

- Folder names are the Stage title with characters Windows doesn't allow in names replaced by `_`. The title itself is shown exactly as typed. Duplicate titles get `Name (2)` folders.
- **The title and folder stay in sync both ways:**
  - Renaming a Stage on the desktop renames its folder. If the folder is in use by another program, the title still changes, the folder keeps its old name, and you're told.
  - Renaming a Stage's folder in Explorer changes the Stage's title straight away. This also happens at the next start if the folder was renamed while Stages wasn't running.
  - Deleting a Stage's folder, or moving it out of `Data\`, closes that Stage.
  - Creating a new folder in `Data\` adds a new Stage named after it.
- Removing the last Stage quits Stages (start it again to get a fresh first Stage).
- While Stages are hidden (Hide Stages), they don't rescan their folders; they catch up when shown.
- Each Stage watches its folder for changes, and also rescans it every 30 seconds as a safety net (every 5 seconds if the folder can't be watched). Each Stage scans on its own background thread, so a slow or unreachable network folder only delays that one Stage.
- Stages are sharp on every monitor, including monitors with different scaling (per-monitor DPI).
- **Remove Stage** sends the whole folder to the Recycle Bin, tabs included. If the folder (or one of its tabs) contains anything other than shortcuts (real files someone put there), Stages refuses and explains, so it never deletes a real file. A folder Stage's own folder in `Data\` only holds its settings; the folder it shows is never touched.
- `Data\` can be backed up or copied to another PC as-is.

## Project layout

```
Stages.sln
Stages\
  Program.vb                 Entry point: single instance, global error logging, startup, tray icon
  Model\                     StageInfo (per-Stage settings), StageKind (Standard / Live folder /
                             Running tasks / Clipboard history), AppSettings (app-wide settings),
                             StageItem (one folder entry or window), StageDefaults (built-in defaults)
  ViewModels\                StageItemViewModel (what each icon binds to), StageTabHeader (one tab)
  Services\                  StageManager (load/save/rename/remove/recover/hide), StageFolder (folder rules,
                             shortcuts, Recycle Bin), AppSettingsStore, AtomicFile (safe saves),
                             ThemeManager (light/dark), ItemLabels (extensions), ShortcutIcons
                             (Change icon), IconChangeWatcher (live icon updates),
                             IconProvider, PictureLoader (background pictures), GlobalHotkey
                             (hide/show shortcut), StartupRegistration, Logger, AppPaths, StaWorkerThread,
                             StageSnapper (lining Stages up), NamePatterns (name filters),
                             RunningWindows (Running tasks), ClipboardMonitor and ClipboardHistoryStore
                             (Clipboard history)
  Native\                    Win32 interop: desktop gluing, blur, desktop icons, light/dark menus and
                             title bars, native popup menus, shell icons, shortcuts, Explorer menu
  Themes\                    StagesStyles.xaml (Stages), SettingsStyles.xaml (Settings window and dialogs),
                             LightColors.xaml / DarkColors.xaml (theme colours)
  Views\                     StageWindow (split into .xaml.vb, .Layout.vb, .Tabs.vb, .Appearance.vb,
                             .Snapping.vb, .Kinds.vb, .Resizing.vb, .TitleClick.vb, .DetailsColumns.vb), StageItemsPanel
                             (lays out the items), DetailsColumnsPanel (the Details columns),
                             SettingsWindow, StagesTrayIcon, SnapGuideOverlay (guide lines),
                             RenameDialog, TitleHeightDialog, BackgroundPictureDialog,
                             StageKindOptionsDialog (Live folder / Running tasks / Clipboard options)
  Resources\                 Stages.ico (app and tray icon), StagesLogo.png (About page)
```

## Fixes relative to NoFences 1.8

The main ones, which made fences disappear or the app stop:
1. **Explorer restarts killed the app.** Windows destroys the windows Explorer's desktop owns when it restarts, and NoFences then exited. Stages re-creates them, re-attaches survivors on `TaskbarCreated`, and runs a watchdog every 5 seconds.
2. **GDI handle leak.** Icons, brushes and fonts were created on every mouse-move repaint and never released, until Windows' handle limit was hit. Stages uses WPF rendering, and each icon is extracted once and released immediately.
3. **A damaged settings file stopped all fences loading.** Saves are now atomic with a `.bak`, and an unreadable Stage is skipped and logged instead of crashing.
4. **Save errors crashed the app.** They're caught, logged and retried.
5. **No global error handling.** UI errors are now logged and survived.
6. **Thumbnail loader could jam permanently** after 4 bad images.
7. **Alt+F4 closed a fence.** It's refused now.
8. **Fences could end up off-screen** after a monitor change. They move back on load.

Smaller fixes:
- Drops report "Link" instead of "Move", so drag sources never delete the original.
- Shell calls run on a background STA thread, so a slow network drive doesn't freeze the UI.
- Proper 64-bit `GetWindowLongPtr` calls.
- Thumbnails: case-insensitive extension check and correct aspect ratio.
- The Explorer menu works on drive roots.
- Labels fixed for folders with dots and for dot-files.
- The Stage title can't be empty.
- NoFences only stored file paths, so "deleting" an item from its menu deleted the real file, and a new file created at the same path reappeared in the fence. Both are gone with shortcut-based Stages.

## 32-bit and 64-bit programs

Stages runs as a 64-bit program. Shortcuts made by 32-bit installers often store `%ProgramFiles%\...`, which a 64-bit program reads as `C:\Program Files` even when the program is in `C:\Program Files (x86)`. When a shortcut's target seems missing, Stages also checks the exact location saved inside the shortcut and the `(x86)` folders, as Explorer does, so such shortcuts show their real icon instead of looking broken. Opening shortcuts was never affected: Windows resolves them itself.

## Long paths (over 260 characters)

Stages doesn't switch on Windows' long-path support, deliberately. It needs an admin change to a PC-wide registry setting, and even then File Explorer and many programs (Office included) still can't open such files, so a shortcut to one would fail anyway. Stages keeps its own paths short: Stage folder and shortcut names are capped at 80 characters.

## Known differences from NoFences

- Stage windows are normal (non-layered) windows, and Windows' compositor supplies the blur, acrylic or translucency. This works on both Windows 10 and 11, whereas WPF's `AllowsTransparency` windows render blur as solid black on Windows 11.
- Mica isn't offered yet. It's Windows 11-only, uses a different API, and probably only draws while a window is active (which a Stage almost never is), so it needs testing first.
- NoFences data isn't imported. Stages uses its own folder-based format.

## Licence

Stages is based on [NoFences](https://github.com/Twometer/NoFences) by Twometer, released under the MIT licence. Stages is released under the MIT licence too; see [LICENSE](LICENSE), which keeps NoFences' original copyright notice.
