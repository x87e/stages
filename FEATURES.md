# Stages features

Everything Stages does, version 0.8.5. The [user guide](USER-GUIDE.md) explains each one in detail.

## Stages on the desktop

- Semi-transparent panels that sit on the desktop, behind your windows, on every monitor.
- Each Stage is an ordinary folder in `%LOCALAPPDATA%\Programs\Stages\Data`, so it can be browsed, backed up or copied to another PC as it is.
- The title and the folder stay in step both ways: rename one and the other follows, even if Stages wasn't running at the time.
- Changes made to a Stage's folder in Explorer show on the Stage straight away.
- Create Stages from the tray icon or any Stage's menu. Creating a folder in `Data` in Explorer also adds a Stage.
- **Remove Stage** sends the Stage to the Recycle Bin, and refuses if its folder holds anything other than shortcuts, so a real file is never deleted.

## Four kinds of Stage

| Kind | What it shows |
|---|---|
| **Standard** | Shortcuts to the files and folders you drop on it. |
| **Live folder** | The real contents of any folder, such as Downloads, a project or a drive. Optionally includes sub-folders (up to 5 levels), and can show or leave out files by name, show or hide folders, and cap the number of items. |
| **Running tasks** | Your open windows, roughly as the taskbar shows them. Click one to switch to it, and minimise, restore or close windows from the Stage. Filter by title or program, and choose how often it updates. |
| **Clipboard history** | Text, pictures and files you copied, newest first, kept across restarts. Double-click one to copy it again. Skips anything a password manager marks as private. You can set size and count limits, pause it, or clear it. |

## Tabs

- One Stage can hold several tabs, each of any kind and each with its own items, view and sort.
- Click a tab to open it and double-click to rename it. You can add, reorder and remove tabs, move a tab to its own Stage, or add a Stage to another as a tab.
- On disk, a tab is a sub-folder, so tabs can be made and renamed in Explorer too.

## Items

- **Dropping never moves your files.** A dropped file or folder becomes a shortcut, and a dropped shortcut is copied in. Name clashes become `Name (2)`.
- **Remove from Stage** sends only the shortcut to the Recycle Bin. The original is never touched.
- **Explorer's own right-click menu** on every item: Open with, Copy, Properties and the rest.
- Real icons, including custom folder icons and icon packs, with **no shortcut arrows**. Picture previews for images.
- **Change icon...** using Windows' own dialog, and **Reset icon**.
- Rename a shortcut without touching the file it opens.
- Shortcuts whose target has gone are shown dimmed rather than disappearing.
- 32-bit program shortcuts are resolved the way Explorer does, so they show their real icons.
- File name extensions: same as Windows, always shown or always hidden.
- On Live folders, **Delete** removes real files your way: to the Recycle Bin (the default), the same as Explorer, or permanently. Stages asks first.

## Views and arranging

- **Views**: large, medium or small icons, list, or details.
- **Details columns**: name, type, date modified and size. Click a heading to sort, drag a heading to move its column, and right-click the headings to show or hide columns.
- **Sort by**: name (in natural order, so 2 comes before 10), type, date modified or size, ascending or descending, or a **custom order** you set by dragging.
- **Auto arrange**, or place icons anywhere by hand, with optional **align to grid**.
- Select like Explorer: click, Ctrl+click, Shift+click, drag a selection box, or press Ctrl+A. Enter opens every selected item.
- Every Stage and every tab remembers its own view, sort and arrangement.

## Moving, sizing and lining up

- Drag the title bar to move a Stage. Drag any corner or the left, right or bottom edge to resize it.
- **Snapping**, three ways:
  - **Beside other Stages** (the default): a Stage brought up next to another sits an even gap away, lined up with it, and moves completely freely everywhere else.
  - **Full**: also lines up with every Stage on screen and with the screen edges.
  - **Off**.
- **Only snap side by side**: ignore Stages above and below, and count minified Stages as just their title bar, so a row can sit right under a row of minified Stages.
- **Keep Stages from overlapping**: a Stage let go of on top of another moves to the nearest free spot. When resizing, an edge stops short of the next Stage.
- **Guide lines** show which edges line up, in a colour you choose.
- Hold **Alt** to place or size a Stage freely.
- **Lock** a Stage to stop it being moved, resized, dropped on or rearranged.
- A Stage left off-screen after a monitor change moves back on screen when Stages starts.

## Rolling up

- **Minify**: a Stage shrinks to its title bar until the mouse comes over it.
- **Title bar click**: one click rolls a Stage up to its title bar, and it stays that way until clicked again. It can be on for every Stage, or set Stage by Stage, and is remembered after a restart.
- **Opens downwards, upwards or automatically**: opening upwards, the title bar stays put and the Stage opens above it, like a list that drops upwards; Automatic opens upwards only when there isn't room below on the screen.
- Optional smooth roll animation (100 to 600 ms), which follows Windows' own animation setting.

## Looks

- **Backgrounds**: blur, imitation acrylic or translucent, for every Stage or per Stage.
- **Background picture** for each Stage, with fill, fit or stretch, strength, a dark shade and blur. The picture is copied into the Stage so it travels with it.
- **Theme**: dark, light, or the same as Windows, for Settings, the dialogs and all right-click menus.
- Choose the tint colour and strength, the acrylic's sheen and grain, the font, label and title sizes and colours, the title bar colour, an outline, and rounded or square corners. Rounded corners need Windows 11.
- Title bar height for every Stage, or per Stage.
- **Title position**: left, centre or right.
- **Tabs**: the open tab underlined, shaded, outlined (like Windows' tabs, with the outline running round the whole Stage) or clear, in a tab colour you choose, or automatically 5% lighter than the Stage's own colour for the outline.
- Sharp on every monitor, including mixed scaling (per-monitor DPI).

## Control

- **Tray icon** for a new Stage, hide or show all, Settings and Quit. It can be switched off for a clean notification area; Stages keeps running, and starting Stages again opens Settings.
- **Hide or show every Stage** with a keyboard shortcut you choose (Ctrl+Alt+Shift+H by default). It warns if another program already uses the combination.
- **Start with Windows** (a normal per-user startup entry).
- **Hide desktop icons** switch, for when your Stages replace them.
- **Backup**: save every Stage, tab, picture and setting into one .zip file, and import it again on this PC or another. The old data goes to the Recycle Bin, and nothing changes if anything fails.
- Settings apply straight away and save automatically.

## Reliability

- **Survives Explorer restarts.** NoFences closed when Explorer restarted; Stages re-attaches its Stages and checks every few seconds.
- **No handle leaks.** Icons are extracted once and released, so Stages doesn't slowly run out of Windows resources.
- **Safe saves.** Every settings file is written automatically with a backup copy. A damaged Stage is skipped and logged instead of stopping the others.
- Errors are caught, logged to `Logs\stages.log`, and survived.
- Slow or unplugged network drives only slow down their own Stage, never the app.
- Alt+F4 can't close a Stage by accident.

## Installing and requirements

- **Install from Settings**: run a portable copy (your own build, or a downloaded .exe) and choose Settings → Install. It copies itself into place, adds a Start menu entry and start-with-Windows (both optional), lists itself in Windows Settings → Apps with a working Uninstall, and can send the portable copy to the Recycle Bin. No administrator rights needed.
- Upgrades only replace the program and leave your Stages and settings alone.
- Uninstalling asks whether to remove your Stages too (the default is No). If you say Yes, they go to the Recycle Bin.
- Windows 10 (1903 or later, including IoT Enterprise LTSC) and Windows 11, with .NET Framework 4.8.
- No third-party libraries: everything Stages uses is part of .NET Framework.
